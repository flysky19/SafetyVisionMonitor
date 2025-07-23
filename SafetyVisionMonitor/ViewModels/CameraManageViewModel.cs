using System;
using System.Collections.ObjectModel;
using System.IO;
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
        private readonly string _captureDirectory;
        
        [ObservableProperty]
        private ObservableCollection<CameraItemViewModel> cameraItems;
        
        [ObservableProperty]
        private bool isFrameSavingEnabled = false;
        
        [ObservableProperty]
        private int savedFrameCount = 0;
        
        [ObservableProperty]
        private int saveFrameInterval = 30; // 30프레임마다 저장 (약 1초마다)
        
        public CameraManageViewModel()
        {
            Title = "카메라 관리";
            CameraItems = new ObservableCollection<CameraItemViewModel>();
            
            // 캡처 디렉토리 생성
            _captureDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
                                           "SafetyVisionMonitor", "Captures");
            Directory.CreateDirectory(_captureDirectory);
            
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
            
            // 타이머는 더 이상 필요 없음 (직접 프레임 업데이트 방식 사용)
            _frameTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000) // 1초마다 연결 상태 체크용으로만 사용
            };
            _frameTimer.Tick += CheckConnectionStatus;
        }
        
        public override async void OnLoaded()
        {
            base.OnLoaded();
            
            // 카메라 서비스 이벤트 구독 (UI용 저화질 프레임 사용)
            App.CameraService.FrameReceivedForUI += OnFrameReceived;
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
            App.CameraService.FrameReceivedForUI -= OnFrameReceived;
            App.CameraService.ConnectionChanged -= OnConnectionChanged;
            
            _frameTimer.Stop();
        }
        
        private void OnFrameReceived(object? sender, CameraFrameEventArgs e)
        {
            var cameraItem = CameraItems.FirstOrDefault(c => c.Camera.Id == e.CameraId);
            if (cameraItem == null)
            {
                e.Frame?.Dispose();
                return;
            }
            
            if (e.Frame == null || e.Frame.Empty())
            {
                e.Frame?.Dispose();
                return;
            }
            
            // UI 스레드에서 변환과 업데이트 처리
            App.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    using (var frame = e.Frame)
                    {
                        if (frame != null && !frame.Empty())
                        {
                            var bitmap = frame.ToBitmapSource();
                            
                            if (bitmap != null)
                            {
                                cameraItem.CurrentFrame = bitmap;
                                
                                // 프레임을 받았다는 것은 카메라가 연결되어 있다는 뜻
                                if (!cameraItem.Camera.IsConnected)
                                {
                                    cameraItem.Camera.IsConnected = true;
                                }
                                
                                cameraItem.FrameCount++;
                                cameraItem.StatusText = $"프레임: {cameraItem.FrameCount}";
                                
                                // 프레임 저장 로직 (선택적)
                                if (IsFrameSavingEnabled && ShouldSaveFrame(cameraItem))
                                {
                                    SaveFrameAsJpg(frame, e.CameraId, cameraItem.FrameCount);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CameraManageViewModel: Frame processing error for {e.CameraId}: {ex.Message}");
                }
            });
        }
        
        private bool ShouldSaveFrame(CameraItemViewModel cameraItem)
        {
            // 지정된 간격마다 저장 (SaveFrameInterval 프레임마다)
            return cameraItem.FrameCount % SaveFrameInterval == 0;
        }
        
        private void SaveFrameAsJpg(Mat frame, string cameraId, int frameCount)
        {
            try
            {
                // 파일명 생성: CameraID_YYYYMMDD_HHMMSS_FrameNumber.jpg
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var fileName = $"{cameraId}_{timestamp}_{frameCount:D6}.jpg";
                var filePath = Path.Combine(_captureDirectory, fileName);
                
                // OpenCV를 사용해 JPG로 저장
                Cv2.ImWrite(filePath, frame, new ImageEncodingParam(ImwriteFlags.JpegQuality, 90));
                
                SavedFrameCount++;
            }
            catch (Exception ex)
            {
                StatusMessage = $"프레임 저장 실패: {ex.Message}";
            }
        }
        
        partial void OnIsFrameSavingEnabledChanged(bool value)
        {
            StatusMessage = value ? "프레임 저장 시작됨" : "프레임 저장 중지됨";
        }
        
        [RelayCommand]
        private void ToggleFrameSaving()
        {
            IsFrameSavingEnabled = !IsFrameSavingEnabled;
        }
        
        [RelayCommand]
        private void OpenCaptureFolder()
        {
            try
            {
                if (Directory.Exists(_captureDirectory))
                {
                    System.Diagnostics.Process.Start("explorer.exe", _captureDirectory);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"폴더 열기 실패: {ex.Message}";
            }
        }
        
        [RelayCommand]
        private void ClearCapturedFrames()
        {
            try
            {
                var files = Directory.GetFiles(_captureDirectory, "*.jpg");
                foreach (var file in files)
                {
                    File.Delete(file);
                }
                
                SavedFrameCount = 0;
                StatusMessage = $"{files.Length}개의 캡처된 프레임을 삭제했습니다.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"파일 삭제 실패: {ex.Message}";
            }
        }
        
        private void OnConnectionChanged(object? sender, CameraConnectionEventArgs e)
        {
            var cameraItem = CameraItems.FirstOrDefault(c => c.Camera.Id == e.CameraId);
            if (cameraItem != null)
            {
                cameraItem.Camera.IsConnected = e.IsConnected;
            }
        }
        
        private void CheckConnectionStatus(object? sender, EventArgs e)
        {
            // 연결되지 않은 카메라의 상태 텍스트만 업데이트 (CurrentFrame은 유지)
            foreach (var item in CameraItems)
            {
                if (!item.Camera.IsConnected)
                {
                    // CurrentFrame = null 제거 - 프레임이 들어오고 있으면 표시해야 함
                    if (item.CurrentFrame == null)
                    {
                        item.StatusText = "미연결";
                    }
                }
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
    
    // 각 카메라 아이템을 위한 ViewModel (단순화됨)
    public partial class CameraItemViewModel : ObservableObject
    {
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
    }
}