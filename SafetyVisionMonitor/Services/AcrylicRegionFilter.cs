using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenCvSharp;
using SafetyVisionMonitor.Models;
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
        private string _cameraId;
        private Mat? _boundaryMask;

        public AcrylicRegionFilter(string cameraId)
        {
            _cameraId = cameraId;
        }

        /// <summary>
        /// 아크릴 경계선 설정
        /// </summary>
        public void SetAcrylicBoundary(Point[] boundary)
        {
            _acrylicBoundary = boundary?.ToArray() ?? Array.Empty<Point>();
            UpdateBoundaryMask();
            System.Diagnostics.Debug.WriteLine($"AcrylicRegionFilter: Set boundary with {_acrylicBoundary.Length} points for camera {_cameraId}");
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
                    // 사람 객체의 위치 판단
                    var center = new Point((int)detection.Center.X, (int)detection.Center.Y);
                    var isInside = IsPointInsideAcrylicRegion(center);
                    
                    detection.Location = isInside ? PersonLocation.Interior : PersonLocation.Exterior;

                    // 추적 모드에 따라 필터링
                    if (ShouldTrackPerson(detection.Location))
                    {
                        filteredDetections.Add(detection);
                    }
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
            return detection.ClassName?.ToLower()?.Contains("person") == true;
        }

        /// <summary>
        /// 점이 아크릴 영역 내부에 있는지 확인
        /// </summary>
        private bool IsPointInsideAcrylicRegion(Point point)
        {
            if (_acrylicBoundary.Length < 3) return false;

            // Ray casting algorithm
            bool inside = false;
            int j = _acrylicBoundary.Length - 1;

            for (int i = 0; i < _acrylicBoundary.Length; i++)
            {
                if (((_acrylicBoundary[i].Y > point.Y) != (_acrylicBoundary[j].Y > point.Y)) &&
                    (point.X < (_acrylicBoundary[j].X - _acrylicBoundary[i].X) * (point.Y - _acrylicBoundary[i].Y) / 
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
        /// 아크릴 경계선 시각화
        /// </summary>
        public Mat VisualizeAcrylicBoundary(Mat frame)
        {
            if (_acrylicBoundary.Length < 3)
                return frame;

            var visualization = frame.Clone();
            var cvPoints = _acrylicBoundary.Select(p => new OpenCvSharp.Point(p.X, p.Y)).ToArray();

            // 반투명 내부 영역 표시
            var overlay = frame.Clone();
            Cv2.FillPoly(overlay, new[] { cvPoints }, new Scalar(255, 255, 0, 100)); // 노란색
            Cv2.AddWeighted(visualization, 0.8, overlay, 0.2, 0, visualization);

            // 경계선 (점선 효과)
            for (int i = 0; i < cvPoints.Length; i++)
            {
                var start = cvPoints[i];
                var end = cvPoints[(i + 1) % cvPoints.Length];
                
                // 점선 그리기
                DrawDashedLine(visualization, start, end, new Scalar(0, 255, 255), 3); // 노란색 점선
            }

            // 경계점 표시
            foreach (var point in cvPoints)
            {
                Cv2.Circle(visualization, point, 8, new Scalar(0, 255, 255), -1); // 노란색 원
                Cv2.Circle(visualization, point, 8, new Scalar(0, 0, 0), 2); // 검은색 테두리
            }

            // 라벨 표시
            if (cvPoints.Length > 0)
            {
                var labelPos = cvPoints[0];
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
                FrameSize = new { Width = _frameSize.Width, Height = _frameSize.Height }
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
                    _frameSize = new Size(data.FrameSize.Width, data.FrameSize.Height);
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