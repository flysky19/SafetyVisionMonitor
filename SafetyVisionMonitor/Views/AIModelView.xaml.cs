using System.Windows.Controls;
using SafetyVisionMonitor.ViewModels;

namespace SafetyVisionMonitor.Views;

public partial class AIModelView : UserControl
{
    private bool _isFirstLoad = false;
    
    public AIModelView()
    {
        InitializeComponent();
    }
    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_isFirstLoad && DataContext is AIModelViewModel viewModel)
        {
            viewModel.OnLoaded();
            _isFirstLoad = false;
        }
    }
}