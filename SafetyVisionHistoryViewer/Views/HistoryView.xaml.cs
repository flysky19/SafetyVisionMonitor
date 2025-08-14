using System.Windows.Controls;
using SafetyVisionHistoryViewer.ViewModels;

namespace SafetyVisionHistoryViewer.Views;

public partial class HistoryView : UserControl
{
    private bool _isFirstLoad = true;
    
    public HistoryView()
    {
        InitializeComponent();
        DataContext = new HistoryViewModel();
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