using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionMonitor.ViewModels
{
    public partial class CameraConfigDialogViewModel : ObservableObject
    {
        private static class CameraDefaults
        {
            public const double Brightness = 128.0;
            public const double Contrast = 32.0;
            public const double Saturation = 64.0;
            public const double Exposure = -1.0;
            public const double Gain = 0.0;
            public const double Hue = 0.0;
            public const double Gamma = 1.0;
            public const double Sharpness = 0.0;
            public const bool AutoExposure = true;
            public const bool AutoWhiteBalance = true;
        }
        [ObservableProperty]
        private Camera camera;
        
        [ObservableProperty]
        private CameraType[] cameraTypes = Enum.GetValues<CameraType>();
        
        [ObservableProperty]
        private string connectionHint = string.Empty;
        
        [ObservableProperty]
        private string testResult = string.Empty;
        
        [ObservableProperty]
        private Brush testResultColor = Brushes.Gray;
        
        private readonly Camera _originalCamera;
        
        public CameraConfigDialogViewModel(Camera camera)
        {
            _originalCamera = camera;
            
            // 원본을 직접 수정하지 않도록 복사본 생성
            Camera = CreateCameraCopy(camera);
            
            UpdateConnectionHint();
        }
        
        partial void OnCameraChanged(Camera? oldValue, Camera newValue)
        {
            if (newValue != null)
            {
                UpdateConnectionHint();
            }
        }
        
        private void UpdateConnectionHint()
        {
            ConnectionHint = Camera.Type switch
            {
                CameraType.RTSP => "예: rtsp://192.168.1.100:554/stream1",
                CameraType.USB => "예: 0 (첫 번째 USB 카메라)",
                CameraType.File => "예: C:\\Videos\\test.mp4",
                _ => ""
            };
        }
        
        [RelayCommand]
        private async Task TestConnection()
        {
            if (!ValidateCameraConfig())
            {
                TestResult = "✗ 카메라 설정이 유효하지 않습니다.";
                TestResultColor = Brushes.Red;
                return;
            }
            
            TestResult = "연결 테스트 중...";
            TestResultColor = Brushes.Yellow;
            
            try
            {
                // 실제 연결 테스트
                var testCamera = new Camera
                {
                    Id = "TEST",
                    ConnectionString = Camera.ConnectionString,
                    Type = Camera.Type,
                    Width = Camera.Width,
                    Height = Camera.Height,
                    Fps = Camera.Fps
                };
                
                var success = await App.CameraService.ConnectCamera(testCamera);
                
                if (success)
                {
                    TestResult = "✓ 연결 성공!";
                    TestResultColor = Brushes.LightGreen;
                    
                    // 테스트 연결 해제
                    App.CameraService.DisconnectCamera("TEST");
                }
                else
                {
                    TestResult = "✗ 연결 실패";
                    TestResultColor = Brushes.Red;
                }
            }
            catch (Exception ex)
            {
                TestResult = $"✗ 오류: {ex.Message}";
                TestResultColor = Brushes.Red;
            }
        }
        
        [RelayCommand]
        private void SetResolution(string preset)
        {
            switch (preset)
            {
                case "HD":
                    Camera.Width = 1280;
                    Camera.Height = 720;
                    break;
                case "FHD":
                    Camera.Width = 1920;
                    Camera.Height = 1080;
                    break;
                case "4K":
                    Camera.Width = 3840;
                    Camera.Height = 2160;
                    break;
            }
        }
        
        [RelayCommand]
        private async Task SaveConfig()
        {
            if (!ValidateCameraConfig())
            {
                TestResult = "✗ 저장할 수 없습니다. 카메라 설정을 확인하세요.";
                TestResultColor = Brushes.Red;
                return;
            }
            
            try
            {
                // 단일 카메라 설정을 직접 DB에 저장 (효율적이고 정확한 방식)
                await App.DatabaseService.SaveCameraConfigAsync(Camera);
                
                TestResult = "✓ 설정이 DB에 저장되었습니다.";
                TestResultColor = Brushes.LightGreen;
            }
            catch (Exception ex)
            {
                TestResult = $"저장 실패: {ex.Message}";
                TestResultColor = Brushes.Red;
            }
        }
        
        [RelayCommand]
        private async Task LoadConfig()
        {
            try
            {
                // DB에서 저장된 카메라 설정 불러오기
                var cameras = await App.DatabaseService.LoadCameraConfigsAsync();
                var savedCamera = cameras.FirstOrDefault(c => c.Id == Camera.Id);
                
                if (savedCamera != null)
                {
                    // 현재 ID는 유지하고 나머지 속성만 복사
                    var originalId = Camera.Id;
                    Camera = CreateCameraCopy(savedCamera);
                    Camera.Id = originalId;
                    
                    UpdateConnectionHint();
                    
                    TestResult = "✓ DB에서 설정을 불러왔습니다.";
                    TestResultColor = Brushes.LightGreen;
                }
                else
                {
                    TestResult = "저장된 설정이 없습니다.";
                    TestResultColor = Brushes.Orange;
                }
            }
            catch (Exception ex)
            {
                TestResult = $"불러오기 실패: {ex.Message}";
                TestResultColor = Brushes.Red;
            }
        }
        
        [RelayCommand]
        private void ResetImageSettings()
        {
            Camera.Brightness = CameraDefaults.Brightness;
            Camera.Contrast = CameraDefaults.Contrast;
            Camera.Saturation = CameraDefaults.Saturation;
            Camera.Exposure = CameraDefaults.Exposure;
            Camera.Gain = CameraDefaults.Gain;
            Camera.Hue = CameraDefaults.Hue;
            Camera.Gamma = CameraDefaults.Gamma;
            Camera.Sharpness = CameraDefaults.Sharpness;
            Camera.AutoExposure = CameraDefaults.AutoExposure;
            Camera.AutoWhiteBalance = CameraDefaults.AutoWhiteBalance;
            
            TestResult = "✓ 이미지 설정이 기본값으로 재설정되었습니다.";
            TestResultColor = Brushes.LightGreen;
        }
        
        [RelayCommand]
        private async Task ApplyImageSettings()
        {
            try
            {
                if (Camera.IsConnected)
                {
                    App.CameraService.UpdateCameraSettings(Camera.Id, Camera);
                    TestResult = "✓ 이미지 설정이 적용되었습니다.";
                    TestResultColor = Brushes.LightGreen;
                }
                else
                {
                    TestResult = "카메라가 연결되지 않았습니다.";
                    TestResultColor = Brushes.Orange;
                }
            }
            catch (Exception ex)
            {
                TestResult = $"설정 적용 실패: {ex.Message}";
                TestResultColor = Brushes.Red;
            }
        }
        
        public void ApplyTo(Camera target)
        {
            CopyPropertiesTo(target);
        }
        
        private static Camera CreateCameraCopy(Camera source)
        {
            return new Camera
            {
                Id = source.Id,
                Name = source.Name,
                ConnectionString = source.ConnectionString,
                Type = source.Type,
                Width = source.Width,
                Height = source.Height,
                Fps = source.Fps,
                IsConnected = source.IsConnected,
                Brightness = source.Brightness,
                Contrast = source.Contrast,
                Saturation = source.Saturation,
                Exposure = source.Exposure,
                Gain = source.Gain,
                Hue = source.Hue,
                Gamma = source.Gamma,
                Sharpness = source.Sharpness,
                AutoExposure = source.AutoExposure,
                AutoWhiteBalance = source.AutoWhiteBalance,
                IsEnabled = source.IsEnabled
            };
        }
        
        private void CopyPropertiesTo(Camera target)
        {
            target.Name = Camera.Name;
            target.ConnectionString = Camera.ConnectionString;
            target.Type = Camera.Type;
            target.Width = Camera.Width;
            target.Height = Camera.Height;
            target.Fps = Camera.Fps;
            target.Brightness = Camera.Brightness;
            target.Contrast = Camera.Contrast;
            target.Saturation = Camera.Saturation;
            target.Exposure = Camera.Exposure;
            target.Gain = Camera.Gain;
            target.Hue = Camera.Hue;
            target.Gamma = Camera.Gamma;
            target.Sharpness = Camera.Sharpness;
            target.AutoExposure = Camera.AutoExposure;
            target.AutoWhiteBalance = Camera.AutoWhiteBalance;
            target.IsEnabled = Camera.IsEnabled;
            target.IsConnected = Camera.IsConnected;
        }
        
        private bool ValidateCameraConfig()
        {
            if (string.IsNullOrWhiteSpace(Camera?.Name))
                return false;
                
            if (string.IsNullOrWhiteSpace(Camera.ConnectionString))
                return false;
                
            if (Camera.Width <= 0 || Camera.Height <= 0)
                return false;
                
            if (Camera.Fps <= 0)
                return false;
                
            return true;
        }
    }
}