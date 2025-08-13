using System.Windows;
using System.Windows.Controls;
using SafetyVisionMonitor.ViewModels;
using System.Linq;
using SafetyVisionMonitor.Models;

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
        
        private async void EditZoneButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is TrackingZone zone)
            {
                if (DataContext is TrackingSetupViewModel viewModel)
                {
                    // 카메라 목록 가져오기 (이미 Camera 객체를 반환함)
                    var cameras = await App.DatabaseService.LoadCameraConfigsAsync();
                    
                    // 편집 다이얼로그 표시
                    var dialog = new TrackingZoneEditDialog(zone, cameras);
                    dialog.Owner = Window.GetWindow(this);
                    
                    if (dialog.ShowDialog() == true)
                    {
                        var editedZone = dialog.GetEditedZone();
                        
                        // 기존 구역 업데이트
                        var index = viewModel.TrackingZones.IndexOf(zone);
                        if (index >= 0)
                        {
                            viewModel.TrackingZones[index] = editedZone;
                        }
                    }
                }
            }
        }
    }
}