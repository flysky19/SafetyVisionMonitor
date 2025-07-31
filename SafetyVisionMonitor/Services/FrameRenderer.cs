using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

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

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            lock (_bitmapLock)
            {
                _bitmap = null;
            }
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