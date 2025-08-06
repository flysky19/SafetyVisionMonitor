using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SafetyVisionMonitor.Services;

namespace SafetyVisionMonitor.Services.Handlers
{
    /// <summary>
    /// 확장 가능한 알림 처리기 (이메일, SMS, 웹훅 등)
    /// </summary>
    public class NotificationHandler : BaseSafetyEventHandler, IDisposable
    {
        public override string Name => "Notification Handler";
        public override int Priority => 400; // 낮은 우선순위 (외부 통신)

        private readonly List<INotificationProvider> _providers = new();
        private readonly object _providersLock = new object();
        private bool _disposed = false;

        public NotificationHandler()
        {
            // 기본 알림 제공자들 등록
            RegisterDefaultProviders();
        }

        private void RegisterDefaultProviders()
        {
            // 향후 구현할 알림 제공자들
            RegisterProvider(new EmailNotificationProvider());
            RegisterProvider(new WebhookNotificationProvider());
            RegisterProvider(new SlackNotificationProvider());
            RegisterProvider(new TeamsNotificationProvider());
        }

        public override async Task HandleAsync(SafetyEventContext context)
        {
            try
            {
                var violation = context.Violation;
                var notification = CreateNotification(violation, context);

                List<INotificationProvider> activeProviders;
                lock (_providersLock)
                {
                    activeProviders = _providers
                        .Where(p => p.IsEnabled && p.ShouldNotify(violation))
                        .ToList();
                }

                if (activeProviders.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("NotificationHandler: No active providers for this violation");
                    return;
                }

                // 모든 활성화된 제공자에게 병렬로 알림 발송
                var notificationTasks = activeProviders.Select(async provider =>
                {
                    try
                    {
                        await provider.SendNotificationAsync(notification);
                        System.Diagnostics.Debug.WriteLine($"NotificationHandler: Sent via {provider.Name}");
                        return (provider.Name, true, "");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"NotificationHandler: Failed via {provider.Name} - {ex.Message}");
                        return (provider.Name, false, ex.Message);
                    }
                });

                var results = await Task.WhenAll(notificationTasks);
                
                // 결과를 컨텍스트에 저장
                var successCount = results.Count(r => r.Item2);
                var failureCount = results.Length - successCount;
                
                context.SetProperty("NotificationProviders", results.Length);
                context.SetProperty("NotificationSuccesses", successCount);
                context.SetProperty("NotificationFailures", failureCount);
                context.SetProperty("NotificationResults", results);

                System.Diagnostics.Debug.WriteLine($"NotificationHandler: Completed - {successCount} success, {failureCount} failed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NotificationHandler: Error - {ex.Message}");
                context.SetProperty("NotificationError", ex.Message);
            }
        }

        public override bool CanHandle(SafetyEventContext context)
        {
            // 설정에 따라 알림이 필요한 이벤트만 처리
            return IsNotificationEnabled(context.Violation.ViolationType);
        }

        /// <summary>
        /// 알림 제공자 등록
        /// </summary>
        public void RegisterProvider(INotificationProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));

            lock (_providersLock)
            {
                if (!_providers.Any(p => p.GetType() == provider.GetType()))
                {
                    _providers.Add(provider);
                    System.Diagnostics.Debug.WriteLine($"NotificationHandler: Registered provider - {provider.Name}");
                }
            }
        }

        /// <summary>
        /// 알림 제공자 제거
        /// </summary>
        public void UnregisterProvider<T>() where T : INotificationProvider
        {
            lock (_providersLock)
            {
                var provider = _providers.FirstOrDefault(p => p is T);
                if (provider != null)
                {
                    _providers.Remove(provider);
                    System.Diagnostics.Debug.WriteLine($"NotificationHandler: Unregistered provider - {provider.Name}");
                }
            }
        }

        private SafetyNotification CreateNotification(ZoneViolation violation, SafetyEventContext context)
        {
            var notification = new SafetyNotification
            {
                Id = Guid.NewGuid(),
                Timestamp = violation.Timestamp,
                ViolationType = violation.ViolationType,
                Severity = GetNotificationSeverity(violation.ViolationType),
                Subject = GenerateSubject(violation),
                Message = GenerateMessage(violation, context),
                CameraId = violation.Detection.CameraId,
                ZoneId = violation.Zone.Id,
                ZoneName = violation.Zone.Name,
                Confidence = violation.Confidence,
                ImagePath = context.GetProperty<string>("CapturedImagePath"),
                VideoPath = context.GetProperty<string>("RecordedVideoPath"),
                Metadata = context.Properties.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString() ?? "")
            };

            return notification;
        }

        private NotificationSeverity GetNotificationSeverity(ViolationType violationType)
        {
            return violationType switch
            {
                ViolationType.DangerZoneEntry => NotificationSeverity.Critical,
                ViolationType.WarningZoneEntry => NotificationSeverity.Warning,
                ViolationType.UnauthorizedAreaAccess => NotificationSeverity.High,
                _ => NotificationSeverity.Info
            };
        }

        private string GenerateSubject(ZoneViolation violation)
        {
            var zoneType = violation.ViolationType == ViolationType.DangerZoneEntry ? "위험구역" : "경고구역";
            return $"🚨 {zoneType} 진입 감지 - {violation.Zone.Name}";
        }

        private string GenerateMessage(ZoneViolation violation, SafetyEventContext context)
        {
            var zoneType = violation.ViolationType == ViolationType.DangerZoneEntry ? "위험구역" : "경고구역";
            
            return $"안전 모니터링 시스템에서 {zoneType} 진입을 감지했습니다.\n\n" +
                   $"📍 위치 정보:\n" +
                   $"  - 구역: {violation.Zone.Name} ({violation.Zone.Id})\n" +
                   $"  - 카메라: {violation.Detection.CameraId}\n" +
                   $"  - 시간: {violation.Timestamp:yyyy-MM-dd HH:mm:ss}\n\n" +
                   $"🔍 검출 정보:\n" +
                   $"  - 신뢰도: {violation.Confidence:P1}\n" +
                   $"  - 심각도: {context.SafetyEvent.Severity}\n\n" +
                   $"📎 첨부 파일:\n" +
                   $"  - 이미지: {(context.GetProperty<string>("CapturedImagePath") != null ? "있음" : "없음")}\n" +
                   $"  - 동영상: {(context.GetProperty<string>("RecordedVideoPath") != null ? "있음" : "없음")}\n\n" +
                   $"즉시 현장을 확인하고 필요한 조치를 취해주시기 바랍니다.";
        }

        private bool IsNotificationEnabled(ViolationType violationType)
        {
            // 설정에서 알림 활성화 여부 확인 (향후 구현)
            return violationType == ViolationType.DangerZoneEntry; // 위험구역만 기본 활성화
        }

        public void Dispose()
        {
            if (_disposed) return;

            lock (_providersLock)
            {
                foreach (var provider in _providers.OfType<IDisposable>())
                {
                    try
                    {
                        provider.Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"NotificationHandler: Provider disposal error - {ex.Message}");
                    }
                }
                _providers.Clear();
            }

            _disposed = true;
            System.Diagnostics.Debug.WriteLine("NotificationHandler: Disposed");
        }
    }

    /// <summary>
    /// 알림 제공자 인터페이스
    /// </summary>
    public interface INotificationProvider
    {
        string Name { get; }
        bool IsEnabled { get; set; }
        bool ShouldNotify(ZoneViolation violation);
        Task SendNotificationAsync(SafetyNotification notification);
    }

    /// <summary>
    /// 안전 알림 데이터
    /// </summary>
    public class SafetyNotification
    {
        public Guid Id { get; set; }
        public DateTime Timestamp { get; set; }
        public ViolationType ViolationType { get; set; }
        public NotificationSeverity Severity { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string CameraId { get; set; } = string.Empty;
        public string ZoneId { get; set; } = string.Empty;
        public string ZoneName { get; set; } = string.Empty;
        public float Confidence { get; set; }
        public string? ImagePath { get; set; }
        public string? VideoPath { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>
    /// 알림 심각도
    /// </summary>
    public enum NotificationSeverity
    {
        Info,
        Warning,
        High,
        Critical
    }

    // 기본 알림 제공자들 (향후 구현)
    internal class EmailNotificationProvider : INotificationProvider
    {
        public string Name => "Email";
        public bool IsEnabled { get; set; } = false; // 기본 비활성화
        
        public bool ShouldNotify(ZoneViolation violation) => IsEnabled;
        
        public async Task SendNotificationAsync(SafetyNotification notification)
        {
            // 이메일 발송 구현 (향후)
            await Task.Delay(100); // 임시
            System.Diagnostics.Debug.WriteLine($"EmailProvider: Would send email - {notification.Subject}");
        }
    }

    internal class WebhookNotificationProvider : INotificationProvider
    {
        public string Name => "Webhook";
        public bool IsEnabled { get; set; } = false;
        
        public bool ShouldNotify(ZoneViolation violation) => IsEnabled;
        
        public async Task SendNotificationAsync(SafetyNotification notification)
        {
            // HTTP 웹훅 호출 구현 (향후)
            await Task.Delay(50);
            System.Diagnostics.Debug.WriteLine($"WebhookProvider: Would send webhook - {notification.Subject}");
        }
    }

    internal class SlackNotificationProvider : INotificationProvider
    {
        public string Name => "Slack";
        public bool IsEnabled { get; set; } = false;
        
        public bool ShouldNotify(ZoneViolation violation) => IsEnabled;
        
        public async Task SendNotificationAsync(SafetyNotification notification)
        {
            // Slack 메시지 발송 구현 (향후)
            await Task.Delay(200);
            System.Diagnostics.Debug.WriteLine($"SlackProvider: Would send slack - {notification.Subject}");
        }
    }

    internal class TeamsNotificationProvider : INotificationProvider
    {
        public string Name => "Teams";
        public bool IsEnabled { get; set; } = false;
        
        public bool ShouldNotify(ZoneViolation violation) => IsEnabled;
        
        public async Task SendNotificationAsync(SafetyNotification notification)
        {
            // Microsoft Teams 메시지 발송 구현 (향후)
            await Task.Delay(150);
            System.Diagnostics.Debug.WriteLine($"TeamsProvider: Would send teams - {notification.Subject}");
        }
    }
}