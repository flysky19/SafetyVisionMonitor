using System;
using System.Windows;
using SafetyVisionMonitor.ViewModels;

namespace SafetyVisionMonitor.Views
{
    public partial class CameraConfigDialog : Window
    {
        public CameraConfigDialogViewModel ViewModel { get; }
        private readonly SafetyVisionMonitor.Shared.Models.Camera _originalCamera;
        
        public CameraConfigDialog(SafetyVisionMonitor.Shared.Models.Camera camera)
        {
            InitializeComponent();
            _originalCamera = camera;
            ViewModel = new CameraConfigDialogViewModel(camera);
            DataContext = ViewModel;
        }
        
        private async void OnOkClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // ViewModel의 변경사항을 원본 Camera 객체에 적용
                ViewModel.ApplyTo(_originalCamera);
                
                // 데이터베이스에 즉시 저장
                await App.DatabaseService.SaveCameraConfigAsync(_originalCamera);
                
                System.Diagnostics.Debug.WriteLine($"Camera '{_originalCamera.Name}' settings saved to database");
                System.Diagnostics.Debug.WriteLine($"Brightness: {_originalCamera.Brightness}, Contrast: {_originalCamera.Contrast}");
                
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"설정 저장 중 오류가 발생했습니다: {ex.Message}", "오류", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}