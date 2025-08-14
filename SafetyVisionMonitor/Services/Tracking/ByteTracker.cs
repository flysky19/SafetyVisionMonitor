using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionMonitor.Services.Tracking
{
    /// <summary>
    /// ByteTrack 알고리즘 구현
    /// 낮은 신뢰도 검출까지 활용하는 트래킹 방식
    /// </summary>
    public class ByteTracker : ITrackingAlgorithm
    {
        public string Name => "ByteTrack";
        
        private readonly List<PersonTracker> _activeTrackers;
        private readonly List<PersonTracker> _lostTrackers;
        private int _nextTrackerId = 1;
        private readonly TrackingConfiguration _config;
        private int _totalTrackersCreated = 0;
        
        private const float HIGH_THRESHOLD = 0.6f;
        private const float LOW_THRESHOLD = 0.1f;
        
        public ByteTracker(TrackingConfiguration config)
        {
            _activeTrackers = new List<PersonTracker>();
            _lostTrackers = new List<PersonTracker>();
            _config = config;
        }
        
        public List<TrackedPerson> UpdateTracking(List<DetectionResult> detections, string cameraId)
        {
            var personDetections = detections
                .Where(d => d.Label == "person")
                .ToList();
            var trackedPersons = new List<TrackedPerson>();

            // ByteTrack 특징: 검출을 신뢰도로 분류
            var highDetections = personDetections.Where(d => d.Confidence >= HIGH_THRESHOLD).ToList();
            var lowDetections = personDetections.Where(d => d.Confidence < HIGH_THRESHOLD && d.Confidence >= LOW_THRESHOLD).ToList();

            // 1단계: 높은 신뢰도 검출과 활성 추적자 매칭
            var firstMatches = AssignDetectionsToTrackers(highDetections, _activeTrackers);
            
            foreach (var (tracker, detection) in firstMatches)
            {
                tracker.Update(detection);
                trackedPersons.Add(CreateTrackedPerson(tracker, cameraId));
            }

            // 매칭되지 않은 높은 신뢰도 검출
            var unmatchedHighDetections = highDetections
                .Except(firstMatches.Select(p => p.Detection))
                .ToList();

            // 2단계: 매칭되지 않은 추적자와 낮은 신뢰도 검출 매칭
            var unmatchedTrackers = _activeTrackers
                .Except(firstMatches.Select(p => p.Tracker))
                .ToList();
                
            var secondMatches = AssignDetectionsToTrackers(lowDetections, unmatchedTrackers);
            
            foreach (var (tracker, detection) in secondMatches)
            {
                tracker.Update(detection);
                trackedPersons.Add(CreateTrackedPerson(tracker, cameraId));
            }

            // 3단계: 여전히 매칭되지 않은 추적자 처리
            var stillUnmatchedTrackers = unmatchedTrackers
                .Except(secondMatches.Select(p => p.Tracker))
                .ToList();
                
            foreach (var tracker in stillUnmatchedTrackers)
            {
                tracker.Predict();
                if (tracker.IsActive)
                {
                    trackedPersons.Add(CreateTrackedPerson(tracker, cameraId));
                }
                else
                {
                    _lostTrackers.Add(tracker);
                    _activeTrackers.Remove(tracker);
                }
            }

            // 4단계: 잃어버린 추적자 재활성화 시도
            var reactivatedTrackers = new List<PersonTracker>();
            foreach (var detection in unmatchedHighDetections)
            {
                var bestMatch = FindBestLostTracker(detection);
                if (bestMatch != null)
                {
                    bestMatch.Update(detection);
                    _lostTrackers.Remove(bestMatch);
                    _activeTrackers.Add(bestMatch);
                    reactivatedTrackers.Add(bestMatch);
                    trackedPersons.Add(CreateTrackedPerson(bestMatch, cameraId));
                }
            }

            // 5단계: 새 추적자 생성
            var finalUnmatchedDetections = unmatchedHighDetections
                .Where(d => !reactivatedTrackers.Any(t => t.CurrentBox == d.BoundingBox))
                .ToList();
                
            foreach (var detection in finalUnmatchedDetections)
            {
                var newTracker = new PersonTracker(_nextTrackerId++, detection, _config);
                _activeTrackers.Add(newTracker);
                _totalTrackersCreated++;
                trackedPersons.Add(CreateTrackedPerson(newTracker, cameraId));
            }

            // 오래된 잃어버린 추적자 제거
            _lostTrackers.RemoveAll(t => t.FramesSinceUpdate > _config.MaxDisappearFrames * 2);

            return trackedPersons;
        }
        
        private PersonTracker? FindBestLostTracker(DetectionResult detection)
        {
            PersonTracker? bestTracker = null;
            float bestIou = _config.IouThreshold * 0.5f; // 더 낮은 임계값
            
            foreach (var tracker in _lostTrackers)
            {
                var iou = CalculateIoU(tracker.PredictedBox, detection.BoundingBox);
                if (iou > bestIou)
                {
                    bestIou = iou;
                    bestTracker = tracker;
                }
            }
            
            return bestTracker;
        }
        
        private List<(PersonTracker Tracker, DetectionResult Detection)> AssignDetectionsToTrackers(
            List<DetectionResult> detections, List<PersonTracker> trackers)
        {
            var matches = new List<(PersonTracker, DetectionResult)>();
            
            if (trackers.Count == 0 || detections.Count == 0)
                return matches;

            // IOU 매트릭스 계산
            var iouMatrix = new float[trackers.Count, detections.Count];
            for (int i = 0; i < trackers.Count; i++)
            {
                for (int j = 0; j < detections.Count; j++)
                {
                    iouMatrix[i, j] = CalculateIoU(trackers[i].PredictedBox, detections[j].BoundingBox);
                }
            }

            // 탐욕적 매칭
            var usedTrackers = new HashSet<int>();
            var usedDetections = new HashSet<int>();

            while (true)
            {
                float maxIou = _config.IouThreshold;
                int bestTrackerIdx = -1;
                int bestDetectionIdx = -1;

                for (int i = 0; i < trackers.Count; i++)
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

                if (bestTrackerIdx == -1)
                    break;

                matches.Add((trackers[bestTrackerIdx], detections[bestDetectionIdx]));
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
                ActiveTrackerCount = _activeTrackers.Count + _lostTrackers.Count,
                TotalTrackersCreated = _totalTrackersCreated,
                AverageTrackDuration = _activeTrackers.Any() 
                    ? _activeTrackers.Average(t => t.Age) 
                    : 0
            };
        }
        
        public void Reset()
        {
            _activeTrackers.Clear();
            _lostTrackers.Clear();
            _nextTrackerId = 1;
            _totalTrackersCreated = 0;
        }
    }
}