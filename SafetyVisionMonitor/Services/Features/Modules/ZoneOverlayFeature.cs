using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using SafetyVisionMonitor.Shared.Models;
using SafetyVisionMonitor.Shared.Database;
using SafetyVisionMonitor.Services;

namespace SafetyVisionMonitor.Services.Features
{
    /// <summary>
    /// 구역 오버레이 기능 (위험구역, 경고구역 표시)
    /// </summary>
    public class ZoneOverlayFeature : BaseFeature
    {
        public override string Id => "zone_overlay";
        public override string Name => "구역 표시";
        public override string Description => "안전 구역과 위험 구역을 시각적으로 표시합니다";
        public override int RenderPriority => 50; // 개인정보 보호 다음, 객체 검출보다 먼저

        private bool _showWarningZones = true;
        private bool _showDangerZones = true;
        private bool _showZoneLabels = true;
        private double _zoneOpacity = 0.3;
        private readonly KoreanTextRenderer _koreanTextRenderer = new();
        
        // Zone3D 캐시 (무한 생성 방지)
        private readonly Dictionary<string, Zone3D> _zoneCache = new();
        private readonly object _cacheLock = new();

        public override FeatureConfiguration DefaultConfiguration => new()
        {
            IsEnabled = true,
            Properties = new Dictionary<string, object>
            {
                ["showWarningZones"] = true,
                ["showDangerZones"] = true,
                ["showZoneLabels"] = true,
                ["zoneOpacity"] = 0.3,
                ["warningZoneColor"] = "#FFFF00", // 노란색
                ["dangerZoneColor"] = "#FF0000",  // 빨간색
                ["zoneBorderThickness"] = 2
            }
        };

        protected override void OnConfigurationChanged(FeatureConfiguration configuration)
        {
            _showWarningZones = configuration.GetProperty("showWarningZones", true);
            _showDangerZones = configuration.GetProperty("showDangerZones", true);
            _showZoneLabels = configuration.GetProperty("showZoneLabels", true);
            _zoneOpacity = configuration.GetProperty("zoneOpacity", 0.3);

            System.Diagnostics.Debug.WriteLine(
                $"ZoneOverlayFeature: Configuration updated - IsEnabled: {IsEnabled}, Warning: {_showWarningZones}, Danger: {_showDangerZones}, Opacity: {_zoneOpacity}");
        }

        public override Mat ProcessFrame(Mat frame, FrameProcessingContext context)
        {
            System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: ProcessFrame called - IsEnabled: {IsEnabled}, Frame: {frame?.Width}x{frame?.Height}");
            
            if (!IsEnabled || frame == null || frame.Empty())
            {
                System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Skipped - IsEnabled: {IsEnabled}, Frame null: {frame == null}");
                return frame;
            }

            try
            {
                // App.AppData에서 구역 정보 가져오기
                var zones = GetZonesForCamera(context.CameraId);
                var zoneCount = zones.Count();
                
                System.Diagnostics.Debug.WriteLine(
                    $"ZoneOverlayFeature: Camera {context.CameraId} - Found {zoneCount} zones, ShowWarning: {_showWarningZones}, ShowDanger: {_showDangerZones}");
                
                if (zoneCount == 0)
                    return frame;

                System.Diagnostics.Debug.WriteLine(
                    $"ZoneOverlayFeature: Rendering {zoneCount} zones for camera {context.CameraId}");

                foreach (var zone in zones)
                {
                    RenderZone(frame, zone, context.Scale);
                }

                return frame;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Error processing frame: {ex.Message}");
                return frame;
            }
        }

        public override bool ShouldProcess(FrameProcessingContext context)
        {
            return IsEnabled && (_showWarningZones || _showDangerZones);
        }

        private void RenderZone(Mat frame, Zone3D zone, float scale)
        {
            try
            {
                // 이미 GetZonesForCamera에서 필터링 됨
                var color = GetZoneColor(zone.Type);
                var thickness = CurrentConfiguration?.GetProperty("zoneBorderThickness", 2) ?? 2;
                
                System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Rendering zone '{zone.Name}' - Type: {zone.Type}, Color: {color}");

                // 2D 평면 투영된 구역 경계 그리기 (FloorPoints 사용)
                if (zone.FloorPoints != null && zone.FloorPoints.Count > 2)
                {
                    RenderZoneBoundary(frame, zone, color, scale, thickness);
                }

                // 구역 라벨 표시
                if (_showZoneLabels)
                {
                    RenderZoneLabel(frame, zone, color, scale);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Render zone error: {ex.Message}");
            }
        }

        private void RenderZoneBoundary(Mat frame, Zone3D zone, Scalar color, float scale, int thickness)
        {
            try
            {
                // FloorPoints(월드 좌표)를 현재 프레임의 화면 좌표로 변환
                var currentFrameWidth = zone.CalibrationFrameWidth * scale;
                var currentFrameHeight = zone.CalibrationFrameHeight * scale;
                
                var projectedPoints = zone.FloorPoints!
                    .Select(point => 
                    {
                        Point screenPoint;
                        
                        if (zone.UseRelativeCoordinates)
                        {
                            // 상대 좌표 (0.0~1.0)를 현재 프레임 좌표로 직접 변환 (반올림 적용)
                            screenPoint = new Point(
                                (int)Math.Round(point.X * currentFrameWidth),
                                (int)Math.Round(point.Y * currentFrameHeight)
                            );
                            
                            System.Diagnostics.Debug.WriteLine($"Relative coordinate conversion: ({point.X:F4},{point.Y:F4}) -> Screen({screenPoint.X},{screenPoint.Y}) [Frame: {currentFrameWidth:F0}x{currentFrameHeight:F0}]");
                        }
                        else
                        {
                            // 기존 월드 좌표 시스템 (호환성 유지)
                            // 월드 좌표를 상대 좌표로 변환 (임시 해결책)
                            var frameCenter = new Point2D(zone.CalibrationFrameWidth / 2.0, zone.CalibrationFrameHeight / 2.0);
                            var pixelX = frameCenter.X + (point.X * zone.CalibrationPixelsPerMeter);
                            var pixelY = frameCenter.Y + (point.Y * zone.CalibrationPixelsPerMeter);
                            
                            // 픽셀 좌표를 상대 좌표로 변환
                            var relativeX = pixelX / zone.CalibrationFrameWidth;
                            var relativeY = pixelY / zone.CalibrationFrameHeight;
                            
                            // 상대 좌표를 현재 프레임으로 변환
                            screenPoint = new Point(
                                (int)(relativeX * currentFrameWidth),
                                (int)(relativeY * currentFrameHeight)
                            );
                            
                            System.Diagnostics.Debug.WriteLine($"Zone coordinate conversion: World({point.X:F2},{point.Y:F2}) -> Pixel({pixelX:F0},{pixelY:F0}) -> Relative({relativeX:F3},{relativeY:F3}) -> Screen({screenPoint.X},{screenPoint.Y})");
                        }
                        
                        // 화면 경계 내로 클램핑 (안전장치)
                        var clampedX = Math.Max(0, Math.Min(currentFrameWidth - 1, screenPoint.X));
                        var clampedY = Math.Max(0, Math.Min(currentFrameHeight - 1, screenPoint.Y));
                        
                        return new Point((int)clampedX, (int)clampedY);
                    })
                    .ToArray();

                if (projectedPoints.Length < 3) return;
                
                System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Rendering boundary with {projectedPoints.Length} points");
                System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Frame scale {scale:F2}, Current frame size: {currentFrameWidth:F0}x{currentFrameHeight:F0}");
                System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: CoordSystem: {(zone.UseRelativeCoordinates ? "Relative" : "World")}, First point ({zone.FloorPoints[0].X:F3}, {zone.FloorPoints[0].Y:F3}) -> Screen({projectedPoints[0].X}, {projectedPoints[0].Y})");
                
                // 모든 좌표 출력 및 폴리곤 크기 계산
                var minX = projectedPoints.Min(p => p.X);
                var maxX = projectedPoints.Max(p => p.X);
                var minY = projectedPoints.Min(p => p.Y);
                var maxY = projectedPoints.Max(p => p.Y);
                var polyWidth = maxX - minX;
                var polyHeight = maxY - minY;
                
                System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Polygon bounds - X:({minX}-{maxX}), Y:({minY}-{maxY}), Size:({polyWidth}x{polyHeight})");
                
                for (int i = 0; i < projectedPoints.Length; i++)
                {
                    System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Point[{i}] = ({projectedPoints[i].X}, {projectedPoints[i].Y})");
                }
                
                // 폴리곤이 너무 작으면 경고 메시지 출력
                if (polyWidth < 10 || polyHeight < 10)
                {
                    System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: WARNING - Polygon too small! Size: {polyWidth}x{polyHeight}");
                    
                    // 너무 작은 폴리곤은 중심점 기준으로 최소 크기로 확대
                    var centerX = (minX + maxX) / 2;
                    var centerY = (minY + maxY) / 2;
                    var minSize = 30; // 최소 30픽셀
                    
                    projectedPoints[0] = new Point(centerX - minSize/2, centerY - minSize/2);
                    projectedPoints[1] = new Point(centerX + minSize/2, centerY - minSize/2);
                    projectedPoints[2] = new Point(centerX + minSize/2, centerY + minSize/2);
                    projectedPoints[3] = new Point(centerX - minSize/2, centerY + minSize/2);
                    
                    System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Polygon expanded to minimum size around ({centerX}, {centerY})");
                }

                // 1. 반투명 채우기 (ZoneSetupView 스타일과 일치)
                var fillPoints = new Point[1][];
                fillPoints[0] = projectedPoints;
                
                // 반투명 오버레이 생성 (ZoneSetupView처럼)
                using var overlay = Mat.Zeros(frame.Size(), frame.Type());
                Cv2.FillPoly(overlay, fillPoints, color);
                
                // 오버레이 블렌딩 (투명도 적용)
                Cv2.AddWeighted(frame, 1.0 - _zoneOpacity, overlay, _zoneOpacity, 0, frame);
                System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Fill with overlay blending for zone '{zone.Name}' with opacity {_zoneOpacity}");

                // 2. 경계선 그리기 (더 얇고 깔끔하게)
                var borderThickness = 2; // ZoneSetupView와 비슷한 두께
                Cv2.Polylines(frame, new[] { projectedPoints }, true, color, borderThickness);
                System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Border drawn for zone '{zone.Name}' with thickness {borderThickness}");
                


                // 꼭짓점 표시 (선택사항)
                if (CurrentConfiguration?.GetProperty("showZoneVertices", false) == true)
                {
                    foreach (var point in projectedPoints)
                    {
                        Cv2.Circle(frame, point, 4, color, -1);
                        Cv2.Circle(frame, point, 4, new Scalar(255, 255, 255), 1);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Render boundary error: {ex.Message}");
            }
        }

        private void RenderZoneLabel(Mat frame, Zone3D zone, Scalar color, float scale)
        {
            try
            {
                // 구역 중심점 계산 (FloorPoints 사용)
                if (zone.FloorPoints == null || !zone.FloorPoints.Any())
                    return;

                // Zone 중심점 계산
                var centerX_coord = zone.FloorPoints.Average(p => p.X);
                var centerY_coord = zone.FloorPoints.Average(p => p.Y);
                
                var currentFrameWidth = zone.CalibrationFrameWidth * scale;
                var currentFrameHeight = zone.CalibrationFrameHeight * scale;
                
                int centerX, centerY;
                
                if (zone.UseRelativeCoordinates)
                {
                    // 상대 좌표 중심점을 화면 좌표로 변환
                    centerX = (int)(centerX_coord * currentFrameWidth);
                    centerY = (int)(centerY_coord * currentFrameHeight);
                }
                else
                {
                    // 월드 좌표 중심점을 상대 좌표로 변환 후 화면 좌표로
                    var frameCenter = new Point2D(zone.CalibrationFrameWidth / 2.0, zone.CalibrationFrameHeight / 2.0);
                    var pixelX = frameCenter.X + (centerX_coord * zone.CalibrationPixelsPerMeter);
                    var pixelY = frameCenter.Y + (centerY_coord * zone.CalibrationPixelsPerMeter);
                    
                    var relativeX = pixelX / zone.CalibrationFrameWidth;
                    var relativeY = pixelY / zone.CalibrationFrameHeight;
                    
                    centerX = (int)(relativeX * currentFrameWidth);
                    centerY = (int)(relativeY * currentFrameHeight);
                }
                
                // 화면 경계 내로 클램핑 (라벨 표시 위해 여백 확보)
                centerX = Math.Max(50, Math.Min((int)currentFrameWidth - 50, centerX));
                centerY = Math.Max(20, Math.Min((int)currentFrameHeight - 20, centerY));
                
                // 라벨 텍스트 생성
                var labelText = BuildZoneLabel(zone);
                var textScale = CurrentConfiguration?.GetProperty("zoneLabelScale", 0.8) ?? 0.8;

                // 텍스트 크기 계산 (한글 지원)
                var textSize = _koreanTextRenderer.GetTextSize(labelText, textScale);
                
                // 텍스트 위치 조정
                var textPos = new Point(
                    centerX - textSize.Width / 2,
                    centerY + textSize.Height / 2
                );

                // 한글 텍스트 렌더링 (ZoneSetupView 스타일과 일치)
                bool showBackground = CurrentConfiguration?.GetProperty("showLabelBackground", true) ?? true;
                var textColor = new Scalar(255, 255, 255); // 흰색 텍스트
                var backgroundColor = new Scalar(0, 0, 0); // 검은색 배경
                
                _koreanTextRenderer.PutText(frame, labelText, textPos, textScale, 
                           textColor, 2, showBackground, backgroundColor);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Render label error: {ex.Message}");
            }
        }

        private string BuildZoneLabel(Zone3D zone)
        {
            var parts = new List<string>();

            // 구역 이름
            if (!string.IsNullOrEmpty(zone.Name))
            {
                parts.Add(zone.Name);
            }

            // 구역 타입
            var typeText = zone.Type switch
            {
                ZoneType.Warning => "경고구역",
                ZoneType.Danger => "위험구역",
                _ => "구역"
            };
            parts.Add(typeText);

            // 현재 상태 (선택사항)
            if (CurrentConfiguration?.GetProperty("showZoneStatus", false) == true)
            {
                // TODO: 구역 내 사람 수, 위반 상태 등 추가 가능
                if (!zone.IsEnabled)
                {
                    parts.Add("(비활성)");
                }
            }

            return string.Join("\n", parts);
        }

        private Scalar GetZoneColor(ZoneType zoneType)
        {
            return zoneType switch
            {
                ZoneType.Warning => ParseColorFromConfig("warningZoneColor", new Scalar(0, 255, 255)), // 노란색 (BGR)
                ZoneType.Danger => ParseColorFromConfig("dangerZoneColor", new Scalar(0, 0, 255)),     // 빨간색 (BGR)
                _ => new Scalar(128, 128, 128) // 회색 (기본값)
            };
        }

        private Scalar ParseColorFromConfig(string configKey, Scalar defaultColor)
        {
            try
            {
                var colorHex = CurrentConfiguration?.GetProperty<string>(configKey, "");
                if (!string.IsNullOrEmpty(colorHex))
                {
                    var color = System.Drawing.ColorTranslator.FromHtml(colorHex);
                    return new Scalar(color.B, color.G, color.R); // BGR 순서
                }
            }
            catch
            {
                // 파싱 실패 시 기본 색상 사용
            }
            return defaultColor;
        }

        private IEnumerable<Zone3D> GetZonesForCamera(string cameraId)
        {
            try
            {
                // App.AppData에서 해당 카메라의 구역들 가져오기
                var allZones = App.AppData?.Zones;
                System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: GetZonesForCamera called for '{cameraId}'");
                System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Total zones in AppData: {allZones?.Count ?? 0}, AppData null: {App.AppData == null}");
                
                // 카메라별 구역 필터링 (IsEnabled는 표시 여부가 아닌 구역 자체의 활성화 상태)
                var cameraZones = allZones?.Where(z => z.CameraId == cameraId) ?? Enumerable.Empty<Zone3DConfig>();
                var cameraZoneCount = cameraZones.Count();
                
                // 모든 Zone의 카메라 할당 상태 출력 (디버깅용)
                if (allZones != null)
                {
                    foreach (var zone in allZones)
                    {
                        System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Zone '{zone.Name}' -> Camera '{zone.CameraId}' (Requesting: '{cameraId}')");
                    }
                }
                
                System.Diagnostics.Debug.WriteLine(
                    $"ZoneOverlayFeature: Camera {cameraId} - Total zones: {cameraZoneCount}, ShowWarning: {_showWarningZones}, ShowDanger: {_showDangerZones}");
                
                // Zone3DConfig를 Zone3D로 변환하고, 표시 설정에 따라 필터링
                var convertedZones = cameraZones.Select(config => ConvertToZone3D(config));
                
                // 타입별 표시 설정에 따라 필터링
                var filteredZones = convertedZones.Where(zone => 
                    (zone.Type == ZoneType.Warning && _showWarningZones) ||
                    (zone.Type == ZoneType.Danger && _showDangerZones)
                );
                
                var filteredCount = filteredZones.Count();
                System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: After filtering - {filteredCount} zones to render");
                
                return filteredZones;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Get zones error: {ex.Message}");
                return Enumerable.Empty<Zone3D>();
            }
        }

        private Zone3D ConvertToZone3D(Zone3DConfig config)
        {
            lock (_cacheLock)
            {
                // 캐시에서 기존 Zone3D 확인
                if (_zoneCache.TryGetValue(config.ZoneId, out var existingZone))
                {
                    // 기존 객체 업데이트 (새로 생성하지 않음)
                    existingZone.IsLoading = true; // 이벤트 억제
                    try
                    {
                        existingZone.Name = config.Name;
                        existingZone.CameraId = config.CameraId;
                        existingZone.IsEnabled = config.IsEnabled;
                        existingZone.Opacity = config.Opacity;
                        existingZone.CalibrationPixelsPerMeter = config.CalibrationPixelsPerMeter;
                        existingZone.CalibrationFrameWidth = config.CalibrationFrameWidth;
                        existingZone.CalibrationFrameHeight = config.CalibrationFrameHeight;
                        
                        // Type 업데이트
                        if (Enum.TryParse<ZoneType>(config.Type, out var zoneType1))
                        {
                            existingZone.Type = zoneType1;
                        }
                        
                        // VerticesJson 업데이트
                        if (!string.IsNullOrEmpty(config.VerticesJson))
                        {
                            try
                            {
                                using var doc = System.Text.Json.JsonDocument.Parse(config.VerticesJson);
                                var root = doc.RootElement;
                                
                                // Height 값 읽기
                                if (root.TryGetProperty("Height", out var heightElement))
                                {
                                    existingZone.Height = heightElement.GetDouble();
                                }
                                
                                // FloorPoints 배열 읽기
                                if (root.TryGetProperty("FloorPoints", out var pointsElement))
                                {
                                    var points = new List<Point2D>();
                                    foreach (var pointElement in pointsElement.EnumerateArray())
                                    {
                                        if (pointElement.TryGetProperty("X", out var xElement) &&
                                            pointElement.TryGetProperty("Y", out var yElement))
                                        {
                                            points.Add(new Point2D(xElement.GetDouble(), yElement.GetDouble()));
                                        }
                                    }
                                    
                                    if (points.Count > 0)
                                    {
                                        existingZone.FloorPoints = points;
                                        
                                        // 좌표 타입 자동 판단 (기존 캐시된 구역에도 적용)
                                        var allPointsInRelativeRange = points.All(p => 
                                            p.X >= 0.0 && p.X <= 1.0 && p.Y >= 0.0 && p.Y <= 1.0);
                                        
                                        existingZone.UseRelativeCoordinates = allPointsInRelativeRange;
                                        System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Existing zone '{existingZone.Name}' - Auto-detected coordinates: {(allPointsInRelativeRange ? "RELATIVE" : "WORLD")}");
                                    }
                                }
                            }
                            catch
                            {
                                // JSON 파싱 실패 시 무시
                            }
                        }
                    }
                    finally
                    {
                        existingZone.IsLoading = false; // 이벤트 억제 해제
                    }
                    
                    return existingZone;
                }
                
                // 새 Zone3D 생성 (캐시에 없는 경우만)
                var zone = new Zone3D
                {
                    IsLoading = true, // 생성 시 이벤트 억제
                    Id = config.ZoneId,
                    Name = config.Name,
                    CameraId = config.CameraId,
                    IsEnabled = config.IsEnabled,
                    CreatedDate = config.CreatedTime,
                    Opacity = config.Opacity,
                    CalibrationPixelsPerMeter = config.CalibrationPixelsPerMeter,
                    CalibrationFrameWidth = config.CalibrationFrameWidth,
                    CalibrationFrameHeight = config.CalibrationFrameHeight,
                    UseRelativeCoordinates = false // 일단 false로 설정, 아래에서 자동 판단
                };
                
                // 캐시에 추가
                _zoneCache[config.ZoneId] = zone;

                // Type 문자열을 ZoneType 열거형으로 변환
                if (Enum.TryParse<ZoneType>(config.Type, out var zoneType))
                {
                    zone.Type = zoneType;
                }

                // Color 문자열을 Color로 변환
                try
                {
                    if (!string.IsNullOrEmpty(config.Color))
                    {
                        var color = System.Windows.Media.ColorConverter.ConvertFromString(config.Color);
                        if (color != null)
                        {
                            zone.DisplayColor = (System.Windows.Media.Color)color;
                        }
                    }
                }
                catch
                {
                    // 색상 변환 실패 시 기본 색상 유지
                }

                // VerticesJson에서 FloorPoints 복원
                try
                {
                    if (!string.IsNullOrEmpty(config.VerticesJson))
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(config.VerticesJson);
                        var root = doc.RootElement;
                        
                        // Height 값 읽기
                        if (root.TryGetProperty("Height", out var heightElement))
                        {
                            zone.Height = heightElement.GetDouble();
                        }
                        
                        // FloorPoints 배열 읽기
                        if (root.TryGetProperty("FloorPoints", out var pointsElement))
                        {
                            var points = new List<Point2D>();
                            foreach (var pointElement in pointsElement.EnumerateArray())
                            {
                                if (pointElement.TryGetProperty("X", out var xElement) &&
                                    pointElement.TryGetProperty("Y", out var yElement))
                                {
                                    points.Add(new Point2D(xElement.GetDouble(), yElement.GetDouble()));
                                }
                            }
                            
                            if (points.Count > 0)
                            {
                                zone.FloorPoints = points;
                                
                                // 좌표 타입 자동 판단 (상대 좌표는 보통 0.0~1.0 범위)
                                var allPointsInRelativeRange = points.All(p => 
                                    p.X >= 0.0 && p.X <= 1.0 && p.Y >= 0.0 && p.Y <= 1.0);
                                
                                if (allPointsInRelativeRange)
                                {
                                    zone.UseRelativeCoordinates = true;
                                    System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Zone '{zone.Name}' - Auto-detected as RELATIVE coordinates");
                                }
                                else
                                {
                                    zone.UseRelativeCoordinates = false;
                                    System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Zone '{zone.Name}' - Auto-detected as WORLD coordinates");
                                }
                                
                                System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Zone '{zone.Name}' - Loaded {points.Count} floor points, UseRelativeCoordinates: {zone.UseRelativeCoordinates}");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Zone '{zone.Name}' - No valid points in FloorPoints array");
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Zone '{zone.Name}' - No FloorPoints property in JSON");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Zone '{zone.Name}' - VerticesJson is empty");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Zone '{zone.Name}' - JSON parsing error: {ex.Message}");
                    // JSON 파싱 실패 시 빈 리스트 유지
                    zone.FloorPoints = new List<Point2D>();
                }

                zone.IsLoading = false; // 설정 완료 후 이벤트 활성화
                return zone;
            }
        }
        

        public override FeatureStatus GetStatus()
        {
            var status = base.GetStatus();
            status.Metrics["showWarningZones"] = _showWarningZones;
            status.Metrics["showDangerZones"] = _showDangerZones;
            status.Metrics["zoneOpacity"] = _zoneOpacity;
            
            // 카메라별 구역 수 통계
            try
            {
                var cameras = App.AppData?.Cameras?.Select(c => c.Id) ?? Enumerable.Empty<string>();
                foreach (var cameraId in cameras)
                {
                    var zoneCount = GetZonesForCamera(cameraId).Count();
                    if (zoneCount > 0)
                    {
                        status.Metrics[$"zones_{cameraId}"] = zoneCount;
                    }
                }
            }
            catch { }

            return status;
        }

        public override void Dispose()
        {
            _koreanTextRenderer?.Dispose();
            
            // 캐시 정리
            lock (_cacheLock)
            {
                _zoneCache.Clear();
            }
            
            base.Dispose();
        }
    }
}