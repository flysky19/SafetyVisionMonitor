using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenCvSharp;
using SafetyVisionMonitor.Shared.Models;
using Point = System.Drawing.Point;
using Size = OpenCvSharp.Size;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 아크릴 영역 기반 필터링 서비스 (단일 경계선으로 내부/외부 자동 판단)
    /// </summary>
    public class AcrylicRegionFilter : IDisposable
    {
        private Point[] _acrylicBoundary = Array.Empty<Point>();
        private TrackingMode _trackingMode = TrackingMode.Both;
        private Size _frameSize;
        private Size _originalFrameSize; // JSON 파일에서 로드한 경계선 기준 해상도
        private string _cameraId;
        private Mat? _boundaryMask;

        public AcrylicRegionFilter(string cameraId)
        {
            _cameraId = cameraId;
            // 기본값으로 초기화 (파일에서 로드하면 덮어씀)
            _originalFrameSize = new Size(1920, 1080);
        }

        /// <summary>
        /// 아크릴 경계선 설정
        /// </summary>
        public void SetAcrylicBoundary(Point[] boundary)
        {
            _acrylicBoundary = boundary?.ToArray() ?? Array.Empty<Point>();
            // 새로 경계선을 설정할 때는 현재 프레임 크기를 원본 기준으로 설정
            _originalFrameSize = _frameSize;
            UpdateBoundaryMask();
            System.Diagnostics.Debug.WriteLine($"AcrylicRegionFilter: Set boundary with {_acrylicBoundary.Length} points for camera {_cameraId}, originalFrameSize: {_originalFrameSize.Width}x{_originalFrameSize.Height}");
        }

        /// <summary>
        /// 추적 모드 설정
        /// </summary>
        public void SetTrackingMode(TrackingMode mode)
        {
            _trackingMode = mode;
            System.Diagnostics.Debug.WriteLine($"AcrylicRegionFilter: Tracking mode set to {mode} for camera {_cameraId}");
        }

        /// <summary>
        /// 프레임 크기 설정
        /// </summary>
        public void SetFrameSize(Size frameSize)
        {
            _frameSize = frameSize;
            UpdateBoundaryMask();
        }

        /// <summary>
        /// 검출 결과 필터링 및 위치 판단
        /// </summary>
        public List<DetectionResult> FilterDetections(List<DetectionResult> detections)
        {
            if (_acrylicBoundary.Length < 3)
            {
                // 아크릴 경계가 설정되지 않았으면 모든 검출 결과를 Unknown으로 반환
                foreach (var detection in detections)
                {
                    if (IsPersonDetection(detection))
                    {
                        detection.Location = PersonLocation.Unknown;
                    }
                }
                return detections;
            }

            var filteredDetections = new List<DetectionResult>();

            foreach (var detection in detections)
            {
                if (IsPersonDetection(detection))
                {
                    // detection 좌표를 원본 프레임 크기로 변환하여 boundary와 비교
                    // (boundary는 원본 해상도 기준으로 저장되어 있음)
                    var center = new Point((int)detection.Center.X, (int)detection.Center.Y);
                    var isInside = IsPointInsideAcrylicRegion(center);
                    
                    detection.Location = isInside ? PersonLocation.Interior : PersonLocation.Exterior;

                    // 추적 모드에 따라 필터링
                    if (ShouldTrackPerson(detection.Location))
                    {
                        filteredDetections.Add(detection);
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"AcrylicRegionFilter: Person at ({center.X}, {center.Y}) - {(isInside ? "Interior" : "Exterior")}, Mode: {_trackingMode}, ShouldTrack: {ShouldTrackPerson(detection.Location)}, BoundaryPoints: {_acrylicBoundary.Length}");
                }
                else
                {
                    // 사람이 아닌 객체는 그대로 포함 (위치 판단 안함)
                    filteredDetections.Add(detection);
                }
            }

            return filteredDetections;
        }

        /// <summary>
        /// 사람 검출인지 확인
        /// </summary>
        private bool IsPersonDetection(DetectionResult detection)
        {
            return detection.Label == "person";
        }

        /// <summary>
        /// 점이 아크릴 영역 내부에 있는지 확인 (현재 프레임 크기 기준)
        /// </summary>
        private bool IsPointInsideAcrylicRegion(Point point)
        {
            if (_acrylicBoundary.Length < 3) return false;

            // detection 좌표를 boundary 좌표 시스템에 맞게 변환
            // boundary는 _originalFrameSize 기준으로 저장되어 있고
            // detection 좌표는 _frameSize 기준으로 들어옴
            var currentWidth = _frameSize.Width > 0 ? _frameSize.Width : 1920;
            var currentHeight = _frameSize.Height > 0 ? _frameSize.Height : 1080;
            var originalWidth = _originalFrameSize.Width > 0 ? _originalFrameSize.Width : 1920;
            var originalHeight = _originalFrameSize.Height > 0 ? _originalFrameSize.Height : 1080;
            
            // detection 좌표를 boundary 좌표계로 변환
            var scaleX = (double)originalWidth / currentWidth;
            var scaleY = (double)originalHeight / currentHeight;
            var adjustedPoint = new Point(
                (int)(point.X * scaleX),
                (int)(point.Y * scaleY)
            );
            
            System.Diagnostics.Debug.WriteLine($"AcrylicRegionFilter: Coordinate transformation for camera {_cameraId} - Original: ({point.X}, {point.Y}) -> Adjusted: ({adjustedPoint.X}, {adjustedPoint.Y}), Scale: {scaleX:F3}x{scaleY:F3}, CurrentFrame: {currentWidth}x{currentHeight}, OriginalFrame: {originalWidth}x{originalHeight}");

            // Ray casting algorithm (원본 boundary 좌표와 비교)
            bool inside = false;
            int j = _acrylicBoundary.Length - 1;

            for (int i = 0; i < _acrylicBoundary.Length; i++)
            {
                if (((_acrylicBoundary[i].Y > adjustedPoint.Y) != (_acrylicBoundary[j].Y > adjustedPoint.Y)) &&
                    (adjustedPoint.X < (_acrylicBoundary[j].X - _acrylicBoundary[i].X) * (adjustedPoint.Y - _acrylicBoundary[i].Y) / 
                               (_acrylicBoundary[j].Y - _acrylicBoundary[i].Y) + _acrylicBoundary[i].X))
                {
                    inside = !inside;
                }
                j = i;
            }

            return inside;
        }

        /// <summary>
        /// 추적 모드에 따라 사람을 추적할지 결정
        /// </summary>
        private bool ShouldTrackPerson(PersonLocation location)
        {
            return _trackingMode switch
            {
                TrackingMode.InteriorOnly => location == PersonLocation.Interior,
                TrackingMode.ExteriorOnly => location == PersonLocation.Exterior,
                TrackingMode.Both => true,
                TrackingMode.InteriorAlert => location == PersonLocation.Interior,
                _ => true
            };
        }

        /// <summary>
        /// 경계선 마스크 이미지 업데이트
        /// </summary>
        private void UpdateBoundaryMask()
        {
            if (_frameSize.Width <= 0 || _frameSize.Height <= 0 || _acrylicBoundary.Length < 3)
                return;

            _boundaryMask?.Dispose();
            _boundaryMask = new Mat(_frameSize.Height, _frameSize.Width, MatType.CV_8UC1, new Scalar(0));

            var cvPoints = _acrylicBoundary.Select(p => new OpenCvSharp.Point(p.X, p.Y)).ToArray();
            Cv2.FillPoly(_boundaryMask, new[] { cvPoints }, new Scalar(255));
        }

        /// <summary>
        /// 아크릴 경계선 시각화 (현재 프레임 크기에 맞게 스케일링)
        /// </summary>
        public Mat VisualizeAcrylicBoundary(Mat frame)
        {
            if (_acrylicBoundary.Length < 3)
            {
                System.Diagnostics.Debug.WriteLine($"AcrylicRegionFilter: No boundary to visualize for camera {_cameraId} (boundary points: {_acrylicBoundary.Length})");
                return frame;
            }

            var visualization = frame.Clone();
            
            // 현재 프레임 크기와 JSON 파일에 저장된 원본 경계선 기준 크기 간의 스케일 계산
            var currentWidth = frame.Width;
            var currentHeight = frame.Height;
            
            // JSON 파일에서 로드한 원본 frameSize를 사용 (경계선 좌표의 기준 해상도)
            var originalWidth = _originalFrameSize.Width > 0 ? _originalFrameSize.Width : currentWidth;
            var originalHeight = _originalFrameSize.Height > 0 ? _originalFrameSize.Height : currentHeight;
            
            var scaleX = (double)currentWidth / originalWidth;
            var scaleY = (double)currentHeight / originalHeight;
            
            System.Diagnostics.Debug.WriteLine($"AcrylicRegionFilter: Scaling boundary for camera {_cameraId} - OriginalFromFile: {originalWidth}x{originalHeight}, Current: {currentWidth}x{currentHeight}, Scale: {scaleX:F3}x{scaleY:F3}");

            // 경계 좌표를 현재 프레임 크기에 맞게 스케일링
            var scaledPoints = _acrylicBoundary.Select(p => new OpenCvSharp.Point(
                (int)(p.X * scaleX), 
                (int)(p.Y * scaleY)
            )).ToArray();

            // 반투명 내부 영역 표시
            var overlay = frame.Clone();
            Cv2.FillPoly(overlay, new[] { scaledPoints }, new Scalar(255, 255, 0, 100)); // 노란색
            Cv2.AddWeighted(visualization, 0.8, overlay, 0.2, 0, visualization);

            // 경계선 (점선 효과)
            for (int i = 0; i < scaledPoints.Length; i++)
            {
                var start = scaledPoints[i];
                var end = scaledPoints[(i + 1) % scaledPoints.Length];
                
                // 점선 그리기
                DrawDashedLine(visualization, start, end, new Scalar(0, 255, 255), 3); // 노란색 점선
            }

            // 경계점 표시
            foreach (var point in scaledPoints)
            {
                Cv2.Circle(visualization, point, 8, new Scalar(0, 255, 255), -1); // 노란색 원
                Cv2.Circle(visualization, point, 8, new Scalar(0, 0, 0), 2); // 검은색 테두리
            }

            // 라벨 표시
            if (scaledPoints.Length > 0)
            {
                var labelPos = scaledPoints[0];
                Cv2.PutText(visualization, "아크릴 경계", 
                          new OpenCvSharp.Point(labelPos.X, labelPos.Y - 15),
                          HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 255), 2);
            }

            overlay.Dispose();
            return visualization;
        }

        /// <summary>
        /// 점선 그리기
        /// </summary>
        private void DrawDashedLine(Mat image, OpenCvSharp.Point start, OpenCvSharp.Point end, Scalar color, int thickness)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            var dashLength = 10;
            var gapLength = 5;
            var totalLength = dashLength + gapLength;

            var steps = (int)(distance / totalLength);
            if (steps == 0) return;

            for (int i = 0; i < steps; i++)
            {
                var t1 = (double)(i * totalLength) / distance;
                var t2 = (double)(i * totalLength + dashLength) / distance;

                if (t2 > 1.0) t2 = 1.0;

                var dashStart = new OpenCvSharp.Point(
                    (int)(start.X + dx * t1),
                    (int)(start.Y + dy * t1)
                );
                var dashEnd = new OpenCvSharp.Point(
                    (int)(start.X + dx * t2),
                    (int)(start.Y + dy * t2)
                );

                Cv2.Line(image, dashStart, dashEnd, color, thickness);
            }
        }

        /// <summary>
        /// 아크릴 경계선을 JSON 파일로 저장
        /// </summary>
        public void SaveToFile(string filePath)
        {
            var data = new AcrylicBoundaryData
            {
                CameraId = _cameraId,
                AcrylicBoundary = _acrylicBoundary.ToArray(),
                TrackingMode = _trackingMode,
                Timestamp = DateTime.Now,
                FrameSize = new { Width = _originalFrameSize.Width, Height = _originalFrameSize.Height }
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(data, options);
            File.WriteAllText(filePath, json);

            System.Diagnostics.Debug.WriteLine($"Acrylic boundary saved to {filePath}");
        }

        /// <summary>
        /// JSON 파일에서 아크릴 경계선 로드
        /// </summary>
        public void LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                return;

            try
            {
                var json = File.ReadAllText(filePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var data = JsonSerializer.Deserialize<AcrylicBoundaryData>(json, options);
                if (data == null) return;

                _acrylicBoundary = data.AcrylicBoundary.ToArray();
                _trackingMode = data.TrackingMode;

                if (data.FrameSize != null)
                {
                    _originalFrameSize = new Size(data.FrameSize.Width, data.FrameSize.Height);
                    System.Diagnostics.Debug.WriteLine($"AcrylicRegionFilter: Loaded originalFrameSize from file: {_originalFrameSize.Width}x{_originalFrameSize.Height}");
                }
                else
                {
                    // FrameSize 정보가 없으면 기본값 유지
                    System.Diagnostics.Debug.WriteLine($"AcrylicRegionFilter: No frameSize in file, using default: {_originalFrameSize.Width}x{_originalFrameSize.Height}");
                }

                UpdateBoundaryMask();
                System.Diagnostics.Debug.WriteLine($"Acrylic boundary loaded from {filePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load acrylic boundary: {ex.Message}");
            }
        }

        /// <summary>
        /// 경계선 초기화
        /// </summary>
        public void ClearBoundary()
        {
            _acrylicBoundary = Array.Empty<Point>();
            UpdateBoundaryMask();
        }

        /// <summary>
        /// 통계 정보 조회
        /// </summary>
        public AcrylicRegionStats GetStats(List<DetectionResult> detections)
        {
            var personDetections = detections.Where(IsPersonDetection).ToList();
            
            return new AcrylicRegionStats
            {
                TotalPersons = personDetections.Count,
                InteriorPersons = personDetections.Count(d => d.Location == PersonLocation.Interior),
                ExteriorPersons = personDetections.Count(d => d.Location == PersonLocation.Exterior),
                UnknownPersons = personDetections.Count(d => d.Location == PersonLocation.Unknown),
                HasAcrylicBoundary = _acrylicBoundary.Length >= 3,
                TrackingMode = _trackingMode
            };
        }

        public void Dispose()
        {
            _boundaryMask?.Dispose();
        }
    }

    /// <summary>
    /// 아크릴 경계선 데이터
    /// </summary>
    public class AcrylicBoundaryData
    {
        public string CameraId { get; set; } = "";
        public Point[] AcrylicBoundary { get; set; } = Array.Empty<Point>();
        public TrackingMode TrackingMode { get; set; } = TrackingMode.Both;
        public DateTime Timestamp { get; set; }
        public dynamic? FrameSize { get; set; }
    }

    /// <summary>
    /// 아크릴 영역 통계 정보
    /// </summary>
    public class AcrylicRegionStats
    {
        public int TotalPersons { get; set; }
        public int InteriorPersons { get; set; }
        public int ExteriorPersons { get; set; }
        public int UnknownPersons { get; set; }
        public bool HasAcrylicBoundary { get; set; }
        public TrackingMode TrackingMode { get; set; }
    }
}