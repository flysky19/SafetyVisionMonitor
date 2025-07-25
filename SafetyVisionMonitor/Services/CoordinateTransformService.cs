using System.Windows;
using SafetyVisionMonitor.Models;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 2D 화면 좌표와 3D 실세계 좌표 간 변환을 담당하는 서비스
    /// </summary>
    public static class CoordinateTransformService
    {
        private const double DEFAULT_PIXELS_PER_METER = 100.0;
        
        /// <summary>
        /// 2D 화면 좌표를 3D 실세계 좌표로 변환
        /// </summary>
        /// <param name="screenPoint">화면 좌표</param>
        /// <param name="frameWidth">프레임 너비</param>
        /// <param name="frameHeight">프레임 높이</param>
        /// <param name="pixelsPerMeter">픽셀/미터 비율 (캘리브레이션 값)</param>
        /// <returns>실세계 좌표 (미터 단위)</returns>
        public static Point2D ScreenToWorld(Point screenPoint, double frameWidth, double frameHeight, double pixelsPerMeter = DEFAULT_PIXELS_PER_METER)
        {
            var centerX = frameWidth / 2.0;
            var centerY = frameHeight / 2.0;
            
            // 중심점 기준 상대 좌표를 미터 단위로 변환
            var worldX = (screenPoint.X - centerX) / pixelsPerMeter;
            var worldY = (screenPoint.Y - centerY) / pixelsPerMeter;
            
            System.Diagnostics.Debug.WriteLine($"ScreenToWorld: Screen({screenPoint.X:F1}, {screenPoint.Y:F1}) -> World({worldX:F2}, {worldY:F2})");
            
            return new Point2D(worldX, worldY);
        }
        
        /// <summary>
        /// 3D 실세계 좌표를 2D 화면 좌표로 변환
        /// </summary>
        /// <param name="worldPoint">실세계 좌표 (미터 단위)</param>
        /// <param name="frameWidth">프레임 너비</param>
        /// <param name="frameHeight">프레임 높이</param>
        /// <param name="pixelsPerMeter">픽셀/미터 비율 (캘리브레이션 값)</param>
        /// <returns>화면 좌표</returns>
        public static Point WorldToScreen(Point2D worldPoint, double frameWidth, double frameHeight, double pixelsPerMeter = DEFAULT_PIXELS_PER_METER)
        {
            var centerX = frameWidth / 2.0;
            var centerY = frameHeight / 2.0;
            
            // 미터를 픽셀로 변환하고 화면 중심 기준으로 배치
            var screenX = centerX + (worldPoint.X * pixelsPerMeter);
            var screenY = centerY + (worldPoint.Y * pixelsPerMeter);
            
            var finalPoint = new Point(screenX, screenY);
            System.Diagnostics.Debug.WriteLine($"WorldToScreen: World({worldPoint.X:F2}, {worldPoint.Y:F2}) -> Screen({finalPoint.X:F1}, {finalPoint.Y:F1})");
            
            return finalPoint;
        }
        
        /// <summary>
        /// 기본 프레임 크기 (320x240)를 사용한 변환
        /// </summary>
        public static Point2D ScreenToWorld(Point screenPoint, double pixelsPerMeter = DEFAULT_PIXELS_PER_METER)
        {
            return ScreenToWorld(screenPoint, 640, 480, pixelsPerMeter);
        }
        
        /// <summary>
        /// 기본 프레임 크기 (320x240)를 사용한 변환
        /// </summary>
        public static Point WorldToScreen(Point2D worldPoint, double pixelsPerMeter = DEFAULT_PIXELS_PER_METER)
        {
            return WorldToScreen(worldPoint, 640, 480, pixelsPerMeter);
        }
    }
}