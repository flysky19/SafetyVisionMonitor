using System.Windows.Controls;
using SafetyVisionHistoryViewer.ViewModels;

namespace SafetyVisionHistoryViewer.Views
{
    public partial class TrackingLogView : UserControl
    {
        public TrackingLogView()
        {
            InitializeComponent();
            DataContext = new TrackingLogViewModel();
        }
        
        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is TrackingLogViewModel viewModel)
            {
                viewModel.OnLoaded();
            }
        }
    }
}