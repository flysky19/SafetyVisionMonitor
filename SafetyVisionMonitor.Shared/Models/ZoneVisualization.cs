using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SafetyVisionMonitor.Shared.Models
{
    public class ZoneVisualization : ObservableObject
    {
        public string ZoneId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        
        private ObservableCollection<Point> _screenPoints = new();
        public ObservableCollection<Point> ScreenPoints 
        { 
            get => _screenPoints;
            set 
            { 
                SetProperty(ref _screenPoints, value);
                OnPropertyChanged(nameof(PointCollection));
            }
        }
        
        private Color _zoneColor;
        public Color ZoneColor 
        { 
            get => _zoneColor; 
            set => SetProperty(ref _zoneColor, value); 
        }
        
        private double _opacity;
        public double Opacity 
        { 
            get => _opacity; 
            set => SetProperty(ref _opacity, value); 
        }
        
        private bool _isSelected;
        public bool IsSelected 
        { 
            get => _isSelected; 
            set => SetProperty(ref _isSelected, value); 
        }
        
        private bool _isEnabled;
        public bool IsEnabled 
        { 
            get => _isEnabled; 
            set => SetProperty(ref _isEnabled, value); 
        }
        
        private double _height;
        public double Height 
        { 
            get => _height; 
            set => SetProperty(ref _height, value); 
        }
        
        // 상대 좌표 (0~1 범위)
        private ObservableCollection<Point> _relativePoints = new();
        public ObservableCollection<Point> RelativePoints 
        { 
            get => _relativePoints;
            set 
            { 
                SetProperty(ref _relativePoints, value);
                OnPropertyChanged(nameof(RelativePointCollection));
            }
        }
        
        // WPF Polygon에서 사용할 수 있는 PointCollection 반환 (절대 좌표)
        public PointCollection PointCollection
        {
            get
            {
                var pointCollection = new PointCollection();
                foreach (var point in ScreenPoints)
                {
                    pointCollection.Add(point);
                }
                System.Diagnostics.Debug.WriteLine($"PointCollection created with {pointCollection.Count} points for zone {Name}");
                return pointCollection;
            }
        }
        
        // 상대 좌표 PointCollection
        public PointCollection RelativePointCollection
        {
            get
            {
                var pointCollection = new PointCollection();
                foreach (var point in RelativePoints)
                {
                    pointCollection.Add(point);
                }
                return pointCollection;
            }
        }
        
        // 점을 추가할 때 PointCollection도 업데이트되도록 알림
        public void AddPoint(Point point)
        {
            ScreenPoints.Add(point);
            OnPropertyChanged(nameof(PointCollection));
        }
        
        // 상대 좌표 점 추가
        public void AddRelativePoint(Point relativePoint)
        {
            RelativePoints.Add(relativePoint);
            OnPropertyChanged(nameof(RelativePointCollection));
        }
    }
}