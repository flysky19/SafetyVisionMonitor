using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using OpenCvSharp;
using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 향상된 AI 처리 파이프라인 - 스마트 선택적 처리로 성능 최적화
    /// </summary>
    public class EnhancedAIProcessingPipeline : IDisposable
    {
        private readonly SmartAIProcessingService _smartAIService;
        private readonly ActionBlock<SmartFrameTask> _processingBlock;
        private readonly SemaphoreSlim _processingLimiter;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly ConcurrentDictionary<string, FrameSkipCounter> _frameSkipCounters;
        private readonly Timer _performanceReportTimer;
        
        private bool _disposed = false;
        private long _totalFramesQueued = 0;
        private long _totalFramesProcessed = 0;
        private long _totalFramesDropped = 0;
        private long _totalMotionDetections = 0;
        private long _totalAIProcessings = 0;
        
        // 성능 최적화 설정 (버벅임 해결)
        private const int MaxConcurrentProcessing = 1; // 안정성을 위해 1로 복원
        private const int ProcessEveryNthFrame = 1; // 모든 프레임 처리
        private const int MaxQueueSize = 3; // 큐 크기 증가로 드랍 감소
        
        // 이벤트
        public event EventHandler<SmartObjectDetectionEventArgs>? ObjectDetected;
        public event EventHandler<SmartPipelinePerformanceEventArgs>? PerformanceUpdated;
        
        // 속성
        public long TotalFramesQueued => _totalFramesQueued;
        public long TotalFramesProcessed => _totalFramesProcessed;
        public long TotalFramesDropped => _totalFramesDropped;
        public long TotalMotionDetections => _totalMotionDetections;
        public long TotalAIProcessings => _totalAIProcessings;
        public bool IsProcessing => !_processingBlock.Completion.IsCompleted;

        public EnhancedAIProcessingPipeline(AIInferenceService aiService)
        {
            _smartAIService = new SmartAIProcessingService(aiService);
            _cancellationTokenSource = new CancellationTokenSource();
            _processingLimiter = new SemaphoreSlim(MaxConcurrentProcessing, MaxConcurrentProcessing);
            _frameSkipCounters = new ConcurrentDictionary<string, FrameSkipCounter>();
            
            // 스마트 AI 서비스 이벤트 구독
            _smartAIService.SmartDetectionCompleted += OnSmartDetectionCompleted;
            
            // 처리 블록 설정 - 스마트 처리용
            var options = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = MaxConcurrentProcessing,
                BoundedCapacity = MaxQueueSize,
                CancellationToken = _cancellationTokenSource.Token
            };
            
            _processingBlock = new ActionBlock<SmartFrameTask>(ProcessFrameTaskAsync, options);
            
            // 성능 보고 타이머 (10초마다)
            _performanceReportTimer = new Timer(ReportPerformance, null, 
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
            
            Debug.WriteLine("EnhancedAIProcessingPipeline: 스마트 AI 파이프라인 초기화 완료");
        }

        /// <summary>
        /// 프레임을 스마트 처리 큐에 추가
        /// </summary>
        public async Task<bool> EnqueueFrameAsync(string cameraId, Mat frame)
        {
            if (_disposed || frame == null || frame.Empty())
            {
                return false;
            }

            // 프레임 스키핑 체크 (기존 AIProcessingPipeline 로직 사용)
            var skipCounter = _frameSkipCounters.GetOrAdd(cameraId, _ => new FrameSkipCounter());
            skipCounter.TotalFrames++;
            
            // 매 프레임 처리 (스마트 시스템에서 자체적으로 최적화)
            if (skipCounter.TotalFrames % ProcessEveryNthFrame != 0)
            {
                return false;
            }
            
            skipCounter.ProcessedFrames++;

            var task = new SmartFrameTask
            {
                CameraId = cameraId,
                Frame = frame.Clone(), // 복사본 생성
                Timestamp = DateTime.Now
            };

            Interlocked.Increment(ref _totalFramesQueued);
            
            // 큐가 가득 찬 경우 가장 오래된 프레임 드롭 (비블로킹 방식)
            if (!_processingBlock.Post(task))
            {
                task.Frame.Dispose();
                Interlocked.Increment(ref _totalFramesDropped);
                Debug.WriteLine($"EnhancedAI: Frame dropped for {cameraId} - queue full");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 기존 호환성을 위한 QueueFrame 메서드 (동기 버전)
        /// </summary>
        public bool QueueFrame(string cameraId, Mat frame)
        {
            // 비동기 메서드를 동기적으로 호출
            return EnqueueFrameAsync(cameraId, frame).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 스마트 프레임 처리 태스크
        /// </summary>
        private async Task ProcessFrameTaskAsync(SmartFrameTask task)
        {
            await _processingLimiter.WaitAsync(_cancellationTokenSource.Token);
            
            try
            {
                if (_disposed || task.Frame.Empty())
                {
                    return;
                }

                var processingStart = DateTime.Now;
                
                // 스마트 AI 처리 실행
                var result = await _smartAIService.ProcessFrameSmartAsync(task.CameraId, task.Frame);
                
                var processingTime = DateTime.Now - processingStart;
                Interlocked.Increment(ref _totalFramesProcessed);
                
                // 통계 업데이트
                if (result.MotionDetected)
                {
                    Interlocked.Increment(ref _totalMotionDetections);
                }
                
                if (result.ProcessingLevel > SmartAIProcessingService.ProcessingLevel.MotionOnly)
                {
                    Interlocked.Increment(ref _totalAIProcessings);
                }

                // 객체 검출 이벤트 발생 (검출 결과가 있을 때만)
                if (result.Detections.Length > 0)
                {
                    var eventArgs = new SmartObjectDetectionEventArgs
                    {
                        CameraId = task.CameraId,
                        Detections = result.Detections,
                        ProcessingLevel = result.ProcessingLevel,
                        MotionDetected = result.MotionDetected,
                        HasPersons = result.HasPersons,
                        ProcessingTime = processingTime,
                        QueueDelay = processingStart - task.Timestamp
                    };
                    
                    ObjectDetected?.Invoke(this, eventArgs);
                }

                // 디버그 로깅 (간소화)
                if (_totalFramesProcessed % 50 == 0) // 50프레임마다
                {
                    Debug.WriteLine($"EnhancedAI [{task.CameraId}]: Level={result.ProcessingLevel}, " +
                                  $"Motion={result.MotionDetected}, Persons={result.HasPersons}, " +
                                  $"Processing={result.ProcessingTimeMs}ms");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnhancedAI processing error [{task.CameraId}]: {ex.Message}");
            }
            finally
            {
                task.Frame?.Dispose();
                _processingLimiter.Release();
            }
        }

        /// <summary>
        /// 스마트 검출 완료 이벤트 핸들러
        /// </summary>
        private void OnSmartDetectionCompleted(object? sender, SmartDetectionResult result)
        {
            // 추가 통계나 이벤트 처리 필요시 여기서 구현
        }

        /// <summary>
        /// 성능 보고
        /// </summary>
        private void ReportPerformance(object? state)
        {
            if (_disposed) return;

            try
            {
                var stats = _smartAIService.GetProcessingStatistics();
                var performance = new SmartPipelinePerformanceEventArgs
                {
                    Timestamp = DateTime.Now,
                    TotalFramesQueued = _totalFramesQueued,
                    TotalFramesProcessed = _totalFramesProcessed,
                    TotalFramesDropped = _totalFramesDropped,
                    TotalMotionDetections = _totalMotionDetections,
                    TotalAIProcessings = _totalAIProcessings,
                    CameraStatistics = stats
                };

                PerformanceUpdated?.Invoke(this, performance);

                // 콘솔 성능 요약
                var motionRate = _totalFramesProcessed > 0 ? 
                    (double)_totalMotionDetections / _totalFramesProcessed * 100 : 0;
                var aiRate = _totalFramesProcessed > 0 ? 
                    (double)_totalAIProcessings / _totalFramesProcessed * 100 : 0;
                    
                Debug.WriteLine($"EnhancedAI Performance: Processed={_totalFramesProcessed}, " +
                              $"Motion={motionRate:F1}%, AI={aiRate:F1}%, Dropped={_totalFramesDropped}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Performance reporting error: {ex.Message}");
            }
        }

        /// <summary>
        /// 파이프라인 시작
        /// </summary>
        public Task StartAsync()
        {
            Debug.WriteLine("EnhancedAIProcessingPipeline: 시작됨");
            return Task.CompletedTask;
        }

        /// <summary>
        /// 파이프라인 중지
        /// </summary>
        public async Task StopAsync()
        {
            Debug.WriteLine("EnhancedAIProcessingPipeline: 중지 중...");
            
            _processingBlock.Complete();
            await _processingBlock.Completion;
            
            Debug.WriteLine("EnhancedAIProcessingPipeline: 중지 완료");
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            
            try
            {
                _cancellationTokenSource.Cancel();
                _processingBlock.Complete();
                _processingBlock.Completion.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Dispose error: {ex.Message}");
            }
            finally
            {
                _performanceReportTimer?.Dispose();
                _processingLimiter?.Dispose();
                _cancellationTokenSource?.Dispose();
                _smartAIService?.Dispose();
            }
        }
    }

    /// <summary>
    /// 스마트 프레임 처리 태스크
    /// </summary>
    internal class SmartFrameTask
    {
        public string CameraId { get; set; } = "";
        public Mat Frame { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }


    /// <summary>
    /// 스마트 객체 검출 이벤트 인수 - ObjectDetectionEventArgs를 확장
    /// </summary>
    public class SmartObjectDetectionEventArgs : ObjectDetectionEventArgs
    {
        public SmartAIProcessingService.ProcessingLevel ProcessingLevel { get; set; }
        public bool MotionDetected { get; set; }
        public bool HasPersons { get; set; }
        public new TimeSpan ProcessingTime { get; set; } // TimeSpan으로 확장 (기존은 double)
        public TimeSpan QueueDelay { get; set; }
    }

    /// <summary>
    /// 스마트 파이프라인 성능 이벤트 인수
    /// </summary>
    public class SmartPipelinePerformanceEventArgs : EventArgs
    {
        public DateTime Timestamp { get; set; }
        public long TotalFramesQueued { get; set; }
        public long TotalFramesProcessed { get; set; }
        public long TotalFramesDropped { get; set; }
        public long TotalMotionDetections { get; set; }
        public long TotalAIProcessings { get; set; }
        public Dictionary<string, object> CameraStatistics { get; set; } = new();
    }

}