using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using Point = OpenCvSharp.Point;
using SafetyVisionMonitor.Helpers;
using SafetyVisionMonitor.Models;
using SafetyVisionMonitor.Services;
using SafetyVisionMonitor.ViewModels.Base;
using Rect = OpenCvSharp.Rect;

namespace SafetyVisionMonitor.ViewModels
{
    public partial class DashboardViewModel : BaseViewModel
    {
        private DispatcherTimer _updateTimer;
        private readonly Dictionary<string, BitmapSource?> _cameraFrames = new();
        private object _frameLock = new();
        
        // 추적 관련 (백그라운드 서비스에서 관리)
        private readonly Dictionary<string, List<TrackedPerson>> _latestTrackedPersons = new();
        private readonly Dictionary<string, List<DetectionResult>> _latestDetections = new();
        
        // 카메라 관련
        [ObservableProperty]
        private ObservableCollection<CameraViewModel> cameras;
        
        // 3D 영역 표시 옵션
        [ObservableProperty]
        private bool showWarningZones = true;
        
        [ObservableProperty]
        private bool showDangerZones = true;
        
        // 디버그 옵션
        [ObservableProperty]
        private bool showAllDetections = false;
        
        [ObservableProperty]
        private bool showDetailedInfo = false;
        
        [ObservableProperty]
        private bool showDetectionStats = false;
        
        // 추적 관련 UI 속성
        [ObservableProperty]
        private bool isTrackingActive = false;
        
        [ObservableProperty]
        private int activeTrackersCount = 0;
        
        [ObservableProperty]
        private int totalTrackersCreated = 0;
        
        [ObservableProperty]
        private bool showTrackingIds = true;
        
        [ObservableProperty]
        private bool showTrackingPaths = true;
        
        /// <summary>
        /// ShowTrackingIds 속성 변경 시 백그라운드 서비스에 반영
        /// </summary>
        partial void OnShowTrackingIdsChanged(bool value)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var trackingService = App.TrackingService;
                    if (trackingService != null)
                    {
                        // 현재 설정을 복사하고 ShowTrackingId만 변경
                        var currentConfig = new TrackingConfiguration
                        {
                            IsEnabled = true,
                            ShowTrackingId = value,
                            ShowTrackingPath = ShowTrackingPaths,
                            MaxTrackingDistance = 50,
                            MaxDisappearFrames = 30,
                            IouThreshold = 0.3f,
                            SimilarityThreshold = 0.7f,
                            TrackHistoryLength = 50,
                            PathDisplayLength = 20,
                            TrackingMethod = "SORT"
                        };
                        
                        await trackingService.UpdateTrackingConfigurationAsync(currentConfig);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to update ShowTrackingIds: {ex.Message}");
                }
            });
        }
        
        /// <summary>
        /// ShowTrackingPaths 속성 변경 시 백그라운드 서비스에 반영
        /// </summary>
        partial void OnShowTrackingPathsChanged(bool value)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var trackingService = App.TrackingService;
                    if (trackingService != null)
                    {
                        // 현재 설정을 복사하고 ShowTrackingPath만 변경
                        var currentConfig = new TrackingConfiguration
                        {
                            IsEnabled = true,
                            ShowTrackingId = ShowTrackingIds,
                            ShowTrackingPath = value,
                            MaxTrackingDistance = 50,
                            MaxDisappearFrames = 30,
                            IouThreshold = 0.3f,
                            SimilarityThreshold = 0.7f,
                            TrackHistoryLength = 50,
                            PathDisplayLength = 20,
                            TrackingMethod = "SORT"
                        };
                        
                        await trackingService.UpdateTrackingConfigurationAsync(currentConfig);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to update ShowTrackingPaths: {ex.Message}");
                }
            });
        }
        
        // 정적 속성으로 디버그 설정 공유
        public static bool StaticShowAllDetections { get; private set; }
        public static bool StaticShowDetailedInfo { get; private set; }
        
        // 카메라별 3D 구역 데이터 - Dictionary로 카메라별 관리
        private readonly Dictionary<string, ObservableCollection<ZoneVisualization>> _cameraWarningZones = new();
        private readonly Dictionary<string, ObservableCollection<ZoneVisualization>> _cameraDangerZones = new();
        
        // 현재 표시 중인 구역들 (모든 카메라의 구역을 합쳐서 표시)
        [ObservableProperty]
        private ObservableCollection<ZoneVisualization> warningZones = new();
        
        [ObservableProperty]
        private ObservableCollection<ZoneVisualization> dangerZones = new();
        
        // AI 모델 상태
        [ObservableProperty]
        private string aiModelName = "YOLO v8";
        
        [ObservableProperty]
        private string aiModelVersion = "1.0.0";
        
        [ObservableProperty]
        private bool isModelRunning = false;
        
        [ObservableProperty]
        private string modelStatusText = "미실행";
        
        [ObservableProperty]
        private string executionProvider = "CPU";
        
        [ObservableProperty]
        private bool isUsingGpu = false;
        
        [ObservableProperty]
        private double modelConfidence = 0.7;
        
        // 시스템 상태
        [ObservableProperty]
        private double cpuUsage = 0;
        
        [ObservableProperty]
        private double gpuUsage = 0;
        
        [ObservableProperty]
        private double memoryUsage = 0;
        
        [ObservableProperty]
        private double memoryUsagePercent = 0;
        
        // GPU 상세 정보
        [ObservableProperty]
        private string gpuName = "Unknown GPU";
        
        [ObservableProperty]
        private double gpuMemoryUsage = 0;
        
        [ObservableProperty]
        private double gpuMemoryPercent = 0;
        
        [ObservableProperty]
        private double gpuTemperature = 0;
        
        [ObservableProperty]
        private bool isGpuActive = false;
        
        [ObservableProperty]
        private bool isAIAccelerationEnabled = false;
        
        [ObservableProperty]
        private int processingQueueLength = 0;
        
        [ObservableProperty]
        private int processedFps = 0;
        
        [ObservableProperty]
        private int detectedPersonCount = 0;
        
        [ObservableProperty]
        private int activeAlertsCount = 0;
        
        // 디버그 정보
        [ObservableProperty]
        private ObservableCollection<DetectionResult> debugDetectionInfo = new();
        
        private readonly object _debugLock = new object();
        
        public DashboardViewModel()
        {
            Title = "실시간 모니터링 대시보드";
            Cameras = new ObservableCollection<CameraViewModel>();
            
            // App.AppData에서 카메라 정보 로드
            foreach (var camera in App.AppData.Cameras)
            {
                var cameraVm = new CameraViewModel
                {
                    CameraId = camera.Id,
                    CameraName = camera.Name,
                    IsConnected = camera.IsConnected
                };
                Cameras.Add(cameraVm);
                _cameraFrames[camera.Id] = null;
                _latestTrackedPersons[camera.Id] = new List<TrackedPerson>();
            }
            
            // 추적은 백그라운드 서비스에서 처리됨
        }
        
        public override void OnLoaded()
        {
            base.OnLoaded();
            
            // 서비스 이벤트 구독 (UI용 저화질 프레임 사용)
            App.CameraService.FrameReceivedForUI += OnFrameReceived;
            App.CameraService.ConnectionChanged += OnConnectionChanged;
            App.MonitoringService.PerformanceUpdated += OnPerformanceUpdated;
            
            // AI 검출 이벤트 구독 (디버그용)
            if (App.AIPipeline != null)
            {
                App.AIPipeline.ObjectDetected += OnObjectDetectedForDebug;
            }
            
            // 백그라운드 추적 서비스 이벤트 구독
            App.MonitoringService.TrackingUpdated += OnBackgroundTrackingUpdated;
            
            // 구역 업데이트 이벤트 구독
            App.AppData.ZoneUpdated += OnZoneUpdated;
            
            // 구역 데이터 로드
            LoadZoneOverlaysAsync();
        }
        
        public override void Cleanup()
        {
            // 이벤트 구독 해제
            App.CameraService.FrameReceivedForUI -= OnFrameReceived;
            App.CameraService.ConnectionChanged -= OnConnectionChanged;
            App.MonitoringService.PerformanceUpdated -= OnPerformanceUpdated;
            App.MonitoringService.TrackingUpdated -= OnBackgroundTrackingUpdated;
            
            if (App.AIPipeline != null)
            {
                App.AIPipeline.ObjectDetected -= OnObjectDetectedForDebug;
            }
            
            App.AppData.ZoneUpdated -= OnZoneUpdated;
            
            base.Cleanup();
        }

        private async Task LoadZoneOverlaysAsync()
        {
            try
            {
                var zones = await App.DatabaseService.LoadZone3DConfigsAsync();
                
                // 카메라별 구역 저장소 초기화
                _cameraWarningZones.Clear();
                _cameraDangerZones.Clear();
                
                // 각 카메라별로 구역 분류
                foreach (var zone in zones)
                {
                    if (zone.FloorPoints.Count >= 3)
                    {
                        var visualization = new ZoneVisualization
                        {
                            ZoneId = zone.Id,
                            Name = zone.Name,
                            ZoneColor = zone.DisplayColor,
                            Opacity = zone.IsEnabled ? zone.Opacity : 0.05,
                            IsSelected = false,
                            IsEnabled = zone.IsEnabled
                        };
                        
                        System.Diagnostics.Debug.WriteLine($"Dashboard visualization for {zone.Name} (Camera: {zone.CameraId}): IsEnabled={zone.IsEnabled}");
                        
                        // 3D 좌표를 상대 좌표(0~1)로 변환
                        var originalFrameWidth = zone.CalibrationFrameWidth;
                        var originalFrameHeight = zone.CalibrationFrameHeight;
                        
                        foreach (var worldPoint in zone.FloorPoints)
                        {
                            // 먼저 원본 프레임 크기로 화면 좌표 계산
                            var screenPoint = ConvertWorldToScreenPointOriginal(worldPoint, zone);
                            
                            // 상대 좌표로 변환 (0~1 범위)
                            var relativeX = screenPoint.X / originalFrameWidth;
                            var relativeY = screenPoint.Y / originalFrameHeight;
                            var relativePoint = new System.Windows.Point(relativeX, relativeY);
                            
                            visualization.AddRelativePoint(relativePoint);
                            System.Diagnostics.Debug.WriteLine($"World({worldPoint.X:F2}, {worldPoint.Y:F2}) -> Relative({relativeX:F3}, {relativeY:F3})");
                        }
                        
                        // 다각형 닫기
                        if (zone.FloorPoints.Count >= 3)
                        {
                            var firstScreenPoint = ConvertWorldToScreenPointOriginal(zone.FloorPoints[0], zone);
                            var relativeX = firstScreenPoint.X / originalFrameWidth;
                            var relativeY = firstScreenPoint.Y / originalFrameHeight;
                            visualization.AddRelativePoint(new System.Windows.Point(relativeX, relativeY));
                        }
                        
                        // 카메라별로 구역 저장
                        if (zone.Type == ZoneType.Warning)
                        {
                            if (!_cameraWarningZones.ContainsKey(zone.CameraId))
                                _cameraWarningZones[zone.CameraId] = new ObservableCollection<ZoneVisualization>();
                            _cameraWarningZones[zone.CameraId].Add(visualization);
                        }
                        else
                        {
                            if (!_cameraDangerZones.ContainsKey(zone.CameraId))
                                _cameraDangerZones[zone.CameraId] = new ObservableCollection<ZoneVisualization>();
                            _cameraDangerZones[zone.CameraId].Add(visualization);
                        }
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"Loaded zones for {_cameraWarningZones.Count + _cameraDangerZones.Count} cameras");
                
                // CameraViewModel에 구역 정보 설정
                UpdateCameraZones();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Zone overlay load error: {ex.Message}");
            }
        }
        
        private System.Windows.Point ConvertWorldToScreenPointOriginal(Point2D worldPoint, Zone3D zone)
        {
            // 구역에 저장된 원본 캘리브레이션 정보 사용
            var pixelsPerMeter = zone.CalibrationPixelsPerMeter;
            var frameWidth = zone.CalibrationFrameWidth;
            var frameHeight = zone.CalibrationFrameHeight;
            
            return CoordinateTransformService.WorldToScreen(worldPoint, frameWidth, frameHeight, pixelsPerMeter);
        }
        
        public override void OnActivated()
        {
            base.OnActivated();
            _updateTimer?.Start();
            
            // 구역 오버레이 다시 로드 (데이터 동기화)
            _ = LoadZoneOverlaysAsync();
        }
        
        public override void OnDeactivated()
        {
            base.OnDeactivated();
            _updateTimer?.Stop();
        }
        
        // public override void Cleanup()
        // {
        //     base.Cleanup();
        //     
        //     // 이벤트 구독 해제는 ViewModel이 완전히 소멸될 때만
        //     App.CameraService.FrameReceivedForUI -= OnFrameReceived;
        //     App.CameraService.ConnectionChanged -= OnConnectionChanged;
        //     App.MonitoringService.PerformanceUpdated -= OnPerformanceUpdated;
        //     App.AppData.ZoneUpdated -= OnZoneUpdated;
        //     
        //     _updateTimer?.Stop();
        // }
        
        [RelayCommand]
        private async Task TestCameraDisplay()
        {
            // 테스트 이미지 생성
            await Task.Run(() =>
            {
                using (var testMat = new Mat(480, 640, MatType.CV_8UC3, new Scalar(0, 255, 0)))
                {
                    // Cv2.PutText(testMat, "TEST IMAGE", new Point(200, 240), 
                    //     HersheyFonts.HersheySimplex, 2.0, new Scalar(255, 255, 255), 3);
            
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        var firstCamera = Cameras.FirstOrDefault();
                        if (firstCamera != null)
                        {
                            firstCamera.CurrentFrame = testMat.ToBitmapSource();
                            System.Diagnostics.Debug.WriteLine("Test image set");
                        }
                    });
                }
            });
        }
        
        // 성능 최적화를 위한 프레임 스키핑
        private DateTime _lastFrameUpdate = DateTime.MinValue;
        private readonly TimeSpan _minFrameInterval = TimeSpan.FromMilliseconds(33); // 30 FPS 제한
        
        private void OnFrameReceived(object? sender, CameraFrameEventArgs e)
        {
            try
            {
                if (e?.Frame == null || e.Frame.Empty())
                {
                    e?.Frame?.Dispose();
                    return;
                }
                
                // 프레임 스키핑 - 성능 최적화
                var now = DateTime.Now;
                if (now - _lastFrameUpdate < _minFrameInterval)
                {
                    e.Frame?.Dispose();
                    return;
                }
                _lastFrameUpdate = now;
        
                // UI 스레드에서 변환과 업데이트를 모두 처리
                App.Current.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                {
                    try
                    {
                        using (var originalFrame = e.Frame)
                        {
                            if (originalFrame != null && !originalFrame.Empty())
                            {
                                // 디버그: 프레임 크기 정보 출력 (첫 번째 프레임에만)
                                // System.Diagnostics.Debug.WriteLine($"UI Frame for camera {e.CameraId}: Size=({originalFrame.Width}x{originalFrame.Height})");
                                
                                // 프레임에 구역 오버레이 그리기
                                var frameWithZones = DrawZoneOverlaysOnFrame(originalFrame, e.CameraId);
                                
                                // 추적 정보 그리기 (최신 검출 결과 사용)
                                var latestDetections = _latestDetections.GetValueOrDefault(e.CameraId, new List<DetectionResult>());
                                var finalFrame = DrawTrackingOverlaysOnFrame(frameWithZones, e.CameraId, latestDetections);
                                
                                // UI 스레드에서 BitmapSource 변환
                                var bitmap = ImageConverter.MatToBitmapSource(finalFrame);
                                
                                // 그려진 프레임들 해제
                                frameWithZones.Dispose();
                                if (finalFrame != frameWithZones) finalFrame.Dispose();
        
                                if (bitmap != null)
                                {
                                    var cameraVm = Cameras.FirstOrDefault(c => c.CameraId == e.CameraId);
                                    if (cameraVm != null)
                                    {
                                        cameraVm.CurrentFrame = bitmap;
                                        cameraVm.DetectionCount++;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception uiEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Frame processing error: {uiEx.Message}");
                    }
                }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnFrameReceived error: {ex.Message}");
                e?.Frame?.Dispose();
            }
        }
        
        [RelayCommand]
        private async Task TestCameraDirectly()
        {
            await Task.Run(() =>
            {
                try
                {
                    // 기본 Windows 카메라 앱이 실행 중인지 확인
                    var cameraProcesses = System.Diagnostics.Process.GetProcessesByName("WindowsCamera");
                    if (cameraProcesses.Length > 0)
                    {
                        System.Diagnostics.Debug.WriteLine("Windows Camera app is running. Please close it.");
                        return;
                    }
                    
                    // 직접 카메라 열기 테스트
                    using (var cap = new VideoCapture(0, VideoCaptureAPIs.DSHOW))
                    {
                        if (!cap.IsOpened())
                        {
                            System.Diagnostics.Debug.WriteLine("Cannot open camera 0");
                            return;
                        }
                        
                        // 기본 해상도로 시도
                        cap.Set(VideoCaptureProperties.FrameWidth, 640);
                        cap.Set(VideoCaptureProperties.FrameHeight, 480);
                        
                        using (var frame = new Mat())
                        {
                            for (int i = 0; i < 5; i++)
                            {
                                cap.Read(frame);
                                Thread.Sleep(200);
                                
                                if (!frame.Empty())
                                {
                                    var mean = Cv2.Mean(frame);
                                    System.Diagnostics.Debug.WriteLine(
                                        $"Frame {i}: Mean values B={mean.Val0:F1}, " +
                                        $"G={mean.Val1:F1}, R={mean.Val2:F1}");
                                    
                                    if (mean.Val0 > 0 || mean.Val1 > 0 || mean.Val2 > 0)
                                    {
                                        App.Current.Dispatcher.Invoke(() =>
                                        {
                                            var firstCamera = Cameras.FirstOrDefault();
                                            if (firstCamera != null)
                                            {
                                                firstCamera.CurrentFrame = frame.ToBitmapSource();
                                                System.Diagnostics.Debug.WriteLine("Test frame displayed");
                                            }
                                        });
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Direct test error: {ex.Message}");
                }
            });
        }

        [RelayCommand]
        private async Task TestDirectCameraCapture()
        {
            await Task.Run(() =>
            {
                try
                {
                    // USB 카메라 직접 테스트
                    using (var capture = new VideoCapture(0)) // 0번 USB 카메라
                    {
                        if (!capture.IsOpened())
                        {
                            System.Diagnostics.Debug.WriteLine("Cannot open camera");
                            return;
                        }
                
                        using (var frame = new Mat())
                        {
                            if (capture.Read(frame) && !frame.Empty())
                            {
                                App.Current.Dispatcher.Invoke(() =>
                                {
                                    var firstCamera = Cameras.FirstOrDefault();
                                    if (firstCamera != null)
                                    {
                                        firstCamera.CurrentFrame = frame.ToBitmapSource();
                                        System.Diagnostics.Debug.WriteLine("Direct capture successful");
                                    }
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Direct capture error: {ex.Message}");
                }
            });
        }
        
        private void OnConnectionChanged(object? sender, CameraConnectionEventArgs e)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                var cameraVm = Cameras.FirstOrDefault(c => c.CameraId == e.CameraId);
                if (cameraVm != null)
                {
                    cameraVm.IsConnected = e.IsConnected;
                }
            });
        }
        
        private void OnPerformanceUpdated(object? sender, PerformanceMetrics metrics)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                // CPU & Memory
                CpuUsage = metrics.CpuUsage;
                MemoryUsage = metrics.MemoryUsage;
                
                // 시스템 총 메모리를 기준으로 사용률 계산 (예: 16GB = 16384MB)
                var totalMemoryMB = 16384; // 임시로 16GB 가정
                MemoryUsagePercent = Math.Min((MemoryUsage / totalMemoryMB) * 100, 100);
                
                // GPU 정보
                GpuUsage = metrics.GpuUsage;
                GpuName = metrics.GpuName;
                GpuMemoryUsage = metrics.GpuMemoryUsage;
                GpuTemperature = metrics.GpuTemperature;
                
                // GPU 메모리 사용률 계산 (전체 VRAM 기준)
                var estimatedTotalGpuMemory = 8192; // 예: 8GB VRAM 가정
                GpuMemoryPercent = Math.Min((GpuMemoryUsage / estimatedTotalGpuMemory) * 100, 100);
                
                // GPU 활성 상태 (사용률이 5% 이상이면 활성)
                IsGpuActive = GpuUsage > 5;
                
                // AI 가속 활성화 여부 (GPU 사용률과 연결된 카메라 수 기준)
                var connectedCamerasCount = Cameras.Count(c => c.IsConnected);
                IsAIAccelerationEnabled = connectedCamerasCount > 0 && GpuUsage > 10;
                
                // 처리 큐 길이 (연결된 카메라 수 기준 추정)
                ProcessingQueueLength = connectedCamerasCount * 2;
                
                // 기타 성능 지표
                ProcessedFps = metrics.ProcessedFps;
                DetectedPersonCount = metrics.DetectedPersons;
                ActiveAlertsCount = metrics.ActiveAlerts;
                
                // AI 모델 상태 업데이트
                var activeModel = App.AppData.AIModels.FirstOrDefault(m => m.IsActive);
                if (activeModel != null)
                {
                    AiModelName = activeModel.ModelName;
                    AiModelVersion = activeModel.ModelVersion;
                    IsModelRunning = App.MonitoringService.IsRunning;
                    ModelConfidence = activeModel.DefaultConfidence;
                    
                    // AI 실행 환경 정보 업데이트
                    IsUsingGpu = metrics.IsUsingGpu;
                    ExecutionProvider = App.AIInferenceService?.ExecutionProvider;

                    ModelStatusText = IsModelRunning ? "실행 중" : "미실행";
                }
            });
        }
        
        private void UpdateStatus(object? sender, EventArgs e)
        {
            // 임시 데이터 (나중에 실제 서비스에서 가져옴)
            CpuUsage = Random.Shared.Next(20, 60);
            GpuUsage = Random.Shared.Next(30, 80);
            MemoryUsage = Random.Shared.Next(40, 70);
            ProcessedFps = Random.Shared.Next(20, 30);
            DetectedPersonCount = Random.Shared.Next(0, 10);
        }
        
        private void OnObjectDetectedForDebug(object? sender, ObjectDetectionEventArgs e)
        {
            // 최신 검출 결과 업데이트 (추적에 사용)
            _latestDetections[e.CameraId] = e.Detections.ToList();
            
            // 디버그: 검출 결과 좌표 정보 출력 (필요시에만)
            // if (e.Detections.Length > 0)
            // {
            //     var firstDetection = e.Detections[0];
            //     System.Diagnostics.Debug.WriteLine($"Detection for camera {e.CameraId}: " +
            //         $"Box=({firstDetection.BoundingBox.X:F1}, {firstDetection.BoundingBox.Y:F1}, " +
            //         $"{firstDetection.BoundingBox.Width:F1}, {firstDetection.BoundingBox.Height:F1})");
            // }
            
            if (!ShowDetectionStats) return;
            
            App.Current.Dispatcher.Invoke(() =>
            {
                lock (_debugLock)
                {
                    // 디버그 정보 업데이트
                    DebugDetectionInfo.Clear();
                    
                    var allDetections = new List<DetectionResult>();
                    
                    // 모든 카메라의 최신 검출 결과를 수집
                    foreach (var camera in Cameras)
                    {
                        var detections = App.CameraService.GetLatestDetections(camera.CameraId);
                        foreach (var detection in detections)
                        {
                            // 신뢰도 필터링 (디버그 모드에서는 낮은 신뢰도도 표시)
                            if (ShowAllDetections || detection.ClassName.ToLower() == "person")
                            {
                                allDetections.Add(detection);
                            }
                        }
                    }
                    
                    // 신뢰도순으로 정렬
                    var sortedDetections = allDetections
                        .OrderByDescending(d => d.Confidence)
                        .Take(20) // 최대 20개만 표시
                        .ToList();
                    
                    foreach (var detection in sortedDetections)
                    {
                        DebugDetectionInfo.Add(detection);
                    }
                    
                    // 검출된 사람 수 업데이트
                    DetectedPersonCount = allDetections.Count(d => d.ClassName.ToLower() == "person");
                }
            });
        }
        
        [RelayCommand]
        private async Task ToggleAIModel()
        {
            if (IsModelRunning)
            {
                await App.MonitoringService.StopAsync();
            }
            else
            {
                await App.MonitoringService.StartAsync();
            }
            
            IsModelRunning = App.MonitoringService.IsRunning;
        }
        
        [RelayCommand]
        private async Task RefreshZoneOverlays()
        {
            await LoadZoneOverlaysAsync();
        }
        
        // Zone의 IsEnabled 상태 변경 시 호출되는 메서드
        partial void OnShowWarningZonesChanged(bool value)
        {
            // 경고 구역 표시/숨김 처리는 XAML Visibility 바인딩으로 자동 처리됨
            System.Diagnostics.Debug.WriteLine($"Warning zones visibility changed: {value}");
        }
        
        partial void OnShowDangerZonesChanged(bool value)
        {
            // 위험 구역 표시/숨김 처리는 XAML Visibility 바인딩으로 자동 처리됨
            System.Diagnostics.Debug.WriteLine($"Danger zones visibility changed: {value}");
        }
        
        // 디버그 옵션 변경 이벤트 처리
        partial void OnShowAllDetectionsChanged(bool value)
        {
            StaticShowAllDetections = value;
            System.Diagnostics.Debug.WriteLine($"Show all detections changed: {value}");
        }
        
        partial void OnShowDetailedInfoChanged(bool value)
        {
            StaticShowDetailedInfo = value;
            System.Diagnostics.Debug.WriteLine($"Show detailed info changed: {value}");
        }
        
        private void OnZoneUpdated(object? sender, Services.ZoneUpdateEventArgs e)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                System.Diagnostics.Debug.WriteLine($"Dashboard received zone update: {e.Zone.Name}, IsEnabled={e.IsEnabled}");
                
                // 해당 카메라의 구역을 찾아서 업데이트
                var camera = Cameras.FirstOrDefault(c => c.CameraId == e.Zone.CameraId);
                if (camera != null)
                {
                    // 경고 구역 업데이트
                    var warningZone = camera.WarningZones.FirstOrDefault(z => z.ZoneId == e.ZoneId);
                    if (warningZone != null)
                    {
                        warningZone.IsEnabled = e.IsEnabled;
                        warningZone.Opacity = e.IsEnabled ? e.Zone.Opacity : 0.05;
                        System.Diagnostics.Debug.WriteLine($"Updated warning zone visualization: {warningZone.Name} for camera {camera.CameraId}");
                        return;
                    }
                    
                    // 위험 구역 업데이트
                    var dangerZone = camera.DangerZones.FirstOrDefault(z => z.ZoneId == e.ZoneId);
                    if (dangerZone != null)
                    {
                        dangerZone.IsEnabled = e.IsEnabled;
                        dangerZone.Opacity = e.IsEnabled ? e.Zone.Opacity : 0.05;
                        System.Diagnostics.Debug.WriteLine($"Updated danger zone visualization: {dangerZone.Name} for camera {camera.CameraId}");
                    }
                }
            });
        }
        
        private void UpdateCameraZones()
        {
            foreach (var camera in Cameras)
            {
                // 해당 카메라의 경고 구역 설정
                camera.WarningZones.Clear();
                if (_cameraWarningZones.ContainsKey(camera.CameraId))
                {
                    foreach (var zone in _cameraWarningZones[camera.CameraId])
                    {
                        camera.WarningZones.Add(zone);
                    }
                }
                
                // 해당 카메라의 위험 구역 설정
                camera.DangerZones.Clear();
                if (_cameraDangerZones.ContainsKey(camera.CameraId))
                {
                    foreach (var zone in _cameraDangerZones[camera.CameraId])
                    {
                        camera.DangerZones.Add(zone);
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"Camera {camera.CameraId}: {camera.WarningZones.Count} warning zones, {camera.DangerZones.Count} danger zones");
            }
        }
        
        /// <summary>
        /// OpenCV를 사용하여 프레임에 구역 오버레이를 직접 그립니다
        /// </summary>
        private Mat DrawZoneOverlaysOnFrame(Mat originalFrame, string cameraId)
        {
            if (originalFrame == null || originalFrame.Empty())
                return originalFrame;
            
            // 원본 프레임 복사
            var frameWithZones = originalFrame.Clone();
            
            try
            {
                var frameWidth = frameWithZones.Width;
                var frameHeight = frameWithZones.Height;
                
                // 경고 구역 그리기 (주황색)
                if (ShowWarningZones && _cameraWarningZones.ContainsKey(cameraId))
                {
                    foreach (var zone in _cameraWarningZones[cameraId])
                    {
                        if (zone.IsEnabled && zone.RelativePoints.Count >= 3)
                        {
                            DrawZoneOnFrame(frameWithZones, zone, frameWidth, frameHeight, new Scalar(0, 165, 255)); // Orange
                        }
                    }
                }
                
                // 위험 구역 그리기 (빨간색)
                if (ShowDangerZones && _cameraDangerZones.ContainsKey(cameraId))
                {
                    foreach (var zone in _cameraDangerZones[cameraId])
                    {
                        if (zone.IsEnabled && zone.RelativePoints.Count >= 3)
                        {
                            DrawZoneOnFrame(frameWithZones, zone, frameWidth, frameHeight, new Scalar(0, 0, 255)); // Red
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Zone drawing error: {ex.Message}");
            }
            
            return frameWithZones;
        }
        
        /// <summary>
        /// 개별 구역을 프레임에 그립니다
        /// </summary>
        private void DrawZoneOnFrame(Mat frame, ZoneVisualization zone, int frameWidth, int frameHeight, Scalar color)
        {
            try
            {
                // 상대 좌표(0~1)를 픽셀 좌표로 변환
                var points = new List<Point>();
                
                foreach (var relativePoint in zone.RelativePoints)
                {
                    var pixelX = (int)(relativePoint.X * frameWidth);
                    var pixelY = (int)(relativePoint.Y * frameHeight);
                    
                    // 프레임 경계 내로 제한
                    pixelX = Math.Max(0, Math.Min(frameWidth - 1, pixelX));
                    pixelY = Math.Max(0, Math.Min(frameHeight - 1, pixelY));
                    
                    points.Add(new Point(pixelX, pixelY));
                }
                
                if (points.Count >= 3)
                {
                    // 성능 최적화: 투명도가 낮은 경우 오버레이 생성하지 않음
                    if (zone.Opacity > 0.05)
                    {
                        using (var overlay = frame.Clone())
                        {
                            Cv2.FillPoly(overlay, new Point[][] { points.ToArray() }, color);
                            
                            // 투명도 적용하여 원본과 합성
                            var alpha = zone.Opacity;
                            Cv2.AddWeighted(frame, 1.0 - alpha, overlay, alpha, 0, frame);
                        }
                    }
                    
                    // 구역 경계선 그리기 (항상 표시)
                    Cv2.Polylines(frame, new Point[][] { points.ToArray() }, true, color, 2);
                    
                    // 구역 이름 표시 (성능을 위해 중심점 계산 최적화)
                    if (points.Count > 0)
                    {
                        // Average 대신 더 빠른 계산
                        var sumX = 0;
                        var sumY = 0;
                        foreach (var p in points)
                        {
                            sumX += p.X;
                            sumY += p.Y;
                        }
                        var centerX = sumX / points.Count;
                        var centerY = sumY / points.Count;
                        
                        // OpenCV는 한글을 지원하지 않으므로 영문 라벨 사용
                        var displayText = zone.Name.Contains("경고구역") ? "WARNING" : "DANGER";
                        Cv2.PutText(frame, displayText, new Point(centerX - 30, centerY), 
                            HersheyFonts.HersheySimplex, 0.6, new Scalar(255, 255, 255), 2);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Individual zone drawing error for {zone.Name}: {ex.Message}");
            }
        }
    }
    
    // 각 카메라를 위한 ViewModel
    public partial class CameraViewModel : ObservableObject, IDisposable
    {
        [ObservableProperty]
        private string cameraId = string.Empty;
        
        [ObservableProperty]
        private string cameraName = string.Empty;
        
        [ObservableProperty]
        private bool isConnected;
        
        [ObservableProperty]
        private BitmapSource? currentFrame;
        
        [ObservableProperty]
        private int detectionCount;
        
        // 해당 카메라의 구역들
        [ObservableProperty]
        private ObservableCollection<ZoneVisualization> warningZones = new();
        
        [ObservableProperty]
        private ObservableCollection<ZoneVisualization> dangerZones = new();
        
        public void Dispose()
        {
            CurrentFrame = null;
        }
        
        // 프레임 존재 여부 확인용 속성 추가
        public bool HasFrame => CurrentFrame != null;
    }
    
    // DashboardViewModel 추적 관련 메소드들
    public partial class DashboardViewModel
    {
        /// <summary>
        /// 백그라운드 추적 서비스 업데이트 이벤트 처리
        /// </summary>
        private void OnBackgroundTrackingUpdated(object? sender, TrackingUpdateEventArgs e)
        {
            try
            {
                // 최신 추적 결과 저장
                _latestTrackedPersons[e.CameraId] = e.TrackedPersons;
                
                // 검출 결과도 업데이트 (트래킹 ID 포함)
                _latestDetections[e.CameraId] = e.DetectionsWithTracking;
                
                // UI 스레드에서 추적 상태 업데이트
                App.Current.Dispatcher.BeginInvoke(() =>
                {
                    // 추적 활성 상태 업데이트
                    var totalActiveTracks = _latestTrackedPersons.Values.Sum(list => list.Count(p => p.IsActive));
                    IsTrackingActive = totalActiveTracks > 0;
                    
                    // MonitoringService에서 통계 가져오기
                    ActiveTrackersCount = App.MonitoringService.ActiveTrackersCount;
                    TotalTrackersCreated = App.MonitoringService.TotalTrackersCreated;
                });
                
                System.Diagnostics.Debug.WriteLine($"DashboardViewModel: Tracking update for {e.CameraId} - {e.TrackedPersons.Count} active tracks");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DashboardViewModel: Tracking update error - {ex.Message}");
            }
        }
        
        /// <summary>
        /// 기본 검출 박스 그리기 (추적 정보 없이)
        /// </summary>
        private void DrawBasicDetectionBoxes(Mat frame, List<DetectionResult> detections)
        {
            foreach (var detection in detections)
            {
                // AI 검출 좌표를 현재 프레임 크기에 맞게 스케일링
                var scaledBoundingBox = ScaleDetectionToFrame(detection.BoundingBox, frame.Width, frame.Height);
                
                var rect = new Rect(
                    (int)scaledBoundingBox.X,
                    (int)scaledBoundingBox.Y,
                    (int)scaledBoundingBox.Width,
                    (int)scaledBoundingBox.Height
                );

                var color = detection.ClassName.ToLower() == "person" 
                    ? new Scalar(0, 0, 255) : new Scalar(0, 255, 0);

                Cv2.Rectangle(frame, rect, color, 2);

                // 라벨 텍스트
                var label = $"{detection.ClassName} ({detection.Confidence:P0})";
                
                // 텍스트 배경
                var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, 0.5, 1, out var baseline);
                var textRect = new Rect(
                    rect.X,
                    rect.Y - textSize.Height - 5,
                    textSize.Width + 5,
                    textSize.Height + 5
                );

                Cv2.Rectangle(frame, textRect, color, -1);

                // 텍스트 그리기
                Cv2.PutText(frame, label,
                    new Point(rect.X + 2, rect.Y - 5),
                    HersheyFonts.HersheySimplex,
                    0.5,
                    new Scalar(255, 255, 255),
                    1);

                // 중심점 표시
                var center = new Point(
                    rect.X + rect.Width / 2,
                    rect.Y + rect.Height / 2
                );
                Cv2.Circle(frame, center, 3, color, -1);
            }
        }
        
        /// <summary>
        /// AI 검출 좌표를 현재 프레임 크기에 맞게 스케일링
        /// </summary>
        private System.Drawing.RectangleF ScaleDetectionToFrame(System.Drawing.RectangleF originalBox, int targetWidth, int targetHeight)
        {
            // AI 검출 결과에서 추정되는 원본 프레임 크기
            // Detection Box 정보로 보아 AI는 더 큰 해상도(예: 1280x720)를 사용하는 것으로 추정
            // UI Frame은 960x540이므로 스케일링이 필요
            
            // 가능한 AI 원본 해상도들을 시도해보자
            // 검출 박스가 (372, 353, 725, 716)이고 화면 오른쪽에 나타나므로
            // AI 원본이 1280x720일 가능성이 높음
            
            float aiWidth = 1280f;  // AI 원본 추정 너비
            float aiHeight = 720f;  // AI 원본 추정 높이
            
            // 스케일링 비율 계산
            float scaleX = targetWidth / aiWidth;
            float scaleY = targetHeight / aiHeight;
            
            // 좌표 스케일링
            var scaledBox = new System.Drawing.RectangleF(
                originalBox.X * scaleX,
                originalBox.Y * scaleY,
                originalBox.Width * scaleX,
                originalBox.Height * scaleY
            );
            
            // 디버그: 스케일링 정보 출력
            System.Diagnostics.Debug.WriteLine($"Scale from {aiWidth}x{aiHeight} to {targetWidth}x{targetHeight}: " +
                $"Original=({originalBox.X:F1}, {originalBox.Y:F1}, {originalBox.Width:F1}, {originalBox.Height:F1}) -> " +
                $"Scaled=({scaledBox.X:F1}, {scaledBox.Y:F1}, {scaledBox.Width:F1}, {scaledBox.Height:F1})");
            
            return scaledBox;
        }
        
        
        /// <summary>
        /// 프레임에 추적 정보 그리기
        /// </summary>
        private Mat DrawTrackingOverlaysOnFrame(Mat frame, string cameraId, List<DetectionResult>? detections)
        {
            try
            {
                // 백그라운드 서비스에서 관리되는 추적 데이터 가져오기
                var trackedPersons = _latestTrackedPersons.GetValueOrDefault(cameraId, new List<TrackedPerson>());
                
                // 추적 데이터가 없거나 백그라운드 서비스를 사용할 수 없으면 원본 프레임 반환
                if (!trackedPersons.Any() || App.TrackingService == null)
                {
                    return frame; // CameraService가 이미 기본 검출 박스를 그림
                }
                
                // 프레임 복사본 생성
                var drawFrame = frame.Clone();
                
                // 추적 경로 그리기 (체크박스 설정 확인)
                foreach (var person in trackedPersons.Where(p => p.IsActive))
                {
                    // 추적 ID별 색상 결정
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
                    
                    // UI 프레임 크기에 맞게 스케일링 (CameraService에서 0.5 스케일 적용)
                    var scale = 0.5f;
                    
                    // 경로 표시 설정 확인 후 경로 그리기
                    if (ShowTrackingPaths && person.TrackingHistory != null && person.TrackingHistory.Count >= 2)
                    {
                        // 경로 표시 (더 길게 50개 점으로 확장)
                        var recentPath = person.TrackingHistory.TakeLast(50).ToList();
                        
                        if (recentPath.Count >= 2)
                        {
                            // 경로 선을 꼬리처럼 자연스럽게 그리기 (뒤에서부터 서서히 사라지는 효과)
                            for (int i = 0; i < recentPath.Count - 1; i++)
                            {
                                var startPoint = new Point(
                                    (int)(recentPath[i].X * scale), 
                                    (int)(recentPath[i].Y * scale)
                                );
                                var endPoint = new Point(
                                    (int)(recentPath[i + 1].X * scale), 
                                    (int)(recentPath[i + 1].Y * scale)
                                );
                                
                                // 진행률 (0.0 = 가장 오래된 점, 1.0 = 최신 점)
                                var progress = (float)i / (recentPath.Count - 1);
                                
                                // 알파값 계산 (뒤쪽부터 서서히 사라짐)
                                var alpha = Math.Max(0.1f, progress * progress); // 제곱으로 더 부드러운 페이드아웃
                                
                                // 두께 계산 (앞쪽이 더 두껍게)
                                var thickness = Math.Max(1, (int)(4 * progress + 1));
                                
                                // 색상에 알파값 적용 (BGR에서 투명도는 별도 처리)
                                var fadeColor = new Scalar(
                                    pathColor.Val0 * alpha,  // B
                                    pathColor.Val1 * alpha,  // G  
                                    pathColor.Val2 * alpha   // R
                                );
                                
                                // 선 그리기
                                Cv2.Line(drawFrame, startPoint, endPoint, fadeColor, thickness);
                                
                                // 추가적으로 각 점에 작은 원 그리기 (더 부드러운 연결)
                                if (progress > 0.3f) // 너무 오래된 점은 원 생략
                                {
                                    var pointRadius = Math.Max(1, (int)(3 * progress));
                                    Cv2.Circle(drawFrame, endPoint, pointRadius, fadeColor, -1);
                                }
                            }
                        }
                    }
                    
                    // 현재 위치와 ID 표시 (체크박스 설정에 따라)
                    var centerPoint = new Point(
                        (int)(person.BoundingBox.X + person.BoundingBox.Width / 2) / 2, // 0.5 스케일 적용
                        (int)(person.BoundingBox.Y + person.BoundingBox.Height / 2) / 2
                    );
                    
                    // 현재 위치에 원 표시 (항상)
                    Cv2.Circle(drawFrame, centerPoint, 5, pathColor, -1);
                    
                    // ID 표시 (체크박스 확인)
                    if (ShowTrackingIds)
                    {
                        var idText = $"#{person.TrackingId}";
                        var textPos = new Point(centerPoint.X + 10, centerPoint.Y - 10);
                        Cv2.PutText(drawFrame, idText, textPos, HersheyFonts.HersheySimplex, 
                                  0.6, new Scalar(255, 255, 255), 2);
                    }
                }
                
                return drawFrame;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Tracking overlay error for camera {cameraId}: {ex.Message}");
                return frame; // 오류 발생 시 원본 프레임 반환
            }
        }
    }
}