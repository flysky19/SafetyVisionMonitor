using System;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using SafetyVisionMonitor.Helpers;
using SafetyVisionMonitor.Services;

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
        }
        
        public override void OnActivated()
        {
            base.OnActivated();
            _updateTimer?.Start();
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
                    Cv2.PutText(testMat, "TEST IMAGE", new Point(200, 240), 
                        HersheyFonts.HersheySimplex, 2.0, new Scalar(255, 255, 255), 3);
            
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
                        using (var frame = e.Frame)
                        {
                            if (frame != null && !frame.Empty())
                            {
                                // UI 스레드에서 BitmapSource 변환 (크로스 스레드 오류 방지)
                                var bitmap = ImageConverter.MatToBitmapSource(frame);
        
                                if (bitmap != null)
                                {
                                    var cameraVm = Cameras.FirstOrDefault(c => c.CameraId == e.CameraId);
                                    if (cameraVm != null)
                                    {
                                        // 이전 프레임 정리 (GC 부담 감소)
                                        var oldFrame = cameraVm.CurrentFrame;
                                        cameraVm.CurrentFrame = bitmap;
                                        cameraVm.DetectionCount++;
                                
                                        System.Diagnostics.Debug.WriteLine(
                                            $"Frame updated for {e.CameraId}: {bitmap.PixelWidth}x{bitmap.PixelHeight}");
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
        
        public void Dispose()
        {
            CurrentFrame = null;
        }
        
        // 프레임 존재 여부 확인용 속성 추가
        public bool HasFrame => CurrentFrame != null;
    }
}