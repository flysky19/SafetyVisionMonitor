using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionMonitor.Services.Tracking
{
    /// <summary>
    /// DeepSORT 알고리즘 구현 (특징 추출 시뮬레이션)
    /// 실제 구현에서는 딥러닝 모델을 사용한 특징 추출이 필요
    /// </summary>
    public class DeepSortTracker : ITrackingAlgorithm
    {
        public string Name => "DeepSORT";
        
        private readonly List<PersonTracker> _activeTrackers;
        private int _nextTrackerId = 1;
        private readonly TrackingConfiguration _config;
        private int _totalTrackersCreated = 0;
        
        public DeepSortTracker(TrackingConfiguration config)
        {
            _activeTrackers = new List<PersonTracker>();
            _config = config;
        }
        
        public List<TrackedPerson> UpdateTracking(List<DetectionResult> detections, string cameraId)
        {
            var personDetections = detections
                .Where(d => d.Label == "person")
                .ToList();
            var trackedPersons = new List<TrackedPerson>();

            // 1. 예측 단계 - 칼만 필터로 위치 예측
            foreach (var tracker in _activeTrackers)
            {
                tracker.Predict();
            }

            // 2. 딥 특징 기반 매칭 (시뮬레이션)
            var matchedPairs = AssignDetectionsWithFeatures(personDetections);

            // 3. 매칭된 추적자 업데이트
            foreach (var (tracker, detection) in matchedPairs)
            {
                tracker.Update(detection);
                trackedPersons.Add(CreateTrackedPerson(tracker, cameraId));
            }

            // 4. 새로운 검출에 대한 추적자 생성
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

            // 5. 재식별 시도 (Re-ID)
            if (_config.EnableReIdentification)
            {
                PerformReIdentification(unmatchedDetections);
            }

            // 6. 비활성 추적자 제거
            _activeTrackers.RemoveAll(t => !t.IsActive);

            return trackedPersons;
        }
        
        private List<(PersonTracker Tracker, DetectionResult Detection)> AssignDetectionsWithFeatures(
            List<DetectionResult> detections)
        {
            var matches = new List<(PersonTracker, DetectionResult)>();
            
            if (_activeTrackers.Count == 0 || detections.Count == 0)
                return matches;

            // 코스트 매트릭스 계산 (IOU + 특징 유사도)
            var costMatrix = new float[_activeTrackers.Count, detections.Count];
            for (int i = 0; i < _activeTrackers.Count; i++)
            {
                for (int j = 0; j < detections.Count; j++)
                {
                    var iou = CalculateIoU(_activeTrackers[i].PredictedBox, detections[j].BoundingBox);
                    var featureSimilarity = CalculateFeatureSimilarity(_activeTrackers[i], detections[j]);
                    
                    // 가중치 조합 (DeepSORT는 특징을 더 중요시)
                    costMatrix[i, j] = 0.3f * iou + 0.7f * featureSimilarity;
                }
            }

            // 헝가리안 알고리즘 대신 탐욕적 매칭 사용
            var usedTrackers = new HashSet<int>();
            var usedDetections = new HashSet<int>();

            while (true)
            {
                float maxScore = _config.SimilarityThreshold;
                int bestTrackerIdx = -1;
                int bestDetectionIdx = -1;

                for (int i = 0; i < _activeTrackers.Count; i++)
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

                matches.Add((_activeTrackers[bestTrackerIdx], detections[bestDetectionIdx]));
                usedTrackers.Add(bestTrackerIdx);
                usedDetections.Add(bestDetectionIdx);
            }

            return matches;
        }
        
        private float CalculateFeatureSimilarity(PersonTracker tracker, DetectionResult detection)
        {
            // 실제 DeepSORT에서는 딥러닝 모델로 추출한 특징 벡터 비교
            // 여기서는 간단한 시뮬레이션
            var sizeSimilarity = 1.0f - Math.Abs(tracker.CurrentBox.Width - detection.BoundingBox.Width) / 
                                       Math.Max(tracker.CurrentBox.Width, detection.BoundingBox.Width);
            var aspectRatioSim = 1.0f - Math.Abs(
                (tracker.CurrentBox.Height / tracker.CurrentBox.Width) - 
                (detection.BoundingBox.Height / detection.BoundingBox.Width)
            ) / 2.0f;
            
            return (sizeSimilarity + aspectRatioSim) / 2.0f;
        }
        
        private void PerformReIdentification(List<DetectionResult> unmatchedDetections)
        {
            // 최근 사라진 추적자와 새 검출 매칭 시도
            // 실제 구현에서는 특징 데이터베이스를 유지하고 비교
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