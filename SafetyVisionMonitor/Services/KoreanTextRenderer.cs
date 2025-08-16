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
            "맑은 고딕",          // 한글 이름
            "NanumGothic",       // 나눔고딕
            "나눔고딕",          // 한글 이름
            "Gulim",             // 굴림
            "굴림",              // 한글 이름
            "Dotum",             // 돋움
            "돋움",              // 한글 이름
            "Batang",            // 바탕
            "바탕",              // 한글 이름
            "Arial Unicode MS",   // 유니코드 지원
            "Microsoft Sans Serif",
            "Segoe UI"           // Windows 10/11 기본 UI 폰트
        };
        
        private SKTypeface? _defaultTypeface;
        
        public KoreanTextRenderer()
        {
            InitializeDefaultTypeface();
        }
        
        private void InitializeDefaultTypeface()
        {
            System.Diagnostics.Debug.WriteLine("KoreanTextRenderer: Initializing fonts...");
            
            // 사용 가능한 첫 번째 한글 폰트 찾기
            foreach (var fontName in KoreanFonts)
            {
                try
                {
                    var typeface = SKTypeface.FromFamilyName(fontName, SKFontStyle.Normal);
                    if (typeface != null && typeface.FamilyName != null)
                    {
                        // 한글 테스트 문자열로 폰트 검증
                        using var paint = new SKPaint { Typeface = typeface, TextSize = 12 };
                        var testText = "가나다";
                        var bounds = new SKRect();
                        paint.MeasureText(testText, ref bounds);
                        
                        if (bounds.Width > 0 && bounds.Height > 0)
                        {
                            _defaultTypeface = typeface;
                            System.Diagnostics.Debug.WriteLine($"KoreanTextRenderer: Successfully loaded font '{fontName}' (FamilyName: {typeface.FamilyName})");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"KoreanTextRenderer: Failed to load font '{fontName}': {ex.Message}");
                }
            }
            
            // 폴백: 시스템 기본 폰트 사용
            if (_defaultTypeface == null)
            {
                _defaultTypeface = SKTypeface.Default;
                System.Diagnostics.Debug.WriteLine($"KoreanTextRenderer: Using system default font (FamilyName: {_defaultTypeface.FamilyName})");
            }
        }
        
        /// <summary>
        /// 텍스트를 Mat에 렌더링
        /// </summary>
        public void PutText(Mat img, string text, Point org, double fontScale, Scalar color, 
            int thickness = 1, bool drawBackground = false, Scalar? backgroundColor = null)
        {
            if (string.IsNullOrEmpty(text) || img == null || img.IsDisposed)
            {
                System.Diagnostics.Debug.WriteLine($"KoreanTextRenderer.PutText: Invalid input - text: '{text}', img null: {img == null}, disposed: {img?.IsDisposed}");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"KoreanTextRenderer.PutText: Rendering '{text}' at ({org.X}, {org.Y}) with scale {fontScale}");
            
            try
            {
                // 폰트 체크
                if (_defaultTypeface == null)
                {
                    System.Diagnostics.Debug.WriteLine("KoreanTextRenderer: Default typeface is null!");
                    throw new InvalidOperationException("Default typeface not initialized");
                }
                
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
                
                System.Diagnostics.Debug.WriteLine($"KoreanTextRenderer: Text bounds - Width: {textWidth}, Height: {textHeight}");
                
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
                
                // SkiaSharp 비트맵 생성 (배경색 포함)
                using var bitmap = new SKBitmap(textWidth, textHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var canvas = new SKCanvas(bitmap);
                
                // 배경 처리
                if (drawBackground && backgroundColor.HasValue)
                {
                    var bgColor = backgroundColor.Value;
                    canvas.Clear(new SKColor((byte)bgColor.Val2, (byte)bgColor.Val1, (byte)bgColor.Val0, 255));
                }
                else
                {
                    canvas.Clear(SKColors.Transparent);
                }
                
                // 텍스트 그리기
                canvas.DrawText(text, -textBounds.Left, -textBounds.Top, paint);
                canvas.Flush();
                
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
                    try
                    {
                        // 간단한 오버레이 방식 사용
                        using var textMat = new Mat(textHeight, textWidth, MatType.CV_8UC4);
                        
                        // SKBitmap 픽셀 데이터를 Mat으로 복사
                        var pixels = bitmap.Pixels;
                        var pixelBytes = new byte[pixels.Length * 4];
                        for (int i = 0; i < pixels.Length; i++)
                        {
                            var pixel = pixels[i];
                            pixelBytes[i * 4] = pixel.Blue;
                            pixelBytes[i * 4 + 1] = pixel.Green;
                            pixelBytes[i * 4 + 2] = pixel.Red;
                            pixelBytes[i * 4 + 3] = pixel.Alpha;
                        }
                        Marshal.Copy(pixelBytes, 0, textMat.Data, pixelBytes.Length);
                        
                        // 단순 복사 (배경이 있는 경우) 또는 알파 블렌딩
                        if (drawBackground && backgroundColor.HasValue)
                        {
                            // 배경이 있으면 단순히 BGR로 변환하여 복사
                            using var bgrMat = new Mat();
                            Cv2.CvtColor(textMat, bgrMat, ColorConversionCodes.BGRA2BGR);
                            bgrMat.CopyTo(img[new Rect(x, y, textWidth, textHeight)]);
                        }
                        else
                        {
                            // 투명 배경인 경우 알파 블렌딩
                            var roi = img[new Rect(x, y, textWidth, textHeight)];
                            SimpleAlphaBlend(textMat, roi);
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"KoreanTextRenderer: Successfully rendered text at ({x}, {y})");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"KoreanTextRenderer: Error in overlay: {ex.Message}");
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"KoreanTextRenderer: Error rendering text '{text}': {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"KoreanTextRenderer: Stack trace: {ex.StackTrace}");
                
                // 폴백: 기본 OpenCV 렌더링 시도 (디버깅용)
                // 한글은 지원하지 않지만 시스템 상태 확인용
                try
                {
                    // 간단한 사각형과 함께 디버그 표시
                    if (drawBackground)
                    {
                        var bgColor = backgroundColor ?? new Scalar(128, 128, 128);
                        Cv2.Rectangle(img, new Rect(org.X, org.Y - 20, 100, 25), bgColor, -1);
                    }
                    
                    // 에러 표시
                    Cv2.PutText(img, "[Korean]", org, HersheyFonts.HersheySimplex, fontScale * 0.8, color, thickness);
                }
                catch
                {
                    // 최종 폴백: 아무것도 그리지 않음
                }
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
        /// 간단한 알파 블렌딩
        /// </summary>
        private void SimpleAlphaBlend(Mat src, Mat dst)
        {
            if (src.Channels() != 4 || dst.Channels() != 3)
                return;
            
            try
            {
                // BGRA를 BGR로 변환하면서 알파 채널 적용
                using var bgr = new Mat();
                
                // 채널 분리
                var srcChannels = src.Split();
                Cv2.Merge(new[] { srcChannels[0], srcChannels[1], srcChannels[2] }, bgr);
                var alpha = srcChannels[3];
                
                // 알파 값이 있는 픽셀만 복사
                using var mask = new Mat();
                Cv2.Threshold(alpha, mask, 0, 255, ThresholdTypes.Binary);
                bgr.CopyTo(dst, mask);
                
                // 메모리 해제
                foreach (var channel in srcChannels)
                    channel.Dispose();
                
                // alpha는 srcChannels[3]로 이미 해제됨
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"KoreanTextRenderer: SimpleAlphaBlend error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 알파 블렌딩으로 텍스트 오버레이 (복잡한 버전 - 백업용)
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