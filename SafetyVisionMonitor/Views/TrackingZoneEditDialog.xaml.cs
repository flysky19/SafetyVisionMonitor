using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SafetyVisionMonitor.Shared.Models;
using SafetyVisionMonitor.ViewModels;
using Syncfusion.Windows.Shared;

namespace SafetyVisionMonitor.Views
{
    public partial class TrackingZoneEditDialog : Window
    {
        private TrackingZoneEditDialogViewModel _viewModel;
        private bool _isUpdatingPolygon = false;
        private int _retryCount = 0;
        private const int MAX_RETRY_COUNT = 5;
        
        public TrackingZoneEditDialog(TrackingZone zone, List<Camera> cameras)
        {
            InitializeComponent();
            _viewModel = new TrackingZoneEditDialogViewModel(zone, cameras);
            _viewModel.Initialize(this);
            DataContext = _viewModel;
            
            // 폴리곤 업데이트 이벤트 구독
            _viewModel.PolygonUpdateRequested += OnPolygonUpdateRequested;
            
            // 컨테이너 크기 변경 시 폴리곤 업데이트
            VideoContainer.SizeChanged += (s, args) => UpdatePolygonOverlay();
            
            // 이미지 로드 완료 시 폴리곤 업데이트
            CameraImage.Loaded += (s, args) => UpdatePolygonOverlay();
        }
        
        public TrackingZone GetEditedZone()
        {
            return _viewModel.Zone;
        }
        
        private void OnPolygonUpdateRequested(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"TrackingZoneEditDialog: Polygon update requested - Zone: {_viewModel?.Zone?.Id}, CameraFrame: {(_viewModel?.CameraFrame != null ? "Available" : "Null")}");
            
            if (_viewModel?.Zone == null || _viewModel.CameraFrame == null)
            {
                System.Diagnostics.Debug.WriteLine("TrackingZoneEditDialog: Skipping polygon update - missing data");
                return;
            }

            UpdatePolygonOverlay();
        }
        
        private void UpdatePolygonOverlay()
        {
            // UI 스레드에서 실행되도록 보장
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => UpdatePolygonOverlay());
                return;
            }
            
            // 무한 루프 방지 - 이미 업데이트 중이면 건너뛰기
            if (_isUpdatingPolygon)
            {
                System.Diagnostics.Debug.WriteLine("TrackingZoneEditDialog: Skipping polygon update - already in progress");
                return;
            }

            // 기본 검증
            if (_viewModel?.Zone == null)
            {
                System.Diagnostics.Debug.WriteLine("TrackingZoneEditDialog: No zone data available");
                return;
            }

            try
            {
                _isUpdatingPolygon = true;
                
                // Canvas 초기화
                PolygonCanvas.Children.Clear();

                // 점이 없으면 종료
                if (_viewModel.Zone.PolygonPoints == null || _viewModel.Zone.PolygonPoints.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("TrackingZoneEditDialog: No points to draw");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"TrackingZoneEditDialog: Drawing {_viewModel.Zone.PolygonPoints.Count} points for zone {_viewModel.Zone.Id}");

                // Image의 실제 렌더링 크기와 위치 계산
                var imageActualSize = GetImageActualSize();
                if (imageActualSize.Width <= 0 || imageActualSize.Height <= 0)
                {
                    _retryCount++;
                    if (_retryCount <= MAX_RETRY_COUNT)
                    {
                        System.Diagnostics.Debug.WriteLine($"TrackingZoneEditDialog: Image size not ready, retry {_retryCount}/{MAX_RETRY_COUNT}");
                        // 짧은 지연 후 재시도
                        System.Threading.Tasks.Task.Delay(50).ContinueWith(_ => 
                        {
                            Dispatcher.BeginInvoke(() => 
                            {
                                _isUpdatingPolygon = false;
                                UpdatePolygonOverlay();
                            }, System.Windows.Threading.DispatcherPriority.Background);
                        });
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"TrackingZoneEditDialog: Max retry count reached, using default size");
                        _retryCount = 0;
                        // 기본 크기로 설정
                        PolygonCanvas.Width = VideoContainer.ActualWidth;
                        PolygonCanvas.Height = VideoContainer.ActualHeight;
                        PolygonCanvas.Margin = new Thickness(0);
                        imageActualSize = (VideoContainer.ActualWidth, VideoContainer.ActualHeight, new Thickness(0));
                    }
                    return;
                }
                
                _retryCount = 0;

                // Canvas를 Image와 동일한 크기와 위치로 설정
                PolygonCanvas.Width = imageActualSize.Width;
                PolygonCanvas.Height = imageActualSize.Height;
                PolygonCanvas.Margin = imageActualSize.Margin;

                // 백분율 좌표를 Canvas 픽셀 좌표로 변환
                var scaleX = imageActualSize.Width / 100.0;
                var scaleY = imageActualSize.Height / 100.0;

                System.Diagnostics.Debug.WriteLine($"TrackingZoneEditDialog: Canvas size: {imageActualSize.Width}x{imageActualSize.Height}, Scale: {scaleX:F2}x{scaleY:F2}");

                // 3개 이상의 점이 있으면 폴리곤 그리기
                if (_viewModel.Zone.PolygonPoints.Count >= 3)
                {
                    var polygon = new Polygon
                    {
                        Stroke = new SolidColorBrush(Colors.Yellow),
                        StrokeThickness = 2,
                        Fill = new SolidColorBrush(Colors.Yellow) { Opacity = 0.3 },
                        StrokeDashArray = new DoubleCollection { 5, 5 } // 점선 효과
                    };

                    // 백분율 좌표를 Canvas 좌표로 변환
                    foreach (var point in _viewModel.Zone.PolygonPoints)
                    {
                        var canvasX = Math.Max(0, Math.Min(imageActualSize.Width, point.X * scaleX));
                        var canvasY = Math.Max(0, Math.Min(imageActualSize.Height, point.Y * scaleY));
                        polygon.Points.Add(new System.Windows.Point(canvasX, canvasY));
                    }

                    PolygonCanvas.Children.Add(polygon);
                    System.Diagnostics.Debug.WriteLine($"TrackingZoneEditDialog: Added polygon with {polygon.Points.Count} points");
                }
                
                // 모든 점들을 빨간 원으로 그리기 (인덱스 표시 추가)
                for (int i = 0; i < _viewModel.Zone.PolygonPoints.Count; i++)
                {
                    var point = _viewModel.Zone.PolygonPoints[i];
                    var canvasX = Math.Max(0, Math.Min(imageActualSize.Width, point.X * scaleX));
                    var canvasY = Math.Max(0, Math.Min(imageActualSize.Height, point.Y * scaleY));
                    
                    // 점 표시
                    var ellipse = new Ellipse
                    {
                        Width = 12,
                        Height = 12,
                        Fill = new SolidColorBrush(Colors.Red),
                        Stroke = new SolidColorBrush(Colors.White),
                        StrokeThickness = 2
                    };
                    
                    Canvas.SetLeft(ellipse, canvasX - 6);
                    Canvas.SetTop(ellipse, canvasY - 6);
                    PolygonCanvas.Children.Add(ellipse);
                    
                    // 점 번호 표시 (선택적)
                    if (_viewModel.Zone.PolygonPoints.Count <= 10) // 너무 많으면 숫자 표시 안함
                    {
                        var textBlock = new TextBlock
                        {
                            Text = (i + 1).ToString(),
                            Foreground = new SolidColorBrush(Colors.White),
                            FontSize = 10,
                            FontWeight = FontWeights.Bold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        
                        Canvas.SetLeft(textBlock, canvasX - 6);
                        Canvas.SetTop(textBlock, canvasY - 10);
                        PolygonCanvas.Children.Add(textBlock);
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"TrackingZoneEditDialog: 폴리곤 업데이트 완료 - {_viewModel.Zone.PolygonPoints.Count}개 점, Canvas: {PolygonCanvas.Width}x{PolygonCanvas.Height}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TrackingZoneEditDialog: 폴리곤 업데이트 실패 - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"TrackingZoneEditDialog: Stack trace - {ex.StackTrace}");
            }
            finally
            {
                _isUpdatingPolygon = false;
            }
        }
        
        private (double Width, double Height, Thickness Margin) GetImageActualSize()
        {
            if (CameraImage.Source == null)
                return (0, 0, new Thickness());

            var containerWidth = VideoContainer.ActualWidth;
            var containerHeight = VideoContainer.ActualHeight;
            
            if (containerWidth <= 0 || containerHeight <= 0)
                return (0, 0, new Thickness());

            var imageWidth = CameraImage.Source.Width;
            var imageHeight = CameraImage.Source.Height;

            // Uniform 스트레치에서의 실제 렌더링 크기 계산
            var containerAspect = containerWidth / containerHeight;
            var imageAspect = imageWidth / imageHeight;

            double actualWidth, actualHeight;
            double marginLeft = 0, marginTop = 0;

            if (containerAspect > imageAspect)
            {
                // 세로가 꽉 참, 가로에 여백
                actualHeight = containerHeight;
                actualWidth = actualHeight * imageAspect;
                marginLeft = (containerWidth - actualWidth) / 2;
            }
            else
            {
                // 가로가 꽉 함, 세로에 여백
                actualWidth = containerWidth;
                actualHeight = actualWidth / imageAspect;
                marginTop = (containerHeight - actualHeight) / 2;
            }

            return (actualWidth, actualHeight, new Thickness(marginLeft, marginTop, marginLeft, marginTop));
        }
        
        private void PreviewArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 클릭한 위치에 점 추가
            var position = e.GetPosition(PolygonCanvas);
            
            // Canvas 좌표를 백분율 좌표로 변환
            var imageActualSize = GetImageActualSize();
            if (imageActualSize.Width > 0 && imageActualSize.Height > 0)
            {
                // Canvas 내부 클릭인지 확인
                if (position.X >= 0 && position.X <= imageActualSize.Width &&
                    position.Y >= 0 && position.Y <= imageActualSize.Height)
                {
                    var scaleX = 100.0 / imageActualSize.Width;
                    var scaleY = 100.0 / imageActualSize.Height;
                    
                    var x = (float)(position.X * scaleX); // 백분율로 저장
                    var y = (float)(position.Y * scaleY);
                    
                    System.Diagnostics.Debug.WriteLine($"TrackingZoneEditDialog: Adding point at ({x:F1}%, {y:F1}%) from canvas click ({position.X:F1}, {position.Y:F1})");
                    
                    _viewModel.AddPoint(new PointF(x, y));
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"TrackingZoneEditDialog: Click outside image area - Canvas: ({position.X:F1}, {position.Y:F1}), Image: {imageActualSize.Width}x{imageActualSize.Height}");
                }
            }
        }
    }
}