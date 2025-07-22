using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SafetyVisionMonitor.ViewModels
{
    public partial class HistoryViewModel : BaseViewModel
    {
        [ObservableProperty]
        private DateTime startDate = DateTime.Today.AddDays(-30);
        
        [ObservableProperty]
        private DateTime endDate = DateTime.Today;
        
        [ObservableProperty]
        private ObservableCollection<DailyStatistics> dailyStatistics;
        
        [ObservableProperty]
        private ObservableCollection<EventTypeStatistics> eventTypeStatistics;
        
        [ObservableProperty]
        private ObservableCollection<CameraStatistics> cameraStatistics;
        
        [ObservableProperty]
        private ObservableCollection<HourlyStatistics> hourlyStatistics;
        
        [ObservableProperty]
        private int totalEvents;
        
        [ObservableProperty]
        private int totalPersonsDetected;
        
        [ObservableProperty]
        private int totalDangerZoneEvents;
        
        [ObservableProperty]
        private int totalNoHelmetEvents;
        
        [ObservableProperty]
        private string mostActiveCamera = "CAM001";
        
        [ObservableProperty]
        private string peakHour = "14:00";
        
        [ObservableProperty]
        private bool isGeneratingReport;
        
        public HistoryViewModel()
        {
            Title = "이력 조회";
            DailyStatistics = new ObservableCollection<DailyStatistics>();
            EventTypeStatistics = new ObservableCollection<EventTypeStatistics>();
            CameraStatistics = new ObservableCollection<CameraStatistics>();
            HourlyStatistics = new ObservableCollection<HourlyStatistics>();
            
            LoadSampleData();
        }
        
        public override async void OnLoaded()
        {
            base.OnLoaded();
            await LoadStatistics();
        }
        
        private void LoadSampleData()
        {
            // 일별 통계 샘플 데이터
            var random = new Random();
            for (int i = 30; i >= 0; i--)
            {
                var date = DateTime.Today.AddDays(-i);
                DailyStatistics.Add(new DailyStatistics
                {
                    Date = date,
                    TotalEvents = random.Next(20, 100),
                    DangerZoneEvents = random.Next(5, 30),
                    WarningZoneEvents = random.Next(10, 40),
                    NoHelmetEvents = random.Next(5, 25)
                });
            }
            
            // 이벤트 타입별 통계
            EventTypeStatistics.Clear();
            EventTypeStatistics.Add(new EventTypeStatistics 
            { 
                EventType = "안전모 미착용", 
                Count = 145, 
                Percentage = 35,
                Color = "#FFA500"  // Orange
            });
            EventTypeStatistics.Add(new EventTypeStatistics 
            { 
                EventType = "위험구역 진입", 
                Count = 89, 
                Percentage = 21,
                Color = "#FF0000"  // Red
            });
            EventTypeStatistics.Add(new EventTypeStatistics 
            { 
                EventType = "경고구역 진입", 
                Count = 156, 
                Percentage = 37,
                Color = "#FFFF00"  // Yellow
            });
            EventTypeStatistics.Add(new EventTypeStatistics 
            { 
                EventType = "기타", 
                Count = 30, 
                Percentage = 7,
                Color = "#808080"  // Gray
            });
            
            // 카메라별 통계
            for (int i = 1; i <= 4; i++)
            {
                CameraStatistics.Add(new CameraStatistics
                {
                    CameraId = $"CAM{i:D3}",
                    EventCount = random.Next(50, 200),
                    ActiveHours = random.Next(100, 700),
                    DetectionRate = random.Next(70, 95)
                });
            }
            
            // 시간대별 통계
            for (int hour = 0; hour < 24; hour++)
            {
                HourlyStatistics.Add(new HourlyStatistics
                {
                    Hour = hour,
                    EventCount = random.Next(5, 50),
                    PersonCount = random.Next(10, 100)
                });
            }
            
            UpdateSummaryStatistics();
        }
        
        [RelayCommand]
        private async Task LoadStatistics()
        {
            IsLoading = true;
            
            try
            {
                // TODO: DatabaseService에서 실제 통계 데이터 로드
                await Task.Delay(1000); // 시뮬레이션
                
                UpdateSummaryStatistics();
                StatusMessage = "통계 데이터를 불러왔습니다.";
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        [RelayCommand]
        private async Task GenerateReport()
        {
            IsGeneratingReport = true;
            
            try
            {
                // TODO: 리포트 생성 로직
                await Task.Delay(2000); // 시뮬레이션
                
                StatusMessage = "리포트가 생성되었습니다.";
            }
            finally
            {
                IsGeneratingReport = false;
            }
        }
        
        [RelayCommand]
        private void ExportChart(string chartType)
        {
            // TODO: 차트 이미지 내보내기
            StatusMessage = $"{chartType} 차트를 내보냈습니다.";
        }
        
        partial void OnStartDateChanged(DateTime value)
        {
            if (value > EndDate)
            {
                EndDate = value;
            }
            _ = LoadStatistics();
        }
        
        partial void OnEndDateChanged(DateTime value)
        {
            if (value < StartDate)
            {
                StartDate = value;
            }
            _ = LoadStatistics();
        }
        
        private void UpdateSummaryStatistics()
        {
            TotalEvents = DailyStatistics.Sum(d => d.TotalEvents);
            TotalDangerZoneEvents = DailyStatistics.Sum(d => d.DangerZoneEvents);
            TotalNoHelmetEvents = DailyStatistics.Sum(d => d.NoHelmetEvents);
            TotalPersonsDetected = random.Next(500, 2000);
            
            // 가장 활발한 카메라
            var topCamera = CameraStatistics.OrderByDescending(c => c.EventCount).FirstOrDefault();
            if (topCamera != null)
            {
                MostActiveCamera = topCamera.CameraId;
            }
            
            // 피크 시간
            var peakHourData = HourlyStatistics.OrderByDescending(h => h.EventCount).FirstOrDefault();
            if (peakHourData != null)
            {
                PeakHour = $"{peakHourData.Hour:D2}:00";
            }
        }
        
        private readonly Random random = new();
    }
    
    // 통계 데이터 모델들
    public class DailyStatistics
    {
        public DateTime Date { get; set; }
        public int TotalEvents { get; set; }
        public int DangerZoneEvents { get; set; }
        public int WarningZoneEvents { get; set; }
        public int NoHelmetEvents { get; set; }
    }
    
    public class EventTypeStatistics
    {
        public string EventType { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percentage { get; set; }
        public string Color { get; set; } = string.Empty;
    }
    
    public class CameraStatistics
    {
        public string CameraId { get; set; } = string.Empty;
        public int EventCount { get; set; }
        public int ActiveHours { get; set; }
        public double DetectionRate { get; set; }
    }
    
    public class HourlyStatistics
    {
        public int Hour { get; set; }
        public int EventCount { get; set; }
        public int PersonCount { get; set; }
    }
}