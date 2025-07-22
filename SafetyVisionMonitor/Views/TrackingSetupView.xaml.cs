using System.Windows;
using System.Windows.Controls;
using SafetyVisionMonitor.ViewModels;

namespace SafetyVisionMonitor.Views
{
    public partial class TrackingSetupView : UserControl
    {
        private bool _isFirstLoad = true;
        
        public TrackingSetupView()
        {
            InitializeComponent();
        }
        
        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_isFirstLoad && DataContext is TrackingSetupViewModel viewModel)
            {
                viewModel.OnLoaded();
                _isFirstLoad = false;
            }
        }
        
        private void DeleteZoneButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is TrackingZone zone)
            {
                if (DataContext is TrackingSetupViewModel viewModel)
                {
                    viewModel.DeleteTrackingZoneCommand.Execute(zone);
                }
            }
        }
    }
}