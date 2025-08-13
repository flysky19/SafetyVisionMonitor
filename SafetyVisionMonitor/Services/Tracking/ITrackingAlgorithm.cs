using System.Collections.Generic;
using System.Drawing;
using SafetyVisionMonitor.Models;

namespace SafetyVisionMonitor.Services.Tracking
{
    /// <summary>
    /// 트래킹 알고리즘 인터페이스
    /// </summary>
    public interface ITrackingAlgorithm
    {
        string Name { get; }
        List<TrackedPerson> UpdateTracking(List<DetectionResult> detections, string cameraId);
        TrackingStatistics GetStatistics();
        void Reset();
    }
    
    /// <summary>
    /// 트래킹 알고리즘 팩토리
    /// </summary>
    public static class TrackingAlgorithmFactory
    {
        public static ITrackingAlgorithm Create(string method, TrackingConfiguration config)
        {
            return method.ToUpper() switch
            {
                "SORT" => new SortTracker(config),
                "DEEPSORT" => new DeepSortTracker(config),
                "BYTETRACK" => new ByteTracker(config),
                "STRONGSORT" => new StrongSortTracker(config),
                _ => new SortTracker(config) // 기본값
            };
        }
    }
}