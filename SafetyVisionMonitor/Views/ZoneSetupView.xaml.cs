using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SafetyVisionMonitor.ViewModels;
using SafetyVisionMonitor.Controls;

namespace SafetyVisionMonitor.Views;

public partial class ZoneSetupView : UserControl
{
    private bool _isFirstLoad = true;
    
    public ZoneSetupView()
    {
        InitializeComponent();
    }
    
    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_isFirstLoad && DataContext is ZoneSetupViewModel viewModel)
        {
            viewModel.OnLoaded();
            _isFirstLoad = false;
        }
    }
    
    private void VideoContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 비디오 컨테이너 크기가 변경되면 ViewModel에 알림
        if (DataContext is ZoneSetupViewModel viewModel)
        {
            viewModel.UpdateCanvasSize(VideoContainer.ActualWidth, VideoContainer.ActualHeight);
        }
    }
    
    private void ZoneOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ZoneSetupViewModel viewModel)
        {
            var overlay = sender as ZoneOverlayCanvas;
            if (overlay != null)
            {
                var canvasPoint = e.GetPosition(overlay);
                
                // 이미지 영역 내인지 확인
                if (!overlay.IsPointInImageArea(canvasPoint))
                    return;
                
                
                // Mouse Capture 설정 (드래그 이벤트를 위해)
                overlay.CaptureMouse();
                
                // 캔버스 좌표를 이미지 좌표로 변환
                var imagePoint = overlay.CanvasToImage(canvasPoint);
                
                // ViewModel의 MouseDown 메서드 호출
                viewModel.OnCanvasMouseDown(imagePoint);
            }
        }
    }
    
    private void ZoneOverlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is ZoneSetupViewModel viewModel)
        {
            var overlay = sender as ZoneOverlayCanvas;
            if (overlay != null && overlay.IsMouseCaptured)
            {
                var canvasPoint = e.GetPosition(overlay);
                
                
                // 캔버스 좌표를 이미지 좌표로 변환
                var imagePoint = overlay.CanvasToImage(canvasPoint);
                
                // ViewModel의 MouseMove 메서드 호출
                viewModel.OnCanvasMouseMove(imagePoint);
            }
        }
    }
    
    private void ZoneOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ZoneSetupViewModel viewModel)
        {
            var overlay = sender as ZoneOverlayCanvas;
            if (overlay != null)
            {
                var canvasPoint = e.GetPosition(overlay);
                
                // Mouse Capture 해제
                overlay.ReleaseMouseCapture();
                
                // 캔버스 좌표를 이미지 좌표로 변환
                var imagePoint = overlay.CanvasToImage(canvasPoint);
                
                // ViewModel의 MouseUp 메서드 호출
                viewModel.OnCanvasMouseUp(imagePoint);
            }
        }
    }
}