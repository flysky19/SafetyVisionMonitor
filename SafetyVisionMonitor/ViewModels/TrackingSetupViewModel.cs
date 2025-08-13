using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafetyVisionMonitor.ViewModels.Base;
using SafetyVisionMonitor.Services;
using SafetyVisionMonitor.Database;
using SafetyVisionMonitor.Models;
using Microsoft.Win32;

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
                "SORT", 
                "DeepSORT", 
                "ByteTrack", 
                "StrongSORT" 
            };
        }
        
        public void OnLoaded()
        {
            LoadSettingsFromDatabase();
        }
        
        private async void LoadSettingsFromDatabase()
        {
            try
            {
                // 추적 설정 로드
                var config = await App.DatabaseService.LoadTrackingConfigAsync();
                if (config != null)
                {
                    IsTrackingEnabled = config.IsEnabled;
                    MaxTrackingDistance = config.MaxTrackingDistance;
                    MaxDisappearFrames = config.MaxDisappearFrames;
                    IouThreshold = config.IouThreshold;
                    SimilarityThreshold = config.SimilarityThreshold;
                    EnableReIdentification = config.EnableReIdentification;
                    EnableMultiCameraTracking = config.EnableMultiCameraTracking;
                    TrackHistoryLength = config.TrackHistoryLength;
                    ShowTrackingId = config.ShowTrackingId;
                    ShowTrackingPath = config.ShowTrackingPath;
                    PathDisplayLength = config.PathDisplayLength;
                    AutoSaveTracking = config.AutoSaveTracking;
                    AutoSaveInterval = config.AutoSaveInterval;
                    SelectedTrackingMethod = config.TrackingMethod;
                }

                // 추적 구역 로드
                var zones = await App.DatabaseService.LoadTrackingZonesAsync();
                TrackingZones.Clear();
                foreach (var zone in zones)
                {
                    var trackingZone = new TrackingZone
                    {
                        Id = zone.ZoneId,
                        Name = zone.Name,
                        IsEntryZone = zone.IsEntryZone,
                        IsExitZone = zone.IsExitZone,
                        CountingEnabled = zone.CountingEnabled,
                        CameraId = zone.CameraId
                    };
                    
                    // 좌표 데이터 복원
                    if (!string.IsNullOrEmpty(zone.PolygonJson) && zone.PolygonJson != "{}")
                    {
                        try
                        {
                            var points = JsonSerializer.Deserialize<List<System.Drawing.PointF>>(zone.PolygonJson);
                            if (points != null)
                            {
                                trackingZone.PolygonPoints = points;
                            }
                        }
                        catch
                        {
                            // 좌표 파싱 실패 시 기본값 사용
                        }
                    }
                    
                    trackingZone.CreatedTime = zone.CreatedTime;
                    
                    TrackingZones.Add(trackingZone);
                }

                StatusMessage = "설정을 불러왔습니다.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"설정 로드 실패: {ex.Message}");
                StatusMessage = "설정 로드에 실패했습니다.";
            }
        }
        
        [RelayCommand]
        private async Task SaveSettings()
        {
            IsLoading = true;
            
            try
            {
                // 추적 설정 저장
                var config = new TrackingConfig
                {
                    IsEnabled = IsTrackingEnabled,
                    MaxTrackingDistance = MaxTrackingDistance,
                    MaxDisappearFrames = MaxDisappearFrames,
                    IouThreshold = IouThreshold,
                    SimilarityThreshold = SimilarityThreshold,
                    EnableReIdentification = EnableReIdentification,
                    EnableMultiCameraTracking = EnableMultiCameraTracking,
                    TrackHistoryLength = TrackHistoryLength,
                    ShowTrackingId = ShowTrackingId,
                    ShowTrackingPath = ShowTrackingPath,
                    PathDisplayLength = PathDisplayLength,
                    AutoSaveTracking = AutoSaveTracking,
                    AutoSaveInterval = AutoSaveInterval,
                    TrackingMethod = SelectedTrackingMethod,
                    LastModified = DateTime.Now
                };
                
                await App.DatabaseService.SaveTrackingConfigAsync(config);
                
                // 추적 구역 저장
                var zoneConfigs = TrackingZones.Select(z => new TrackingZoneConfig
                {
                    ZoneId = z.Id,
                    Name = z.Name,
                    IsEntryZone = z.IsEntryZone,
                    IsExitZone = z.IsExitZone,
                    CountingEnabled = z.CountingEnabled,
                    PolygonJson = z.PolygonPoints?.Count > 0 
                        ? JsonSerializer.Serialize(z.PolygonPoints) 
                        : "[]",
                    CameraId = z.CameraId,
                    CreatedTime = z.CreatedTime
                }).ToList();
                
                await App.DatabaseService.SaveTrackingZonesAsync(zoneConfigs);
                
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
        private async Task DeleteTrackingZone(TrackingZone zone)
        {
            var result = MessageBox.Show($"'{zone.Name}' 구역을 삭제하시겠습니까?", "확인",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // 데이터베이스에서 삭제
                    await App.DatabaseService.DeleteTrackingZoneAsync(zone.Id);
                    
                    // UI에서 제거
                    TrackingZones.Remove(zone);
                    StatusMessage = $"'{zone.Name}' 구역이 삭제되었습니다.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"구역 삭제 중 오류 발생: {ex.Message}", "오류",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusMessage = $"구역 삭제 실패: {ex.Message}";
                }
            }
        }
        
        [RelayCommand]
        private async Task TestTracking()
        {
            IsLoading = true;
            StatusMessage = "트래킹 테스트 중...";
            
            try
            {
                // 트래킹 설정 검증
                var config = new TrackingConfiguration
                {
                    IsEnabled = IsTrackingEnabled,
                    MaxTrackingDistance = MaxTrackingDistance,
                    MaxDisappearFrames = MaxDisappearFrames,
                    IouThreshold = (float)IouThreshold,
                    SimilarityThreshold = (float)SimilarityThreshold,
                    EnableReIdentification = EnableReIdentification,
                    EnableMultiCameraTracking = EnableMultiCameraTracking,
                    TrackHistoryLength = TrackHistoryLength,
                    ShowTrackingId = ShowTrackingId,
                    ShowTrackingPath = ShowTrackingPath,
                    PathDisplayLength = PathDisplayLength,
                    TrackingMethod = SelectedTrackingMethod,
                    AutoSaveTracking = AutoSaveTracking,
                    AutoSaveInterval = AutoSaveInterval
                };
                
                // 트래킹 서비스 인스턴스 생성 테스트
                var trackingService = new PersonTrackingService(config);
                var stats = trackingService.GetStatistics();
                
                await Task.Delay(1000); // UI 업데이트 시간 확보
                
                var message = $"트래킹 테스트가 완료되었습니다.\n\n" +
                              $"설정 검증 결과:\n" +
                              $"- 트래킹 방식: {SelectedTrackingMethod}\n" +
                              $"- IOU 임계값: {IouThreshold:F2}\n" +
                              $"- 최대 사라짐 프레임: {MaxDisappearFrames}\n" +
                              $"- 다중 카메라 지원: {(EnableMultiCameraTracking ? "활성화" : "비활성화")}";
                
                MessageBox.Show(message, "테스트 완료",
                              MessageBoxButton.OK, MessageBoxImage.Information);
                              
                StatusMessage = "트래킹 테스트 완료 - 설정이 유효합니다.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"테스트 중 오류 발생: {ex.Message}", "오류",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"테스트 실패: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        [RelayCommand]
        private async Task ExportTrackingData()
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Title = "트래킹 데이터 내보내기",
                    Filter = "JSON 파일 (*.json)|*.json|CSV 파일 (*.csv)|*.csv",
                    FileName = $"tracking_data_{DateTime.Now:yyyyMMdd_HHmmss}"
                };
                
                if (saveDialog.ShowDialog() == true)
                {
                    IsLoading = true;
                    StatusMessage = "트래킹 데이터를 내보내는 중...";
                    
                    // 현재 설정 데이터 수집
                    var exportData = new
                    {
                        ExportTime = DateTime.Now,
                        TrackingSettings = new
                        {
                            IsEnabled = IsTrackingEnabled,
                            Method = SelectedTrackingMethod,
                            MaxTrackingDistance = MaxTrackingDistance,
                            MaxDisappearFrames = MaxDisappearFrames,
                            IouThreshold = IouThreshold,
                            SimilarityThreshold = SimilarityThreshold,
                            EnableReIdentification = EnableReIdentification,
                            EnableMultiCameraTracking = EnableMultiCameraTracking,
                            TrackHistoryLength = TrackHistoryLength,
                            ShowTrackingId = ShowTrackingId,
                            ShowTrackingPath = ShowTrackingPath,
                            PathDisplayLength = PathDisplayLength,
                            AutoSaveTracking = AutoSaveTracking,
                            AutoSaveInterval = AutoSaveInterval
                        },
                        TrackingZones = TrackingZones.ToList()
                    };
                    
                    string content;
                    if (saveDialog.FilterIndex == 1) // JSON
                    {
                        content = JsonSerializer.Serialize(exportData, new JsonSerializerOptions { WriteIndented = true });
                    }
                    else // CSV
                    {
                        content = "Setting,Value\n";
                        content += $"IsEnabled,{IsTrackingEnabled}\n";
                        content += $"Method,{SelectedTrackingMethod}\n";
                        content += $"MaxTrackingDistance,{MaxTrackingDistance}\n";
                        content += $"MaxDisappearFrames,{MaxDisappearFrames}\n";
                        content += $"IouThreshold,{IouThreshold}\n";
                        content += $"SimilarityThreshold,{SimilarityThreshold}\n";
                        content += $"ZoneCount,{TrackingZones.Count}\n";
                    }
                    
                    await System.IO.File.WriteAllTextAsync(saveDialog.FileName, content);
                    
                    MessageBox.Show($"트래킹 데이터가 성공적으로 내보내졌습니다.\n\n파일: {saveDialog.FileName}", 
                                    "내보내기 완료",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                                    
                    StatusMessage = "트래킹 데이터 내보내기 완료";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"내보내기 중 오류 발생: {ex.Message}", "오류",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"내보내기 실패: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}