using System;
using System.Collections.Generic;
using OpenCvSharp;
using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionMonitor.Services.Features
{
    /// <summary>
    /// 기능 인터페이스 - 모든 기능이 구현해야 하는 공통 인터페이스
    /// </summary>
    public interface IFeature : IDisposable
    {
        /// <summary>
        /// 기능 고유 ID
        /// </summary>
        string Id { get; }

        /// <summary>
        /// 기능 이름
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 기능 설명
        /// </summary>
        string Description { get; }

        /// <summary>
        /// 렌더링 우선순위 (낮을수록 먼저 실행)
        /// </summary>
        int RenderPriority { get; }

        /// <summary>
        /// 기본 설정
        /// </summary>
        FeatureConfiguration DefaultConfiguration { get; }

        /// <summary>
        /// 현재 활성화 상태
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// 기능 설정 적용
        /// </summary>
        void Configure(FeatureConfiguration configuration);

        /// <summary>
        /// 프레임에 기능 적용
        /// </summary>
        Mat ProcessFrame(Mat frame, FrameProcessingContext context);

        /// <summary>
        /// 기능이 해당 컨텍스트에서 실행되어야 하는지 확인
        /// </summary>
        bool ShouldProcess(FrameProcessingContext context);

        /// <summary>
        /// 기능 상태 가져오기
        /// </summary>
        FeatureStatus GetStatus();
    }

    /// <summary>
    /// 기본 기능 클래스
    /// </summary>
    public abstract class BaseFeature : IFeature
    {
        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Description { get; }
        public virtual int RenderPriority => 100;
        public abstract FeatureConfiguration DefaultConfiguration { get; }
        
        public bool IsEnabled { get; protected set; }
        protected FeatureConfiguration? CurrentConfiguration { get; private set; }

        public virtual void Configure(FeatureConfiguration configuration)
        {
            CurrentConfiguration = configuration;
            IsEnabled = configuration.IsEnabled;
            OnConfigurationChanged(configuration);
        }

        public abstract Mat ProcessFrame(Mat frame, FrameProcessingContext context);

        public virtual bool ShouldProcess(FrameProcessingContext context)
        {
            return IsEnabled;
        }

        public virtual FeatureStatus GetStatus()
        {
            return new FeatureStatus
            {
                Id = Id,
                Name = Name,
                IsEnabled = IsEnabled,
                LastProcessedTime = DateTime.Now,
                ProcessingTimeMs = 0,
                ErrorCount = 0
            };
        }

        /// <summary>
        /// 설정 변경 시 호출되는 메서드 (하위 클래스에서 오버라이드)
        /// </summary>
        protected virtual void OnConfigurationChanged(FeatureConfiguration configuration)
        {
            // 하위 클래스에서 필요에 따라 구현
        }

        public virtual void Dispose()
        {
            // 하위 클래스에서 필요에 따라 구현
        }
    }

    /// <summary>
    /// 기능 설정
    /// </summary>
    public class FeatureConfiguration
    {
        /// <summary>
        /// 기능 활성화 여부
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 기능별 속성
        /// </summary>
        public Dictionary<string, object> Properties { get; set; } = new();

        /// <summary>
        /// 마지막 업데이트 시간
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.Now;

        /// <summary>
        /// 속성 값 가져오기
        /// </summary>
        public T GetProperty<T>(string key, T defaultValue = default!)
        {
            if (Properties.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            return defaultValue;
        }

        /// <summary>
        /// 속성 값 설정
        /// </summary>
        public void SetProperty<T>(string key, T value)
        {
            if (value != null)
            {
                Properties[key] = value;
                LastUpdated = DateTime.Now;
            }
        }
    }

    /// <summary>
    /// 프레임 처리 컨텍스트
    /// </summary>
    public class FrameProcessingContext
    {
        /// <summary>
        /// 카메라 ID
        /// </summary>
        public string CameraId { get; set; } = string.Empty;

        /// <summary>
        /// 검출 결과
        /// </summary>
        public DetectionResult[] Detections { get; set; } = Array.Empty<DetectionResult>();

        /// <summary>
        /// 추적된 사람들
        /// </summary>
        public List<TrackedPerson>? TrackedPersons { get; set; }

        /// <summary>
        /// 추적 설정
        /// </summary>
        public TrackingConfiguration? TrackingConfig { get; set; }

        /// <summary>
        /// 프레임 해상도 스케일 (UI용 축소 프레임의 경우)
        /// </summary>
        public float Scale { get; set; } = 1.0f;

        /// <summary>
        /// 처리 시작 시간
        /// </summary>
        public DateTime ProcessingStartTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 추가 데이터
        /// </summary>
        public Dictionary<string, object> AdditionalData { get; set; } = new();

        /// <summary>
        /// 추가 데이터 가져오기
        /// </summary>
        public T GetData<T>(string key, T defaultValue = default!)
        {
            if (AdditionalData.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            return defaultValue;
        }

        /// <summary>
        /// 추가 데이터 설정
        /// </summary>
        public void SetData<T>(string key, T value)
        {
            if (value != null)
            {
                AdditionalData[key] = value;
            }
        }
    }

    /// <summary>
    /// 기능 상태 정보
    /// </summary>
    public class FeatureStatus
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public DateTime LastProcessedTime { get; set; }
        public double ProcessingTimeMs { get; set; }
        public int ErrorCount { get; set; }
        public string? LastError { get; set; }
        public Dictionary<string, object> Metrics { get; set; } = new();
    }
}