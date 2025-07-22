using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafetyVisionMonitor.Models;

namespace SafetyVisionMonitor.ViewModels
{
    public partial class EventLogViewModel : BaseViewModel
    {
        [ObservableProperty]
        private ObservableCollection<SafetyEventViewModel> events;
        
        [ObservableProperty]
        private ObservableCollection<string> eventTypes;
        
        [ObservableProperty]
        private ObservableCollection<string> cameras;
        
        [ObservableProperty]
        private DateTime startDate = DateTime.Today.AddDays(-7);
        
        [ObservableProperty]
        private DateTime endDate = DateTime.Today;
        
        [ObservableProperty]
        private string? selectedEventType = "전체";
        
        [ObservableProperty]
        private string? selectedCamera = "전체";
        
        [ObservableProperty]
        private int totalEventCount;
        
        [ObservableProperty]
        private int filteredEventCount;
        
        [ObservableProperty]
        private bool isExporting = false;
        
        public EventLogViewModel()
        {
            Title = "이벤트/로그";
            Events = new ObservableCollection<SafetyEventViewModel>();
            EventTypes = new ObservableCollection<string> { "전체", "NoHelmet", "DangerZone", "WarningZone", "Fall" };
            Cameras = new ObservableCollection<string> { "전체", "CAM001", "CAM002", "CAM003", "CAM004" };
            
            LoadSampleData();
        }
        
        public override async void OnLoaded()
        {
            base.OnLoaded();
            await LoadEvents();
        }
        
        private void LoadSampleData()
        {
            // 샘플 이벤트 데이터 생성
            var random = new Random();
            var eventTypesArray = new[] { "NoHelmet", "DangerZone", "WarningZone" };
            var camerasArray = new[] { "CAM001", "CAM002", "CAM003", "CAM004" };
            
            for (int i = 0; i < 50; i++)
            {
                var timestamp = DateTime.Now.AddMinutes(-random.Next(0, 10080)); // 지난 7일
                var eventType = eventTypesArray[random.Next(eventTypesArray.Length)];
                var camera = camerasArray[random.Next(camerasArray.Length)];
                
                Events.Add(new SafetyEventViewModel
                {
                    Id = i + 1,
                    Timestamp = timestamp,
                    EventType = eventType,
                    EventTypeDisplay = GetEventTypeDisplay(eventType),
                    CameraId = camera,
                    PersonTrackingId = $"P{random.Next(1, 20):D3}",
                    Confidence = 0.7 + random.NextDouble() * 0.3,
                    Description = GetEventDescription(eventType),
                    HasImage = random.Next(10) > 3,
                    HasVideo = random.Next(10) > 5
                });
            }
            
            UpdateCounts();
        }
        
        private string GetEventTypeDisplay(string eventType)
        {
            return eventType switch
            {
                "NoHelmet" => "안전모 미착용",
                "DangerZone" => "위험구역 진입",
                "WarningZone" => "경고구역 진입",
                "Fall" => "넘어짐 감지",
                _ => eventType
            };
        }
        
        private string GetEventDescription(string eventType)
        {
            return eventType switch
            {
                "NoHelmet" => "작업자가 안전모를 착용하지 않았습니다.",
                "DangerZone" => "작업자가 위험구역에 진입했습니다.",
                "WarningZone" => "작업자가 경고구역에 진입했습니다.",
                "Fall" => "작업자가 넘어진 것으로 감지되었습니다.",
                _ => ""
            };
        }
        
        [RelayCommand]
        private async Task LoadEvents()
        {
            IsLoading = true;
            
            try
            {
                // TODO: DatabaseService에서 실제 데이터 로드
                await Task.Delay(500); // 시뮬레이션
                
                // 필터링 적용
                ApplyFilters();
                
                StatusMessage = $"{FilteredEventCount}개의 이벤트를 불러왔습니다.";
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        [RelayCommand]
        private void ApplyFilters()
        {
            var filtered = Events.AsEnumerable();
            
            // 날짜 필터
            filtered = filtered.Where(e => e.Timestamp >= StartDate && e.Timestamp <= EndDate.AddDays(1));
            
            // 이벤트 타입 필터
            if (SelectedEventType != "전체")
            {
                filtered = filtered.Where(e => e.EventType == SelectedEventType);
            }
            
            // 카메라 필터
            if (SelectedCamera != "전체")
            {
                filtered = filtered.Where(e => e.CameraId == SelectedCamera);
            }
            
            // UI 업데이트
            foreach (var evt in Events)
            {
                evt.IsVisible = filtered.Contains(evt);
            }
            
            UpdateCounts();
        }
        
        [RelayCommand]
        private async Task ExportToExcel()
        {
            IsExporting = true;
            
            try
            {
                // TODO: Excel 내보내기 구현
                await Task.Delay(1000); // 시뮬레이션
                
                MessageBox.Show("이벤트 로그가 Excel 파일로 내보내졌습니다.", "내보내기 완료",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                IsExporting = false;
            }
        }
        
        [RelayCommand]
        private void ViewEventDetail(SafetyEventViewModel safetyEvent)
        {
            // TODO: 이벤트 상세 다이얼로그 표시
            MessageBox.Show($"이벤트 ID: {safetyEvent.Id}\n" +
                          $"시간: {safetyEvent.Timestamp}\n" +
                          $"타입: {safetyEvent.EventTypeDisplay}\n" +
                          $"카메라: {safetyEvent.CameraId}\n" +
                          $"신뢰도: {safetyEvent.Confidence:P0}",
                          "이벤트 상세", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        
        [RelayCommand]
        private async Task DeleteEvent(SafetyEventViewModel safetyEvent)
        {
            var result = MessageBox.Show("이 이벤트를 삭제하시겠습니까?", "확인",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                Events.Remove(safetyEvent);
                UpdateCounts();
                
                // TODO: DatabaseService에서 삭제
                await Task.CompletedTask;
                
                StatusMessage = "이벤트가 삭제되었습니다.";
            }
        }
        
        partial void OnStartDateChanged(DateTime value)
        {
            if (value > EndDate)
            {
                EndDate = value;
            }
            ApplyFilters();
        }
        
        partial void OnEndDateChanged(DateTime value)
        {
            if (value < StartDate)
            {
                StartDate = value;
            }
            ApplyFilters();
        }
        
        partial void OnSelectedEventTypeChanged(string? value)
        {
            ApplyFilters();
        }
        
        partial void OnSelectedCameraChanged(string? value)
        {
            ApplyFilters();
        }
        
        private void UpdateCounts()
        {
            TotalEventCount = Events.Count;
            FilteredEventCount = Events.Count(e => e.IsVisible);
        }
    }
    
    // 개별 이벤트 ViewModel
    public partial class SafetyEventViewModel : ObservableObject
    {
        [ObservableProperty]
        private int id;
        
        [ObservableProperty]
        private DateTime timestamp;
        
        [ObservableProperty]
        private string eventType = string.Empty;
        
        [ObservableProperty]
        private string eventTypeDisplay = string.Empty;
        
        [ObservableProperty]
        private string cameraId = string.Empty;
        
        [ObservableProperty]
        private string? personTrackingId;
        
        [ObservableProperty]
        private double confidence;
        
        [ObservableProperty]
        private string? imagePath;
        
        [ObservableProperty]
        private string? videoClipPath;
        
        [ObservableProperty]
        private string? description;
        
        [ObservableProperty]
        private bool hasImage;
        
        [ObservableProperty]
        private bool hasVideo;
        
        [ObservableProperty]
        private bool isVisible = true;
        
        public string TimeAgo
        {
            get
            {
                var span = DateTime.Now - Timestamp;
                if (span.TotalMinutes < 1) return "방금 전";
                if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}분 전";
                if (span.TotalHours < 24) return $"{(int)span.TotalHours}시간 전";
                if (span.TotalDays < 7) return $"{(int)span.TotalDays}일 전";
                return Timestamp.ToString("yyyy-MM-dd");
            }
        }
    }
}