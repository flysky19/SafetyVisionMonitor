using System;
using System.Diagnostics;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// CUDA GPU 사용량 실시간 모니터링
    /// </summary>
    public class GPUUsageMonitor : IDisposable
    {
        private readonly Timer _monitorTimer;
        private bool _disposed = false;

        // GPU 사용량 통계
        public double CurrentGPUUsage { get; private set; } = 0;
        public double AverageGPUUsage { get; private set; } = 0;
        public long GPUMemoryUsed { get; private set; } = 0;
        public long GPUMemoryTotal { get; private set; } = 0;
        public bool IsUsingGPU { get; private set; } = false;
        public string GPUName { get; private set; } = "Unknown";

        public event EventHandler<GPUUsageReport>? GPUUsageUpdated;

        public GPUUsageMonitor()
        {
            InitializeGPUInfo();
            
            // 1초마다 GPU 사용량 체크 (더 빈번한 모니터링)
            _monitorTimer = new Timer(CheckGPUUsage, null, 
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                
            Debug.WriteLine("GPUUsageMonitor: CUDA GPU 모니터링 시작");
        }

        private void InitializeGPUInfo()
        {
            try
            {
                // GPU 이름 가져오기
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name FROM Win32_VideoController WHERE Name LIKE '%NVIDIA%' OR Name LIKE '%GeForce%' OR Name LIKE '%RTX%'");
                    
                foreach (ManagementObject obj in searcher.Get())
                {
                    GPUName = obj["Name"]?.ToString() ?? "NVIDIA GPU";
                    break;
                }
                
                Debug.WriteLine($"GPUUsageMonitor: GPU 감지됨 - {GPUName}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GPUUsageMonitor: GPU 정보 초기화 실패 - {ex.Message}");
            }
        }

        private void CheckGPUUsage(object? state)
        {
            if (_disposed) return;

            try
            {
                var usage = GetNVIDIASMIUsage();
                if (usage.HasValue)
                {
                    CurrentGPUUsage = usage.Value;
                    UpdateAverageUsage(CurrentGPUUsage);
                    
                    // GPU 사용량이 2% 이상이면 실제 사용 중 (더 민감하게)
                    IsUsingGPU = CurrentGPUUsage >= 2.0;
                    
                    var report = new GPUUsageReport
                    {
                        Timestamp = DateTime.Now,
                        GPUName = GPUName,
                        CurrentUsage = CurrentGPUUsage,
                        AverageUsage = AverageGPUUsage,
                        MemoryUsedMB = GPUMemoryUsed,
                        MemoryTotalMB = GPUMemoryTotal,
                        IsActivelyUsing = IsUsingGPU
                    };
                    
                    GPUUsageUpdated?.Invoke(this, report);
                    
                    // 주요 상태 변화시 로깅
                    LogGPUStatus();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GPUUsageMonitor: GPU 사용량 체크 실패 - {ex.Message}");
            }
        }

        /// <summary>
        /// nvidia-smi를 통한 정확한 GPU 사용량 측정
        /// </summary>
        private double? GetNVIDIASMIUsage()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=utilization.gpu,memory.used,memory.total --format=csv,noheader,nounits",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    process.WaitForExit(5000); // 5초 타임아웃
                    
                    if (process.ExitCode == 0)
                    {
                        var output = process.StandardOutput.ReadToEnd().Trim();
                        var parts = output.Split(',');
                        
                        if (parts.Length >= 3)
                        {
                            if (double.TryParse(parts[0].Trim(), out var gpuUsage))
                            {
                                // 메모리 정보도 업데이트
                                if (long.TryParse(parts[1].Trim(), out var memUsed))
                                    GPUMemoryUsed = memUsed;
                                    
                                if (long.TryParse(parts[2].Trim(), out var memTotal))
                                    GPUMemoryTotal = memTotal;
                                
                                return gpuUsage;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"nvidia-smi 실행 실패: {ex.Message}");
            }

            // nvidia-smi 실패시 WMI 대안 사용
            return GetWMIGPUUsage();
        }

        /// <summary>
        /// WMI를 통한 대안 GPU 사용량 측정
        /// </summary>
        private double? GetWMIGPUUsage()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PerfRawData_GPUPerformanceCounters_GPUEngine");
                    
                double totalUsage = 0;
                int engineCount = 0;
                
                foreach (ManagementObject obj in searcher.Get())
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
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WMI GPU 사용량 측정 실패: {ex.Message}");
            }
            
            return null;
        }

        private void UpdateAverageUsage(double currentUsage)
        {
            // 간단한 지수 이동 평균
            AverageGPUUsage = AverageGPUUsage * 0.8 + currentUsage * 0.2;
        }

        // GPU 상태 로깅을 위한 정적 변수들
        private static bool _lastWasUsingGPU = false;
        private static DateTime _lastLogTime = DateTime.MinValue;
        
        private void LogGPUStatus()
        {
            
            // 상태 변화시 또는 5초마다 로깅 (더 빈번한 로깅)
            var shouldLog = (IsUsingGPU != _lastWasUsingGPU) || 
                           (DateTime.Now - _lastLogTime).TotalSeconds > 5;
                           
            if (shouldLog)
            {
                if (CurrentGPUUsage < 1.0)
                {
                    Debug.WriteLine($"🚨 GPU 미사용 감지! 사용률: {CurrentGPUUsage:F1}% (CPU 전용 모드?)");
                }
                else if (CurrentGPUUsage < 10.0)
                {
                    Debug.WriteLine($"⚠️ GPU 저사용: {CurrentGPUUsage:F1}% (일부 처리만 GPU 사용)");
                }
                else
                {
                    Debug.WriteLine($"🚀 GPU 활성 사용: {CurrentGPUUsage:F1}% (CUDA 가속 중!)");
                }
                
                Debug.WriteLine($"GPU 메모리: {GPUMemoryUsed}MB / {GPUMemoryTotal}MB");
                
                _lastWasUsingGPU = IsUsingGPU;
                _lastLogTime = DateTime.Now;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _monitorTimer?.Dispose();
            Debug.WriteLine("GPUUsageMonitor: 모니터링 종료");
        }
    }

    /// <summary>
    /// GPU 사용량 보고서
    /// </summary>
    public class GPUUsageReport
    {
        public DateTime Timestamp { get; set; }
        public string GPUName { get; set; } = "";
        public double CurrentUsage { get; set; }
        public double AverageUsage { get; set; }
        public long MemoryUsedMB { get; set; }
        public long MemoryTotalMB { get; set; }
        public bool IsActivelyUsing { get; set; }
        
        public string StatusMessage => 
            IsActivelyUsing ? 
            $"🚀 GPU 활성: {CurrentUsage:F1}%" : 
            $"💤 GPU 미사용: {CurrentUsage:F1}%";
    }
}