using System;
using System.Collections.Generic;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SafetyVisionMonitor.Models
{
    public partial class Zone3D : ObservableObject
    {
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
        private bool isEnabled = true;
        
        [ObservableProperty]
        private DateTime createdDate = DateTime.Now;
        
        // 바닥면의 4개 점 (사용자가 클릭하는 점들)
        public List<Point2D> FloorPoints { get; set; } = new();
        
        [ObservableProperty]
        private double height = 2.0; // 미터 단위
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