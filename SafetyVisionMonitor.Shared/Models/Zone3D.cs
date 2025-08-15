using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SafetyVisionMonitor.Shared.Services;

namespace SafetyVisionMonitor.Shared.Models
{
    public partial class Zone3D : ObservableObject
    {
        // 데이터베이스 로딩 중에는 자동 저장하지 않도록 하는 플래그
        public bool IsLoading { get; set; } = false;
        
        // 이벤트 억제 플래그 (무한 루프 방지)
        private bool _suppressEvents = false;
        
        // 디바운싱을 위한 타이머
        private static readonly Dictionary<string, System.Threading.Timer> _debounceTimers = new();
        private static readonly object _timerLock = new();
        
        // 의존성 주입된 서비스들
        public static IZoneDatabaseService? DatabaseService { get; set; }
        public static IZoneNotificationService? NotificationService { get; set; }
        
        public Zone3D()
        {
            System.Diagnostics.Debug.WriteLine($"Zone3D constructor called: Name will be '{Name}', Initial IsEnabled={IsEnabled}");
        }
        
        [ObservableProperty]
        private string id = Guid.NewGuid().ToString();
        
        [ObservableProperty]
        private string name = "새 구역";
        
        [ObservableProperty]
        private ZoneType type = ZoneType.Warning;
        
        [ObservableProperty]
        private string cameraId = string.Empty;
        
        // 3D 박스의 8개 꼭지점
        public List<Point3D> Vertices { get; set; } = new();
        
        // 2D 투영 점들 (화면에 그리기용)
        public List<Point2D> ProjectedPoints { get; set; } = new();
        
        [ObservableProperty]
        private Color displayColor = Colors.Yellow;
        
        [ObservableProperty]
        private double opacity = 0.3;
        
        [ObservableProperty]
        private bool isEnabled = false;
        
        [ObservableProperty]
        private DateTime createdDate = DateTime.Now;
        
        // 바닥면의 4개 점 (사용자가 클릭하는 점들)
        public List<Point2D> FloorPoints { get; set; } = new();
        
        [ObservableProperty]
        private double height = 2.0; // 미터 단위
        
        // 캘리브레이션 정보 (좌표 변환에 필요)
        [ObservableProperty]
        private double calibrationPixelsPerMeter = 100.0;
        
        [ObservableProperty]
        private double calibrationFrameWidth = 640.0;
        
        [ObservableProperty]
        private double calibrationFrameHeight = 480.0;
        
        // IsEnabled 속성 변경 감지
        partial void OnIsEnabledChanged(bool value)
        {
            System.Diagnostics.Debug.WriteLine($"Zone3D {Name}: IsEnabled changed to {value}, IsLoading={IsLoading}, SuppressEvents={_suppressEvents}");
            
            // 로딩 중이거나 이벤트 억제 중일 때는 자동 저장하지 않음 (무한 루프 방지)
            if (IsLoading || _suppressEvents)
            {
                System.Diagnostics.Debug.WriteLine($"Zone3D {Name}: Event suppressed - IsLoading={IsLoading}, SuppressEvents={_suppressEvents}");
                return;
            }
            
            // 이벤트 억제 플래그 설정
            _suppressEvents = true;
            
            try
            {
                // 즉시 알림 (UI 반응성 보장)
                NotificationService?.NotifyZoneUpdated(this);
                NotificationService?.NotifyZoneVisualizationUpdate();
                
                // 디바운싱된 데이터베이스 저장 (500ms 지연)
                ScheduleDebouncedSave();
            }
            catch
            {
                // 예외 발생 시에도 이벤트 억제 해제
                _suppressEvents = false;
            }
            
            // 이벤트 억제 해제는 즉시 수행
            _suppressEvents = false;
        }
        
        private void ScheduleDebouncedSave()
        {
            lock (_timerLock)
            {
                // 기존 타이머가 있으면 제거
                if (_debounceTimers.TryGetValue(Id, out var existingTimer))
                {
                    existingTimer.Dispose();
                }
                
                // 새 타이머 생성 (500ms 후 저장)
                _debounceTimers[Id] = new System.Threading.Timer(async _ =>
                {
                    try
                    {
                        if (DatabaseService != null)
                        {
                            await DatabaseService.SaveZone3DConfigsAsync(new List<Zone3D> { this });
                            System.Diagnostics.Debug.WriteLine($"Zone {Name} debounced save completed");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Debounced save failed for zone {Name}: {ex.Message}");
                    }
                    finally
                    {
                        // 타이머 정리
                        lock (_timerLock)
                        {
                            if (_debounceTimers.TryGetValue(Id, out var timer))
                            {
                                timer.Dispose();
                                _debounceTimers.Remove(Id);
                            }
                        }
                    }
                }, null, 500, Timeout.Infinite); // 500ms 후 1회 실행
            }
        }
    }
    
    public enum ZoneType
    {
        Warning,  // 경고 구역
        Danger    // 위험 구역
    }
    
    public struct Point3D
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        
        public Point3D(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
    
    public struct Point2D
    {
        public double X { get; set; }
        public double Y { get; set; }
        
        public Point2D(double x, double y)
        {
            X = x;
            Y = y;
        }
    }
}