using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using SafetyVisionMonitor.Models;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 안전 감시 서비스 - 3D 구역과 검출된 객체의 교차 검사
    /// </summary>
    public class SafetyDetectionService : IDisposable
    {
        private readonly DatabaseService _databaseService;
        private readonly Dictionary<string, List<Zone3D>> _cameraZones = new();
        private readonly Dictionary<string, DateTime> _lastAlertTime = new();
        private readonly TimeSpan _alertCooldown = TimeSpan.FromSeconds(5); // 5초 쿨다운
        
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
            
            // 초기 구역 데이터 로드
            _ = Task.Run(LoadZoneDataAsync);
            
            System.Diagnostics.Debug.WriteLine("SafetyDetectionService: Initialized");
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
        public async Task<SafetyCheckResult> CheckSafetyAsync(string cameraId, Models.DetectionResult[] detections)
        {
            if (detections.Length == 0 || !_cameraZones.ContainsKey(cameraId))
            {
                return new SafetyCheckResult { CameraId = cameraId };
            }
            
            var result = new SafetyCheckResult { CameraId = cameraId };
            var zones = _cameraZones[cameraId];
            
            try
            {
                foreach (var detection in detections)
                {
                    // 사람만 체크 (필요시 다른 객체도 확장 가능)
                    if (detection.ClassName != "person")
                        continue;
                    
                    var personViolations = new List<ZoneViolation>();
                    
                    foreach (var zone in zones)
                    {
                        if (IsPersonInZone(detection, zone))
                        {
                            var violation = new ZoneViolation
                            {
                                Detection = detection,
                                Zone = zone,
                                ViolationType = GetViolationType(zone.Type),
                                Timestamp = DateTime.Now,
                                Confidence = detection.Confidence
                            };
                            
                            personViolations.Add(violation);
                            result.Violations.Add(violation);
                            
                            // 알림 쿨다운 체크
                            var alertKey = $"{cameraId}_{zone.Id}";
                            if (ShouldTriggerAlert(alertKey))
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
        /// 사람이 특정 구역 안에 있는지 검사
        /// </summary>
        private bool IsPersonInZone(Models.DetectionResult detection, Zone3D zone)
        {
            if (zone.FloorPoints.Count < 3)
                return false;
            
            try
            {
                // 검출된 사람의 바운딩 박스에서 발 위치 추정 (하단 중앙)
                var personFootX = detection.BoundingBox.X + detection.BoundingBox.Width / 2;
                var personFootY = detection.BoundingBox.Y + detection.BoundingBox.Height; // 바닥
                
                // 스크린 좌표를 월드 좌표로 변환
                var worldPoint = CoordinateTransformService.ScreenToWorld(
                    new System.Windows.Point(personFootX, personFootY),
                    zone.CalibrationFrameWidth,
                    zone.CalibrationFrameHeight,
                    zone.CalibrationPixelsPerMeter
                );
                
                // Point-in-Polygon 알고리즘 (Ray Casting)
                return IsPointInPolygon(worldPoint, zone.FloorPoints);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SafetyDetectionService: Zone intersection check error: {ex.Message}");
                return false;
            }
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
        /// 알림 쿨다운 체크
        /// </summary>
        private bool ShouldTriggerAlert(string alertKey)
        {
            var now = DateTime.Now;
            
            if (!_lastAlertTime.ContainsKey(alertKey))
            {
                _lastAlertTime[alertKey] = now;
                return true;
            }
            
            if (now - _lastAlertTime[alertKey] >= _alertCooldown)
            {
                _lastAlertTime[alertKey] = now;
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// 안전 알림 발생
        /// </summary>
        private async Task TriggerSafetyAlert(ZoneViolation violation)
        {
            try
            {
                // 안전 이벤트 생성
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
                
                // 데이터베이스에 저장
                await _databaseService.SaveSafetyEventAsync(safetyEvent);
                TotalSafetyEventsGenerated++;
                
                // 이벤트 발생
                SafetyEventDetected?.Invoke(this, new SafetyEventArgs
                {
                    SafetyEvent = safetyEvent,
                    Violation = violation
                });
                
                System.Diagnostics.Debug.WriteLine(
                    $"SafetyDetectionService: Safety alert triggered - {violation.ViolationType} " +
                    $"in {violation.Zone.Name} (Camera: {violation.Detection.CameraId})");
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
            return $"작업자가 {zoneTypeName} '{violation.Zone.Name}'에 진입했습니다. " +
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
        
        public void Dispose()
        {
            if (_disposed) return;
            
            _cameraZones.Clear();
            _lastAlertTime.Clear();
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
        public List<Models.DetectionResult> ViolatingPersons { get; set; } = new();
        public List<Models.DetectionResult> SafePersons { get; set; } = new();
        
        public bool HasViolations => Violations.Count > 0;
        public int TotalPersons => ViolatingPersons.Count + SafePersons.Count;
    }
    
    /// <summary>
    /// 구역 위반 정보
    /// </summary>
    public class ZoneViolation
    {
        public Models.DetectionResult Detection { get; set; } = new();
        public Zone3D Zone { get; set; } = new();
        public ViolationType ViolationType { get; set; }
        public DateTime Timestamp { get; set; }
        public float Confidence { get; set; }
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
}