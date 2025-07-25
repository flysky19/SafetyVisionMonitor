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
        
        // 드래그 그리기 관련 속성
        [ObservableProperty]
        private bool isDragging = false;
        
        [ObservableProperty]
        private System.Windows.Point dragStartPoint;
        
        [ObservableProperty]
        private System.Windows.Point dragCurrentPoint;
        
        [ObservableProperty]
        private ObservableCollection<System.Windows.Point> dragRectanglePoints = new();
        
        // 편집 모드 관련 속성
        [ObservableProperty]
        private bool isEditMode = false;
        
        [ObservableProperty]
        private Zone3D? editingZone;
        
        // editingPoints 제거 - EditingAbsolutePoints 프로퍼티로 대체됨
        
        // 편집용 상대 좌표 (0~1 범위)
        [ObservableProperty] 
        private ObservableCollection<System.Windows.Point> editingRelativePoints = new();
        
        [ObservableProperty]
        private int selectedPointIndex = -1;
        
        [ObservableProperty]
        private bool isDraggingPoint = false;
        
        // 성능 최적화를 위한 프레임 스키핑
        private DateTime _lastFrameUpdate = DateTime.MinValue;
        private readonly TimeSpan _minFrameInterval = TimeSpan.FromMilliseconds(50); // 20 FPS 제한
        
        // 편집 중인 구역을 시각적으로 표시하기 위한 PointCollection (상대 좌표 기반)
        public System.Windows.Media.PointCollection EditingPointCollection
        {
            get
            {
                var pointCollection = new System.Windows.Media.PointCollection();
                
                // 상대 좌표를 절대 좌표로 변환하여 사용
                foreach (var relativePoint in EditingRelativePoints)
                {
                    var absolutePoint = RelativeToAbsolute(relativePoint);
                    pointCollection.Add(absolutePoint);
                }
                
                // 다각형 닫기 (4개 점이 있을 때)
                if (EditingRelativePoints.Count >= 4)
                {
                    var firstAbsolutePoint = RelativeToAbsolute(EditingRelativePoints[0]);
                    pointCollection.Add(firstAbsolutePoint);
                }
                return pointCollection;
            }
        }
        
        // 편집 포인트들도 상대 좌표 기반으로 업데이트
        public ObservableCollection<System.Windows.Point> EditingAbsolutePoints
        {
            get
            {
                var absolutePoints = new ObservableCollection<System.Windows.Point>();
                foreach (var relativePoint in EditingRelativePoints)
                {
                    absolutePoints.Add(RelativeToAbsolute(relativePoint));
                }
                return absolutePoints;
            }
        }
        
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
        
        // zoneVisualizations 제거 - OpenCV 프레임에 직접 그리므로 불필요
        
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
            var pixelsPerMeter = SelectedCamera?.IsCalibrated == true ? SelectedCamera.CalibrationPixelsPerMeter : 100.0;
            var frameWidth = CurrentCameraFrame?.PixelWidth ?? 640;
            var frameHeight = CurrentCameraFrame?.PixelHeight ?? 480;
            
            return CoordinateTransformService.ScreenToWorld(screenPoint, frameWidth, frameHeight, pixelsPerMeter);
        }
        
        // 기존 ConvertToScreenPoint와 ConvertToScreenPointWithZoneCalibration 메서드들을 
        // ConvertWorldToScreenPoint로 통일하여 좌표 변환 로직 단순화
        
        /// <summary>
        /// 상대 좌표(0~1)를 현재 프레임의 절대 좌표로 변환
        /// </summary>
        private System.Windows.Point RelativeToAbsolute(System.Windows.Point relativePoint)
        {
            var frameWidth = CurrentCameraFrame?.PixelWidth ?? 640;
            var frameHeight = CurrentCameraFrame?.PixelHeight ?? 480;
            
            return new System.Windows.Point(
                relativePoint.X * frameWidth,
                relativePoint.Y * frameHeight
            );
        }
        
        /// <summary>
        /// 절대 좌표를 상대 좌표(0~1)로 변환
        /// </summary>
        private System.Windows.Point AbsoluteToRelative(System.Windows.Point absolutePoint)
        {
            var frameWidth = CurrentCameraFrame?.PixelWidth ?? 640;
            var frameHeight = CurrentCameraFrame?.PixelHeight ?? 480;
            
            return new System.Windows.Point(
                absolutePoint.X / frameWidth,
                absolutePoint.Y / frameHeight
            );
        }
        
        /// <summary>
        /// Canvas 좌표를 실제 이미지 좌표로 변환
        /// Canvas는 Image와 동일한 크기이고 Stretch="Uniform"이므로 직접 변환
        /// </summary>
        private System.Windows.Point ConvertCanvasToImageCoordinates(System.Windows.Point canvasPoint)
        {
            if (CurrentCameraFrame == null)
                return canvasPoint;
            
            // Canvas 오버레이는 Image와 같은 위치에 있고 투명하므로
            // Canvas 좌표가 곧 이미지 좌표임 (Stretch="Uniform"으로 인한 스케일링은 동일하게 적용)
            // 
            // Canvas와 Image가 모두 동일한 Grid 셀에 있고 같은 크기 제약을 받으므로
            // 좌표 변환 없이 직접 사용 가능
            
            System.Diagnostics.Debug.WriteLine($"Canvas to Image: ({canvasPoint.X:F1}, {canvasPoint.Y:F1}) -> No conversion needed");
            
            return canvasPoint;
        }
        
        /// <summary>
        /// 월드 좌표를 현재 캘리브레이션 기준으로 화면 좌표로 변환 (단순화)
        /// </summary>
        private System.Windows.Point ConvertWorldToScreenPoint(Point2D worldPoint)
        {
            var frameWidth = CurrentCameraFrame?.PixelWidth ?? 640;
            var frameHeight = CurrentCameraFrame?.PixelHeight ?? 480;
            var pixelsPerMeter = SelectedCamera?.IsCalibrated == true ? SelectedCamera.CalibrationPixelsPerMeter : 100.0;
            
            return CoordinateTransformService.WorldToScreen(worldPoint, frameWidth, frameHeight, pixelsPerMeter);
        }
        
        private void SubscribeToCameraFrame(Camera camera)
        {
            try
            {
                // 기존 구독 해제
                App.CameraService.FrameReceivedForUI -= OnFrameReceived;
                App.CameraService.FrameReceivedForAI -= OnFrameReceived;
                
                if (camera.IsEnabled)
                {
                    // ZoneSetup은 정확한 구역 설정을 위해 고해상도 프레임 사용
                    App.CameraService.FrameReceivedForAI += OnFrameReceived;
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
                // 프레임 스키핑 - 성능 최적화
                var now = DateTime.Now;
                if (now - _lastFrameUpdate < _minFrameInterval)
                {
                    e.Frame?.Dispose();
                    return;
                }
                _lastFrameUpdate = now;
                
                // UI 스레드에서 업데이트
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        using (var frame = e.Frame)
                        {
                            if (frame != null && !frame.Empty())
                            {
                                // 오버레이가 활성화된 경우에만 그리기
                                Mat frameWithZones;
                                if (ShowZoneOverlay)
                                {
                                    frameWithZones = DrawZoneOverlaysOnFrame(frame, e.CameraId);
                                }
                                else
                                {
                                    frameWithZones = frame.Clone();
                                }
                                
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
                        System.Diagnostics.Debug.WriteLine($"ZoneSetupViewModel: Frame processing error for {e.CameraId}: {ex.Message}");
                    }
                });
            }
        }
        
        // 정리 메소드
        public void Cleanup()
        {
            App.CameraService.FrameReceivedForUI -= OnFrameReceived;
            App.CameraService.FrameReceivedForAI -= OnFrameReceived;
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
            
            // 현재 프레임 정보를 고정값으로 저장 (일관성 확보)
            var currentFrameWidth = CurrentCameraFrame?.PixelWidth ?? 640;
            var currentFrameHeight = CurrentCameraFrame?.PixelHeight ?? 480;
            var currentPixelsPerMeter = SelectedCamera?.IsCalibrated == true ? SelectedCamera.CalibrationPixelsPerMeter : 100.0;
            
            System.Diagnostics.Debug.WriteLine($"AddZone: Using frame {currentFrameWidth}x{currentFrameHeight}, PixelsPerMeter={currentPixelsPerMeter:F1}");
            
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
                // 캘리브레이션 정보를 현재 값으로 고정
                CalibrationPixelsPerMeter = currentPixelsPerMeter,
                CalibrationFrameWidth = currentFrameWidth,
                CalibrationFrameHeight = currentFrameHeight
            };
            
            // 드래그한 점들 로그 출력
            System.Diagnostics.Debug.WriteLine($"AddZone: TempDrawingPoints ({TempDrawingPoints.Count} points):");
            for (int i = 0; i < TempDrawingPoints.Count; i++)
            {
                System.Diagnostics.Debug.WriteLine($"  Point {i}: ({TempDrawingPoints[i].X:F1}, {TempDrawingPoints[i].Y:F1})");
            }
            
            // 2D 점들을 실제 3D 바닥 좌표로 변환 (고정된 캘리브레이션 정보 사용)
            foreach (var point in TempDrawingPoints)
            {
                var realWorldPoint = CoordinateTransformService.ScreenToWorld(
                    point, currentFrameWidth, currentFrameHeight, currentPixelsPerMeter);
                newZone.FloorPoints.Add(realWorldPoint);
                
                System.Diagnostics.Debug.WriteLine($"AddZone: Screen({point.X:F1}, {point.Y:F1}) -> World({realWorldPoint.X:F2}, {realWorldPoint.Y:F2})");
            }
            
            // 변환 검증을 위해 역변환 테스트
            System.Diagnostics.Debug.WriteLine("AddZone: 역변환 검증:");
            for (int i = 0; i < newZone.FloorPoints.Count; i++)
            {
                var worldPoint = newZone.FloorPoints[i];
                var backToScreen = ConvertWorldToScreenPoint(worldPoint);
                var originalScreen = TempDrawingPoints[i];
                
                var deltaX = Math.Abs(backToScreen.X - originalScreen.X);
                var deltaY = Math.Abs(backToScreen.Y - originalScreen.Y);
                
                System.Diagnostics.Debug.WriteLine($"  Point {i}: Original({originalScreen.X:F1}, {originalScreen.Y:F1}) -> BackConverted({backToScreen.X:F1}, {backToScreen.Y:F1}) | Delta({deltaX:F1}, {deltaY:F1})");
                
                // 좌표 차이가 5픽셀 이상이면 경고
                if (deltaX > 5.0 || deltaY > 5.0)
                {
                    System.Diagnostics.Debug.WriteLine($"  WARNING: Large coordinate difference detected at point {i}!");
                }
            }
            
            // 전체 델타 평균 계산
            var avgDeltaX = 0.0;
            var avgDeltaY = 0.0;
            for (int i = 0; i < Math.Min(newZone.FloorPoints.Count, TempDrawingPoints.Count); i++)
            {
                var worldPoint = newZone.FloorPoints[i];
                var backToScreen = ConvertWorldToScreenPoint(worldPoint);
                var originalScreen = TempDrawingPoints[i];
                
                avgDeltaX += Math.Abs(backToScreen.X - originalScreen.X);
                avgDeltaY += Math.Abs(backToScreen.Y - originalScreen.Y);
            }
            
            if (newZone.FloorPoints.Count > 0)
            {
                avgDeltaX /= newZone.FloorPoints.Count;
                avgDeltaY /= newZone.FloorPoints.Count;
                System.Diagnostics.Debug.WriteLine($"AddZone: Average coordinate delta: ({avgDeltaX:F2}, {avgDeltaY:F2}) pixels");
            }
            
            Zones.Add(newZone);
            SelectedZone = newZone;
            
            // 데이터베이스에 저장
            _ = Task.Run(async () =>
            {
                try
                {
                    await App.DatabaseService.SaveZone3DConfigsAsync(new List<Zone3D> { newZone });
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        System.Diagnostics.Debug.WriteLine($"AddZone: '{newZone.Name}' saved to database successfully");
                    });
                }
                catch (Exception ex)
                {
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        System.Diagnostics.Debug.WriteLine($"AddZone: Failed to save '{newZone.Name}' to database: {ex.Message}");
                    });
                }
            });
            
            // 그리기 모드 종료
            IsDrawingMode = false;
            DrawingModeText = "그리기 시작";
            TempDrawingPoints.Clear();
            DragRectanglePoints.Clear();
            IsDragging = false;
            
            // 시각적 피드백 업데이트
            UpdateVisualFeedback();
            UpdateZoneVisualizations();
            
            System.Diagnostics.Debug.WriteLine($"AddZone: '{newZone.Name}' 구역이 추가되었습니다. FloorPoints count: {newZone.FloorPoints.Count}");
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
        
        [RelayCommand]
        private void StartEditZone(Zone3D zone)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"StartEditZone: {zone.Name}");
                
                // 편집 모드 활성화
                IsEditMode = true;
                EditingZone = zone;
                SelectedZone = zone;
                
                // 기존 구역의 좌표를 편집 포인트로 설정 (상대 좌표 사용)
                EditingRelativePoints.Clear();
                
                foreach (var worldPoint in zone.FloorPoints)
                {
                    // 월드 좌표를 현재 프레임 기준으로 상대 좌표(0~1)로 변환
                    var screenPoint = ConvertWorldToScreenPoint(worldPoint);
                    var relativePoint = AbsoluteToRelative(screenPoint);
                    EditingRelativePoints.Add(relativePoint);
                    
                    System.Diagnostics.Debug.WriteLine($"StartEditZone: World({worldPoint.X:F2}, {worldPoint.Y:F2}) -> Screen({screenPoint.X:F1}, {screenPoint.Y:F1}) -> Relative({relativePoint.X:F3}, {relativePoint.Y:F3})");
                }
                
                // UI 업데이트
                OnPropertyChanged(nameof(EditingPointCollection));
                OnPropertyChanged(nameof(EditingAbsolutePoints));
                
                StatusMessage = $"'{zone.Name}' 구역을 편집 중입니다. 포인트를 드래그하여 크기를 조정하세요.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StartEditZone error: {ex.Message}");
                StatusMessage = $"편집 시작 실패: {ex.Message}";
            }
        }
        
        [RelayCommand]
        private void FinishEditZone()
        {
            try
            {
                if (EditingZone != null && EditingRelativePoints.Count >= 4)
                {
                    System.Diagnostics.Debug.WriteLine($"FinishEditZone: {EditingZone.Name}");
                    
                    // 편집된 상대 좌표를 3D 좌표로 변환하여 저장
                    EditingZone.FloorPoints.Clear();
                    
                    for (int i = 0; i < Math.Min(4, EditingRelativePoints.Count); i++)
                    {
                        var relativePoint = EditingRelativePoints[i];
                        
                        // 상대 좌표 -> 절대 좌표 -> 월드 좌표로 변환
                        var absolutePoint = RelativeToAbsolute(relativePoint);
                        var worldPoint = ConvertTo3DFloorPoint(absolutePoint);
                        
                        EditingZone.FloorPoints.Add(worldPoint);
                        
                        System.Diagnostics.Debug.WriteLine($"FinishEditZone point {i}: Rel({relativePoint.X:F3}, {relativePoint.Y:F3}) -> Abs({absolutePoint.X:F1}, {absolutePoint.Y:F1}) -> World({worldPoint.X:F2}, {worldPoint.Y:F2})");
                    }
                    
                    // 편집 완료 시 현재 캘리브레이션 정보로 업데이트
                    var currentFrameWidth = CurrentCameraFrame?.PixelWidth ?? 640;
                    var currentFrameHeight = CurrentCameraFrame?.PixelHeight ?? 480;
                    var currentPixelsPerMeter = SelectedCamera?.IsCalibrated == true ? SelectedCamera.CalibrationPixelsPerMeter : 100.0;
                    
                    EditingZone.CalibrationFrameWidth = currentFrameWidth;
                    EditingZone.CalibrationFrameHeight = currentFrameHeight;
                    EditingZone.CalibrationPixelsPerMeter = currentPixelsPerMeter;
                    
                    // 시각화 업데이트
                    UpdateZoneVisualizations();
                    
                    StatusMessage = $"'{EditingZone.Name}' 구역 편집이 완료되었습니다.";
                }
                
                // 편집 모드 종료
                IsEditMode = false;
                EditingZone = null;
                EditingRelativePoints.Clear();
                SelectedPointIndex = -1;
                IsDraggingPoint = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FinishEditZone error: {ex.Message}");
                StatusMessage = $"편집 완료 실패: {ex.Message}";
            }
        }
        
        [RelayCommand]
        private void CancelEditZone()
        {
            IsEditMode = false;
            EditingZone = null;
            EditingRelativePoints.Clear();
            SelectedPointIndex = -1;
            IsDraggingPoint = false;
            
            StatusMessage = "구역 편집이 취소되었습니다.";
        }
        
        public void OnCanvasMouseDown(System.Windows.Point clickPoint)
        {
            // Canvas 좌표를 이미지 좌표로 변환
            var imagePoint = ConvertCanvasToImageCoordinates(clickPoint);
            
            System.Diagnostics.Debug.WriteLine($"OnCanvasMouseDown: Canvas({clickPoint.X:F1}, {clickPoint.Y:F1}) -> Image({imagePoint.X:F1}, {imagePoint.Y:F1})");
            System.Diagnostics.Debug.WriteLine($"IsCalibrationMode: {IsCalibrationMode}, IsDrawingMode: {IsDrawingMode}, IsEditMode: {IsEditMode}");
            
            if (IsCalibrationMode)
            {
                HandleCalibrationClick(imagePoint);
                return;
            }
            
            if (IsEditMode)
            {
                HandleEditModeMouseDown(imagePoint);
                return;
            }
            
            if (IsDrawingMode)
            {
                HandleDrawingModeMouseDown(imagePoint);
                return;
            }
        }
        
        public void OnCanvasMouseMove(System.Windows.Point movePoint)
        {
            // Canvas 좌표를 이미지 좌표로 변환
            var imagePoint = ConvertCanvasToImageCoordinates(movePoint);
            
            if (IsEditMode && IsDraggingPoint && SelectedPointIndex >= 0)
            {
                HandleEditModeMouseMove(imagePoint);
            }
            else if (IsDrawingMode && IsDragging)
            {
                HandleDrawingModeMouseMove(imagePoint);
            }
        }
        
        public void OnCanvasMouseUp(System.Windows.Point releasePoint)
        {
            // Canvas 좌표를 이미지 좌표로 변환
            var imagePoint = ConvertCanvasToImageCoordinates(releasePoint);
            
            if (IsEditMode && IsDraggingPoint)
            {
                HandleEditModeMouseUp(imagePoint);
            }
            else if (IsDrawingMode && IsDragging)
            {
                HandleDrawingModeMouseUp(imagePoint);
            }
        }
        
        private void HandleDrawingModeMouseDown(System.Windows.Point clickPoint)
        {
            // 드래그 시작
            IsDragging = true;
            DragStartPoint = clickPoint;
            DragCurrentPoint = clickPoint;
            
            // 기존의 임시 포인트 초기화
            TempDrawingPoints.Clear();
            DragRectanglePoints.Clear();
            
            System.Diagnostics.Debug.WriteLine($"Started dragging at ({clickPoint.X}, {clickPoint.Y})");
            StatusMessage = "마우스를 드래그하여 구역을 그리세요.";
        }
        
        private void HandleDrawingModeMouseMove(System.Windows.Point movePoint)
        {
            if (!IsDragging) return;
            
            DragCurrentPoint = movePoint;
            
            // 사각형 4개 포인트 계산
            DragRectanglePoints.Clear();
            DragRectanglePoints.Add(DragStartPoint); // 좌상
            DragRectanglePoints.Add(new System.Windows.Point(DragCurrentPoint.X, DragStartPoint.Y)); // 우상
            DragRectanglePoints.Add(DragCurrentPoint); // 우하
            DragRectanglePoints.Add(new System.Windows.Point(DragStartPoint.X, DragCurrentPoint.Y)); // 좌하
            
            // TempDrawingPoints도 업데이트 (기존 UI 호환성)
            TempDrawingPoints.Clear();
            foreach (var point in DragRectanglePoints)
            {
                TempDrawingPoints.Add(point);
            }
            
            UpdateVisualFeedback();
            
            System.Diagnostics.Debug.WriteLine($"Dragging to ({movePoint.X}, {movePoint.Y})");
        }
        
        private void HandleDrawingModeMouseUp(System.Windows.Point releasePoint)
        {
            if (!IsDragging) return;
            
            IsDragging = false;
            DragCurrentPoint = releasePoint;
            
            // 최종 사각형 포인트 계산
            DragRectanglePoints.Clear();
            DragRectanglePoints.Add(DragStartPoint); // 좌상
            DragRectanglePoints.Add(new System.Windows.Point(DragCurrentPoint.X, DragStartPoint.Y)); // 우상
            DragRectanglePoints.Add(DragCurrentPoint); // 우하
            DragRectanglePoints.Add(new System.Windows.Point(DragStartPoint.X, DragCurrentPoint.Y)); // 좌하
            
            // TempDrawingPoints 업데이트
            TempDrawingPoints.Clear();
            foreach (var point in DragRectanglePoints)
            {
                TempDrawingPoints.Add(point);
            }
            
            UpdateVisualFeedback();
            
            System.Diagnostics.Debug.WriteLine($"Finished dragging. Rectangle created with 4 points");
            StatusMessage = "구역이 그려졌습니다. '구역 추가' 버튼을 클릭하세요.";
        }
        
        private void HandleEditModeMouseDown(System.Windows.Point clickPoint)
        {
            // 클릭한 위치 근처의 포인트 찾기 (절대 좌표 기준)
            var absolutePoints = EditingAbsolutePoints;
            SelectedPointIndex = FindNearbyPoint(clickPoint, absolutePoints);
            
            if (SelectedPointIndex >= 0)
            {
                IsDraggingPoint = true;
                System.Diagnostics.Debug.WriteLine($"Started dragging point {SelectedPointIndex} at ({clickPoint.X:F1}, {clickPoint.Y:F1})");
                StatusMessage = $"포인트 {SelectedPointIndex + 1}을 드래그하여 이동하세요.";
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("No nearby point found");
            }
        }
        
        private void HandleEditModeMouseMove(System.Windows.Point movePoint)
        {
            if (SelectedPointIndex >= 0 && SelectedPointIndex < EditingRelativePoints.Count)
            {
                // 절대 좌표를 상대 좌표로 변환하여 저장
                var relativePoint = AbsoluteToRelative(movePoint);
                
                EditingRelativePoints[SelectedPointIndex] = relativePoint;
                
                // UI 업데이트
                OnPropertyChanged(nameof(EditingPointCollection));
                OnPropertyChanged(nameof(EditingAbsolutePoints));
                
                System.Diagnostics.Debug.WriteLine($"Moving point {SelectedPointIndex}: Abs({movePoint.X:F1}, {movePoint.Y:F1}) -> Rel({relativePoint.X:F3}, {relativePoint.Y:F3})");
            }
        }
        
        private void HandleEditModeMouseUp(System.Windows.Point releasePoint)
        {
            if (SelectedPointIndex >= 0)
            {
                System.Diagnostics.Debug.WriteLine($"Finished dragging point {SelectedPointIndex}");
                StatusMessage = "포인트 이동이 완료되었습니다. '편집 완료' 버튼을 클릭하세요.";
            }
            
            IsDraggingPoint = false;
            SelectedPointIndex = -1;
        }
        
        private int FindNearbyPoint(System.Windows.Point clickPoint, ObservableCollection<System.Windows.Point> points)
        {
            const double threshold = 15.0; // 15픽셀 반경 내에서 포인트 검색
            
            for (int i = 0; i < points.Count; i++)
            {
                var distance = Math.Sqrt(
                    Math.Pow(clickPoint.X - points[i].X, 2) + 
                    Math.Pow(clickPoint.Y - points[i].Y, 2)
                );
                
                if (distance <= threshold)
                {
                    return i;
                }
            }
            
            return -1;
        }
        
        // 기존 OnCanvasClick은 호환성을 위해 유지 (MouseDown으로 리다이렉트)
        public void OnCanvasClick(System.Windows.Point clickPoint)
        {
            OnCanvasMouseDown(clickPoint);
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
        
        /// <summary>
        /// 카메라 프레임 변경 시 편집 모드 포인트들의 좌표 업데이트
        /// </summary>
        partial void OnCurrentCameraFrameChanged(BitmapSource? value)
        {
            // if (IsEditMode && EditingRelativePoints.Count > 0)
            // {
            //     // 프레임 크기가 변경되면 편집 중인 절대 좌표들도 업데이트
            //     OnPropertyChanged(nameof(EditingAbsolutePoints));
            //     OnPropertyChanged(nameof(EditingPointCollection));
            //     
            //     System.Diagnostics.Debug.WriteLine($"Frame size changed, updated editing points. New frame size: {value?.PixelWidth ?? 0}x{value?.PixelHeight ?? 0}");
            // }
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
        /// 구역 시각화 업데이트 (OpenCV용 컬렉션만 업데이트)
        /// </summary>
        private void UpdateZoneVisualizations()
        {
            System.Diagnostics.Debug.WriteLine($"UpdateZoneVisualizations called. Zones count: {Zones.Count}");
            
            // OpenCV용 카메라별 컬렉션 업데이트 (실제 프레임에 그리기 위함)
            PopulateCameraZoneCollections();
            
            System.Diagnostics.Debug.WriteLine($"Zone visualizations updated for OpenCV rendering");
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
                    
                    // 상대 좌표 변환 (0~1 범위) - ZoneSetup은 고해상도 프레임 기준
                    var currentFrameWidth = CurrentCameraFrame?.PixelWidth ?? zone.CalibrationFrameWidth;
                    var currentFrameHeight = CurrentCameraFrame?.PixelHeight ?? zone.CalibrationFrameHeight;
                    var currentPixelsPerMeter = SelectedCamera?.IsCalibrated == true ? SelectedCamera.CalibrationPixelsPerMeter : zone.CalibrationPixelsPerMeter;
                    
                    System.Diagnostics.Debug.WriteLine($"Zone {zone.Name}: Current frame {currentFrameWidth}x{currentFrameHeight}, Stored frame {zone.CalibrationFrameWidth}x{zone.CalibrationFrameHeight}");
                    
                    foreach (var worldPoint in zone.FloorPoints)
                    {
                        // 저장된 캘리브레이션 기준으로 화면 좌표 계산 (호환성 유지)
                        var screenPoint = CoordinateTransformService.WorldToScreen(worldPoint, 
                            zone.CalibrationFrameWidth, zone.CalibrationFrameHeight, zone.CalibrationPixelsPerMeter);
                        
                        // 해상도 스케일링 팩터 계산 (저해상도 -> 고해상도)
                        var scaleX = currentFrameWidth / zone.CalibrationFrameWidth;
                        var scaleY = currentFrameHeight / zone.CalibrationFrameHeight;
                        
                        // 현재 해상도에 맞게 스케일링
                        var scaledScreenPoint = new System.Windows.Point(
                            screenPoint.X * scaleX,
                            screenPoint.Y * scaleY
                        );
                            
                        // 상대 좌표로 변환 (0~1 범위)
                        var relativeX = scaledScreenPoint.X / currentFrameWidth;
                        var relativeY = scaledScreenPoint.Y / currentFrameHeight;
                        var relativePoint = new System.Windows.Point(relativeX, relativeY);
                        
                        visualization.AddRelativePoint(relativePoint);
                        
                        System.Diagnostics.Debug.WriteLine($"ZoneSetup: World({worldPoint.X:F2}, {worldPoint.Y:F2}) -> Screen({screenPoint.X:F1}, {screenPoint.Y:F1}) -> Scaled({scaledScreenPoint.X:F1}, {scaledScreenPoint.Y:F1}) -> Relative({relativeX:F3}, {relativeY:F3})");
                    }
                    
                    // 다각형 닫기
                    if (zone.FloorPoints.Count >= 3)
                    {
                        var firstScreenPoint = CoordinateTransformService.WorldToScreen(zone.FloorPoints[0], 
                            zone.CalibrationFrameWidth, zone.CalibrationFrameHeight, zone.CalibrationPixelsPerMeter);
                        
                        var scaleX = currentFrameWidth / zone.CalibrationFrameWidth;
                        var scaleY = currentFrameHeight / zone.CalibrationFrameHeight;
                        
                        var scaledFirstPoint = new System.Windows.Point(
                            firstScreenPoint.X * scaleX,
                            firstScreenPoint.Y * scaleY
                        );
                        
                        var relativeX = scaledFirstPoint.X / currentFrameWidth;
                        var relativeY = scaledFirstPoint.Y / currentFrameHeight;
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
        /// OpenCV를 사용하여 프레임에 모든 오버레이(구역, 임시 그리기, 편집)를 직접 그립니다
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
                
                // 경고 구역 그리기 (주황색) - 디버그 로그 제거로 성능 향상
                if (_cameraWarningZones.ContainsKey(cameraId))
                {
                    var warningZones = _cameraWarningZones[cameraId];
                    
                    foreach (var zone in warningZones)
                    {
                        if (zone.IsEnabled && zone.RelativePoints.Count >= 3)
                        {
                            DrawZoneOnFrame(frameWithZones, zone, frameWidth, frameHeight, new Scalar(0, 165, 255)); // Orange
                        }
                    }
                }
                
                // 위험 구역 그리기 (빨간색) - 디버그 로그 제거로 성능 향상
                if (_cameraDangerZones.ContainsKey(cameraId))
                {
                    var dangerZones = _cameraDangerZones[cameraId];
                    
                    foreach (var zone in dangerZones)
                    {
                        if (zone.IsEnabled && zone.RelativePoints.Count >= 3)
                        {
                            DrawZoneOnFrame(frameWithZones, zone, frameWidth, frameHeight, new Scalar(0, 0, 255)); // Red
                        }
                    }
                }
                
                // 성능 최적화: 활성 상태일 때만 오버레이 렌더링
                if (ShowZoneOverlay)
                {
                    // 임시 그리기 오버레이 (드래그 중인 사각형)
                    if (IsDrawingMode && IsDragging && DragRectanglePoints.Count == 4)
                    {
                        DrawTemporaryDrawing(frameWithZones, frameWidth, frameHeight);
                    }
                    
                    // 편집 모드 오버레이 (편집 중인 구역과 포인트들)
                    if (IsEditMode && EditingRelativePoints.Count >= 3)
                    {
                        DrawEditingOverlay(frameWithZones, frameWidth, frameHeight);
                    }
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
                    // 성능 최적화: 투명도가 낮은 경우 오버레이 생성하지 않음
                    if (zone.Opacity > 0.05)
                    {
                        using (var overlay = frame.Clone())
                        {
                            Cv2.FillPoly(overlay, new Point[][] { points.ToArray() }, color);
                            
                            // 투명도 적용하여 원본과 합성
                            var alpha = zone.Opacity;
                            Cv2.AddWeighted(frame, 1.0 - alpha, overlay, alpha, 0, frame);
                        }
                    }
                    
                    // 구역 경계선 그리기 (항상 표시)
                    Cv2.Polylines(frame, new Point[][] { points.ToArray() }, true, color, 2);
                    
                    // 구역 이름 표시 (성능을 위해 중심점 계산 최적화)
                    if (points.Count > 0)
                    {
                        // Average 대신 더 빠른 계산
                        var sumX = 0;
                        var sumY = 0;
                        foreach (var p in points)
                        {
                            sumX += p.X;
                            sumY += p.Y;
                        }
                        var centerX = sumX / points.Count;
                        var centerY = sumY / points.Count;
                        
                        Cv2.PutText(frame, zone.Name, new Point(centerX - 30, centerY), 
                            HersheyFonts.HersheySimplex, 0.6, new Scalar(255, 255, 255), 2);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ZoneSetup: Individual zone drawing error for {zone.Name}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 임시 그리기 (드래그 중인 사각형)를 프레임에 그립니다
        /// </summary>
        private void DrawTemporaryDrawing(Mat frame, int frameWidth, int frameHeight)
        {
            try
            {
                var points = new List<Point>();
                
                foreach (var point in DragRectanglePoints)
                {
                    var pixelX = Math.Max(0, Math.Min(frameWidth - 1, (int)point.X));
                    var pixelY = Math.Max(0, Math.Min(frameHeight - 1, (int)point.Y));
                    points.Add(new Point(pixelX, pixelY));
                }
                
                if (points.Count >= 4)
                {
                    // 임시 사각형 그리기 (노란색, 점선)
                    var color = new Scalar(0, 255, 255); // 노란색
                    
                    // 반투명 채우기
                    var overlay = frame.Clone();
                    Cv2.FillPoly(overlay, new Point[][] { points.ToArray() }, color);
                    Cv2.AddWeighted(frame, 0.8, overlay, 0.2, 0, frame);
                    
                    // 점선 경계
                    for (int i = 0; i < points.Count; i++)
                    {
                        var nextIndex = (i + 1) % points.Count;
                        DrawDashedLine(frame, points[i], points[nextIndex], color, 2);
                    }
                    
                    // 모서리 점들 그리기
                    foreach (var point in points)
                    {
                        Cv2.Circle(frame, point, 5, new Scalar(255, 255, 0), -1);
                        Cv2.Circle(frame, point, 5, new Scalar(0, 0, 0), 2);
                    }
                    
                    overlay.Dispose();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DrawTemporaryDrawing error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 편집 모드 오버레이를 프레임에 그립니다
        /// </summary>
        private void DrawEditingOverlay(Mat frame, int frameWidth, int frameHeight)
        {
            try
            {
                var points = new List<Point>();
                
                // 편집 중인 구역의 상대 좌표를 픽셀 좌표로 변환
                foreach (var relativePoint in EditingRelativePoints)
                {
                    var pixelX = (int)(relativePoint.X * frameWidth);
                    var pixelY = (int)(relativePoint.Y * frameHeight);
                    
                    pixelX = Math.Max(0, Math.Min(frameWidth - 1, pixelX));
                    pixelY = Math.Max(0, Math.Min(frameHeight - 1, pixelY));
                    
                    points.Add(new Point(pixelX, pixelY));
                }
                
                if (points.Count >= 3)
                {
                    // 편집 중인 구역 그리기 (주황색, 점선)
                    var color = new Scalar(0, 165, 255); // 주황색
                    
                    // 반투명 채우기
                    var overlay = frame.Clone();
                    Cv2.FillPoly(overlay, new Point[][] { points.ToArray() }, color);
                    Cv2.AddWeighted(frame, 0.7, overlay, 0.3, 0, frame);
                    
                    // 점선 경계
                    for (int i = 0; i < points.Count; i++)
                    {
                        var nextIndex = (i + 1) % points.Count;
                        DrawDashedLine(frame, points[i], points[nextIndex], color, 3);
                    }
                    
                    // 편집 포인트들 그리기
                    for (int i = 0; i < points.Count; i++)
                    {
                        var pointColor = (i == SelectedPointIndex && IsDraggingPoint) ? 
                            new Scalar(0, 255, 0) : new Scalar(255, 165, 0); // 선택된 점은 초록색
                        
                        Cv2.Circle(frame, points[i], 8, pointColor, -1);
                        Cv2.Circle(frame, points[i], 8, new Scalar(255, 255, 255), 2);
                        
                        // 포인트 번호 표시
                        Cv2.PutText(frame, (i + 1).ToString(), 
                            new Point(points[i].X - 4, points[i].Y + 4),
                            HersheyFonts.HersheySimplex, 0.4, new Scalar(0, 0, 0), 1);
                    }
                    
                    overlay.Dispose();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DrawEditingOverlay error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 점선 그리기 헬퍼 메서드
        /// </summary>
        private void DrawDashedLine(Mat frame, Point start, Point end, Scalar color, int thickness)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            
            var dashLength = 10;
            var gapLength = 5;
            var totalLength = dashLength + gapLength;
            
            var steps = (int)(distance / totalLength);
            
            for (int i = 0; i < steps; i++)
            {
                var t1 = (double)(i * totalLength) / distance;
                var t2 = (double)(i * totalLength + dashLength) / distance;
                
                if (t2 > 1.0) t2 = 1.0;
                
                var x1 = (int)(start.X + t1 * dx);
                var y1 = (int)(start.Y + t1 * dy);
                var x2 = (int)(start.X + t2 * dx);
                var y2 = (int)(start.Y + t2 * dy);
                
                Cv2.Line(frame, new Point(x1, y1), new Point(x2, y2), color, thickness);
            }
        }
    }
    
}