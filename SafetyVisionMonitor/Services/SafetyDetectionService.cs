using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using SafetyVisionMonitor.Shared.Models;
using SafetyVisionMonitor.Services.Handlers;
using SafetyVisionMonitor.Shared.Services;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 안전 감시 서비스 - 3D 구역과 검출된 객체의 교차 검사
    /// </summary>
    public class SafetyDetectionService : IDisposable
    {
        private readonly DatabaseService _databaseService;
        private readonly Dictionary<string, List<Zone3D>> _cameraZones = new();
        private readonly Dictionary<string, ZoneState> _zoneStates = new(); // 구역별 상태 추적
        private readonly TimeSpan _alertCooldown = TimeSpan.FromSeconds(5); // 5초 쿨다운
        private readonly SafetyEventHandlerManager _eventHandlerManager;
        
        private bool _disposed = false;
        
        // 이벤트
        public event EventHandler<SafetyEventArgs>? SafetyEventDetected;
        public event EventHandler<ZoneViolationArgs>? ZoneViolationDetected;
        
        // 통계
        public long TotalViolationsDetected { get; private set; } = 0;
        public long TotalSafetyEventsGenerated { get; private set; } = 0;
        
        public SafetyDetectionService(DatabaseService databaseService)
        {
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            _eventHandlerManager = new SafetyEventHandlerManager();
            
            // 초기 구역 데이터 로드
            _ = Task.Run(LoadZoneDataAsync);
            
            System.Diagnostics.Debug.WriteLine("SafetyDetectionService: Initialized with event handler manager");
        }
        
        /// <summary>
        /// 구역 데이터를 데이터베이스에서 로드
        /// </summary>
        public async Task LoadZoneDataAsync()
        {
            try
            {
                var zones = await _databaseService.LoadZone3DConfigsAsync();
                
                lock (_cameraZones)
                {
                    _cameraZones.Clear();
                    
                    // 카메라별로 구역 그룹화
                    foreach (var zone in zones.Where(z => z.IsEnabled))
                    {
                        if (!_cameraZones.ContainsKey(zone.CameraId))
                        {
                            _cameraZones[zone.CameraId] = new List<Zone3D>();
                        }
                        
                        _cameraZones[zone.CameraId].Add(zone);
                    }
                }
                
                var totalZones = zones.Count(z => z.IsEnabled);
                System.Diagnostics.Debug.WriteLine($"SafetyDetectionService: Loaded {totalZones} active zones from {_cameraZones.Count} cameras");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SafetyDetectionService: Failed to load zone data: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 검출된 객체들에 대해 안전 검사 수행
        /// </summary>
        public async Task<SafetyCheckResult> CheckSafetyAsync(string cameraId, DetectionResult[] detections)
        {
            if (detections.Length == 0 || !_cameraZones.ContainsKey(cameraId))
            {
                return new SafetyCheckResult { CameraId = cameraId };
            }
            
            var result = new SafetyCheckResult { CameraId = cameraId };
            var zones = _cameraZones[cameraId];
            
            try
            {
                // 구역 상태 정리 (퇴장한 사람들 제거)
                CleanupZoneStates(cameraId, detections);
                
                foreach (var detection in detections)
                {
                    // 사람만 체크 (필요시 다른 객체도 확장 가능)
                    if (detection.Label != "person")
                        continue;
                    
                    var personViolations = new List<ZoneViolation>();
                    
                    foreach (var zone in zones)
                    {
                        var detectedBodyPart = GetPersonInZone(detection, zone);
                        if (!string.IsNullOrEmpty(detectedBodyPart))
                        {
                            var violation = new ZoneViolation
                            {
                                Detection = detection,
                                Zone = zone,
                                ViolationType = GetViolationType(zone.Type),
                                Timestamp = DateTime.Now,
                                Confidence = detection.Confidence,
                                DetectedBodyPart = detectedBodyPart
                            };
                            
                            personViolations.Add(violation);
                            result.Violations.Add(violation);
                            
                            // 스마트 알림 체크 (구역 상태 기반)
                            if (ShouldTriggerSmartAlert(violation))
                            {
                                await TriggerSafetyAlert(violation);
                            }
                        }
                    }
                    
                    if (personViolations.Count > 0)
                    {
                        result.ViolatingPersons.Add(detection);
                    }
                    else
                    {
                        result.SafePersons.Add(detection);
                    }
                }
                
                TotalViolationsDetected += result.Violations.Count;
                
                // 위반 사항이 있으면 이벤트 발생
                if (result.HasViolations)
                {
                    ZoneViolationDetected?.Invoke(this, new ZoneViolationArgs
                    {
                        CameraId = cameraId,
                        Violations = result.Violations.ToArray(),
                        Timestamp = DateTime.Now
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SafetyDetectionService: Safety check error for {cameraId}: {ex.Message}");
            }
            
            return result;
        }
        
        /// <summary>
        /// 사람이 특정 구역 안에 있는지 검사하고 감지된 신체 부위 반환 (발, 손, 몸 전체 포함)
        /// </summary>
        /// <returns>감지된 신체 부위 이름, 없으면 빈 문자열</returns>
        private string GetPersonInZone(DetectionResult detection, Zone3D zone)
        {
            if (zone.FloorPoints.Count < 3)
                return string.Empty;
            
            try
            {
                var bbox = detection.BoundingBox;
                
                // 사람의 주요 신체 부위 좌표 계산
                var checkPoints = new List<System.Windows.Point>();
                
                // 1. 발 위치 (하단 중앙) - 기존 방식
                checkPoints.Add(new System.Windows.Point(
                    bbox.X + bbox.Width / 2, 
                    bbox.Y + bbox.Height
                ));
                
                // 2. 왼손 추정 위치 (상단 왼쪽, 어깨 높이)
                checkPoints.Add(new System.Windows.Point(
                    bbox.X,
                    bbox.Y + bbox.Height * 0.3 // 상단에서 30% 지점
                ));
                
                // 3. 오른손 추정 위치 (상단 오른쪽, 어깨 높이)
                checkPoints.Add(new System.Windows.Point(
                    bbox.X + bbox.Width,
                    bbox.Y + bbox.Height * 0.3
                ));
                
                // 4. 몸 중앙 (허리 높이)
                checkPoints.Add(new System.Windows.Point(
                    bbox.X + bbox.Width / 2,
                    bbox.Y + bbox.Height * 0.6
                ));
                
                // 5. 머리 위치 (상단 중앙)
                checkPoints.Add(new System.Windows.Point(
                    bbox.X + bbox.Width / 2,
                    bbox.Y
                ));
                
                // 각 지점을 월드 좌표로 변환하여 구역 내부에 있는지 확인
                for (int i = 0; i < checkPoints.Count; i++)
                {
                    var screenPoint = checkPoints[i];
                    var worldPoint = CoordinateTransformService.ScreenToWorld(
                        screenPoint,
                        zone.CalibrationFrameWidth,
                        zone.CalibrationFrameHeight,
                        zone.CalibrationPixelsPerMeter
                    );
                    
                    if (IsPointInPolygon(worldPoint, zone.FloorPoints))
                    {
                        // 감지된 신체 부위 이름
                        var bodyPart = GetBodyPartName(i);
                        System.Diagnostics.Debug.WriteLine(
                            $"SafetyDetectionService: {bodyPart} detected in zone '{zone.Name}' " +
                            $"(Camera: {detection.CameraId})");
                        
                        return bodyPart; // 첫 번째로 감지된 신체 부위 반환
                    }
                }
                
                return string.Empty; // 모든 지점이 구역 외부에 있음
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SafetyDetectionService: Zone intersection check error: {ex.Message}");
                return string.Empty;
            }
        }
        
        /// <summary>
        /// 체크 포인트 인덱스에 따른 신체 부위 이름 반환
        /// </summary>
        private string GetBodyPartName(int pointIndex)
        {
            return pointIndex switch
            {
                0 => "발",
                1 => "왼손",
                2 => "오른손", 
                3 => "몸통",
                4 => "머리",
                _ => "신체"
            };
        }
        
        /// <summary>
        /// Point-in-Polygon 알고리즘 (Ray Casting Method)
        /// </summary>
        private static bool IsPointInPolygon(Point2D point, List<Point2D> polygon)
        {
            if (polygon.Count < 3)
                return false;
            
            bool inside = false;
            int j = polygon.Count - 1;
            
            for (int i = 0; i < polygon.Count; j = i++)
            {
                var pi = polygon[i];
                var pj = polygon[j];
                
                if (((pi.Y > point.Y) != (pj.Y > point.Y)) &&
                    (point.X < (pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y) + pi.X))
                {
                    inside = !inside;
                }
            }
            
            return inside;
        }
        
        /// <summary>
        /// 구역 타입에 따른 위반 타입 결정
        /// </summary>
        private ViolationType GetViolationType(ZoneType zoneType)
        {
            return zoneType switch
            {
                ZoneType.Warning => ViolationType.WarningZoneEntry,
                ZoneType.Danger => ViolationType.DangerZoneEntry,
                _ => ViolationType.WarningZoneEntry
            };
        }
        
        /// <summary>
        /// 스마트 알림 판단 (구역 상태 기반)
        /// </summary>
        private bool ShouldTriggerSmartAlert(ZoneViolation violation)
        {
            var zoneKey = $"{violation.Detection.CameraId}_{violation.Zone.Id}";
            var trackingId = violation.Detection.TrackingId ?? 0; // 추적ID가 없으면 0으로 처리
            var now = DateTime.Now;
            
            // 구역 상태 초기화
            if (!_zoneStates.ContainsKey(zoneKey))
            {
                _zoneStates[zoneKey] = new ZoneState();
            }
            
            var zoneState = _zoneStates[zoneKey];
            
            // 1. 새로운 사람이 구역에 진입한 경우
            if (!zoneState.CurrentPersons.Contains(trackingId))
            {
                // 진입 기록
                zoneState.CurrentPersons.Add(trackingId);
                zoneState.LastDetectedParts[trackingId] = violation.DetectedBodyPart;
                zoneState.EntryTimes[trackingId] = now;
                zoneState.LastAlertTime = now;
                
                System.Diagnostics.Debug.WriteLine(
                    $"SafetyDetectionService: NEW ENTRY - Person {trackingId} ({violation.DetectedBodyPart}) " +
                    $"entered zone '{violation.Zone.Name}' (Camera: {violation.Detection.CameraId})");
                
                return true; // 새 진입자는 무조건 알림
            }
            
            // 2. 기존 사람의 새로운 신체 부위가 감지된 경우
            var lastDetectedPart = zoneState.LastDetectedParts.GetValueOrDefault(trackingId, "");
            if (lastDetectedPart != violation.DetectedBodyPart)
            {
                zoneState.LastDetectedParts[trackingId] = violation.DetectedBodyPart;
                
                // 중요한 신체 부위(손) 감지 시에만 추가 알림
                if (violation.DetectedBodyPart.Contains("손") && !lastDetectedPart.Contains("손"))
                {
                    // 손 감지는 쿨다운 체크
                    if (now - zoneState.LastAlertTime >= _alertCooldown)
                    {
                        zoneState.LastAlertTime = now;
                        System.Diagnostics.Debug.WriteLine(
                            $"SafetyDetectionService: HAND DETECTED - Person {trackingId} " +
                            $"hand detected in zone '{violation.Zone.Name}' (was: {lastDetectedPart})");
                        
                        return true;
                    }
                }
            }
            
            return false; // 기존 사람의 동일 또는 중요하지 않은 부위는 알림 없음
        }
        
        /// <summary>
        /// 구역에서 퇴장한 사람들 정리 (주기적 호출 필요)
        /// </summary>
        private void CleanupZoneStates(string cameraId, DetectionResult[] currentDetections)
        {
            var currentTrackingIds = currentDetections
                .Where(d => d.TrackingId.HasValue)
                .Select(d => d.TrackingId.Value)
                .ToHashSet();
            
            // 각 구역에서 현재 감지되지 않는 사람들 제거
            var zonesToClean = _zoneStates.Keys
                .Where(key => key.StartsWith($"{cameraId}_"))
                .ToList();
                
            foreach (var zoneKey in zonesToClean)
            {
                var zoneState = _zoneStates[zoneKey];
                var personsToRemove = zoneState.CurrentPersons
                    .Where(personId => !currentTrackingIds.Contains(personId))
                    .ToList();
                    
                foreach (var personId in personsToRemove)
                {
                    zoneState.CurrentPersons.Remove(personId);
                    zoneState.LastDetectedParts.Remove(personId);
                    zoneState.EntryTimes.Remove(personId);
                    zoneState.AlertedPersons.Remove(personId);
                    
                    System.Diagnostics.Debug.WriteLine(
                        $"SafetyDetectionService: Person {personId} left zone {zoneKey}");
                }
            }
        }
        
        /// <summary>
        /// 안전 알림 발생 (새로운 이벤트 핸들러 시스템 사용)
        /// </summary>
        private async Task TriggerSafetyAlert(ZoneViolation violation)
        {
            try
            {
                // 안전 이벤트 생성 (기본 정보만)
                var safetyEvent = new SafetyEvent
                {
                    Id = 0, // 자동 생성
                    EventType = violation.ViolationType.ToString(),
                    CameraId = violation.Detection.CameraId,
                    ZoneId = violation.Zone.Id,
                    PersonBoundingBox = $"{violation.Detection.BoundingBox.X:F0},{violation.Detection.BoundingBox.Y:F0},{violation.Detection.BoundingBox.Width:F0},{violation.Detection.BoundingBox.Height:F0}",
                    Confidence = violation.Detection.Confidence,
                    Timestamp = violation.Timestamp,
                    Severity = GetSeverityLevel(violation.ViolationType),
                    Description = GenerateEventDescription(violation),
                    IsAcknowledged = false
                };
                
                // 이벤트 컨텍스트 생성
                var context = new SafetyEventContext
                {
                    SafetyEvent = safetyEvent,
                    Violation = violation,
                    ProcessingStartTime = DateTime.Now
                };
                
                // 추가 컨텍스트 정보 설정
                context.SetProperty("OriginalAlertTime", DateTime.Now);
                context.SetProperty("ServiceVersion", "1.0");
                context.SetProperty("ZoneType", violation.Zone.Type.ToString());
                
                // 이벤트 핸들러 체인 실행 (병렬 처리)
                await _eventHandlerManager.HandleEventAsync(context);
                
                TotalSafetyEventsGenerated++;
                
                // 기존 이벤트도 발생 (하위 호환성)
                SafetyEventDetected?.Invoke(this, new SafetyEventArgs
                {
                    SafetyEvent = context.SafetyEvent,
                    Violation = violation
                });
                
                var processingTime = DateTime.Now - context.ProcessingStartTime;
                System.Diagnostics.Debug.WriteLine(
                    $"SafetyDetectionService: Safety alert completed - {violation.ViolationType} " +
                    $"in {violation.Zone.Name} (Camera: {violation.Detection.CameraId}) " +
                    $"[{processingTime.TotalMilliseconds:F1}ms]");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SafetyDetectionService: Failed to trigger safety alert: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 위반 타입에 따른 심각도 레벨 결정
        /// </summary>
        private string GetSeverityLevel(ViolationType violationType)
        {
            return violationType switch
            {
                ViolationType.DangerZoneEntry => "High",
                ViolationType.WarningZoneEntry => "Medium",
                _ => "Low"
            };
        }
        
        /// <summary>
        /// 이벤트 설명 생성
        /// </summary>
        private string GenerateEventDescription(ZoneViolation violation)
        {
            var zoneTypeName = violation.Zone.Type == ZoneType.Danger ? "위험구역" : "경고구역";
            var bodyPartInfo = string.IsNullOrEmpty(violation.DetectedBodyPart) 
                ? "" 
                : $" ({violation.DetectedBodyPart} 감지)";
            
            return $"작업자{bodyPartInfo}가 {zoneTypeName} '{violation.Zone.Name}'에 진입했습니다. " +
                   $"신뢰도: {violation.Confidence:P1}";
        }
        
        /// <summary>
        /// 구역 데이터 새로고침 (구역 설정 변경 시 호출)
        /// </summary>
        public async Task RefreshZoneDataAsync()
        {
            await LoadZoneDataAsync();
            System.Diagnostics.Debug.WriteLine("SafetyDetectionService: Zone data refreshed");
        }
        
        /// <summary>
        /// 특정 카메라의 통계 조회
        /// </summary>
        public SafetyStatistics GetStatistics(string? cameraId = null)
        {
            var stats = new SafetyStatistics
            {
                TotalViolations = TotalViolationsDetected,
                TotalSafetyEvents = TotalSafetyEventsGenerated,
                ActiveZones = _cameraZones.Values.Sum(zones => zones.Count),
                MonitoredCameras = _cameraZones.Keys.Count
            };
            
            if (!string.IsNullOrEmpty(cameraId) && _cameraZones.ContainsKey(cameraId))
            {
                stats.ActiveZones = _cameraZones[cameraId].Count;
                stats.MonitoredCameras = 1;
            }
            
            return stats;
        }
        
        /// <summary>
        /// 이벤트 핸들러 관리자 접근 (확장성을 위해)
        /// </summary>
        public SafetyEventHandlerManager EventHandlerManager => _eventHandlerManager;
        
        /// <summary>
        /// 커스텀 이벤트 핸들러 등록
        /// </summary>
        public void RegisterEventHandler(ISafetyEventHandler handler)
        {
            _eventHandlerManager.RegisterHandler(handler);
            System.Diagnostics.Debug.WriteLine($"SafetyDetectionService: Registered custom handler - {handler.Name}");
        }
        
        /// <summary>
        /// 이벤트 핸들러 제거
        /// </summary>
        public void UnregisterEventHandler<T>() where T : ISafetyEventHandler
        {
            _eventHandlerManager.UnregisterHandler<T>();
            System.Diagnostics.Debug.WriteLine($"SafetyDetectionService: Unregistered handler - {typeof(T).Name}");
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            _eventHandlerManager?.Dispose();
            _cameraZones.Clear();
            _zoneStates.Clear();
            _disposed = true;
            
            System.Diagnostics.Debug.WriteLine("SafetyDetectionService: Disposed");
        }
    }
    
    /// <summary>
    /// 안전 검사 결과
    /// </summary>
    public class SafetyCheckResult
    {
        public string CameraId { get; set; } = string.Empty;
        public List<ZoneViolation> Violations { get; set; } = new();
        public List<DetectionResult> ViolatingPersons { get; set; } = new();
        public List<DetectionResult> SafePersons { get; set; } = new();
        
        public bool HasViolations => Violations.Count > 0;
        public int TotalPersons => ViolatingPersons.Count + SafePersons.Count;
    }
    
    /// <summary>
    /// 구역 위반 정보
    /// </summary>
    public class ZoneViolation
    {
        public DetectionResult Detection { get; set; } = new();
        public Zone3D Zone { get; set; } = new();
        public ViolationType ViolationType { get; set; }
        public DateTime Timestamp { get; set; }
        public float Confidence { get; set; }
        
        /// <summary>
        /// 감지된 신체 부위 (발, 왼손, 오른손, 몸통, 머리)
        /// </summary>
        public string DetectedBodyPart { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// 위반 타입
    /// </summary>
    public enum ViolationType
    {
        WarningZoneEntry,
        DangerZoneEntry,
        UnauthorizedAreaAccess,
        SafetyEquipmentMissing
    }
    
    /// <summary>
    /// 안전 통계
    /// </summary>
    public class SafetyStatistics
    {
        public long TotalViolations { get; set; }
        public long TotalSafetyEvents { get; set; }
        public int ActiveZones { get; set; }
        public int MonitoredCameras { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
    
    /// <summary>
    /// 안전 이벤트 인자
    /// </summary>
    public class SafetyEventArgs : EventArgs
    {
        public SafetyEvent SafetyEvent { get; set; } = new();
        public ZoneViolation Violation { get; set; } = new();
    }
    
    /// <summary>
    /// 구역 위반 이벤트 인자
    /// </summary>
    public class ZoneViolationArgs : EventArgs
    {
        public string CameraId { get; set; } = string.Empty;
        public ZoneViolation[] Violations { get; set; } = Array.Empty<ZoneViolation>();
        public DateTime Timestamp { get; set; }
    }
    
    /// <summary>
    /// 구역별 상태 추적 클래스
    /// </summary>
    public class ZoneState
    {
        /// <summary>
        /// 현재 구역 내에 있는 사람들 (TrackingId)
        /// </summary>
        public HashSet<int> CurrentPersons { get; set; } = new();
        
        /// <summary>
        /// 각 사람의 마지막 감지 신체 부위 (TrackingId -> BodyPart)
        /// </summary>
        public Dictionary<int, string> LastDetectedParts { get; set; } = new();
        
        /// <summary>
        /// 마지막 알림 시간
        /// </summary>
        public DateTime LastAlertTime { get; set; } = DateTime.MinValue;
        
        /// <summary>
        /// 구역 진입 시간 추적 (TrackingId -> EntryTime)
        /// </summary>
        public Dictionary<int, DateTime> EntryTimes { get; set; } = new();
        
        /// <summary>
        /// 알림이 발생한 사람들 (TrackingId)
        /// </summary>
        public HashSet<int> AlertedPersons { get; set; } = new();
    }
}