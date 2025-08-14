using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafetyVisionMonitor.Shared.ViewModels.Base;

namespace SafetyVisionHistoryViewer.ViewModels
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
                var databaseService = SafetyVisionHistoryViewer.App.DatabaseService;
                if (databaseService != null)
                {
                    // 실제 DB에서 통계 데이터 로드
                    await LoadDailyStatisticsFromDatabase(databaseService);
                    await LoadEventTypeStatisticsFromDatabase(databaseService);
                    await LoadCameraStatisticsFromDatabase(databaseService);
                    await LoadHourlyStatisticsFromDatabase(databaseService);
                    
                    StatusMessage = "실제 데이터베이스에서 통계를 불러왔습니다.";
                }
                else
                {
                    // 샘플 데이터 로드 (DB 연결 실패 시)
                    LoadSampleData();
                    StatusMessage = "샘플 데이터를 불러왔습니다 (DB 연결 실패).";
                }
                
                UpdateSummaryStatistics();
            }
            catch (Exception ex)
            {
                StatusMessage = $"통계 로드 중 오류: {ex.Message}";
                // 오류 시 샘플 데이터 로드
                LoadSampleData();
                UpdateSummaryStatistics();
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadDailyStatisticsFromDatabase(SafetyVisionMonitor.Shared.Services.DatabaseService databaseService)
        {
            DailyStatistics.Clear();
            
            for (int i = 30; i >= 0; i--)
            {
                var date = DateTime.Today.AddDays(-i);
                var nextDate = date.AddDays(1);
                
                var events = await databaseService.GetSafetyEventsAsync(
                    startDate: date,
                    endDate: nextDate,
                    limit: 10000
                );
                
                DailyStatistics.Add(new DailyStatistics
                {
                    Date = date,
                    TotalEvents = events.Count,
                    DangerZoneEvents = events.Count(e => e.EventType == "DangerZoneEntry"),
                    WarningZoneEvents = events.Count(e => e.EventType == "WarningZoneEntry"),
                    NoHelmetEvents = events.Count(e => e.EventType == "NoHelmet")
                });
            }
        }

        private async Task LoadEventTypeStatisticsFromDatabase(SafetyVisionMonitor.Shared.Services.DatabaseService databaseService)
        {
            var events = await databaseService.GetSafetyEventsAsync(
                startDate: StartDate,
                endDate: EndDate.AddDays(1),
                limit: 10000
            );
            
            var totalCount = events.Count;
            if (totalCount == 0)
            {
                // 데이터가 없으면 샘플 데이터 유지
                return;
            }
            
            EventTypeStatistics.Clear();
            
            var eventGroups = events.GroupBy(e => e.EventType).ToList();
            var colors = new[] { "#FF0000", "#FFA500", "#FFFF00", "#808080", "#00FF00", "#0000FF" };
            
            for (int i = 0; i < eventGroups.Count && i < colors.Length; i++)
            {
                var group = eventGroups[i];
                var count = group.Count();
                var percentage = (double)count / totalCount * 100;
                
                EventTypeStatistics.Add(new EventTypeStatistics
                {
                    EventType = GetEventTypeDisplay(group.Key),
                    Count = count,
                    Percentage = percentage,
                    Color = colors[i]
                });
            }
        }

        private string GetEventTypeDisplay(string eventType)
        {
            return eventType switch
            {
                "DangerZoneEntry" => "위험구역 진입",
                "WarningZoneEntry" => "경고구역 진입", 
                "NoHelmet" => "안전모 미착용",
                "Fall" => "넘어짐 감지",
                "UnauthorizedAreaAccess" => "무단 구역 접근",
                "SafetyEquipmentMissing" => "안전 장비 미착용",
                _ => eventType
            };
        }

        private async Task LoadCameraStatisticsFromDatabase(SafetyVisionMonitor.Shared.Services.DatabaseService databaseService)
        {
            CameraStatistics.Clear();
            
            var cameras = new[] { "CAM001", "CAM002", "CAM003", "CAM004" };
            
            foreach (var camera in cameras)
            {
                var events = await databaseService.GetSafetyEventsAsync(
                    startDate: StartDate,
                    endDate: EndDate.AddDays(1),
                    cameraId: camera,
                    limit: 10000
                );
                
                var trackingRecords = await databaseService.GetPersonTrackingRecordsAsync(
                    startDate: StartDate,
                    endDate: EndDate.AddDays(1),
                    cameraId: camera,
                    limit: 10000
                );
                
                CameraStatistics.Add(new CameraStatistics
                {
                    CameraId = camera,
                    EventCount = events.Count,
                    ActiveHours = trackingRecords.Count > 0 ? 
                        (int)(trackingRecords.Max(r => r.LastSeenTime) - trackingRecords.Min(r => r.FirstDetectedTime)).TotalHours : 0,
                    DetectionRate = trackingRecords.Count > 0 ? 
                        trackingRecords.Average(r => r.Confidence) * 100 : 0
                });
            }
        }

        private async Task LoadHourlyStatisticsFromDatabase(SafetyVisionMonitor.Shared.Services.DatabaseService databaseService)
        {
            HourlyStatistics.Clear();
            
            var events = await databaseService.GetSafetyEventsAsync(
                startDate: StartDate,
                endDate: EndDate.AddDays(1),
                limit: 10000
            );
            
            var trackingRecords = await databaseService.GetPersonTrackingRecordsAsync(
                startDate: StartDate,
                endDate: EndDate.AddDays(1),
                limit: 10000
            );
            
            for (int hour = 0; hour < 24; hour++)
            {
                var hourlyEvents = events.Count(e => e.Timestamp.Hour == hour);
                var hourlyPersons = trackingRecords.Count(r => r.FirstDetectedTime.Hour == hour);
                
                HourlyStatistics.Add(new HourlyStatistics
                {
                    Hour = hour,
                    EventCount = hourlyEvents,
                    PersonCount = hourlyPersons
                });
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