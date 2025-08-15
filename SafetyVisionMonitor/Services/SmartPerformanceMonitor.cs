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
    /// 스마트 성능 모니터링 - 선택적 AI 처리에 특화된 모니터링
    /// </summary>
    public class SmartPerformanceMonitor : IDisposable
    {
        private readonly ConcurrentDictionary<string, SmartCameraMetrics> _cameraMetrics = new();
        private readonly Timer _reportTimer;
        private bool _disposed = false;

        public event EventHandler<SmartPerformanceReport>? PerformanceReported;

        public SmartPerformanceMonitor()
        {
            // 5초마다 성능 보고
            _reportTimer = new Timer(ReportPerformance, null, 
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
                
            Debug.WriteLine("SmartPerformanceMonitor: 초기화 완료");
        }

        /// <summary>
        /// 프레임 수신 기록
        /// </summary>
        public void RecordFrame(string cameraId)
        {
            var metrics = _cameraMetrics.GetOrAdd(cameraId, _ => new SmartCameraMetrics(cameraId));
            metrics.RecordFrame();
        }

        /// <summary>
        /// 모션 감지 기록
        /// </summary>
        public void RecordMotionDetection(string cameraId)
        {
            var metrics = _cameraMetrics.GetOrAdd(cameraId, _ => new SmartCameraMetrics(cameraId));
            metrics.RecordMotion();
        }

        /// <summary>
        /// AI 처리 기록
        /// </summary>
        public void RecordAIProcessing(string cameraId, SmartAIProcessingService.ProcessingLevel level, TimeSpan processingTime)
        {
            var metrics = _cameraMetrics.GetOrAdd(cameraId, _ => new SmartCameraMetrics(cameraId));
            metrics.RecordAIProcessing(level, processingTime);
        }

        /// <summary>
        /// 사람 감지 기록
        /// </summary>
        public void RecordPersonDetection(string cameraId, int personCount)
        {
            var metrics = _cameraMetrics.GetOrAdd(cameraId, _ => new SmartCameraMetrics(cameraId));
            metrics.RecordPersons(personCount);
        }

        /// <summary>
        /// 성능 보고서 생성
        /// </summary>
        private void ReportPerformance(object? state)
        {
            if (_disposed) return;

            try
            {
                var report = new SmartPerformanceReport
                {
                    Timestamp = DateTime.Now,
                    CameraMetrics = new Dictionary<string, SmartCameraPerformance>()
                };

                double totalFrameRate = 0;
                double totalMotionRate = 0;
                double totalAIEfficiency = 0;
                int activeCameras = 0;

                // 카메라별 성능 수집
                foreach (var kvp in _cameraMetrics)
                {
                    var metrics = kvp.Value;
                    var performance = metrics.GetPerformance();
                    
                    if (performance.FrameRate > 0)
                    {
                        report.CameraMetrics[kvp.Key] = performance;
                        totalFrameRate += performance.FrameRate;
                        totalMotionRate += performance.MotionDetectionRate;
                        totalAIEfficiency += performance.AIEfficiency;
                        activeCameras++;
                    }
                }

                // 전체 통계
                if (activeCameras > 0)
                {
                    report.OverallFrameRate = totalFrameRate;
                    report.AverageMotionRate = totalMotionRate / activeCameras;
                    report.AverageAIEfficiency = totalAIEfficiency / activeCameras;
                }

                PerformanceReported?.Invoke(this, report);

                // 콘솔 출력 (요약)
                if (activeCameras > 0)
                {
                    Debug.WriteLine($"[SmartPerf] 카메라: {activeCameras}, 총 FPS: {totalFrameRate:F1}, " +
                                  $"평균 모션: {report.AverageMotionRate:F1}%, AI 효율: {report.AverageAIEfficiency:F1}%");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SmartPerformanceMonitor error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            _reportTimer?.Dispose();
            
            foreach (var metrics in _cameraMetrics.Values)
            {
                metrics.Dispose();
            }
            _cameraMetrics.Clear();
        }
    }

    /// <summary>
    /// 카메라별 스마트 성능 지표
    /// </summary>
    internal class SmartCameraMetrics : IDisposable
    {
        private readonly object _lock = new();
        private readonly Queue<DateTime> _frameTimestamps = new();
        private readonly Queue<DateTime> _motionTimestamps = new();
        private readonly Queue<AIProcessingRecord> _aiProcessingRecords = new();
        
        public string CameraId { get; }
        public long TotalFrames { get; private set; }
        public long TotalMotions { get; private set; }
        public long TotalAIProcessings { get; private set; }
        public int CurrentPersons { get; private set; }

        public SmartCameraMetrics(string cameraId)
        {
            CameraId = cameraId;
        }

        public void RecordFrame()
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                _frameTimestamps.Enqueue(now);
                TotalFrames++;
                
                // 최근 10초간의 데이터만 유지
                CleanOldRecords(now);
            }
        }

        public void RecordMotion()
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                _motionTimestamps.Enqueue(now);
                TotalMotions++;
            }
        }

        public void RecordAIProcessing(SmartAIProcessingService.ProcessingLevel level, TimeSpan processingTime)
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                _aiProcessingRecords.Enqueue(new AIProcessingRecord 
                { 
                    Timestamp = now, 
                    Level = level, 
                    ProcessingTime = processingTime 
                });
                TotalAIProcessings++;
            }
        }

        public void RecordPersons(int count)
        {
            CurrentPersons = count;
        }

        public SmartCameraPerformance GetPerformance()
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                CleanOldRecords(now);

                var frameRate = _frameTimestamps.Count; // 최근 1초간 프레임 수
                var motionRate = _frameTimestamps.Count > 0 ? 
                    (double)_motionTimestamps.Count / _frameTimestamps.Count * 100 : 0;
                
                var avgProcessingTime = _aiProcessingRecords.Any() ? 
                    TimeSpan.FromMilliseconds(_aiProcessingRecords.Average(r => r.ProcessingTime.TotalMilliseconds)) : 
                    TimeSpan.Zero;
                
                var aiEfficiency = _frameTimestamps.Count > 0 ? 
                    (double)_aiProcessingRecords.Count / _frameTimestamps.Count * 100 : 0;

                return new SmartCameraPerformance
                {
                    CameraId = CameraId,
                    FrameRate = frameRate,
                    MotionDetectionRate = motionRate,
                    AIProcessingRate = _aiProcessingRecords.Count,
                    AverageProcessingTime = avgProcessingTime,
                    AIEfficiency = aiEfficiency,
                    CurrentPersons = CurrentPersons,
                    TotalFramesProcessed = TotalFrames,
                    TotalMotionsDetected = TotalMotions,
                    TotalAIProcessings = TotalAIProcessings
                };
            }
        }

        private void CleanOldRecords(DateTime now)
        {
            var oneSecondAgo = now.AddSeconds(-1);
            
            while (_frameTimestamps.Count > 0 && _frameTimestamps.Peek() < oneSecondAgo)
                _frameTimestamps.Dequeue();
                
            while (_motionTimestamps.Count > 0 && _motionTimestamps.Peek() < oneSecondAgo)
                _motionTimestamps.Dequeue();
                
            while (_aiProcessingRecords.Count > 0 && _aiProcessingRecords.Peek().Timestamp < oneSecondAgo)
                _aiProcessingRecords.Dequeue();
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _frameTimestamps.Clear();
                _motionTimestamps.Clear();
                _aiProcessingRecords.Clear();
            }
        }
    }

    /// <summary>
    /// AI 처리 기록
    /// </summary>
    internal struct AIProcessingRecord
    {
        public DateTime Timestamp { get; set; }
        public SmartAIProcessingService.ProcessingLevel Level { get; set; }
        public TimeSpan ProcessingTime { get; set; }
    }

    /// <summary>
    /// 스마트 성능 보고서
    /// </summary>
    public class SmartPerformanceReport
    {
        public DateTime Timestamp { get; set; }
        public double OverallFrameRate { get; set; }
        public double AverageMotionRate { get; set; }
        public double AverageAIEfficiency { get; set; }
        public Dictionary<string, SmartCameraPerformance> CameraMetrics { get; set; } = new();
    }

    /// <summary>
    /// 카메라별 스마트 성능 지표
    /// </summary>
    public class SmartCameraPerformance
    {
        public string CameraId { get; set; } = "";
        public double FrameRate { get; set; }
        public double MotionDetectionRate { get; set; }
        public double AIProcessingRate { get; set; }
        public TimeSpan AverageProcessingTime { get; set; }
        public double AIEfficiency { get; set; }
        public int CurrentPersons { get; set; }
        public long TotalFramesProcessed { get; set; }
        public long TotalMotionsDetected { get; set; }
        public long TotalAIProcessings { get; set; }
    }
}