using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using SkiaSharp;
using System.Runtime.InteropServices;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 한글 텍스트 렌더링을 위한 서비스
    /// OpenCV의 기본 폰트는 한글을 지원하지 않으므로 SkiaSharp를 사용하여 렌더링
    /// </summary>
    public class KoreanTextRenderer : IDisposable
    {
        private readonly Dictionary<string, SKTypeface> _typefaceCache = new();
        private readonly object _cacheLock = new();
        
        // Windows에서 사용 가능한 한글 폰트 목록 (우선순위 순)
        private static readonly string[] KoreanFonts = new[]
        {
            "Malgun Gothic",      // 맑은 고딕 (Windows 기본)
            "NanumGothic",       // 나눔고딕
            "Gulim",             // 굴림
            "Dotum",             // 돋움
            "Batang",            // 바탕
            "Arial Unicode MS",   // 유니코드 지원
            "Microsoft Sans Serif"
        };
        
        private SKTypeface? _defaultTypeface;
        
        public KoreanTextRenderer()
        {
            InitializeDefaultTypeface();
        }
        
        private void InitializeDefaultTypeface()
        {
            // 사용 가능한 첫 번째 한글 폰트 찾기
            foreach (var fontName in KoreanFonts)
            {
                try
                {
                    var typeface = SKTypeface.FromFamilyName(fontName);
                    if (typeface != null)
                    {
                        _defaultTypeface = typeface;
                        System.Diagnostics.Debug.WriteLine($"KoreanTextRenderer: Using font '{fontName}'");
                        break;
                    }
                }
                catch
                {
                    // 폰트를 찾을 수 없음, 다음 시도
                }
            }
            
            // 폴백: 시스템 기본 폰트 사용
            if (_defaultTypeface == null)
            {
                _defaultTypeface = SKTypeface.Default;
                System.Diagnostics.Debug.WriteLine("KoreanTextRenderer: Using system default font");
            }
        }
        
        /// <summary>
        /// 텍스트를 Mat에 렌더링
        /// </summary>
        public void PutText(Mat img, string text, Point org, double fontScale, Scalar color, 
            int thickness = 1, bool drawBackground = false, Scalar? backgroundColor = null)
        {
            if (string.IsNullOrEmpty(text) || img == null || img.IsDisposed)
                return;
            
            try
            {
                // 폰트 크기 계산 (OpenCV fontScale을 픽셀 크기로 변환)
                float fontSize = (float)(fontScale * 20); // 기본 크기 조정
                
                using var paint = new SKPaint
                {
                    Typeface = _defaultTypeface,
                    TextSize = fontSize,
                    IsAntialias = true,
                    Color = new SKColor((byte)color.Val2, (byte)color.Val1, (byte)color.Val0, 255)
                };
                
                // 텍스트 크기 측정
                var textBounds = new SKRect();
                paint.MeasureText(text, ref textBounds);
                
                int textWidth = (int)Math.Ceiling(textBounds.Width);
                int textHeight = (int)Math.Ceiling(textBounds.Height);
                
                // 배경 그리기 (옵션)
                if (drawBackground && backgroundColor.HasValue)
                {
                    var bgColor = backgroundColor.Value;
                    int padding = 5;
                    Cv2.Rectangle(img, 
                        new Rect(org.X - padding, org.Y - textHeight - padding, 
                                textWidth + padding * 2, textHeight + padding * 2),
                        bgColor, -1);
                }
                
                // SkiaSharp 비트맵 생성
                using var bitmap = new SKBitmap(textWidth, textHeight);
                using var canvas = new SKCanvas(bitmap);
                
                // 투명 배경
                canvas.Clear(SKColors.Transparent);
                
                // 텍스트 그리기
                canvas.DrawText(text, -textBounds.Left, -textBounds.Top, paint);
                canvas.Flush();
                
                // SKBitmap을 Mat으로 변환
                using var textMat = ConvertSKBitmapToMat(bitmap);
                
                // 대상 위치 계산
                int x = org.X;
                int y = org.Y - textHeight;
                
                // 이미지 경계 체크
                if (x < 0) x = 0;
                if (y < 0) y = 0;
                if (x + textWidth > img.Width) x = img.Width - textWidth;
                if (y + textHeight > img.Height) y = img.Height - textHeight;
                
                if (x >= 0 && y >= 0 && x + textWidth <= img.Width && y + textHeight <= img.Height)
                {
                    // 알파 블렌딩으로 텍스트 오버레이
                    var roi = img[new Rect(x, y, textWidth, textHeight)];
                    AlphaBlend(textMat, roi);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"KoreanTextRenderer: Error rendering text: {ex.Message}");
                // 폴백: OpenCV 기본 텍스트 렌더링 (한글은 ???로 표시됨)
                Cv2.PutText(img, text, org, HersheyFonts.HersheySimplex, fontScale, color, thickness);
            }
        }
        
        /// <summary>
        /// 텍스트 크기 측정
        /// </summary>
        public Size GetTextSize(string text, double fontScale)
        {
            if (string.IsNullOrEmpty(text))
                return new Size(0, 0);
            
            float fontSize = (float)(fontScale * 20);
            
            using var paint = new SKPaint
            {
                Typeface = _defaultTypeface,
                TextSize = fontSize,
                IsAntialias = true
            };
            
            var textBounds = new SKRect();
            paint.MeasureText(text, ref textBounds);
            
            return new Size((int)Math.Ceiling(textBounds.Width), (int)Math.Ceiling(textBounds.Height));
        }
        
        /// <summary>
        /// SKBitmap을 OpenCV Mat으로 변환
        /// </summary>
        private Mat ConvertSKBitmapToMat(SKBitmap bitmap)
        {
            var info = bitmap.Info;
            var mat = new Mat(info.Height, info.Width, MatType.CV_8UC4);
            
            // 픽셀 데이터 복사
            var pixelData = bitmap.GetPixelSpan();
            Marshal.Copy(pixelData.ToArray(), 0, mat.Data, pixelData.Length);
            
            // RGBA to BGRA 변환
            Cv2.CvtColor(mat, mat, ColorConversionCodes.RGBA2BGRA);
            
            return mat;
        }
        
        /// <summary>
        /// 알파 블렌딩으로 텍스트 오버레이
        /// </summary>
        private void AlphaBlend(Mat src, Mat dst)
        {
            if (src.Channels() != 4 || dst.Channels() != 3)
                return;
            
            // BGRA를 BGR과 Alpha로 분리
            var channels = src.Split();
            var bgr = new Mat();
            Cv2.Merge(new[] { channels[0], channels[1], channels[2] }, bgr);
            var alpha = channels[3];
            
            // 정규화된 알파 채널
            var alphaNorm = new Mat();
            alpha.ConvertTo(alphaNorm, MatType.CV_32F, 1.0 / 255);
            
            // 알파 블렌딩
            for (int c = 0; c < 3; c++)
            {
                var srcChannel = new Mat();
                var dstChannel = new Mat();
                var resultChannel = new Mat();
                
                Cv2.ExtractChannel(bgr, srcChannel, c);
                Cv2.ExtractChannel(dst, dstChannel, c);
                
                srcChannel.ConvertTo(srcChannel, MatType.CV_32F);
                dstChannel.ConvertTo(dstChannel, MatType.CV_32F);
                
                // result = src * alpha + dst * (1 - alpha)
                var srcWeighted = new Mat();
                var dstWeighted = new Mat();
                Cv2.Multiply(srcChannel, alphaNorm, srcWeighted);
                Cv2.Multiply(dstChannel, Scalar.All(1.0) - alphaNorm, dstWeighted);
                Cv2.Add(srcWeighted, dstWeighted, resultChannel);
                
                resultChannel.ConvertTo(resultChannel, MatType.CV_8U);
                Cv2.InsertChannel(resultChannel, dst, c);
                
                // 메모리 해제
                srcChannel.Dispose();
                dstChannel.Dispose();
                resultChannel.Dispose();
                srcWeighted.Dispose();
                dstWeighted.Dispose();
            }
            
            // 메모리 해제
            foreach (var channel in channels)
                channel.Dispose();
            bgr.Dispose();
            alphaNorm.Dispose();
        }
        
        public void Dispose()
        {
            lock (_cacheLock)
            {
                foreach (var typeface in _typefaceCache.Values)
                {
                    typeface?.Dispose();
                }
                _typefaceCache.Clear();
            }
            
            _defaultTypeface?.Dispose();
        }
    }
}