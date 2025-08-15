using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using OpenCvSharp;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 카메라 프레임 처리를 위한 최적화된 서비스
    /// Producer-Consumer 패턴으로 프레임 드롭을 방지하면서도 최신 프레임 처리
    /// </summary>
    public class FrameProcessingService : IDisposable
    {
        private readonly ActionBlock<FrameProcessingRequest> _processingBlock;
        private readonly SemaphoreSlim _frameProcessingSemaphore;
        private bool _disposed;

        public FrameProcessingService()
        {
            // 동시에 1개의 프레임만 처리 (프레임 드롭 방지)
            _frameProcessingSemaphore = new SemaphoreSlim(1, 1);

            // 최신 프레임만 유지하도록 설정 - 성능 최적화
            var options = new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = 2, // 큐 크기 줄임 (지연 감소)
                MaxDegreeOfParallelism = 2, // 병렬 처리 증가
                CancellationToken = CancellationToken.None
            };

            _processingBlock = new ActionBlock<FrameProcessingRequest>(ProcessFrameAsync, options);
        }

        /// <summary>
        /// 프레임 처리 요청 (최신 프레임만 처리됨)
        /// </summary>
        public async Task<bool> QueueFrameAsync(string cameraId, Mat frame, Func<Mat, Task> processAction)
        {
            if (_disposed || frame == null || frame.Empty())
                return false;

            var request = new FrameProcessingRequest
            {
                CameraId = cameraId,
                Frame = frame.Clone(), // 프레임 복사본 생성
                ProcessAction = processAction,
                Timestamp = DateTime.UtcNow
            };

            // 이전 프레임은 자동으로 드롭됨 (BoundedCapacity = 1)
            var posted = await _processingBlock.SendAsync(request);
            
            if (!posted)
            {
                request.Frame?.Dispose();
            }

            return posted;
        }

        private async Task ProcessFrameAsync(FrameProcessingRequest request)
        {
            if (_disposed)
            {
                request.Frame?.Dispose();
                return;
            }

            await _frameProcessingSemaphore.WaitAsync();
            try
            {
                // 프레임이 너무 오래되었으면 스킵 - 더 관대하게 설정
                var age = DateTime.UtcNow - request.Timestamp;
                if (age.TotalMilliseconds > 200) // 200ms로 늘림 (더 안정적)
                {
                    return;
                }

                await request.ProcessAction(request.Frame);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Frame processing error: {ex.Message}");
            }
            finally
            {
                request.Frame?.Dispose();
                _frameProcessingSemaphore.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _processingBlock.Complete();
            _processingBlock.Completion.Wait(TimeSpan.FromSeconds(5));
            _frameProcessingSemaphore?.Dispose();
        }

        private class FrameProcessingRequest
        {
            public string CameraId { get; set; } = string.Empty;
            public Mat Frame { get; set; } = new Mat();
            public Func<Mat, Task> ProcessAction { get; set; } = _ => Task.CompletedTask;
            public DateTime Timestamp { get; set; }
        }
    }
}