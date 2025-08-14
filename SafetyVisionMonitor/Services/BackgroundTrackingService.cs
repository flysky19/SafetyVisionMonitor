using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SafetyVisionMonitor.Shared.Database;
using SafetyVisionMonitor.Shared.Models;

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
        
        // AutoSave 관련
        private System.Timers.Timer? _autoSaveTimer;
        private readonly object _autoSaveLock = new object();
        
        // 성능 모니터링
        private DateTime _lastCpuCheck = DateTime.MinValue;
        private readonly TimeSpan _cpuCheckInterval = TimeSpan.FromSeconds(5);
        private const double CPU_THRESHOLD = 70.0; // CPU 사용률 임계값 (%)
        private const int MAX_TRACKERS_PER_CAMERA = 20; // 카메라당 최대 트래커 수
        private const int MAX_HISTORY_LENGTH = 20; // 트래킹 히스토리 최대 길이 (기존 100 → 20)
        
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
                    
                    // AutoSave 타이머 시작
                    StartAutoSaveTimer();
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
            if (_globalTrackingConfig == null)
            {
                System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Global tracking config is null for camera {cameraId}");
                return;
            }
            
            if (!_trackingServices.ContainsKey(cameraId))
            {
                System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Initializing tracking service for camera {cameraId}, Config enabled: {_globalTrackingConfig.IsEnabled}");
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
                
                System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Initialized tracking and acrylic filter for camera {cameraId} (file: {acrylicFilePath})");
            }
        }
        
        /// <summary>
        /// 검출 결과를 처리하여 추적 업데이트 (비동기)
        /// </summary>
        public async Task<List<TrackedPerson>> ProcessDetectionsAsync(string cameraId, List<DetectionResult> detections)
        {
            if (!_globalTrackingConfig?.IsEnabled == true)
                return new List<TrackedPerson>();
            
            return await Task.Run(() => ProcessDetectionsInternal(cameraId, detections));
        }
        
        /// <summary>
        /// 검출 결과를 처리하여 추적 업데이트 (동기 버전 - 호환성 유지)
        /// </summary>
        public List<TrackedPerson> ProcessDetections(string cameraId, List<DetectionResult> detections)
        {
            if (!_globalTrackingConfig?.IsEnabled == true)
                return new List<TrackedPerson>();
                
            return ProcessDetectionsInternal(cameraId, detections);
        }
        
        /// <summary>
        /// 실제 검출 결과 처리 로직
        /// </summary>
        private List<TrackedPerson> ProcessDetectionsInternal(string cameraId, List<DetectionResult> detections)
        {
            System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: ProcessDetectionsInternal - Camera: {cameraId}, Detections: {detections.Count}");
            
            // 카메라별 추적 서비스 초기화 (필요시)
            InitializeCameraTracking(cameraId);
            
            if (!_trackingServices.TryGetValue(cameraId, out var trackingService))
            {
                System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: No tracking service found for camera {cameraId}");
                return new List<TrackedPerson>();
            }
            
            try
            {
                // 트래킹이 비활성화된 경우 스킵
                if (_globalTrackingConfig == null || !_globalTrackingConfig.IsEnabled)
                {
                    System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Tracking disabled for camera {cameraId}");
                    return new List<TrackedPerson>();
                }
                
                // 성능 체크: CPU 사용률이 너무 높으면 스킵
                if (IsSystemOverloaded())
                {
                    System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Skipping tracking for {cameraId} due to high system load");
                    return GetLatestTrackedPersons(cameraId);
                }
                
                // 검출 결과 Label 확인
                foreach (var detection in detections.Take(3)) // 처음 3개만 로그
                {
                    System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Camera {cameraId} - Detection Label: '{detection.Label}', ClassName: '{detection.ClassName}'");
                }
                
                // 사람만 필터링 (효율적인 Label 속성 사용)
                var personDetections = detections.Where(d => d.Label == "person").ToList();
                System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Camera {cameraId} - Total detections: {detections.Count}, Person detections: {personDetections.Count}");
                
                // 아크릴 필터링 적용 (경계선 기준으로 내부/외부 판단 및 필터링)
                if (_acrylicFilters.TryGetValue(cameraId, out var acrylicFilter))
                {
                    personDetections = acrylicFilter.FilterDetections(personDetections);
                    System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Acrylic filtered {personDetections.Count} detections for {cameraId}");
                }
                
                // 추적 업데이트
                var trackedPersons = trackingService.UpdateTracking(personDetections, cameraId);
                System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Camera {cameraId} - Tracked persons: {trackedPersons.Count}");
                
                // 메모리 정리: 오래된 트래커 제거
                CleanupOldTrackers(trackedPersons);
                
                // 최신 추적 결과 저장 (스레드 안전)
                lock (_latestTrackedPersons)
                {
                    _latestTrackedPersons[cameraId] = trackedPersons;
                }
                
                // 검출 결과에 트래킹 ID 적용
                ApplyTrackingIdsToDetections(detections, trackedPersons);
                
                // 이벤트 발생 (UI 스레드에서)
                Task.Run(() =>
                {
                    TrackingUpdated?.Invoke(this, new TrackingUpdateEventArgs
                    {
                        CameraId = cameraId,
                        TrackedPersons = trackedPersons.ToList(), // 복사본 생성
                        DetectionsWithTracking = detections.ToList() // 복사본 생성
                    });
                });
                
                // 통계 업데이트 (주기적으로)
                if (DateTime.Now.Millisecond % 500 == 0) // 500ms마다 (부하 감소)
                {
                    _ = Task.Run(UpdateStatistics);
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
            foreach (var detection in detections.Where(d => d.Label == "person"))
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
        /// 시스템 과부하 상태 체크
        /// </summary>
        private bool IsSystemOverloaded()
        {
            // CPU 체크 주기 제한 (5초마다)
            if (DateTime.Now - _lastCpuCheck < _cpuCheckInterval)
                return false;
                
            _lastCpuCheck = DateTime.Now;
            
            try
            {
                // 메모리 사용량 체크
                var workingSet = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
                var workingSetMB = workingSet / (1024 * 1024);
                
                // 메모리 사용량이 1GB 초과 시 과부하로 판단
                if (workingSetMB > 1024)
                {
                    System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: High memory usage detected: {workingSetMB}MB");
                    return true;
                }
                
                // 활성 트래커 수 체크
                var totalTrackers = _trackingServices.Values.Sum(ts => ts.GetStatistics().ActiveTrackerCount);
                if (totalTrackers > MAX_TRACKERS_PER_CAMERA * _trackingServices.Count)
                {
                    System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Too many trackers: {totalTrackers}");
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Error checking system load - {ex.Message}");
                return false; // 오류 시 계속 진행
            }
        }
        
        /// <summary>
        /// 오래된 트래커 정리
        /// </summary>
        private void CleanupOldTrackers(List<TrackedPerson> trackedPersons)
        {
            try
            {
                // 비활성 트래커나 너무 오래된 트래커 제거
                var trackedToRemove = trackedPersons.Where(p => 
                    !p.IsActive || 
                    (DateTime.Now - p.FirstDetectionTime).TotalSeconds > 300 || // 5분 이상 된 트래커
                    (p.TrackingHistory?.Count ?? 0) > MAX_HISTORY_LENGTH // 히스토리가 너무 긴 트래커
                ).ToList();
                
                foreach (var tracked in trackedToRemove)
                {
                    // 히스토리 크기 제한
                    if (tracked.TrackingHistory != null && tracked.TrackingHistory.Count > MAX_HISTORY_LENGTH)
                    {
                        var recentHistory = tracked.TrackingHistory.TakeLast(MAX_HISTORY_LENGTH).ToList();
                        tracked.TrackingHistory.Clear();
                        tracked.TrackingHistory.AddRange(recentHistory);
                    }
                }
                
                if (trackedToRemove.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Cleaned up {trackedToRemove.Count} old trackers");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Error during tracker cleanup - {ex.Message}");
            }
        }
        
        /// <summary>
        /// 추적 설정을 데이터베이스에서 다시 로드
        /// </summary>
        public async Task ReloadConfigurationAsync()
        {
            await LoadTrackingConfigurationAsync();
            
            // 모든 추적 서비스에 새 설정 적용
            if (_globalTrackingConfig != null)
            {
                var cameraIds = _trackingServices.Keys.ToList();
                foreach (var cameraId in cameraIds)
                {
                    _trackingServices[cameraId] = new PersonTrackingService(_globalTrackingConfig);
                    _latestTrackedPersons[cameraId] = new List<TrackedPerson>();
                }
                
                // AutoSave 타이머 재시작
                StartAutoSaveTimer();
                
                System.Diagnostics.Debug.WriteLine("BackgroundTrackingService: Configuration reloaded and applied");
            }
        }
        
        /// <summary>
        /// 현재 추적 설정 가져오기
        /// </summary>
        public TrackingConfiguration? GetTrackingConfiguration()
        {
            return _globalTrackingConfig;
        }
        
        /// <summary>
        /// 메모리 내 추적 설정만 업데이트 (데이터베이스 저장 없이)
        /// </summary>
        public void UpdateTrackingConfigurationInMemory(TrackingConfiguration newConfig)
        {
            _globalTrackingConfig = newConfig;
            
            // 모든 추적 서비스에 새 설정 적용 (재생성 없이 설정만 업데이트)
            foreach (var trackingService in _trackingServices.Values)
            {
                // PersonTrackingService의 설정 업데이트 메서드가 필요하지만,
                // 현재는 재생성으로 대체
            }
            
            // AutoSave 타이머 재시작
            StartAutoSaveTimer();
            
            System.Diagnostics.Debug.WriteLine("BackgroundTrackingService: Configuration updated in memory");
        }
        
        /// <summary>
        /// 추적 설정 업데이트 (데이터베이스에도 저장)
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
                await App.DatabaseService.SaveTrackingConfigAsync(new TrackingConfig
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
        /// 카메라의 아크릴 필터 새로고침 (파일에서 다시 로드)
        /// </summary>
        public void RefreshAcrylicFilter(string cameraId)
        {
            if (_acrylicFilters.TryGetValue(cameraId, out var acrylicFilter))
            {
                var acrylicFilePath = Path.Combine("Config", "Acrylic", $"camera_{cameraId}_boundary.json");
                acrylicFilter.LoadFromFile(acrylicFilePath);
                System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: Refreshed acrylic filter for camera {cameraId}");
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
        
        /// <summary>
        /// AutoSave 타이머 시작/재시작
        /// </summary>
        private void StartAutoSaveTimer()
        {
            lock (_autoSaveLock)
            {
                // 기존 타이머 정리
                StopAutoSaveTimer();
                
                if (_globalTrackingConfig?.AutoSaveTracking == true && _globalTrackingConfig.AutoSaveInterval > 0)
                {
                    var intervalMs = _globalTrackingConfig.AutoSaveInterval * 1000; // 초를 밀리초로 변환
                    _autoSaveTimer = new System.Timers.Timer(intervalMs);
                    _autoSaveTimer.Elapsed += OnAutoSaveElapsed;
                    _autoSaveTimer.AutoReset = true;
                    _autoSaveTimer.Start();
                    
                    System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: AutoSave timer started - interval: {_globalTrackingConfig.AutoSaveInterval} seconds");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("BackgroundTrackingService: AutoSave disabled or invalid interval");
                }
            }
        }
        
        /// <summary>
        /// AutoSave 타이머 정지
        /// </summary>
        private void StopAutoSaveTimer()
        {
            lock (_autoSaveLock)
            {
                if (_autoSaveTimer != null)
                {
                    _autoSaveTimer.Stop();
                    _autoSaveTimer.Elapsed -= OnAutoSaveElapsed;
                    _autoSaveTimer.Dispose();
                    _autoSaveTimer = null;
                    
                    System.Diagnostics.Debug.WriteLine("BackgroundTrackingService: AutoSave timer stopped");
                }
            }
        }
        
        /// <summary>
        /// AutoSave 타이머 이벤트 핸들러
        /// </summary>
        private async void OnAutoSaveElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            if (_disposed || _globalTrackingConfig?.AutoSaveTracking != true)
                return;
                
            try
            {
                await SaveCurrentTrackingData();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: AutoSave error - {ex.Message}");
            }
        }
        
        /// <summary>
        /// 현재 추적 데이터를 데이터베이스에 저장 (비동기, 별도 스레드)
        /// </summary>
        private async Task SaveCurrentTrackingData()
        {
            await Task.Run(async () =>
            {
                try
                {
                    var saveTime = DateTime.Now;
                    var trackingRecords = new List<PersonTrackingRecord>();
                    
                    // 모든 카메라의 추적 데이터 수집 (스레드 안전하게)
                    Dictionary<string, List<TrackedPerson>> currentTrackedPersons;
                    lock (_latestTrackedPersons)
                    {
                        currentTrackedPersons = _latestTrackedPersons.ToDictionary(
                            kvp => kvp.Key, 
                            kvp => kvp.Value.Where(p => p.IsActive).ToList() // 활성 상태만
                        );
                    }
                    
                    foreach (var kvp in currentTrackedPersons)
                    {
                        var cameraId = kvp.Key;
                        var trackedPersons = kvp.Value;
                        
                        // 메모리 제한: 카메라당 최대 10개 트래커만 저장
                        var limitedPersons = trackedPersons.Take(10).ToList();
                        
                        foreach (var person in limitedPersons)
                        {
                            // 최근 추적 히스토리 수집 (메모리 최적화: 100 → 20개 점)
                            var recentHistory = person.TrackingHistory?.TakeLast(MAX_HISTORY_LENGTH).ToList() ?? new List<System.Drawing.PointF>();
                            
                            // JSON 직렬화 크기 제한 (너무 큰 데이터 방지)
                            var historyJson = "[]";
                            if (recentHistory.Count > 0)
                            {
                                try
                                {
                                    historyJson = System.Text.Json.JsonSerializer.Serialize(recentHistory);
                                    
                                    // JSON 크기가 2KB 초과 시 데이터 축소
                                    if (historyJson.Length > 2048)
                                    {
                                        var reducedHistory = recentHistory.TakeLast(10).ToList();
                                        historyJson = System.Text.Json.JsonSerializer.Serialize(reducedHistory);
                                    }
                                }
                                catch
                                {
                                    historyJson = "[]"; // 직렬화 실패 시 빈 배열
                                }
                            }
                            
                            var record = new PersonTrackingRecord
                            {
                                TrackingId = person.TrackingId,
                                CameraId = cameraId,
                                BoundingBoxX = person.BoundingBox.X,
                                BoundingBoxY = person.BoundingBox.Y,
                                BoundingBoxWidth = person.BoundingBox.Width,
                                BoundingBoxHeight = person.BoundingBox.Height,
                                CenterX = person.BoundingBox.X + person.BoundingBox.Width / 2,
                                CenterY = person.BoundingBox.Y + person.BoundingBox.Height / 2,
                                Confidence = person.Confidence,
                                TrackingHistoryJson = historyJson,
                                Location = person.Location?.ToString() ?? "Unknown",
                                IsActive = person.IsActive,
                                CreatedTime = person.FirstDetectionTime,
                                FirstDetectedTime = person.FirstDetectionTime,
                                LastSeenTime = person.Timestamp,
                                LastUpdated = saveTime
                            };
                            
                            trackingRecords.Add(record);
                        }
                    }
                    
                    if (trackingRecords.Any())
                    {
                        // 데이터베이스에 일괄 저장 (별도 스레드에서)
                        await App.DatabaseService.SavePersonTrackingRecordsAsync(trackingRecords);
                        
                        System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: AutoSave completed - {trackingRecords.Count} tracking records saved from {currentTrackedPersons.Count} cameras");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("BackgroundTrackingService: AutoSave skipped - no active tracking data");
                    }
                    
                    // 메모리 정리
                    trackingRecords.Clear();
                    currentTrackedPersons.Clear();
                    
                    // 가비지 컬렉션 힌트 (대량 데이터 처리 후)
                    if (trackingRecords.Count > 50)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"BackgroundTrackingService: SaveCurrentTrackingData error - {ex.Message}");
                }
            });
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            
            // AutoSave 타이머 정리
            StopAutoSaveTimer();
            
            // PersonTrackingService는 IDisposable을 구현하지 않으므로 단순히 컬렉션만 정리
            _trackingServices.Clear();
            _latestTrackedPersons.Clear();
            
            // 아크릴 필터 정리
            foreach (var acrylicFilter in _acrylicFilters.Values)
            {
                acrylicFilter.Dispose();
            }
            _acrylicFilters.Clear();
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