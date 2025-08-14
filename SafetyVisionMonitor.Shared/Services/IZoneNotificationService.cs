using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionMonitor.Shared.Services
{
    /// <summary>
    /// Zone 변경 알림을 위한 인터페이스
    /// </summary>
    public interface IZoneNotificationService
    {
        /// <summary>
        /// Zone 업데이트 알림
        /// </summary>
        void NotifyZoneUpdated(Zone3D zone);
        
        /// <summary>
        /// Zone 시각화 업데이트 알림
        /// </summary>
        void NotifyZoneVisualizationUpdate();
    }
}