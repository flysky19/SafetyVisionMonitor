using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using Point = System.Drawing.Point;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 인터랙티브 영역 선택 도구
    /// </summary>
    public class RegionSelector
    {
        private Mat _originalFrame;
        private List<Point> _currentPolygon = new();
        private List<PolygonRegion> _completedRegions = new();
        private bool _isDrawing = false;
        private RegionType _currentRegionType = RegionType.Interior;
        private string _windowName;
        private TaskCompletionSource<List<PolygonRegion>>? _completionSource;

        public RegionSelector(string windowName = "Region Selection")
        {
            _windowName = windowName;
        }

        /// <summary>
        /// 영역 선택 시작
        /// </summary>
        public async Task<List<PolygonRegion>> SelectRegionsAsync(Mat frame)
        {
            _originalFrame = frame.Clone();
            _completionSource = new TaskCompletionSource<List<PolygonRegion>>();
            _completedRegions.Clear();
            _currentPolygon.Clear();

            try
            {
                // OpenCV 윈도우 생성 및 마우스 콜백 설정
                Cv2.NamedWindow(_windowName, WindowFlags.Normal);
                Cv2.ResizeWindow(_windowName, Math.Min(1200, _originalFrame.Width), Math.Min(800, _originalFrame.Height));
                Cv2.SetMouseCallback(_windowName, OnMouse, IntPtr.Zero);

                // 초기 화면 표시
                ShowInstructions();
                
                var initialDisplay = CreateDisplayFrame();
                Cv2.ImShow(_windowName, initialDisplay);
                initialDisplay.Dispose();

                System.Diagnostics.Debug.WriteLine($"RegionSelector: Window '{_windowName}' created. Use mouse and keyboard...");

                // 직접적인 이벤트 루프 (UI 스레드에서)
                return await RunEventLoop();
            }
            finally
            {
                Cv2.DestroyWindow(_windowName);
                _originalFrame?.Dispose();
            }
        }

        /// <summary>
        /// 이벤트 루프 실행
        /// </summary>
        private async Task<List<PolygonRegion>> RunEventLoop()
        {
            return await Task.Run(() =>
            {
                while (!_completionSource.Task.IsCompleted)
                {
                    // 키 입력 확인 및 화면 업데이트
                    var key = Cv2.WaitKey(50); // 50ms 대기
                    
                    // 화면 업데이트
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            var display = CreateDisplayFrame();
                            Cv2.ImShow(_windowName, display);
                            display.Dispose();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Display update error: {ex.Message}");
                        }
                    });

                    // 키 입력 처리
                    if (key >= 0 && key != 255)
                    {
                        System.Diagnostics.Debug.WriteLine($"Key pressed: {key} ('{(char)key}')");
                        ProcessKeyInput(key);
                        
                        if (key == 27) // ESC
                        {
                            System.Diagnostics.Debug.WriteLine("ESC pressed - exiting");
                            break;
                        }
                    }

                    // 윈도우가 닫혔는지 확인
                    try
                    {
                        var windowProperty = Cv2.GetWindowProperty(_windowName, WindowPropertyFlags.Visible);
                        if (windowProperty <= 0)
                        {
                            System.Diagnostics.Debug.WriteLine("Window was closed");
                            break;
                        }
                    }
                    catch (Exception)
                    {
                        // 무시
                    }
                }

                if (!_completionSource.Task.IsCompleted)
                {
                    _completionSource.TrySetResult(_completedRegions);
                }
                
                return _completedRegions;
            });
        }

        /// <summary>
        /// 마우스 이벤트 처리
        /// </summary>
        private void OnMouse(MouseEventTypes eventType, int x, int y, MouseEventFlags flags, IntPtr userData)
        {
            System.Diagnostics.Debug.WriteLine($"Mouse event: {eventType} at ({x}, {y})");
            
            switch (eventType)
            {
                case MouseEventTypes.LButtonDown:
                    _currentPolygon.Add(new Point(x, y));
                    System.Diagnostics.Debug.WriteLine($"Added point ({x}, {y}). Total points: {_currentPolygon.Count}");
                    break;

                case MouseEventTypes.RButtonDown:
                    // 우클릭으로 현재 폴리곤 취소
                    var prevCount = _currentPolygon.Count;
                    _currentPolygon.Clear();
                    System.Diagnostics.Debug.WriteLine($"Cleared current polygon (had {prevCount} points)");
                    break;

                case MouseEventTypes.MButtonDown:
                    // 중간 클릭으로 현재 폴리곤 완성
                    System.Diagnostics.Debug.WriteLine("Middle click - completing current polygon");
                    CompleteCurrentPolygon();
                    break;
            }
        }

        /// <summary>
        /// 키보드 입력 처리
        /// </summary>
        private void ProcessKeyInput(int key)
        {
            System.Diagnostics.Debug.WriteLine($"Processing key: {key} ('{(char)key}')");
            
            switch (key)
            {
                case 32: // SPACE - 현재 폴리곤 완성
                    System.Diagnostics.Debug.WriteLine("SPACE pressed - completing current polygon");
                    CompleteCurrentPolygon();
                    break;

                case (int)'i':
                case (int)'I': // I - 내부 영역 모드
                    _currentRegionType = RegionType.Interior;
                    System.Diagnostics.Debug.WriteLine("Switched to Interior region mode");
                    break;

                case (int)'o':
                case (int)'O': // O - 외부 영역 모드
                    _currentRegionType = RegionType.Exterior;
                    System.Diagnostics.Debug.WriteLine("Switched to Exterior region mode");
                    break;

                case (int)'u':
                case (int)'U': // U - 마지막 폴리곤 취소
                    if (_completedRegions.Any())
                    {
                        var removed = _completedRegions.Last();
                        _completedRegions.RemoveAt(_completedRegions.Count - 1);
                        System.Diagnostics.Debug.WriteLine($"Removed last region: {removed.Name}");
                    }
                    break;

                case (int)'c':
                case (int)'C': // C - 모든 영역 지우기
                    var clearedCount = _completedRegions.Count;
                    var currentPoints = _currentPolygon.Count;
                    _completedRegions.Clear();
                    _currentPolygon.Clear();
                    System.Diagnostics.Debug.WriteLine($"Cleared all regions ({clearedCount}) and current polygon ({currentPoints} points)");
                    break;

                case (int)'r':
                case (int)'R': // R - 현재 그리기 취소
                    var pointCount = _currentPolygon.Count;
                    _currentPolygon.Clear();
                    System.Diagnostics.Debug.WriteLine($"Reset current drawing ({pointCount} points cleared)");
                    break;

                case 13: // ENTER - 완료
                case 27: // ESC - 완료
                    System.Diagnostics.Debug.WriteLine($"Exit key pressed: {(key == 13 ? "ENTER" : "ESC")}");
                    _completionSource?.TrySetResult(_completedRegions);
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

                System.Diagnostics.Debug.WriteLine($"Region completed: {regionName} with {region.Points.Length} points");
            }
        }

        /// <summary>
        /// 화면 표시용 프레임 생성
        /// </summary>
        private Mat CreateDisplayFrame()
        {
            var display = _originalFrame.Clone();

            // 완성된 영역들 그리기
            foreach (var region in _completedRegions)
            {
                DrawRegion(display, region);
            }

            // 현재 그리는 중인 폴리곤 그리기
            DrawCurrentPolygon(display);

            // 상태 정보 표시
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
                ? new Scalar(0, 255, 0, 100)  // 초록색 (내부)
                : new Scalar(0, 0, 255, 100); // 빨간색 (외부)

            // 반투명 채우기
            var overlay = image.Clone();
            Cv2.FillPoly(overlay, new[] { cvPoints }, color);
            Cv2.AddWeighted(image, 0.7, overlay, 0.3, 0, image);

            // 경계선
            var lineColor = region.RegionType == RegionType.Interior 
                ? new Scalar(0, 255, 0) 
                : new Scalar(0, 0, 255);
            Cv2.Polylines(image, new[] { cvPoints }, true, lineColor, 2);

            // 라벨
            if (cvPoints.Length > 0)
            {
                var prefix = region.RegionType == RegionType.Interior ? "[내부]" : "[외부]";
                var labelText = $"{prefix} {region.Name}";
                var labelPos = new OpenCvSharp.Point(cvPoints[0].X, cvPoints[0].Y - 10);
                
                Cv2.PutText(image, labelText, labelPos, HersheyFonts.HersheySimplex, 
                          0.6, new Scalar(255, 255, 255), 2);
                Cv2.PutText(image, labelText, labelPos, HersheyFonts.HersheySimplex, 
                          0.6, lineColor, 1);
            }

            overlay.Dispose();
        }

        /// <summary>
        /// 현재 그리는 중인 폴리곤 그리기
        /// </summary>
        private void DrawCurrentPolygon(Mat image)
        {
            if (_currentPolygon.Count == 0) return;

            var color = _currentRegionType == RegionType.Interior 
                ? new Scalar(0, 255, 0) 
                : new Scalar(0, 0, 255);

            // 점들 그리기
            for (int i = 0; i < _currentPolygon.Count; i++)
            {
                var cvPoint = new OpenCvSharp.Point(_currentPolygon[i].X, _currentPolygon[i].Y);
                
                // 현재 점
                Cv2.Circle(image, cvPoint, 5, color, -1);
                Cv2.Circle(image, cvPoint, 6, new Scalar(255, 255, 255), 2);

                // 선 연결
                if (i > 0)
                {
                    var prevPoint = new OpenCvSharp.Point(_currentPolygon[i - 1].X, _currentPolygon[i - 1].Y);
                    Cv2.Line(image, prevPoint, cvPoint, color, 2);
                }

                // 점 번호 표시
                Cv2.PutText(image, (i + 1).ToString(), 
                          new OpenCvSharp.Point(cvPoint.X + 8, cvPoint.Y - 8),
                          HersheyFonts.HersheySimplex, 0.5, 
                          new Scalar(255, 255, 255), 1);
            }

            // 첫 번째와 마지막 점 연결 (3개 이상일 때)
            if (_currentPolygon.Count >= 3)
            {
                var firstPoint = new OpenCvSharp.Point(_currentPolygon[0].X, _currentPolygon[0].Y);
                var lastPoint = new OpenCvSharp.Point(_currentPolygon.Last().X, _currentPolygon.Last().Y);
                Cv2.Line(image, lastPoint, firstPoint, color, 1, LineTypes.Link4);
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
            var modeText = _currentRegionType == RegionType.Interior ? "내부 영역 모드" : "외부 영역 모드";
            var modeColor = _currentRegionType == RegionType.Interior 
                ? new Scalar(0, 255, 0) 
                : new Scalar(0, 0, 255);

            Cv2.PutText(image, $"현재: {modeText}", 
                      new OpenCvSharp.Point(10, y), HersheyFonts.HersheySimplex, 
                      0.6, new Scalar(255, 255, 255), 2);
            Cv2.PutText(image, $"현재: {modeText}", 
                      new OpenCvSharp.Point(10, y), HersheyFonts.HersheySimplex, 
                      0.6, modeColor, 1);

            y += lineHeight;

            // 완성된 영역 개수
            var interiorCount = _completedRegions.Count(r => r.RegionType == RegionType.Interior);
            var exteriorCount = _completedRegions.Count(r => r.RegionType == RegionType.Exterior);
            
            Cv2.PutText(image, $"완성된 영역: 내부 {interiorCount}개, 외부 {exteriorCount}개", 
                      new OpenCvSharp.Point(10, y), HersheyFonts.HersheySimplex, 
                      0.5, new Scalar(255, 255, 0), 1);

            y += lineHeight;

            // 현재 폴리곤 점 개수
            if (_currentPolygon.Count > 0)
            {
                Cv2.PutText(image, $"현재 폴리곤: {_currentPolygon.Count}개 점", 
                          new OpenCvSharp.Point(10, y), HersheyFonts.HersheySimplex, 
                          0.5, new Scalar(255, 255, 255), 1);
            }
        }

        /// <summary>
        /// 사용법 표시
        /// </summary>
        private void ShowInstructions()
        {
            System.Diagnostics.Debug.WriteLine("=== 아크릴 벽면 영역 선택 도구 ===");
            System.Diagnostics.Debug.WriteLine("마우스 조작:");
            System.Diagnostics.Debug.WriteLine("  - 좌클릭: 점 추가");
            System.Diagnostics.Debug.WriteLine("  - 우클릭: 현재 폴리곤 취소");
            System.Diagnostics.Debug.WriteLine("  - 중간클릭: 현재 폴리곤 완성");
            System.Diagnostics.Debug.WriteLine("");
            System.Diagnostics.Debug.WriteLine("키보드 조작:");
            System.Diagnostics.Debug.WriteLine("  - SPACE: 현재 폴리곤 완성");
            System.Diagnostics.Debug.WriteLine("  - I: 내부 영역 모드");
            System.Diagnostics.Debug.WriteLine("  - O: 외부 영역 모드");
            System.Diagnostics.Debug.WriteLine("  - U: 마지막 폴리곤 취소");
            System.Diagnostics.Debug.WriteLine("  - C: 모든 영역 지우기");
            System.Diagnostics.Debug.WriteLine("  - R: 현재 그리기 취소");
            System.Diagnostics.Debug.WriteLine("  - ENTER/ESC: 완료");
            System.Diagnostics.Debug.WriteLine("=====================================");
        }

        public void Dispose()
        {
            _originalFrame?.Dispose();
        }
    }
}