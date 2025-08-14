using SafetyVisionMonitor.Shared.Models;
using SafetyVisionMonitor.Shared.Services;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// Zone 알림 서비스 구현
    /// </summary>
    public class ZoneNotificationService : IZoneNotificationService
    {
        public void NotifyZoneUpdated(Zone3D zone)
        {
            try
            {
                // UI 스레드에서 앱 데이터에 알림
                App.Current.Dispatcher.Invoke(() =>
                {
                    App.AppData.NotifyZoneUpdated(zone);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ZoneNotificationService: Failed to notify zone update - {ex.Message}");
            }
        }

        public void NotifyZoneVisualizationUpdate()
        {
            try
            {
                // UI 스레드에서 시각화 업데이트 알림
                App.Current.Dispatcher.Invoke(() =>
                {
                    App.AppData.NotifyZoneVisualizationUpdate();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ZoneNotificationService: Failed to notify visualization update - {ex.Message}");
            }
        }
    }
}