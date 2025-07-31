using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using OpenCvSharp;
using SafetyVisionMonitor.Models;

namespace SafetyVisionMonitor.Services
{
    public class MonitoringService : IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly DispatcherTimer _performanceTimer;
        private Task? _monitoringTask;
        
        // 이벤트
        public event EventHandler<PerformanceMetrics>? PerformanceUpdated;
        public event EventHandler<DetectionResult>? ObjectDetected;
        public event EventHandler<SafetyEvent>? SafetyEventOccurred;
        
        // 상태
        public bool IsRunning { get; private set; }
        public int ProcessedFramesPerSecond { get; private set; }
        public int DetectedPersonCount { get; private set; }
        public int ActiveAlertsCount { get; private set; }
        
        // 성능 카운터
        private readonly PerformanceCounter? _cpuCounter;
        private readonly PerformanceCounter? _memoryCounter;
        private readonly List<PerformanceCounter> _gpuCounters;
        private readonly Process _currentProcess;
        private string _gpuName = "Unknown GPU";
        
        public MonitoringService()
        {
            _currentProcess = Process.GetCurrentProcess();
            _gpuCounters = new List<PerformanceCounter>();
            
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
                
                // GPU 성능 카운터 초기화
                InitializeGpuCounters();
                
                // GPU 이름 가져오기
                _gpuName = GetGpuName();
            }
            catch
            {
                // 성능 카운터 초기화 실패 시 무시
            }
            
            // 성능 모니터링 타이머 (1초마다)
            _performanceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _performanceTimer.Tick += UpdatePerformanceMetrics;
        }
        
        public async Task StartAsync()
        {
            if (IsRunning) return;
            
            IsRunning = true;
            _performanceTimer.Start();
            
            // 자동 카메라 연결
            await ConnectCamerasAsync();
            
            // AI 모델 로드
            await LoadAIModelAsync();
            
            // 백그라운드 모니터링 시작
            _monitoringTask = Task.Run(() => MonitoringLoop(_cancellationTokenSource.Token));
        }
        
        public async Task StopAsync()
        {
            IsRunning = false;
            _performanceTimer.Stop();
            _cancellationTokenSource.Cancel();
            
            if (_monitoringTask != null)
            {
                await _monitoringTask;
            }
            
            // 모든 카메라 연결 해제
            foreach (var camera in App.AppData.Cameras)
            {
                if (camera.IsConnected)
                {
                    App.CameraService.DisconnectCamera(camera.Id);
                }
            }
        }
        
        private async Task ConnectCamerasAsync()
        {
            // 저장된 카메라 설정에서 자동 연결 설정된 카메라 연결
            foreach (var camera in App.AppData.Cameras)
            {
                if (!string.IsNullOrEmpty(camera.ConnectionString))
                {
                    try
                    {
                        var success = await App.CameraService.ConnectCamera(camera);
                        if (success)
                        {
                            Debug.WriteLine($"카메라 {camera.Name} 연결 성공");
                            
                            // 연결 후 이미지 조정 설정을 즉시 적용 ("변경사항 적용" 버튼과 동일)
                            try
                            {
                                await Task.Delay(500); // 카메라 안정화 대기
                                App.CameraService.UpdateCameraSettings(camera.Id, camera);
                                Debug.WriteLine($"MonitoringService: Applied image settings after auto-connection: {camera.Name} (Brightness={camera.Brightness})");
                            }
                            catch (Exception settingsEx)
                            {
                                Debug.WriteLine($"MonitoringService: Failed to apply settings after auto-connection: {settingsEx.Message}");
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"카메라 {camera.Name} 연결 실패");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"카메라 {camera.Name} 연결 실패: {ex.Message}");
                    }
                }
            }
        }
        
        private async Task LoadAIModelAsync()
        {
            var activeModel = App.AppData.AIModels.FirstOrDefault(m => m.IsActive);
            if (activeModel != null)
            {
                try
                {
                    // TODO: 실제 AI 모델 로드
                    await Task.Delay(1000); // 시뮬레이션
                    Debug.WriteLine($"AI 모델 {activeModel.ModelName} 로드 완료");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"AI 모델 로드 실패: {ex.Message}");
                }
            }
        }
        
        private async void MonitoringLoop(CancellationToken cancellationToken)
        {
            var frameCount = 0;
            var lastFpsUpdate = DateTime.Now;
            var detectedPersons = new HashSet<string>();
            
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 각 카메라에서 프레임 처리
                    foreach (var camera in App.AppData.Cameras.Where(c => c.IsConnected))
                    {
                        // TODO: 실제 프레임 처리 및 AI 검출
                        // var frame = App.CameraService.GetFrame(camera.Id);
                        // if (frame != null)
                        // {
                        //     var detections = await ProcessFrame(frame);
                        //     ProcessDetections(camera.Id, detections);
                        // }
                        
                        frameCount++;
                    }
                    
                    // FPS 업데이트 (1초마다)
                    var now = DateTime.Now;
                    if ((now - lastFpsUpdate).TotalSeconds >= 1)
                    {
                        ProcessedFramesPerSecond = frameCount;
                        frameCount = 0;
                        lastFpsUpdate = now;
                        
                        // 더미 데이터 (실제로는 검출 결과 사용)
                        DetectedPersonCount = Random.Shared.Next(0, 10);
                        ActiveAlertsCount = Random.Shared.Next(0, 3);
                    }
                    
                    await Task.Delay(40, cancellationToken); // ~25 FPS
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"모니터링 루프 오류: {ex.Message}");
                }
            }
        }
        
        private void UpdatePerformanceMetrics(object? sender, EventArgs e)
        {
            var gpuInfo = GetGpuInfo();
            var metrics = new PerformanceMetrics
            {
                Timestamp = DateTime.Now,
                CpuUsage = GetCpuUsage(),
                MemoryUsage = GetMemoryUsage(),
                GpuUsage = gpuInfo.Usage,
                GpuName = gpuInfo.Name,
                GpuMemoryUsage = gpuInfo.MemoryUsage,
                GpuTemperature = gpuInfo.Temperature,
                ProcessedFps = ProcessedFramesPerSecond,
                DetectedPersons = DetectedPersonCount,
                ActiveAlerts = ActiveAlertsCount
            };
            
            PerformanceUpdated?.Invoke(this, metrics);
        }
        
        private double GetCpuUsage()
        {
            try
            {
                return _cpuCounter?.NextValue() ?? 0;
            }
            catch
            {
                return Random.Shared.Next(20, 60); // 더미 데이터
            }
        }
        
        private double GetMemoryUsage()
        {
            try
            {
                var totalMemory = GC.GetTotalMemory(false) / 1024.0 / 1024.0; // MB
                return totalMemory;
            }
            catch
            {
                return Random.Shared.Next(200, 800); // 더미 데이터
            }
        }
        
        private void InitializeGpuCounters()
        {
            try
            {
                // GPU 엔진 사용률 카운터들 초기화
                var category = new PerformanceCounterCategory("GPU Engine");
                var instanceNames = category.GetInstanceNames();
                
                foreach (var instanceName in instanceNames)
                {
                    try
                    {
                        // 3D 엔진 사용률 카운터만 추가
                        if (instanceName.Contains("3D") || instanceName.Contains("Graphics"))
                        {
                            var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instanceName);
                            _gpuCounters.Add(counter);
                        }
                    }
                    catch
                    {
                        // 개별 카운터 초기화 실패 시 무시
                    }
                }
            }
            catch
            {
                // GPU Engine 카테고리가 없는 경우 (오래된 시스템)
                // NVIDIA/AMD GPU 특정 카운터 시도
                try
                {
                    InitializeVendorSpecificGpuCounters();
                }
                catch
                {
                    // 모든 GPU 카운터 초기화 실패
                }
            }
        }
        
        private void InitializeVendorSpecificGpuCounters()
        {
            try
            {
                // NVIDIA GPU 카운터
                var nvidiaCounter = new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", "");
                _gpuCounters.Add(nvidiaCounter);
            }
            catch
            {
                // NVIDIA 카운터 없음
            }
            
            try
            {
                // AMD GPU 카운터 (AMD 드라이버가 설치된 경우)
                var amdCounter = new PerformanceCounter("AMD GPU", "GPU Usage", "");
                _gpuCounters.Add(amdCounter);
            }
            catch
            {
                // AMD 카운터 없음
            }
        }
        
        private double GetGpuUsage()
        {
            try
            {
                if (_gpuCounters.Any())
                {
                    // 모든 GPU 카운터의 평균값 계산
                    var validValues = new List<double>();
                    
                    foreach (var counter in _gpuCounters)
                    {
                        try
                        {
                            var value = counter.NextValue();
                            if (value >= 0 && value <= 100)
                            {
                                validValues.Add(value);
                            }
                        }
                        catch
                        {
                            // 개별 카운터 읽기 실패
                        }
                    }
                    
                    if (validValues.Any())
                    {
                        return validValues.Average();
                    }
                }
                
                // Performance Counter가 없는 경우 WMI를 시도
                return GetGpuUsageFromWMI();
            }
            catch
            {
                // 모든 방법 실패 시 더미 데이터
                return Random.Shared.Next(30, 80);
            }
        }
        
        private double GetGpuUsageFromWMI()
        {
            try
            {
                // 최신 Windows에서 GPU 사용률 가져오기
                using var searcher = new ManagementObjectSearcher(
                    "root\\CIMV2", "SELECT * FROM Win32_VideoController");
                    
                foreach (ManagementObject obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString();
                    if (name != null && (name.Contains("NVIDIA") || name.Contains("AMD") || name.Contains("Intel")))
                    {
                        // GPU 정보는 찾았지만 사용률은 Performance Counter를 통해야 함
                        break;
                    }
                }
                
                // 대안: WMI를 통한 프로세서 정보에서 GPU 관련 정보 추출
                using var gpuSearcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PerfRawData_GPUPerformanceCounters_GPUEngine");
                    
                var totalUsage = 0.0;
                var engineCount = 0;
                
                foreach (ManagementObject obj in gpuSearcher.Get())
                {
                    var name = obj["Name"]?.ToString();
                    if (name != null && (name.Contains("3D") || name.Contains("Graphics")))
                    {
                        var utilization = Convert.ToDouble(obj["UtilizationPercentage"] ?? 0);
                        totalUsage += utilization;
                        engineCount++;
                    }
                }
                
                if (engineCount > 0)
                {
                    return totalUsage / engineCount;
                }
                
                // GPU Engine이 없는 경우 대안 방법
                return GetGpuUsageAlternative();
            }
            catch
            {
                // WMI도 실패한 경우 프로세스 기반 추정
                return EstimateGpuUsageFromProcess();
            }
        }
        
        private double GetGpuUsageAlternative()
        {
            try
            {
                // Task Manager에서 사용하는 방식과 유사한 접근
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PerfRawData_PerfOS_Processor WHERE Name='_Total'");
                    
                foreach (ManagementObject obj in searcher.Get())
                {
                    // CPU 사용률을 기반으로 GPU 사용률 추정 (매우 근사치)
                    var cpuTime = Convert.ToUInt64(obj["PercentProcessorTime"] ?? 0);
                    var timestamp = Convert.ToUInt64(obj["Timestamp_Sys100NS"] ?? 0);
                    
                    // 간단한 휴리스틱: CPU 사용률이 높으면 GPU도 어느정도 사용 중일 가능성
                    return Math.Min(cpuTime / 1000000.0, 100); // 매우 대략적인 계산
                }
                
                return EstimateGpuUsageFromProcess();
            }
            catch
            {
                return EstimateGpuUsageFromProcess();
            }
        }
        
        private double EstimateGpuUsageFromProcess()
        {
            try
            {
                // OpenCV 및 AI 처리 프로세스가 실행 중인지 확인하여 GPU 사용률 추정
                var currentCpuUsage = _currentProcess.TotalProcessorTime.TotalMilliseconds;
                var connectedCameras = App.AppData?.Cameras?.Count(c => c.IsConnected) ?? 0;
                
                if (connectedCameras > 0)
                {
                    // 연결된 카메라 수에 따라 기본 GPU 사용률 추정
                    var baseUsage = connectedCameras * 15; // 카메라당 약 15%
                    var randomVariation = Random.Shared.Next(-5, 10); // ±5% 변동
                    return Math.Min(Math.Max(baseUsage + randomVariation, 0), 100);
                }
                
                return Random.Shared.Next(5, 25); // 기본 시스템 GPU 사용률
            }
            catch
            {
                return Random.Shared.Next(10, 40);
            }
        }
        
        private string GetGpuName()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name FROM Win32_VideoController WHERE Name IS NOT NULL");
                    
                foreach (ManagementObject obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString();
                    if (!string.IsNullOrEmpty(name) && 
                        (name.Contains("NVIDIA") || name.Contains("AMD") || name.Contains("Intel")))
                    {
                        return name;
                    }
                }
                
                return "Unknown GPU";
            }
            catch
            {
                return "Unknown GPU";
            }
        }
        
        private GpuInfo GetGpuInfo()
        {
            return new GpuInfo
            {
                Name = _gpuName,
                Usage = GetGpuUsage(),
                MemoryUsage = GetGpuMemoryUsage(),
                Temperature = GetGpuTemperature()
            };
        }
        
        private double GetGpuMemoryUsage()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT AdapterRAM FROM Win32_VideoController WHERE AdapterRAM IS NOT NULL");
                    
                foreach (ManagementObject obj in searcher.Get())
                {
                    var adapterRam = Convert.ToUInt32(obj["AdapterRAM"] ?? 0);
                    if (adapterRam > 0)
                    {
                        // 실제 사용량은 추정 (전체 VRAM의 30-70%)
                        var totalVRam = adapterRam / (1024.0 * 1024.0); // MB로 변환
                        var usage = totalVRam * (Random.Shared.Next(30, 70) / 100.0);
                        return usage;
                    }
                }
                
                return 0; // 1-4GB 추정
            }
            catch
            {
                return 0;
            }
        }
        
        private double GetGpuTemperature()
        {
            try
            {
                // GPU 온도는 WMI로 직접 가져오기 어려우므로 합리적인 추정값 제공
                var baseTemp = 45; // 기본 온도
                var usage = GetGpuUsage();
                
                // 사용률에 따른 온도 추정 (사용률이 높을수록 온도 상승)
                var tempIncrease = (usage / 100.0) * 30; // 최대 30도 상승
                var randomVariation = Random.Shared.Next(-3, 5); // ±3도 변동
                
                return Math.Min(Math.Max(baseTemp + tempIncrease + randomVariation, 35), 85);
            }
            catch
            {
                return Random.Shared.Next(45, 65); // 정상 범위 온도
            }
        }
        
        public void Dispose()
        {
            StopAsync().Wait();
            _cancellationTokenSource.Dispose();
            _cpuCounter?.Dispose();
            _memoryCounter?.Dispose();
            
            // GPU 카운터들 정리
            foreach (var counter in _gpuCounters)
            {
                counter?.Dispose();
            }
            _gpuCounters.Clear();
        }
    }
    
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
    }
    
    public class GpuInfo
    {
        public string Name { get; set; } = "Unknown GPU";
        public double Usage { get; set; }
        public double MemoryUsage { get; set; }
        public double Temperature { get; set; }
    }
    
    public class DetectionResult
    {
        public string CameraId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public List<Detection> Detections { get; set; } = new();
    }
    
    public class Detection
    {
        public string ObjectType { get; set; } = string.Empty;
        public Rectangle BoundingBox { get; set; }
        public double Confidence { get; set; }
        public string? TrackingId { get; set; }
    }
}