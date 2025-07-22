using System.Windows;
using System.Windows.Controls;
using SafetyVisionMonitor.ViewModels;

namespace SafetyVisionMonitor.Views;

public partial class DashboardView : UserControl
{
    private bool _isFirstLoad = true;
    
    public DashboardView()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isFirstLoad && DataContext is DashboardViewModel viewModel)
        {
            viewModel.OnLoaded();
            _isFirstLoad = false;
        }
    }
}