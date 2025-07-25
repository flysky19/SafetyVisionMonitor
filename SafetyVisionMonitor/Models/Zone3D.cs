using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SafetyVisionMonitor.Models
{
    public partial class Zone3D : ObservableObject
    {
        // 데이터베이스 로딩 중에는 자동 저장하지 않도록 하는 플래그
        public bool IsLoading { get; set; } = false;
        
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
            System.Diagnostics.Debug.WriteLine($"Zone3D {Name}: IsEnabled changed to {value}, IsLoading={IsLoading}");
            
            // 로딩 중이 아닐 때만 자동 저장
            if (!IsLoading)
            {
                // 변경사항을 즉시 데이터베이스에 저장하고 다른 ViewModel들에 알림
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await App.DatabaseService.SaveZone3DConfigsAsync(new List<Zone3D> { this });
                        System.Diagnostics.Debug.WriteLine($"Zone {Name} IsEnabled={value} auto-saved to database");
                        
                        // UI 스레드에서 다른 ViewModel들에 구역 상태 변경 알림
                        App.Current.Dispatcher.Invoke(() =>
                        {
                            App.AppData.NotifyZoneUpdated(this);
                            
                            // ZoneSetupViewModel의 시각화도 업데이트
                            App.AppData.NotifyZoneVisualizationUpdate();
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to auto-save zone toggle: {ex.Message}");
                    }
                });
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