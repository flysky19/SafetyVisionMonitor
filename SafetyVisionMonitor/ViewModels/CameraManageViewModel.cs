using System;
using System.Collections.Generic;
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
using Point = OpenCvSharp.Point;
using SafetyVisionMonitor.Helpers;
using SafetyVisionMonitor.Shared.Models;
using SafetyVisionMonitor.Services;
using SafetyVisionMonitor.Views;
using SafetyVisionMonitor.Shared.ViewModels.Base;

namespace SafetyVisionMonitor.ViewModels
{
    public partial class CameraManageViewModel : BaseViewModel
    {
        private readonly DispatcherTimer _frameTimer;
        private readonly string _captureDirectory;
        
        // 카메라별 3D 구역 데이터 - Dictionary로 카메라별 관리
        private readonly Dictionary<string, ObservableCollection<ZoneVisualization>> _cameraWarningZones = new();
        private readonly Dictionary<string, ObservableCollection<ZoneVisualization>> _cameraDangerZones = new();
        
        [ObservableProperty]
        private ObservableCollection<CameraItemViewModel> cameraItems;
        
        [ObservableProperty]
        private bool isFrameSavingEnabled = false;
        
        [ObservableProperty]
        private int savedFrameCount = 0;
        
        [ObservableProperty]
        private int saveFrameInterval = 30; // 30프레임마다 저장 (약 1초마다)
        
        [ObservableProperty]
        private int activeCameraCount = 0;
        
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
            
            // 구역 업데이트 이벤트 구독
            App.AppData.ZoneUpdated += OnZoneUpdated;
            
            // 저장된 카메라 설정 불러오기
            try
            {
                var savedCameras = await App.DatabaseService.LoadCameraConfigsAsync();
        
                // Camera ID로 정확히 매칭하여 로드
                foreach (var item in CameraItems)
                {
                    var saved = savedCameras.FirstOrDefault(c => c.Id == item.Camera.Id);
                    if (saved != null)
                    {
                        item.Camera.Name = saved.Name;
                        item.Camera.ConnectionString = saved.ConnectionString;
                        item.Camera.Type = saved.Type;
                        item.Camera.Width = saved.Width;
                        item.Camera.Height = saved.Height;
                        item.Camera.Fps = saved.Fps;
                        item.Camera.IsEnabled = saved.IsEnabled;
                        
                        // 이미지 조정 설정도 로드
                        item.Camera.Brightness = saved.Brightness;
                        item.Camera.Contrast = saved.Contrast;
                        item.Camera.Saturation = saved.Saturation;
                        item.Camera.Exposure = saved.Exposure;
                        item.Camera.Gain = saved.Gain;
                        item.Camera.Hue = saved.Hue;
                        item.Camera.Gamma = saved.Gamma;
                        item.Camera.Sharpness = saved.Sharpness;
                        item.Camera.AutoExposure = saved.AutoExposure;
                        item.Camera.AutoWhiteBalance = saved.AutoWhiteBalance;
                        
                        System.Diagnostics.Debug.WriteLine($"Loaded camera {saved.Name}: Brightness={saved.Brightness}, Contrast={saved.Contrast}");
                        
                        // 이미 연결된 카메라인 경우 설정을 즉시 적용
                        if (item.Camera.IsConnected)
                        {
                            try
                            {
                                App.CameraService.UpdateCameraSettings(item.Camera.Id, item.Camera);
                                System.Diagnostics.Debug.WriteLine($"Applied loaded settings to connected camera {item.Camera.Name}");
                            }
                            catch (Exception settingsEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to apply settings to {item.Camera.Name}: {settingsEx.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"설정 불러오기 실패: {ex.Message}";
            }
            
            // 활성 카메라 카운트 업데이트
            UpdateActiveCameraCount();
            
            // 구역 데이터 로드
            await LoadZoneOverlaysAsync();
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
            App.AppData.ZoneUpdated -= OnZoneUpdated;
            
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
                            // 프레임에 구역 오버레이 그리기
                            var frameWithZones = DrawZoneOverlaysOnFrame(frame, e.CameraId);
                            
                            // UI 스레드에서 BitmapSource 변환
                            var bitmap = ImageConverter.MatToBitmapSource(frameWithZones);
                            
                            // 그려진 프레임 해제
                            frameWithZones.Dispose();
                            
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
                                
                                // 프레임 저장 로직 (선택적) - 구역이 그려진 프레임을 저장
                                if (IsFrameSavingEnabled && ShouldSaveFrame(cameraItem))
                                {
                                    // 원본 프레임 대신 구역이 그려진 프레임을 저장
                                    using (var finalFrame = DrawZoneOverlaysOnFrame(frame, e.CameraId))
                                    {
                                        SaveFrameAsJpg(finalFrame, e.CameraId, cameraItem.FrameCount);
                                    }
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
        
        private void OnZoneUpdated(object? sender, Services.ZoneUpdateEventArgs e)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                System.Diagnostics.Debug.WriteLine($"CameraManage received zone update: {e.Zone.Name}, IsEnabled={e.IsEnabled}");
                
                // 해당 카메라의 구역을 찾아서 업데이트
                var cameraId = e.Zone.CameraId;
                
                // 경고 구역 업데이트
                if (_cameraWarningZones.ContainsKey(cameraId))
                {
                    var warningZone = _cameraWarningZones[cameraId].FirstOrDefault(z => z.ZoneId == e.ZoneId);
                    if (warningZone != null)
                    {
                        warningZone.IsEnabled = e.IsEnabled;
                        warningZone.Opacity = e.IsEnabled ? e.Zone.Opacity : 0.05;
                        System.Diagnostics.Debug.WriteLine($"CameraManage: Updated warning zone visualization: {warningZone.Name} for camera {cameraId}");
                        return;
                    }
                }
                
                // 위험 구역 업데이트
                if (_cameraDangerZones.ContainsKey(cameraId))
                {
                    var dangerZone = _cameraDangerZones[cameraId].FirstOrDefault(z => z.ZoneId == e.ZoneId);
                    if (dangerZone != null)
                    {
                        dangerZone.IsEnabled = e.IsEnabled;
                        dangerZone.Opacity = e.IsEnabled ? e.Zone.Opacity : 0.05;
                        System.Diagnostics.Debug.WriteLine($"CameraManage: Updated danger zone visualization: {dangerZone.Name} for camera {cameraId}");
                    }
                }
            });
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
                    try
                    {
                        // 설정 적용
                        dialog.ViewModel.ApplyTo(item.Camera);
                
                        // 연결된 카메라인 경우 재연결
                        if (item.Camera.IsConnected && item.Camera.IsEnabled)
                        {
                            App.CameraService.DisconnectCamera(item.Camera.Id);
                            var reconnectSuccess = await App.CameraService.ConnectCamera(item.Camera);
                            
                            if (reconnectSuccess)
                            {
                                // 재연결 후 이미지 조정 설정을 즉시 적용
                                try
                                {
                                    await Task.Delay(500); // 카메라 안정화 대기
                                    App.CameraService.UpdateCameraSettings(item.Camera.Id, item.Camera);
                                    System.Diagnostics.Debug.WriteLine($"Applied updated settings after reconnection: {item.Camera.Name}");
                                }
                                catch (Exception settingsEx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Failed to apply settings after reconnection: {settingsEx.Message}");
                                }
                            }
                        }
                        else if (item.Camera.IsConnected && !item.Camera.IsEnabled)
                        {
                            // 카메라가 비활성화되면 연결 해제
                            App.CameraService.DisconnectCamera(item.Camera.Id);
                            item.Camera.IsConnected = false;
                        }
                
                        // 설정 저장 (DatabaseService 사용)
                        await SaveCameraConfigurations();
                
                        StatusMessage = $"{item.Camera.Name} 설정이 업데이트되었습니다.";
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = $"오류: {ex.Message}";
                        System.Diagnostics.Debug.WriteLine($"Camera configuration error: {ex}");
                    }
                }
            }
        }
        
        private async Task SaveCameraConfigurations()
        {
            var cameras = CameraItems.Select(ci => ci.Camera).ToList();
            await App.DatabaseService.SaveCameraConfigsAsync(cameras);
        }
        
        [RelayCommand]
        private async Task ToggleCameraEnabled(int index)
        {
            if (index >= 0 && index < CameraItems.Count)
            {
                var item = CameraItems[index];
                
                // 사용 여부 토글
                item.Camera.IsEnabled = !item.Camera.IsEnabled;
                
                // 미사용으로 변경 시 연결 해제
                if (!item.Camera.IsEnabled && item.Camera.IsConnected)
                {
                    App.CameraService.DisconnectCamera(item.Camera.Id);
                    item.Camera.IsConnected = false;
                    item.CurrentFrame = null;
                }
                
                // 설정 저장
                await SaveCameraConfigurations();
                
                // 활성 카메라 카운트 업데이트
                UpdateActiveCameraCount();
                
                StatusMessage = item.Camera.IsEnabled 
                    ? $"{item.Camera.Name} 사용으로 변경됨" 
                    : $"{item.Camera.Name} 미사용으로 변경됨 (AI 처리 제외)";
            }
        }
        
        private void UpdateActiveCameraCount()
        {
            ActiveCameraCount = CameraItems.Count(item => item.Camera.IsEnabled);
        }

        [RelayCommand]
        private async Task ToggleConnection(int index)
        {
            if (index >= 0 && index < CameraItems.Count)
            {
                var item = CameraItems[index];
                
                // 미사용 카메라는 연결할 수 없음
                if (!item.Camera.IsEnabled)
                {
                    StatusMessage = $"{item.Camera.Name}은(는) 미사용 상태입니다. 먼저 사용함으로 변경해주세요.";
                    return;
                }
                
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
                            
                            // 연결 후 이미지 조정 설정을 즉시 적용 ("변경사항 적용" 버튼과 동일)
                            try
                            {
                                await Task.Delay(500); // 카메라 안정화 대기
                                App.CameraService.UpdateCameraSettings(item.Camera.Id, item.Camera);
                                System.Diagnostics.Debug.WriteLine($"Applied image settings after connection: {item.Camera.Name} (Brightness={item.Camera.Brightness})");
                            }
                            catch (Exception settingsEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to apply settings after connection: {settingsEx.Message}");
                            }
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
        
        private async Task LoadZoneOverlaysAsync()
        {
            try
            {
                var zones = await App.DatabaseService.LoadZone3DConfigsAsync();
                
                // 카메라별 구역 저장소 초기화
                _cameraWarningZones.Clear();
                _cameraDangerZones.Clear();
                
                // 각 카메라별로 구역 분류
                foreach (var zone in zones)
                {
                    if (zone.FloorPoints.Count >= 3)
                    {
                        var visualization = new ZoneVisualization
                        {
                            ZoneId = zone.Id,
                            Name = zone.Name,
                            ZoneColor = zone.DisplayColor,
                            Opacity = zone.IsEnabled ? zone.Opacity : 0.05,
                            IsSelected = false,
                            IsEnabled = zone.IsEnabled
                        };
                        
                        // 3D 좌표를 상대 좌표(0~1)로 변환
                        var originalFrameWidth = zone.CalibrationFrameWidth;
                        var originalFrameHeight = zone.CalibrationFrameHeight;
                        
                        foreach (var worldPoint in zone.FloorPoints)
                        {
                            // 먼저 원본 프레임 크기로 화면 좌표 계산
                            var screenPoint = CoordinateTransformService.WorldToScreen(worldPoint, 
                                originalFrameWidth, originalFrameHeight, zone.CalibrationPixelsPerMeter);
                                
                            // 상대 좌표로 변환 (0~1 범위)
                            var relativeX = screenPoint.X / originalFrameWidth;
                            var relativeY = screenPoint.Y / originalFrameHeight;
                            var relativePoint = new System.Windows.Point(relativeX, relativeY);
                            
                            visualization.AddRelativePoint(relativePoint);
                        }
                        
                        // 다각형 닫기
                        if (zone.FloorPoints.Count >= 3)
                        {
                            var firstScreenPoint = CoordinateTransformService.WorldToScreen(zone.FloorPoints[0], 
                                originalFrameWidth, originalFrameHeight, zone.CalibrationPixelsPerMeter);
                            var relativeX = firstScreenPoint.X / originalFrameWidth;
                            var relativeY = firstScreenPoint.Y / originalFrameHeight;
                            visualization.AddRelativePoint(new System.Windows.Point(relativeX, relativeY));
                        }
                        
                        // 카메라별로 구역 저장
                        if (zone.Type == ZoneType.Warning)
                        {
                            if (!_cameraWarningZones.ContainsKey(zone.CameraId))
                                _cameraWarningZones[zone.CameraId] = new ObservableCollection<ZoneVisualization>();
                            _cameraWarningZones[zone.CameraId].Add(visualization);
                        }
                        else
                        {
                            if (!_cameraDangerZones.ContainsKey(zone.CameraId))
                                _cameraDangerZones[zone.CameraId] = new ObservableCollection<ZoneVisualization>();
                            _cameraDangerZones[zone.CameraId].Add(visualization);
                        }
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"CameraManage: Loaded zones for {_cameraWarningZones.Count + _cameraDangerZones.Count} cameras");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CameraManage: Zone overlay load error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// OpenCV를 사용하여 프레임에 구역 오버레이를 직접 그립니다
        /// </summary>
        private Mat DrawZoneOverlaysOnFrame(Mat originalFrame, string cameraId)
        {
            if (originalFrame == null || originalFrame.Empty())
                return originalFrame;
            
            // 원본 프레임 복사
            var frameWithZones = originalFrame.Clone();
            
            try
            {
                var frameWidth = frameWithZones.Width;
                var frameHeight = frameWithZones.Height;
                
                // 경고 구역 그리기 (주황색)
                if (_cameraWarningZones.ContainsKey(cameraId))
                {
                    foreach (var zone in _cameraWarningZones[cameraId])
                    {
                        if (zone.IsEnabled && zone.RelativePoints.Count >= 3)
                        {
                            DrawZoneOnFrame(frameWithZones, zone, frameWidth, frameHeight, new Scalar(0, 165, 255)); // Orange
                        }
                    }
                }
                
                // 위험 구역 그리기 (빨간색)
                if (_cameraDangerZones.ContainsKey(cameraId))
                {
                    foreach (var zone in _cameraDangerZones[cameraId])
                    {
                        if (zone.IsEnabled && zone.RelativePoints.Count >= 3)
                        {
                            DrawZoneOnFrame(frameWithZones, zone, frameWidth, frameHeight, new Scalar(0, 0, 255)); // Red
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CameraManage: Zone drawing error: {ex.Message}");
            }
            
            return frameWithZones;
        }
        
        /// <summary>
        /// 개별 구역을 프레임에 그립니다
        /// </summary>
        private void DrawZoneOnFrame(Mat frame, ZoneVisualization zone, int frameWidth, int frameHeight, Scalar color)
        {
            try
            {
                // 상대 좌표(0~1)를 픽셀 좌표로 변환
                var points = new List<Point>();
                
                foreach (var relativePoint in zone.RelativePoints)
                {
                    var pixelX = (int)(relativePoint.X * frameWidth);
                    var pixelY = (int)(relativePoint.Y * frameHeight);
                    
                    // 프레임 경계 내로 제한
                    pixelX = Math.Max(0, Math.Min(frameWidth - 1, pixelX));
                    pixelY = Math.Max(0, Math.Min(frameHeight - 1, pixelY));
                    
                    points.Add(new Point(pixelX, pixelY));
                }
                
                if (points.Count >= 3)
                {
                    // 다각형으로 채우기 (투명도 적용)
                    var overlay = frame.Clone();
                    Cv2.FillPoly(overlay, new Point[][] { points.ToArray() }, color);
                    
                    // 투명도 적용하여 원본과 합성
                    var alpha = zone.Opacity;
                    Cv2.AddWeighted(frame, 1.0 - alpha, overlay, alpha, 0, frame);
                    
                    // 구역 경계선 그리기
                    Cv2.Polylines(frame, new Point[][] { points.ToArray() }, true, color, 2);
                    
                    overlay.Dispose();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CameraManage: Individual zone drawing error for {zone.Name}: {ex.Message}");
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