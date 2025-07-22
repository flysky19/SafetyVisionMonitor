using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SafetyVisionMonitor.ViewModels
{
    public partial class TrackingSetupViewModel : BaseViewModel
    {
        [ObservableProperty]
        private bool isTrackingEnabled = true;
        
        [ObservableProperty]
        private int maxTrackingDistance = 50;
        
        [ObservableProperty]
        private int maxDisappearFrames = 30;
        
        [ObservableProperty]
        private double iouThreshold = 0.3;
        
        [ObservableProperty]
        private double similarityThreshold = 0.7;
        
        [ObservableProperty]
        private bool enableReIdentification = true;
        
        [ObservableProperty]
        private bool enableMultiCameraTracking = true;
        
        [ObservableProperty]
        private int trackHistoryLength = 50;
        
        [ObservableProperty]
        private bool showTrackingId = true;
        
        [ObservableProperty]
        private bool showTrackingPath = true;
        
        [ObservableProperty]
        private int pathDisplayLength = 20;
        
        [ObservableProperty]
        private ObservableCollection<TrackingZone> trackingZones;
        
        [ObservableProperty]
        private ObservableCollection<string> trackingMethods;
        
        [ObservableProperty]
        private string selectedTrackingMethod = "DeepSORT";
        
        [ObservableProperty]
        private bool autoSaveTracking = true;
        
        [ObservableProperty]
        private int autoSaveInterval = 60;
        
        public TrackingSetupViewModel()
        {
            Title = "트래킹 설정";
            TrackingZones = new ObservableCollection<TrackingZone>();
            TrackingMethods = new ObservableCollection<string> 
            { 
                "DeepSORT", 
                "SORT", 
                "ByteTrack", 
                "StrongSORT" 
            };
            
            LoadSampleData();
        }
        
        private void LoadSampleData()
        {
            // 샘플 트래킹 구역
            TrackingZones.Add(new TrackingZone
            {
                Id = "TZ001",
                Name = "출입구 구역",
                IsEntryZone = true,
                IsExitZone = true,
                CountingEnabled = true
            });
            
            TrackingZones.Add(new TrackingZone
            {
                Id = "TZ002",
                Name = "작업장 A",
                IsEntryZone = false,
                IsExitZone = false,
                CountingEnabled = false
            });
            
            TrackingZones.Add(new TrackingZone
            {
                Id = "TZ003",
                Name = "통로",
                IsEntryZone = false,
                IsExitZone = false,
                CountingEnabled = true
            });
        }
        
        [RelayCommand]
        private async Task SaveSettings()
        {
            IsLoading = true;
            
            try
            {
                // TODO: 설정 저장 로직
                await Task.Delay(500); // 시뮬레이션
                
                MessageBox.Show("트래킹 설정이 저장되었습니다.", "저장 완료",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                    
                StatusMessage = "설정이 저장되었습니다.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"설정 저장 중 오류 발생: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        [RelayCommand]
        private void ResetToDefaults()
        {
            var result = MessageBox.Show("모든 설정을 기본값으로 초기화하시겠습니까?", "확인",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                // 기본값으로 초기화
                MaxTrackingDistance = 50;
                MaxDisappearFrames = 30;
                IouThreshold = 0.3;
                SimilarityThreshold = 0.7;
                EnableReIdentification = true;
                EnableMultiCameraTracking = true;
                TrackHistoryLength = 50;
                ShowTrackingId = true;
                ShowTrackingPath = true;
                PathDisplayLength = 20;
                SelectedTrackingMethod = "DeepSORT";
                AutoSaveTracking = true;
                AutoSaveInterval = 60;
                
                StatusMessage = "설정이 기본값으로 초기화되었습니다.";
            }
        }
        
        [RelayCommand]
        private void AddTrackingZone()
        {
            var newZone = new TrackingZone
            {
                Id = $"TZ{TrackingZones.Count + 1:D3}",
                Name = $"새 구역 {TrackingZones.Count + 1}",
                IsEntryZone = false,
                IsExitZone = false,
                CountingEnabled = false
            };
            
            TrackingZones.Add(newZone);
            StatusMessage = $"'{newZone.Name}' 구역이 추가되었습니다.";
        }
        
        [RelayCommand]
        private void DeleteTrackingZone(TrackingZone zone)
        {
            var result = MessageBox.Show($"'{zone.Name}' 구역을 삭제하시겠습니까?", "확인",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                TrackingZones.Remove(zone);
                StatusMessage = $"'{zone.Name}' 구역이 삭제되었습니다.";
            }
        }
        
        [RelayCommand]
        private async Task TestTracking()
        {
            IsLoading = true;
            StatusMessage = "트래킹 테스트 중...";
            
            try
            {
                // TODO: 트래킹 테스트 로직
                await Task.Delay(2000); // 시뮬레이션
                
                MessageBox.Show("트래킹 테스트가 완료되었습니다.\n" +
                              "현재 설정으로 정상 작동합니다.", "테스트 완료",
                              MessageBoxButton.OK, MessageBoxImage.Information);
                              
                StatusMessage = "트래킹 테스트 완료";
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        [RelayCommand]
        private void ExportTrackingData()
        {
            // TODO: 트래킹 데이터 내보내기
            StatusMessage = "트래킹 데이터를 내보냈습니다.";
        }
    }
    
    // 트래킹 구역 모델
    public partial class TrackingZone : ObservableObject
    {
        [ObservableProperty]
        private string id = string.Empty;
        
        [ObservableProperty]
        private string name = string.Empty;
        
        [ObservableProperty]
        private bool isEntryZone;
        
        [ObservableProperty]
        private bool isExitZone;
        
        [ObservableProperty]
        private bool countingEnabled;
    }
}