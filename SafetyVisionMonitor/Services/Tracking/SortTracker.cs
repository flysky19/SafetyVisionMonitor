using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionMonitor.Services.Tracking
{
    /// <summary>
    /// SORT (Simple Online and Realtime Tracking) 알고리즘 구현
    /// </summary>
    public class SortTracker : ITrackingAlgorithm
    {
        public string Name => "SORT";
        
        private readonly List<PersonTracker> _activeTrackers;
        private int _nextTrackerId = 1;
        private readonly TrackingConfiguration _config;
        private int _totalTrackersCreated = 0;
        
        public SortTracker(TrackingConfiguration config)
        {
            _activeTrackers = new List<PersonTracker>();
            _config = config;
        }
        
        public List<TrackedPerson> UpdateTracking(List<DetectionResult> detections, string cameraId)
        {
            var personDetections = detections
                .Where(d => d.ClassName?.ToLower()?.Contains("person") == true)
                .ToList();
            var trackedPersons = new List<TrackedPerson>();

            // 1. IOU 기반 매칭
            var matchedPairs = AssignDetectionsToTrackers(personDetections);

            // 2. 매칭된 추적자 업데이트
            foreach (var (tracker, detection) in matchedPairs)
            {
                tracker.Update(detection);
                trackedPersons.Add(CreateTrackedPerson(tracker, cameraId));
            }

            // 3. 새로운 검출에 대한 추적자 생성
            var unmatchedDetections = personDetections
                .Except(matchedPairs.Select(p => p.Detection))
                .ToList();

            foreach (var detection in unmatchedDetections)
            {
                var newTracker = new PersonTracker(_nextTrackerId++, detection, _config);
                _activeTrackers.Add(newTracker);
                _totalTrackersCreated++;
                trackedPersons.Add(CreateTrackedPerson(newTracker, cameraId));
            }

            // 4. 업데이트되지 않은 추적자 처리
            var unmatchedTrackers = _activeTrackers
                .Except(matchedPairs.Select(p => p.Tracker))
                .ToList();

            foreach (var tracker in unmatchedTrackers)
            {
                tracker.Predict();
                if (tracker.IsActive)
                {
                    trackedPersons.Add(CreateTrackedPerson(tracker, cameraId));
                }
            }

            // 5. 비활성 추적자 제거
            _activeTrackers.RemoveAll(t => !t.IsActive);

            return trackedPersons;
        }
        
        private List<(PersonTracker Tracker, DetectionResult Detection)> AssignDetectionsToTrackers(
            List<DetectionResult> detections)
        {
            var matches = new List<(PersonTracker, DetectionResult)>();
            
            if (_activeTrackers.Count == 0 || detections.Count == 0)
                return matches;

            // IOU 매트릭스 계산
            var iouMatrix = new float[_activeTrackers.Count, detections.Count];
            for (int i = 0; i < _activeTrackers.Count; i++)
            {
                for (int j = 0; j < detections.Count; j++)
                {
                    iouMatrix[i, j] = CalculateIoU(
                        _activeTrackers[i].PredictedBox,
                        detections[j].BoundingBox
                    );
                }
            }

            // 탐욕적 매칭
            var usedTrackers = new HashSet<int>();
            var usedDetections = new HashSet<int>();

            while (true)
            {
                float maxIou = 0;
                int bestTrackerIdx = -1;
                int bestDetectionIdx = -1;

                for (int i = 0; i < _activeTrackers.Count; i++)
                {
                    if (usedTrackers.Contains(i)) continue;
                    
                    for (int j = 0; j < detections.Count; j++)
                    {
                        if (usedDetections.Contains(j)) continue;
                        
                        if (iouMatrix[i, j] > maxIou)
                        {
                            maxIou = iouMatrix[i, j];
                            bestTrackerIdx = i;
                            bestDetectionIdx = j;
                        }
                    }
                }

                if (maxIou < _config.IouThreshold || bestTrackerIdx == -1)
                    break;

                matches.Add((_activeTrackers[bestTrackerIdx], detections[bestDetectionIdx]));
                usedTrackers.Add(bestTrackerIdx);
                usedDetections.Add(bestDetectionIdx);
            }

            return matches;
        }
        
        private float CalculateIoU(RectangleF box1, RectangleF box2)
        {
            var intersectArea = RectangleF.Intersect(box1, box2);
            if (intersectArea.IsEmpty)
                return 0;

            float area1 = box1.Width * box1.Height;
            float area2 = box2.Width * box2.Height;
            float intersection = intersectArea.Width * intersectArea.Height;
            float union = area1 + area2 - intersection;

            return intersection / union;
        }
        
        private TrackedPerson CreateTrackedPerson(PersonTracker tracker, string cameraId)
        {
            return new TrackedPerson
            {
                TrackingId = tracker.TrackingId,
                BoundingBox = tracker.CurrentBox,
                Confidence = tracker.Confidence,
                CameraId = cameraId,
                Timestamp = DateTime.Now,
                FirstDetectionTime = tracker.CreatedTime,
                TrackingHistory = tracker.GetHistory().ToList(),
                IsActive = tracker.IsActive,
                FramesSinceUpdate = tracker.FramesSinceUpdate
            };
        }
        
        public TrackingStatistics GetStatistics()
        {
            return new TrackingStatistics
            {
                ActiveTrackerCount = _activeTrackers.Count,
                TotalTrackersCreated = _totalTrackersCreated,
                AverageTrackDuration = _activeTrackers.Any() 
                    ? _activeTrackers.Average(t => t.Age) 
                    : 0
            };
        }
        
        public void Reset()
        {
            _activeTrackers.Clear();
            _nextTrackerId = 1;
            _totalTrackersCreated = 0;
        }
    }
}