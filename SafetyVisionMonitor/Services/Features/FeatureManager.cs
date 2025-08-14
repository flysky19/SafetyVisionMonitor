using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SafetyVisionMonitor.Models;

namespace SafetyVisionMonitor.Services.Features
{
    /// <summary>
    /// 중앙집중식 기능 관리 시스템
    /// 모든 기능의 활성화/비활성화를 관리하고 실시간으로 적용
    /// </summary>
    public class FeatureManager : IDisposable
    {
        private static FeatureManager? _instance;
        public static FeatureManager Instance => _instance ??= new FeatureManager();

        private readonly ConcurrentDictionary<string, IFeature> _features = new();
        private readonly ConcurrentDictionary<string, FeatureConfiguration> _configurations = new();
        private bool _disposed = false;

        // 이벤트
        public event EventHandler<FeatureStateChangedEventArgs>? FeatureStateChanged;
        public event EventHandler<FeatureConfigurationChangedEventArgs>? ConfigurationChanged;

        private FeatureManager()
        {
            InitializeFeatures();
        }

        /// <summary>
        /// 기본 기능들 초기화
        /// </summary>
        private void InitializeFeatures()
        {
            // 개인정보 보호 기능
            RegisterFeature(new PrivacyProtectionFeature());
            
            // 객체 검출 표시 기능
            RegisterFeature(new ObjectDetectionOverlayFeature());
            
            // 추적 표시 기능
            RegisterFeature(new TrackingOverlayFeature());
            
            // 구역 표시 기능
            RegisterFeature(new ZoneOverlayFeature());
            
            // 통계 표시 기능
            RegisterFeature(new StatisticsOverlayFeature());

            System.Diagnostics.Debug.WriteLine($"FeatureManager: Initialized with {_features.Count} features");
        }

        /// <summary>
        /// 기능 등록
        /// </summary>
        public void RegisterFeature(IFeature feature)
        {
            if (feature == null) throw new ArgumentNullException(nameof(feature));

            _features[feature.Id] = feature;
            
            // 기본 설정 로드
            var config = LoadFeatureConfiguration(feature.Id) ?? feature.DefaultConfiguration;
            _configurations[feature.Id] = config;
            
            // 초기 상태 적용
            feature.Configure(config);
            
            System.Diagnostics.Debug.WriteLine($"FeatureManager: Registered feature '{feature.Name}' (ID: {feature.Id})");
        }

        /// <summary>
        /// 기능 활성화/비활성화
        /// </summary>
        public void SetFeatureEnabled(string featureId, bool enabled)
        {
            if (_features.TryGetValue(featureId, out var feature) &&
                _configurations.TryGetValue(featureId, out var config))
            {
                var oldEnabled = config.IsEnabled;
                config.IsEnabled = enabled;
                
                // 기능에 새 설정 적용
                feature.Configure(config);
                
                // 설정 저장
                SaveFeatureConfiguration(featureId, config);
                
                // 이벤트 발생
                FeatureStateChanged?.Invoke(this, new FeatureStateChangedEventArgs(
                    featureId, feature.Name, oldEnabled, enabled));

                System.Diagnostics.Debug.WriteLine($"FeatureManager: Feature '{feature.Name}' {(enabled ? "enabled" : "disabled")}");
            }
        }

        /// <summary>
        /// 기능 설정 업데이트
        /// </summary>
        public void UpdateFeatureConfiguration(string featureId, FeatureConfiguration configuration)
        {
            if (_features.TryGetValue(featureId, out var feature))
            {
                var oldConfig = _configurations.GetValueOrDefault(featureId);
                _configurations[featureId] = configuration;
                
                // 기능에 새 설정 적용
                feature.Configure(configuration);
                
                // 설정 저장
                SaveFeatureConfiguration(featureId, configuration);
                
                // 이벤트 발생
                ConfigurationChanged?.Invoke(this, new FeatureConfigurationChangedEventArgs(
                    featureId, feature.Name, oldConfig, configuration));

                System.Diagnostics.Debug.WriteLine($"FeatureManager: Updated configuration for '{feature.Name}'");
            }
        }

        /// <summary>
        /// 기능 상태 조회
        /// </summary>
        public bool IsFeatureEnabled(string featureId)
        {
            return _configurations.TryGetValue(featureId, out var config) && config.IsEnabled;
        }

        /// <summary>
        /// 기능 설정 조회
        /// </summary>
        public FeatureConfiguration? GetFeatureConfiguration(string featureId)
        {
            return _configurations.GetValueOrDefault(featureId);
        }

        /// <summary>
        /// 모든 활성 기능 목록 조회
        /// </summary>
        public List<IFeature> GetActiveFeatures()
        {
            return _features.Values
                .Where(f => _configurations.TryGetValue(f.Id, out var config) && config.IsEnabled)
                .ToList();
        }

        /// <summary>
        /// 모든 기능 목록 조회
        /// </summary>
        public List<IFeature> GetAllFeatures()
        {
            return _features.Values.ToList();
        }

        /// <summary>
        /// 기능별 우선순위 정렬된 활성 기능 목록
        /// </summary>
        public List<IFeature> GetActiveFeaturesByPriority()
        {
            return GetActiveFeatures()
                .OrderBy(f => f.RenderPriority)
                .ToList();
        }

        /// <summary>
        /// 기능 설정 로드 (데이터베이스 또는 파일에서)
        /// </summary>
        private FeatureConfiguration? LoadFeatureConfiguration(string featureId)
        {
            try
            {
                // TODO: 실제로는 데이터베이스나 설정 파일에서 로드
                // 현재는 SafetySettingsManager에서 가져오기
                var settings = SafetySettingsManager.Instance.CurrentSettings;
                
                return featureId switch
                {
                    "privacy_protection" => new FeatureConfiguration
                    {
                        IsEnabled = settings.IsFaceBlurEnabled || settings.IsFullBodyBlurEnabled,
                        Properties = new Dictionary<string, object>
                        {
                            ["faceBlurEnabled"] = settings.IsFaceBlurEnabled,
                            ["bodyBlurEnabled"] = settings.IsFullBodyBlurEnabled
                        }
                    },
                    "object_detection" => new FeatureConfiguration
                    {
                        IsEnabled = true, // 기본적으로 활성화
                        Properties = new Dictionary<string, object>
                        {
                            ["showConfidence"] = true,
                            ["showBoundingBox"] = true
                        }
                    },
                    "tracking_overlay" => new FeatureConfiguration
                    {
                        IsEnabled = true,
                        Properties = new Dictionary<string, object>
                        {
                            ["showTrackingId"] = true,
                            ["showTrackingPath"] = true
                        }
                    },
                    _ => null
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FeatureManager: Failed to load configuration for {featureId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 기능 설정 저장
        /// </summary>
        private void SaveFeatureConfiguration(string featureId, FeatureConfiguration configuration)
        {
            try
            {
                // TODO: 실제로는 데이터베이스나 설정 파일에 저장
                // 현재는 SafetySettingsManager와 동기화
                if (featureId == "privacy_protection")
                {
                    var settings = SafetySettingsManager.Instance.CurrentSettings;
                    if (configuration.Properties.TryGetValue("faceBlurEnabled", out var faceBlur))
                    {
                        settings.IsFaceBlurEnabled = (bool)faceBlur;
                    }
                    if (configuration.Properties.TryGetValue("bodyBlurEnabled", out var bodyBlur))
                    {
                        settings.IsFullBodyBlurEnabled = (bool)bodyBlur;
                    }
                    
                    // 비동기 메서드를 호출하되 결과를 기다리지 않음 (fire-and-forget)
                    _ = SafetySettingsManager.Instance.UpdateSettingsAsync(settings);
                }
                
                System.Diagnostics.Debug.WriteLine($"FeatureManager: Saved configuration for {featureId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FeatureManager: Failed to save configuration for {featureId}: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var feature in _features.Values)
            {
                try
                {
                    feature.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"FeatureManager: Error disposing feature {feature.Id}: {ex.Message}");
                }
            }
            
            _features.Clear();
            _configurations.Clear();
            
            System.Diagnostics.Debug.WriteLine("FeatureManager: Disposed");
        }
    }

    /// <summary>
    /// 기능 상태 변경 이벤트 인자
    /// </summary>
    public class FeatureStateChangedEventArgs : EventArgs
    {
        public string FeatureId { get; }
        public string FeatureName { get; }
        public bool OldEnabled { get; }
        public bool NewEnabled { get; }

        public FeatureStateChangedEventArgs(string featureId, string featureName, bool oldEnabled, bool newEnabled)
        {
            FeatureId = featureId;
            FeatureName = featureName;
            OldEnabled = oldEnabled;
            NewEnabled = newEnabled;
        }
    }

    /// <summary>
    /// 기능 설정 변경 이벤트 인자
    /// </summary>
    public class FeatureConfigurationChangedEventArgs : EventArgs
    {
        public string FeatureId { get; }
        public string FeatureName { get; }
        public FeatureConfiguration? OldConfiguration { get; }
        public FeatureConfiguration NewConfiguration { get; }

        public FeatureConfigurationChangedEventArgs(string featureId, string featureName, 
            FeatureConfiguration? oldConfig, FeatureConfiguration newConfig)
        {
            FeatureId = featureId;
            FeatureName = featureName;
            OldConfiguration = oldConfig;
            NewConfiguration = newConfig;
        }
    }
}