using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using SafetyVisionMonitor.Models;
using SafetyVisionMonitor.Services;
using SafetyVisionMonitor.Views;

namespace SafetyVisionMonitor.ViewModels
{
    public partial class CameraManageViewModel : BaseViewModel
    {
        private readonly DispatcherTimer _frameTimer;
        
        [ObservableProperty]
        private ObservableCollection<CameraItemViewModel> cameraItems;
        
        public CameraManageViewModel()
        {
            Title = "카메라 관리";
            CameraItems = new ObservableCollection<CameraItemViewModel>();
            
            // 4개 카메라 슬롯 초기화
            for (int i = 0; i < 4; i++)
            {
                var camera = new Camera
                {
                    Id = $"CAM{i + 1:D3}",
                    Name = $"카메라 {i + 1}",
                    Type = CameraType.USB,
                    ConnectionString = i.ToString() // USB 카메라 인덱스
                };
                
                CameraItems.Add(new CameraItemViewModel(camera, i));
            }
            
            // 프레임 업데이트 타이머
            _frameTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(40) // 25 FPS
            };
            _frameTimer.Tick += UpdateFrames;
        }
        
        public override async void OnLoaded()
        {
            base.OnLoaded();
            
            // 카메라 서비스 이벤트 구독
            App.CameraService.FrameReceived += OnFrameReceived;
            App.CameraService.ConnectionChanged += OnConnectionChanged;
            
            
            // 저장된 카메라 설정 불러오기
            try
            {
                var savedCameras = await App.DatabaseService.LoadCameraConfigsAsync();
        
                for (int i = 0; i < Math.Min(savedCameras.Count, CameraItems.Count); i++)
                {
                    var saved = savedCameras[i];
                    var item = CameraItems[i];
            
                    item.Camera.Name = saved.Name;
                    item.Camera.ConnectionString = saved.ConnectionString;
                    item.Camera.Type = saved.Type;
                    item.Camera.Width = saved.Width;
                    item.Camera.Height = saved.Height;
                    item.Camera.Fps = saved.Fps;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"설정 불러오기 실패: {ex.Message}";
            }
        }
        
        public override void OnActivated()
        {
            base.OnActivated();
            _frameTimer.Start();
        }
        
        public override void OnDeactivated()
        {
            base.OnDeactivated();
            _frameTimer.Stop();
        }
        
        public override void Cleanup()
        {
            base.Cleanup();
            
            // 이벤트 구독 해제
            App.CameraService.FrameReceived -= OnFrameReceived;
            App.CameraService.ConnectionChanged -= OnConnectionChanged;
            
            _frameTimer.Stop();
        }
        
        private void OnFrameReceived(object? sender, CameraFrameEventArgs e)
        {
            var cameraItem = CameraItems.FirstOrDefault(c => c.Camera.Id == e.CameraId);
            cameraItem?.UpdateFrame(e.Frame);
        }
        
        private void OnConnectionChanged(object? sender, CameraConnectionEventArgs e)
        {
            var cameraItem = CameraItems.FirstOrDefault(c => c.Camera.Id == e.CameraId);
            if (cameraItem != null)
            {
                cameraItem.Camera.IsConnected = e.IsConnected;
            }
        }
        
        private void UpdateFrames(object? sender, EventArgs e)
        {
            foreach (var item in CameraItems)
            {
                item.UpdateDisplay();
            }
        }
        
        [RelayCommand]
        private async Task ConfigureCamera(int index)
        {
            if (index >= 0 && index < CameraItems.Count)
            {
                var item = CameraItems[index];
                var dialog = new CameraConfigDialog(item.Camera);
        
                if (dialog.ShowDialog() == true)
                {
                    // 설정 적용
                    dialog.ViewModel.ApplyTo(item.Camera);
            
                    // 연결된 카메라인 경우 재연결
                    if (item.Camera.IsConnected)
                    {
                        App.CameraService.DisconnectCamera(item.Camera.Id);
                        await App.CameraService.ConnectCamera(item.Camera);
                    }
            
                    // 설정 저장 (DatabaseService 사용)
                    await SaveCameraConfigurations();
            
                    StatusMessage = $"{item.Camera.Name} 설정이 업데이트되었습니다.";
                }
            }
        }
        
        private async Task SaveCameraConfigurations()
        {
            var cameras = CameraItems.Select(ci => ci.Camera).ToList();
            await App.DatabaseService.SaveCameraConfigsAsync(cameras);
        }
        
        [RelayCommand]
        private async Task ToggleConnection(int index)
        {
            if (index >= 0 && index < CameraItems.Count)
            {
                var item = CameraItems[index];
                
                if (item.Camera.IsConnected)
                {
                    App.CameraService.DisconnectCamera(item.Camera.Id);
                    item.Camera.IsConnected = false;
                    StatusMessage = $"{item.Camera.Name} 연결 해제됨";
                }
                else
                {
                    IsLoading = true;
                    try
                    {
                        var success = await App.CameraService.ConnectCamera(item.Camera);
                        if (success)
                        {
                            StatusMessage = $"{item.Camera.Name} 연결됨";
                        }
                        else
                        {
                            StatusMessage = $"{item.Camera.Name} 연결 실패";
                        }
                    }
                    finally
                    {
                        IsLoading = false;
                    }
                }
            }
        }
    }
    
    // 각 카메라 아이템을 위한 ViewModel
    public partial class CameraItemViewModel : ObservableObject
    {
        private Mat? _latestFrame;
        private readonly object _frameLock = new();
        
        [ObservableProperty]
        private Camera camera;
        
        [ObservableProperty]
        private int index;
        
        [ObservableProperty]
        private BitmapSource? currentFrame;
        
        [ObservableProperty]
        private string statusText = "미연결";
        
        [ObservableProperty]
        private int frameCount = 0;
        
        public CameraItemViewModel(Camera camera, int index)
        {
            Camera = camera;
            Index = index;
        }
        
        public void UpdateFrame(Mat frame)
        {
            lock (_frameLock)
            {
                _latestFrame?.Dispose();
                _latestFrame = frame.Clone();
                FrameCount++;
            }
        }
        
        public void UpdateDisplay()
        {
            if (!Camera.IsConnected)
            {
                CurrentFrame = null;
                StatusText = "미연결";
                return;
            }
            
            lock (_frameLock)
            {
                if (_latestFrame != null && !_latestFrame.Empty())
                {
                    // UI 스레드에서 BitmapSource 생성
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        CurrentFrame = _latestFrame.ToBitmapSource();
                    });
                    
                    StatusText = $"프레임: {FrameCount}";
                }
            }
        }
    }
}