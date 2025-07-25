using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SafetyVisionMonitor.ViewModels;

namespace SafetyVisionMonitor.Views;

public partial class ZoneSetupView : UserControl
{
    private bool _isFirstLoad = false;
    
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
    
    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ZoneSetupViewModel viewModel)
        {
            var canvas = sender as Canvas;
            if (canvas != null)
            {
                var clickPoint = e.GetPosition(canvas);
                
                // Mouse Capture 설정 (드래그 이벤트를 위해)
                canvas.CaptureMouse();
                
                // ViewModel의 MouseDown 메서드 호출
                viewModel.OnCanvasMouseDown(clickPoint);
            }
        }
    }
    
    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is ZoneSetupViewModel viewModel)
        {
            var canvas = sender as Canvas;
            if (canvas != null && canvas.IsMouseCaptured)
            {
                var movePoint = e.GetPosition(canvas);
                
                // ViewModel의 MouseMove 메서드 호출
                viewModel.OnCanvasMouseMove(movePoint);
            }
        }
    }
    
    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ZoneSetupViewModel viewModel)
        {
            var canvas = sender as Canvas;
            if (canvas != null)
            {
                var releasePoint = e.GetPosition(canvas);
                
                // Mouse Capture 해제
                canvas.ReleaseMouseCapture();
                
                // ViewModel의 MouseUp 메서드 호출
                viewModel.OnCanvasMouseUp(releasePoint);
            }
        }
    }
}