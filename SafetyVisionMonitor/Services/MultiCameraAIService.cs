using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 카메라별 독립적인 AI 추론 서비스
    /// 각 카메라마다 별도의 YOLO 엔진 인스턴스를 관리하여 진정한 병렬 처리를 구현
    /// </summary>
    public class MultiCameraAIService : IDisposable
    {
        private readonly ConcurrentDictionary<string, YOLOv8Engine> _cameraEngines;
        private readonly ConcurrentDictionary<string, Task> _initializationTasks;
        private readonly SemaphoreSlim _initializationSemaphore;
        private readonly int _maxEngines;
        private bool _disposed = false;
        
        // 성능 통계
        private readonly ConcurrentDictionary<string, long> _processedFrames;
        private readonly ConcurrentDictionary<string, double> _totalInferenceTime;
        
        // 이벤트
        public event EventHandler<ObjectDetectionEventArgs>? ObjectDetected;
        public event EventHandler<ModelStatusEventArgs>? ModelStatusChanged;
        
        // 속성
        public IReadOnlyDictionary<string, bool> CameraEngineStatus => 
            _cameraEngines.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.IsLoaded);
        
        public int ActiveEngineCount => _cameraEngines.Count(kvp => kvp.Value.IsLoaded);
        
        public MultiCameraAIService(int maxEngines = 4)
        {
            _maxEngines = maxEngines;
            _cameraEngines = new ConcurrentDictionary<string, YOLOv8Engine>();
            _initializationTasks = new ConcurrentDictionary<string, Task>();
            _initializationSemaphore = new SemaphoreSlim(1, 1);
            _processedFrames = new ConcurrentDictionary<string, long>();
            _totalInferenceTime = new ConcurrentDictionary<string, double>();
            
            System.Diagnostics.Debug.WriteLine($"MultiCameraAIService: 초기화 완료 (최대 엔진 수: {_maxEngines})");
        }
        
        /// <summary>
        /// 특정 카메라용 AI 엔진 초기화
        /// </summary>
        public async Task<bool> InitializeForCameraAsync(string cameraId, string? modelPath = null)
        {
            if (_disposed || string.IsNullOrEmpty(cameraId))
                return false;
            
            // 이미 초기화 중인지 확인
            if (_initializationTasks.ContainsKey(cameraId))
            {
                System.Diagnostics.Debug.WriteLine($"MultiCameraAI: Camera {cameraId} already initializing");
                return await _initializationTasks[cameraId].ContinueWith(t => t.IsCompletedSuccessfully);
            }
            
            // 최대 엔진 수 제한 확인
            if (_cameraEngines.Count >= _maxEngines)
            {
                System.Diagnostics.Debug.WriteLine($"MultiCameraAI: Maximum engine count ({_maxEngines}) reached");
                return false;
            }
            
            var initTask = InitializeEngineAsync(cameraId, modelPath);
            _initializationTasks[cameraId] = initTask;
            
            try
            {
                return await initTask;
            }
            finally
            {
                _initializationTasks.TryRemove(cameraId, out _);
            }
        }
        
        private async Task<bool> InitializeEngineAsync(string cameraId, string? modelPath)
        {
            await _initializationSemaphore.WaitAsync();
            
            try
            {
                // 이미 초기화된 경우 스킵
                if (_cameraEngines.ContainsKey(cameraId) && _cameraEngines[cameraId].IsLoaded)
                {
                    System.Diagnostics.Debug.WriteLine($"MultiCameraAI: Engine for camera {cameraId} already loaded");
                    return true;
                }
                
                System.Diagnostics.Debug.WriteLine($"MultiCameraAI: Initializing engine for camera {cameraId}");
                NotifyModelStatus(cameraId, ModelStatus.Loading, "AI 엔진 초기화 중...");
                
                var engine = new YOLOv8Engine();
                var success = await engine.InitializeAsync(modelPath, useGpu: true);
                
                if (success)
                {
                    _cameraEngines[cameraId] = engine;
                    _processedFrames[cameraId] = 0;
                    _totalInferenceTime[cameraId] = 0.0;
                    
                    var statusMessage = $"AI 엔진 로드 완료 ({engine.ExecutionProvider})";
                    NotifyModelStatus(cameraId, ModelStatus.Ready, statusMessage);
                    
                    System.Diagnostics.Debug.WriteLine($"MultiCameraAI: Engine for camera {cameraId} initialized successfully using {engine.ExecutionProvider}");
                    return true;
                }
                else
                {
                    engine?.Dispose();
                    NotifyModelStatus(cameraId, ModelStatus.Error, "AI 엔진 로드 실패");
                    System.Diagnostics.Debug.WriteLine($"MultiCameraAI: Failed to initialize engine for camera {cameraId}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                NotifyModelStatus(cameraId, ModelStatus.Error, $"AI 엔진 초기화 오류: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"MultiCameraAI: Engine initialization error for camera {cameraId}: {ex.Message}");
                return false;
            }
            finally
            {
                _initializationSemaphore.Release();
            }
        }
        
        /// <summary>
        /// 프레임 추론 실행 (카메라별 독립적)
        /// </summary>
        public async Task<DetectionResult[]> ProcessFrameAsync(string cameraId, Mat frame)
        {
            if (_disposed || frame == null || frame.Empty() || string.IsNullOrEmpty(cameraId))
                return Array.Empty<DetectionResult>();
            
            // 해당 카메라의 엔진 가져오기 (없으면 생성 시도)
            var engine = await GetOrCreateEngineAsync(cameraId);
            if (engine == null || !engine.IsLoaded)
            {
                System.Diagnostics.Debug.WriteLine($"MultiCameraAI: No engine available for camera {cameraId}");
                return Array.Empty<DetectionResult>();
            }
            
            try
            {
                var startTime = DateTime.Now;
                
                // 독립적인 추론 실행 (비동기 래퍼)
                var detections = await Task.Run(() => engine.InferFrame(frame, 0.7f, 0.45f));
                
                var inferenceTime = (DateTime.Now - startTime).TotalMilliseconds;
                
                // 통계 업데이트
                _processedFrames.AddOrUpdate(cameraId, 1, (key, value) => value + 1);
                _totalInferenceTime.AddOrUpdate(cameraId, inferenceTime, (key, value) => value + inferenceTime);
                
                // 검출 결과 이벤트 발생
                if (detections.Length > 0)
                {
                    ObjectDetected?.Invoke(this, new ObjectDetectionEventArgs
                    {
                        CameraId = cameraId,
                        Detections = detections,
                        ProcessingTime = inferenceTime
                    });
                    
                    System.Diagnostics.Debug.WriteLine($"MultiCameraAI: Camera {cameraId} - {detections.Length} objects detected in {inferenceTime:F1}ms");
                }
                
                return detections;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MultiCameraAI: Processing error for camera {cameraId}: {ex.Message}");
                return Array.Empty<DetectionResult>();
            }
        }
        
        /// <summary>
        /// 카메라용 엔진 가져오기 또는 생성
        /// </summary>
        private async Task<YOLOv8Engine?> GetOrCreateEngineAsync(string cameraId)
        {
            // 기존 엔진이 있고 로드된 상태라면 반환
            if (_cameraEngines.TryGetValue(cameraId, out var existingEngine) && existingEngine.IsLoaded)
            {
                return existingEngine;
            }
            
            // 엔진이 없거나 로드되지 않은 경우 새로 초기화 시도
            var success = await InitializeForCameraAsync(cameraId);
            if (success && _cameraEngines.TryGetValue(cameraId, out var newEngine))
            {
                return newEngine;
            }
            
            return null;
        }
        
        /// <summary>
        /// 특정 카메라의 엔진 제거
        /// </summary>
        public void RemoveCameraEngine(string cameraId)
        {
            if (_cameraEngines.TryRemove(cameraId, out var engine))
            {
                engine?.Dispose();
                _processedFrames.TryRemove(cameraId, out _);
                _totalInferenceTime.TryRemove(cameraId, out _);
                
                NotifyModelStatus(cameraId, ModelStatus.Unloaded, "AI 엔진 언로드됨");
                System.Diagnostics.Debug.WriteLine($"MultiCameraAI: Engine for camera {cameraId} removed");
            }
        }
        
        /// <summary>
        /// 모든 엔진 제거
        /// </summary>
        public void RemoveAllEngines()
        {
            var cameraIds = _cameraEngines.Keys.ToList();
            foreach (var cameraId in cameraIds)
            {
                RemoveCameraEngine(cameraId);
            }
        }
        
        /// <summary>
        /// 성능 통계 가져오기
        /// </summary>
        public Dictionary<string, (long ProcessedFrames, double AverageInferenceTime)> GetPerformanceStats()
        {
            var stats = new Dictionary<string, (long ProcessedFrames, double AverageInferenceTime)>();
            
            foreach (var cameraId in _cameraEngines.Keys)
            {
                var processedFrames = _processedFrames.GetValueOrDefault(cameraId, 0);
                var totalTime = _totalInferenceTime.GetValueOrDefault(cameraId, 0.0);
                var avgTime = processedFrames > 0 ? totalTime / processedFrames : 0.0;
                
                stats[cameraId] = (processedFrames, avgTime);
            }
            
            return stats;
        }
        
        private void NotifyModelStatus(string cameraId, ModelStatus status, string message)
        {
            ModelStatusChanged?.Invoke(this, new ModelStatusEventArgs
            {
                Model = null,
                CameraId = cameraId,
                Status = status,
                Message = message
            });
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            
            System.Diagnostics.Debug.WriteLine("MultiCameraAI: Disposing all engines...");
            
            // 모든 엔진 정리
            RemoveAllEngines();
            
            // 리소스 정리
            _initializationSemaphore?.Dispose();
            
            System.Diagnostics.Debug.WriteLine("MultiCameraAI: Disposed");
        }
    }
}