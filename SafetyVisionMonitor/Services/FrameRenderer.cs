using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using SafetyVisionMonitor.Shared.Models;
using SafetyVisionMonitor.Services;
using Rect = OpenCvSharp.Rect;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 효율적인 카메라 프레임 렌더링 서비스
    /// WriteableBitmap을 사용하여 메모리 할당 최소화
    /// </summary>
    public class FrameRenderer : IDisposable
    {
        private WriteableBitmap? _bitmap;
        private readonly object _bitmapLock = new object();
        private int _frameWidth;
        private int _frameHeight;
        private bool _disposed;
        // private readonly KoreanTextRenderer _koreanTextRenderer = new(); // 한글 렌더러 제거

        /// <summary>
        /// 현재 비트맵 (UI 바인딩용)
        /// </summary>
        public WriteableBitmap? CurrentBitmap
        {
            get
            {
                lock (_bitmapLock)
                {
                    return _bitmap;
                }
            }
        }

        /// <summary>
        /// 프레임 크기 변경 이벤트
        /// </summary>
        public event EventHandler<FrameSizeChangedEventArgs>? FrameSizeChanged;

        /// <summary>
        /// Mat 프레임을 WriteableBitmap으로 효율적으로 변환
        /// </summary>
        public async Task<bool> RenderFrameAsync(Mat frame)
        {
            if (_disposed || frame == null || frame.Empty())
                return false;

            return await Task.Run(() =>
            {
                try
                {
                    lock (_bitmapLock)
                    {
                        // 프레임 크기가 변경되었는지 확인
                        if (_bitmap == null || 
                            _frameWidth != frame.Width || 
                            _frameHeight != frame.Height)
                        {
                            // 새로운 WriteableBitmap 생성
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                _frameWidth = frame.Width;
                                _frameHeight = frame.Height;
                                _bitmap = new WriteableBitmap(
                                    _frameWidth, 
                                    _frameHeight, 
                                    96, 96, 
                                    PixelFormats.Bgr24, 
                                    null);
                            });

                            // 프레임 크기 변경 알림
                            FrameSizeChanged?.Invoke(this, 
                                new FrameSizeChangedEventArgs(_frameWidth, _frameHeight));
                        }

                        // WriteableBitmap에 직접 쓰기
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (_bitmap != null && !_disposed)
                            {
                                WriteableBitmapConverter.ToWriteableBitmap(frame, _bitmap);
                            }
                        });
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Frame rendering error: {ex.Message}");
                    return false;
                }
            });
        }

        /// <summary>
        /// Mat 프레임에 검출 결과를 그리고 WriteableBitmap으로 변환
        /// </summary>
        public async Task<bool> RenderFrameWithDetectionsAsync(Mat frame, IEnumerable<DetectionResult>? detections)
        {
            return await RenderFrameWithTrackingAsync(frame, detections, null, null);
        }
        
        /// <summary>
        /// Mat 프레임에 검출 결과와 추적 정보를 그리고 WriteableBitmap으로 변환
        /// </summary>
        public async Task<bool> RenderFrameWithTrackingAsync(Mat frame, IEnumerable<DetectionResult>? detections, 
            List<TrackedPerson>? trackedPersons, TrackingConfiguration? trackingConfig)
        {
            if (_disposed || frame == null || frame.Empty())
                return false;

            return await Task.Run(() =>
            {
                try
                {
                    // 프레임 복사본 생성 (원본 보호)
                    using var displayFrame = frame.Clone();
                    
                    // 추적 경로 그리기 (검출 박스보다 먼저)
                    if (trackedPersons != null && trackingConfig?.ShowTrackingPath == true)
                    {
                        DrawTrackingPaths(displayFrame, trackedPersons, trackingConfig);
                    }
                    
                    // 검출 결과가 있으면 박스 그리기
                    if (detections != null)
                    {
                        DrawDetectionBoxes(displayFrame, detections, trackingConfig);
                    }

                    // 렌더링
                    lock (_bitmapLock)
                    {
                        // 프레임 크기가 변경되었는지 확인
                        if (_bitmap == null || 
                            _frameWidth != displayFrame.Width || 
                            _frameHeight != displayFrame.Height)
                        {
                            // 새로운 WriteableBitmap 생성
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                _frameWidth = displayFrame.Width;
                                _frameHeight = displayFrame.Height;
                                _bitmap = new WriteableBitmap(
                                    _frameWidth, 
                                    _frameHeight, 
                                    96, 96, 
                                    PixelFormats.Bgr24, 
                                    null);
                            });

                            // 프레임 크기 변경 알림
                            FrameSizeChanged?.Invoke(this, 
                                new FrameSizeChangedEventArgs(_frameWidth, _frameHeight));
                        }

                        // WriteableBitmap에 직접 쓰기
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (_bitmap != null && !_disposed)
                            {
                                WriteableBitmapConverter.ToWriteableBitmap(displayFrame, _bitmap);
                            }
                        });
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Frame rendering error: {ex.Message}");
                    return false;
                }
            });
        }

        /// <summary>
        /// 검출된 객체에 대한 박스 그리기
        /// </summary>
        private void DrawDetectionBoxes(Mat frame, IEnumerable<DetectionResult> detections, TrackingConfiguration? config = null)
        {
            foreach (var detection in detections)
            {
                // 바운딩 박스 좌표
                var rect = new Rect(
                    (int)detection.BoundingBox.X,
                    (int)detection.BoundingBox.Y,
                    (int)detection.BoundingBox.Width,
                    (int)detection.BoundingBox.Height
                );

                // 색상 결정 (객체별 색상)
                var color = GetDetectionColor(detection.Label);

                // 박스 그리기 (사람은 얇은 선, 나머지는 기본)
                int thickness = detection.Label == "person" ? 1 : 2;
                Cv2.Rectangle(frame, rect, color, thickness);

                // 라벨 텍스트 (트래킹 ID 포함 - 설정에 따라)
                var label = detection.TrackingId.HasValue && (config?.ShowTrackingId ?? true)
                    ? $"{detection.DisplayName} ID:{detection.TrackingId} ({detection.Confidence:P0})"
                    : $"{detection.DisplayName} ({detection.Confidence:P0})";
                
                // OpenCV 기본 텍스트 사용
                double fontScale = 0.5;
                int textThickness = 1;
                int baseline = 0;
                var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, fontScale, textThickness, out baseline);
                
                // 텍스트 배경 영역
                var textRect = new Rect(
                    rect.X,
                    rect.Y - textSize.Height - 8,
                    textSize.Width + 8,
                    textSize.Height + 8
                );

                // 텍스트가 화면 밖으로 나가지 않도록 조정
                if (textRect.Y < 0)
                {
                    textRect.Y = rect.Y + rect.Height + 5;
                }

                // 텍스트 배경 그리기
                Cv2.Rectangle(frame, textRect, color, -1);
                
                // 텍스트 그리기 (흰색)
                Cv2.PutText(frame, label,
                    new OpenCvSharp.Point(rect.X + 4, rect.Y - 5),
                    HersheyFonts.HersheySimplex,
                    fontScale,
                    new Scalar(255, 255, 255), // 흰색 텍스트
                    textThickness,
                    LineTypes.AntiAlias);

                // 중심점 표시 (선택사항)
                var center = new OpenCvSharp.Point(
                    rect.X + rect.Width / 2,
                    rect.Y + rect.Height / 2
                );
                Cv2.Circle(frame, center, 3, color, -1);
            }
        }

        /// <summary>
        /// 객체별 색상 반환
        /// </summary>
        private Scalar GetDetectionColor(string label)
        {
            return label switch
            {
                "person" => new Scalar(0, 255, 0),      // 초록색
                "car" => new Scalar(255, 0, 0),         // 파란색
                "truck" => new Scalar(255, 255, 0),     // 청록색
                "bicycle" => new Scalar(0, 255, 255),   // 노란색
                "motorcycle" => new Scalar(255, 0, 255), // 자홍색
                _ => new Scalar(128, 128, 128)          // 기본 회색
            };
        }
        
        /// <summary>
        /// 빈 프레임으로 초기화
        /// </summary>
        public void Clear()
        {
            lock (_bitmapLock)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_bitmap != null)
                    {
                        // 새로운 검은색 WriteableBitmap 생성
                        var clearBitmap = new WriteableBitmap(
                            _bitmap.PixelWidth,
                            _bitmap.PixelHeight,
                            _bitmap.DpiX,
                            _bitmap.DpiY,
                            _bitmap.Format,
                            null);
                        
                        // 검은색 픽셀 데이터 생성
                        var stride = clearBitmap.PixelWidth * 3; // BGR24 = 3 bytes per pixel
                        var pixelData = new byte[stride * clearBitmap.PixelHeight];
                        
                        // WriteableBitmap에 쓰기
                        clearBitmap.WritePixels(
                            new Int32Rect(0, 0, clearBitmap.PixelWidth, clearBitmap.PixelHeight),
                            pixelData,
                            stride,
                            0);
                        
                        _bitmap = clearBitmap;
                    }
                });
            }
        }
        
        /// <summary>
        /// 추적 경로 그리기
        /// </summary>
        private void DrawTrackingPaths(Mat frame, List<TrackedPerson> trackedPersons, TrackingConfiguration config)
        {
            foreach (var person in trackedPersons.Where(p => p.IsActive))
            {
                if (person.TrackingHistory == null || person.TrackingHistory.Count < 2)
                    continue;
                    
                // 경로 표시 길이 제한
                var pathLength = Math.Min(person.TrackingHistory.Count, config.PathDisplayLength);
                var recentPath = person.TrackingHistory.TakeLast(pathLength).ToList();
                
                if (recentPath.Count < 2) continue;
                
                // 추적 ID별 색상 결정 (고유한 색상)
                var colors = new[]
                {
                    new Scalar(255, 0, 0),    // 빨강
                    new Scalar(0, 255, 0),    // 초록
                    new Scalar(0, 0, 255),    // 파랑
                    new Scalar(255, 255, 0),  // 노랑
                    new Scalar(255, 0, 255),  // 마젠타
                    new Scalar(0, 255, 255),  // 시안
                    new Scalar(255, 128, 0),  // 주황
                    new Scalar(128, 0, 255)   // 보라
                };
                
                var colorIndex = person.TrackingId % colors.Length;
                var pathColor = colors[colorIndex];
                
                // 경로 선 그리기
                for (int i = 0; i < recentPath.Count - 1; i++)
                {
                    var startPoint = new OpenCvSharp.Point((int)recentPath[i].X, (int)recentPath[i].Y);
                    var endPoint = new OpenCvSharp.Point((int)recentPath[i + 1].X, (int)recentPath[i + 1].Y);
                    
                    // 선의 두께는 최신 경로일수록 두껍게
                    var thickness = Math.Max(1, 3 - (recentPath.Count - i - 1) / 3);
                    
                    Cv2.Line(frame, startPoint, endPoint, pathColor, thickness);
                }
                
                // 현재 위치에 원 그리기
                if (recentPath.Any())
                {
                    var currentPos = recentPath.Last();
                    var centerPoint = new OpenCvSharp.Point((int)currentPos.X, (int)currentPos.Y);
                    Cv2.Circle(frame, centerPoint, 5, pathColor, -1);
                    
                    // 트래킹 ID 표시 (설정이 활성화된 경우)
                    if (config.ShowTrackingId)
                    {
                        var idText = $"#{person.TrackingId}";
                        var textPos = new OpenCvSharp.Point((int)currentPos.X + 10, (int)currentPos.Y - 10);
                        
                        // OpenCV 기본 텍스트 렌더링
                        double fontScale = 0.6;
                        int textThickness = 2;
                        int baseline = 0;
                        var textSize = Cv2.GetTextSize(idText, HersheyFonts.HersheySimplex, fontScale, textThickness, out baseline);
                        
                        // 텍스트 배경
                        var bgRect = new Rect(textPos.X - 2, textPos.Y - textSize.Height - 2, textSize.Width + 4, textSize.Height + 4);
                        Cv2.Rectangle(frame, bgRect, new Scalar(0, 0, 0), -1);
                        
                        // 텍스트 그리기
                        Cv2.PutText(frame, idText, textPos, HersheyFonts.HersheySimplex, fontScale, 
                                  new Scalar(255, 255, 255), 1, LineTypes.AntiAlias);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            lock (_bitmapLock)
            {
                _bitmap = null;
            }
            
            // _koreanTextRenderer?.Dispose(); // 한글 렌더러 제거됨
        }
    }

    public class FrameSizeChangedEventArgs : EventArgs
    {
        public int Width { get; }
        public int Height { get; }

        public FrameSizeChangedEventArgs(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }
}