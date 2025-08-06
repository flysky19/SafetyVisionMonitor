using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SafetyVisionMonitor.Models;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 백그라운드 추적 서비스 - UI와 독립적으로 사람 추적 수행
    /// </summary>
    public class BackgroundTrackingService : IDisposable
    {
        private readonly ConcurrentDictionary<string, PersonTrackingService> _trackingServices = new();
        private readonly ConcurrentDictionary<string, List<TrackedPerson>> _latestTrackedPersons = new();
        private TrackingConfiguration? _globalTrackingConfig;
        private bool _disposed = false;
        
        // 이벤트
        public event EventHandler<TrackingUpdateEventArgs>? TrackingUpdated;
        public event EventHandler<TrackingStatisticsEventArgs>? StatisticsUpdated;
        
        // 통계
        public int TotalActiveTrackers => _trackingServices.Values.Sum(ts => ts.GetStatistics().ActiveTrackerCount);
        public int TotalTrackersCreated => _trackingServices.Values.Sum(ts => ts.GetStatistics().TotalTrackersCreated);
        
        public BackgroundTrackingService()
        {
            // 추적 설정 로드
            _ = Task.Run(LoadTrackingConfigurationAsync);
            
            System.Diagnostics.Debug.WriteLine("BackgroundTrackingService: Initialized");
        }
        
        /// <summary>
        /// 추적 설정을 데이터베이스에서 로드
        /// </summary>
        private async Task LoadTrackingConfigurationAsync()
        {
            try
            {
                var config = await App.DatabaseService.LoadTrackingConfigAsync();
                
                if (config != null)
                {
                    _globalTrackingConfig = new TrackingConfiguration
                    {
                        IsEnabled = config.IsEnabled,
                        MaxTrackingDistance = config.MaxTrackingDistance,
                        MaxDisappearFrames = config.MaxDisappearFrames,
                        IouThreshold = (float)config.IouThreshold,
                        SimilarityThreshold = (float)config.SimilarityThreshold,
                        EnableReIdentification = config.EnableReIdentification,
                        EnableMultiCameraTracking = config.EnableMultiCameraTracking,
                        TrackHistoryLength = config.TrackHistoryLength,
                        ShowTrackingId = config.ShowTrackingId,
                        ShowTrackingPath = config.ShowTrackingPath,
                        PathDisplayLength = config.PathDisplayLength,
                        AutoSaveTracking = config.AutoSaveTracking,
                        AutoSaveInterval = config.AutoSaveInterval,
                        TrackingMethod = config.TrackingMethod
                    };
                    
                    System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Configuration loaded - Enabled: {_globalTrackingConfig.IsEnabled}");
                }
                else
                {
                    // 기본 설정 사용
                    _globalTrackingConfig = new TrackingConfiguration();
                    System.Diagnostics.Debug.WriteLine("BackgroundTrackingService: Using default configuration");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Failed to load configuration - {ex.Message}");
                _globalTrackingConfig = new TrackingConfiguration();
            }
        }
        
        /// <summary>
        /// 카메라별 추적 서비스 초기화
        /// </summary>
        public void InitializeCameraTracking(string cameraId)
        {
            if (_globalTrackingConfig == null) return;
            
            if (!_trackingServices.ContainsKey(cameraId))
            {
                var trackingService = new PersonTrackingService(_globalTrackingConfig);
                _trackingServices[cameraId] = trackingService;
                _latestTrackedPersons[cameraId] = new List<TrackedPerson>();
                
                System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Initialized tracking for camera {cameraId}");
            }
        }
        
        /// <summary>
        /// 검출 결과를 처리하여 추적 업데이트
        /// </summary>
        public List<TrackedPerson> ProcessDetections(string cameraId, List<DetectionResult> detections)
        {
            if (!_globalTrackingConfig?.IsEnabled == true)
                return new List<TrackedPerson>();
            
            // 카메라별 추적 서비스 초기화 (필요시)
            InitializeCameraTracking(cameraId);
            
            if (!_trackingServices.TryGetValue(cameraId, out var trackingService))
                return new List<TrackedPerson>();
            
            try
            {
                // 사람만 필터링 (문자열에서 "person" 포함 여부 확인)
                var personDetections = detections.Where(d => d.ClassName?.ToLower()?.Contains("person") == true).ToList();
                if(personDetections.Count > 0)
                    System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Detected {personDetections.Count} people for {cameraId}");
                
                // 추적 업데이트
                var trackedPersons = trackingService.UpdateTracking(personDetections, cameraId);
                
                // 최신 추적 결과 저장
                _latestTrackedPersons[cameraId] = trackedPersons;
                
                // 검출 결과에 트래킹 ID 적용
                ApplyTrackingIdsToDetections(detections, trackedPersons);
                
                // 이벤트 발생
                TrackingUpdated?.Invoke(this, new TrackingUpdateEventArgs
                {
                    CameraId = cameraId,
                    TrackedPersons = trackedPersons,
                    DetectionsWithTracking = detections
                });
                
                // 통계 업데이트 (주기적으로)
                if (DateTime.Now.Millisecond % 100 == 0) // 대략 100ms마다
                {
                    UpdateStatistics();
                }
                
                return trackedPersons;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Error processing detections for {cameraId} - {ex.Message}");
                return new List<TrackedPerson>();
            }
        }
        
        /// <summary>
        /// 검출 결과에 트래킹 ID 적용
        /// </summary>
        private void ApplyTrackingIdsToDetections(List<DetectionResult> detections, List<TrackedPerson> trackedPersons)
        {
            foreach (var detection in detections.Where(d => d.ClassName?.ToLower()?.Contains("person") == true))
            {
                var tracked = trackedPersons.FirstOrDefault(t => 
                    Math.Abs(t.BoundingBox.X - detection.BoundingBox.X) < 10 &&
                    Math.Abs(t.BoundingBox.Y - detection.BoundingBox.Y) < 10);
                    
                if (tracked != null)
                {
                    detection.TrackingId = tracked.TrackingId;
                }
            }
        }
        
        /// <summary>
        /// 통계 정보 업데이트
        /// </summary>
        private void UpdateStatistics()
        {
            var totalStats = new TrackingStatistics
            {
                ActiveTrackerCount = TotalActiveTrackers,
                TotalTrackersCreated = TotalTrackersCreated,
                AverageTrackDuration = _trackingServices.Values
                    .SelectMany(ts => new[] { ts.GetStatistics().AverageTrackDuration })
                    .DefaultIfEmpty(0)
                    .Average()
            };
            
            StatisticsUpdated?.Invoke(this, new TrackingStatisticsEventArgs
            {
                Statistics = totalStats,
                CameraStats = _trackingServices.ToDictionary(
                    kvp => kvp.Key, 
                    kvp => kvp.Value.GetStatistics()
                )
            });
        }
        
        /// <summary>
        /// 카메라의 최신 추적 결과 조회
        /// </summary>
        public List<TrackedPerson> GetLatestTrackedPersons(string cameraId)
        {
            return _latestTrackedPersons.GetValueOrDefault(cameraId, new List<TrackedPerson>());
        }
        
        /// <summary>
        /// 추적 설정 업데이트
        /// </summary>
        public async Task UpdateTrackingConfigurationAsync(TrackingConfiguration newConfig)
        {
            _globalTrackingConfig = newConfig;
            
            // 모든 추적 서비스 초기화 (새 설정으로 재생성)
            var cameraIds = _trackingServices.Keys.ToList();
            _trackingServices.Clear();
            
            foreach (var cameraId in cameraIds)
            {
                _trackingServices[cameraId] = new PersonTrackingService(newConfig);
                _latestTrackedPersons[cameraId] = new List<TrackedPerson>();
            }
            
            // 데이터베이스에 저장
            try
            {
                await App.DatabaseService.SaveTrackingConfigAsync(new Database.TrackingConfig
                {
                    IsEnabled = newConfig.IsEnabled,
                    MaxTrackingDistance = newConfig.MaxTrackingDistance,
                    MaxDisappearFrames = newConfig.MaxDisappearFrames,
                    IouThreshold = newConfig.IouThreshold,
                    SimilarityThreshold = newConfig.SimilarityThreshold,
                    EnableReIdentification = newConfig.EnableReIdentification,
                    EnableMultiCameraTracking = newConfig.EnableMultiCameraTracking,
                    TrackHistoryLength = newConfig.TrackHistoryLength,
                    ShowTrackingId = newConfig.ShowTrackingId,
                    ShowTrackingPath = newConfig.ShowTrackingPath,
                    PathDisplayLength = newConfig.PathDisplayLength,
                    AutoSaveTracking = newConfig.AutoSaveTracking,
                    AutoSaveInterval = newConfig.AutoSaveInterval,
                    TrackingMethod = newConfig.TrackingMethod,
                    LastModified = DateTime.Now
                });
                
                System.Diagnostics.Debug.WriteLine("BackgroundTrackingService: Configuration updated and saved");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Failed to save configuration - {ex.Message}");
            }
        }
        
        /// <summary>
        /// 모든 추적 초기화
        /// </summary>
        public void ResetAllTracking()
        {
            foreach (var trackingService in _trackingServices.Values)
            {
                trackingService.Reset();
            }
            
            foreach (var cameraId in _latestTrackedPersons.Keys.ToList())
            {
                _latestTrackedPersons[cameraId] = new List<TrackedPerson>();
            }
            
            System.Diagnostics.Debug.WriteLine("BackgroundTrackingService: All tracking reset");
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            // PersonTrackingService는 IDisposable을 구현하지 않으므로 단순히 컬렉션만 정리
            _trackingServices.Clear();
            _latestTrackedPersons.Clear();
            
            _disposed = true;
            System.Diagnostics.Debug.WriteLine("BackgroundTrackingService: Disposed");
        }
    }
    
    /// <summary>
    /// 추적 업데이트 이벤트 인자
    /// </summary>
    public class TrackingUpdateEventArgs : EventArgs
    {
        public string CameraId { get; set; } = string.Empty;
        public List<TrackedPerson> TrackedPersons { get; set; } = new();
        public List<DetectionResult> DetectionsWithTracking { get; set; } = new();
    }
    
    /// <summary>
    /// 추적 통계 이벤트 인자
    /// </summary>
    public class TrackingStatisticsEventArgs : EventArgs
    {
        public TrackingStatistics Statistics { get; set; } = new();
        public Dictionary<string, TrackingStatistics> CameraStats { get; set; } = new();
    }
}