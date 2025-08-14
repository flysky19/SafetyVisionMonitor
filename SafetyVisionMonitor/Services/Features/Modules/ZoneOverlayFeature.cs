using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using SafetyVisionMonitor.Shared.Models;
using SafetyVisionMonitor.Shared.Database;

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
                $"ZoneOverlayFeature: Configuration updated - Warning: {_showWarningZones}, Danger: {_showDangerZones}, Opacity: {_zoneOpacity}");
        }

        public override Mat ProcessFrame(Mat frame, FrameProcessingContext context)
        {
            if (!IsEnabled || frame == null || frame.Empty())
                return frame;

            try
            {
                // App.AppData에서 구역 정보 가져오기
                var zones = GetZonesForCamera(context.CameraId);
                if (!zones.Any())
                    return frame;

                System.Diagnostics.Debug.WriteLine(
                    $"ZoneOverlayFeature: Rendering {zones.Count()} zones for camera {context.CameraId}");

                // 오버레이 생성 (반투명 효과용)
                using var overlay = frame.Clone();

                foreach (var zone in zones)
                {
                    RenderZone(frame, overlay, zone, context.Scale);
                }

                // 반투명 오버레이 적용
                if (_zoneOpacity > 0 && _zoneOpacity < 1.0)
                {
                    Cv2.AddWeighted(frame, 1.0 - _zoneOpacity, overlay, _zoneOpacity, 0, frame);
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

        private void RenderZone(Mat frame, Mat overlay, Zone3D zone, float scale)
        {
            try
            {
                // 구역 타입에 따른 필터링
                if (zone.Type == ZoneType.Warning && !_showWarningZones)
                    return;
                if (zone.Type == ZoneType.Danger && !_showDangerZones)
                    return;

                var color = GetZoneColor(zone.Type);
                var thickness = CurrentConfiguration?.GetProperty("zoneBorderThickness", 2) ?? 2;

                // 2D 평면 투영된 구역 경계 그리기 (FloorPoints 사용)
                if (zone.FloorPoints != null && zone.FloorPoints.Count > 2)
                {
                    RenderZoneBoundary(frame, overlay, zone, color, scale, thickness);
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

        private void RenderZoneBoundary(Mat frame, Mat overlay, Zone3D zone, Scalar color, float scale, int thickness)
        {
            try
            {
                // FloorPoints를 OpenCV Point로 변환 (스케일 적용)
                var projectedPoints = zone.FloorPoints!
                    .Select(p => new Point((int)(p.X * scale), (int)(p.Y * scale)))
                    .ToArray();

                if (projectedPoints.Length < 3) return;

                // 폴리곤 채우기 (오버레이용)
                var fillPoints = new Point[1][];
                fillPoints[0] = projectedPoints;
                Cv2.FillPoly(overlay, fillPoints, color);

                // 경계선 그리기
                Cv2.Polylines(frame, new[] { projectedPoints }, true, color, thickness);

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

                var centerX = (int)(zone.FloorPoints.Average(p => p.X) * scale);
                var centerY = (int)(zone.FloorPoints.Average(p => p.Y) * scale);
                
                // 라벨 텍스트 생성
                var labelText = BuildZoneLabel(zone);
                var textScale = CurrentConfiguration?.GetProperty("zoneLabelScale", 0.8) ?? 0.8;

                // 텍스트 크기 계산
                var textSize = Cv2.GetTextSize(labelText, HersheyFonts.HersheySimplex, textScale, 2, out var baseline);
                
                // 텍스트 위치 조정
                var textPos = new Point(
                    centerX - textSize.Width / 2,
                    centerY + textSize.Height / 2
                );

                // 배경 그리기
                if (CurrentConfiguration?.GetProperty("showLabelBackground", true) == true)
                {
                    var bgRect = new Rect(
                        textPos.X - 4, 
                        textPos.Y - textSize.Height - 4,
                        textSize.Width + 8, 
                        textSize.Height + 8
                    );
                    
                    Cv2.Rectangle(frame, bgRect, new Scalar(0, 0, 0), -1);
                    Cv2.Rectangle(frame, bgRect, color, 1);
                }

                // 텍스트 그리기
                Cv2.PutText(frame, labelText, textPos, HersheyFonts.HersheySimplex, 
                           textScale, new Scalar(255, 255, 255), 2);
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
                // App.AppData에서 해당 카메라의 구역들 가져오기 (활성화된 구역만)
                var zones = App.AppData?.Zones?.Where(z => z.CameraId == cameraId && z.IsEnabled) ?? Enumerable.Empty<Zone3DConfig>();
                
                // Zone3DConfig를 Zone3D로 변환
                return zones.Select(config => ConvertToZone3D(config));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ZoneOverlayFeature: Get zones error: {ex.Message}");
                return Enumerable.Empty<Zone3D>();
            }
        }

        private Zone3D ConvertToZone3D(Zone3DConfig config)
        {
            var zone = new Zone3D
            {
                Id = config.ZoneId,
                Name = config.Name,
                CameraId = config.CameraId,
                IsEnabled = config.IsEnabled,
                CreatedDate = config.CreatedTime,
                Opacity = config.Opacity,
                CalibrationPixelsPerMeter = config.CalibrationPixelsPerMeter,
                CalibrationFrameWidth = config.CalibrationFrameWidth,
                CalibrationFrameHeight = config.CalibrationFrameHeight
            };

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
                    var points = System.Text.Json.JsonSerializer.Deserialize<List<Point2D>>(config.VerticesJson);
                    if (points != null)
                    {
                        zone.FloorPoints = points;
                    }
                }
            }
            catch
            {
                // JSON 파싱 실패 시 빈 리스트 유지
                zone.FloorPoints = new List<Point2D>();
            }

            return zone;
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
    }
}