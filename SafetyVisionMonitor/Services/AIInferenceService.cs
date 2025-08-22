using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using SafetyVisionMonitor.Services;
using SafetyVisionMonitor.Shared.Database;
using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// AI 추론 서비스 - YOLO 모델 관리 및 실시간 객체 검출
    /// </summary>
    public partial class AIInferenceService : IDisposable
    {
        private YOLOv8Engine? _currentEngine;
        private YOLOv8MultiTaskEngine? _yolov8MultiTaskEngine;
        private MultiCameraAIService? _multiCameraService;
        private AIModel? _activeModel;
        private readonly ConcurrentQueue<PerformanceMetrics> _performanceHistory = new();
        private readonly Stopwatch _performanceTimer = new();
        private bool _disposed = false;
        private bool _useMultiTaskEngine = false;
        private bool _useMultiCameraService = true; // 기본적으로 다중 카메라 서비스 사용
        
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
        public bool IsModelLoaded => _useMultiTaskEngine 
            ? (_yolov8MultiTaskEngine?.IsDetectionModelLoaded == true)
            : _currentEngine?.IsLoaded == true;
        public bool IsMultiTaskEngineActive => _useMultiTaskEngine;
        public AIModel? ActiveModel => _activeModel;
        public long TotalFramesProcessed => _totalFramesProcessed;
        public double AverageInferenceTime => _totalFramesProcessed > 0 ? _totalInferenceTime / _totalFramesProcessed : 0;
        public bool IsUsingGpu => _useMultiTaskEngine 
            ? (_yolov8MultiTaskEngine?.IsUsingGpu ?? false) 
            : _currentEngine?.IsUsingGpu ?? false;
        public string ExecutionProvider => _useMultiTaskEngine 
            ? (_yolov8MultiTaskEngine?.ExecutionProvider ?? "Unknown")
            : _currentEngine?.ExecutionProvider ?? "Unknown";
        
        // 멀티태스크 엔진 상태 속성
        public bool IsPoseModelLoaded => _yolov8MultiTaskEngine?.IsPoseModelLoaded ?? false;
        public bool IsSegmentationModelLoaded => _yolov8MultiTaskEngine?.IsSegmentationModelLoaded ?? false;
        public bool IsClassificationModelLoaded => _yolov8MultiTaskEngine?.IsClassificationModelLoaded ?? false;
        public bool IsOBBModelLoaded => _yolov8MultiTaskEngine?.IsOBBModelLoaded ?? false;
        
        /// <summary>
        /// AIInferenceService 생성자
        /// </summary>
        public AIInferenceService()
        {
            // MultiCameraAIService 초기화
            if (_useMultiCameraService)
            {
                _multiCameraService = new MultiCameraAIService(maxEngines: 4);
                
                // MultiCameraAIService 이벤트를 AIInferenceService로 전달
                _multiCameraService.ObjectDetected += (sender, e) =>
                {
                    ObjectDetected?.Invoke(this, e);
                };
                
                _multiCameraService.ModelStatusChanged += (sender, e) =>
                {
                    ModelStatusChanged?.Invoke(this, new ModelStatusEventArgs
                    {
                        Model = _activeModel,
                        CameraId = e.CameraId,
                        Status = e.Status,
                        Message = e.Message
                    });
                };
                
                System.Diagnostics.Debug.WriteLine("AIInferenceService: MultiCameraAIService 초기화 완료");
            }
        }
        
        /// <summary>
        /// YOLOv8 멀티태스크 엔진 초기화
        /// </summary>
        public async Task<bool> InitializeYOLOv8MultiTaskEngineAsync(bool useGpu = true)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("AIInferenceService: YOLOv8 멀티태스크 엔진 초기화 시작...");
                
                // 기존 모델 언로드
                UnloadModel();
                
                _yolov8MultiTaskEngine = new YOLOv8MultiTaskEngine();
                
                // 진행률 이벤트 구독
                _yolov8MultiTaskEngine.ModelLoadProgress += OnMultiTaskModelLoadProgress;
                
                var loadedCount = await _yolov8MultiTaskEngine.InitializeAllModelsAsync(useGpu);
                
                if (loadedCount > 0)
                {
                    _useMultiTaskEngine = true;
                    
                    // 데이터베이스에 YOLOv8 모델들 등록
                    await RegisterYOLOv8ModelsToDatabase(loadedCount);
                    
                    // 가상의 AI 모델 생성 (UI 호환성을 위해)
                    _activeModel = new AIModel
                    {
                        Id = "yolov8_multitask",
                        Name = "YOLOv8 멀티태스크",
                        Type = ModelType.YOLOv8,
                        ModelPath = "Models/yolov8s.onnx",
                        Confidence = 0.7f,
                        IsActive = true
                    };
                    
                    System.Diagnostics.Debug.WriteLine($"AIInferenceService: YOLOv8 멀티태스크 엔진 초기화 완료 ({loadedCount}/5 모델 로드)");
                    return true;
                }
                else
                {
                    _yolov8MultiTaskEngine?.Dispose();
                    _yolov8MultiTaskEngine = null;
                    System.Diagnostics.Debug.WriteLine("AIInferenceService: YOLOv8 멀티태스크 엔진 초기화 실패");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _yolov8MultiTaskEngine?.Dispose();
                _yolov8MultiTaskEngine = null;
                System.Diagnostics.Debug.WriteLine($"AIInferenceService: YOLOv8 멀티태스크 엔진 초기화 오류: {ex.Message}");
                return false;
            }
        }
        
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
                
                // 멀티태스크 엔진 사용 해제
                _useMultiTaskEngine = false;
                
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
            
            if (_yolov8MultiTaskEngine != null)
            {
                // 이벤트 구독 해제
                _yolov8MultiTaskEngine.ModelLoadProgress -= OnMultiTaskModelLoadProgress;
                
                _yolov8MultiTaskEngine.Dispose();
                _yolov8MultiTaskEngine = null;
                _useMultiTaskEngine = false;
                
                System.Diagnostics.Debug.WriteLine("AIInferenceService: YOLOv8 MultiTask engine unloaded");
            }
        }
        
        /// <summary>
        /// 안전 모니터링 구역 크롭 기반 객체 검출 (성능 최적화)
        /// </summary>
        public async Task<DetectionResult[]> InferFrameWithSafetyZoneAsync(string cameraId, Mat frame, List<System.Drawing.Point> safetyZonePoints)
        {
            if (frame.Empty() || safetyZonePoints == null || safetyZonePoints.Count < 3)
            {
                // 안전 구역이 없으면 전체 프레임 처리
                return await InferFrameAsync(cameraId, frame);
            }

            try
            {
                _performanceTimer.Restart();
                
                // 1. 안전 구역 바운딩 박스 계산
                var boundingRect = CalculateBoundingRect(safetyZonePoints);
                
                // 2. 구역 크롭 (여유분 10% 추가로 경계 객체 누락 방지)
                var expandedRect = ExpandRect(boundingRect, frame.Width, frame.Height, 0.1f);
                using var croppedFrame = new Mat(frame, expandedRect);
                
                // 3. 크롭된 이미지로 추론
                DetectionResult[] croppedDetections;
                if (_useMultiTaskEngine && _yolov8MultiTaskEngine != null && _activeModel != null)
                {
                    croppedDetections = await _yolov8MultiTaskEngine.RunObjectDetectionAsync(croppedFrame, (float)_activeModel.Confidence);
                }
                else if (_currentEngine != null && _activeModel != null)
                {
                    croppedDetections = await Task.Run(() =>
                        _currentEngine.InferFrame(croppedFrame, (float)_activeModel.Confidence, 0.45f));
                }
                else
                {
                    return Array.Empty<DetectionResult>();
                }
                
                // 4. 좌표를 원본 프레임 기준으로 변환
                var adjustedDetections = ConvertCroppedCoordinates(croppedDetections, expandedRect, cameraId);
                
                // 5. 안전 구역 내부 객체만 필터링
                var safetyZoneDetections = FilterDetectionsByZone(adjustedDetections, safetyZonePoints);
                
                _performanceTimer.Stop();
                
                // 성능 통계 업데이트
                var inferenceTime = _performanceTimer.Elapsed.TotalMilliseconds;
                UpdatePerformanceMetrics(inferenceTime, safetyZoneDetections.Length);
                
                // 이벤트 발생
                if (safetyZoneDetections.Length > 0)
                {
                    ObjectDetected?.Invoke(this, new ObjectDetectionEventArgs
                    {
                        CameraId = cameraId,
                        Detections = safetyZoneDetections,
                        ProcessingTime = inferenceTime
                    });
                }
                
                System.Diagnostics.Debug.WriteLine($"SafetyZone Crop Inference: {inferenceTime:F1}ms, Objects: {safetyZoneDetections.Length} (Crop ratio: {(float)croppedFrame.Width * croppedFrame.Height / (frame.Width * frame.Height):P1})");
                
                return safetyZoneDetections;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AIInferenceService: SafetyZone inference error: {ex.Message}");
                // 오류 시 전체 프레임 처리로 폴백
                return await InferFrameAsync(cameraId, frame);
            }
        }
        
        /// <summary>
        /// 단일 프레임에서 객체 검출 (전체 프레임)
        /// </summary>
        public async Task<DetectionResult[]> InferFrameAsync(string cameraId, Mat frame)
        {
            // 다중 카메라 서비스 사용 시 (카메라별 독립적인 AI 엔진)
            if (_useMultiCameraService && _multiCameraService != null)
            {
                return await _multiCameraService.ProcessFrameAsync(cameraId, frame);
            }
            
            // 멀티태스크 엔진 사용 시
            if (_useMultiTaskEngine && _yolov8MultiTaskEngine != null && _activeModel != null)
            {
                return await InferFrameWithMultiTaskEngineAsync(cameraId, frame);
            }
            
            // 기존 단일 엔진 사용 시
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
        /// 멀티태스크 엔진을 사용한 프레임 추론
        /// </summary>
        private async Task<DetectionResult[]> InferFrameWithMultiTaskEngineAsync(string cameraId, Mat frame)
        {
            if (_yolov8MultiTaskEngine == null || _activeModel == null || frame.Empty())
            {
                return Array.Empty<DetectionResult>();
            }
            
            try
            {
                _performanceTimer.Restart();
                
                // Object Detection 추론 (YOLOv8) - 안전성 체크 추가
                DetectionResult[] detections;
                try
                {
                    detections = await _yolov8MultiTaskEngine.RunObjectDetectionAsync(frame, (float)_activeModel.Confidence);
                }
                catch (AccessViolationException avEx)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ MultiTask Engine AccessViolationException: {avEx.Message}");
                    System.Diagnostics.Debug.WriteLine("⚠️ 단일 엔진으로 폴백 시도");
                    
                    // 기존 단일 엔진으로 폴백
                    return await FallbackToSingleEngine(cameraId, frame);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ MultiTask Engine 오류: {ex.Message}");
                    return Array.Empty<DetectionResult>();
                }
                
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
                System.Diagnostics.Debug.WriteLine($"AIInferenceService: MultiTask inference error: {ex.Message}");
                return Array.Empty<DetectionResult>();
            }
        }
        
        /// <summary>
        /// 멀티태스크 엔진 실패 시 단일 엔진으로 폴백
        /// </summary>
        private async Task<DetectionResult[]> FallbackToSingleEngine(string cameraId, Mat frame)
        {
            try
            {
                // 기존 단일 엔진이 없으면 생성
                if (_currentEngine == null)
                {
                    System.Diagnostics.Debug.WriteLine("단일 엔진 초기화 중...");
                    _currentEngine = new YOLOv8Engine();
                    
                    // 기본 YOLOv8 모델 로드
                    var fallbackModelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "yolov8s.onnx");
                    if (File.Exists(fallbackModelPath))
                    {
                        var success = await _currentEngine.InitializeAsync(fallbackModelPath, useGpu: false); // CPU 전용
                        if (!success)
                        {
                            System.Diagnostics.Debug.WriteLine("단일 엔진 초기화 실패");
                            return Array.Empty<DetectionResult>();
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("폴백 모델 파일이 없음");
                        return Array.Empty<DetectionResult>();
                    }
                }
                
                // 단일 엔진으로 추론
                return await Task.Run(() =>
                {
                    try
                    {
                        return _currentEngine.InferFrame(frame, (float)_activeModel.Confidence, 0.45f);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"단일 엔진 추론도 실패: {ex.Message}");
                        return Array.Empty<DetectionResult>();
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"폴백 엔진 오류: {ex.Message}");
                return Array.Empty<DetectionResult>();
            }
        }
        
        /// <summary>
        /// Pose Estimation 추론
        /// </summary>
        public async Task<PoseResult[]> InferPoseAsync(string cameraId, Mat frame, float confidenceThreshold = 0.7f)
        {
            if (!_useMultiTaskEngine || _yolov8MultiTaskEngine == null)
            {
                return Array.Empty<PoseResult>();
            }
            
            try
            {
                return await _yolov8MultiTaskEngine.RunPoseEstimationAsync(frame, confidenceThreshold);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AIInferenceService: Pose inference error: {ex.Message}");
                return Array.Empty<PoseResult>();
            }
        }
        
        /// <summary>
        /// Instance Segmentation 추론
        /// </summary>
        public async Task<SegmentationResult[]> InferSegmentationAsync(string cameraId, Mat frame, float confidenceThreshold = 0.7f)
        {
            if (!_useMultiTaskEngine || _yolov8MultiTaskEngine == null)
            {
                return Array.Empty<SegmentationResult>();
            }
            
            try
            {
                return await _yolov8MultiTaskEngine.RunInstanceSegmentationAsync(frame, confidenceThreshold);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AIInferenceService: Segmentation inference error: {ex.Message}");
                return Array.Empty<SegmentationResult>();
            }
        }
        
        /// <summary>
        /// Image Classification 추론
        /// </summary>
        public async Task<ClassificationResult[]> InferClassificationAsync(string cameraId, Mat frame)
        {
            if (!_useMultiTaskEngine || _yolov8MultiTaskEngine == null)
            {
                return Array.Empty<ClassificationResult>();
            }
            
            try
            {
                return await _yolov8MultiTaskEngine.RunClassificationAsync(frame);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AIInferenceService: Classification inference error: {ex.Message}");
                return Array.Empty<ClassificationResult>();
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
        private void NotifyModelStatus(AIModel model, ModelStatus status, string message, string cameraId = "")
        {
            model.Status = status;
            
            ModelStatusChanged?.Invoke(this, new ModelStatusEventArgs
            {
                Model = model,
                CameraId = cameraId,
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
        
        /// <summary>
        /// 멀티태스크 모델 로드 진행률 이벤트 핸들러
        /// </summary>
        private void OnMultiTaskModelLoadProgress(object? sender, ModelLoadProgressEventArgs e)
        {
            // ModelDownloadProgressEventArgs 형태로 변환하여 기존 시스템과 호환
            var progressEvent = new ModelDownloadProgressEventArgs
            {
                ProgressPercentage = e.ProgressPercentage,
                DownloadedBytes = (long)(e.ProgressPercentage * 1000), // 더미 값
                TotalBytes = 100000 // 더미 값
            };
            
            ModelDownloadProgress?.Invoke(this, progressEvent);
            System.Diagnostics.Debug.WriteLine($"AIInferenceService: MultiTask load progress: {e.Message} ({e.ProgressPercentage:F1}%)");
        }
        
        /// <summary>
        /// YOLOv8 멀티태스크 모델들을 데이터베이스에 등록
        /// </summary>
        private async Task RegisterYOLOv8ModelsToDatabase(int loadedCount)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("AIInferenceService: YOLOv8 모델들을 데이터베이스에 등록 중...");
                
                var modelConfigs = new List<AIModelConfig>();
                
                // Detection 모델
                if (_yolov8MultiTaskEngine?.IsDetectionModelLoaded == true)
                {
                    modelConfigs.Add(CreateModelConfig("yolov8s_detection", "YOLOv8s Detection", "Models/yolov8s.onnx", "Object Detection"));
                }
                
                // Pose 모델
                if (_yolov8MultiTaskEngine?.IsPoseModelLoaded == true)
                {
                    modelConfigs.Add(CreateModelConfig("yolov8s_pose", "YOLOv8s Pose", "Models/yolov8s-pose.onnx", "Pose Estimation"));
                }
                
                // Segmentation 모델
                if (_yolov8MultiTaskEngine?.IsSegmentationModelLoaded == true)
                {
                    modelConfigs.Add(CreateModelConfig("yolov8s_seg", "YOLOv8s Segmentation", "Models/yolov8s-seg.onnx", "Instance Segmentation"));
                }
                
                // Classification 모델
                if (_yolov8MultiTaskEngine?.IsClassificationModelLoaded == true)
                {
                    modelConfigs.Add(CreateModelConfig("yolov8s_cls", "YOLOv8s Classification", "Models/yolov8s-cls.onnx", "Image Classification"));
                }
                
                // OBB 모델
                if (_yolov8MultiTaskEngine?.IsOBBModelLoaded == true)
                {
                    modelConfigs.Add(CreateModelConfig("yolov8s_obb", "YOLOv8s OBB", "Models/yolov8s-obb.onnx", "Oriented Bounding Box"));
                }
                
                // 데이터베이스에 저장
                if (modelConfigs.Count > 0)
                {
                    await App.DatabaseService.SaveAIModelConfigsAsync(modelConfigs);
                    System.Diagnostics.Debug.WriteLine($"AIInferenceService: {modelConfigs.Count}개 YOLOv8 모델이 데이터베이스에 등록됨");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AIInferenceService: 모델 데이터베이스 등록 오류: {ex.Message}");
            }
        }
        
        /// <summary>
        /// AIModelConfig 생성 헬퍼
        /// </summary>
        private AIModelConfig CreateModelConfig(string id, string name, string modelPath, string description)
        {
            var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, modelPath);
            var fileSize = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;
            
            return new AIModelConfig
            {
                ModelName = name,
                ModelVersion = "8.0.0",
                ModelType = "YOLOv8",
                ModelPath = fullPath,
                DefaultConfidence = 0.7,
                IsActive = id == "yolov8s_detection", // Detection 모델만 기본 활성화
                FileSize = fileSize,
                UploadedTime = DateTime.Now,
                Description = $"YOLOv8 {description} 모델 (자동 등록)",
                ConfigJson = "{}"
            };
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            // 이벤트 구독 해제
            if (_currentEngine != null)
            {
                _currentEngine.DownloadProgressChanged -= OnModelDownloadProgress;
            }
            
            // MultiCameraAIService 정리
            if (_multiCameraService != null)
            {
                _multiCameraService.Dispose();
                _multiCameraService = null;
                System.Diagnostics.Debug.WriteLine("AIInferenceService: MultiCameraAIService disposed");
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
        public AIModel? Model { get; set; } = new();
        public string CameraId { get; set; } = string.Empty;
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
    
    // AIInferenceService 헬퍼 메서드들 (부분 클래스 확장)
    public partial class AIInferenceService
    {
        /// <summary>
        /// 안전 구역 포인트들로부터 바운딩 사각형 계산
        /// </summary>
        private OpenCvSharp.Rect CalculateBoundingRect(List<System.Drawing.Point> points)
        {
            if (points.Count == 0) return new OpenCvSharp.Rect();
            
            var minX = points.Min(p => p.X);
            var minY = points.Min(p => p.Y);
            var maxX = points.Max(p => p.X);
            var maxY = points.Max(p => p.Y);
            
            return new OpenCvSharp.Rect(minX, minY, maxX - minX, maxY - minY);
        }
        
        /// <summary>
        /// 바운딩 사각형을 지정된 비율만큼 확장 (경계 객체 누락 방지)
        /// </summary>
        private OpenCvSharp.Rect ExpandRect(OpenCvSharp.Rect rect, int frameWidth, int frameHeight, float expandRatio)
        {
            var expandX = (int)(rect.Width * expandRatio);
            var expandY = (int)(rect.Height * expandRatio);
            
            var newX = Math.Max(0, rect.X - expandX);
            var newY = Math.Max(0, rect.Y - expandY);
            var newWidth = Math.Min(frameWidth - newX, rect.Width + expandX * 2);
            var newHeight = Math.Min(frameHeight - newY, rect.Height + expandY * 2);
            
            return new OpenCvSharp.Rect(newX, newY, newWidth, newHeight);
        }
        
        /// <summary>
        /// 크롭된 좌표를 원본 프레임 좌표로 변환
        /// </summary>
        private DetectionResult[] ConvertCroppedCoordinates(DetectionResult[] detections, OpenCvSharp.Rect cropRect, string cameraId)
        {
            foreach (var detection in detections)
            {
                // 바운딩 박스 좌표를 원본 프레임 기준으로 변환
                detection.BoundingBox = new System.Drawing.RectangleF(
                    detection.BoundingBox.X + cropRect.X,
                    detection.BoundingBox.Y + cropRect.Y,
                    detection.BoundingBox.Width,
                    detection.BoundingBox.Height
                );
                
                detection.CameraId = cameraId;
            }
            
            return detections;
        }
        
        /// <summary>
        /// 안전 구역 내부에 있는 검출 결과만 필터링
        /// </summary>
        private DetectionResult[] FilterDetectionsByZone(DetectionResult[] detections, List<System.Drawing.Point> safetyZonePoints)
        {
            var filteredDetections = new List<DetectionResult>();
            
            foreach (var detection in detections)
            {
                // 객체의 중심점이 안전 구역 내부에 있는지 확인
                var centerPoint = new System.Drawing.Point(
                    (int)detection.Center.X,
                    (int)detection.Center.Y
                );
                
                if (IsPointInPolygon(centerPoint, safetyZonePoints))
                {
                    filteredDetections.Add(detection);
                }
            }
            
            return filteredDetections.ToArray();
        }
        
        /// <summary>
        /// 점이 다각형 내부에 있는지 확인 (Ray Casting Algorithm)
        /// </summary>
        private bool IsPointInPolygon(System.Drawing.Point point, List<System.Drawing.Point> polygon)
        {
            if (polygon.Count < 3) return false;
            
            bool inside = false;
            int j = polygon.Count - 1;
            
            for (int i = 0; i < polygon.Count; j = i++)
            {
                if (((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)) &&
                    (point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X))
                {
                    inside = !inside;
                }
            }
            
            return inside;
        }
    }
}