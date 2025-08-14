using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenCvSharp;
using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionMonitor.Services.Features
{
    /// <summary>
    /// 모듈러 오버레이 렌더링 파이프라인
    /// 모든 시각적 기능들을 효율적으로 관리하고 렌더링
    /// </summary>
    public class OverlayRenderingPipeline : IDisposable
    {
        private readonly FeatureManager _featureManager;
        private readonly Dictionary<string, double> _processingTimes = new();
        private readonly Dictionary<string, int> _errorCounts = new();
        private bool _disposed = false;

        // 성능 모니터링
        private readonly Stopwatch _overallStopwatch = new();
        private readonly Dictionary<string, Stopwatch> _featureStopwatches = new();

        // 이벤트
        public event EventHandler<PipelineProcessingCompletedEventArgs>? ProcessingCompleted;
        public event EventHandler<FeatureProcessingErrorEventArgs>? FeatureProcessingError;

        public OverlayRenderingPipeline(FeatureManager featureManager)
        {
            _featureManager = featureManager ?? throw new ArgumentNullException(nameof(featureManager));
            
            // 기능 상태 변경 이벤트 구독
            _featureManager.FeatureStateChanged += OnFeatureStateChanged;
            _featureManager.ConfigurationChanged += OnFeatureConfigurationChanged;
            
            System.Diagnostics.Debug.WriteLine("OverlayRenderingPipeline: Initialized");
        }

        /// <summary>
        /// 프레임에 모든 활성 기능들 적용
        /// </summary>
        public Mat ProcessFrame(Mat inputFrame, FrameProcessingContext context)
        {
            if (_disposed || inputFrame == null || inputFrame.Empty())
                return inputFrame;

            _overallStopwatch.Restart();
            
            try
            {
                // 입력 프레임 복사 (원본 보호)
                var processedFrame = inputFrame.Clone();
                
                // 활성 기능들을 우선순위에 따라 정렬
                var activeFeatures = _featureManager.GetActiveFeaturesByPriority();
                
                System.Diagnostics.Debug.WriteLine($"OverlayPipeline: Processing {activeFeatures.Count} features for camera {context.CameraId}");

                var processedFeatures = new List<string>();
                var errors = new List<FeatureProcessingError>();

                // 각 기능 순차 적용
                foreach (var feature in activeFeatures)
                {
                    try
                    {
                        if (!feature.ShouldProcess(context))
                        {
                            continue;
                        }

                        // 기능별 처리 시간 측정 시작
                        var featureStopwatch = GetOrCreateStopwatch(feature.Id);
                        featureStopwatch.Restart();

                        // 기능 적용
                        var previousFrame = processedFrame;
                        processedFrame = feature.ProcessFrame(processedFrame, context);

                        // 처리 시간 기록
                        featureStopwatch.Stop();
                        _processingTimes[feature.Id] = featureStopwatch.Elapsed.TotalMilliseconds;

                        // 메모리 관리: 중간 프레임 해제
                        if (previousFrame != inputFrame && previousFrame != processedFrame)
                        {
                            previousFrame?.Dispose();
                        }

                        processedFeatures.Add(feature.Id);
                        
                        System.Diagnostics.Debug.WriteLine(
                            $"OverlayPipeline: Applied '{feature.Name}' in {_processingTimes[feature.Id]:F2}ms");
                    }
                    catch (Exception ex)
                    {
                        // 개별 기능 오류 처리
                        _errorCounts[feature.Id] = _errorCounts.GetValueOrDefault(feature.Id, 0) + 1;
                        
                        var error = new FeatureProcessingError
                        {
                            FeatureId = feature.Id,
                            FeatureName = feature.Name,
                            ErrorMessage = ex.Message,
                            Timestamp = DateTime.Now
                        };
                        errors.Add(error);

                        // 오류 이벤트 발생
                        FeatureProcessingError?.Invoke(this, new FeatureProcessingErrorEventArgs(error));

                        System.Diagnostics.Debug.WriteLine(
                            $"OverlayPipeline: Error in feature '{feature.Name}': {ex.Message}");
                    }
                }

                _overallStopwatch.Stop();
                
                // 처리 완료 이벤트 발생
                ProcessingCompleted?.Invoke(this, new PipelineProcessingCompletedEventArgs
                {
                    CameraId = context.CameraId,
                    ProcessedFeatures = processedFeatures,
                    Errors = errors,
                    TotalProcessingTimeMs = _overallStopwatch.Elapsed.TotalMilliseconds,
                    FeatureProcessingTimes = new Dictionary<string, double>(_processingTimes)
                });

                return processedFrame;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OverlayPipeline: Critical error: {ex.Message}");
                return inputFrame; // 오류 시 원본 반환
            }
        }

        /// <summary>
        /// 비동기 프레임 처리 (고성능)
        /// </summary>
        public async System.Threading.Tasks.Task<Mat> ProcessFrameAsync(Mat inputFrame, FrameProcessingContext context)
        {
            return await System.Threading.Tasks.Task.Run(() => ProcessFrame(inputFrame, context));
        }

        /// <summary>
        /// 특정 기능만 적용
        /// </summary>
        public Mat ProcessFrameWithSpecificFeatures(Mat inputFrame, FrameProcessingContext context, params string[] featureIds)
        {
            if (_disposed || inputFrame == null || inputFrame.Empty())
                return inputFrame;

            var processedFrame = inputFrame.Clone();
            
            foreach (var featureId in featureIds)
            {
                var feature = _featureManager.GetAllFeatures().FirstOrDefault(f => f.Id == featureId);
                if (feature != null && feature.IsEnabled && feature.ShouldProcess(context))
                {
                    try
                    {
                        var previousFrame = processedFrame;
                        processedFrame = feature.ProcessFrame(processedFrame, context);
                        
                        if (previousFrame != inputFrame && previousFrame != processedFrame)
                        {
                            previousFrame?.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"OverlayPipeline: Error in specific feature '{feature.Name}': {ex.Message}");
                    }
                }
            }

            return processedFrame;
        }

        /// <summary>
        /// 기능별 성능 통계 조회
        /// </summary>
        public Dictionary<string, FeaturePerformanceStats> GetPerformanceStats()
        {
            var stats = new Dictionary<string, FeaturePerformanceStats>();
            
            foreach (var feature in _featureManager.GetAllFeatures())
            {
                stats[feature.Id] = new FeaturePerformanceStats
                {
                    FeatureId = feature.Id,
                    FeatureName = feature.Name,
                    IsEnabled = feature.IsEnabled,
                    AverageProcessingTimeMs = _processingTimes.GetValueOrDefault(feature.Id, 0),
                    ErrorCount = _errorCounts.GetValueOrDefault(feature.Id, 0),
                    LastProcessedTime = DateTime.Now
                };
            }

            return stats;
        }

        /// <summary>
        /// 파이프라인 성능 최적화
        /// </summary>
        public void OptimizePerformance()
        {
            try
            {
                // 처리 시간이 긴 기능들 식별
                var slowFeatures = _processingTimes
                    .Where(kvp => kvp.Value > 10.0) // 10ms 이상
                    .OrderByDescending(kvp => kvp.Value)
                    .ToList();

                foreach (var (featureId, processingTime) in slowFeatures)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"OverlayPipeline: Slow feature detected - {featureId}: {processingTime:F2}ms");
                    
                    // TODO: 자동 최적화 로직 (예: 프레임 스키핑, 해상도 조정 등)
                }

                // 오류가 많은 기능들 식별
                var errorProneFeatures = _errorCounts
                    .Where(kvp => kvp.Value > 5)
                    .ToList();

                foreach (var (featureId, errorCount) in errorProneFeatures)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"OverlayPipeline: Error-prone feature detected - {featureId}: {errorCount} errors");
                    
                    // TODO: 자동 비활성화 또는 안전 모드 전환
                }

                System.Diagnostics.Debug.WriteLine("OverlayPipeline: Performance optimization completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OverlayPipeline: Optimization error: {ex.Message}");
            }
        }

        private Stopwatch GetOrCreateStopwatch(string featureId)
        {
            if (!_featureStopwatches.TryGetValue(featureId, out var stopwatch))
            {
                stopwatch = new Stopwatch();
                _featureStopwatches[featureId] = stopwatch;
            }
            return stopwatch;
        }

        private void OnFeatureStateChanged(object? sender, FeatureStateChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(
                $"OverlayPipeline: Feature '{e.FeatureName}' state changed: {e.OldEnabled} → {e.NewEnabled}");
            
            // 기능이 비활성화된 경우 성능 통계 초기화
            if (!e.NewEnabled)
            {
                _processingTimes.Remove(e.FeatureId);
                _errorCounts.Remove(e.FeatureId);
                _featureStopwatches.Remove(e.FeatureId);
            }
        }

        private void OnFeatureConfigurationChanged(object? sender, FeatureConfigurationChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(
                $"OverlayPipeline: Feature '{e.FeatureName}' configuration updated");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _featureManager.FeatureStateChanged -= OnFeatureStateChanged;
            _featureManager.ConfigurationChanged -= OnFeatureConfigurationChanged;
            
            _overallStopwatch?.Stop();
            
            foreach (var stopwatch in _featureStopwatches.Values)
            {
                stopwatch?.Stop();
            }
            
            _featureStopwatches.Clear();
            _processingTimes.Clear();
            _errorCounts.Clear();
            
            System.Diagnostics.Debug.WriteLine("OverlayRenderingPipeline: Disposed");
        }
    }

    // 이벤트 및 데이터 클래스들
    public class PipelineProcessingCompletedEventArgs : EventArgs
    {
        public string CameraId { get; set; } = string.Empty;
        public List<string> ProcessedFeatures { get; set; } = new();
        public List<FeatureProcessingError> Errors { get; set; } = new();
        public double TotalProcessingTimeMs { get; set; }
        public Dictionary<string, double> FeatureProcessingTimes { get; set; } = new();
    }

    public class FeatureProcessingErrorEventArgs : EventArgs
    {
        public FeatureProcessingError Error { get; }

        public FeatureProcessingErrorEventArgs(FeatureProcessingError error)
        {
            Error = error;
        }
    }

    public class FeatureProcessingError
    {
        public string FeatureId { get; set; } = string.Empty;
        public string FeatureName { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    public class FeaturePerformanceStats
    {
        public string FeatureId { get; set; } = string.Empty;
        public string FeatureName { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public double AverageProcessingTimeMs { get; set; }
        public int ErrorCount { get; set; }
        public DateTime LastProcessedTime { get; set; }
    }
}