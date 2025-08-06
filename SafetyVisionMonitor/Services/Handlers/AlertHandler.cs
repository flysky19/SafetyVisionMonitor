using System;
using System.Threading.Tasks;
using SafetyVisionMonitor.Services;

namespace SafetyVisionMonitor.Services.Handlers
{
    /// <summary>
    /// 즉시 알림 처리기 (팝업, 소리 등)
    /// </summary>
    public class AlertHandler : BaseSafetyEventHandler
    {
        public override string Name => "Alert Handler";
        public override int Priority => 100; // 높은 우선순위로 즉시 알림

        public override async Task HandleAsync(SafetyEventContext context)
        {
            try
            {
                var violation = context.Violation;
                var safetyEvent = context.SafetyEvent;

                // 알림 타입 결정
                var alertLevel = GetAlertLevel(violation.ViolationType);
                var message = GenerateAlertMessage(violation);

                // 시각적 알림 (향후 UI 통합 시 구현)
                await ShowVisualAlertAsync(message, alertLevel);

                // 음향 알림 (향후 구현)
                await PlaySoundAlertAsync(alertLevel);

                // 시스템 알림 (Windows 토스트 등, 향후 구현)
                await ShowSystemNotificationAsync(message, alertLevel);

                System.Diagnostics.Debug.WriteLine($"AlertHandler: Alert triggered - {alertLevel} level for {violation.Zone.Name}");
                context.SetProperty("AlertLevel", alertLevel);
                context.SetProperty("AlertMessage", message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AlertHandler: Error - {ex.Message}");
            }
        }

        public override bool CanHandle(SafetyEventContext context)
        {
            // 위험 구역 진입은 무조건 알림, 경고 구역은 설정에 따라
            return context.Violation.ViolationType == ViolationType.DangerZoneEntry ||
                   (context.Violation.ViolationType == ViolationType.WarningZoneEntry && IsWarningAlertEnabled());
        }

        private AlertLevel GetAlertLevel(ViolationType violationType)
        {
            return violationType switch
            {
                ViolationType.DangerZoneEntry => AlertLevel.Critical,
                ViolationType.WarningZoneEntry => AlertLevel.Warning,
                ViolationType.UnauthorizedAreaAccess => AlertLevel.High,
                _ => AlertLevel.Info
            };
        }

        private string GenerateAlertMessage(ZoneViolation violation)
        {
            var zoneType = violation.ViolationType == ViolationType.DangerZoneEntry ? "위험구역" : "경고구역";
            return $"⚠️ {zoneType} 진입 감지!\n" +
                   $"구역: {violation.Zone.Name}\n" +
                   $"카메라: {violation.Detection.CameraId}\n" +
                   $"신뢰도: {violation.Confidence:P1}\n" +
                   $"시간: {violation.Timestamp:HH:mm:ss}";
        }

        private async Task ShowVisualAlertAsync(string message, AlertLevel level)
        {
            // 향후 UI 통합 시 메인 윈도우에 알림 패널 표시
            await Task.Run(() =>
            {
                // TODO: UI 알림 구현
                System.Diagnostics.Debug.WriteLine($"AlertHandler: Visual alert - {level}: {message.Replace('\n', ' ')}");
            });
        }

        private async Task PlaySoundAlertAsync(AlertLevel level)
        {
            await Task.Run(() =>
            {
                try
                {
                    // 시스템 기본 소리 재생
                    if (level == AlertLevel.Critical)
                    {
                        System.Media.SystemSounds.Hand.Play(); // 오류음
                    }
                    else if (level == AlertLevel.Warning || level == AlertLevel.High)
                    {
                        System.Media.SystemSounds.Exclamation.Play(); // 경고음
                    }
                    else
                    {
                        System.Media.SystemSounds.Beep.Play(); // 기본음
                    }

                    System.Diagnostics.Debug.WriteLine($"AlertHandler: Sound alert played - {level}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AlertHandler: Sound play error - {ex.Message}");
                }
            });
        }

        private async Task ShowSystemNotificationAsync(string message, AlertLevel level)
        {
            // 향후 Windows 10/11 토스트 알림 구현
            await Task.Run(() =>
            {
                // TODO: 토스트 알림 구현
                System.Diagnostics.Debug.WriteLine($"AlertHandler: System notification - {level}");
            });
        }

        private bool IsWarningAlertEnabled()
        {
            // 설정에서 경고 구역 알림 활성화 여부 확인 (향후 구현)
            return true; // 기본적으로 활성화
        }
    }

    /// <summary>
    /// 알림 레벨
    /// </summary>
    public enum AlertLevel
    {
        Info,
        Warning,
        High,
        Critical
    }
}