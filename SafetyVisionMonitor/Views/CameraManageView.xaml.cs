using System.Windows.Controls;
using SafetyVisionMonitor.ViewModels;

namespace SafetyVisionMonitor.Views;

public partial class CameraManageView : UserControl
{
    private bool _isFirstLoad = false;
    
    public CameraManageView()
    {
        InitializeComponent();
    }
    
    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_isFirstLoad && DataContext is CameraManageViewModel viewModel)
        {
            viewModel.OnLoaded();
            _isFirstLoad = false;
        }
    }
}