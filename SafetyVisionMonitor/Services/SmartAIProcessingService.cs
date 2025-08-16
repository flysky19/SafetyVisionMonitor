using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using SafetyVisionMonitor.Models;
using SafetyVisionMonitor.Shared.Models;
using SafetyVisionMonitor.Services;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 선택적 AI 처리 서비스 - 필요할 때만 AI 처리하여 성능 최적화
    /// 안전성은 유지하면서 불필요한 처리를 최소화
    /// </summary>
    public class SmartAIProcessingService : IDisposable
    {
        // 처리 레벨 정의
        public enum ProcessingLevel
        {
            None = 0,           // 처리 안함
            MotionOnly = 1,     // 모션 감지만
            ObjectDetection = 2, // 객체 검출
            FullAnalysis = 3    // 전체 분석 (포즈, 세그멘테이션 등)
        }

        // 카메라별 상태 관리
        private class CameraState
        {
            public string CameraId { get; set; } = "";
            public Mat? LastFrame { get; set; }
            public Mat? BackgroundModel { get; set; }
            public DateTime LastMotionTime { get; set; } = DateTime.MinValue;
            public DateTime LastAIProcessTime { get; set; } = DateTime.MinValue;
            public int ConsecutiveNoMotionFrames { get; set; } = 0;
            public bool HasPersons { get; set; } = false;
            public ProcessingLevel CurrentLevel { get; set; } = ProcessingLevel.MotionOnly;
            public DateTime LastLevelChange { get; set; } = DateTime.MinValue; // 레벨 변경 시간
            public List<DetectionResult> LastDetections { get; set; } = new();
            
            // 성능 지표
            public int FramesProcessed { get; set; } = 0;
            public int MotionDetections { get; set; } = 0;
            public int AIProcessings { get; set; } = 0;
        }

        private readonly ConcurrentDictionary<string, CameraState> _cameraStates = new();
        private readonly AIInferenceService _aiService;
        private readonly Timer _periodicCheckTimer;
        
        // 설정 값들 (GPU 최대 활용을 위한 적극적 설정)
        private readonly TimeSpan MaxNoMotionTime = TimeSpan.FromSeconds(3);  // 3초로 더 단축
        private readonly TimeSpan MinAIInterval = TimeSpan.FromMilliseconds(200); // 0.2초로 단축 (GPU이니까 빠르게)
        private readonly TimeSpan PeriodicSafetyCheck = TimeSpan.FromSeconds(5); // 5초로 단축 (GPU 있으니까)
        private readonly double MotionThreshold = 100.0; // 임계값 더 낮춤 (더 민감하게)
        private readonly int MaxConsecutiveNoMotion = 3;  // 3프레임으로 단축 (더 빠른 반응)
        private readonly TimeSpan LevelStabilizationTime = TimeSpan.FromMilliseconds(500); // 0.5초로 단축

        public event EventHandler<SmartDetectionResult>? SmartDetectionCompleted;

        public SmartAIProcessingService(AIInferenceService aiService)
        {
            _aiService = aiService;
            
            // 주기적 안전 체크 타이머 (위험을 놓치지 않기 위함)
            _periodicCheckTimer = new Timer(PeriodicSafetyCheck_Elapsed, null, 
                PeriodicSafetyCheck, PeriodicSafetyCheck);
                
            Debug.WriteLine("SmartAIProcessingService: 선택적 AI 처리 시스템 초기화 완료");
        }

        /// <summary>
        /// 스마트 프레임 처리 - 상황에 따라 적절한 레벨로 처리
        /// </summary>
        public async Task<SmartDetectionResult> ProcessFrameSmartAsync(string cameraId, Mat frame)
        {
            var state = _cameraStates.GetOrAdd(cameraId, _ => new CameraState { CameraId = cameraId });
            state.FramesProcessed++;

            var result = new SmartDetectionResult
            {
                CameraId = cameraId,
                ProcessingLevel = ProcessingLevel.None,
                Detections = Array.Empty<DetectionResult>(),
                ProcessingTimeMs = 0
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // 1단계: 모션 감지 (모든 프레임)
                var hasMotion = DetectMotion(state, frame);
                
                if (hasMotion)
                {
                    state.LastMotionTime = DateTime.Now;
                    state.ConsecutiveNoMotionFrames = 0;
                    state.MotionDetections++;
                    
                    // 움직임 감지 시 처리 레벨 상승 (안정화 시간 체크)
                    var timeSinceLastChange = DateTime.Now - state.LastLevelChange;
                    if (state.CurrentLevel < ProcessingLevel.ObjectDetection && 
                        timeSinceLastChange >= LevelStabilizationTime)
                    {
                        state.CurrentLevel = ProcessingLevel.ObjectDetection;
                        state.LastLevelChange = DateTime.Now;
                        Debug.WriteLine($"SmartAI: {cameraId} - 움직임 감지, AI 처리 레벨 상승 (안정화됨)");
                    }
                }
                else
                {
                    state.ConsecutiveNoMotionFrames++;
                    
                    // GPU 유지를 위해 레벨 하향을 더 보수적으로
                    if (state.ConsecutiveNoMotionFrames > (MaxConsecutiveNoMotion * 2) && !state.HasPersons)
                    {
                        // GPU 활용을 위해 ObjectDetection 레벨 유지
                        if (state.CurrentLevel > ProcessingLevel.ObjectDetection)
                        {
                            state.CurrentLevel = ProcessingLevel.ObjectDetection; // MotionOnly 대신 ObjectDetection 유지
                            Debug.WriteLine($"SmartAI: {cameraId} - 장시간 무움직임, GPU 활용 유지를 위해 ObjectDetection 레벨 유지");
                        }
                    }
                }

                // 2단계: AI 처리 결정 (GPU 활용 극대화)
                var shouldProcessAI = ShouldProcessWithAI(state, hasMotion);
                
                // GPU 활용을 위해 더 적극적으로 AI 처리
                if (shouldProcessAI || state.CurrentLevel >= ProcessingLevel.ObjectDetection)
                {
                    // 안전 모니터링 구역 설정에 따른 객체 검출 실행
                    var detections = await ProcessWithAI(state, frame, cameraId);
                    result.Detections = detections;
                    result.ProcessingLevel = state.CurrentLevel;
                    state.LastAIProcessTime = DateTime.Now;
                    state.AIProcessings++;
                    
                    // 사람 검출 여부 업데이트
                    state.HasPersons = detections.Any(d => d.ClassName.Contains("person"));
                    state.LastDetections = detections.ToList();
                    
                    // 사람이 있으면 처리 레벨 유지/상승
                    if (state.HasPersons && state.CurrentLevel < ProcessingLevel.FullAnalysis)
                    {
                        state.CurrentLevel = ProcessingLevel.FullAnalysis;
                    }
                }
                else
                {
                    // AI 처리 없이 이전 결과 재사용 (일정 시간 내)
                    if (DateTime.Now - state.LastAIProcessTime < TimeSpan.FromSeconds(5))
                    {
                        result.Detections = state.LastDetections.ToArray();
                        result.ProcessingLevel = ProcessingLevel.None; // 재사용임을 표시
                    }
                }

                result.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                result.MotionDetected = hasMotion;
                result.HasPersons = state.HasPersons;

                // 통계 로깅 (매 100프레임마다)
                if (state.FramesProcessed % 100 == 0)
                {
                    var motionRate = (double)state.MotionDetections / state.FramesProcessed * 100;
                    var aiRate = (double)state.AIProcessings / state.FramesProcessed * 100;
                    Debug.WriteLine($"SmartAI Stats [{cameraId}]: Motion {motionRate:F1}%, AI {aiRate:F1}%, Level {state.CurrentLevel}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SmartAI Error [{cameraId}]: {ex.Message}");
                result.Error = ex.Message;
            }
            finally
            {
                stopwatch.Stop();
                SmartDetectionCompleted?.Invoke(this, result);
            }

            return result;
        }

        /// <summary>
        /// 배경 차분을 이용한 모션 감지
        /// </summary>
        private bool DetectMotion(CameraState state, Mat currentFrame)
        {
            try
            {
                // 그레이스케일 변환
                using var grayFrame = new Mat();
                Cv2.CvtColor(currentFrame, grayFrame, ColorConversionCodes.BGR2GRAY);
                
                // 가우시안 블러 적용
                using var blurredFrame = new Mat();
                Cv2.GaussianBlur(grayFrame, blurredFrame, new Size(21, 21), 0);

                // 배경 모델 초기화
                if (state.BackgroundModel == null)
                {
                    state.BackgroundModel = blurredFrame.Clone();
                    return false;
                }

                // 배경 차분
                using var diff = new Mat();
                Cv2.Absdiff(state.BackgroundModel, blurredFrame, diff);
                
                // 이진화
                using var threshold = new Mat();
                Cv2.Threshold(diff, threshold, 25, 255, ThresholdTypes.Binary);
                
                // 모폴로지 연산으로 노이즈 제거
                using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
                using var morphed = new Mat();
                Cv2.MorphologyEx(threshold, morphed, MorphTypes.Close, kernel);
                
                // 움직임 영역 계산
                var motionArea = Cv2.CountNonZero(morphed);
                
                // 배경 모델 업데이트 (학습률 0.01)
                Cv2.AddWeighted(state.BackgroundModel, 0.99, blurredFrame, 0.01, 0, state.BackgroundModel);
                
                return motionArea > MotionThreshold;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Motion detection error: {ex.Message}");
                return true; // 오류 시 안전하게 모션 있다고 가정
            }
        }

        /// <summary>
        /// AI 처리 필요성 판단 (GPU 최적화)
        /// </summary>
        private bool ShouldProcessWithAI(CameraState state, bool hasMotion)
        {
            var timeSinceLastAI = DateTime.Now - state.LastAIProcessTime;
            
            // GPU 활용 극대화를 위한 적극적 AI 처리 조건들
            return hasMotion && timeSinceLastAI >= MinAIInterval || // 움직임 + 최소 간격
                   state.HasPersons && timeSinceLastAI >= TimeSpan.FromMilliseconds(500) || // 사람 있음 + 0.5초 간격
                   state.CurrentLevel >= ProcessingLevel.ObjectDetection || // 객체 검출 레벨 이상이면 계속 처리
                   timeSinceLastAI >= TimeSpan.FromSeconds(3); // 최대 3초마다는 반드시 체크 (GPU 유지)
        }

        /// <summary>
        /// AI 처리 실행
        /// </summary>
        private async Task<DetectionResult[]> ProcessWithAI(CameraState state, Mat frame)
        {
            try
            {
                switch (state.CurrentLevel)
                {
                    case ProcessingLevel.ObjectDetection:
                        // 기본 객체 검출만
                        return await _aiService.InferFrameAsync(state.CameraId, frame);
                        
                    case ProcessingLevel.FullAnalysis:
                        // 전체 분석 (포즈, 세그멘테이션 등)
                        var detections = await _aiService.InferFrameAsync(state.CameraId, frame);
                        // TODO: 포즈 분석, 세그멘테이션 추가
                        return detections;
                        
                    default:
                        return Array.Empty<DetectionResult>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AI processing error: {ex.Message}");
                return Array.Empty<DetectionResult>();
            }
        }

        /// <summary>
        /// 주기적 안전 체크 - 위험 상황을 놓치지 않기 위함
        /// </summary>
        private async void PeriodicSafetyCheck_Elapsed(object? state)
        {
            try
            {
                foreach (var cameraState in _cameraStates.Values)
                {
                    var timeSinceLastCheck = DateTime.Now - cameraState.LastAIProcessTime;
                    
                    // 너무 오랫동안 AI 처리를 안했으면 강제 실행
                    if (timeSinceLastCheck > PeriodicSafetyCheck && cameraState.LastFrame != null)
                    {
                        Debug.WriteLine($"SmartAI: {cameraState.CameraId} - 주기적 안전 체크 실행");
                        await ProcessWithAI(cameraState, cameraState.LastFrame);
                        cameraState.LastAIProcessTime = DateTime.Now;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Periodic safety check error: {ex.Message}");
            }
        }

        /// <summary>
        /// AI 처리 실행 (안전 모니터링 구역 최적화 적용)
        /// </summary>
        private async Task<DetectionResult[]> ProcessWithAI(CameraState state, Mat frame, string cameraId)
        {
            try
            {
                // 안전 모니터링 구역 설정 확인
                var safetySettings = SafetySettingsManager.Instance.CurrentSettings;
                
                if (safetySettings.IsSafetyMonitoringZoneEnabled)
                {
                    // 안전 구역 정보 가져오기 (아크릴 설정에서)
                    var safetyZonePoints = await GetSafetyZonePointsAsync(cameraId);
                    
                    if (safetyZonePoints != null && safetyZonePoints.Count >= 3)
                    {
                        // 크롭 기반 추론 (성능 최적화)
                        Debug.WriteLine($"SmartAI: Using safety zone crop optimization for {cameraId}");
                        return await _aiService.InferFrameWithSafetyZoneAsync(cameraId, frame, safetyZonePoints);
                    }
                }
                
                // 기본 전체 프레임 추론
                Debug.WriteLine($"SmartAI: Using full frame inference for {cameraId}");
                return await _aiService.InferFrameAsync(cameraId, frame);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SmartAI ProcessWithAI error [{cameraId}]: {ex.Message}");
                return Array.Empty<DetectionResult>();
            }
        }
        
        /// <summary>
        /// 카메라의 안전 구역 좌표 가져오기
        /// </summary>
        private async Task<List<System.Drawing.Point>?> GetSafetyZonePointsAsync(string cameraId)
        {
            try
            {
                // 아크릴 설정에서 안전 구역 좌표 로드
                var configPath = System.IO.Path.Combine("Config", "Acrylic", $"{cameraId}_boundary.json");
                
                if (System.IO.File.Exists(configPath))
                {
                    var json = await System.IO.File.ReadAllTextAsync(configPath);
                    var boundaryData = System.Text.Json.JsonSerializer.Deserialize<BoundaryData>(json);
                    
                    if (boundaryData?.Points != null && boundaryData.Points.Count >= 3)
                    {
                        // WPF Point를 System.Drawing.Point로 변환
                        return boundaryData.Points.Select(p => new System.Drawing.Point((int)p.X, (int)p.Y)).ToList();
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading safety zone for {cameraId}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 처리 통계 조회
        /// </summary>
        public Dictionary<string, object> GetProcessingStatistics()
        {
            var stats = new Dictionary<string, object>();
            
            foreach (var kvp in _cameraStates)
            {
                var state = kvp.Value;
                if (state.FramesProcessed > 0)
                {
                    stats[kvp.Key] = new
                    {
                        FramesProcessed = state.FramesProcessed,
                        MotionRate = (double)state.MotionDetections / state.FramesProcessed * 100,
                        AIProcessingRate = (double)state.AIProcessings / state.FramesProcessed * 100,
                        CurrentLevel = state.CurrentLevel.ToString(),
                        HasPersons = state.HasPersons
                    };
                }
            }
            
            return stats;
        }

        public void Dispose()
        {
            _periodicCheckTimer?.Dispose();
            
            foreach (var state in _cameraStates.Values)
            {
                state.LastFrame?.Dispose();
                state.BackgroundModel?.Dispose();
            }
            
            _cameraStates.Clear();
        }
    }

    /// <summary>
    /// 경계선 데이터 (JSON 직렬화용)
    /// </summary>
    internal class BoundaryData
    {
        public List<System.Windows.Point>? Points { get; set; }
        public string TrackingMode { get; set; } = "InteriorOnly";
    }
    
    /// <summary>
    /// 스마트 검출 결과
    /// </summary>
    public class SmartDetectionResult
    {
        public string CameraId { get; set; } = "";
        public SmartAIProcessingService.ProcessingLevel ProcessingLevel { get; set; }
        public DetectionResult[] Detections { get; set; } = Array.Empty<DetectionResult>();
        public bool MotionDetected { get; set; }
        public bool HasPersons { get; set; }
        public long ProcessingTimeMs { get; set; }
        public string? Error { get; set; }
    }
}