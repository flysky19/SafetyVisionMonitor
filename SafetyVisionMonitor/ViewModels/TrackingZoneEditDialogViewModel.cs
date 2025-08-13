using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafetyVisionMonitor.Models;

namespace SafetyVisionMonitor.ViewModels
{
    public partial class TrackingZoneEditDialogViewModel : ObservableObject
    {
        [ObservableProperty]
        private TrackingZone zone;
        
        [ObservableProperty]
        private ObservableCollection<Camera> availableCameras;
        
        [ObservableProperty]
        private PointF? selectedPoint;
        
        [ObservableProperty]
        private BitmapSource? cameraFrame;
        
        [ObservableProperty]
        private string polygonPointsForDisplay = "";
        
        private Window? _dialogWindow;
        
        public event EventHandler? PolygonUpdateRequested;
        
        public TrackingZoneEditDialogViewModel(TrackingZone zone, List<Camera> cameras)
        {
            Zone = zone ?? new TrackingZone 
            { 
                Id = $"TZ{DateTime.Now:HHmmss}",
                Name = "새 트래킹 구역"
            };
            
            AvailableCameras = new ObservableCollection<Camera>(cameras);
            
            // 좌표 변경 시 디스플레이용 문자열 업데이트
            Zone.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TrackingZone.PolygonPoints))
                {
                    UpdatePolygonPointsForDisplay();
                }
                // 카메라 변경 시 프레임 다시 로드
                else if (e.PropertyName == nameof(TrackingZone.CameraId))
                {
                    LoadCameraFrame();
                }
            };
            
            UpdatePolygonPointsForDisplay();
            LoadCameraFrame();
        }
        
        public void Initialize(Window dialogWindow)
        {
            _dialogWindow = dialogWindow;
        }
        
        public void AddPoint(PointF point)
        {
            if (Zone.PolygonPoints == null)
                Zone.PolygonPoints = new List<PointF>();
                
            Zone.PolygonPoints.Add(point);
            Zone.PolygonPoints = new List<PointF>(Zone.PolygonPoints); // 변경 알림 트리거
            
            // 폴리곤 업데이트 요청
            PolygonUpdateRequested?.Invoke(this, EventArgs.Empty);
        }
        
        [RelayCommand]
        private void DeletePoint()
        {
            if (SelectedPoint.HasValue && Zone.PolygonPoints != null)
            {
                var pointToRemove = Zone.PolygonPoints.FirstOrDefault(p => 
                    Math.Abs(p.X - SelectedPoint.Value.X) < 0.01 && 
                    Math.Abs(p.Y - SelectedPoint.Value.Y) < 0.01);
                    
                if (pointToRemove != default)
                {
                    Zone.PolygonPoints.Remove(pointToRemove);
                    Zone.PolygonPoints = new List<PointF>(Zone.PolygonPoints); // 변경 알림 트리거
                    SelectedPoint = null;
                    
                    // 폴리곤 업데이트 요청
                    PolygonUpdateRequested?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        
        [RelayCommand]
        private void Clear()
        {
            Zone.PolygonPoints = new List<PointF>();
            // 폴리곤 업데이트 요청
            PolygonUpdateRequested?.Invoke(this, EventArgs.Empty);
        }
        
        [RelayCommand]
        private void Save()
        {
            if (string.IsNullOrWhiteSpace(Zone.Name))
            {
                MessageBox.Show("구역명을 입력해주세요.", "확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            if (Zone.PolygonPoints == null || Zone.PolygonPoints.Count < 3)
            {
                MessageBox.Show("최소 3개 이상의 점을 지정해야 합니다.", "확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            _dialogWindow.DialogResult = true;
            _dialogWindow.Close();
        }
        
        [RelayCommand]
        private void Cancel()
        {
            _dialogWindow.DialogResult = false;
            _dialogWindow.Close();
        }
        
        private void UpdatePolygonPointsForDisplay()
        {
            if (Zone.PolygonPoints == null || Zone.PolygonPoints.Count == 0)
            {
                PolygonPointsForDisplay = "";
                return;
            }
            
            // 이제 Canvas에서 직접 그리므로 이 메서드는 비워둡니다
            // 실제 폴리곤은 UpdatePolygonOverlay()에서 처리됩니다
            PolygonPointsForDisplay = "";
            
            System.Diagnostics.Debug.WriteLine($"UpdatePolygonPointsForDisplay called with {Zone.PolygonPoints.Count} points");
        }
        
        private async void LoadCameraFrame()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"LoadCameraFrame called for Camera: {Zone.CameraId}");
                
                // 선택된 카메라의 현재 프레임 로드 시도
                if (!string.IsNullOrEmpty(Zone.CameraId) && App.CameraService != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Attempting to get latest frame from camera: {Zone.CameraId}");
                    
                    // 먼저 카메라가 연결되어 있는지 확인
                    var connectedCameras = App.CameraService.GetConnectedCameras();
                    var targetCamera = connectedCameras.FirstOrDefault(c => c.Id == Zone.CameraId);
                    
                    if (targetCamera == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Camera {Zone.CameraId} is not connected. Checking available cameras...");
                        
                        // 사용 가능한 카메라 목록에서 찾기
                        var availableCamera = AvailableCameras.FirstOrDefault(c => c.Id == Zone.CameraId);
                        if (availableCamera != null && availableCamera.IsEnabled)
                        {
                            System.Diagnostics.Debug.WriteLine($"Found available camera {Zone.CameraId}. Attempting to connect...");
                            
                            try
                            {
                                // 카메라 연결 시도
                                var connected = await App.CameraService.ConnectCamera(availableCamera);
                                if (!connected)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Failed to connect camera {Zone.CameraId}");
                                    CreateTestImage();
                                    return;
                                }
                                System.Diagnostics.Debug.WriteLine($"Successfully connected camera {Zone.CameraId}");
                                
                                // 연결 후 약간 대기 (카메라 초기화 시간)
                                await System.Threading.Tasks.Task.Delay(1000);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error connecting camera {Zone.CameraId}: {ex.Message}");
                                CreateTestImage();
                                return;
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Camera {Zone.CameraId} is not available or disabled");
                            CreateTestImage();
                            return;
                        }
                    }
                    
                    // 카메라 서비스에서 최신 프레임 가져오기
                    var latestFrame = App.CameraService.GetLatestFrame(Zone.CameraId);
                    if (latestFrame != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Got frame from camera: {latestFrame.Width}x{latestFrame.Height}");
                        
                        // OpenCV Mat을 BitmapSource로 변환
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                using var frame = latestFrame.Clone();
                                CameraFrame = OpenCvSharp.WpfExtensions.WriteableBitmapConverter.ToWriteableBitmap(frame);
                                System.Diagnostics.Debug.WriteLine("Successfully converted frame to WriteableBitmap");
                                
                                // 이미지 로드 후 폴리곤 업데이트 요청
                                PolygonUpdateRequested?.Invoke(this, EventArgs.Empty);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Frame conversion error: {ex.Message}");
                                CreateTestImage();
                            }
                        });
                        return;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"No latest frame available for camera: {Zone.CameraId}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"CameraId is empty or CameraService is null. CameraId: '{Zone.CameraId}', CameraService: {App.CameraService != null}");
                }
                
                // 카메라 프레임을 가져올 수 없으면 테스트 이미지 생성
                System.Diagnostics.Debug.WriteLine("Creating test image as fallback");
                CreateTestImage();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadCameraFrame error: {ex.Message}");
                CreateTestImage();
            }
        }
        
        private void CreateTestImage()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    // 테스트용 이미지 생성 (회색 배경에 격자 패턴)
                    var bitmap = new WriteableBitmap(640, 480, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null);
                    
                    var stride = bitmap.PixelWidth * 3; // BGR24 = 3 bytes per pixel
                    var pixelData = new byte[stride * bitmap.PixelHeight];
                    
                    // 격자 패턴 생성
                    for (int y = 0; y < bitmap.PixelHeight; y++)
                    {
                        for (int x = 0; x < bitmap.PixelWidth; x++)
                        {
                            var index = y * stride + x * 3;
                            
                            // 50x50 격자 패턴
                            bool isGrid = (x % 50 == 0) || (y % 50 == 0);
                            
                            if (isGrid)
                            {
                                // 회색 격자 선
                                pixelData[index] = 128;     // B
                                pixelData[index + 1] = 128; // G
                                pixelData[index + 2] = 128; // R
                            }
                            else
                            {
                                // 연한 회색 배경
                                pixelData[index] = 200;     // B
                                pixelData[index + 1] = 200; // G
                                pixelData[index + 2] = 200; // R
                            }
                        }
                    }
                    
                    // 비트맵에 데이터 쓰기
                    bitmap.WritePixels(
                        new System.Windows.Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight),
                        pixelData,
                        stride,
                        0);
                    
                    CameraFrame = bitmap;
                    
                    // 테스트 이미지 로드 후 폴리곤 업데이트 요청
                    PolygonUpdateRequested?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CreateTestImage error: {ex.Message}");
                }
            });
        }
    }
}