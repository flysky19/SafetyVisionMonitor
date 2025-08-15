using System.Windows.Controls;
using SafetyVisionMonitor.ViewModels;

namespace SafetyVisionMonitor.Views;

public partial class AIModelView : UserControl
{
    private bool _isFirstLoad = true;
    
    public AIModelView()
    {
        InitializeComponent();
    }
    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"AIModelView: OnLoaded 이벤트 - _isFirstLoad: {_isFirstLoad}, DataContext: {DataContext?.GetType().Name}");
        
        if (_isFirstLoad && DataContext is AIModelViewModel viewModel)
        {
            System.Diagnostics.Debug.WriteLine("AIModelView: ViewModel OnLoaded 호출");
            viewModel.OnLoaded();
            _isFirstLoad = false;
        }
    }
}