using System;
using System.Collections.Generic;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 시스템 성능 기반 최적 카메라 수 계산기
    /// </summary>
    public static class PerformanceCalculator
    {
        public enum SystemProfile
        {
            Conservative,  // 안정적 운영 우선
            Balanced,     // 균형잡힌 성능
            Performance   // 최고 성능 우선
        }

        public enum GPUType
        {
            Integrated,   // 내장 그래픽 (Intel UHD 등)
            Entry,        // 엔트리급 전용 GPU (GTX 1650 등)
            Mid,          // 중급 GPU (RTX 3060 등) 
            High          // 고급 GPU (RTX 4070+ 등)
        }

        /// <summary>
        /// 스마트 AI 시스템 기반 최적 카메라 수 계산
        /// </summary>
        public static CameraRecommendation CalculateOptimalCameraCount(
            int cpuCores, 
            int ramGB, 
            GPUType gpuType, 
            SystemProfile profile)
        {
            var recommendation = new CameraRecommendation();
            
            // 기본 성능 지표
            var baseCpuCapacity = cpuCores * 0.8; // CPU 80% 사용률 기준
            var baseMemoryCapacity = ramGB - 4; // 시스템용 4GB 제외
            
            // GPU 성능 계수
            var gpuMultiplier = gpuType switch
            {
                GPUType.Integrated => 1.0,
                GPUType.Entry => 2.5,
                GPUType.Mid => 4.0,
                GPUType.High => 6.0,
                _ => 1.0
            };

            // 스마트 AI 시스템 효율성 (기존 대비 60-80% 성능 향상)
            var smartAIEfficiency = 2.5; // 평균 2.5배 효율 향상
            
            // 카메라별 리소스 요구량 (스마트 시스템 기준)
            var cpuPerCamera = profile switch
            {
                SystemProfile.Conservative => 1.2, // 여유롭게
                SystemProfile.Balanced => 1.0,     // 균형있게
                SystemProfile.Performance => 0.8,  // 공격적으로
                _ => 1.0
            };
            
            var memoryPerCamera = 800; // MB per camera (프레임 버퍼 포함)
            
            // 계산
            var cpuLimitedCount = (int)(baseCpuCapacity * gpuMultiplier * smartAIEfficiency / cpuPerCamera);
            var memoryLimitedCount = (int)(baseMemoryCapacity * 1024 / memoryPerCamera);
            
            // 병목 지점 결정
            var maxCameras = Math.Min(cpuLimitedCount, memoryLimitedCount);
            
            // 프로파일별 조정
            recommendation.OptimalCount = profile switch
            {
                SystemProfile.Conservative => Math.Max(1, maxCameras - 1),
                SystemProfile.Balanced => maxCameras,
                SystemProfile.Performance => maxCameras + 1,
                _ => maxCameras
            };
            
            recommendation.MaxCount = maxCameras + 2;
            recommendation.MinCount = Math.Max(1, maxCameras - 2);
            
            // 성능 예상치 계산
            recommendation.ExpectedPerformance = CalculateExpectedPerformance(
                recommendation.OptimalCount, cpuCores, ramGB, gpuType);
                
            return recommendation;
        }

        /// <summary>
        /// 현재 하드웨어 기준 권장사항 (i7 + 16GB 기준)
        /// </summary>
        public static Dictionary<GPUType, CameraRecommendation> GetCurrentHardwareRecommendations()
        {
            var recommendations = new Dictionary<GPUType, CameraRecommendation>();
            
            // i7 8코어, 16GB RAM 기준
            foreach (GPUType gpu in Enum.GetValues<GPUType>())
            {
                recommendations[gpu] = CalculateOptimalCameraCount(8, 16, gpu, SystemProfile.Balanced);
            }
            
            return recommendations;
        }

        private static PerformanceEstimate CalculateExpectedPerformance(
            int cameraCount, int cpuCores, int ramGB, GPUType gpuType)
        {
            return new PerformanceEstimate
            {
                ExpectedFPS = cameraCount <= 3 ? 25 : cameraCount <= 5 ? 20 : 15,
                CPUUsagePercent = Math.Min(85, cameraCount * 12),
                MemoryUsagePercent = Math.Min(80, cameraCount * 8),
                AIProcessingRate = cameraCount <= 4 ? 90 : cameraCount <= 6 ? 75 : 60,
                SystemStability = cameraCount <= 4 ? "매우안정" : cameraCount <= 6 ? "안정" : "주의필요"
            };
        }
    }

    public class CameraRecommendation
    {
        public int OptimalCount { get; set; } // 권장 카메라 수
        public int MaxCount { get; set; }     // 최대 가능 카메라 수
        public int MinCount { get; set; }     // 최소 안전 카메라 수
        public PerformanceEstimate ExpectedPerformance { get; set; } = new();
        public List<string> Notes { get; set; } = new();
        
        public override string ToString()
        {
            return $"권장: {OptimalCount}개, 최대: {MaxCount}개 (FPS: {ExpectedPerformance.ExpectedFPS}, CPU: {ExpectedPerformance.CPUUsagePercent}%)";
        }
    }

    public class PerformanceEstimate
    {
        public int ExpectedFPS { get; set; }
        public int CPUUsagePercent { get; set; }
        public int MemoryUsagePercent { get; set; }
        public int AIProcessingRate { get; set; } // AI 처리 효율 %
        public string SystemStability { get; set; } = "";
    }
}