using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafetyVisionMonitor.Models;

namespace SafetyVisionMonitor.ViewModels
{
    public partial class ZoneSetupViewModel : BaseViewModel
    {
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
        private ObservableCollection<Point> tempDrawingPoints;
        
        [ObservableProperty]
        private ZoneType newZoneType = ZoneType.Warning;
        
        [ObservableProperty]
        private double newZoneHeight = 2.0;
        
        [ObservableProperty]
        private bool showZoneOverlay = true;
        
        public ZoneSetupViewModel()
        {
            Title = "3D 영역 설정";
            Zones = new ObservableCollection<Zone3D>();
            AvailableCameras = new ObservableCollection<Camera>();
            TempDrawingPoints = new ObservableCollection<Point>();
            
            LoadSampleData();
        }
        
        private void LoadSampleData()
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
            
            // 샘플 구역 추가
            var sampleZone = new Zone3D
            {
                Name = "위험구역 1",
                Type = ZoneType.Danger,
                CameraId = "CAM001",
                DisplayColor = Colors.Red,
                Opacity = 0.3,
                Height = 2.5
            };
            
            // 샘플 바닥 점들
            sampleZone.FloorPoints.Add(new Point2D(100, 200));
            sampleZone.FloorPoints.Add(new Point2D(300, 200));
            sampleZone.FloorPoints.Add(new Point2D(300, 400));
            sampleZone.FloorPoints.Add(new Point2D(100, 400));
            
            Zones.Add(sampleZone);
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
            }
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
                Name = $"{(NewZoneType == ZoneType.Warning ? "경고" : "위험")}구역 {Zones.Count + 1}",
                Type = NewZoneType,
                CameraId = SelectedCamera!.Id,
                DisplayColor = NewZoneType == ZoneType.Warning ? Colors.Orange : Colors.Red,
                Opacity = 0.3,
                Height = NewZoneHeight,
                CreatedDate = DateTime.Now
            };
            
            // 바닥 점들 복사
            foreach (var point in TempDrawingPoints)
            {
                newZone.FloorPoints.Add(new Point2D(point.X, point.Y));
            }
            
            Zones.Add(newZone);
            SelectedZone = newZone;
            
            // 그리기 모드 종료
            IsDrawingMode = false;
            DrawingModeText = "그리기 시작";
            TempDrawingPoints.Clear();
            
            StatusMessage = $"'{newZone.Name}' 구역이 추가되었습니다.";
        }
        
        [RelayCommand]
        private void DeleteZone(Zone3D zone)
        {
            var result = MessageBox.Show($"'{zone.Name}' 구역을 삭제하시겠습니까?", "확인",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                Zones.Remove(zone);
                if (SelectedZone == zone)
                {
                    SelectedZone = null;
                }
                StatusMessage = $"'{zone.Name}' 구역이 삭제되었습니다.";
            }
        }
        
        [RelayCommand]
        private void ToggleZone(Zone3D zone)
        {
            zone.IsEnabled = !zone.IsEnabled;
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
                StatusMessage = "모든 구역이 삭제되었습니다.";
            }
        }
        
        [RelayCommand]
        private async Task SaveZones()
        {
            try
            {
                // TODO: DatabaseService를 통해 저장
                await Task.Delay(500); // 시뮬레이션
                
                MessageBox.Show("구역 설정이 저장되었습니다.", "저장 완료",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                    
                StatusMessage = "구역 설정이 저장되었습니다.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"저장 중 오류 발생: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        public void OnCanvasClick(Point clickPoint)
        {
            if (!IsDrawingMode)
                return;
                
            if (TempDrawingPoints.Count < 4)
            {
                TempDrawingPoints.Add(clickPoint);
                
                if (TempDrawingPoints.Count == 4)
                {
                    StatusMessage = "4개 점이 모두 선택되었습니다. '구역 추가' 버튼을 클릭하세요.";
                }
                else
                {
                    StatusMessage = $"바닥 점 {TempDrawingPoints.Count}/4 선택됨";
                }
            }
        }
        
        partial void OnSelectedCameraChanged(Camera? value)
        {
            if (value != null)
            {
                // 카메라 변경 시 해당 카메라의 구역만 표시
                UpdateZonesForCamera(value.Id);
            }
        }
        
        private void UpdateZonesForCamera(string cameraId)
        {
            // 실제로는 필터링된 구역만 표시하도록 구현
            StatusMessage = $"{cameraId}의 구역을 표시합니다.";
        }
    }
}