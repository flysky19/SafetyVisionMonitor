using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using OpenCvSharp;
using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// AI 처리 파이프라인 - 실시간 프레임 처리를 위한 큐 기반 시스템
    /// </summary>
    public class AIProcessingPipeline : IDisposable
    {
        private readonly AIInferenceService _aiService;
        private readonly ActionBlock<FrameTask> _processingBlock;
        private readonly SemaphoreSlim _processingLimiter;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly ConcurrentDictionary<string, FrameSkipCounter> _frameSkipCounters;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _cameraLimiters;
        
        private bool _disposed = false;
        private long _totalFramesQueued = 0;
        private long _totalFramesProcessed = 0;
        private long _totalFramesDropped = 0;
        
        // 설정 - 성능 최적화 (카메라별 병렬 처리)
        private const int MaxConcurrentProcessing = 4; // 카메라 수만큼 병렬 처리
        private const int ProcessEveryNthFrame = 2; // 프레임 스키핑 줄임 (더 부드러운 영상)
        private const int MaxQueueSize = 8; // 큐 크기 증가로 드랍 감소
        
        // 이벤트
        public event EventHandler<ObjectDetectionEventArgs>? ObjectDetected;
        public event EventHandler<PipelinePerformanceEventArgs>? PerformanceUpdated;
        
        // 속성
        public long TotalFramesQueued => _totalFramesQueued;
        public long TotalFramesProcessed => _totalFramesProcessed;
        public long TotalFramesDropped => _totalFramesDropped;
        public bool IsProcessing => !_processingBlock.Completion.IsCompleted;
        
        public AIProcessingPipeline(AIInferenceService aiService)
        {
            _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
            _cancellationTokenSource = new CancellationTokenSource();
            _processingLimiter = new SemaphoreSlim(MaxConcurrentProcessing, MaxConcurrentProcessing);
            _frameSkipCounters = new ConcurrentDictionary<string, FrameSkipCounter>();
            _cameraLimiters = new ConcurrentDictionary<string, SemaphoreSlim>();
            
            // 처리 블록 설정 (Producer-Consumer 패턴)
            var options = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = MaxConcurrentProcessing,
                BoundedCapacity = MaxQueueSize,
                CancellationToken = _cancellationTokenSource.Token
            };
            
            _processingBlock = new ActionBlock<FrameTask>(ProcessFrameTask, options);
            
            // AI 서비스 이벤트 구독
            _aiService.ObjectDetected += OnObjectDetected;
            
            System.Diagnostics.Debug.WriteLine("AIProcessingPipeline: Initialized with max concurrency = " + MaxConcurrentProcessing);
        }
        
        /// <summary>
        /// 프레임을 처리 큐에 추가
        /// </summary>
        public bool QueueFrame(string cameraId, Mat frame, int priority = 0)
        {
            if (_disposed || frame.Empty())
                return false;
            
            Interlocked.Increment(ref _totalFramesQueued);
            
            // 프레임 스키핑 체크 (성능 최적화)
            if (!ShouldProcessFrame(cameraId))
            {
                Interlocked.Increment(ref _totalFramesDropped);
                return false;
            }
            
            var frameTask = new FrameTask
            {
                CameraId = cameraId,
                Frame = frame.Clone(), // 프레임 복사 (원본 보호)
                Priority = priority,
                QueuedTime = DateTime.Now
            };
            
            // 큐가 가득 찬 경우 가장 오래된 프레임 드롭
            if (!_processingBlock.Post(frameTask))
            {
                // 큐가 가득 참 - 프레임 드롭
                frameTask.Frame.Dispose();
                Interlocked.Increment(ref _totalFramesDropped);
                
                System.Diagnostics.Debug.WriteLine($"AIProcessingPipeline: Frame dropped for camera {cameraId} - queue full");
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// 배치 프레임 처리 (여러 카메라 동시)
        /// </summary>
        public async Task<bool> QueueBatchFrames(string[] cameraIds, Mat[] frames)
        {
            if (cameraIds.Length != frames.Length)
                throw new ArgumentException("Camera IDs and frames count mismatch");
            
            var tasks = new Task<bool>[frames.Length];
            
            for (int i = 0; i < frames.Length; i++)
            {
                var index = i;
                tasks[i] = Task.Run(() => QueueFrame(cameraIds[index], frames[index], 1)); // 배치는 높은 우선순위
            }
            
            var results = await Task.WhenAll(tasks);
            
            // 모든 프레임이 성공적으로 큐에 추가되었는지 확인
            foreach (var result in results)
            {
                if (!result) return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// 프레임 처리 태스크 실행 (카메라별 독립 병렬 처리)
        /// </summary>
        private async Task ProcessFrameTask(FrameTask task)
        {
            // 카메라별 독립적인 처리 제한 (각 카메라당 1개씩 동시 처리)
            var cameraLimiter = _cameraLimiters.GetOrAdd(task.CameraId, 
                _ => new SemaphoreSlim(1, 1));
            
            await cameraLimiter.WaitAsync(_cancellationTokenSource.Token);
            
            try
            {
                var processingStart = DateTime.Now;
                
                System.Diagnostics.Debug.WriteLine($"AIProcessingPipeline: Processing frame for camera {task.CameraId} - Thread: {Thread.CurrentThread.ManagedThreadId}");
                
                // AI 추론 실행 (각 카메라별로 병렬 실행)
                var detections = await _aiService.InferFrameAsync(task.CameraId, task.Frame);
                
                var processingTime = (DateTime.Now - processingStart).TotalMilliseconds;
                var queueTime = (processingStart - task.QueuedTime).TotalMilliseconds;
                
                Interlocked.Increment(ref _totalFramesProcessed);
                
                // 성능 지표 업데이트 (주기적) - 빈도 줄임
                if (_totalFramesProcessed % 60 == 0) // 60프레임마다로 줄임
                {
                    ReportPerformance();
                }
                
                // 검출 결과가 있으면 이벤트 발생
                if (detections.Length > 0)
                {
                    ObjectDetected?.Invoke(this, new ObjectDetectionEventArgs
                    {
                        CameraId = task.CameraId,
                        Detections = detections,
                        ProcessingTime = processingTime
                    });
                }
                
                // 디버그 로그 (필요시)
                if (detections.Length > 0)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"AIProcessingPipeline: {task.CameraId} - {detections.Length} objects detected " +
                        $"(Queue: {queueTime:F1}ms, Process: {processingTime:F1}ms)");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AIProcessingPipeline: Processing error for {task.CameraId}: {ex.Message}");
            }
            finally
            {
                // 리소스 해제
                task.Frame?.Dispose();
                cameraLimiter.Release();
            }
        }
        
        /// <summary>
        /// 프레임 스키핑 결정 (성능 최적화)
        /// </summary>
        private bool ShouldProcessFrame(string cameraId)
        {
            var counter = _frameSkipCounters.GetOrAdd(cameraId, _ => new FrameSkipCounter());
            
            counter.TotalFrames++;
            
            // N번째 프레임마다만 처리
            if (counter.TotalFrames % ProcessEveryNthFrame == 0)
            {
                counter.ProcessedFrames++;
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// AI 서비스에서 검출 결과 수신
        /// </summary>
        private void OnObjectDetected(object? sender, ObjectDetectionEventArgs e)
        {
            // 파이프라인에서 다시 전파
            ObjectDetected?.Invoke(this, e);
        }
        
        /// <summary>
        /// 성능 지표 보고
        /// </summary>
        private void ReportPerformance()
        {
            var performance = new PipelinePerformance
            {
                TotalFramesQueued = _totalFramesQueued,
                TotalFramesProcessed = _totalFramesProcessed,
                TotalFramesDropped = _totalFramesDropped,
                QueueUtilization = (double)_totalFramesProcessed / Math.Max(1, _totalFramesQueued),
                ActiveWorkers = MaxConcurrentProcessing - _processingLimiter.CurrentCount,
                Timestamp = DateTime.Now
            };
            
            PerformanceUpdated?.Invoke(this, new PipelinePerformanceEventArgs
            {
                Performance = performance
            });
            
            // 카메라별 스키핑 통계 로그
            foreach (var kvp in _frameSkipCounters)
            {
                var counter = kvp.Value;
                var skipRate = 1.0 - (double)counter.ProcessedFrames / Math.Max(1, counter.TotalFrames);
                
                if (counter.TotalFrames % 100 == 0) // 100프레임마다 로그
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"AIProcessingPipeline: {kvp.Key} - Skip rate: {skipRate:P1} " +
                        $"({counter.ProcessedFrames}/{counter.TotalFrames})");
                }
            }
        }
        
        /// <summary>
        /// 파이프라인 중지 및 정리
        /// </summary>
        public async Task StopAsync()
        {
            if (_disposed) return;
            
            System.Diagnostics.Debug.WriteLine("AIProcessingPipeline: Stopping...");
            
            // 새로운 프레임 받기 중단
            _processingBlock.Complete();
            
            try
            {
                // 진행 중인 작업 완료 대기 (최대 5초)
                await _processingBlock.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                System.Diagnostics.Debug.WriteLine("AIProcessingPipeline: Stop timeout - forcing shutdown");
            }
            
            _cancellationTokenSource.Cancel();
            
            System.Diagnostics.Debug.WriteLine(
                $"AIProcessingPipeline: Stopped - Processed: {_totalFramesProcessed}, " +
                $"Dropped: {_totalFramesDropped}, Total: {_totalFramesQueued}");
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            StopAsync().Wait(TimeSpan.FromSeconds(10));
            
            _aiService.ObjectDetected -= OnObjectDetected;
            _processingLimiter?.Dispose();
            _cancellationTokenSource?.Dispose();
            
            // 카메라별 제한기들 정리
            foreach (var limiter in _cameraLimiters.Values)
            {
                limiter?.Dispose();
            }
            _cameraLimiters.Clear();
            
            _disposed = true;
            System.Diagnostics.Debug.WriteLine("AIProcessingPipeline: Disposed");
        }
    }
    
    /// <summary>
    /// 프레임 처리 태스크
    /// </summary>
    public class FrameTask
    {
        public string CameraId { get; set; } = string.Empty;
        public Mat Frame { get; set; } = new();
        public int Priority { get; set; } = 0;
        public DateTime QueuedTime { get; set; } = DateTime.Now;
    }
    
    /// <summary>
    /// 프레임 스키핑 카운터
    /// </summary>
    public class FrameSkipCounter
    {
        public long TotalFrames { get; set; } = 0;
        public long ProcessedFrames { get; set; } = 0;
        public DateTime LastReset { get; set; } = DateTime.Now;
        
        public void Reset()
        {
            TotalFrames = 0;
            ProcessedFrames = 0;
            LastReset = DateTime.Now;
        }
    }
    
    /// <summary>
    /// 파이프라인 성능 지표
    /// </summary>
    public class PipelinePerformance
    {
        public long TotalFramesQueued { get; set; }
        public long TotalFramesProcessed { get; set; }
        public long TotalFramesDropped { get; set; }
        public double QueueUtilization { get; set; }
        public int ActiveWorkers { get; set; }
        public DateTime Timestamp { get; set; }
        
        public double DropRate => TotalFramesQueued > 0 ? (double)TotalFramesDropped / TotalFramesQueued : 0;
        public double ProcessRate => TotalFramesQueued > 0 ? (double)TotalFramesProcessed / TotalFramesQueued : 0;
    }
    
    /// <summary>
    /// 파이프라인 성능 이벤트 인자
    /// </summary>
    public class PipelinePerformanceEventArgs : EventArgs
    {
        public PipelinePerformance Performance { get; set; } = new();
    }
}