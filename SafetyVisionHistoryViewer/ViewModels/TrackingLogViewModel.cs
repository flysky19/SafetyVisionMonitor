using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafetyVisionMonitor.Shared.Models;
using SafetyVisionMonitor.Shared.ViewModels.Base;

namespace SafetyVisionHistoryViewer.ViewModels
{
    public partial class TrackingLogViewModel : BaseViewModel
    {
        [ObservableProperty]
        private ObservableCollection<PersonTrackingRecordViewModel> trackingRecords;
        
        [ObservableProperty]
        private ObservableCollection<string> cameras;
        
        [ObservableProperty]
        private ObservableCollection<string> statusFilters;
        
        [ObservableProperty]
        private DateTime startDate = DateTime.Today.AddDays(-7);
        
        [ObservableProperty]
        private DateTime endDate = DateTime.Today;
        
        [ObservableProperty]
        private string? selectedCamera = "전체";
        
        [ObservableProperty]
        private string? selectedStatus = "전체";
        
        [ObservableProperty]
        private int totalRecordCount;
        
        [ObservableProperty]
        private int filteredRecordCount;
        
        [ObservableProperty]
        private int activeTrackingCount;
        
        [ObservableProperty]
        private bool isExporting = false;
        
        [ObservableProperty]
        private string loadButtonText = "🔍 조회";
        
        public TrackingLogViewModel()
        {
            Title = "트래킹 로그";
            TrackingRecords = new ObservableCollection<PersonTrackingRecordViewModel>();
            Cameras = new ObservableCollection<string> { "전체", "CAM001", "CAM002", "CAM003", "CAM004" };
            StatusFilters = new ObservableCollection<string> { "전체", "활성", "비활성" };
        }
        
        public override async void OnLoaded()
        {
            base.OnLoaded();
            await LoadTrackingRecords();
        }
        
        [RelayCommand]
        private async Task LoadTrackingRecords()
        {
            IsLoading = true;
            LoadButtonText = "⏳ 조회 중...";
            
            try
            {
                var databaseService = SafetyVisionHistoryViewer.App.DatabaseService;
                if (databaseService != null)
                {
                    // PersonTrackingRecord 조회를 위한 새 메서드 필요
                    var records = await databaseService.GetPersonTrackingRecordsAsync(
                        startDate: StartDate,
                        endDate: EndDate.AddDays(1),
                        cameraId: SelectedCamera == "전체" ? null : SelectedCamera,
                        isActive: SelectedStatus == "전체" ? null : SelectedStatus == "활성",
                        limit: 1000
                    );
                    
                    TrackingRecords.Clear();
                    foreach (var record in records)
                    {
                        TrackingRecords.Add(new PersonTrackingRecordViewModel
                        {
                            Id = record.Id,
                            TrackingId = record.TrackingId,
                            GlobalTrackingId = record.GlobalTrackingId,
                            CameraId = record.CameraId,
                            BoundingBoxX = record.BoundingBoxX,
                            BoundingBoxY = record.BoundingBoxY,
                            BoundingBoxWidth = record.BoundingBoxWidth,
                            BoundingBoxHeight = record.BoundingBoxHeight,
                            CenterX = record.CenterX,
                            CenterY = record.CenterY,
                            Confidence = record.Confidence,
                            TrackingHistoryJson = record.TrackingHistoryJson,
                            Location = record.Location,
                            IsActive = record.IsActive,
                            FirstDetectedTime = record.FirstDetectedTime,
                            LastSeenTime = record.LastSeenTime,
                            LastUpdated = record.LastUpdated,
                            IsVisible = true
                        });
                    }
                    
                    StatusMessage = $"{TrackingRecords.Count}개의 트래킹 기록을 불러왔습니다.";
                }
                else
                {
                    StatusMessage = "데이터베이스 서비스에 연결할 수 없습니다.";
                }
                
                ApplyFilters();
            }
            catch (Exception ex)
            {
                StatusMessage = $"트래킹 기록 로드 중 오류 발생: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"TrackingLogViewModel: Load records error - {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                LoadButtonText = "🔍 조회";
            }
        }
        
        [RelayCommand]
        private void ApplyFilters()
        {
            var filtered = TrackingRecords.AsEnumerable();
            
            // 날짜 필터
            filtered = filtered.Where(r => r.FirstDetectedTime >= StartDate && r.FirstDetectedTime <= EndDate.AddDays(1));
            
            // 카메라 필터
            if (SelectedCamera != "전체")
            {
                filtered = filtered.Where(r => r.CameraId == SelectedCamera);
            }
            
            // 상태 필터
            if (SelectedStatus == "활성")
            {
                filtered = filtered.Where(r => r.IsActive);
            }
            else if (SelectedStatus == "비활성")
            {
                filtered = filtered.Where(r => !r.IsActive);
            }
            
            // UI 업데이트
            foreach (var record in TrackingRecords)
            {
                record.IsVisible = filtered.Contains(record);
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
                await Task.Delay(1000);
                
                MessageBox.Show("트래킹 로그가 Excel 파일로 내보내졌습니다.", "내보내기 완료",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                IsExporting = false;
            }
        }
        
        [RelayCommand]
        private void ViewTrackingDetail(PersonTrackingRecordViewModel record)
        {
            var details = $"추적 기록 ID: {record.Id}\n" +
                         $"추적 ID: {record.TrackingId}\n" +
                         $"글로벌 ID: {record.GlobalTrackingId?.ToString() ?? "N/A"}\n" +
                         $"카메라: {record.CameraId}\n" +
                         $"최초 검출: {record.FirstDetectedTime:yyyy-MM-dd HH:mm:ss}\n" +
                         $"마지막 목격: {record.LastSeenTime:yyyy-MM-dd HH:mm:ss}\n" +
                         $"지속 시간: {record.TrackingDurationDisplay}\n" +
                         $"신뢰도: {record.ConfidenceDisplay}\n" +
                         $"위치: {record.Location}\n" +
                         $"중심점: ({record.CenterX:F1}, {record.CenterY:F1})\n" +
                         $"바운딩 박스: ({record.BoundingBoxX:F1}, {record.BoundingBoxY:F1}, " +
                         $"{record.BoundingBoxWidth:F1}, {record.BoundingBoxHeight:F1})\n" +
                         $"상태: {record.StatusDisplay}";

            MessageBox.Show(details, "트래킹 기록 상세", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        
        [RelayCommand]
        private void ViewTrackingPath(PersonTrackingRecordViewModel record)
        {
            // TODO: 트래킹 경로 시각화 창 열기
            MessageBox.Show($"추적 ID {record.TrackingId}의 이동 경로를 표시합니다.\n\n" +
                           $"추적 히스토리: {record.TrackingHistoryJson}", 
                           "트래킹 경로", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        
        [RelayCommand]
        private async Task DeleteTrackingRecord(PersonTrackingRecordViewModel record)
        {
            var result = MessageBox.Show(
                $"트래킹 기록을 삭제하시겠습니까?\n\n" +
                $"추적 ID: {record.TrackingId}\n" +
                $"카메라: {record.CameraId}\n" +
                $"검출 시간: {record.FirstDetectedTime:yyyy-MM-dd HH:mm:ss}",
                "트래킹 기록 삭제",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var databaseService = SafetyVisionHistoryViewer.App.DatabaseService;
                    if (databaseService != null)
                    {
                        var deleteSuccess = await databaseService.DeletePersonTrackingRecordAsync(record.Id);
                        
                        if (deleteSuccess)
                        {
                            TrackingRecords.Remove(record);
                            UpdateCounts();
                            StatusMessage = $"트래킹 기록 ID {record.Id}가 삭제되었습니다.";
                        }
                        else
                        {
                            StatusMessage = $"트래킹 기록 삭제에 실패했습니다.";
                            MessageBox.Show("삭제할 기록을 찾을 수 없습니다.", "오류", 
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    else
                    {
                        StatusMessage = "데이터베이스 서비스에 연결할 수 없습니다.";
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"삭제 중 오류 발생: {ex.Message}";
                    MessageBox.Show($"삭제 중 오류가 발생했습니다:\n{ex.Message}", "오류", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        partial void OnStartDateChanged(DateTime value)
        {
            if (value > EndDate)
            {
                EndDate = value;
            }
        }
        
        partial void OnEndDateChanged(DateTime value)
        {
            if (value < StartDate)
            {
                StartDate = value;
            }
        }
        
        partial void OnSelectedCameraChanged(string? value)
        {
            if (TrackingRecords.Any())
            {
                ApplyFilters();
            }
        }
        
        partial void OnSelectedStatusChanged(string? value)
        {
            if (TrackingRecords.Any())
            {
                ApplyFilters();
            }
        }
        
        private void UpdateCounts()
        {
            TotalRecordCount = TrackingRecords.Count;
            FilteredRecordCount = TrackingRecords.Count(r => r.IsVisible);
            ActiveTrackingCount = TrackingRecords.Count(r => r.IsActive && r.IsVisible);
        }
    }
    
    // 트래킹 기록 ViewModel
    public partial class PersonTrackingRecordViewModel : ObservableObject
    {
        [ObservableProperty]
        private int id;
        
        [ObservableProperty]
        private int trackingId;
        
        [ObservableProperty]
        private int? globalTrackingId;
        
        [ObservableProperty]
        private string cameraId = string.Empty;
        
        [ObservableProperty]
        private float boundingBoxX;
        
        [ObservableProperty]
        private float boundingBoxY;
        
        [ObservableProperty]
        private float boundingBoxWidth;
        
        [ObservableProperty]
        private float boundingBoxHeight;
        
        [ObservableProperty]
        private float centerX;
        
        [ObservableProperty]
        private float centerY;
        
        [ObservableProperty]
        private float confidence;
        
        [ObservableProperty]
        private string trackingHistoryJson = "[]";
        
        [ObservableProperty]
        private string location = "Unknown";
        
        [ObservableProperty]
        private bool isActive = true;
        
        [ObservableProperty]
        private DateTime firstDetectedTime;
        
        [ObservableProperty]
        private DateTime lastSeenTime;
        
        [ObservableProperty]
        private DateTime lastUpdated;
        
        [ObservableProperty]
        private bool isVisible = true;
        
        public string FirstDetectedTimeDisplay => FirstDetectedTime.ToString("yyyy-MM-dd HH:mm:ss");
        
        public string LastSeenTimeDisplay => LastSeenTime.ToString("yyyy-MM-dd HH:mm:ss");
        
        public string TrackingDurationDisplay
        {
            get
            {
                var duration = (LastSeenTime - FirstDetectedTime).TotalSeconds;
                return $"{duration:F1}";
            }
        }
        
        public string ConfidenceDisplay => $"{Confidence:P1}";
        
        public string StatusDisplay => IsActive ? "활성" : "비활성";
    }
}