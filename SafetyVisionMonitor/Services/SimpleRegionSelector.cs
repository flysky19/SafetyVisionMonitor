using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using OpenCvSharp;
using Point = System.Drawing.Point;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 간단한 영역 선택 도구 (동기 방식)
    /// </summary>
    public class SimpleRegionSelector : IDisposable
    {
        private Mat _originalFrame;
        private List<Point> _currentPolygon = new();
        private List<PolygonRegion> _completedRegions = new();
        private RegionType _currentRegionType = RegionType.Interior;
        private string _windowName;
        private bool _isRunning = false;

        public SimpleRegionSelector(string windowName = "Region Selection")
        {
            _windowName = windowName;
        }

        /// <summary>
        /// 영역 선택 시작 (동기 방식)
        /// </summary>
        public List<PolygonRegion> SelectRegions(Mat frame)
        {
            _originalFrame = frame.Clone();
            _completedRegions.Clear();
            _currentPolygon.Clear();
            _isRunning = true;

            try
            {
                // OpenCV 윈도우 생성
                Cv2.NamedWindow(_windowName, WindowFlags.Normal);
                Cv2.ResizeWindow(_windowName, Math.Min(1200, _originalFrame.Width), Math.Min(800, _originalFrame.Height));
                Cv2.SetMouseCallback(_windowName, OnMouse, IntPtr.Zero);

                ShowInstructions();

                // 메인 루프
                while (_isRunning)
                {
                    // 화면 업데이트
                    var display = CreateDisplayFrame();
                    Cv2.ImShow(_windowName, display);
                    display.Dispose();

                    // 키 입력 대기
                    var key = Cv2.WaitKey(30);
                    if (key >= 0)
                    {
                        ProcessKeyInput(key);
                    }
                }

                return _completedRegions.ToList();
            }
            finally
            {
                Cv2.DestroyWindow(_windowName);
            }
        }

        /// <summary>
        /// 비동기 래퍼
        /// </summary>
        public async Task<List<PolygonRegion>> SelectRegionsAsync(Mat frame)
        {
            return await Task.Run(() => SelectRegions(frame));
        }

        /// <summary>
        /// 마우스 이벤트 처리
        /// </summary>
        private void OnMouse(MouseEventTypes eventType, int x, int y, MouseEventFlags flags, IntPtr userData)
        {
            System.Diagnostics.Debug.WriteLine($"Mouse: {eventType} at ({x}, {y})");
            
            switch (eventType)
            {
                case MouseEventTypes.LButtonDown:
                    _currentPolygon.Add(new Point(x, y));
                    System.Diagnostics.Debug.WriteLine($"Added point. Total: {_currentPolygon.Count}");
                    break;

                case MouseEventTypes.RButtonDown:
                    var count = _currentPolygon.Count;
                    _currentPolygon.Clear();
                    System.Diagnostics.Debug.WriteLine($"Cleared {count} points");
                    break;

                case MouseEventTypes.MButtonDown:
                    CompleteCurrentPolygon();
                    break;
            }
        }

        /// <summary>
        /// 키보드 입력 처리
        /// </summary>
        private void ProcessKeyInput(int key)
        {
            System.Diagnostics.Debug.WriteLine($"Key: {key} ('{(char)key}')");
            
            switch (key)
            {
                case 32: // SPACE
                    System.Diagnostics.Debug.WriteLine("SPACE - completing polygon");
                    CompleteCurrentPolygon();
                    break;

                case (int)'i':
                case (int)'I':
                case (int)'m':
                case (int)'M':
                    _currentRegionType = RegionType.Interior;
                    System.Diagnostics.Debug.WriteLine("모드: 모니터링 구역 (아크릴 내부)");
                    break;

                case (int)'e':
                case (int)'E':
                case (int)'x':
                case (int)'X':
                    _currentRegionType = RegionType.Exterior;
                    System.Diagnostics.Debug.WriteLine("모드: 제외 구역 (무시할 영역)");
                    break;

                case (int)'u':
                case (int)'U':
                    if (_completedRegions.Any())
                    {
                        _completedRegions.RemoveAt(_completedRegions.Count - 1);
                        System.Diagnostics.Debug.WriteLine("Removed last region");
                    }
                    break;

                case (int)'c':
                case (int)'C':
                    _completedRegions.Clear();
                    _currentPolygon.Clear();
                    System.Diagnostics.Debug.WriteLine("Cleared all");
                    break;

                case (int)'r':
                case (int)'R':
                    _currentPolygon.Clear();
                    System.Diagnostics.Debug.WriteLine("Reset current polygon");
                    break;

                case 13: // ENTER
                case 27: // ESC
                    System.Diagnostics.Debug.WriteLine($"Exiting - {(key == 13 ? "ENTER" : "ESC")}");
                    _isRunning = false;
                    break;
            }
        }

        /// <summary>
        /// 현재 폴리곤 완성
        /// </summary>
        private void CompleteCurrentPolygon()
        {
            if (_currentPolygon.Count >= 3)
            {
                var regionName = _currentRegionType == RegionType.Interior
                    ? $"Interior_{_completedRegions.Count(r => r.RegionType == RegionType.Interior) + 1}"
                    : $"Exterior_{_completedRegions.Count(r => r.RegionType == RegionType.Exterior) + 1}";

                var region = new PolygonRegion
                {
                    Points = _currentPolygon.ToArray(),
                    Name = regionName,
                    IsActive = true,
                    RegionType = _currentRegionType,
                    CreatedTime = DateTime.Now
                };

                _completedRegions.Add(region);
                _currentPolygon.Clear();

                System.Diagnostics.Debug.WriteLine($"Completed: {regionName} ({region.Points.Length} points)");
            }
        }

        /// <summary>
        /// 화면 그리기
        /// </summary>
        private Mat CreateDisplayFrame()
        {
            var display = _originalFrame.Clone();

            // 완성된 영역들 그리기
            foreach (var region in _completedRegions)
            {
                DrawRegion(display, region);
            }

            // 현재 그리는 중인 폴리곤
            DrawCurrentPolygon(display);

            // 상태 정보
            DrawStatusInfo(display);

            return display;
        }

        /// <summary>
        /// 영역 그리기
        /// </summary>
        private void DrawRegion(Mat image, PolygonRegion region)
        {
            if (region.Points.Length < 3) return;

            var cvPoints = region.Points.Select(p => new OpenCvSharp.Point(p.X, p.Y)).ToArray();
            var color = region.RegionType == RegionType.Interior 
                ? new Scalar(0, 255, 0) 
                : new Scalar(0, 0, 255);

            // 반투명 채우기
            var overlay = image.Clone();
            Cv2.FillPoly(overlay, new[] { cvPoints }, color);
            Cv2.AddWeighted(image, 0.7, overlay, 0.3, 0, image);

            // 경계선
            Cv2.Polylines(image, new[] { cvPoints }, true, color, 2);

            // 라벨
            if (cvPoints.Length > 0)
            {
                var prefix = region.RegionType == RegionType.Interior ? "[내부]" : "[외부]";
                Cv2.PutText(image, $"{prefix} {region.Name}", 
                          new OpenCvSharp.Point(cvPoints[0].X, cvPoints[0].Y - 10),
                          HersheyFonts.HersheySimplex, 0.6, color, 2);
            }

            overlay.Dispose();
        }

        /// <summary>
        /// 현재 폴리곤 그리기
        /// </summary>
        private void DrawCurrentPolygon(Mat image)
        {
            if (_currentPolygon.Count == 0) return;

            var color = _currentRegionType == RegionType.Interior 
                ? new Scalar(0, 255, 0) 
                : new Scalar(0, 0, 255);

            for (int i = 0; i < _currentPolygon.Count; i++)
            {
                var point = new OpenCvSharp.Point(_currentPolygon[i].X, _currentPolygon[i].Y);
                
                // 점 그리기
                Cv2.Circle(image, point, 5, color, -1);
                Cv2.Circle(image, point, 6, new Scalar(255, 255, 255), 2);

                // 선 연결
                if (i > 0)
                {
                    var prevPoint = new OpenCvSharp.Point(_currentPolygon[i - 1].X, _currentPolygon[i - 1].Y);
                    Cv2.Line(image, prevPoint, point, color, 2);
                }

                // 번호 표시
                Cv2.PutText(image, (i + 1).ToString(), 
                          new OpenCvSharp.Point(point.X + 8, point.Y - 8),
                          HersheyFonts.HersheySimplex, 0.5, new Scalar(255, 255, 255), 1);
            }

            // 닫기 선 (3개 이상일 때)
            if (_currentPolygon.Count >= 3)
            {
                var first = new OpenCvSharp.Point(_currentPolygon[0].X, _currentPolygon[0].Y);
                var last = new OpenCvSharp.Point(_currentPolygon.Last().X, _currentPolygon.Last().Y);
                Cv2.Line(image, last, first, color, 1, LineTypes.Link4);
            }
        }

        /// <summary>
        /// 상태 정보 표시
        /// </summary>
        private void DrawStatusInfo(Mat image)
        {
            var y = 30;
            var lineHeight = 25;

            // 현재 모드
            var modeText = _currentRegionType == RegionType.Interior ? "모니터링 구역" : "제외 구역";
            var modeColor = _currentRegionType == RegionType.Interior 
                ? new Scalar(0, 255, 0) 
                : new Scalar(0, 0, 255);

            Cv2.PutText(image, $"모드: {modeText}", 
                      new OpenCvSharp.Point(10, y), HersheyFonts.HersheySimplex, 
                      0.6, new Scalar(255, 255, 255), 2);
            Cv2.PutText(image, $"모드: {modeText}", 
                      new OpenCvSharp.Point(10, y), HersheyFonts.HersheySimplex, 
                      0.6, modeColor, 1);

            y += lineHeight;

            // 완성된 영역
            var monitorCount = _completedRegions.Count(r => r.RegionType == RegionType.Interior);
            var excludeCount = _completedRegions.Count(r => r.RegionType == RegionType.Exterior);
            
            Cv2.PutText(image, $"완성: 모니터링 {monitorCount}, 제외 {excludeCount}", 
                      new OpenCvSharp.Point(10, y), HersheyFonts.HersheySimplex, 
                      0.5, new Scalar(255, 255, 0), 1);

            y += lineHeight;

            // 현재 점 수
            if (_currentPolygon.Count > 0)
            {
                Cv2.PutText(image, $"현재: {_currentPolygon.Count}개 점", 
                          new OpenCvSharp.Point(10, y), HersheyFonts.HersheySimplex, 
                          0.5, new Scalar(255, 255, 255), 1);
            }
        }

        /// <summary>
        /// 사용법 출력
        /// </summary>
        private void ShowInstructions()
        {
            System.Diagnostics.Debug.WriteLine("=== 아크릴 영역 선택 도구 ===");
            System.Diagnostics.Debug.WriteLine("좌클릭: 점 추가");
            System.Diagnostics.Debug.WriteLine("우클릭: 현재 폴리곤 취소");  
            System.Diagnostics.Debug.WriteLine("SPACE: 폴리곤 완성");
            System.Diagnostics.Debug.WriteLine("M/I: 모니터링 구역 모드 (아크릴 내부 - 사람 추적)");
            System.Diagnostics.Debug.WriteLine("E/X: 제외 구역 모드 (무시할 영역 - 추적 안함)");
            System.Diagnostics.Debug.WriteLine("ESC: 완료");
            System.Diagnostics.Debug.WriteLine("==============================");
        }

        public void Dispose()
        {
            _originalFrame?.Dispose();
        }
    }
}