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
using OpenCvSharp.WpfExtensions;
using SafetyVisionMonitor.Helpers;
using SafetyVisionMonitor.Models;
using SafetyVisionMonitor.ViewModels.Base;
using SafetyVisionMonitor.Services;
using Point = OpenCvSharp.Point;

namespace SafetyVisionMonitor.ViewModels
{
    public partial class ZoneSetupViewModel : BaseViewModel
    {
        // 카메라별 3D 구역 데이터 - Dictionary로 카메라별 관리
        private readonly Dictionary<string, ObservableCollection<ZoneVisualization>> _cameraWarningZones = new();
        private readonly Dictionary<string, ObservableCollection<ZoneVisualization>> _cameraDangerZones = new();
        
        [ObservableProperty]
        private ObservableCollection<Zone3D> zones;
        
        [ObservableProperty]
        private Zone3D? selectedZone;
        
        [ObservableProperty]
        private ObservableCollection<Camera> availableCameras;
        
        [ObservableProperty]
        private Camera? selectedCamera;
        
        [ObservableProperty]
        private BitmapSource? currentCameraFrame;
        
        [ObservableProperty]
        private bool isDrawingMode = false;
        
        [ObservableProperty]
        private string drawingModeText = "그리기 시작";
        
        [ObservableProperty]
        private ObservableCollection<System.Windows.Point> tempDrawingPoints;
        
        [ObservableProperty]
        private ZoneType newZoneType = ZoneType.Warning;
        
        [ObservableProperty]
        private double newZoneHeight = 2.0;
        
        [ObservableProperty]
        private bool showZoneOverlay = true;
        
        [ObservableProperty] 
        private bool isCalibrationMode = false;
        
        [ObservableProperty]
        private string calibrationStep = "1단계: 참조점 설정";
        
        [ObservableProperty]
        private ObservableCollection<System.Windows.Point> referencePoints = new();
        
        [ObservableProperty]
        private double referenceDistance = 1.0; // 참조 거리 (미터)
        
        [ObservableProperty]
        private double cameraHeight = 3.0; // 카메라 높이 (미터)
        
        [ObservableProperty]
        private double pixelsPerMeter = 100.0; // 계산된 픽셀/미터 비율
        
        // 시각적 피드백을 위한 속성들
        [ObservableProperty]
        private ObservableCollection<System.Windows.Point> visualPoints = new(); // 화면에 표시할 점들
        
        [ObservableProperty]
        private ObservableCollection<System.Windows.Point> tempPolygonPoints = new(); // 임시 다각형 점들
        
        // WPF Polygon에서 사용할 수 있는 PointCollection 반환 (임시 다각형용)
        public System.Windows.Media.PointCollection TempPolygonPointCollection
        {
            get
            {
                var pointCollection = new System.Windows.Media.PointCollection();
                foreach (var point in TempPolygonPoints)
                {
                    pointCollection.Add(point);
                }
                return pointCollection;
            }
        }
        
        // TempPolygonPoints 변경 시 TempPolygonPointCollection도 알림
        partial void OnTempPolygonPointsChanged(ObservableCollection<System.Windows.Point> value)
        {
            OnPropertyChanged(nameof(TempPolygonPointCollection));
        }
        
        [ObservableProperty]
        private ObservableCollection<ZoneVisualization> zoneVisualizations = new(); // 저장된 구역들의 시각화
        
        public ZoneSetupViewModel()
        {
            Title = "3D 영역 설정";
            Zones = new ObservableCollection<Zone3D>();
            AvailableCameras = new ObservableCollection<Camera>();
            TempDrawingPoints = new ObservableCollection<System.Windows.Point>();
            
            // 구역 시각화 업데이트 이벤트 구독
            App.AppData.ZoneVisualizationUpdateRequested += OnZoneVisualizationUpdateRequested;
            
            LoadDataAsync();
        }
        
        private async void LoadDataAsync()
        {
            await LoadCamerasAsync();
            await LoadZonesAsync();
            
            // 테스트용: 구역이 없으면 샘플 구역 추가
            if (Zones.Count == 0)
            {
                CreateTestZone();
            }
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
                
                // 샘플 카메라가 없으면 추가
                if (!AvailableCameras.Any())
                {
                    LoadSampleCameras();
                }
                
                SelectedCamera = AvailableCameras.FirstOrDefault();
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
                    System.Diagnostics.Debug.WriteLine($"LoadZonesAsync: Loading zone {zone.Name}, IsEnabled={zone.IsEnabled}");
                    Zones.Add(zone);
                }
                
                // 구역 로드 후 시각화 업데이트
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
            // 샘플 카메라 추가
            for (int i = 0; i < 4; i++)
            {
                AvailableCameras.Add(new Camera
                {
                    Id = $"CAM{i + 1:D3}",
                    Name = $"카메라 {i + 1}",
                    IsConnected = i == 0 // 첫 번째만 연결된 것으로
                });
            }
            
            SelectedCamera = AvailableCameras.FirstOrDefault();
        }
        
        /// <summary>
        /// 2D 화면 좌표를 실제 3D 바닥면 좌표로 변환
        /// </summary>
        private Point2D ConvertTo3DFloorPoint(System.Windows.Point screenPoint)
        {
            var pixelsPerMeter = PixelsPerMeter > 0 ? PixelsPerMeter : 100.0;
            var frameWidth = CurrentCameraFrame?.PixelWidth ?? 640;
            var frameHeight = CurrentCameraFrame?.PixelHeight ?? 480;
            
            return CoordinateTransformService.ScreenToWorld(screenPoint, frameWidth, frameHeight, pixelsPerMeter);
        }
        
        /// <summary>
        /// 3D 바닥 좌표를 2D 화면 좌표로 역변환 (구역 표시용)
        /// </summary>
        private System.Windows.Point ConvertToScreenPoint(Point2D worldPoint)
        {
            var pixelsPerMeter = PixelsPerMeter > 0 ? PixelsPerMeter : 100.0;
            var frameWidth = CurrentCameraFrame?.PixelWidth ?? 640;
            var frameHeight = CurrentCameraFrame?.PixelHeight ?? 480;
            
            System.Diagnostics.Debug.WriteLine($"ConvertToScreenPoint: World({worldPoint.X:F2}, {worldPoint.Y:F2}), Frame({frameWidth}x{frameHeight}), PixelsPerMeter={pixelsPerMeter:F1}");
            
            var screenPoint = CoordinateTransformService.WorldToScreen(worldPoint, frameWidth, frameHeight, pixelsPerMeter);
            System.Diagnostics.Debug.WriteLine($"ConvertToScreenPoint: Result({screenPoint.X:F1}, {screenPoint.Y:F1})");
            
            return screenPoint;
        }
        
        private void SubscribeToCameraFrame(Camera camera)
        {
            try
            {
                // 기존 구독 해제
                App.CameraService.FrameReceivedForUI -= OnFrameReceived;
                
                if (camera.IsEnabled)
                {
                    // 새 카메라 프레임 구독
                    App.CameraService.FrameReceivedForUI += OnFrameReceived;
                    StatusMessage = $"{camera.Name}의 프레임을 표시합니다.";
                }
                else
                {
                    CurrentCameraFrame = null;
                    StatusMessage = $"{camera.Name}이 연결되지 않았습니다.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"카메라 프레임 구독 실패: {ex.Message}";
            }
        }
        
        private void OnFrameReceived(object? sender, Services.CameraFrameEventArgs e)
        {
            if (SelectedCamera != null && e.CameraId == SelectedCamera.Id)
            {
                // UI 스레드에서 업데이트
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    //CurrentCameraFrame = e.Frame.ToBitmapSource();
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
                                    CurrentCameraFrame = bitmap;
                                }
                                
                                if (!SelectedCamera.IsConnected)
                                {
                                    SelectedCamera.IsConnected = true;
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
        }
        
        // 정리 메소드
        public void Cleanup()
        {
            App.CameraService.FrameReceivedForUI -= OnFrameReceived;
        }
        
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
                
                // 캘리브레이션 정보를 카메라에 저장
                SelectedCamera.CalibrationPixelsPerMeter = PixelsPerMeter;
                SelectedCamera.IsCalibrated = true;
                
                // 데이터베이스에 저장
                try
                {
                    await App.DatabaseService.SaveCameraConfigAsync(SelectedCamera);
                    StatusMessage = $"캘리브레이션 완료 및 저장! 스케일: {PixelsPerMeter:F1} 픽셀/미터";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"캘리브레이션 완료! 저장 실패: {ex.Message} (스케일: {PixelsPerMeter:F1} px/m)";
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
                UpdateVisualFeedback();
                StatusMessage = "그리기 모드가 취소되었습니다.";
            }
            else
            {
                StatusMessage = "그리기 모드 활성화! 바닥면의 4개 점을 클릭하세요.";
            }
            
            System.Diagnostics.Debug.WriteLine($"Drawing mode changed to: {IsDrawingMode}");
        }
        
        [RelayCommand]
        private void AddZone()
        {
            if (TempDrawingPoints.Count != 4)
            {
                MessageBox.Show("바닥면의 4개 점을 모두 선택해주세요.", "알림",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var newZone = new Zone3D
            {
                Id = Guid.NewGuid().ToString(),
                Name = $"{(NewZoneType == ZoneType.Warning ? "경고" : "위험")}구역 {Zones.Count + 1}",
                Type = NewZoneType,
                CameraId = SelectedCamera!.Id,
                DisplayColor = NewZoneType == ZoneType.Warning ? Colors.Orange : Colors.Red,
                Opacity = 0.3,
                Height = NewZoneHeight,
                IsEnabled = true, // 새로 생성되는 구역은 기본적으로 활성화
                CreatedDate = DateTime.Now,
                // 캘리브레이션 정보 설정
                CalibrationPixelsPerMeter = SelectedCamera.IsCalibrated ? SelectedCamera.CalibrationPixelsPerMeter : 100.0,
                CalibrationFrameWidth = CurrentCameraFrame?.PixelWidth ?? 640,
                CalibrationFrameHeight = CurrentCameraFrame?.PixelHeight ?? 480
            };
            
            // 2D 점들을 실제 3D 바닥 좌표로 변환
            foreach (var point in TempDrawingPoints)
            {
                var realWorldPoint = ConvertTo3DFloorPoint(point);
                newZone.FloorPoints.Add(realWorldPoint);
            }
            
            Zones.Add(newZone);
            SelectedZone = newZone;
            
            // 그리기 모드 종료
            IsDrawingMode = false;
            DrawingModeText = "그리기 시작";
            TempDrawingPoints.Clear();
            
            // 시각적 피드백 업데이트
            UpdateVisualFeedback();
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
                    await App.DatabaseService.DeleteZone3DConfigAsync(zone.Id);
                    Zones.Remove(zone);
                    if (SelectedZone == zone)
                    {
                        SelectedZone = null;
                    }
                    StatusMessage = $"'{zone.Name}' 구역이 삭제되었습니다.";
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
        private void ToggleZone(Zone3D zone)
        {
            // 이제 IsEnabled 변경은 바인딩을 통해 자동으로 처리됨
            // 이 Command는 시각화 업데이트만 담당 (필요한 경우에만 호출)
            System.Diagnostics.Debug.WriteLine($"ToggleZone command called for {zone.Name}, current IsEnabled={zone.IsEnabled}");
            
            // 시각화 업데이트 (IsEnabled 상태에 따라 투명도 변경)
            UpdateZoneVisualizations();
            
            StatusMessage = $"'{zone.Name}' 구역이 {(zone.IsEnabled ? "활성화" : "비활성화")}되었습니다.";
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
                StatusMessage = "모든 구역이 삭제되었습니다.";
                UpdateVisualFeedback();
                UpdateZoneVisualizations();
            }
        }
        
        [RelayCommand]
        private async Task SaveZones()
        {
            try
            {
                // 각 구역에 캘리브레이션 정보 저장
                foreach (var zone in Zones)
                {
                    if (zone.CameraId == SelectedCamera?.Id)
                    {
                        // 현재 카메라의 캘리브레이션 정보를 구역에 저장
                        var pixelsPerMeter = SelectedCamera?.IsCalibrated == true ? 
                            SelectedCamera.CalibrationPixelsPerMeter : 100.0;
                        
                        zone.CalibrationPixelsPerMeter = pixelsPerMeter;
                        zone.CalibrationFrameWidth = CurrentCameraFrame?.PixelWidth ?? 640;
                        zone.CalibrationFrameHeight = CurrentCameraFrame?.PixelHeight ?? 480;
                        
                        System.Diagnostics.Debug.WriteLine($"Saving zone {zone.Name} with calibration: {pixelsPerMeter:F1} px/m, frame: {zone.CalibrationFrameWidth}x{zone.CalibrationFrameHeight}");
                    }
                }
                
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
        
        public void OnCanvasClick(System.Windows.Point clickPoint)
        {
            System.Diagnostics.Debug.WriteLine($"OnCanvasClick called at ({clickPoint.X}, {clickPoint.Y})");
            System.Diagnostics.Debug.WriteLine($"IsCalibrationMode: {IsCalibrationMode}, IsDrawingMode: {IsDrawingMode}");
            
            if (IsCalibrationMode)
            {
                HandleCalibrationClick(clickPoint);
                return;
            }
            
            if (!IsDrawingMode)
            {
                System.Diagnostics.Debug.WriteLine("Not in drawing mode, returning");
                return;
            }
                
            if (TempDrawingPoints.Count < 4)
            {
                TempDrawingPoints.Add(clickPoint);
                System.Diagnostics.Debug.WriteLine($"Added point {TempDrawingPoints.Count}: ({clickPoint.X}, {clickPoint.Y})");
                
                // 시각적 피드백 업데이트
                UpdateVisualFeedback();
                
                if (TempDrawingPoints.Count == 4)
                {
                    StatusMessage = "4개 점이 모두 선택되었습니다. '구역 추가' 버튼을 클릭하세요.";
                    System.Diagnostics.Debug.WriteLine("4 points completed!");
                }
                else
                {
                    StatusMessage = $"바닥 점 {TempDrawingPoints.Count}/4 선택됨";
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Already have 4 points");
            }
        }
        
        private void HandleCalibrationClick(System.Windows.Point clickPoint)
        {
            if (ReferencePoints.Count < 2)
            {
                ReferencePoints.Add(clickPoint);
                
                // 캘리브레이션 점 시각화 업데이트
                UpdateCalibrationVisuals();
                
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
                
                // 두 점 사이의 픽셀 거리 계산
                var pixelDistance = Math.Sqrt(
                    Math.Pow(point2.X - point1.X, 2) + 
                    Math.Pow(point2.Y - point1.Y, 2)
                );
                
                // 픽셀/미터 비율 계산
                PixelsPerMeter = pixelDistance / ReferenceDistance;
                
                System.Diagnostics.Debug.WriteLine($"Calibration calculated: {pixelDistance:F1} pixels = {ReferenceDistance:F1} meters -> {PixelsPerMeter:F1} px/m");
            }
        }
        
        partial void OnSelectedCameraChanged(Camera? value)
        {
            if (value != null)
            {
                // 카메라의 캘리브레이션 정보 복원
                PixelsPerMeter = value.CalibrationPixelsPerMeter;
                if (value.IsCalibrated)
                {
                    StatusMessage = $"{value.Name} 캘리브레이션 정보 로드됨: {PixelsPerMeter:F1} px/m";
                }
                else
                {
                    StatusMessage = $"{value.Name} 선택됨. 캘리브레이션이 필요합니다.";
                }
                
                // 카메라 변경 시 해당 카메라의 구역만 표시
                UpdateZonesForCamera(value.Id);
                
                // 카메라 프레임 구독
                SubscribeToCameraFrame(value);
            }
        }
        
        private async void UpdateZonesForCamera(string cameraId)
        {
            try
            {
                // 해당 카메라의 구역만 로드
                var cameraZones = await App.DatabaseService.LoadZone3DConfigsAsync(cameraId);
                
                // 기존 구역들을 모두 지우고 새로 로드된 구역들만 표시
                Zones.Clear();
                
                // DB에서 로드한 해당 카메라의 구역들만 추가
                foreach (var dbZone in cameraZones)
                {
                    System.Diagnostics.Debug.WriteLine($"UpdateZonesForCamera: Loading zone {dbZone.Name}, IsEnabled={dbZone.IsEnabled}");
                    Zones.Add(dbZone);
                }
                
                // 시각화 업데이트 (OpenCV용 컬렉션도 함께 업데이트됨)
                UpdateZoneVisualizations();
                
                StatusMessage = $"{cameraId}의 구역 {cameraZones.Count}개를 표시합니다.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"구역 로드 실패: {ex.Message}";
            }
        }
        
        /// <summary>
        /// 실시간 시각적 피드백 업데이트
        /// </summary>
        private void UpdateVisualFeedback()
        {
            // 클릭한 점들 표시
            VisualPoints.Clear();
            foreach (var point in TempDrawingPoints)
            {
                VisualPoints.Add(point);
            }
            
            // 캘리브레이션 점들 표시
            foreach (var point in ReferencePoints)
            {
                VisualPoints.Add(point);
            }
            
            // 임시 다각형 업데이트
            UpdateTempPolygon();
        }
        
        /// <summary>
        /// 임시 다각형 업데이트 (4개 점 연결)
        /// </summary>
        private void UpdateTempPolygon()
        {
            TempPolygonPoints.Clear();
            
            if (TempDrawingPoints.Count >= 3)
            {
                foreach (var point in TempDrawingPoints)
                {
                    TempPolygonPoints.Add(point);
                }
                
                // 4개 점이 모두 있으면 다각형 닫기
                if (TempDrawingPoints.Count == 4)
                {
                    TempPolygonPoints.Add(TempDrawingPoints[0]); // 첫 점으로 돌아가서 닫기
                }
            }
            
            // UI에 업데이트 알림
            OnPropertyChanged(nameof(TempPolygonPointCollection));
        }
        
        /// <summary>
        /// 캘리브레이션 시각화 업데이트
        /// </summary>
        private void UpdateCalibrationVisuals()
        {
            VisualPoints.Clear();
            
            foreach (var point in ReferencePoints)
            {
                VisualPoints.Add(point);
            }
            
            // 캘리브레이션 선 표시
            if (ReferencePoints.Count == 2)
            {
                TempPolygonPoints.Clear();
                TempPolygonPoints.Add(ReferencePoints[0]);
                TempPolygonPoints.Add(ReferencePoints[1]);
                
                // UI에 업데이트 알림
                OnPropertyChanged(nameof(TempPolygonPointCollection));
            }
        }
        
        /// <summary>
        /// 저장된 구역들의 시각화 업데이트
        /// </summary>
        private void UpdateZoneVisualizations()
        {
            System.Diagnostics.Debug.WriteLine($"UpdateZoneVisualizations called. Zones count: {Zones.Count}");
            
            // XAML용 시각화 업데이트 (임시 그리기용으로만 사용)
            ZoneVisualizations.Clear();
            
            foreach (var zone in Zones)
            {
                System.Diagnostics.Debug.WriteLine($"Processing zone: {zone.Name}, FloorPoints: {zone.FloorPoints.Count}");
                
                if (zone.FloorPoints.Count >= 3)
                {
                    var visualization = new ZoneVisualization
                    {
                        ZoneId = zone.Id,
                        Name = zone.Name,
                        ZoneColor = zone.DisplayColor,
                        Opacity = zone.IsEnabled ? zone.Opacity : 0.05, // 비활성화시 거의 투명하게
                        IsSelected = zone == SelectedZone,
                        IsEnabled = zone.IsEnabled
                    };
                    
                    System.Diagnostics.Debug.WriteLine($"Created visualization for {zone.Name}: IsEnabled={zone.IsEnabled}, Opacity={visualization.Opacity}");
                    
                    // 3D 좌표를 화면 좌표로 변환
                    foreach (var worldPoint in zone.FloorPoints)
                    {
                        var screenPoint = ConvertToScreenPoint(worldPoint);
                        visualization.AddPoint(screenPoint);
                        System.Diagnostics.Debug.WriteLine($"World ({worldPoint.X:F1}, {worldPoint.Y:F1}) -> Screen ({screenPoint.X:F1}, {screenPoint.Y:F1})");
                    }
                    
                    // 다각형 닫기 (첫 점으로 돌아가기)
                    if (zone.FloorPoints.Count >= 3)
                    {
                        var firstScreenPoint = ConvertToScreenPoint(zone.FloorPoints[0]);
                        visualization.AddPoint(firstScreenPoint);
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"Zone visualization has {visualization.ScreenPoints.Count} screen points");
                    ZoneVisualizations.Add(visualization);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Zone {zone.Name} has insufficient points: {zone.FloorPoints.Count}");
                }
            }
            
            // OpenCV용 카메라별 컬렉션 업데이트
            PopulateCameraZoneCollections();
            
            System.Diagnostics.Debug.WriteLine($"Total visualizations created: {ZoneVisualizations.Count}");
        }
        
        /// <summary>
        /// 구역 시각화 업데이트 요청 이벤트 핸들러
        /// </summary>
        private void OnZoneVisualizationUpdateRequested(object? sender, EventArgs e)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                System.Diagnostics.Debug.WriteLine("ZoneSetupViewModel: Received zone visualization update request");
                UpdateZoneVisualizations();
            });
        }
        
        /// <summary>
        /// Zones 컬렉션에서 OpenCV용 카메라별 구역 컬렉션으로 데이터 변환
        /// </summary>
        private void PopulateCameraZoneCollections()
        {
            System.Diagnostics.Debug.WriteLine($"PopulateCameraZoneCollections: Processing {Zones.Count} zones");
            
            // 기존 컬렉션 초기화
            _cameraWarningZones.Clear();
            _cameraDangerZones.Clear();
            
            foreach (var zone in Zones)
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
                    
                    // 상대 좌표 변환 (0~1 범위)
                    var originalFrameWidth = zone.CalibrationFrameWidth;
                    var originalFrameHeight = zone.CalibrationFrameHeight;
                    
                    foreach (var worldPoint in zone.FloorPoints)
                    {
                        // 원본 프레임 크기로 화면 좌표 계산
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
                    
                    System.Diagnostics.Debug.WriteLine($"Added {zone.Type} zone '{zone.Name}' for camera {zone.CameraId}");
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"PopulateCameraZoneCollections completed: {_cameraWarningZones.Count} warning cameras, {_cameraDangerZones.Count} danger cameras");
        }
        
        /// <summary>
        /// 테스트용 샘플 구역 생성
        /// </summary>
        private void CreateTestZone()
        {
            try
            {
                var testZone = new Zone3D
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "테스트 구역",
                    Type = ZoneType.Warning,
                    CameraId = SelectedCamera?.Id ?? "CAM001",
                    DisplayColor = System.Windows.Media.Colors.Yellow,
                    Opacity = 0.3,
                    IsEnabled = true,
                    Height = 2.0,
                    CalibrationPixelsPerMeter = 100.0,
                    CalibrationFrameWidth = 640,
                    CalibrationFrameHeight = 480
                };
                
                // 사각형 구역 생성 (화면 중앙 부근)
                testZone.FloorPoints.Add(new Point2D(1.0, 1.0));   // 좌상
                testZone.FloorPoints.Add(new Point2D(3.0, 1.0));   // 우상  
                testZone.FloorPoints.Add(new Point2D(3.0, 3.0));   // 우하
                testZone.FloorPoints.Add(new Point2D(1.0, 3.0));   // 좌하
                
                Zones.Add(testZone);
                
                // 시각화 업데이트 (OpenCV용 컬렉션도 함께 업데이트됨)
                UpdateZoneVisualizations();
                
                System.Diagnostics.Debug.WriteLine("CreateTestZone: Test zone created and visualization updated");
                StatusMessage = "테스트 구역이 생성되었습니다.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateTestZone error: {ex.Message}");
                StatusMessage = $"테스트 구역 생성 실패: {ex.Message}";
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
                
                System.Diagnostics.Debug.WriteLine($"DrawZoneOverlaysOnFrame for {cameraId}: Frame {frameWidth}x{frameHeight}");
                
                // 경고 구역 그리기 (주황색)
                if (_cameraWarningZones.ContainsKey(cameraId))
                {
                    var warningZones = _cameraWarningZones[cameraId];
                    System.Diagnostics.Debug.WriteLine($"Drawing {warningZones.Count} warning zones for {cameraId}");
                    
                    foreach (var zone in warningZones)
                    {
                        if (zone.IsEnabled && zone.RelativePoints.Count >= 3)
                        {
                            System.Diagnostics.Debug.WriteLine($"Drawing warning zone: {zone.Name} (enabled: {zone.IsEnabled}, points: {zone.RelativePoints.Count})");
                            DrawZoneOnFrame(frameWithZones, zone, frameWidth, frameHeight, new Scalar(0, 165, 255)); // Orange
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Skipping warning zone: {zone.Name} (enabled: {zone.IsEnabled}, points: {zone.RelativePoints.Count})");
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"No warning zones found for camera {cameraId}");
                }
                
                // 위험 구역 그리기 (빨간색)
                if (_cameraDangerZones.ContainsKey(cameraId))
                {
                    var dangerZones = _cameraDangerZones[cameraId];
                    System.Diagnostics.Debug.WriteLine($"Drawing {dangerZones.Count} danger zones for {cameraId}");
                    
                    foreach (var zone in dangerZones)
                    {
                        if (zone.IsEnabled && zone.RelativePoints.Count >= 3)
                        {
                            System.Diagnostics.Debug.WriteLine($"Drawing danger zone: {zone.Name} (enabled: {zone.IsEnabled}, points: {zone.RelativePoints.Count})");
                            DrawZoneOnFrame(frameWithZones, zone, frameWidth, frameHeight, new Scalar(0, 0, 255)); // Red
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Skipping danger zone: {zone.Name} (enabled: {zone.IsEnabled}, points: {zone.RelativePoints.Count})");
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"No danger zones found for camera {cameraId}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ZoneSetup: Zone drawing error: {ex.Message}");
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
                    
                    // 구역 이름 표시
                    if (points.Count > 0)
                    {
                        var centerX = (int)points.Average(p => p.X);
                        var centerY = (int)points.Average(p => p.Y);
                        
                        Cv2.PutText(frame, zone.Name, new Point(centerX - 30, centerY), 
                            HersheyFonts.HersheySimplex, 0.6, new Scalar(255, 255, 255), 2);
                    }
                    
                    overlay.Dispose();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ZoneSetup: Individual zone drawing error for {zone.Name}: {ex.Message}");
            }
        }
    }
    
}