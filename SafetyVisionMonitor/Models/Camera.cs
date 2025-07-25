using CommunityToolkit.Mvvm.ComponentModel;

namespace SafetyVisionMonitor.Models
{
    public partial class Camera : ObservableObject
    {
        [ObservableProperty]
        private string id = string.Empty;
        
        [ObservableProperty]
        private string name = string.Empty;
        
        [ObservableProperty]
        private string connectionString = string.Empty;
        
        [ObservableProperty]
        private CameraType type = CameraType.RTSP;
        
        [ObservableProperty]
        private int width = 1920;
        
        [ObservableProperty]
        private int height = 1080;
        
        [ObservableProperty]
        private bool isConnected;
        
        [ObservableProperty]
        private bool isEnabled = true;
        
        [ObservableProperty]
        private double fps = 25.0;
        
        [ObservableProperty]
        private double brightness = 128.0;
        
        [ObservableProperty]
        private double contrast = 32.0;
        
        [ObservableProperty]
        private double saturation = 64.0;
        
        [ObservableProperty]
        private double exposure = -1.0;
        
        [ObservableProperty]
        private double gain = 0.0;
        
        [ObservableProperty]
        private double hue = 0.0;
        
        [ObservableProperty]
        private double gamma = 1.0;
        
        [ObservableProperty]
        private double sharpness = 0.0;
        
        [ObservableProperty]
        private bool autoExposure = true;
        
        [ObservableProperty]
        private bool autoWhiteBalance = true;
        
        // 캘리브레이션 정보
        [ObservableProperty]
        private double calibrationPixelsPerMeter = 100.0;
        
        [ObservableProperty]
        private bool isCalibrated = false;
        
        public string Resolution => $"{Width}×{Height}";
        public string Status => !IsEnabled ? "미사용" : (IsConnected ? "연결됨" : "연결 안됨");
    }
    
    public enum CameraType
    {
        RTSP,
        USB,
        File
    }
}