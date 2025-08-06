using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using SafetyVisionMonitor.AI;
using SafetyVisionMonitor.Models;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// AI 추론 서비스 - YOLO 모델 관리 및 실시간 객체 검출
    /// </summary>
    public class AIInferenceService : IDisposable
    {
        private YOLOv8Engine? _currentEngine;
        private AIModel? _activeModel;
        private readonly ConcurrentQueue<PerformanceMetrics> _performanceHistory = new();
        private readonly Stopwatch _performanceTimer = new();
        private bool _disposed = false;
        
        // 성능 통계
        private long _totalFramesProcessed = 0;
        private double _totalInferenceTime = 0;
        private DateTime _lastPerformanceUpdate = DateTime.Now;
        
        // 이벤트
        public event EventHandler<ObjectDetectionEventArgs>? ObjectDetected;
        public event EventHandler<ModelPerformanceEventArgs>? PerformanceUpdated;
        public event EventHandler<ModelStatusEventArgs>? ModelStatusChanged;
        public event EventHandler<ModelDownloadProgressEventArgs>? ModelDownloadProgress;
        
        // 속성
        public bool IsModelLoaded => _currentEngine?.IsLoaded == true;
        public AIModel? ActiveModel => _activeModel;
        public long TotalFramesProcessed => _totalFramesProcessed;
        public double AverageInferenceTime => _totalFramesProcessed > 0 ? _totalInferenceTime / _totalFramesProcessed : 0;
        public bool IsUsingGpu => _currentEngine?.IsUsingGpu ?? false;
        public string ExecutionProvider => _currentEngine?.ExecutionProvider ?? "Unknown";
        
        /// <summary>
        /// YOLO 모델 로드
        /// </summary>
        public async Task<bool> LoadModelAsync(AIModel model)
        {
            if (model == null || string.IsNullOrEmpty(model.ModelPath))
            {
                return false;
            }
            
            try
            {
                // 기존 모델 언로드
                UnloadModel();
                
                // 모델 파일 존재 확인
                if (!File.Exists(model.ModelPath))
                {
                    System.Diagnostics.Debug.WriteLine($"AIInferenceService: Model file not found: {model.ModelPath}");
                    NotifyModelStatus(model, ModelStatus.Error, "모델 파일을 찾을 수 없습니다.");
                    return false;
                }
                
                // 상태 업데이트
                NotifyModelStatus(model, ModelStatus.Loading, "모델 로딩 중...");
                
                // 비동기로 모델 로드 (자동 다운로드 포함)
                _currentEngine = new YOLOv8Engine();
                
                // 다운로드 진행률 이벤트 구독
                _currentEngine.DownloadProgressChanged += OnModelDownloadProgress;
                
                var useGpu = true; // GPU 사용 시도
                var success = await _currentEngine.InitializeAsync(model.ModelPath, useGpu);
                
                if (success)
                {
                    _activeModel = model;
                    _totalFramesProcessed = 0;
                    _totalInferenceTime = 0;
                    
                    var statusMessage = $"모델 로드 완료 ({_currentEngine.ExecutionProvider})";
                    NotifyModelStatus(model, ModelStatus.Ready, statusMessage);
                    System.Diagnostics.Debug.WriteLine($"AIInferenceService: Model loaded successfully: {model.Name} using {_currentEngine.ExecutionProvider}");
                    
                    return true;
                }
                else
                {
                    _currentEngine?.Dispose();
                    _currentEngine = null;
                    
                    NotifyModelStatus(model, ModelStatus.Error, "모델 로드 실패");
                    System.Diagnostics.Debug.WriteLine($"AIInferenceService: Failed to load model: {model.Name}");
                    
                    return false;
                }
            }
            catch (Exception ex)
            {
                _currentEngine?.Dispose();
                _currentEngine = null;
                
                NotifyModelStatus(model, ModelStatus.Error, $"모델 로드 오류: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"AIInferenceService: Model load exception: {ex.Message}");
                
                return false;
            }
        }
        
        /// <summary>
        /// 현재 로드된 모델 언로드
        /// </summary>
        public void UnloadModel()
        {
            if (_currentEngine != null)
            {
                // 이벤트 구독 해제
                _currentEngine.DownloadProgressChanged -= OnModelDownloadProgress;
                
                _currentEngine.Dispose();
                _currentEngine = null;
                
                if (_activeModel != null)
                {
                    NotifyModelStatus(_activeModel, ModelStatus.Ready, "모델 언로드됨");
                }
                
                _activeModel = null;
                System.Diagnostics.Debug.WriteLine("AIInferenceService: Model unloaded");
            }
        }
        
        /// <summary>
        /// 단일 프레임에서 객체 검출
        /// </summary>
        public async Task<DetectionResult[]> InferFrameAsync(string cameraId, Mat frame)
        {
            if (_currentEngine == null || _activeModel == null || frame.Empty())
            {
                return Array.Empty<DetectionResult>();
            }
            
            try
            {
                _performanceTimer.Restart();
                
                // 추론 실행 (CPU 집약적이므로 별도 스레드에서)
                var detections = await Task.Run(() =>
                {
                    return _currentEngine.InferFrame(frame, (float)_activeModel.Confidence, 0.45f);
                });
                
                _performanceTimer.Stop();
                
                // 성능 통계 업데이트
                var inferenceTime = _performanceTimer.Elapsed.TotalMilliseconds;
                UpdatePerformanceMetrics(inferenceTime, detections.Length);
                
                // 검출 결과에 카메라 ID 설정
                foreach (var detection in detections)
                {
                    detection.CameraId = cameraId;
                }
                
                // 이벤트 발생
                if (detections.Length > 0)
                {
                    ObjectDetected?.Invoke(this, new ObjectDetectionEventArgs
                    {
                        CameraId = cameraId,
                        Detections = detections,
                        ProcessingTime = inferenceTime
                    });
                }
                
                return detections;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AIInferenceService: Inference error: {ex.Message}");
                return Array.Empty<DetectionResult>();
            }
        }
        
        /// <summary>
        /// 배치 추론 (여러 프레임 동시 처리)
        /// </summary>
        public async Task<DetectionResult[][]> InferBatchAsync(string[] cameraIds, Mat[] frames)
        {
            if (_currentEngine == null || frames.Length == 0)
            {
                return new DetectionResult[frames.Length][];
            }
            
            var results = new DetectionResult[frames.Length][];
            var tasks = new Task[frames.Length];
            
            for (int i = 0; i < frames.Length; i++)
            {
                var index = i; // 클로저 캡처 문제 방지
                tasks[i] = Task.Run(async () =>
                {
                    results[index] = await InferFrameAsync(cameraIds[index], frames[index]);
                });
            }
            
            await Task.WhenAll(tasks);
            return results;
        }
        
        /// <summary>
        /// 테스트 이미지로 추론 실행
        /// </summary>
        public async Task<(DetectionResult[] detections, ModelPerformance performance)> TestInferenceAsync(string imagePath)
        {
            if (_currentEngine == null || !File.Exists(imagePath))
            {
                return (Array.Empty<DetectionResult>(), new ModelPerformance());
            }
            
            try
            {
                // 이미지 로드
                using var image = new Mat(imagePath);
                if (image.Empty())
                {
                    return (Array.Empty<DetectionResult>(), new ModelPerformance());
                }
                
                var totalTimer = Stopwatch.StartNew();
                
                // 추론 실행
                var detections = await InferFrameAsync("TEST", image);
                
                totalTimer.Stop();
                
                var performance = new ModelPerformance
                {
                    //TotalTime = totalTimer.Elapsed.TotalMilliseconds,
                    InferenceTime = totalTimer.Elapsed.TotalMilliseconds, // 단순화
                    ProcessedFrames = 1,
                    DetectedObjects = detections.Length
                };
                
                return (detections, performance);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AIInferenceService: Test inference error: {ex.Message}");
                return (Array.Empty<DetectionResult>(), new ModelPerformance());
            }
        }
        
        /// <summary>
        /// 성능 지표 업데이트
        /// </summary>
        private void UpdatePerformanceMetrics(double inferenceTime, int objectCount)
        {
            Interlocked.Increment(ref _totalFramesProcessed);
            _totalInferenceTime += inferenceTime;
            
            // 성능 이벤트 주기적 발생 (1초마다)
            var now = DateTime.Now;
            if ((now - _lastPerformanceUpdate).TotalSeconds >= 1.0)
            {
                _lastPerformanceUpdate = now;
                
                var performance = new ModelPerformance
                {
                    InferenceTime = inferenceTime,
                    //TotalTime = inferenceTime,
                    ProcessedFrames = (int)_totalFramesProcessed,
                    DetectedObjects = objectCount
                };
                
                PerformanceUpdated?.Invoke(this, new ModelPerformanceEventArgs
                {
                    Performance = performance,
                    ModelName = _activeModel?.Name ?? "Unknown"
                });
            }
        }
        
        /// <summary>
        /// 모델 상태 변경 알림
        /// </summary>
        private void NotifyModelStatus(AIModel model, ModelStatus status, string message)
        {
            model.Status = status;
            
            ModelStatusChanged?.Invoke(this, new ModelStatusEventArgs
            {
                Model = model,
                Status = status,
                Message = message
            });
        }
        
        /// <summary>
        /// 신뢰도 임계값 업데이트
        /// </summary>
        public void UpdateConfidenceThreshold(float confidence)
        {
            if (_activeModel != null)
            {
                _activeModel.Confidence = confidence;
                System.Diagnostics.Debug.WriteLine($"AIInferenceService: Confidence threshold updated to {confidence:F2}");
            }
        }
        
        /// <summary>
        /// 현재 모델의 신뢰도 임계값 조회
        /// </summary>
        public float GetConfidenceThreshold()
        {
            return (float)(_activeModel?.Confidence ?? 0.7);
        }
        
        /// <summary>
        /// 모델 정보 조회
        /// </summary>
        public AIModelInfo GetModelInfo()
        {
            if (_activeModel == null || _currentEngine == null)
            {
                return new AIModelInfo();
            }
            
            return new AIModelInfo
            {
                Name = _activeModel.Name,
                Type = _activeModel.Type.ToString(),
                IsLoaded = IsModelLoaded,
                ConfidenceThreshold = (float)_activeModel.Confidence,
                InputSize = _currentEngine.Metadata.InputSize,
                ClassCount = _currentEngine.Metadata.ClassCount,
                TotalFramesProcessed = _totalFramesProcessed,
                AverageInferenceTime = AverageInferenceTime
            };
        }
        
        /// <summary>
        /// 모델 다운로드 진행률 이벤트 핸들러
        /// </summary>
        private void OnModelDownloadProgress(object? sender, ModelDownloadProgressEventArgs e)
        {
            // 상태 메시지 업데이트
            if (_activeModel != null)
            {
                var message = $"모델 다운로드 중... {e.ProgressPercentage:F1}%";
                NotifyModelStatus(_activeModel, ModelStatus.Loading, message);
            }
            
            // 진행률 이벤트 전파
            ModelDownloadProgress?.Invoke(this, e);
            
            System.Diagnostics.Debug.WriteLine($"AIInferenceService: Download progress: {e.ProgressPercentage:F1}%");
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            // 이벤트 구독 해제
            if (_currentEngine != null)
            {
                _currentEngine.DownloadProgressChanged -= OnModelDownloadProgress;
            }
            
            UnloadModel();
            _disposed = true;
            
            System.Diagnostics.Debug.WriteLine("AIInferenceService: Disposed");
        }
    }
    
    /// <summary>
    /// 객체 검출 이벤트 인자
    /// </summary>
    public class ObjectDetectionEventArgs : EventArgs
    {
        public string CameraId { get; set; } = string.Empty;
        public DetectionResult[] Detections { get; set; } = Array.Empty<DetectionResult>();
        public double ProcessingTime { get; set; }
    }
    
    /// <summary>
    /// 모델 성능 이벤트 인자
    /// </summary>
    public class ModelPerformanceEventArgs : EventArgs
    {
        public ModelPerformance Performance { get; set; } = new();
        public string ModelName { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// 모델 상태 이벤트 인자
    /// </summary>
    public class ModelStatusEventArgs : EventArgs
    {
        public AIModel Model { get; set; } = new();
        public ModelStatus Status { get; set; }
        public string Message { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// 성능 통계 클래스
    /// </summary>
    // public class PerformanceMetrics
    // {
    //     public DateTime Timestamp { get; set; } = DateTime.Now;
    //     public double InferenceTime { get; set; }
    //     public int ObjectCount { get; set; }
    //     public string CameraId { get; set; } = string.Empty;
    // }
    
    public class PerformanceMetrics
    {
        public DateTime Timestamp { get; set; }
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public double GpuUsage { get; set; }
        public string GpuName { get; set; } = "Unknown GPU";
        public double GpuMemoryUsage { get; set; }
        public double GpuTemperature { get; set; }
        public int ProcessedFps { get; set; }
        public int DetectedPersons { get; set; }
        public int ActiveAlerts { get; set; }
        public bool IsUsingGpu { get; set; }
        public string ExecutionProvider { get; set; } = "Unknown";
    }
    
    /// <summary>
    /// AI 모델 정보 클래스
    /// </summary>
    public class AIModelInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsLoaded { get; set; }
        public float ConfidenceThreshold { get; set; }
        public System.Drawing.Size InputSize { get; set; }
        public int ClassCount { get; set; }
        public long TotalFramesProcessed { get; set; }
        public double AverageInferenceTime { get; set; }
    }
}