using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SafetyVisionMonitor.ViewModels;

namespace SafetyVisionMonitor.Views
{
    /// <summary>
    /// AcrylicSetupView.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class MonitoringZoneSetupView : UserControl
    {
        private AcrylicSetupViewModel? _viewModel;
        private bool _isUpdatingBoundary = false; // 무한 루프 방지 플래그
        private int _retryCount = 0; // 재시도 횟수 제한
        private const int MAX_RETRY_COUNT = 5; // 최대 재시도 횟수

        public MonitoringZoneSetupView()
        {
            InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel = DataContext as AcrylicSetupViewModel;
            if (_viewModel != null)
            {
                _viewModel.BoundaryUpdateRequested += OnBoundaryUpdateRequested;
                _viewModel.LoadDataAsync();
                
                // 컨테이너 크기 변경 시 경계선 업데이트
                VideoContainer.SizeChanged += (s, args) => UpdateBoundaryOverlay();
                
                // 이미지 로드 완료 시 경계선 업데이트
                CameraImage.Loaded += (s, args) => UpdateBoundaryOverlay();
            }
        }

        private void OnBoundaryUpdateRequested(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"AcrylicSetupView: Boundary update requested - SelectedCamera: {_viewModel?.SelectedCamera?.Id}, PreviewImage: {(_viewModel?.PreviewImage != null ? "Available" : "Null")}");
            
            if (_viewModel?.SelectedCamera == null || _viewModel.PreviewImage == null)
            {
                System.Diagnostics.Debug.WriteLine("AcrylicSetupView: Skipping boundary update - missing data");
                return;
            }

            UpdateBoundaryOverlay();
        }

        private void UpdateBoundaryOverlay()
        {
            // UI 스레드에서 실행되도록 보장
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => UpdateBoundaryOverlay());
                return;
            }
            
            // 무한 루프 방지 - 이미 업데이트 중이면 건너뛰기
            if (_isUpdatingBoundary)
            {
                System.Diagnostics.Debug.WriteLine("AcrylicSetupView: Skipping boundary update - already in progress");
                return;
            }

            try
            {
                _isUpdatingBoundary = true;
                BoundaryCanvas.Children.Clear();

                if (_viewModel?.SelectedCamera?.BoundaryPoints?.Count > 2)
                {
                    System.Diagnostics.Debug.WriteLine($"AcrylicSetupView: Drawing boundary for camera {_viewModel.SelectedCamera.Id} with {_viewModel.SelectedCamera.BoundaryPoints.Count} points");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"AcrylicSetupView: No boundary to draw - Camera: {_viewModel?.SelectedCamera?.Id}, Points: {_viewModel?.SelectedCamera?.BoundaryPoints?.Count}");
                }

                if (_viewModel?.SelectedCamera?.BoundaryPoints?.Count > 2)
                {
                    // Image의 실제 렌더링 크기와 위치 계산
                    var imageActualSize = GetImageActualSize();
                    if (imageActualSize.Width <= 0 || imageActualSize.Height <= 0)
                    {
                        _retryCount++;
                        if (_retryCount <= MAX_RETRY_COUNT)
                        {
                            System.Diagnostics.Debug.WriteLine($"AcrylicSetupView: Image size not ready, retry {_retryCount}/{MAX_RETRY_COUNT}");
                            // 100ms 지연 후 재시도 (무한 루프 방지)
                            Dispatcher.BeginInvoke(() => 
                            {
                                _isUpdatingBoundary = false; // 플래그 해제 후 재시도
                                UpdateBoundaryOverlay();
                            }, System.Windows.Threading.DispatcherPriority.Background);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"AcrylicSetupView: Max retry count reached, skipping boundary update");
                            _retryCount = 0; // 카운터 리셋
                        }
                        return;
                    }
                    
                    _retryCount = 0; // 성공적으로 크기를 얻었으므로 카운터 리셋

                    // Canvas를 Image와 동일한 크기와 위치로 설정
                    BoundaryCanvas.Width = imageActualSize.Width;
                    BoundaryCanvas.Height = imageActualSize.Height;
                    BoundaryCanvas.Margin = imageActualSize.Margin;

                    // 실제 카메라 해상도 대비 렌더링 크기의 스케일 계산
                    var actualWidth = _viewModel.SelectedCamera.ActualWidth;
                    var actualHeight = _viewModel.SelectedCamera.ActualHeight;
                    
                    if (actualWidth <= 0 || actualHeight <= 0)
                        return;

                    var scaleX = imageActualSize.Width / actualWidth;
                    var scaleY = imageActualSize.Height / actualHeight;

                    var polygon = new Polygon
                    {
                        Stroke = _viewModel.BoundaryColor,
                        StrokeThickness = _viewModel.BoundaryThickness,
                        StrokeDashArray = new DoubleCollection { 5, 5 },
                        Fill = new SolidColorBrush(Colors.Yellow) { Opacity = 0.1 },
                        Opacity = _viewModel.BoundaryOpacity
                    };

                    // 실제 좌표를 Canvas 좌표로 변환
                    foreach (var point in _viewModel.SelectedCamera.BoundaryPoints)
                    {
                        var canvasX = point.X * scaleX;
                        var canvasY = point.Y * scaleY;
                        polygon.Points.Add(new Point(canvasX, canvasY));
                    }

                    BoundaryCanvas.Children.Add(polygon);
                    
                    System.Diagnostics.Debug.WriteLine($"AcrylicSetupView: 경계선 업데이트 완료 - {polygon.Points.Count}개 점, Canvas크기: {BoundaryCanvas.Width}x{BoundaryCanvas.Height}, 카메라: {_viewModel.SelectedCamera.Id}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"경계선 업데이트 실패: {ex.Message}");
            }
            finally
            {
                _isUpdatingBoundary = false; // 항상 플래그 해제
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
                // 가로가 꽉 참, 세로에 여백
                actualWidth = containerWidth;
                actualHeight = actualWidth / imageAspect;
                marginTop = (containerHeight - actualHeight) / 2;
            }

            return (actualWidth, actualHeight, new Thickness(marginLeft, marginTop, marginLeft, marginTop));
        }

        private void CameraItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && 
                element.DataContext is CameraAcrylicInfo camera && 
                _viewModel != null)
            {
                _viewModel.SelectCameraCommand.Execute(camera);
            }
        }
    }
}