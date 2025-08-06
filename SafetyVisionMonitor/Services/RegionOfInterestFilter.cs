using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenCvSharp;
using SafetyVisionMonitor.Models;
using Point = System.Drawing.Point;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 아크릴 벽면 기반 ROI(Region of Interest) 필터링 서비스
    /// </summary>
    public class RegionOfInterestFilter
    {
        private List<PolygonRegion> _interiorRegions = new();
        private List<PolygonRegion> _exteriorRegions = new();
        private Mat? _maskImage;
        private Size _frameSize;
        private string _cameraId;

        public RegionOfInterestFilter(string cameraId)
        {
            _cameraId = cameraId;
        }

        /// <summary>
        /// 내부 영역(아크릴 안쪽) 추가
        /// </summary>
        public void AddInteriorRegion(System.Drawing.Point[] points, string name = "")
        {
            var region = new PolygonRegion
            {
                Points = points,
                Name = string.IsNullOrEmpty(name) ? $"Interior_{_interiorRegions.Count + 1}" : name,
                IsActive = true,
                RegionType = RegionType.Interior
            };
            _interiorRegions.Add(region);
            UpdateMaskImage();
        }

        /// <summary>
        /// 외부 영역(아크릴 바깥쪽, 제외할 영역) 추가
        /// </summary>
        public void AddExteriorRegion(System.Drawing.Point[] points, string name = "")
        {
            var region = new PolygonRegion
            {
                Points = points,
                Name = string.IsNullOrEmpty(name) ? $"Exterior_{_exteriorRegions.Count + 1}" : name,
                IsActive = true,
                RegionType = RegionType.Exterior
            };
            _exteriorRegions.Add(region);
            UpdateMaskImage();
        }

        /// <summary>
        /// 프레임 크기 설정
        /// </summary>
        public void SetFrameSize(Size frameSize)
        {
            _frameSize = frameSize;
            UpdateMaskImage();
        }

        /// <summary>
        /// 검출 결과 필터링 (아크릴 내부에 있는 사람만)
        /// </summary>
        public List<DetectionResult> FilterDetections(List<DetectionResult> detections)
        {
            if (!_interiorRegions.Any(r => r.IsActive))
                return detections; // 설정된 내부 영역이 없으면 모든 검출 결과 반환

            var filtered = new List<DetectionResult>();

            foreach (var detection in detections)
            {
                // 바운딩 박스 중심점 계산
                var centerX = detection.BoundingBox.X + detection.BoundingBox.Width / 2;
                var centerY = detection.BoundingBox.Y + detection.BoundingBox.Height / 2;
                var center = new System.Drawing.Point((int)centerX, (int)centerY);

                // 내부 영역에 있고, 외부 영역에 없는지 확인
                if (IsPointInInteriorRegion(center) && !IsPointInExteriorRegion(center))
                {
                    filtered.Add(detection);
                }
            }

            return filtered;
        }

        /// <summary>
        /// 점이 내부 영역에 있는지 확인
        /// </summary>
        private bool IsPointInInteriorRegion(System.Drawing.Point point)
        {
            foreach (var region in _interiorRegions.Where(r => r.IsActive))
            {
                if (IsPointInPolygon(point, region.Points))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 점이 외부 영역(제외할 영역)에 있는지 확인
        /// </summary>
        private bool IsPointInExteriorRegion(System.Drawing.Point point)
        {
            foreach (var region in _exteriorRegions.Where(r => r.IsActive))
            {
                if (IsPointInPolygon(point, region.Points))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 점이 폴리곤 내부에 있는지 확인 (Ray casting algorithm)
        /// </summary>
        private bool IsPointInPolygon(System.Drawing.Point point, System.Drawing.Point[] polygon)
        {
            if (polygon.Length < 3) return false;

            bool inside = false;
            int j = polygon.Length - 1;

            for (int i = 0; i < polygon.Length; i++)
            {
                if (((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)) &&
                    (point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / 
                               (polygon[j].Y - polygon[i].Y) + polygon[i].X))
                {
                    inside = !inside;
                }
                j = i;
            }

            return inside;
        }

        /// <summary>
        /// 마스크 이미지 업데이트 (성능 최적화용)
        /// </summary>
        private void UpdateMaskImage()
        {
            if (_frameSize.IsEmpty) return;

            _maskImage?.Dispose();
            _maskImage = new Mat(_frameSize.Height, _frameSize.Width, MatType.CV_8UC1, new Scalar(0));

            // 내부 영역을 흰색으로 채우기
            foreach (var region in _interiorRegions.Where(r => r.IsActive))
            {
                var cvPoints = region.Points.Select(p => new OpenCvSharp.Point(p.X, p.Y)).ToArray();
                Cv2.FillPoly(_maskImage, new[] { cvPoints }, new Scalar(255));
            }

            // 외부 영역을 검은색으로 제거
            foreach (var region in _exteriorRegions.Where(r => r.IsActive))
            {
                var cvPoints = region.Points.Select(p => new OpenCvSharp.Point(p.X, p.Y)).ToArray();
                Cv2.FillPoly(_maskImage, new[] { cvPoints }, new Scalar(0));
            }
        }

        /// <summary>
        /// 영역 시각화 (디버깅 및 설정 확인용)
        /// </summary>
        public Mat VisualizeRegions(Mat frame)
        {
            var visualization = frame.Clone();

            // 내부 영역 (초록색)
            foreach (var region in _interiorRegions.Where(r => r.IsActive))
            {
                var cvPoints = region.Points.Select(p => new OpenCvSharp.Point(p.X, p.Y)).ToArray();
                
                // 반투명 채우기
                var overlay = frame.Clone();
                Cv2.FillPoly(overlay, new[] { cvPoints }, new Scalar(0, 255, 0));
                Cv2.AddWeighted(visualization, 0.7, overlay, 0.3, 0, visualization);
                
                // 경계선
                Cv2.Polylines(visualization, new[] { cvPoints }, true, new Scalar(0, 255, 0), 3);
                
                // 라벨
                if (cvPoints.Length > 0)
                {
                    var labelPos = cvPoints[0];
                    Cv2.PutText(visualization, $"[IN] {region.Name}", labelPos,
                              HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 0), 2);
                }
                
                overlay.Dispose();
            }

            // 외부 영역 (빨간색)
            foreach (var region in _exteriorRegions.Where(r => r.IsActive))
            {
                var cvPoints = region.Points.Select(p => new OpenCvSharp.Point(p.X, p.Y)).ToArray();
                
                // 반투명 채우기
                var overlay = frame.Clone();
                Cv2.FillPoly(overlay, new[] { cvPoints }, new Scalar(0, 0, 255));
                Cv2.AddWeighted(visualization, 0.7, overlay, 0.3, 0, visualization);
                
                // 경계선
                Cv2.Polylines(visualization, new[] { cvPoints }, true, new Scalar(0, 0, 255), 3);
                
                // 라벨
                if (cvPoints.Length > 0)
                {
                    var labelPos = cvPoints[0];
                    Cv2.PutText(visualization, $"[OUT] {region.Name}", labelPos,
                              HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 0, 255), 2);
                }
                
                overlay.Dispose();
            }

            return visualization;
        }

        /// <summary>
        /// 영역 설정을 JSON 파일로 저장
        /// </summary>
        public void SaveToFile(string filePath)
        {
            var data = new RegionData
            {
                CameraId = _cameraId,
                InteriorRegions = _interiorRegions.ToList(),
                ExteriorRegions = _exteriorRegions.ToList(),
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

            System.Diagnostics.Debug.WriteLine($"ROI regions saved to {filePath}");
        }

        /// <summary>
        /// JSON 파일에서 영역 설정 로드
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

                var data = JsonSerializer.Deserialize<RegionData>(json, options);
                if (data == null) return;

                _interiorRegions = data.InteriorRegions.ToList();
                _exteriorRegions = data.ExteriorRegions.ToList();

                if (data.FrameSize != null)
                {
                    _frameSize = new Size(data.FrameSize.Width, data.FrameSize.Height);
                }

                UpdateMaskImage();
                System.Diagnostics.Debug.WriteLine($"ROI regions loaded from {filePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load ROI regions: {ex.Message}");
            }
        }

        /// <summary>
        /// 모든 영역 초기화
        /// </summary>
        public void ClearAllRegions()
        {
            _interiorRegions.Clear();
            _exteriorRegions.Clear();
            UpdateMaskImage();
        }

        /// <summary>
        /// 영역 활성화/비활성화
        /// </summary>
        public void SetRegionActive(string name, bool isActive)
        {
            var region = _interiorRegions.FirstOrDefault(r => r.Name == name) ??
                        _exteriorRegions.FirstOrDefault(r => r.Name == name);
            
            if (region != null)
            {
                region.IsActive = isActive;
                UpdateMaskImage();
            }
        }

        /// <summary>
        /// 설정된 영역 개수
        /// </summary>
        public int InteriorRegionCount => _interiorRegions.Count(r => r.IsActive);
        public int ExteriorRegionCount => _exteriorRegions.Count(r => r.IsActive);

        public void Dispose()
        {
            _maskImage?.Dispose();
        }
    }

    /// <summary>
    /// 폴리곤 영역 정보
    /// </summary>
    public class PolygonRegion
    {
        public System.Drawing.Point[] Points { get; set; } = Array.Empty<System.Drawing.Point>();
        public string Name { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public RegionType RegionType { get; set; }
        public DateTime CreatedTime { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 영역 타입
    /// </summary>
    public enum RegionType
    {
        Interior, // 내부 (포함할 영역)
        Exterior  // 외부 (제외할 영역)
    }

    /// <summary>
    /// 영역 설정 데이터
    /// </summary>
    public class RegionData
    {
        public string CameraId { get; set; } = "";
        public List<PolygonRegion> InteriorRegions { get; set; } = new();
        public List<PolygonRegion> ExteriorRegions { get; set; } = new();
        public DateTime Timestamp { get; set; }
        public dynamic? FrameSize { get; set; }
    }
}