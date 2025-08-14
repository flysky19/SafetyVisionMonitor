using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OpenCvSharp;
using SafetyVisionMonitor.Helpers;
using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionMonitor.Services
{
    public class CameraService : IDisposable
    {
        private readonly ConcurrentDictionary<string, CameraConnection> _connections = new();
        private readonly int _maxCameras;
        private readonly ConcurrentDictionary<string, List<DetectionResult>> _latestDetections = new();
        private readonly ConcurrentDictionary<string, Mat> _latestFrames = new(); // 최신 프레임 캐시
        private readonly ConcurrentDictionary<string, Mat> _processedFrames = new(); // 개인정보 보호 처리된 프레임 캐시
        public event EventHandler<CameraFrameEventArgs>? FrameReceived;
        public event EventHandler<CameraFrameEventArgs>? FrameReceivedForAI; // AI용 원본 프레임
        public event EventHandler<CameraFrameEventArgs>? FrameReceivedForUI; // UI용 저화질 프레임
        public event EventHandler<CameraFrameEventArgs>? ProcessedFrameReceived; // 개인정보 보호 처리된 프레임
        public event EventHandler<CameraConnectionEventArgs>? ConnectionChanged;
        
        public CameraService()
        {
            _maxCameras = App.Configuration.GetValue<int>("AppSettings:MaxCameras", 4);
            
            // OpenCV 백엔드는 App.xaml.cs에서 전역 설정됨
            
            // AI 서비스의 객체 검출 이벤트 구독
            if (App.AIPipeline != null)
            {
                App.AIPipeline.ObjectDetected += OnObjectDetected;
            }
        }
        
        public async Task<bool> ConnectCamera(Camera camera)
        {
            // 미사용 카메라는 연결할 수 없음
            if (!camera.IsEnabled)
            {
                var cameraName = string.IsNullOrEmpty(camera.Name) ? camera.Id : camera.Name;
                throw new InvalidOperationException($"카메라 '{cameraName}'은(는) 미사용 상태입니다. 먼저 사용함으로 변경해주세요.");
            }
            
            if (_connections.Count >= _maxCameras)
            {
                throw new InvalidOperationException($"최대 {_maxCameras}대까지만 연결 가능합니다.");
            }
            
            if (_connections.ContainsKey(camera.Id))
            {
                return false;
            }
            
            var connection = new CameraConnection(camera);
            connection.FrameReceived += OnFrameReceived;
            
            var success = await connection.ConnectAsync();
            
            if (success)
            {
                _connections[camera.Id] = connection;
                camera.IsConnected = true;
                ConnectionChanged?.Invoke(this, new CameraConnectionEventArgs(camera.Id, true));
            }
            
            return success;
        }
        
        public void DisconnectCamera(string cameraId)
        {
            if (_connections.TryRemove(cameraId, out var connection))
            {
                connection.FrameReceived -= OnFrameReceived;
                connection.Dispose();
                ConnectionChanged?.Invoke(this, new CameraConnectionEventArgs(cameraId, false));
            }
        }
        
        public List<Camera> GetConnectedCameras()
        {
            return _connections.Values
                .Select(c => c.Camera)
                .ToList();
        }
        
        /// <summary>
        /// 활성화된 카메라 목록 반환 (AI/트래킹 처리용)
        /// </summary>
        public List<Camera> GetActiveCameras()
        {
            return _connections.Values
                .Where(c => c.Camera.IsEnabled)
                .Select(c => c.Camera)
                .ToList();
        }
        
        public void UpdateCameraSettings(string cameraId, Camera settings)
        {
            if (_connections.TryGetValue(cameraId, out var connection))
            {
                connection.UpdateSettings(settings);
            }
        }
        
        public Camera? GetCameraSettings(string cameraId)
        {
            if (_connections.TryGetValue(cameraId, out var connection))
            {
                return connection.GetCurrentSettings();
            }
            return null;
        }
        
        private void OnFrameReceived(object? sender, CameraFrameEventArgs e)
        {
            //System.Diagnostics.Debug.WriteLine($"CameraService: Frame received from {e.CameraId}");
            
            // 미사용 카메라의 프레임은 무시
            if (sender is CameraConnection connection && !connection.Camera.IsEnabled)
            {
                e.Frame?.Dispose();
                return;
            }
    
            if (e.Frame != null && !e.Frame.Empty())
            {
                // 하이브리드 프레임 배포 시스템
                ProcessHybridFrameDistribution(e);
                
                // 기존 호환성을 위한 레거시 이벤트 (Deprecated 예정)
                var legacyHandlers = FrameReceived?.GetInvocationList();
                if (legacyHandlers != null)
                {
                    foreach (var handler in legacyHandlers)
                    {
                        var frameClone = e.Frame.Clone();
                        try
                        {
                            ((EventHandler<CameraFrameEventArgs>)handler).Invoke(this, 
                                new CameraFrameEventArgs(e.CameraId, frameClone));
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Legacy frame handler error: {ex.Message}");
                            frameClone.Dispose();
                        }
                    }
                }
                
                // 원본 프레임 해제
                e.Frame.Dispose();
            }
        }
        
        private void ProcessHybridFrameDistribution(CameraFrameEventArgs originalFrame)
        {
            try
            {
                // 1. AI용 원본 프레임 배포 (고화질, 원본 FPS)
                DistributeAIFrame(originalFrame);
                
                // 2. UI용 저화질 프레임 배포 (저화질, 제한된 FPS)
                DistributeUIFrame(originalFrame);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Hybrid frame distribution error: {ex.Message}");
            }
        }
        
        private void DistributeAIFrame(CameraFrameEventArgs originalFrame)
        {
            // 최신 프레임 캐시 업데이트 (MediaCaptureService용)
            UpdateLatestFrameCache(originalFrame.CameraId, originalFrame.Frame);
            
            // AI 파이프라인으로 직접 전송 (더 효율적)
            if (App.AIPipeline != null)
            {
                try
                {
                    var aiFrame = originalFrame.Frame.Clone();
                    var queued = App.AIPipeline.QueueFrame(originalFrame.CameraId, aiFrame);
                    
                    if (!queued)
                    {
                        // 큐 실패 시 프레임 해제
                        aiFrame.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AI pipeline queue error: {ex.Message}");
                }
            }
            
            // 기존 핸들러들도 지원 (하위 호환성)
            var aiHandlers = FrameReceivedForAI?.GetInvocationList();
            if (aiHandlers != null)
            {
                foreach (var handler in aiHandlers)
                {
                    // AI용은 원본 해상도 유지
                    var aiFrame = originalFrame.Frame.Clone();
                    try
                    {
                        ((EventHandler<CameraFrameEventArgs>)handler).Invoke(this, 
                            new CameraFrameEventArgs(originalFrame.CameraId, aiFrame));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"AI frame handler error: {ex.Message}");
                        aiFrame.Dispose();
                    }
                }
            }
        }
        
        private void DistributeUIFrame(CameraFrameEventArgs originalFrame)
        {
            var uiHandlers = FrameReceivedForUI?.GetInvocationList();
            if (uiHandlers != null)
            {
                // UI용 프레임 스키핑 (3프레임마다 1번)
                if (!ShouldUpdateUI(originalFrame.CameraId))
                    return;
                
                // 최신 검출 결과 가져오기
                var detections = GetLatestDetections(originalFrame.CameraId);
                
                // 추적 정보 가져오기
                var trackedPersons = App.TrackingService?.GetLatestTrackedPersons(originalFrame.CameraId);
                var trackingConfig = App.TrackingService?.GetTrackingConfiguration();
                
                foreach (var handler in uiHandlers)
                {
                    // UI용은 해상도 축소 (1/4 크기)
                    var uiFrame = CreateLowResolutionFrame(originalFrame.Frame);
                    
                    // 새로운 기능 기반 렌더링 파이프라인 사용
                    try
                    {
                        // 축소된 프레임에 맞게 검출 결과 좌표 조정
                        var scaledDetections = detections.Select(d => new DetectionResult
                        {
                            Label = d.Label,
                            ClassName = d.ClassName,
                            Confidence = d.Confidence,
                            TrackingId = d.TrackingId,
                            CameraId = d.CameraId,
                            ClassId = d.ClassId,
                            Timestamp = d.Timestamp,
                            Location = d.Location,
                            BoundingBox = new System.Drawing.RectangleF(
                                d.BoundingBox.X * 0.5f,
                                d.BoundingBox.Y * 0.5f,
                                d.BoundingBox.Width * 0.5f,
                                d.BoundingBox.Height * 0.5f
                            )
                        }).ToArray();

                        // 프레임 처리 컨텍스트 생성
                        var context = new Features.FrameProcessingContext
                        {
                            CameraId = originalFrame.CameraId,
                            Detections = scaledDetections,
                            TrackedPersons = trackedPersons,
                            TrackingConfig = trackingConfig,
                            Scale = 0.5f // UI는 1/2 스케일
                        };

                        // 오버레이 렌더링 파이프라인 적용
                        if (App.OverlayPipeline != null)
                        {
                            uiFrame = App.OverlayPipeline.ProcessFrame(uiFrame, context);
                            System.Diagnostics.Debug.WriteLine($"CameraService: Applied overlay pipeline for {originalFrame.CameraId}");
                        }
                        else
                        {
                            // 폴백: 기존 방식 사용
                            System.Diagnostics.Debug.WriteLine($"CameraService: Using fallback rendering for {originalFrame.CameraId}");
                            if (detections.Count > 0 || trackedPersons?.Count > 0)
                            {
                                RenderFrameWithTracking(uiFrame, detections, trackedPersons, trackingConfig);
                            }
                        }
                    }
                    catch (Exception pipelineEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"CameraService: Pipeline error for {originalFrame.CameraId}: {pipelineEx.Message}");
                        
                        // 오류 시 기존 방식으로 폴백
                        if (detections.Count > 0 || trackedPersons?.Count > 0)
                        {
                            RenderFrameWithTracking(uiFrame, detections, trackedPersons, trackingConfig);
                        }
                    }
                    
                    try
                    {
                        ((EventHandler<CameraFrameEventArgs>)handler).Invoke(this, 
                            new CameraFrameEventArgs(originalFrame.CameraId, uiFrame));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"UI frame handler error: {ex.Message}");
                        uiFrame.Dispose();
                    }
                }
            }
        }
        
        private readonly Dictionary<string, int> _uiFrameCounters = new();
        private bool ShouldUpdateUI(string cameraId)
        {
            if (!_uiFrameCounters.ContainsKey(cameraId))
                _uiFrameCounters[cameraId] = 0;
            
            _uiFrameCounters[cameraId]++;
            
            // 3프레임마다 1번 UI 업데이트 (FPS 1/3로 감소)
            return _uiFrameCounters[cameraId] % 3 == 0;
        }
        
        private Mat CreateLowResolutionFrame(Mat originalFrame)
        {
            // 해상도 1/2로 축소 (면적은 1/4로 감소)
            var targetWidth = originalFrame.Width / 2;
            var targetHeight = originalFrame.Height / 2;
            
            var resizedFrame = new Mat();
            Cv2.Resize(originalFrame, resizedFrame, new OpenCvSharp.Size(targetWidth, targetHeight), 
                      interpolation: InterpolationFlags.Linear);
            
            //System.Diagnostics.Debug.WriteLine($"CameraService: UI frame resized: {originalFrame.Width}x{originalFrame.Height} → {targetWidth}x{targetHeight}");
            
            return resizedFrame;
        }
        
        /// <summary>
        /// 축소된 프레임에 추적 정보와 검출 결과를 렌더링
        /// </summary>
        private void RenderFrameWithTracking(Mat frame, List<DetectionResult> detections, 
            List<TrackedPerson>? trackedPersons, TrackingConfiguration? trackingConfig)
        {
            // 축소된 프레임에 맞게 좌표 조정 (원본의 1/2 크기)
            var scale = 0.5f;
            
            // 추적 경로 그리기 (검출 박스보다 먼저)
            if (trackedPersons != null && trackingConfig?.ShowTrackingPath == true)
            {
                DrawScaledTrackingPaths(frame, trackedPersons, trackingConfig, scale);
            }
            
            // 검출 결과 그리기
            DrawScaledDetectionBoxes(frame, detections, trackingConfig, scale);
        }
        
        /// <summary>
        /// 축소된 프레임에 검출 박스 그리기
        /// </summary>
        private void DrawScaledDetectionBoxes(Mat frame, List<DetectionResult> detections, 
            TrackingConfiguration? trackingConfig, float scale)
        {
            // 정적 속성에서 디버그 설정 가져오기
            var showAllDetections = ViewModels.DashboardViewModel.StaticShowAllDetections;
            var showDetailedInfo = ViewModels.DashboardViewModel.StaticShowDetailedInfo;
            
            foreach (var detection in detections)
            {
                // 디버그 모드가 아니면 사람만 표시
                if (!showAllDetections && detection.Label != "person")
                    continue;
                
                // 바운딩 박스 좌표 스케일 조정
                var rect = new Rect(
                    (int)(detection.BoundingBox.X * scale),
                    (int)(detection.BoundingBox.Y * scale),
                    (int)(detection.BoundingBox.Width * scale),
                    (int)(detection.BoundingBox.Height * scale)
                );

                // 색상 결정 (사람은 빨간색, 다른 객체는 다양한 색상)
                var color = GetDetectionColor(detection.Label);

                // 박스 그리기 (디버그 모드에서는 더 두껍게)
                var thickness = showDetailedInfo ? 3 : 2;
                Cv2.Rectangle(frame, rect, color, thickness);

                // 라벨 텍스트 (트래킹 ID 설정 반영)
                var label = showDetailedInfo 
                    ? $"{detection.DisplayName} {detection.Confidence:P1} [{detection.ClassId}]"
                    : detection.TrackingId.HasValue && (trackingConfig?.ShowTrackingId ?? true)
                        ? $"{detection.DisplayName} ID:{detection.TrackingId} ({detection.Confidence:P0})"
                        : $"{detection.DisplayName} ({detection.Confidence:P0})";
                
                // 텍스트 크기 계산
                var fontSize = showDetailedInfo ? 0.6 : 0.5;
                var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, fontSize, 1, out var baseline);
                
                // 텍스트 배경 위치 (박스 위에 표시)
                var textY = rect.Y - 5;
                if (textY < textSize.Height + 5)
                {
                    textY = rect.Y + rect.Height + textSize.Height + 5;
                }
                
                var textRect = new Rect(
                    rect.X,
                    textY - textSize.Height - 5,
                    textSize.Width + 5,
                    textSize.Height + 5
                );

                // 텍스트 배경 그리기
                Cv2.Rectangle(frame, textRect, color, -1);

                // 텍스트 그리기
                Cv2.PutText(frame, label,
                    new OpenCvSharp.Point(rect.X + 2, textY - 5),
                    HersheyFonts.HersheySimplex,
                    fontSize,
                    new Scalar(255, 255, 255), // 흰색 텍스트
                    1);
                
                // 디버그 모드에서는 중심점과 ID 표시
                if (showDetailedInfo)
                {
                    var center = new OpenCvSharp.Point(
                        rect.X + rect.Width / 2,
                        rect.Y + rect.Height / 2
                    );
                    Cv2.Circle(frame, center, 4, color, -1);
                    
                    // 검출 시간 표시 (오른쪽 하단)
                    var timeText = detection.Timestamp.ToString("mm:ss.fff");
                    Cv2.PutText(frame, timeText,
                        new OpenCvSharp.Point(rect.X + rect.Width - 50, rect.Y + rect.Height - 5),
                        HersheyFonts.HersheySimplex,
                        0.4,
                        new Scalar(255, 255, 0), // 노란색
                        1);
                }
            }
        }
        
        /// <summary>
        /// 축소된 프레임에 추적 경로 그리기
        /// </summary>
        private void DrawScaledTrackingPaths(Mat frame, List<TrackedPerson> trackedPersons, 
            TrackingConfiguration config, float scale)
        {
            foreach (var person in trackedPersons.Where(p => p.IsActive))
            {
                if (person.TrackingHistory == null || person.TrackingHistory.Count < 2)
                    continue;
                    
                // 경로 표시 길이 제한
                var pathLength = Math.Min(person.TrackingHistory.Count, config.PathDisplayLength);
                var recentPath = person.TrackingHistory.TakeLast(pathLength).ToList();
                
                if (recentPath.Count < 2) continue;
                
                // 추적 ID별 색상 결정 (고유한 색상)
                var colors = new[]
                {
                    new Scalar(255, 0, 0),    // 빨강
                    new Scalar(0, 255, 0),    // 초록
                    new Scalar(0, 0, 255),    // 파랑
                    new Scalar(255, 255, 0),  // 노랑
                    new Scalar(255, 0, 255),  // 마젠타
                    new Scalar(0, 255, 255),  // 시안
                    new Scalar(255, 128, 0),  // 주황
                    new Scalar(128, 0, 255)   // 보라
                };
                
                var colorIndex = person.TrackingId % colors.Length;
                var pathColor = colors[colorIndex];
                
                // 경로 선 그리기 (스케일 적용)
                for (int i = 0; i < recentPath.Count - 1; i++)
                {
                    var startPoint = new OpenCvSharp.Point(
                        (int)(recentPath[i].X * scale), 
                        (int)(recentPath[i].Y * scale));
                    var endPoint = new OpenCvSharp.Point(
                        (int)(recentPath[i + 1].X * scale), 
                        (int)(recentPath[i + 1].Y * scale));
                    
                    // 선의 두께는 최신 경로일수록 두껍게
                    var thickness = Math.Max(1, 3 - (recentPath.Count - i - 1) / 3);
                    
                    Cv2.Line(frame, startPoint, endPoint, pathColor, thickness);
                }
                
                // 현재 위치에 원 그리기 (스케일 적용)
                if (recentPath.Any())
                {
                    var currentPos = recentPath.Last();
                    var centerPoint = new OpenCvSharp.Point(
                        (int)(currentPos.X * scale), 
                        (int)(currentPos.Y * scale));
                    Cv2.Circle(frame, centerPoint, 3, pathColor, -1);
                    
                    // 트래킹 ID 표시 (설정이 활성화된 경우)
                    if (config.ShowTrackingId)
                    {
                        var idText = $"#{person.TrackingId}";
                        var textPos = new OpenCvSharp.Point(
                            (int)(currentPos.X * scale) + 8, 
                            (int)(currentPos.Y * scale) - 8);
                        Cv2.PutText(frame, idText, textPos, HersheyFonts.HersheySimplex, 
                                  0.4, new Scalar(255, 255, 255), 1);
                    }
                }
            }
        }
        
        private Scalar GetDetectionColor(string label)
        {
            return label switch
            {
                "person" => new Scalar(0, 0, 255),      // 빨간색
                "car" => new Scalar(255, 0, 0),         // 파란색
                "truck" => new Scalar(255, 255, 0),     // 청록색
                "bicycle" => new Scalar(0, 255, 255),   // 노란색
                "motorcycle" => new Scalar(255, 0, 255), // 자홍색
                _ => new Scalar(0, 255, 0)               // 기본 녹색
            };
        }
        
        private void OnObjectDetected(object? sender, ObjectDetectionEventArgs e)
        {
            // 카메라별로 최신 검출 결과 저장
            _latestDetections[e.CameraId] = e.Detections.ToList();
        }
        
        public List<DetectionResult> GetLatestDetections(string cameraId)
        {
            return _latestDetections.TryGetValue(cameraId, out var detections) 
                ? detections 
                : new List<DetectionResult>();
        }
        
        /// <summary>
        /// 개인정보 보호가 적용된 프레임 업데이트 (MonitoringService에서 호출)
        /// </summary>
        public void UpdateProcessedFrame(string cameraId, Mat processedFrame)
        {
            try
            {
                // 기존 처리된 프레임이 있으면 해제
                if (_processedFrames.TryGetValue(cameraId, out var oldProcessedFrame))
                {
                    oldProcessedFrame?.Dispose();
                }
                
                // 새 처리된 프레임으로 교체 (복사본 저장)
                _processedFrames[cameraId] = processedFrame.Clone();
                
                // 처리된 프레임 이벤트 발생
                ProcessedFrameReceived?.Invoke(this, new CameraFrameEventArgs(cameraId, processedFrame.Clone()));
                
                System.Diagnostics.Debug.WriteLine($"CameraService: Updated processed frame for camera {cameraId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CameraService: Processed frame update error for {cameraId} - {ex.Message}");
            }
        }
        
        /// <summary>
        /// 특정 카메라의 개인정보 보호가 적용된 최신 프레임 가져오기
        /// </summary>
        public Mat? GetLatestProcessedFrame(string cameraId)
        {
            try
            {
                if (_processedFrames.TryGetValue(cameraId, out var frame) && frame != null && !frame.Empty())
                {
                    return frame.Clone(); // 복사본 반환
                }
                
                // 처리된 프레임이 없으면 원본 프레임 반환
                return GetLatestFrame(cameraId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CameraService: Processed frame retrieval error for {cameraId} - {ex.Message}");
                return GetLatestFrame(cameraId);
            }
        }
        
        /// <summary>
        /// 모든 연결된 카메라의 개인정보 보호가 적용된 최신 프레임 가져오기
        /// </summary>
        public Dictionary<string, Mat> GetAllLatestProcessedFrames()
        {
            var result = new Dictionary<string, Mat>();
            
            try
            {
                foreach (var connection in _connections.Keys)
                {
                    var processedFrame = GetLatestProcessedFrame(connection);
                    if (processedFrame != null)
                    {
                        result[connection] = processedFrame;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CameraService: Batch processed frame retrieval error - {ex.Message}");
            }
            
            return result;
        }

        public void Dispose()
        {
            // AI 이벤트 구독 해제
            if (App.AIPipeline != null)
            {
                App.AIPipeline.ObjectDetected -= OnObjectDetected;
            }
            
            foreach (var connection in _connections.Values)
            {
                connection.FrameReceived -= OnFrameReceived;
                connection.Dispose();
            }
            _connections.Clear();
            _latestDetections.Clear();
            
            // 최신 프레임 캐시 정리
            foreach (var frame in _latestFrames.Values)
            {
                frame?.Dispose();
            }
            _latestFrames.Clear();
            
            // 처리된 프레임 캐시 정리
            foreach (var frame in _processedFrames.Values)
            {
                frame?.Dispose();
            }
            _processedFrames.Clear();
        }
        
        /// <summary>
        /// 최신 프레임 캐시 업데이트
        /// </summary>
        private void UpdateLatestFrameCache(string cameraId, Mat frame)
        {
            try
            {
                // 기존 프레임이 있으면 해제
                if (_latestFrames.TryGetValue(cameraId, out var oldFrame))
                {
                    oldFrame?.Dispose();
                }
                
                // 새 프레임으로 교체 (복사본 저장)
                _latestFrames[cameraId] = frame.Clone();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CameraService: Frame cache update error - {ex.Message}");
            }
        }
        
        /// <summary>
        /// 특정 카메라의 최신 프레임 가져오기
        /// </summary>
        public Mat? GetLatestFrame(string cameraId)
        {
            try
            {
                if (_latestFrames.TryGetValue(cameraId, out var frame) && frame != null && !frame.Empty())
                {
                    return frame.Clone(); // 복사본 반환
                }
                
                System.Diagnostics.Debug.WriteLine($"CameraService: No latest frame available for camera {cameraId}");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CameraService: Frame retrieval error - {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 모든 연결된 카메라의 최신 프레임 가져오기
        /// </summary>
        public Dictionary<string, Mat> GetAllLatestFrames()
        {
            var result = new Dictionary<string, Mat>();
            
            try
            {
                foreach (var kvp in _latestFrames)
                {
                    if (kvp.Value != null && !kvp.Value.Empty())
                    {
                        result[kvp.Key] = kvp.Value.Clone();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CameraService: Batch frame retrieval error - {ex.Message}");
            }
            
            return result;
        }
    }
    
    // 카메라 연결 관리 클래스
    internal class CameraConnection : IDisposable
    {
        private VideoCapture? _capture;
        private Thread? _captureThread;
        private readonly CancellationTokenSource _cancellationToken = new();
        private bool _isRunning;
        private object _captureLock = new();
        private bool _disposed = false;
        public Camera Camera { get; }
        public event EventHandler<CameraFrameEventArgs>? FrameReceived;
        
        public CameraConnection(Camera camera)
        {
            Camera = camera;
        }
        
        private void ConfigureCamera()
        {
            if (_capture == null || !_capture.IsOpened()) return;
            
            // YouTube 영상의 경우 카메라 설정을 스킵
            if (Camera.Type == CameraType.YouTube)
            {
                System.Diagnostics.Debug.WriteLine("YouTube 영상 - 카메라 설정 스킵");
                return;
            }
    
            try
            {
                // 먼저 카메라의 네이티브 해상도 확인
                var nativeWidth = _capture.Get(VideoCaptureProperties.FrameWidth);
                var nativeHeight = _capture.Get(VideoCaptureProperties.FrameHeight);
                
                System.Diagnostics.Debug.WriteLine($"Native resolution: {nativeWidth}x{nativeHeight}");
                
                // 카메라가 지원하는 해상도로 설정
                if (Camera.Type == CameraType.USB)
                {
                    // 2560x1440을 먼저 시도
                    _capture.Set(VideoCaptureProperties.FrameWidth, Camera.Width);
                    _capture.Set(VideoCaptureProperties.FrameHeight, (Camera.Height));
                    
                    var actualWidth = _capture.Get(VideoCaptureProperties.FrameWidth);
                    var actualHeight = _capture.Get(VideoCaptureProperties.FrameHeight);
                    
                    // 설정이 안되면 1920x1080 시도
                    if (actualWidth != 2560 || actualHeight != 1440)
                    {
                        _capture.Set(VideoCaptureProperties.FrameWidth, 1920);
                        _capture.Set(VideoCaptureProperties.FrameHeight, 1080);
                        
                        actualWidth = _capture.Get(VideoCaptureProperties.FrameWidth);
                        actualHeight = _capture.Get(VideoCaptureProperties.FrameHeight);
                    }
                    
                    // 최종 설정된 해상도로 Camera 객체 업데이트
                    Camera.Width = (int)actualWidth;
                    Camera.Height = (int)actualHeight;
                }
                
                // 1. 포맷 설정 (MJPEG는 대부분의 USB 카메라에서 지원)
                // 1. 다른 픽셀 포맷 시도
                var formats = new[]
                {
                    VideoWriter.FourCC('Y', 'U', 'Y', '2'),  // YUY2
                    VideoWriter.FourCC('M', 'J', 'P', 'G'),  // MJPEG
                    VideoWriter.FourCC('Y', 'V', '1', '2'),  // YV12
                    VideoWriter.FourCC('B', 'G', 'R', '3'),  // BGR3
                };
        
                foreach (var format in formats)
                {
                    _capture.Set(VideoCaptureProperties.FourCC, format);
            
                    // 테스트 프레임 읽기
                    using (var test = new Mat())
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            _capture.Read(test);
                            Thread.Sleep(100);
                    
                            if (!test.Empty())
                            {
                                var mean = Cv2.Mean(test);
                                var brightness = (mean.Val0 + mean.Val1 + mean.Val2) / 3;
                        
                                System.Diagnostics.Debug.WriteLine(
                                    $"Format {FourCCToString(format)}: Brightness={brightness:F2}");
                        
                                if (brightness > 5) // 검은색이 아니면
                                {
                                    System.Diagnostics.Debug.WriteLine($"Working format found: {FourCCToString(format)}");
                                    break;
                                }
                            }
                        }
                    }
                }
                
                    
                // FPS 설정
                _capture.Set(VideoCaptureProperties.Fps, Camera.Fps);
                
                // DB에서 로드된 카메라 설정 적용
                System.Diagnostics.Debug.WriteLine($"Applying camera settings from DB: Brightness={Camera.Brightness}, Contrast={Camera.Contrast}, Saturation={Camera.Saturation}");
                
                // 자동 노출 설정
                _capture.Set(VideoCaptureProperties.AutoExposure, Camera.AutoExposure ? 0.75 : 0.25);
                
                // 이미지 조정 설정 (DB 값 사용)
                _capture.Set(VideoCaptureProperties.Brightness, Camera.Brightness);
                _capture.Set(VideoCaptureProperties.Contrast, Camera.Contrast);
                _capture.Set(VideoCaptureProperties.Saturation, Camera.Saturation);
                _capture.Set(VideoCaptureProperties.Exposure, Camera.Exposure);
                _capture.Set(VideoCaptureProperties.Gain, Camera.Gain);
                _capture.Set(VideoCaptureProperties.Hue, Camera.Hue);
                _capture.Set(VideoCaptureProperties.Sharpness, Camera.Sharpness);
                
                // 자동 화이트밸런스
                _capture.Set(VideoCaptureProperties.AutoWB, Camera.AutoWhiteBalance ? 1 : 0);
                
                // 버퍼 크기
                _capture.Set(VideoCaptureProperties.BufferSize, 1);
                
                var finalWidth = _capture.Get(VideoCaptureProperties.FrameWidth);
                var finalHeight = _capture.Get(VideoCaptureProperties.FrameHeight);
                var finalFps = _capture.Get(VideoCaptureProperties.Fps);
                
                System.Diagnostics.Debug.WriteLine(
                    $"Camera {Camera.Id} final settings: {finalWidth}x{finalHeight} @ {finalFps}fps");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Configure error: {ex.Message}");
            }
        }
        private void TestExposureModes()
        {
            System.Diagnostics.Debug.WriteLine("Testing exposure modes...");
    
            // 자동 노출 끄기
            _capture.Set(VideoCaptureProperties.AutoExposure, 0.25); // Manual mode
            Thread.Sleep(500);
    
            // 다양한 노출 값 테스트
            int[] exposureValues = { -4, -3, -2, -1, 0 };
    
            foreach (var exp in exposureValues)
            {
                _capture.Set(VideoCaptureProperties.Exposure, exp);
                Thread.Sleep(200);
        
                using (var test = new Mat())
                {
                    _capture.Read(test);
                    if (!test.Empty())
                    {
                        var mean = Cv2.Mean(test);
                        var brightness = (mean.Val0 + mean.Val1 + mean.Val2) / 3;
                        System.Diagnostics.Debug.WriteLine($"Exposure {exp}: Brightness={brightness:F2}");
                    }
                }
            }
        }
        private void AutoAdjustBrightness()
        {
            using (var testFrame = new Mat())
            {
                for (int i = 0; i < 5; i++)
                {
                    _capture.Read(testFrame);
                    if (!testFrame.Empty())
                    {
                        var mean = Cv2.Mean(testFrame);
                        var avgBrightness = (mean.Val0 + mean.Val1 + mean.Val2) / 3;
                
                        if (avgBrightness < 50) // 너무 어두움
                        {
                            var currentBrightness = _capture.Get(VideoCaptureProperties.Brightness);
                            _capture.Set(VideoCaptureProperties.Brightness, currentBrightness + 20);
                            _capture.Set(VideoCaptureProperties.Gain, _capture.Get(VideoCaptureProperties.Gain) + 10);
                        }
                        else if (avgBrightness > 200) // 너무 밝음
                        {
                            var currentBrightness = _capture.Get(VideoCaptureProperties.Brightness);
                            _capture.Set(VideoCaptureProperties.Brightness, currentBrightness - 20);
                        }
                
                        System.Diagnostics.Debug.WriteLine($"Auto-adjust: Brightness={avgBrightness:F2}");
                        break;
                    }
                    Thread.Sleep(100);
                }
            }
        }
        
        public async Task<bool> ConnectAsync()
        {
            try
            {
                string? connectionString = Camera.ConnectionString;
                
                // YouTube 타입인 경우 lock 밖에서 스트림 URL 추출
                if (Camera.Type == CameraType.YouTube)
                {
                    System.Diagnostics.Debug.WriteLine($"유튜브 영상 연결 시도: {Camera.ConnectionString}");
                    
                    var streamUrl = await YouTubeVideoService.GetStreamUrlAsync(Camera.ConnectionString, VideoQuality.Best);
                    if (string.IsNullOrEmpty(streamUrl))
                    {
                        System.Diagnostics.Debug.WriteLine("유튜브 스트림 URL 추출 실패");
                        return false;
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"스트림 URL 추출 성공");
                    connectionString = streamUrl;
                    
                    // 유튜브 영상 정보 가져와서 카메라 설정 업데이트
                    var videoInfo = await YouTubeVideoService.GetVideoInfoAsync(Camera.ConnectionString);
                    if (videoInfo != null)
                    {
                        Camera.Width = videoInfo.Width;
                        Camera.Height = videoInfo.Height;
                        Camera.Fps = videoInfo.Fps > 0 ? videoInfo.Fps : 30;
                        System.Diagnostics.Debug.WriteLine($"유튜브 영상 정보: {videoInfo.Title} ({videoInfo.Width}x{videoInfo.Height} @ {videoInfo.Fps}fps)");
                    }
                }
                
                return await Task.Run(() =>
                {
                    try
                    {
                        lock (_captureLock)
                        {
                            if (_disposed) return false;
                            
                            // 카메라 타입에 따른 연결 처리
                            if (Camera.Type == CameraType.USB)
                            {
                                int cameraIndex = int.Parse(Camera.ConnectionString);
                                
                                // DirectShow 백엔드 사용 (Windows에서 안정적)
                                _capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.MSMF);
                                
                                if (!_capture.IsOpened())
                                {
                                    _capture.Dispose();
                                    _capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.ANY);
                                }
                                
                                if (!_capture.IsOpened())
                                {
                                    _capture.Dispose();
                                    _capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);
                                }
                            }
                            else
                            {
                                // YouTube(변환된 스트림 URL), RTSP, File 등
                                _capture = new VideoCapture(connectionString);
                            }
                            
                            if (_capture?.IsOpened() != true)
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to open camera {Camera.Id}");
                                _capture?.Dispose();
                                _capture = null;
                                return false;
                            }
                            
                            // 카메라 정보 출력
                            var backend = _capture.GetBackendName();
                            System.Diagnostics.Debug.WriteLine($"Camera backend: {backend}");
                            
                            // 카메라 설정
                            ConfigureCamera();
                            
                            // 워밍업 및 검증
                            if (!VerifyCamera())
                            {
                                System.Diagnostics.Debug.WriteLine("Camera verification failed");
                                _capture.Dispose();
                                _capture = null;
                                return false;
                            }
                            
                            _isRunning = true;
                        }
                        
                        // 스레드 시작
                        _captureThread = new Thread(CaptureLoop) 
                        { 
                            IsBackground = true,
                            Name = $"Camera_{Camera.Id}_Thread"
                        };
                        _captureThread.Start();
                        
                        System.Diagnostics.Debug.WriteLine($"Camera {Camera.Id} connected successfully");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Camera connection error: {ex.Message}");
                        return false;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"YouTube connection error: {ex.Message}");
                return false;
            }
        }

        private bool VerifyCamera()
        {
            if (_capture == null || !_capture.IsOpened()) return false;

            try
            {
                System.Diagnostics.Debug.WriteLine("Warming up camera...");
        
                // 카메라 워밍업 (일부 카메라는 초기화 시간이 필요)
                for (int warmup = 0; warmup < 3; warmup++)
                {
                    using (var dummy = new Mat())
                    {
                        _capture.Read(dummy);
                        Thread.Sleep(200); // 각 프레임 간 대기
                
                        if (!dummy.Empty())
                        {
                            var mean = Cv2.Mean(dummy);
                            System.Diagnostics.Debug.WriteLine(
                                $"Warmup {warmup}: RGB=({mean.Val0:F2}, {mean.Val1:F2}, {mean.Val2:F2})");
                        }
                    }
                }
        
                // 실제 검증
                using (var testFrame = new Mat())
                {
                    for (int i = 0; i < 3; i++)
                    {
                        _capture.Read(testFrame);
                        Thread.Sleep(200);
                
                        if (!testFrame.Empty())
                        {
                            var mean = Cv2.Mean(testFrame);
                            var totalMean = (mean.Val0 + mean.Val1 + mean.Val2) / 3;
                    
                            System.Diagnostics.Debug.WriteLine(
                                $"Verify frame {i}: Size={testFrame.Width}x{testFrame.Height}, " +
                                $"Mean={totalMean:F2}");
                    
                            if (totalMean > 5)
                            {
                                return true;
                            }
                        }
                    }
                }
        
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Verify error: {ex.Message}");
                return false;
            }
        }
        
        private string FourCCToString(int fourcc)
        {
            return $"{(char)(fourcc & 255)}{(char)((fourcc >> 8) & 255)}{(char)((fourcc >> 16) & 255)}{(char)((fourcc >> 24) & 255)}";
        }
        
        private void LogFrameInfo(Mat frame)
        {
            System.Diagnostics.Debug.WriteLine($"=== Camera {Camera.Id} Frame Info ===");
            System.Diagnostics.Debug.WriteLine($"Size: {frame.Width}x{frame.Height}");
            System.Diagnostics.Debug.WriteLine($"Channels: {frame.Channels()}");
            System.Diagnostics.Debug.WriteLine($"Type: {frame.Type()}");
            System.Diagnostics.Debug.WriteLine($"Depth: {frame.Depth()}");
    
            // 픽셀 샘플링
            var mean = Cv2.Mean(frame);
            System.Diagnostics.Debug.WriteLine($"Mean RGB: ({mean.Val0:F2}, {mean.Val1:F2}, {mean.Val2:F2})");
    
            // 히스토그램 분석
            using (var hist = new Mat())
            {
                Cv2.CalcHist(new[] { frame }, new[] { 0 }, null, hist, 1, new[] { 256 }, new[] { new[] { 0f, 256f } });
                double minVal, maxVal;
                Cv2.MinMaxLoc(hist, out minVal, out maxVal);
                System.Diagnostics.Debug.WriteLine($"Histogram range: {minVal} - {maxVal}");
            }
        }

        private void LogCameraSettings()
        {
            System.Diagnostics.Debug.WriteLine($"=== Camera {Camera.Id} Settings ===");
            System.Diagnostics.Debug.WriteLine($"Resolution: {_capture.Get(VideoCaptureProperties.FrameWidth)}x{_capture.Get(VideoCaptureProperties.FrameHeight)}");
            System.Diagnostics.Debug.WriteLine($"FPS: {_capture.Get(VideoCaptureProperties.Fps)}");
            System.Diagnostics.Debug.WriteLine($"Brightness: {_capture.Get(VideoCaptureProperties.Brightness)}");
            System.Diagnostics.Debug.WriteLine($"Contrast: {_capture.Get(VideoCaptureProperties.Contrast)}");
            System.Diagnostics.Debug.WriteLine($"Saturation: {_capture.Get(VideoCaptureProperties.Saturation)}");
            System.Diagnostics.Debug.WriteLine($"Exposure: {_capture.Get(VideoCaptureProperties.Exposure)}");
            System.Diagnostics.Debug.WriteLine($"Gain: {_capture.Get(VideoCaptureProperties.Gain)}");
            System.Diagnostics.Debug.WriteLine($"Backend: {_capture.GetBackendName()}");
        }
        
        public void UpdateSettings(Camera settings)
        {
            lock (_captureLock)
            {
                if (_capture == null || !_capture.IsOpened()) return;
                
                try
                {
                    // 밝기
                    //if (Math.Abs(Camera.Brightness - settings.Brightness) > 0.1)
                    {
                        _capture.Set(VideoCaptureProperties.Brightness, settings.Brightness);
                        Camera.Brightness = settings.Brightness;
                    }
                    
                    // 대비
                    //if (Math.Abs(Camera.Contrast - settings.Contrast) > 0.1)
                    {
                        _capture.Set(VideoCaptureProperties.Contrast, settings.Contrast);
                        Camera.Contrast = settings.Contrast;
                    }
                    
                    // 채도
                    //if (Math.Abs(Camera.Saturation - settings.Saturation) > 0.1)
                    {
                        _capture.Set(VideoCaptureProperties.Saturation, settings.Saturation);
                        Camera.Saturation = settings.Saturation;
                    }
                    
                    // 노출
                    //if (Math.Abs(Camera.Exposure - settings.Exposure) > 0.1)
                    {
                        _capture.Set(VideoCaptureProperties.Exposure, settings.Exposure);
                        Camera.Exposure = settings.Exposure;
                    }
                    
                    // 게인
                    //if (Math.Abs(Camera.Gain - settings.Gain) > 0.1)
                    {
                        _capture.Set(VideoCaptureProperties.Gain, settings.Gain);
                        Camera.Gain = settings.Gain;
                    }
                    
                    // 색조
                    //if (Math.Abs(Camera.Hue - settings.Hue) > 0.1)
                    {
                        _capture.Set(VideoCaptureProperties.Hue, settings.Hue);
                        Camera.Hue = settings.Hue;
                    }
                    
                    // 감마
                    //if (Math.Abs(Camera.Gamma - settings.Gamma) > 0.1)
                    // {
                    //     _capture.Set(VideoCaptureProperties.Gamma, settings.Gamma);
                    //     Camera.Gamma = settings.Gamma;
                    // }
                    
                    // 선명도
                    //if (Math.Abs(Camera.Sharpness - settings.Sharpness) > 0.1)
                    {
                        _capture.Set(VideoCaptureProperties.Sharpness, settings.Sharpness);
                        Camera.Sharpness = settings.Sharpness;
                    }
                    
                    // 자동 노출
                    if (settings.AutoExposure == true)
                    {
                        _capture.Set(VideoCaptureProperties.AutoExposure, settings.AutoExposure ? 0.75 : 0.25);
                        Camera.AutoExposure = settings.AutoExposure;
                    }
                    
                    // 자동 화이트 밸런스
                    if (settings.AutoWhiteBalance == true)
                    {
                        _capture.Set(VideoCaptureProperties.AutoWB, settings.AutoWhiteBalance ? 1 : 0);
                        Camera.AutoWhiteBalance = settings.AutoWhiteBalance;
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"Camera {Camera.Id} settings updated");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to update camera settings: {ex.Message}");
                }
            }
        }
        
        public Camera GetCurrentSettings()
        {
            lock (_captureLock)
            {
                if (_capture == null || !_capture.IsOpened()) return Camera;
                
                try
                {
                    // 현재 카메라 설정값 읽기
                    Camera.Brightness = _capture.Get(VideoCaptureProperties.Brightness);
                    Camera.Contrast = _capture.Get(VideoCaptureProperties.Contrast);
                    Camera.Saturation = _capture.Get(VideoCaptureProperties.Saturation);
                    Camera.Exposure = _capture.Get(VideoCaptureProperties.Exposure);
                    Camera.Gain = _capture.Get(VideoCaptureProperties.Gain);
                    Camera.Hue = _capture.Get(VideoCaptureProperties.Hue);
                    Camera.Gamma = _capture.Get(VideoCaptureProperties.Gamma);
                    Camera.Sharpness = _capture.Get(VideoCaptureProperties.Sharpness);
                    
                    var autoExp = _capture.Get(VideoCaptureProperties.AutoExposure);
                    Camera.AutoExposure = autoExp > 0.5;
                    
                    var autoWb = _capture.Get(VideoCaptureProperties.AutoWB);
                    Camera.AutoWhiteBalance = autoWb > 0.5;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to get camera settings: {ex.Message}");
                }
                
                return Camera;
            }
        }
        
        private void CaptureLoop()
        {
            var frame = new Mat();
            var frameCount = 0;
            bool formatLogged = false;
    
            while (_isRunning && !_cancellationToken.Token.IsCancellationRequested)
            {
                try
                {
                    if (_capture != null && _capture.Read(frame) && !frame.Empty())
                    {
                        // 첫 프레임에서 포맷 정보 로깅
                        if (!formatLogged)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"Camera {Camera.Id} format: " +
                                $"Size={frame.Width}x{frame.Height}, " +
                                $"Channels={frame.Channels()}, " +
                                $"Type={frame.Type()}, " +
                                $"Depth={frame.Depth()}");
                            formatLogged = true;
                        }
                
                        // 프레임이 비정상적인 경우 체크
                        if (frame.Width <= 0 || frame.Height <= 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"Invalid frame size for {Camera.Id}");
                            continue;
                        }
                
                        // 프레임을 CameraService로 전달 (CameraService에서 구독자별 복사 관리)
                        FrameReceived?.Invoke(this, new CameraFrameEventArgs(Camera.Id, frame.Clone()));
                        frameCount++;
                
                        // if (frameCount % 30 == 0) // 30프레임마다 로그
                        // {
                        //     System.Diagnostics.Debug.WriteLine($"Camera {Camera.Id}: {frameCount} frames processed");
                        // }
                    }
            
                    Thread.Sleep(1000 / (int)Camera.Fps);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Capture loop error: {ex.Message}");
                }
            }
    
            frame.Dispose();
        }
        
        public void Dispose()
        {
            if (_disposed) return;
    
            _disposed = true;
            _isRunning = false;
    
            // 캡처 스레드 종료 대기
            _cancellationToken.Cancel();
            
            // 스레드가 종료될 때까지 최대 3초 대기
            if (_captureThread?.IsAlive == true)
            {
                if (!_captureThread.Join(3000))
                {
                    // 타임아웃 시 로그만 남기고 계속 진행
                    System.Diagnostics.Debug.WriteLine($"Warning: Camera {Camera.Id} capture thread did not terminate within timeout");
                }
            }
    
            // 카메라 해제
            lock (_captureLock)
            {
                _capture?.Release();
                _capture?.Dispose();
                _capture = null;
            }
    
            _cancellationToken.Dispose();
        }
    }
    
    // 이벤트 인자 클래스들
    public class CameraFrameEventArgs : EventArgs
    {
        public string CameraId { get; }
        public Mat Frame { get; }
        
        public CameraFrameEventArgs(string cameraId, Mat frame)
        {
            CameraId = cameraId;
            Frame = frame;
        }
    }
    
    public class CameraConnectionEventArgs : EventArgs
    {
        public string CameraId { get; }
        public bool IsConnected { get; }
        
        public CameraConnectionEventArgs(string cameraId, bool isConnected)
        {
            CameraId = cameraId;
            IsConnected = isConnected;
        }
    }
}