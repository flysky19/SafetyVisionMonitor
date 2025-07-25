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

namespace SafetyVisionMonitor.ViewModels
{
    public partial class DashboardViewModel : BaseViewModel
    {
        private DispatcherTimer _updateTimer;
        private readonly Dictionary<string, BitmapSource?> _cameraFrames = new();
        private object _frameLock = new();
        
        // 카메라 관련
        [ObservableProperty]
        private ObservableCollection<CameraViewModel> cameras;
        
        // 3D 영역 표시 옵션
        [ObservableProperty]
        private bool showWarningZones = true;
        
        [ObservableProperty]
        private bool showDangerZones = true;
        
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
            }
        }
        
        public override void OnLoaded()
        {
            base.OnLoaded();
            
            // 서비스 이벤트 구독 (UI용 저화질 프레임 사용)
            App.CameraService.FrameReceivedForUI += OnFrameReceived;
            App.CameraService.ConnectionChanged += OnConnectionChanged;
            App.MonitoringService.PerformanceUpdated += OnPerformanceUpdated;
            
            // 구역 업데이트 이벤트 구독
            App.AppData.ZoneUpdated += OnZoneUpdated;
            
            // 구역 데이터 로드
            LoadZoneOverlaysAsync();
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
        
        public override void Cleanup()
        {
            base.Cleanup();
            
            // 이벤트 구독 해제는 ViewModel이 완전히 소멸될 때만
            App.CameraService.FrameReceivedForUI -= OnFrameReceived;
            App.CameraService.ConnectionChanged -= OnConnectionChanged;
            App.MonitoringService.PerformanceUpdated -= OnPerformanceUpdated;
            App.AppData.ZoneUpdated -= OnZoneUpdated;
            
            _updateTimer?.Stop();
        }
        
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
        
        private void OnFrameReceived(object? sender, CameraFrameEventArgs e)
        {
            try
            {
                if (e?.Frame == null || e.Frame.Empty())
                {
                    System.Diagnostics.Debug.WriteLine($"Empty frame received for {e?.CameraId}");
                    e?.Frame?.Dispose(); // 빈 프레임도 해제
                    return;
                }
        
                // UI 스레드에서 변환과 업데이트를 모두 처리
                App.Current.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                {
                    try
                    {
                        using (var originalFrame = e.Frame)
                        {
                            if (originalFrame != null && !originalFrame.Empty())
                            {
                                // 프레임에 구역 오버레이 그리기
                                var frameWithZones = DrawZoneOverlaysOnFrame(originalFrame, e.CameraId);
                                
                                // UI 스레드에서 BitmapSource 변환
                                var bitmap = ImageConverter.MatToBitmapSource(frameWithZones);
                                
                                // 그려진 프레임 해제
                                frameWithZones.Dispose();
        
                                if (bitmap != null)
                                {
                                    var cameraVm = Cameras.FirstOrDefault(c => c.CameraId == e.CameraId);
                                    if (cameraVm != null)
                                    {
                                        // 이전 프레임 정리 (GC 부담 감소)
                                        var oldFrame = cameraVm.CurrentFrame;
                                        cameraVm.CurrentFrame = bitmap;
                                        cameraVm.DetectionCount++;
                                
                                        //System.Diagnostics.Debug.WriteLine(
                                        //    $"Frame updated for {e.CameraId}: {bitmap.PixelWidth}x{bitmap.PixelHeight}");
                                    }
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"Conversion failed for {e.CameraId}");
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
                e?.Frame?.Dispose(); // 예외 발생 시에도 해제
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
                    // 다각형으로 채우기 (투명도 적용)
                    var overlay = frame.Clone();
                    Cv2.FillPoly(overlay, new Point[][] { points.ToArray() }, color);
                    
                    // 투명도 적용하여 원본과 합성
                    var alpha = zone.Opacity;
                    Cv2.AddWeighted(frame, 1.0 - alpha, overlay, alpha, 0, frame);
                    
                    // 구역 경계선 그리기
                    Cv2.Polylines(frame, new Point[][] { points.ToArray() }, true, color, 2);
                    
                    // 구역 이름 표시 (선택사항)
                    if (points.Count > 0)
                    {
                        var centerX = (int)points.Average(p => p.X);
                        var centerY = (int)points.Average(p => p.Y);
                        
                        Cv2.PutText(frame, zone.Name, new Point(centerX - 30, centerY), 
                            HersheyFonts.HersheySimplex, 0.6, new Scalar(255, 255, 255), 2);
                    }
                    
                    overlay.Dispose();
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
}