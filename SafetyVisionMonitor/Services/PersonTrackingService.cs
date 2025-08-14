using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using SafetyVisionMonitor.Shared.Models;
using SafetyVisionMonitor.Services.Tracking;
using Syncfusion.Data.Extensions;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 사람 추적 서비스 - 다양한 알고리즘 지원
    /// </summary>
    public class PersonTrackingService
    {
        private ITrackingAlgorithm _trackingAlgorithm;
        private readonly TrackingConfiguration _config;

        public PersonTrackingService(TrackingConfiguration config)
        {
            _config = config;
            _trackingAlgorithm = TrackingAlgorithmFactory.Create(config.TrackingMethod, config);
        }
        
        /// <summary>
        /// 트래킹 알고리즘 변경
        /// </summary>
        public void ChangeTrackingMethod(string method)
        {
            _trackingAlgorithm.Reset();
            _config.TrackingMethod = method;
            _trackingAlgorithm = TrackingAlgorithmFactory.Create(method, _config);
        }

        /// <summary>
        /// 검출 결과를 바탕으로 추적 업데이트
        /// </summary>
        public List<TrackedPerson> UpdateTracking(List<DetectionResult> detections, string cameraId)
        {
            return _trackingAlgorithm.UpdateTracking(detections, cameraId);
        }
        
        /// <summary>
        /// 트래킹 통계 조회
        /// </summary>
        public TrackingStatistics GetStatistics()
        {
            return _trackingAlgorithm.GetStatistics();
        }
        
        /// <summary>
        /// 트래킹 초기화
        /// </summary>
        public void Reset()
        {
            _trackingAlgorithm.Reset();
        }
    }

    /// <summary>
    /// 개별 사람 추적자
    /// </summary>
    public class PersonTracker
    {
        public int TrackingId { get; }
        public RectangleF CurrentBox { get; private set; }
        public RectangleF PredictedBox { get; private set; }
        public float Confidence { get; private set; }
        public bool IsActive { get; private set; }
        public int FramesSinceUpdate { get; private set; }
        public DateTime CreatedTime { get; }
        public int Age { get; private set; }

        private readonly Queue<PointF> _positionHistory;
        private readonly TrackingConfiguration _config;
        private PointF _velocity;

        public PersonTracker(int id, DetectionResult initialDetection, TrackingConfiguration config)
        {
            TrackingId = id;
            _config = config;
            CurrentBox = initialDetection.BoundingBox;
            PredictedBox = initialDetection.BoundingBox;
            Confidence = initialDetection.Confidence;
            IsActive = true;
            FramesSinceUpdate = 0;
            CreatedTime = DateTime.Now;
            Age = 0;
            _positionHistory = new Queue<PointF>();
            _velocity = new PointF(0, 0);
            
            // 초기 위치 기록
            _positionHistory.Enqueue(GetCenter(CurrentBox));
        }

        /// <summary>
        /// 새 검출 결과로 추적자 업데이트
        /// </summary>
        public void Update(DetectionResult detection)
        {
            var previousCenter = GetCenter(CurrentBox);
            CurrentBox = detection.BoundingBox;
            Confidence = detection.Confidence;
            FramesSinceUpdate = 0;
            Age++;

            // 속도 업데이트
            var currentCenter = GetCenter(CurrentBox);
            _velocity = new PointF(
                currentCenter.X - previousCenter.X,
                currentCenter.Y - previousCenter.Y
            );

            // 위치 히스토리 업데이트
            _positionHistory.Enqueue(currentCenter);
            
            // 히스토리 길이 제한
            while (_positionHistory.Count > _config.TrackHistoryLength)
                _positionHistory.Dequeue();

            // 다음 프레임 위치 예측
            PredictNext();
        }

        /// <summary>
        /// 검출 없이 예측만 수행
        /// </summary>
        public void Predict()
        {
            FramesSinceUpdate++;
            Age++;
            
            // 최대 사라짐 프레임 수 초과 시 비활성화
            if (FramesSinceUpdate > _config.MaxDisappearFrames)
            {
                IsActive = false;
                return;
            }

            // 칼만 필터 기반 예측 (간단한 선형 모델)
            var currentCenter = GetCenter(PredictedBox);
            var predictedCenter = new PointF(
                currentCenter.X + _velocity.X,
                currentCenter.Y + _velocity.Y
            );

            // 예측된 박스 생성
            PredictedBox = new RectangleF(
                predictedCenter.X - CurrentBox.Width / 2,
                predictedCenter.Y - CurrentBox.Height / 2,
                CurrentBox.Width,
                CurrentBox.Height
            );

            // 속도 감쇠 (friction)
            _velocity = new PointF(
                _velocity.X * 0.9f,
                _velocity.Y * 0.9f
            );
        }

        /// <summary>
        /// 다음 위치 예측
        /// </summary>
        private void PredictNext()
        {
            var currentCenter = GetCenter(CurrentBox);
            var predictedCenter = new PointF(
                currentCenter.X + _velocity.X,
                currentCenter.Y + _velocity.Y
            );

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
        public DateTime FirstDetectionTime { get; set; } = DateTime.Now;
        public List<PointF> TrackingHistory { get; set; } = new();
        public bool IsActive { get; set; }
        public int FramesSinceUpdate { get; set; }
        public string? Location { get; set; }
        
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
        public string TrackingMethod { get; set; } = "SORT";
        public bool AutoSaveTracking { get; set; } = true;
        public int AutoSaveInterval { get; set; } = 60;
    }
}