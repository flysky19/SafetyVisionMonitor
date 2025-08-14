using System.Windows.Controls;
using SafetyVisionHistoryViewer.ViewModels;

namespace SafetyVisionHistoryViewer.Views;

public partial class EventLogView : UserControl
{
    private bool _isFirstLoad = true;
    
    public EventLogView()
    {
        InitializeComponent();
        DataContext = new EventLogViewModel();
    }
    
    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_isFirstLoad && DataContext is EventLogViewModel viewModel)
        {
            viewModel.OnLoaded();
            _isFirstLoad = false;
        }
    }
}