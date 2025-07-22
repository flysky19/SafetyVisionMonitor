using System.Windows.Controls;
using SafetyVisionMonitor.ViewModels;

namespace SafetyVisionMonitor.Views;

public partial class HistoryView : UserControl
{
    private bool _isFirstLoad = true;
    
    public HistoryView()
    {
        InitializeComponent();
    }
    
    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_isFirstLoad && DataContext is HistoryViewModel viewModel)
        {
            viewModel.OnLoaded();
            _isFirstLoad = false;
        }
    }
}