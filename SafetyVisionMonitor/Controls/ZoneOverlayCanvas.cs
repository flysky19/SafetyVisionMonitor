using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SafetyVisionMonitor.Shared.Models;
using SafetyVisionMonitor.Services;

namespace SafetyVisionMonitor.Controls
{
    /// <summary>
    /// 구역 오버레이를 효율적으로 그리는 커스텀 Canvas
    /// 벡터 기반으로 부드러운 렌더링과 정확한 좌표 처리
    /// </summary>
    public class ZoneOverlayCanvas : Canvas
    {
        private CoordinateMapper? _coordinateMapper;
        private readonly Dictionary<string, Polygon> _zonePolygons = new();
        private readonly Dictionary<string, TextBlock> _zoneLabels = new();
        private Polygon? _tempDrawingPolygon;
        private Polyline? _tempDrawingOutline;
        private readonly List<UIElement> _tempDrawingPoints = new();

        #region Dependency Properties

        public static readonly DependencyProperty ZonesProperty =
            DependencyProperty.Register(nameof(Zones), typeof(ObservableCollection<ZoneVisualization>), 
                typeof(ZoneOverlayCanvas), new PropertyMetadata(null, OnZonesChanged));

        public static readonly DependencyProperty ImageWidthProperty =
            DependencyProperty.Register(nameof(ImageWidth), typeof(double), 
                typeof(ZoneOverlayCanvas), new PropertyMetadata(640.0, OnImageSizeChanged));

        public static readonly DependencyProperty ImageHeightProperty =
            DependencyProperty.Register(nameof(ImageHeight), typeof(double), 
                typeof(ZoneOverlayCanvas), new PropertyMetadata(480.0, OnImageSizeChanged));

        public static readonly DependencyProperty IsDrawingModeProperty =
            DependencyProperty.Register(nameof(IsDrawingMode), typeof(bool), 
                typeof(ZoneOverlayCanvas), new PropertyMetadata(false, OnDrawingModeChanged));

        public static readonly DependencyProperty TempDrawingPointsProperty =
            DependencyProperty.Register(nameof(TempDrawingPoints), typeof(ObservableCollection<Point>), 
                typeof(ZoneOverlayCanvas), new PropertyMetadata(null, OnTempDrawingPointsChanged));


        public static readonly DependencyProperty NewZoneHeightProperty =
            DependencyProperty.Register(nameof(NewZoneHeight), typeof(double), 
                typeof(ZoneOverlayCanvas), new PropertyMetadata(2.0, OnNewZoneHeightChanged));

        #endregion

        #region Properties

        public ObservableCollection<ZoneVisualization>? Zones
        {
            get => (ObservableCollection<ZoneVisualization>?)GetValue(ZonesProperty);
            set => SetValue(ZonesProperty, value);
        }

        public double ImageWidth
        {
            get => (double)GetValue(ImageWidthProperty);
            set => SetValue(ImageWidthProperty, value);
        }

        public double ImageHeight
        {
            get => (double)GetValue(ImageHeightProperty);
            set => SetValue(ImageHeightProperty, value);
        }

        public bool IsDrawingMode
        {
            get => (bool)GetValue(IsDrawingModeProperty);
            set => SetValue(IsDrawingModeProperty, value);
        }

        public ObservableCollection<Point>? TempDrawingPoints
        {
            get => (ObservableCollection<Point>?)GetValue(TempDrawingPointsProperty);
            set => SetValue(TempDrawingPointsProperty, value);
        }


        public double NewZoneHeight
        {
            get => (double)GetValue(NewZoneHeightProperty);
            set => SetValue(NewZoneHeightProperty, value);
        }

        #endregion

        #region Constructor

        public ZoneOverlayCanvas()
        {
            ClipToBounds = true;
            Background = Brushes.Transparent; // 마우스 이벤트 캡처용
            
            // 초기 좌표 매퍼 생성
            _coordinateMapper = new CoordinateMapper(ImageWidth, ImageHeight);
            
            // 초기 이벤트 구독 설정
            Loaded += OnLoaded;
        }
        
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // TempDrawingPoints가 이미 설정되어 있다면 이벤트 구독
            if (TempDrawingPoints != null)
            {
                TempDrawingPoints.CollectionChanged += OnTempDrawingPointsCollectionChanged;
            }
            
            
            if (Zones != null)
            {
                Zones.CollectionChanged += OnZonesCollectionChanged;
            }
        }

        #endregion

        #region Overrides

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            
            if (_coordinateMapper != null && ActualWidth > 0 && ActualHeight > 0)
            {
                _coordinateMapper.UpdateCanvasSize(ActualWidth, ActualHeight);
                UpdateAllZones();
                UpdateTempDrawing();
            }
        }

        #endregion

        #region Property Changed Handlers

        private static void OnZonesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ZoneOverlayCanvas canvas)
            {
                // 이전 컬렉션의 이벤트 구독 해제
                if (e.OldValue is ObservableCollection<ZoneVisualization> oldCollection)
                {
                    oldCollection.CollectionChanged -= canvas.OnZonesCollectionChanged;
                }
                
                // 새 컬렉션의 이벤트 구독
                if (e.NewValue is ObservableCollection<ZoneVisualization> newCollection)
                {
                    newCollection.CollectionChanged += canvas.OnZonesCollectionChanged;
                }
                
                canvas.UpdateAllZones();
            }
        }
        
        private void OnZonesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Zones collection changed: {e.Action}");
            UpdateAllZones();
        }

        private static void OnImageSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ZoneOverlayCanvas canvas)
            {
                canvas._coordinateMapper = new CoordinateMapper(canvas.ImageWidth, canvas.ImageHeight);
                if (canvas.ActualWidth > 0 && canvas.ActualHeight > 0)
                {
                    canvas._coordinateMapper.UpdateCanvasSize(canvas.ActualWidth, canvas.ActualHeight);
                    canvas.UpdateAllZones();
                }
            }
        }

        private static void OnDrawingModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ZoneOverlayCanvas canvas)
            {
                canvas.UpdateDrawingMode();
            }
        }

        private static void OnTempDrawingPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ZoneOverlayCanvas canvas)
            {
                // 이전 컬렉션의 이벤트 구독 해제
                if (e.OldValue is ObservableCollection<Point> oldCollection)
                {
                    oldCollection.CollectionChanged -= canvas.OnTempDrawingPointsCollectionChanged;
                }
                
                // 새 컬렉션의 이벤트 구독
                if (e.NewValue is ObservableCollection<Point> newCollection)
                {
                    newCollection.CollectionChanged += canvas.OnTempDrawingPointsCollectionChanged;
                }
                
                canvas.UpdateTempDrawing();
            }
        }
        
        private void OnTempDrawingPointsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdateTempDrawing();
        }


        private static void OnNewZoneHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ZoneOverlayCanvas canvas)
            {
                canvas.UpdateTempDrawing();
            }
        }

        #endregion

        #region Zone Management

        private void UpdateAllZones()
        {
            // 기존 구역 폴리곤 제거
            foreach (var polygon in _zonePolygons.Values)
            {
                Children.Remove(polygon);
            }
            foreach (var label in _zoneLabels.Values)
            {
                Children.Remove(label);
            }
            _zonePolygons.Clear();
            _zoneLabels.Clear();

            if (Zones == null || _coordinateMapper == null) return;

            // 새로운 구역 폴리곤 생성
            foreach (var zone in Zones.Where(z => z.IsEnabled))
            {
                CreateZonePolygon(zone);
            }
        }

        private void CreateZonePolygon(ZoneVisualization zone)
        {
            if (zone.RelativePoints.Count < 3 || _coordinateMapper == null) return;

            // 폴리곤 생성
            var polygon = new Polygon
            {
                Fill = new SolidColorBrush(zone.ZoneColor) { Opacity = zone.Opacity },
                Stroke = new SolidColorBrush(zone.ZoneColor),
                StrokeThickness = 2,
                Tag = zone.ZoneId
            };

            // 상대 좌표를 캔버스 좌표로 변환
            foreach (var relativePoint in zone.RelativePoints)
            {
                var canvasPoint = _coordinateMapper.RelativeToCanvas(relativePoint);
                polygon.Points.Add(canvasPoint);
            }

            _zonePolygons[zone.ZoneId] = polygon;
            Children.Add(polygon);

            // 라벨 생성
            if (zone.RelativePoints.Count > 0)
            {
                var centerRelative = new Point(
                    zone.RelativePoints.Average(p => p.X),
                    zone.RelativePoints.Average(p => p.Y)
                );
                var centerCanvas = _coordinateMapper.RelativeToCanvas(centerRelative);

                var labelText = zone.Name;
                // 높이 정보가 있으면 표시
                if (zone.Height > 0)
                {
                    labelText += $" ({zone.Height:F1}m)";
                }
                
                var label = new TextBlock
                {
                    Text = labelText,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    Background = new SolidColorBrush(Colors.Black) { Opacity = 0.5 },
                    Padding = new Thickness(4, 2, 4, 2),
                    Tag = zone.ZoneId
                };

                Canvas.SetLeft(label, centerCanvas.X - 30);
                Canvas.SetTop(label, centerCanvas.Y - 10);

                _zoneLabels[zone.ZoneId] = label;
                Children.Add(label);
            }
        }

        #endregion

        #region Drawing Mode

        private void UpdateDrawingMode()
        {
            if (!IsDrawingMode)
            {
                ClearTempDrawing();
            }
        }

        private void UpdateTempDrawing()
        {
            ClearTempDrawing();

            if (!IsDrawingMode || TempDrawingPoints == null || _coordinateMapper == null) 
                return;

            System.Diagnostics.Debug.WriteLine($"UpdateTempDrawing: {TempDrawingPoints.Count} points");

            if (TempDrawingPoints.Count >= 2)
            {
                // 임시 폴리곤 생성
                _tempDrawingPolygon = new Polygon
                {
                    Fill = new SolidColorBrush(Colors.Yellow) { Opacity = 0.2 },
                    Stroke = Brushes.Yellow,
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 5, 3 }
                };

                // 이미지 좌표를 캔버스 좌표로 변환
                foreach (var imagePoint in TempDrawingPoints)
                {
                    var canvasPoint = _coordinateMapper.ImageToCanvas(imagePoint);
                    _tempDrawingPolygon.Points.Add(canvasPoint);
                    System.Diagnostics.Debug.WriteLine($"  Point: Image({imagePoint.X:F1}, {imagePoint.Y:F1}) -> Canvas({canvasPoint.X:F1}, {canvasPoint.Y:F1})");
                }
                
                // 4개 점이 있으면 닫힌 다각형 만들기
                if (TempDrawingPoints.Count == 4 && _tempDrawingPolygon.Points.Count == 4)
                {
                    _tempDrawingPolygon.Points.Add(_tempDrawingPolygon.Points[0]);
                }

                // ZIndex 설정하여 위에 표시
                Canvas.SetZIndex(_tempDrawingPolygon, 100);
                Children.Add(_tempDrawingPolygon);

                // 외곽선 (아웃라인) 생성
                _tempDrawingOutline = new Polyline
                {
                    Stroke = Brushes.White,
                    StrokeThickness = 1,
                    Points = _tempDrawingPolygon.Points
                };
                Canvas.SetZIndex(_tempDrawingOutline, 101);
                Children.Add(_tempDrawingOutline);
            }

            // 포인트 마커 생성
            foreach (var imagePoint in TempDrawingPoints)
            {
                var canvasPoint = _coordinateMapper.ImageToCanvas(imagePoint);
                var marker = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = Brushes.Yellow,
                    Stroke = Brushes.Black,
                    StrokeThickness = 2
                };

                Canvas.SetLeft(marker, canvasPoint.X - 5);
                Canvas.SetTop(marker, canvasPoint.Y - 5);
                Canvas.SetZIndex(marker, 102);

                _tempDrawingPoints.Add(marker);
                Children.Add(marker);
            }
            
            // 높이 정보 표시
            if (TempDrawingPoints.Count >= 2)
            {
                AddHeightVisualization();
            }
        }

        private void ClearTempDrawing()
        {
            if (_tempDrawingPolygon != null)
            {
                Children.Remove(_tempDrawingPolygon);
                _tempDrawingPolygon = null;
            }

            if (_tempDrawingOutline != null)
            {
                Children.Remove(_tempDrawingOutline);
                _tempDrawingOutline = null;
            }

            foreach (var point in _tempDrawingPoints)
            {
                Children.Remove(point);
            }
            _tempDrawingPoints.Clear();
        }

        /// <summary>
        /// 높이 시각화 추가 (텍스트 + 3D 효과)
        /// </summary>
        private void AddHeightVisualization()
        {
            if (TempDrawingPoints.Count < 2 || _coordinateMapper == null) return;
            
            // 중심점 계산
            var centerX = TempDrawingPoints.Average(p => p.X);
            var centerY = TempDrawingPoints.Average(p => p.Y);
            var centerImage = new Point(centerX, centerY);
            var centerCanvas = _coordinateMapper.ImageToCanvas(centerImage);
            
            // 높이 텍스트 표시
            var heightText = new TextBlock
            {
                Text = $"높이: {NewZoneHeight:F1}m",
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Colors.Black) { Opacity = 0.7 },
                Padding = new Thickness(6, 3, 6, 3),
                FontWeight = FontWeights.Bold,
                FontSize = 12
            };
            
            Canvas.SetLeft(heightText, centerCanvas.X - 30);
            Canvas.SetTop(heightText, centerCanvas.Y - 35);
            Canvas.SetZIndex(heightText, 103);
            
            _tempDrawingPoints.Add(heightText);
            Children.Add(heightText);
            
            // 3D 효과 (그림자)를 위한 오프셋 폴리곤
            if (_tempDrawingPolygon != null && NewZoneHeight > 0)
            {
                var shadowOffset = Math.Max(3, NewZoneHeight * 2); // 높이에 비례한 오프셋
                
                var shadowPolygon = new Polygon
                {
                    Fill = new SolidColorBrush(Colors.Gray) { Opacity = 0.3 },
                    Stroke = new SolidColorBrush(Colors.DarkGray) { Opacity = 0.5 },
                    StrokeThickness = 1
                };
                
                // 원본 폴리곤의 모든 점을 오프셋하여 그림자 효과
                foreach (var point in _tempDrawingPolygon.Points)
                {
                    shadowPolygon.Points.Add(new Point(point.X + shadowOffset, point.Y - shadowOffset));
                }
                
                Canvas.SetZIndex(shadowPolygon, 99); // 원본 폴리곤보다 뒤에
                _tempDrawingPoints.Add(shadowPolygon);
                Children.Add(shadowPolygon);
            }
        }
        
        #endregion


        #region Public Methods

        /// <summary>
        /// 캔버스 좌표를 이미지 좌표로 변환
        /// </summary>
        public Point CanvasToImage(Point canvasPoint)
        {
            return _coordinateMapper?.CanvasToImage(canvasPoint) ?? canvasPoint;
        }

        /// <summary>
        /// 캔버스 좌표가 이미지 영역 내에 있는지 확인
        /// </summary>
        public bool IsPointInImageArea(Point canvasPoint)
        {
            return _coordinateMapper?.IsPointInRenderArea(canvasPoint) ?? false;
        }


        #endregion
    }
}