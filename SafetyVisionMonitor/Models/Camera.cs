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
        private double fps = 25.0;
        
        public string Resolution => $"{Width}×{Height}";
        public string Status => IsConnected ? "연결됨" : "연결 안됨";
    }
    
    public enum CameraType
    {
        RTSP,
        USB,
        File
    }
}