using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 성능 모니터링 클래스 - 프레임 처리 및 AI 추론 성능 추적
    /// </summary>
    public class PerformanceMonitor : IDisposable
    {
        private readonly ConcurrentDictionary<string, PerformanceCounter> _frameCounters = new();
        private readonly ConcurrentDictionary<string, PerformanceCounter> _aiCounters = new();
        private readonly Timer _reportTimer;
        private bool _disposed = false;

        // 성능 지표
        public double OverallFrameRate => _frameCounters.Values.Sum(c => c.CurrentFps);
        public double OverallAIFps => _aiCounters.Values.Sum(c => c.CurrentFps);
        public TimeSpan AverageProcessingTime { get; private set; }
        
        public event EventHandler<PerformanceReport>? PerformanceReported;

        public PerformanceMonitor()
        {
            // 5초마다 성능 보고
            _reportTimer = new Timer(ReportPerformance, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// 프레임 수신 기록
        /// </summary>
        public void RecordFrame(string cameraId)
        {
            var counter = _frameCounters.GetOrAdd(cameraId, _ => new PerformanceCounter());
            counter.RecordEvent();
        }

        /// <summary>
        /// AI 처리 완료 기록
        /// </summary>
        public void RecordAIProcessing(string cameraId, TimeSpan processingTime)
        {
            var counter = _aiCounters.GetOrAdd(cameraId, _ => new PerformanceCounter());
            counter.RecordEvent(processingTime);
        }

        /// <summary>
        /// 성능 보고서 생성
        /// </summary>
        private void ReportPerformance(object? state)
        {
            if (_disposed) return;

            try
            {
                var report = new PerformanceReport
                {
                    Timestamp = DateTime.Now,
                    TotalFrameRate = OverallFrameRate,
                    TotalAIFps = OverallAIFps,
                    CameraPerformance = new Dictionary<string, CameraPerformance>()
                };

                // 카메라별 성능 수집
                foreach (var cameraId in _frameCounters.Keys)
                {
                    var frameCounter = _frameCounters[cameraId];
                    var aiCounter = _aiCounters.GetValueOrDefault(cameraId, new PerformanceCounter());

                    report.CameraPerformance[cameraId] = new CameraPerformance
                    {
                        CameraId = cameraId,
                        FrameRate = frameCounter.CurrentFps,
                        AIProcessingRate = aiCounter.CurrentFps,
                        AverageProcessingTime = aiCounter.AverageProcessingTime,
                        DroppedFrames = frameCounter.DroppedCount
                    };
                }

                PerformanceReported?.Invoke(this, report);

                // 콘솔 출력 (디버그용)
                Debug.WriteLine($"[Performance] Total FPS: {report.TotalFrameRate:F1}, AI FPS: {report.TotalAIFps:F1}");
                foreach (var kvp in report.CameraPerformance)
                {
                    var perf = kvp.Value;
                    Debug.WriteLine($"  {kvp.Key}: Frame={perf.FrameRate:F1}fps, AI={perf.AIProcessingRate:F1}fps, " +
                                  $"Proc={perf.AverageProcessingTime.TotalMilliseconds:F1}ms");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Performance monitoring error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _reportTimer?.Dispose();
            
            foreach (var counter in _frameCounters.Values)
            {
                counter.Dispose();
            }
            foreach (var counter in _aiCounters.Values)
            {
                counter.Dispose();
            }
            
            _frameCounters.Clear();
            _aiCounters.Clear();
        }
    }

    /// <summary>
    /// 개별 성능 카운터
    /// </summary>
    public class PerformanceCounter : IDisposable
    {
        private readonly object _lock = new();
        private readonly Queue<DateTime> _events = new();
        private readonly Queue<TimeSpan> _processingTimes = new();
        private long _totalEvents = 0;
        private long _droppedCount = 0;

        public double CurrentFps 
        { 
            get 
            {
                lock (_lock)
                {
                    var now = DateTime.Now;
                    var oneSecondAgo = now.AddSeconds(-1);
                    
                    // 1초 이내의 이벤트만 계산
                    while (_events.Count > 0 && _events.Peek() < oneSecondAgo)
                    {
                        _events.Dequeue();
                    }
                    
                    return _events.Count;
                }
            }
        }

        public TimeSpan AverageProcessingTime
        {
            get
            {
                lock (_lock)
                {
                    if (_processingTimes.Count == 0) return TimeSpan.Zero;
                    
                    var totalMs = _processingTimes.Sum(t => t.TotalMilliseconds);
                    return TimeSpan.FromMilliseconds(totalMs / _processingTimes.Count);
                }
            }
        }

        public long TotalEvents => _totalEvents;
        public long DroppedCount => _droppedCount;

        public void RecordEvent(TimeSpan? processingTime = null)
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                _events.Enqueue(now);
                Interlocked.Increment(ref _totalEvents);
                
                if (processingTime.HasValue)
                {
                    _processingTimes.Enqueue(processingTime.Value);
                    
                    // 최근 100개의 처리 시간만 유지
                    while (_processingTimes.Count > 100)
                    {
                        _processingTimes.Dequeue();
                    }
                }
                
                // 최근 10초간의 이벤트만 유지
                var tenSecondsAgo = now.AddSeconds(-10);
                while (_events.Count > 0 && _events.Peek() < tenSecondsAgo)
                {
                    _events.Dequeue();
                }
            }
        }

        public void RecordDroppedFrame()
        {
            Interlocked.Increment(ref _droppedCount);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _events.Clear();
                _processingTimes.Clear();
            }
        }
    }

    /// <summary>
    /// 성능 보고서
    /// </summary>
    public class PerformanceReport
    {
        public DateTime Timestamp { get; set; }
        public double TotalFrameRate { get; set; }
        public double TotalAIFps { get; set; }
        public Dictionary<string, CameraPerformance> CameraPerformance { get; set; } = new();
    }

    /// <summary>
    /// 카메라별 성능 지표
    /// </summary>
    public class CameraPerformance
    {
        public string CameraId { get; set; } = string.Empty;
        public double FrameRate { get; set; }
        public double AIProcessingRate { get; set; }
        public TimeSpan AverageProcessingTime { get; set; }
        public long DroppedFrames { get; set; }
    }
}