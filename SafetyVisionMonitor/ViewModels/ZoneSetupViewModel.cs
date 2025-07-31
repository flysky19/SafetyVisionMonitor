using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using SafetyVisionMonitor.Models;
using SafetyVisionMonitor.ViewModels.Base;
using SafetyVisionMonitor.Services;
using Point = System.Windows.Point;

namespace SafetyVisionMonitor.ViewModels
{
    public partial class ZoneSetupViewModel : BaseViewModel
    {
        private readonly FrameRenderer _frameRenderer;
        private readonly FrameProcessingService _frameProcessor;
        private CoordinateMapper _coordinateMapper;
        private readonly Dictionary<string, ObservableCollection<ZoneVisualization>> _cameraZones = new();

        #region Observable Properties

        [ObservableProperty]
        private ObservableCollection<Zone3D> zones = new();

        [ObservableProperty]
        private Zone3D? selectedZone;

        [ObservableProperty]
        private ObservableCollection<Camera> availableCameras = new();

        [ObservableProperty]
        private Camera? selectedCamera;

        [ObservableProperty]
        private WriteableBitmap? frameBitmap;

        [ObservableProperty]
        private double frameWidth = 640;

        [ObservableProperty]
        private double frameHeight = 480;

        [ObservableProperty]
        private bool isDrawingMode = false;

        [ObservableProperty]
        private string drawingModeText = "구역 설정 시작";

        [ObservableProperty]
        private ObservableCollection<Point> tempDrawingPoints = new();

        [ObservableProperty]
        private ZoneType newZoneType = ZoneType.Warning;

        [ObservableProperty]
        private double newZoneHeight = 2.0;

        [ObservableProperty]
        private bool showZoneOverlay = true;

        [ObservableProperty]
        private ObservableCollection<ZoneVisualization> zoneVisualizations = new();

        // 드래그 관련
        [ObservableProperty]
        private bool isDragging = false;

        [ObservableProperty]
        private Point dragStartPoint;

        [ObservableProperty]
        private Point dragCurrentPoint;


        // 캘리브레이션 관련
        [ObservableProperty]
        private bool isCalibrationMode = false;

        [ObservableProperty]
        private string calibrationStep = "1단계: 참조점 설정";

        [ObservableProperty]
        private ObservableCollection<Point> referencePoints = new();

        [ObservableProperty]
        private double referenceDistance = 1.0;

        [ObservableProperty]
        private double pixelsPerMeter = 100.0;

        #endregion

        public ZoneSetupViewModel()
        {
            Title = "3D 영역 설정";
            
            _frameRenderer = new FrameRenderer();
            _frameProcessor = new FrameProcessingService();
            _coordinateMapper = new CoordinateMapper(FrameWidth, FrameHeight);

            _frameRenderer.FrameSizeChanged += OnFrameSizeChanged;
            
            // 구역 시각화 업데이트 이벤트 구독
            App.AppData.ZoneVisualizationUpdateRequested += OnZoneVisualizationUpdateRequested;
            
            LoadDataAsync();
        }

        #region Data Loading

        private async void LoadDataAsync()
        {
            await LoadCamerasAsync();
            await LoadZonesAsync();
        }

        private async Task LoadCamerasAsync()
        {
            try
            {
                var cameras = await App.DatabaseService.LoadCameraConfigsAsync();
                AvailableCameras.Clear();
                
                foreach (var camera in cameras)
                {
                    AvailableCameras.Add(camera);
                }
                
                if (!AvailableCameras.Any())
                {
                    LoadSampleCameras();
                }
                
                // 연결된 카메라를 우선으로 선택
                SelectedCamera = AvailableCameras.FirstOrDefault(c => c.IsEnabled && c.IsConnected) 
                              ?? AvailableCameras.FirstOrDefault(c => c.IsEnabled)
                              ?? AvailableCameras.FirstOrDefault();
            }
            catch (Exception ex)
            {
                StatusMessage = $"카메라 로드 실패: {ex.Message}";
                LoadSampleCameras();
            }
        }

        private async Task LoadZonesAsync()
        {
            try
            {
                var zones = await App.DatabaseService.LoadZone3DConfigsAsync();
                Zones.Clear();
                
                foreach (var zone in zones)
                {
                    Zones.Add(zone);
                }
                
                UpdateZoneVisualizations();
                StatusMessage = $"구역 {zones.Count}개를 로드했습니다.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"구역 로드 실패: {ex.Message}";
            }
        }

        private void LoadSampleCameras()
        {
            for (int i = 0; i < 4; i++)
            {
                AvailableCameras.Add(new Camera
                {
                    Id = $"CAM{i + 1:D3}",
                    Name = $"카메라 {i + 1}",
                    IsConnected = i == 0
                });
            }
            
            SelectedCamera = AvailableCameras.FirstOrDefault();
        }

        #endregion

        #region Frame Processing

        private void SubscribeToCameraFrame(Camera camera)
        {
            try
            {
                // 기존 구독 해제
                App.CameraService.FrameReceivedForUI -= OnFrameReceived;
                App.CameraService.FrameReceivedForAI -= OnFrameReceived;
                
                if (camera.IsEnabled)
                {
                    // 고해상도 프레임 사용
                    App.CameraService.FrameReceivedForAI += OnFrameReceived;
                    StatusMessage = $"{camera.Name}의 프레임을 표시합니다.";
                }
                else
                {
                    _frameRenderer.Clear();
                    FrameBitmap = _frameRenderer.CurrentBitmap;
                    StatusMessage = $"{camera.Name}이 연결되지 않았습니다.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"카메라 프레임 구독 실패: {ex.Message}";
            }
        }

        private async void OnFrameReceived(object? sender, CameraFrameEventArgs e)
        {
            if (SelectedCamera != null && e.CameraId == SelectedCamera.Id)
            {
                // 프레임 처리를 큐에 추가 (최신 프레임만 처리됨)
                await _frameProcessor.QueueFrameAsync(e.CameraId, e.Frame, ProcessFrameAsync);
            }
        }

        private async Task ProcessFrameAsync(Mat frame)
        {
            // WriteableBitmap으로 렌더링
            await _frameRenderer.RenderFrameAsync(frame);
            
            // UI 스레드에서 업데이트
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                FrameBitmap = _frameRenderer.CurrentBitmap;
                
                if (!SelectedCamera.IsConnected)
                {
                    SelectedCamera.IsConnected = true;
                }
            });
        }

        private void OnFrameSizeChanged(object? sender, FrameSizeChangedEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                FrameWidth = e.Width;
                FrameHeight = e.Height;
                _coordinateMapper = new CoordinateMapper(e.Width, e.Height);
            });
        }

        #endregion

        #region Canvas Size Update

        public void UpdateCanvasSize(double canvasWidth, double canvasHeight)
        {
            _coordinateMapper?.UpdateCanvasSize(canvasWidth, canvasHeight);
        }

        #endregion

        #region Mouse Event Handlers

        public void OnCanvasMouseDown(Point clickPoint)
        {
            if (IsCalibrationMode)
            {
                HandleCalibrationClick(clickPoint);
                return;
            }
            
            
            if (IsDrawingMode)
            {
                HandleDrawingModeMouseDown(clickPoint);
                return;
            }
        }

        public void OnCanvasMouseMove(Point movePoint)
        {
            if (IsDrawingMode && IsDragging)
            {
                HandleDrawingModeMouseMove(movePoint);
            }
        }

        public void OnCanvasMouseUp(Point releasePoint)
        {
            if (IsDrawingMode && IsDragging)
            {
                HandleDrawingModeMouseUp(releasePoint);
            }
        }

        #endregion

        #region Drawing Mode Handlers

        private void HandleDrawingModeMouseDown(Point clickPoint)
        {
            IsDragging = true;
            DragStartPoint = clickPoint;
            DragCurrentPoint = clickPoint;
            TempDrawingPoints.Clear();
            StatusMessage = "마우스를 드래그하여 구역을 그리세요.";
        }

        private void HandleDrawingModeMouseMove(Point movePoint)
        {
            if (!IsDragging) return;
            
            DragCurrentPoint = movePoint;
            
            // 사각형 4개 포인트 계산
            TempDrawingPoints.Clear();
            TempDrawingPoints.Add(DragStartPoint);
            TempDrawingPoints.Add(new Point(DragCurrentPoint.X, DragStartPoint.Y));
            TempDrawingPoints.Add(DragCurrentPoint);
            TempDrawingPoints.Add(new Point(DragStartPoint.X, DragCurrentPoint.Y));
        }

        private void HandleDrawingModeMouseUp(Point releasePoint)
        {
            if (!IsDragging) return;
            
            IsDragging = false;
            DragCurrentPoint = releasePoint;
            
            // 최종 사각형 포인트
            TempDrawingPoints.Clear();
            TempDrawingPoints.Add(DragStartPoint);
            TempDrawingPoints.Add(new Point(DragCurrentPoint.X, DragStartPoint.Y));
            TempDrawingPoints.Add(DragCurrentPoint);
            TempDrawingPoints.Add(new Point(DragStartPoint.X, DragCurrentPoint.Y));
            
            StatusMessage = "구역이 그려졌습니다. '구역 추가' 버튼을 클릭하세요.";
        }

        #endregion


        #region Calibration Handlers

        private void HandleCalibrationClick(Point clickPoint)
        {
            if (ReferencePoints.Count < 2)
            {
                ReferencePoints.Add(clickPoint);
                
                if (ReferencePoints.Count == 1)
                {
                    CalibrationStep = "2단계: 참조 거리의 끝점 클릭";
                    StatusMessage = "참조 거리의 끝점을 클릭하세요.";
                }
                else if (ReferencePoints.Count == 2)
                {
                    CalculatePixelsPerMeter();
                    CalibrationStep = "3단계: 보정 완료";
                    StatusMessage = $"보정 완료! 1미터 = {PixelsPerMeter:F1} 픽셀";
                }
            }
        }

        private void CalculatePixelsPerMeter()
        {
            if (ReferencePoints.Count >= 2)
            {
                var point1 = ReferencePoints[0];
                var point2 = ReferencePoints[1];
                
                var pixelDistance = Math.Sqrt(
                    Math.Pow(point2.X - point1.X, 2) + 
                    Math.Pow(point2.Y - point1.Y, 2)
                );
                
                PixelsPerMeter = pixelDistance / ReferenceDistance;
            }
        }

        #endregion

        #region Zone Management

        private void UpdateZoneVisualizations()
        {
            ZoneVisualizations.Clear();
            
            if (SelectedCamera == null) return;
            
            foreach (var zone in Zones.Where(z => z.CameraId == SelectedCamera.Id && z.IsEnabled))
            {
                var visualization = new ZoneVisualization
                {
                    ZoneId = zone.Id,
                    Name = zone.Name,
                    ZoneColor = zone.DisplayColor,
                    Opacity = zone.Opacity,
                    IsEnabled = zone.IsEnabled,
                    Height = zone.Height
                };
                
                // 월드 좌표를 상대 좌표로 변환
                foreach (var worldPoint in zone.FloorPoints)
                {
                    var screenPoint = CoordinateTransformService.WorldToScreen(
                        worldPoint, 
                        zone.CalibrationFrameWidth, 
                        zone.CalibrationFrameHeight, 
                        zone.CalibrationPixelsPerMeter);
                    
                    var relativePoint = new Point(
                        screenPoint.X / zone.CalibrationFrameWidth,
                        screenPoint.Y / zone.CalibrationFrameHeight
                    );
                    
                    visualization.AddRelativePoint(relativePoint);
                }
                
                ZoneVisualizations.Add(visualization);
            }
        }

        #endregion

        #region Commands

        [RelayCommand]
        private void StartCalibration()
        {
            IsCalibrationMode = true;
            IsDrawingMode = false;
            ReferencePoints.Clear();
            CalibrationStep = "1단계: 참조점 설정";
            StatusMessage = "바닥면의 알려진 거리(1미터)의 시작점을 클릭하세요.";
        }

        [RelayCommand]
        private void CancelCalibration()
        {
            IsCalibrationMode = false;
            ReferencePoints.Clear();
            CalibrationStep = "1단계: 참조점 설정";
            StatusMessage = "캘리브레이션이 취소되었습니다.";
        }

        [RelayCommand]
        private async Task CompleteCalibration()
        {
            if (ReferencePoints.Count >= 2 && SelectedCamera != null)
            {
                IsCalibrationMode = false;
                
                SelectedCamera.CalibrationPixelsPerMeter = PixelsPerMeter;
                SelectedCamera.IsCalibrated = true;
                
                try
                {
                    await App.DatabaseService.SaveCameraConfigAsync(SelectedCamera);
                    StatusMessage = $"캘리브레이션 완료! 스케일: {PixelsPerMeter:F1} 픽셀/미터";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"캘리브레이션 저장 실패: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        private void StartDrawing()
        {
            if (SelectedCamera == null || !SelectedCamera.IsConnected)
            {
                MessageBox.Show("연결된 카메라를 선택해주세요.", "알림", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            IsDrawingMode = !IsDrawingMode;
            DrawingModeText = IsDrawingMode ? "그리기 취소" : "그리기 시작";
            
            if (!IsDrawingMode)
            {
                TempDrawingPoints.Clear();
                StatusMessage = "그리기 모드가 취소되었습니다.";
            }
            else
            {
                StatusMessage = "그리기 모드 활성화! 마우스를 드래그하여 구역을 그리세요.";
            }
        }

        [RelayCommand]
        private async Task AddZone()
        {
            if (TempDrawingPoints.Count != 4)
            {
                MessageBox.Show("바닥면의 4개 점을 모두 선택해주세요.", "알림",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var newZone = new Zone3D();
            
            // 로딩 플래그 설정하여 자동 저장 방지
            newZone.IsLoading = true;
            
            // 기본 속성 설정
            newZone.Id = Guid.NewGuid().ToString();
            newZone.Name = $"{(NewZoneType == ZoneType.Warning ? "경고" : "위험")}구역 {Zones.Count + 1}";
            newZone.Type = NewZoneType;
            newZone.CameraId = SelectedCamera!.Id;
            newZone.DisplayColor = NewZoneType == ZoneType.Warning ? Colors.Orange : Colors.Red;
            newZone.Opacity = 0.3;
            newZone.Height = NewZoneHeight;
            newZone.CreatedDate = DateTime.Now;
            newZone.CalibrationPixelsPerMeter = PixelsPerMeter;
            newZone.CalibrationFrameWidth = FrameWidth;
            newZone.CalibrationFrameHeight = FrameHeight;
            
            // 마지막에 IsEnabled 설정 (자동 저장 방지를 위해)
            newZone.IsEnabled = true;
            
            // 로딩 완료
            newZone.IsLoading = false;
            
            // 2D 점들을 실제 3D 바닥 좌표로 변환
            foreach (var point in TempDrawingPoints)
            {
                var worldPoint = CoordinateTransformService.ScreenToWorld(
                    point, FrameWidth, FrameHeight, PixelsPerMeter);
                newZone.FloorPoints.Add(worldPoint);
            }
            
            Zones.Add(newZone);
            SelectedZone = newZone;
            
            // 데이터베이스에 저장
            try
            {
                await App.DatabaseService.SaveZone3DConfigsAsync(new List<Zone3D> { newZone });
            }
            catch (Exception ex)
            {
                StatusMessage = $"구역 저장 실패: {ex.Message}";
            }
            
            // 그리기 모드 종료
            IsDrawingMode = false;
            DrawingModeText = "그리기 시작";
            TempDrawingPoints.Clear();
            IsDragging = false;
            
            UpdateZoneVisualizations();
            StatusMessage = $"'{newZone.Name}' 구역이 추가되었습니다.";
        }

        [RelayCommand]
        private async Task DeleteZone(Zone3D zone)
        {
            var result = MessageBox.Show($"'{zone.Name}' 구역을 삭제하시겠습니까?", "확인",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // 데이터베이스에서 삭제
                    await App.DatabaseService.DeleteZone3DConfigAsync(zone.Id);
                    
                    // 로컬 컴렉션에서 제거
                    Zones.Remove(zone);
                    
                    // 선택된 구역 해제
                    if (SelectedZone == zone)
                    {
                        SelectedZone = null;
                    }
                    
                    
                    // UI 시각화 업데이트
                    UpdateZoneVisualizations();
                    
                    // 다른 ViewModel들에도 알림
                    App.AppData.NotifyZoneVisualizationUpdate();
                    
                    StatusMessage = $"'{zone.Name}' 구역이 삭제되었습니다.";
                    
                    System.Diagnostics.Debug.WriteLine($"Zone '{zone.Name}' deleted successfully. Remaining zones: {Zones.Count}");
                }
                catch (Exception ex)
                {
                    StatusMessage = $"삭제 실패: {ex.Message}";
                    MessageBox.Show($"삭제 중 오류 발생: {ex.Message}", "오류",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private void ClearAllZones()
        {
            var result = MessageBox.Show("모든 구역을 삭제하시겠습니까?", "확인",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
                
            if (result == MessageBoxResult.Yes)
            {
                Zones.Clear();
                SelectedZone = null;
                TempDrawingPoints.Clear();
                UpdateZoneVisualizations();
                StatusMessage = "모든 구역이 삭제되었습니다.";
            }
        }

        [RelayCommand]
        private async Task SaveZones()
        {
            try
            {
                await App.DatabaseService.SaveZone3DConfigsAsync(Zones.ToList());
                
                MessageBox.Show("구역 설정이 저장되었습니다.", "저장 완료",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                    
                StatusMessage = "구역 설정이 저장되었습니다.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"저장 중 오류 발생: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"저장 실패: {ex.Message}";
            }
        }


        #endregion

        #region Property Changed Handlers

        partial void OnSelectedCameraChanged(Camera? value)
        {
            if (value != null)
            {
                PixelsPerMeter = value.CalibrationPixelsPerMeter;
                if (value.IsCalibrated)
                {
                    StatusMessage = $"{value.Name} 캘리브레이션 정보 로드됨: {PixelsPerMeter:F1} px/m";
                }
                else
                {
                    StatusMessage = $"{value.Name} 선택됨. 캘리브레이션이 필요합니다.";
                }
                
                UpdateZoneVisualizations();
                SubscribeToCameraFrame(value);
            }
        }

        #endregion

        #region Event Handlers

        private void OnZoneVisualizationUpdateRequested(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateZoneVisualizations();
            });
        }

        public void OnLoaded()
        {
            // View가 로드되었을 때 필요한 초기화
        }

        #endregion

        #region Cleanup

        public void Cleanup()
        {
            App.CameraService.FrameReceivedForUI -= OnFrameReceived;
            App.CameraService.FrameReceivedForAI -= OnFrameReceived;
            App.AppData.ZoneVisualizationUpdateRequested -= OnZoneVisualizationUpdateRequested;
            
            _frameRenderer?.Dispose();
            _frameProcessor?.Dispose();
        }

        #endregion
    }
}