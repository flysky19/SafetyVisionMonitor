using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionMonitor.Services.Tracking
{
    /// <summary>
    /// StrongSORT 알고리즘 구현
    /// DeepSORT의 개선 버전으로 더 강력한 연관성 및 재식별 기능
    /// </summary>
    public class StrongSortTracker : ITrackingAlgorithm
    {
        public string Name => "StrongSORT";
        
        private readonly List<PersonTracker> _activeTrackers;
        private readonly Dictionary<int, FeatureBank> _featureBanks;
        private int _nextTrackerId = 1;
        private readonly TrackingConfiguration _config;
        private int _totalTrackersCreated = 0;
        
        public StrongSortTracker(TrackingConfiguration config)
        {
            _activeTrackers = new List<PersonTracker>();
            _featureBanks = new Dictionary<int, FeatureBank>();
            _config = config;
        }
        
        public List<TrackedPerson> UpdateTracking(List<DetectionResult> detections, string cameraId)
        {
            var personDetections = detections
                .Where(d => d.Label == "person")
                .ToList();
            var trackedPersons = new List<TrackedPerson>();

            // 1. 예측 단계 - ECC로 보정된 칼만 필터
            foreach (var tracker in _activeTrackers)
            {
                tracker.Predict();
            }

            // 2. 강화된 매칭 (IOU + 특징 + 모션)
            var matchedPairs = StrongAssociation(personDetections);

            // 3. 매칭된 추적자 업데이트
            foreach (var (tracker, detection) in matchedPairs)
            {
                tracker.Update(detection);
                UpdateFeatureBank(tracker.TrackingId, detection);
                trackedPersons.Add(CreateTrackedPerson(tracker, cameraId));
            }

            // 4. 글로벌 재식별 (Global Re-ID)
            var unmatchedDetections = personDetections
                .Except(matchedPairs.Select(p => p.Detection))
                .ToList();
                
            if (_config.EnableReIdentification)
            {
                var reidentified = PerformGlobalReID(unmatchedDetections);
                foreach (var (tracker, detection) in reidentified)
                {
                    tracker.Update(detection);
                    UpdateFeatureBank(tracker.TrackingId, detection);
                    trackedPersons.Add(CreateTrackedPerson(tracker, cameraId));
                    unmatchedDetections.Remove(detection);
                }
            }

            // 5. 새로운 추적자 생성
            foreach (var detection in unmatchedDetections)
            {
                var newTracker = new PersonTracker(_nextTrackerId++, detection, _config);
                _activeTrackers.Add(newTracker);
                _featureBanks[newTracker.TrackingId] = new FeatureBank();
                UpdateFeatureBank(newTracker.TrackingId, detection);
                _totalTrackersCreated++;
                trackedPersons.Add(CreateTrackedPerson(newTracker, cameraId));
            }

            // 6. 비활성 추적자 처리 (소프트 제거)
            var inactiveTrackers = _activeTrackers.Where(t => !t.IsActive).ToList();
            foreach (var tracker in inactiveTrackers)
            {
                if (tracker.FramesSinceUpdate > _config.MaxDisappearFrames * 3)
                {
                    _activeTrackers.Remove(tracker);
                    _featureBanks.Remove(tracker.TrackingId);
                }
            }

            return trackedPersons;
        }
        
        private List<(PersonTracker Tracker, DetectionResult Detection)> StrongAssociation(
            List<DetectionResult> detections)
        {
            var matches = new List<(PersonTracker, DetectionResult)>();
            
            if (_activeTrackers.Count == 0 || detections.Count == 0)
                return matches;

            // 다단계 매칭 전략
            // 1차: 높은 신뢰도 매칭 (IOU + 특징)
            var firstStageMatches = PerformMatching(detections, _activeTrackers, 0.7f);
            matches.AddRange(firstStageMatches);
            
            // 2차: 중간 신뢰도 매칭 (모션 + 특징)
            var remainingDetections = detections.Except(firstStageMatches.Select(m => m.Detection)).ToList();
            var remainingTrackers = _activeTrackers.Except(firstStageMatches.Select(m => m.Tracker)).ToList();
            
            if (remainingDetections.Any() && remainingTrackers.Any())
            {
                var secondStageMatches = PerformMotionMatching(remainingDetections, remainingTrackers, 0.5f);
                matches.AddRange(secondStageMatches);
            }

            return matches;
        }
        
        private List<(PersonTracker Tracker, DetectionResult Detection)> PerformMatching(
            List<DetectionResult> detections, List<PersonTracker> trackers, float threshold)
        {
            var matches = new List<(PersonTracker, DetectionResult)>();
            var costMatrix = new float[trackers.Count, detections.Count];
            
            // 코스트 매트릭스 계산
            for (int i = 0; i < trackers.Count; i++)
            {
                for (int j = 0; j < detections.Count; j++)
                {
                    var iou = CalculateIoU(trackers[i].PredictedBox, detections[j].BoundingBox);
                    var featureSim = CalculateStrongFeatureSimilarity(trackers[i].TrackingId, detections[j]);
                    var motionSim = CalculateMotionSimilarity(trackers[i], detections[j]);
                    
                    // 가중치 조합
                    costMatrix[i, j] = 0.2f * iou + 0.5f * featureSim + 0.3f * motionSim;
                }
            }

            // 최적 매칭 찾기
            return FindOptimalMatches(trackers, detections, costMatrix, threshold);
        }
        
        private List<(PersonTracker Tracker, DetectionResult Detection)> PerformMotionMatching(
            List<DetectionResult> detections, List<PersonTracker> trackers, float threshold)
        {
            var matches = new List<(PersonTracker, DetectionResult)>();
            var costMatrix = new float[trackers.Count, detections.Count];
            
            for (int i = 0; i < trackers.Count; i++)
            {
                for (int j = 0; j < detections.Count; j++)
                {
                    var motionSim = CalculateMotionSimilarity(trackers[i], detections[j]);
                    var featureSim = CalculateStrongFeatureSimilarity(trackers[i].TrackingId, detections[j]);
                    
                    costMatrix[i, j] = 0.6f * motionSim + 0.4f * featureSim;
                }
            }

            return FindOptimalMatches(trackers, detections, costMatrix, threshold);
        }
        
        private List<(PersonTracker Tracker, DetectionResult Detection)> FindOptimalMatches(
            List<PersonTracker> trackers, List<DetectionResult> detections, 
            float[,] costMatrix, float threshold)
        {
            var matches = new List<(PersonTracker, DetectionResult)>();
            var usedTrackers = new HashSet<int>();
            var usedDetections = new HashSet<int>();

            while (true)
            {
                float maxScore = threshold;
                int bestTrackerIdx = -1;
                int bestDetectionIdx = -1;

                for (int i = 0; i < trackers.Count; i++)
                {
                    if (usedTrackers.Contains(i)) continue;
                    
                    for (int j = 0; j < detections.Count; j++)
                    {
                        if (usedDetections.Contains(j)) continue;
                        
                        if (costMatrix[i, j] > maxScore)
                        {
                            maxScore = costMatrix[i, j];
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
        
        private List<(PersonTracker Tracker, DetectionResult Detection)> PerformGlobalReID(
            List<DetectionResult> unmatchedDetections)
        {
            var matches = new List<(PersonTracker, DetectionResult)>();
            var inactiveTrackers = _activeTrackers.Where(t => !t.IsActive).ToList();
            
            foreach (var detection in unmatchedDetections)
            {
                PersonTracker? bestMatch = null;
                float bestSimilarity = 0.8f; // 높은 임계값
                
                foreach (var tracker in inactiveTrackers)
                {
                    var similarity = CalculateStrongFeatureSimilarity(tracker.TrackingId, detection);
                    if (similarity > bestSimilarity)
                    {
                        bestSimilarity = similarity;
                        bestMatch = tracker;
                    }
                }
                
                if (bestMatch != null)
                {
                    matches.Add((bestMatch, detection));
                    inactiveTrackers.Remove(bestMatch);
                }
            }
            
            return matches;
        }
        
        private float CalculateStrongFeatureSimilarity(int trackerId, DetectionResult detection)
        {
            if (!_featureBanks.ContainsKey(trackerId))
                return 0;
                
            var bank = _featureBanks[trackerId];
            
            // 시뮬레이션: 실제로는 딥러닝 특징 비교
            var sizeSim = 1.0f - Math.Abs(bank.AverageSize - detection.BoundingBox.Width) / 
                                Math.Max(bank.AverageSize, detection.BoundingBox.Width);
            var aspectSim = 1.0f - Math.Abs(bank.AverageAspectRatio - 
                                  (detection.BoundingBox.Height / detection.BoundingBox.Width)) / 2.0f;
            var confidenceSim = detection.Confidence;
            
            return (sizeSim + aspectSim + confidenceSim) / 3.0f;
        }
        
        private float CalculateMotionSimilarity(PersonTracker tracker, DetectionResult detection)
        {
            // 예측된 위치와 실제 검출 위치의 차이 계산
            var predictedCenter = new PointF(
                tracker.PredictedBox.X + tracker.PredictedBox.Width / 2,
                tracker.PredictedBox.Y + tracker.PredictedBox.Height / 2
            );
            var detectionCenter = new PointF(
                detection.BoundingBox.X + detection.BoundingBox.Width / 2,
                detection.BoundingBox.Y + detection.BoundingBox.Height / 2
            );
            
            var distance = Math.Sqrt(
                Math.Pow(predictedCenter.X - detectionCenter.X, 2) + 
                Math.Pow(predictedCenter.Y - detectionCenter.Y, 2)
            );
            
            // 거리를 유사도로 변환
            return Math.Max(0, 1.0f - (float)(distance / _config.MaxTrackingDistance));
        }
        
        private void UpdateFeatureBank(int trackerId, DetectionResult detection)
        {
            if (!_featureBanks.ContainsKey(trackerId))
                _featureBanks[trackerId] = new FeatureBank();
                
            var bank = _featureBanks[trackerId];
            bank.Update(detection.BoundingBox.Width, 
                       detection.BoundingBox.Height / detection.BoundingBox.Width);
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
            _featureBanks.Clear();
            _nextTrackerId = 1;
            _totalTrackersCreated = 0;
        }
        
        private class FeatureBank
        {
            private readonly Queue<float> _sizes = new();
            private readonly Queue<float> _aspectRatios = new();
            private const int MaxFeatures = 30;
            
            public float AverageSize => _sizes.Any() ? _sizes.Average() : 0;
            public float AverageAspectRatio => _aspectRatios.Any() ? _aspectRatios.Average() : 1;
            
            public void Update(float size, float aspectRatio)
            {
                _sizes.Enqueue(size);
                _aspectRatios.Enqueue(aspectRatio);
                
                while (_sizes.Count > MaxFeatures)
                    _sizes.Dequeue();
                while (_aspectRatios.Count > MaxFeatures)
                    _aspectRatios.Dequeue();
            }
        }
    }
}