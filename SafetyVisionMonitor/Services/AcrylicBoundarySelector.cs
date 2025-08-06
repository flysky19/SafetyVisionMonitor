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
    /// 아크릴 경계선 선택 도구 (단일 경계선)
    /// </summary>
    public class AcrylicBoundarySelector : IDisposable
    {
        private Mat _originalFrame;
        private List<Point> _boundaryPoints = new();
        private string _windowName;
        private bool _isRunning = false;
        private bool _isCompleted = false;

        public AcrylicBoundarySelector(string windowName = "아크릴 경계선 설정")
        {
            _windowName = windowName;
        }

        /// <summary>
        /// 아크릴 경계선 선택 시작 (동기 방식)
        /// </summary>
        public Point[] SelectBoundary(Mat frame)
        {
            _originalFrame = frame.Clone();
            _boundaryPoints.Clear();
            _isRunning = true;
            _isCompleted = false;

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

                return _isCompleted ? _boundaryPoints.ToArray() : Array.Empty<Point>();
            }
            finally
            {
                Cv2.DestroyWindow(_windowName);
            }
        }

        /// <summary>
        /// 비동기 래퍼
        /// </summary>
        public async Task<Point[]> SelectBoundaryAsync(Mat frame)
        {
            return await Task.Run(() => SelectBoundary(frame));
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
                    _boundaryPoints.Add(new Point(x, y));
                    System.Diagnostics.Debug.WriteLine($"경계점 추가. 총 개수: {_boundaryPoints.Count}");
                    break;

                case MouseEventTypes.RButtonDown:
                    var count = _boundaryPoints.Count;
                    _boundaryPoints.Clear();
                    System.Diagnostics.Debug.WriteLine($"경계점 초기화 ({count}개 삭제)");
                    break;

                case MouseEventTypes.MButtonDown:
                    CompleteBoundary();
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
                    System.Diagnostics.Debug.WriteLine("SPACE - 경계선 완성");
                    CompleteBoundary();
                    break;

                case (int)'c':
                case (int)'C':
                    _boundaryPoints.Clear();
                    System.Diagnostics.Debug.WriteLine("경계점 모두 삭제");
                    break;

                case (int)'u':
                case (int)'U':
                    if (_boundaryPoints.Any())
                    {
                        var removed = _boundaryPoints.Last();
                        _boundaryPoints.RemoveAt(_boundaryPoints.Count - 1);
                        System.Diagnostics.Debug.WriteLine($"마지막 점 제거: ({removed.X}, {removed.Y})");
                    }
                    break;

                case 13: // ENTER
                    System.Diagnostics.Debug.WriteLine("ENTER - 경계선 완성 및 종료");
                    CompleteBoundary();
                    break;

                case 27: // ESC
                    System.Diagnostics.Debug.WriteLine("ESC - 취소 및 종료");
                    _isRunning = false;
                    break;
            }
        }

        /// <summary>
        /// 경계선 완성
        /// </summary>
        private void CompleteBoundary()
        {
            if (_boundaryPoints.Count >= 3)
            {
                _isCompleted = true;
                _isRunning = false;
                System.Diagnostics.Debug.WriteLine($"아크릴 경계선 완성: {_boundaryPoints.Count}개 점");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"경계선 완성하려면 최소 3개 점이 필요합니다 (현재: {_boundaryPoints.Count}개)");
            }
        }

        /// <summary>
        /// 화면 그리기
        /// </summary>
        private Mat CreateDisplayFrame()
        {
            var display = _originalFrame.Clone();

            // 아크릴 경계선 그리기
            DrawBoundary(display);

            // 상태 정보 표시
            DrawStatusInfo(display);

            return display;
        }

        /// <summary>
        /// 아크릴 경계선 그리기
        /// </summary>
        private void DrawBoundary(Mat image)
        {
            if (_boundaryPoints.Count == 0) return;

            var color = new Scalar(0, 255, 255); // 노란색

            // 점들과 연결선 그리기
            for (int i = 0; i < _boundaryPoints.Count; i++)
            {
                var point = new OpenCvSharp.Point(_boundaryPoints[i].X, _boundaryPoints[i].Y);
                
                // 점 그리기
                Cv2.Circle(image, point, 8, color, -1);
                Cv2.Circle(image, point, 8, new Scalar(0, 0, 0), 2);

                // 선 연결
                if (i > 0)
                {
                    var prevPoint = new OpenCvSharp.Point(_boundaryPoints[i - 1].X, _boundaryPoints[i - 1].Y);
                    DrawDashedLine(image, prevPoint, point, color, 3);
                }

                // 점 번호 표시
                Cv2.PutText(image, (i + 1).ToString(), 
                          new OpenCvSharp.Point(point.X + 12, point.Y - 8),
                          HersheyFonts.HersheySimplex, 0.6, new Scalar(255, 255, 255), 2);
                Cv2.PutText(image, (i + 1).ToString(), 
                          new OpenCvSharp.Point(point.X + 12, point.Y - 8),
                          HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 0, 0), 1);
            }

            // 닫기 선 (3개 이상일 때)
            if (_boundaryPoints.Count >= 3)
            {
                var first = new OpenCvSharp.Point(_boundaryPoints[0].X, _boundaryPoints[0].Y);
                var last = new OpenCvSharp.Point(_boundaryPoints.Last().X, _boundaryPoints.Last().Y);
                DrawDashedLine(image, last, first, color, 3);

                // 반투명 내부 영역 표시 (미리보기)
                var cvPoints = _boundaryPoints.Select(p => new OpenCvSharp.Point(p.X, p.Y)).ToArray();
                var overlay = image.Clone();
                Cv2.FillPoly(overlay, new[] { cvPoints }, new Scalar(255, 255, 0, 50)); // 연한 노란색
                Cv2.AddWeighted(image, 0.85, overlay, 0.15, 0, image);
                overlay.Dispose();
            }
        }

        /// <summary>
        /// 점선 그리기
        /// </summary>
        private void DrawDashedLine(Mat image, OpenCvSharp.Point start, OpenCvSharp.Point end, Scalar color, int thickness)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            var dashLength = 15;
            var gapLength = 8;
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
        /// 상태 정보 표시
        /// </summary>
        private void DrawStatusInfo(Mat image)
        {
            var y = 30;
            var lineHeight = 30;

            // 제목
            Cv2.PutText(image, "아크릴 경계선 설정", 
                      new OpenCvSharp.Point(10, y), HersheyFonts.HersheySimplex, 
                      0.8, new Scalar(255, 255, 255), 2);
            Cv2.PutText(image, "아크릴 경계선 설정", 
                      new OpenCvSharp.Point(10, y), HersheyFonts.HersheySimplex, 
                      0.8, new Scalar(0, 255, 255), 1);

            y += lineHeight;

            // 현재 점 개수
            var statusText = $"경계점: {_boundaryPoints.Count}개";
            if (_boundaryPoints.Count >= 3)
            {
                statusText += " (완성 가능)";
            }
            else if (_boundaryPoints.Count > 0)
            {
                statusText += $" (최소 {3 - _boundaryPoints.Count}개 더 필요)";
            }

            Cv2.PutText(image, statusText, 
                      new OpenCvSharp.Point(10, y), HersheyFonts.HersheySimplex, 
                      0.6, new Scalar(255, 255, 255), 1);

            y += lineHeight;

            // 조작 안내 (간략)
            Cv2.PutText(image, "좌클릭: 점 추가 | 우클릭: 초기화 | SPACE/ENTER: 완성 | ESC: 취소", 
                      new OpenCvSharp.Point(10, y), HersheyFonts.HersheySimplex, 
                      0.5, new Scalar(200, 200, 200), 1);
        }

        /// <summary>
        /// 사용법 출력
        /// </summary>
        private void ShowInstructions()
        {
            System.Diagnostics.Debug.WriteLine("=== 아크릴 경계선 설정 도구 ===");
            System.Diagnostics.Debug.WriteLine("좌클릭: 경계점 추가");
            System.Diagnostics.Debug.WriteLine("우클릭: 모든 점 초기화");
            System.Diagnostics.Debug.WriteLine("SPACE/ENTER: 경계선 완성 (최소 3개 점 필요)");
            System.Diagnostics.Debug.WriteLine("U: 마지막 점 제거");
            System.Diagnostics.Debug.WriteLine("C: 모든 점 삭제");
            System.Diagnostics.Debug.WriteLine("ESC: 취소");
            System.Diagnostics.Debug.WriteLine("=====================================");
        }

        public void Dispose()
        {
            _originalFrame?.Dispose();
        }
    }
}