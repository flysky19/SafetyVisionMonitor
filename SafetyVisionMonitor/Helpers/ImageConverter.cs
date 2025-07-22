using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace SafetyVisionMonitor.Helpers
{
    public static class ImageConverter
    {
        public static BitmapSource? MatToBitmapSource(Mat mat)
        {
            if (mat == null || mat.Empty())
                return null;
            
            try
            {
                // 원본 Mat 정보 로깅
                System.Diagnostics.Debug.WriteLine(
                    $"Converting Mat - Size: {mat.Width}x{mat.Height}, " +
                    $"Channels: {mat.Channels()}, " +
                    $"Type: {mat.Type()}, " +
                    $"Depth: {mat.Depth()}");
                
                // BGR24는 직접 변환 가능
                if (mat.Channels() == 3 && mat.Depth() == MatType.CV_8U)
                {
                    // OpenCvSharp의 기본 변환 사용 (BGR -> Bgr24)
                    var bitmap = BitmapSourceConverter.ToBitmapSource(mat);
                    bitmap.Freeze();
                    
                    System.Diagnostics.Debug.WriteLine($"Converted to: {bitmap.Format}");
                    return bitmap;
                }
                
                // 다른 포맷 처리
                Mat convertedMat = new Mat();
                
                switch (mat.Channels())
                {
                    case 1: // 그레이스케일
                        Cv2.CvtColor(mat, convertedMat, ColorConversionCodes.GRAY2BGR);
                        break;
                        
                    case 4: // BGRA
                        Cv2.CvtColor(mat, convertedMat, ColorConversionCodes.BGRA2BGR);
                        break;
                        
                    default:
                        convertedMat = mat.Clone();
                        break;
                }
                
                var result = BitmapSourceConverter.ToBitmapSource(convertedMat);
                result.Freeze();
                convertedMat.Dispose();
                
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Conversion error: {ex.Message}");
                return null;
            }
        }
        
        // 디버깅용: Mat의 일부 픽셀 값 확인
        public static void DebugMatPixels(Mat mat)
        {
            if (mat == null || mat.Empty()) return;
            
            try
            {
                // 좌상단 5x5 픽셀 값 출력
                System.Diagnostics.Debug.WriteLine("=== Pixel Values (Top-Left 5x5) ===");
                
                for (int y = 0; y < Math.Min(5, mat.Height); y++)
                {
                    for (int x = 0; x < Math.Min(5, mat.Width); x++)
                    {
                        if (mat.Channels() == 3)
                        {
                            var pixel = mat.At<Vec3b>(y, x);
                            System.Diagnostics.Debug.Write($"({pixel.Item0},{pixel.Item1},{pixel.Item2}) ");
                        }
                        else if (mat.Channels() == 1)
                        {
                            var pixel = mat.At<byte>(y, x);
                            System.Diagnostics.Debug.Write($"{pixel} ");
                        }
                    }
                    System.Diagnostics.Debug.WriteLine("");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Debug pixels error: {ex.Message}");
            }
        }
    }
}