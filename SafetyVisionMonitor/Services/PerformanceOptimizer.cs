using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 성능 최적화 관리자 - 로그 분석 기반 동적 최적화
    /// </summary>
    public class PerformanceOptimizer : IDisposable
    {
        private readonly ConcurrentDictionary<string, CameraOptimizationState> _cameraStates = new();
        private readonly Timer _optimizationTimer;
        private bool _disposed = false;

        // 최적화 설정값
        private int _maxQueueSize = 2; // 대폭 축소 (기존 3 → 2)
        private int _maxConcurrentProcessing = 1; // CPU 부하 줄임
        private TimeSpan _levelStabilizationTime = TimeSpan.FromSeconds(3); // 레벨 변경 안정화
        private double _motionThreshold = 300; // 모션 감지 임계값 낮춤

        public event EventHandler<OptimizationRecommendation>? OptimizationApplied;

        public PerformanceOptimizer()
        {
            // 5초마다 성능 분석 및 최적화
            _optimizationTimer = new Timer(AnalyzeAndOptimize, null, 
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
                
            Debug.WriteLine("PerformanceOptimizer: 실시간 성능 최적화 시작");
        }

        /// <summary>
        /// 로그 기반 성능 분석 및 자동 최적화
        /// </summary>
        private void AnalyzeAndOptimize(object? state)
        {
            try
            {
                var recommendation = new OptimizationRecommendation();

                foreach (var kvp in _cameraStates)
                {
                    var cameraId = kvp.Key;
                    var state_cam = kvp.Value;
                    
                    // 1. 프레임 드롭률 분석
                    var dropRate = state_cam.GetFrameDropRate();
                    if (dropRate > 50) // 50% 이상 드롭
                    {
                        recommendation.Actions.Add($"🚨 {cameraId}: 프레임 드롭 {dropRate:F1}% - 큐 크기 축소 권장");
                        _maxQueueSize = Math.Max(1, _maxQueueSize - 1);
                    }

                    // 2. AI 처리 레벨 안정화
                    if (state_cam.IsLevelFluctuating())
                    {
                        recommendation.Actions.Add($"⚡ {cameraId}: AI 레벨 불안정 - 안정화 시간 연장");
                        _levelStabilizationTime = TimeSpan.FromSeconds(5);
                    }

                    // 3. 모션 감지 효율성 체크
                    var motionEfficiency = state_cam.GetMotionDetectionEfficiency();
                    if (motionEfficiency < 60) // 60% 미만 효율
                    {
                        recommendation.Actions.Add($"🎯 {cameraId}: 모션 감지 효율 {motionEfficiency:F1}% - 임계값 조정");
                        _motionThreshold *= 0.9; // 10% 민감도 증가
                    }
                }

                // 4. 전역 성능 최적화
                ApplyGlobalOptimizations(recommendation);

                if (recommendation.Actions.Count > 0)
                {
                    OptimizationApplied?.Invoke(this, recommendation);
                    Debug.WriteLine($"PerformanceOptimizer: {recommendation.Actions.Count}개 최적화 적용");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PerformanceOptimizer error: {ex.Message}");
            }
        }

        /// <summary>
        /// 전역 성능 최적화 적용
        /// </summary>
        private void ApplyGlobalOptimizations(OptimizationRecommendation recommendation)
        {
            // CPU 사용률 기반 동시 처리 수 조정
            var cpuUsage = GetCurrentCpuUsage();
            if (cpuUsage > 80)
            {
                _maxConcurrentProcessing = 1;
                recommendation.Actions.Add("🔥 CPU 과부하 - 동시 처리 수 1로 제한");
            }
            else if (cpuUsage < 40)
            {
                _maxConcurrentProcessing = Math.Min(2, _maxConcurrentProcessing + 1);
                recommendation.Actions.Add("💪 CPU 여유 - 동시 처리 수 증가");
            }

            // 메모리 사용률 체크
            var memoryUsage = GC.GetTotalMemory(false) / 1024 / 1024; // MB
            if (memoryUsage > 1000) // 1GB 이상
            {
                recommendation.Actions.Add($"🧠 메모리 사용량 {memoryUsage}MB - GC 강제 실행");
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        /// <summary>
        /// 카메라 상태 업데이트 (스마트 AI에서 호출)
        /// </summary>
        public void UpdateCameraState(string cameraId, SmartDetectionResult result)
        {
            var state = _cameraStates.GetOrAdd(cameraId, _ => new CameraOptimizationState(cameraId));
            state.UpdateState(result);
        }

        /// <summary>
        /// 현재 최적화된 설정값 반환
        /// </summary>
        public PerformanceSettings GetOptimizedSettings()
        {
            return new PerformanceSettings
            {
                MaxQueueSize = _maxQueueSize,
                MaxConcurrentProcessing = _maxConcurrentProcessing,
                LevelStabilizationTime = _levelStabilizationTime,
                MotionThreshold = _motionThreshold
            };
        }

        private double GetCurrentCpuUsage()
        {
            try
            {
                var startTime = DateTime.UtcNow;
                var startCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
                Thread.Sleep(100); // 짧은 대기
                var endTime = DateTime.UtcNow;
                var endCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
                
                var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
                
                return cpuUsageTotal * 100;
            }
            catch
            {
                return 50; // 기본값
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _optimizationTimer?.Dispose();
            _cameraStates.Clear();
        }
    }

    /// <summary>
    /// 카메라별 최적화 상태
    /// </summary>
    internal class CameraOptimizationState
    {
        public string CameraId { get; }
        private readonly Queue<SmartDetectionResult> _recentResults = new();
        private DateTime _lastLevelChange = DateTime.MinValue;
        private SmartAIProcessingService.ProcessingLevel _lastLevel = SmartAIProcessingService.ProcessingLevel.None;

        public CameraOptimizationState(string cameraId)
        {
            CameraId = cameraId;
        }

        public void UpdateState(SmartDetectionResult result)
        {
            lock (_recentResults)
            {
                _recentResults.Enqueue(result);
                
                // 최근 20개 결과만 유지
                while (_recentResults.Count > 20)
                    _recentResults.Dequeue();

                // 레벨 변경 추적
                if (_lastLevel != result.ProcessingLevel)
                {
                    _lastLevelChange = DateTime.Now;
                    _lastLevel = result.ProcessingLevel;
                }
            }
        }

        public double GetFrameDropRate()
        {
            lock (_recentResults)
            {
                if (_recentResults.Count < 5) return 0;
                
                // 최근 결과에서 드롭률 추정
                var recentArray = _recentResults.ToArray();
                var avgProcessingTime = recentArray.Average(r => r.ProcessingTimeMs);
                
                // 처리 시간이 33ms(30fps) 이상이면 드롭 가능성 높음
                return Math.Min(100, Math.Max(0, (avgProcessingTime - 33) / 33 * 100));
            }
        }

        public bool IsLevelFluctuating()
        {
            // 최근 3초 이내에 레벨 변경이 있었으면 불안정
            return DateTime.Now - _lastLevelChange < TimeSpan.FromSeconds(3);
        }

        public double GetMotionDetectionEfficiency()
        {
            lock (_recentResults)
            {
                if (_recentResults.Count < 5) return 100;
                
                var recentArray = _recentResults.ToArray();
                var motionDetectedCount = recentArray.Count(r => r.MotionDetected);
                
                return (double)motionDetectedCount / recentArray.Length * 100;
            }
        }
    }

    /// <summary>
    /// 최적화 권장사항
    /// </summary>
    public class OptimizationRecommendation
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public List<string> Actions { get; set; } = new();
        public string Summary => $"{Actions.Count}개 최적화 적용 @ {Timestamp:HH:mm:ss}";
    }

    /// <summary>
    /// 성능 설정값
    /// </summary>
    public class PerformanceSettings
    {
        public int MaxQueueSize { get; set; }
        public int MaxConcurrentProcessing { get; set; }
        public TimeSpan LevelStabilizationTime { get; set; }
        public double MotionThreshold { get; set; }
    }
}