using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SafetyVisionMonitor.Shared.Models;
using SafetyVisionMonitor.Services.Handlers;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 안전 이벤트 처리기 인터페이스
    /// </summary>
    public interface ISafetyEventHandler
    {
        /// <summary>
        /// 처리기 이름
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 처리기 활성화 여부
        /// </summary>
        bool IsEnabled { get; set; }

        /// <summary>
        /// 우선순위 (낮을수록 먼저 실행)
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// 안전 이벤트 처리
        /// </summary>
        Task HandleAsync(SafetyEventContext context);

        /// <summary>
        /// 처리기가 해당 이벤트를 처리할 수 있는지 확인
        /// </summary>
        bool CanHandle(SafetyEventContext context);
    }

    /// <summary>
    /// 안전 이벤트 컨텍스트
    /// </summary>
    public class SafetyEventContext
    {
        public SafetyEvent SafetyEvent { get; set; } = new();
        public ZoneViolation Violation { get; set; } = new();
        public DateTime ProcessingStartTime { get; set; } = DateTime.Now;
        public Dictionary<string, object> Properties { get; set; } = new();

        /// <summary>
        /// 컨텍스트에 속성 추가
        /// </summary>
        public void SetProperty(string key, object value)
        {
            Properties[key] = value;
        }

        /// <summary>
        /// 컨텍스트에서 속성 조회
        /// </summary>
        public T? GetProperty<T>(string key)
        {
            return Properties.TryGetValue(key, out var value) && value is T typedValue 
                ? typedValue 
                : default;
        }
    }

    /// <summary>
    /// 안전 이벤트 처리 매니저
    /// </summary>
    public class SafetyEventHandlerManager : IDisposable
    {
        private readonly List<ISafetyEventHandler> _handlers = new();
        private readonly object _handlersLock = new object();
        private bool _disposed = false;

        public SafetyEventHandlerManager()
        {
            // 기본 핸들러들 등록
            RegisterDefaultHandlers();
            System.Diagnostics.Debug.WriteLine("SafetyEventHandlerManager: Initialized with default handlers");
        }

        /// <summary>
        /// 기본 핸들러들 등록
        /// </summary>
        private void RegisterDefaultHandlers()
        {
            RegisterHandler(new AlertHandler());
            RegisterHandler(new LogHandler());
            RegisterHandler(new DatabaseHandler());
            RegisterHandler(new MediaCaptureHandler());
            RegisterHandler(new NotificationHandler());
        }

        /// <summary>
        /// 이벤트 처리기 등록
        /// </summary>
        public void RegisterHandler(ISafetyEventHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            lock (_handlersLock)
            {
                // 중복 등록 방지
                if (_handlers.Any(h => h.GetType() == handler.GetType()))
                {
                    System.Diagnostics.Debug.WriteLine($"SafetyEventHandlerManager: Handler {handler.Name} already registered");
                    return;
                }

                _handlers.Add(handler);
                _handlers.Sort((h1, h2) => h1.Priority.CompareTo(h2.Priority)); // 우선순위로 정렬

                System.Diagnostics.Debug.WriteLine($"SafetyEventHandlerManager: Registered handler - {handler.Name} (Priority: {handler.Priority})");
            }
        }

        /// <summary>
        /// 이벤트 처리기 제거
        /// </summary>
        public void UnregisterHandler<T>() where T : ISafetyEventHandler
        {
            lock (_handlersLock)
            {
                var handler = _handlers.FirstOrDefault(h => h is T);
                if (handler != null)
                {
                    _handlers.Remove(handler);
                    System.Diagnostics.Debug.WriteLine($"SafetyEventHandlerManager: Unregistered handler - {handler.Name}");
                }
            }
        }

        /// <summary>
        /// 모든 등록된 핸들러로 이벤트 처리
        /// </summary>
        public async Task HandleEventAsync(SafetyEventContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            List<ISafetyEventHandler> activeHandlers;
            lock (_handlersLock)
            {
                activeHandlers = _handlers
                    .Where(h => h.IsEnabled && h.CanHandle(context))
                    .ToList();
            }

            // 우선순위 순서대로 순차 실행 (파일 저장 완료 후 DB 저장)
            foreach (var handler in activeHandlers)
            {
                try
                {
                    var startTime = DateTime.Now;
                    
                    System.Diagnostics.Debug.WriteLine($"SafetyEventHandlerManager: Starting handler {handler.Name} (Priority: {handler.Priority})");
                    
                    await handler.HandleAsync(context);
                    
                    var elapsed = DateTime.Now - startTime;
                    System.Diagnostics.Debug.WriteLine($"SafetyEventHandlerManager: Handler {handler.Name} completed in {elapsed.TotalMilliseconds:F1}ms");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SafetyEventHandlerManager: Handler {handler.Name} failed - {ex.Message}");
                    // 에러가 발생해도 다음 핸들러 계속 실행
                }
            }
        }

        /// <summary>
        /// 등록된 핸들러 목록 조회
        /// </summary>
        public IReadOnlyList<ISafetyEventHandler> GetHandlers()
        {
            lock (_handlersLock)
            {
                return _handlers.AsReadOnly();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            lock (_handlersLock)
            {
                foreach (var handler in _handlers.OfType<IDisposable>())
                {
                    try
                    {
                        handler.Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"SafetyEventHandlerManager: Error disposing handler - {ex.Message}");
                    }
                }
                _handlers.Clear();
            }

            _disposed = true;
            System.Diagnostics.Debug.WriteLine("SafetyEventHandlerManager: Disposed");
        }
    }

    /// <summary>
    /// 기본 이벤트 처리기 베이스 클래스
    /// </summary>
    public abstract class BaseSafetyEventHandler : ISafetyEventHandler
    {
        public abstract string Name { get; }
        public virtual bool IsEnabled { get; set; } = true;
        public abstract int Priority { get; }

        public abstract Task HandleAsync(SafetyEventContext context);

        public virtual bool CanHandle(SafetyEventContext context)
        {
            return true; // 기본적으로 모든 이벤트 처리 가능
        }
    }
}