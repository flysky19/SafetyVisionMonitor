using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using SafetyVisionMonitor.Models;
using Syncfusion.Data.Extensions;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 사람 추적 서비스 - SORT 기반 다중 객체 추적
    /// </summary>
    public class PersonTrackingService
    {
        private readonly List<PersonTracker> _activeTrackers;
        private int _nextTrackerId = 1;
        private readonly TrackingConfiguration _config;

        public PersonTrackingService(TrackingConfiguration config)
        {
            _activeTrackers = new List<PersonTracker>();
            _config = config;
        }

        /// <summary>
        /// 검출 결과를 바탕으로 추적 업데이트
        /// </summary>
        public List<TrackedPerson> UpdateTracking(List<DetectionResult> detections, string cameraId)
        {
            var personDetections = detections.Where(d => d.ClassName == "person").ToList();
            var trackedPersons = new List<TrackedPerson>();

            // 1. 기존 추적자와 새 검출 매칭
            var matchedPairs = AssignDetectionsToTrackers(personDetections);

            // 2. 매칭된 추적자 업데이트
            foreach (var (tracker, detection) in matchedPairs)
            {
                tracker.Update(detection);
                trackedPersons.Add(CreateTrackedPerson(tracker, cameraId));
            }

            // 3. 매칭되지 않은 검출에 대해 새 추적자 생성
            var unmatchedDetections = personDetections
                .Except(matchedPairs.Select(p => p.Detection))
                .ToList();

            foreach (var detection in unmatchedDetections)
            {
                var newTracker = new PersonTracker(_nextTrackerId++, detection, _config);
                _activeTrackers.Add(newTracker);
                trackedPersons.Add(CreateTrackedPerson(newTracker, cameraId));
            }

            // 4. 업데이트되지 않은 추적자 처리 (예측만)
            var unmatchedTrackers = _activeTrackers
                .Except(matchedPairs.Select(p => p.Tracker))
                .ToList();

            foreach (var tracker in unmatchedTrackers)
            {
                tracker.PredictOnly();
                if (tracker.IsActive)
                {
                    trackedPersons.Add(CreateTrackedPerson(tracker, cameraId));
                }
            }

            // 5. 비활성 추적자 제거
            _activeTrackers.RemoveAll(t => !t.IsActive);

            return trackedPersons;
        }

        /// <summary>
        /// 검출 결과를 기존 추적자와 매칭
        /// </summary>
        private List<(PersonTracker Tracker, DetectionResult Detection)> AssignDetectionsToTrackers(
            List<DetectionResult> detections)
        {
            var assignments = new List<(PersonTracker, DetectionResult)>();
            var usedDetections = new HashSet<DetectionResult>();
            var usedTrackers = new HashSet<PersonTracker>();

            // IOU 기반 매칭
            foreach (var tracker in _activeTrackers.OrderBy(t => t.Id))
            {
                if (usedTrackers.Contains(tracker)) continue;

                var bestDetection = detections
                    .Where(d => !usedDetections.Contains(d))
                    .Select(d => new { Detection = d, IOU = CalculateIOU(tracker.PredictedBox, d.BoundingBox) })
                    .Where(x => x.IOU > _config.IouThreshold)
                    .OrderByDescending(x => x.IOU)
                    .FirstOrDefault();

                if (bestDetection != null)
                {
                    assignments.Add((tracker, bestDetection.Detection));
                    usedTrackers.Add(tracker);
                    usedDetections.Add(bestDetection.Detection);
                }
            }

            return assignments;
        }

        /// <summary>
        /// IOU (Intersection over Union) 계산
        /// </summary>
        private static float CalculateIOU(RectangleF box1, RectangleF box2)
        {
            var intersection = RectangleF.Intersect(box1, box2);
            if (intersection.IsEmpty) return 0f;

            var intersectionArea = intersection.Width * intersection.Height;
            var unionArea = box1.Width * box1.Height + box2.Width * box2.Height - intersectionArea;

            return unionArea > 0 ? intersectionArea / unionArea : 0f;
        }

        /// <summary>
        /// 추적자로부터 TrackedPerson 객체 생성
        /// </summary>
        private static TrackedPerson CreateTrackedPerson(PersonTracker tracker, string cameraId)
        {
            return new TrackedPerson
            {
                TrackingId = tracker.Id,
                BoundingBox = tracker.CurrentBox,
                Confidence = tracker.LastConfidence,
                CameraId = cameraId,
                Timestamp = DateTime.Now,
                TrackingHistory = tracker.GetHistory().ToList(),
                IsActive = tracker.IsActive,
                FramesSinceUpdate = tracker.FramesSinceUpdate
            };
        }

        /// <summary>
        /// 모든 활성 추적자 정보 조회
        /// </summary>
        public List<TrackedPerson> GetActiveTrackers(string cameraId)
        {
            return _activeTrackers
                .Where(t => t.IsActive)
                .Select(t => CreateTrackedPerson(t, cameraId))
                .ToList();
        }

        /// <summary>
        /// 추적 통계 정보
        /// </summary>
        public TrackingStatistics GetStatistics()
        {
            return new TrackingStatistics
            {
                ActiveTrackerCount = _activeTrackers.Count(t => t.IsActive),
                TotalTrackersCreated = _nextTrackerId - 1,
                AverageTrackDuration = _activeTrackers.Where(t => t.IsActive)
                    .Select(t => t.TrackDuration.TotalSeconds)
                    .DefaultIfEmpty(0)
                    .Average()
            };
        }

        /// <summary>
        /// 추적 초기화
        /// </summary>
        public void Reset()
        {
            _activeTrackers.Clear();
            _nextTrackerId = 1;
        }
    }

    /// <summary>
    /// 개별 사람 추적자
    /// </summary>
    public class PersonTracker
    {
        public int Id { get; }
        public RectangleF CurrentBox { get; private set; }
        public RectangleF PredictedBox { get; private set; }
        public float LastConfidence { get; private set; }
        public bool IsActive { get; private set; }
        public int FramesSinceUpdate { get; private set; }
        public DateTime CreatedTime { get; }
        public TimeSpan TrackDuration => DateTime.Now - CreatedTime;

        private readonly Queue<PointF> _positionHistory;
        private readonly TrackingConfiguration _config;

        public PersonTracker(int id, DetectionResult initialDetection, TrackingConfiguration config)
        {
            Id = id;
            _config = config;
            CurrentBox = initialDetection.BoundingBox;
            PredictedBox = initialDetection.BoundingBox;
            LastConfidence = initialDetection.Confidence;
            IsActive = true;
            FramesSinceUpdate = 0;
            CreatedTime = DateTime.Now;
            _positionHistory = new Queue<PointF>();
            
            // 초기 위치 기록
            _positionHistory.Enqueue(GetCenter(CurrentBox));
        }

        /// <summary>
        /// 새 검출 결과로 추적자 업데이트
        /// </summary>
        public void Update(DetectionResult detection)
        {
            CurrentBox = detection.BoundingBox;
            LastConfidence = detection.Confidence;
            FramesSinceUpdate = 0;

            // 위치 히스토리 업데이트
            var center = GetCenter(CurrentBox);
            _positionHistory.Enqueue(center);
            
            // 히스토리 길이 제한
            while (_positionHistory.Count > _config.TrackHistoryLength)
                _positionHistory.Dequeue();

            // 다음 프레임 위치 예측 (단순 선형 예측)
            PredictNext();
        }

        /// <summary>
        /// 검출 없이 예측만 수행
        /// </summary>
        public void PredictOnly()
        {
            FramesSinceUpdate++;
            
            // 최대 사라짐 프레임 수 초과 시 비활성화
            if (FramesSinceUpdate > _config.MaxDisappearFrames)
            {
                IsActive = false;
                return;
            }

            // 현재 박스를 예측된 위치로 업데이트
            CurrentBox = PredictedBox;
            PredictNext();
        }

        /// <summary>
        /// 다음 프레임 위치 예측
        /// </summary>
        private void PredictNext()
        {
            if (_positionHistory.Count < 2)
            {
                PredictedBox = CurrentBox;
                return;
            }

            var positions = _positionHistory.ToArray();
            var lastPos = positions[^1];
            var prevPos = positions[^2];

            // 속도 벡터 계산
            var velocityX = lastPos.X - prevPos.X;
            var velocityY = lastPos.Y - prevPos.Y;

            // 예측된 중심점
            var predictedCenter = new PointF(lastPos.X + velocityX, lastPos.Y + velocityY);

            // 예측된 바운딩 박스
            PredictedBox = new RectangleF(
                predictedCenter.X - CurrentBox.Width / 2,
                predictedCenter.Y - CurrentBox.Height / 2,
                CurrentBox.Width,
                CurrentBox.Height
            );
        }

        /// <summary>
        /// 바운딩 박스의 중심점 계산
        /// </summary>
        private static PointF GetCenter(RectangleF box)
        {
            return new PointF(box.X + box.Width / 2, box.Y + box.Height / 2);
        }

        /// <summary>
        /// 위치 히스토리 조회
        /// </summary>
        public IEnumerable<PointF> GetHistory()
        {
            return _positionHistory.ToList();
        }
    }

    /// <summary>
    /// 추적된 사람 정보
    /// </summary>
    public class TrackedPerson
    {
        public int TrackingId { get; set; }
        public RectangleF BoundingBox { get; set; }
        public float Confidence { get; set; }
        public string CameraId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public List<PointF> TrackingHistory { get; set; } = new();
        public bool IsActive { get; set; }
        public int FramesSinceUpdate { get; set; }
        
        public PointF Center => new(
            BoundingBox.X + BoundingBox.Width / 2,
            BoundingBox.Y + BoundingBox.Height / 2
        );
    }

    /// <summary>
    /// 추적 통계 정보
    /// </summary>
    public class TrackingStatistics
    {
        public int ActiveTrackerCount { get; set; }
        public int TotalTrackersCreated { get; set; }
        public double AverageTrackDuration { get; set; }
    }

    /// <summary>
    /// 추적 설정
    /// </summary>
    public class TrackingConfiguration
    {
        public bool IsEnabled { get; set; } = true;
        public int MaxTrackingDistance { get; set; } = 50;
        public int MaxDisappearFrames { get; set; } = 30;
        public float IouThreshold { get; set; } = 0.3f;
        public float SimilarityThreshold { get; set; } = 0.7f;
        public bool EnableReIdentification { get; set; } = true;
        public bool EnableMultiCameraTracking { get; set; } = true;
        public int TrackHistoryLength { get; set; } = 50;
        public bool ShowTrackingId { get; set; } = true;
        public bool ShowTrackingPath { get; set; } = true;
        public int PathDisplayLength { get; set; } = 20;
        public bool AutoSaveTracking { get; set; } = true;
        public int AutoSaveInterval { get; set; } = 60;
        public string TrackingMethod { get; set; } = "SORT";
    }
}