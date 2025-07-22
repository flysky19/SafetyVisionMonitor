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
            // Canvas 내에서의 클릭 위치 가져오기
            var canvas = sender as Canvas;
            if (canvas != null)
            {
                var clickPoint = e.GetPosition(canvas);
                    
                // ViewModel의 메서드 호출
                viewModel.OnCanvasClick(clickPoint);
            }
        }
    }
}