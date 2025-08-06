using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
        private readonly ConcurrentDictionary<string, AcrylicRegionFilter> _acrylicFilters = new();
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
                
                // 아크릴 필터 초기화 및 설정 파일 로드
                var acrylicFilter = new AcrylicRegionFilter(cameraId);
                var acrylicFilePath = Path.Combine("Config", "Acrylic", $"camera_{cameraId}_boundary.json");
                
                // 디렉토리 생성
                var acrylicDirectory = Path.GetDirectoryName(acrylicFilePath);
                if (!string.IsNullOrEmpty(acrylicDirectory) && !Directory.Exists(acrylicDirectory))
                {
                    Directory.CreateDirectory(acrylicDirectory);
                }
                
                acrylicFilter.LoadFromFile(acrylicFilePath);
                _acrylicFilters[cameraId] = acrylicFilter;
                
                System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Initialized tracking and acrylic filter for camera {cameraId}");
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
                
                // 아크릴 필터링 적용 (경계선 기준으로 내부/외부 판단 및 필터링)
                if (_acrylicFilters.TryGetValue(cameraId, out var acrylicFilter))
                {
                    personDetections = acrylicFilter.FilterDetections(personDetections);
                    System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Acrylic filtered {personDetections.Count} detections for {cameraId}");
                }
                
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
        
        /// <summary>
        /// 카메라의 아크릴 필터 가져오기
        /// </summary>
        public AcrylicRegionFilter? GetAcrylicFilter(string cameraId)
        {
            _acrylicFilters.TryGetValue(cameraId, out var filter);
            return filter;
        }
        
        /// <summary>
        /// 카메라에 아크릴 경계선 설정
        /// </summary>
        public void SetAcrylicBoundary(string cameraId, System.Drawing.Point[] boundary)
        {
            InitializeCameraTracking(cameraId); // 아크릴 필터 초기화 보장
            
            if (_acrylicFilters.TryGetValue(cameraId, out var acrylicFilter))
            {
                acrylicFilter.SetAcrylicBoundary(boundary);
                
                // 설정 파일에 저장
                var acrylicFilePath = Path.Combine("Config", "Acrylic", $"camera_{cameraId}_boundary.json");
                acrylicFilter.SaveToFile(acrylicFilePath);
                
                System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Set acrylic boundary for camera {cameraId} with {boundary.Length} points");
            }
        }
        
        /// <summary>
        /// 카메라의 추적 모드 설정
        /// </summary>
        public void SetTrackingMode(string cameraId, TrackingMode mode)
        {
            InitializeCameraTracking(cameraId);
            
            if (_acrylicFilters.TryGetValue(cameraId, out var acrylicFilter))
            {
                acrylicFilter.SetTrackingMode(mode);
                
                // 설정 파일에 저장
                var acrylicFilePath = Path.Combine("Config", "Acrylic", $"camera_{cameraId}_boundary.json");
                acrylicFilter.SaveToFile(acrylicFilePath);
                
                System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Set tracking mode to {mode} for camera {cameraId}");
            }
        }
        
        /// <summary>
        /// 카메라의 아크릴 영역 통계 조회
        /// </summary>
        public AcrylicRegionStats? GetAcrylicStats(string cameraId, List<DetectionResult> detections)
        {
            if (_acrylicFilters.TryGetValue(cameraId, out var acrylicFilter))
            {
                return acrylicFilter.GetStats(detections);
            }
            return null;
        }
        
        /// <summary>
        /// 카메라의 아크릴 경계선 제거
        /// </summary>
        public void ClearAcrylicBoundary(string cameraId)
        {
            if (_acrylicFilters.TryGetValue(cameraId, out var acrylicFilter))
            {
                acrylicFilter.ClearBoundary();
                
                // 설정 파일에 저장
                var acrylicFilePath = Path.Combine("Config", "Acrylic", $"camera_{cameraId}_boundary.json");
                acrylicFilter.SaveToFile(acrylicFilePath);
                
                System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Cleared acrylic boundary for camera {cameraId}");
            }
        }
        
        /// <summary>
        /// 카메라의 아크릴 경계선 시각화
        /// </summary>
        public OpenCvSharp.Mat? VisualizeAcrylicBoundary(string cameraId, OpenCvSharp.Mat frame)
        {
            if (_acrylicFilters.TryGetValue(cameraId, out var acrylicFilter))
            {
                return acrylicFilter.VisualizeAcrylicBoundary(frame);
            }
            return null;
        }
        
        /// <summary>
        /// 카메라의 프레임 크기 설정 (아크릴 필터용)
        /// </summary>
        public void SetCameraFrameSize(string cameraId, System.Drawing.Size frameSize)
        {
            InitializeCameraTracking(cameraId); // 아크릴 필터 초기화 보장
            
            if (_acrylicFilters.TryGetValue(cameraId, out var acrylicFilter))
            {
                // System.Drawing.Size를 OpenCvSharp.Size로 변환
                var cvSize = new OpenCvSharp.Size(frameSize.Width, frameSize.Height);
                acrylicFilter.SetFrameSize(cvSize);
                System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Set frame size {frameSize} for camera {cameraId}");
            }
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            // PersonTrackingService는 IDisposable을 구현하지 않으므로 단순히 컬렉션만 정리
            _trackingServices.Clear();
            _latestTrackedPersons.Clear();
            
            // 아크릴 필터 정리
            foreach (var acrylicFilter in _acrylicFilters.Values)
            {
                acrylicFilter.Dispose();
            }
            _acrylicFilters.Clear();
            
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