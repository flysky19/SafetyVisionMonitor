using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafetyVisionMonitor.Shared.Models;
using SafetyVisionMonitor.Shared.ViewModels.Base;

namespace SafetyVisionHistoryViewer.ViewModels
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
        
        [ObservableProperty]
        private string loadButtonText = "🔍 조회";
        
        public EventLogViewModel()
        {
            Title = "이벤트/로그";
            Events = new ObservableCollection<SafetyEventViewModel>();
            EventTypes = new ObservableCollection<string> { "전체", "DangerZoneEntry", "WarningZoneEntry", "NoHelmet", "Fall" };
            Cameras = new ObservableCollection<string> { "전체", "CAM001", "CAM002", "CAM003", "CAM004" };
            
            System.Diagnostics.Debug.WriteLine($"EventLogViewModel: Constructor completed");
            System.Diagnostics.Debug.WriteLine($"EventLogViewModel: EventTypes.Count = {EventTypes.Count}");
            System.Diagnostics.Debug.WriteLine($"EventLogViewModel: Cameras.Count = {Cameras.Count}");
            System.Diagnostics.Debug.WriteLine($"EventLogViewModel: StartDate = {StartDate}");
            System.Diagnostics.Debug.WriteLine($"EventLogViewModel: EndDate = {EndDate}");
            System.Diagnostics.Debug.WriteLine($"EventLogViewModel: SelectedEventType = {SelectedEventType}");
            System.Diagnostics.Debug.WriteLine($"EventLogViewModel: SelectedCamera = {SelectedCamera}");
            
            //LoadSampleData();
        }
        
        public override async void OnLoaded()
        {
            base.OnLoaded();
            System.Diagnostics.Debug.WriteLine($"EventLogViewModel: OnLoaded called");
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
                "DangerZoneEntry" => "위험구역 진입",
                "WarningZoneEntry" => "경고구역 진입",
                "NoHelmet" => "안전모 미착용",
                "Fall" => "넘어짐 감지",
                "UnauthorizedAreaAccess" => "무단 구역 접근",
                "SafetyEquipmentMissing" => "안전 장비 미착용",
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
        private async Task AddTestEvent()
        {
            try
            {
                var timestamp = DateTime.Now;
                var imagePath = CreateTestImagePath(timestamp);
                var videoPath = CreateTestVideoPath(timestamp);
                
                var testEvent = new SafetyEvent
                {
                    Id = 0,
                    EventType = "DangerZoneEntry",
                    CameraId = "CAM001",
                    ZoneId = "TestZone",
                    PersonBoundingBox = "100,200,50,150",
                    Confidence = 0.85,
                    Timestamp = timestamp,
                    Severity = "High",
                    Description = "테스트용 위험구역 진입 이벤트 - 작업자 (왼손 감지)가 위험구역 'TestZone'에 진입했습니다.",
                    IsAcknowledged = false,
                    PersonTrackingId = "P001",
                    // 새로운 폴더 구조에 맞는 테스트 파일 경로
                    ImagePath = imagePath,
                    VideoClipPath = videoPath,
                    // 새로운 확장 필드들
                    ProcessingTimeMs = 245.5,
                    ProcessingStatus = "{\"DatabaseSaved\": true, \"AlertSent\": true, \"MediaCaptured\": true}",
                    ImageFileSize = 125000,
                    VideoFileSize = 2500000,
                    VideoDurationSeconds = 10.0,
                    NotificationStatus = "{\"EmailSent\": false, \"SMSSent\": false, \"AlertShown\": true}",
                    Metadata = "{\"DetectedBodyPart\": \"왼손\", \"ZoneType\": \"Danger\", \"AlertLevel\": \"Critical\", \"HandlerChain\": [\"AlertHandler\", \"MediaCaptureHandler\", \"DatabaseHandler\"]}",
                    BoundingBoxJson = "{\"X\": 100, \"Y\": 200, \"Width\": 50, \"Height\": 150, \"CenterX\": 125, \"CenterY\": 275, \"Area\": 7500}"
                };

                var databaseService = SafetyVisionHistoryViewer.App.DatabaseService;
                if (databaseService != null)
                {
                    // 저장 전 테스트 이벤트 상태 확인
                    System.Diagnostics.Debug.WriteLine($"=== BEFORE SAVE ===");
                    System.Diagnostics.Debug.WriteLine($"TestEvent.ImagePath: '{testEvent.ImagePath}'");
                    System.Diagnostics.Debug.WriteLine($"TestEvent.VideoClipPath: '{testEvent.VideoClipPath}'");
                    System.Diagnostics.Debug.WriteLine($"TestEvent.EventType: '{testEvent.EventType}'");
                    
                    var savedId = await databaseService.SaveSafetyEventAsync(testEvent);
                    StatusMessage = $"테스트 이벤트가 저장되었습니다. (ID: {savedId})";
                    
                    // 저장 후 실제 DB에서 불러와서 확인
                    var savedEvent = await databaseService.GetSafetyEventAsync(savedId);
                    System.Diagnostics.Debug.WriteLine($"=== AFTER SAVE (ID: {savedId}) ===");
                    if (savedEvent != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"SavedEvent.ImagePath: '{savedEvent.ImagePath}'");
                        System.Diagnostics.Debug.WriteLine($"SavedEvent.VideoClipPath: '{savedEvent.VideoClipPath}'");
                        System.Diagnostics.Debug.WriteLine($"SavedEvent.EventType: '{savedEvent.EventType}'");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to retrieve saved event!");
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"File existence check:");
                    System.Diagnostics.Debug.WriteLine($"  - Image: {imagePath} (Exists: {File.Exists(imagePath)})");
                    System.Diagnostics.Debug.WriteLine($"  - Video: {videoPath} (Exists: {File.Exists(videoPath)})");
                    
                    // 데이터 새로고침
                    await LoadEvents();
                }
                else
                {
                    StatusMessage = "데이터베이스 서비스를 사용할 수 없습니다.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"테스트 이벤트 추가 중 오류: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Add test event error: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task LoadEvents()
        {
            IsLoading = true;
            LoadButtonText = "⏳ 조회 중...";
            
            try
            {
                System.Diagnostics.Debug.WriteLine($"EventLogViewModel: LoadEvents started");
                System.Diagnostics.Debug.WriteLine($"EventLogViewModel: StartDate={StartDate}, EndDate={EndDate}");
                System.Diagnostics.Debug.WriteLine($"EventLogViewModel: SelectedEventType={SelectedEventType}, SelectedCamera={SelectedCamera}");
                
                // DatabaseService에서 실제 안전 이벤트 데이터 로드
                var databaseService = SafetyVisionHistoryViewer.App.DatabaseService;
                System.Diagnostics.Debug.WriteLine($"EventLogViewModel: DatabaseService is {(databaseService != null ? "available" : "null")}");
                
                if (databaseService != null)
                {
                    var safetyEvents = await databaseService.GetSafetyEventsAsync(
                        startDate: StartDate,
                        endDate: EndDate.AddDays(1), // 종료일 포함
                        eventType: SelectedEventType == "전체" ? null : SelectedEventType,
                        cameraId: SelectedCamera == "전체" ? null : SelectedCamera,
                        limit: 1000
                    );
                    
                    // SafetyEvent를 SafetyEventViewModel로 변환
                    Events.Clear();
                    foreach (var evt in safetyEvents)
                    {
                        // 실제 파일 존재 여부 확인
                        var hasImage = !string.IsNullOrEmpty(evt.ImagePath) && File.Exists(evt.ImagePath);
                        var hasVideo = !string.IsNullOrEmpty(evt.VideoClipPath) && File.Exists(evt.VideoClipPath);
                        
                        Events.Add(new SafetyEventViewModel
                        {
                            Id = evt.Id,
                            Timestamp = evt.Timestamp,
                            EventType = evt.EventType,
                            EventTypeDisplay = GetEventTypeDisplay(evt.EventType),
                            CameraId = evt.CameraId ?? "Unknown",
                            PersonTrackingId = evt.PersonTrackingId,
                            Confidence = evt.Confidence,
                            Description = evt.Description ?? "",
                            ImagePath = evt.ImagePath,
                            VideoClipPath = evt.VideoClipPath,
                            HasImage = hasImage,
                            HasVideo = hasVideo,
                            IsVisible = true
                        });
                        
                        // 디버깅을 위한 상세 로그
                        System.Diagnostics.Debug.WriteLine($"Event {evt.Id}: HasImage={hasImage}, HasVideo={hasVideo}");
                        if (!string.IsNullOrEmpty(evt.ImagePath))
                        {
                            System.Diagnostics.Debug.WriteLine($"  - ImagePath: {evt.ImagePath} (Exists: {hasImage})");
                        }
                        if (!string.IsNullOrEmpty(evt.VideoClipPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"  - VideoPath: {evt.VideoClipPath} (Exists: {hasVideo})");
                        }
                    }
                    
                    StatusMessage = $"실제 {Events.Count}개의 안전 이벤트를 불러왔습니다.";
                    System.Diagnostics.Debug.WriteLine($"EventLogViewModel: Loaded {Events.Count} safety events from database");
                }
                else
                {
                    StatusMessage = "데이터베이스 서비스에 연결할 수 없습니다.";
                    System.Diagnostics.Debug.WriteLine("EventLogViewModel: DatabaseService is null");
                }
                
                // 필터링 적용
                ApplyFilters();
            }
            catch (Exception ex)
            {
                StatusMessage = $"이벤트 로드 중 오류 발생: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"EventLogViewModel: Load events error - {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                LoadButtonText = "🔍 조회";
                System.Diagnostics.Debug.WriteLine($"EventLogViewModel: LoadEvents completed. IsLoading = {IsLoading}");
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
            var details = $"이벤트 ID: {safetyEvent.Id}\n" +
                         $"시간: {safetyEvent.Timestamp:yyyy-MM-dd HH:mm:ss}\n" +
                         $"타입: {safetyEvent.EventTypeDisplay}\n" +
                         $"카메라: {safetyEvent.CameraId}\n" +
                         $"신뢰도: {safetyEvent.Confidence:P1}\n" +
                         $"추적ID: {safetyEvent.PersonTrackingId}\n" +
                         $"설명: {safetyEvent.Description}\n\n";

            if (safetyEvent.HasImage)
            {
                details += $"이미지: {safetyEvent.ImagePath}\n";
            }
            if (safetyEvent.HasVideo)
            {
                details += $"동영상: {safetyEvent.VideoClipPath}\n";
            }

            MessageBox.Show(details, "이벤트 상세", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        
        [RelayCommand]
        private void OpenImage(SafetyEventViewModel safetyEvent)
        {
            if (!safetyEvent.HasImage || string.IsNullOrEmpty(safetyEvent.ImagePath))
            {
                MessageBox.Show("이미지 파일이 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (File.Exists(safetyEvent.ImagePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = safetyEvent.ImagePath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show("이미지 파일을 찾을 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"이미지를 열 수 없습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        [RelayCommand]
        private void OpenVideo(SafetyEventViewModel safetyEvent)
        {
            if (!safetyEvent.HasVideo || string.IsNullOrEmpty(safetyEvent.VideoClipPath))
            {
                MessageBox.Show("동영상 파일이 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (File.Exists(safetyEvent.VideoClipPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = safetyEvent.VideoClipPath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show("동영상 파일을 찾을 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"동영상을 열 수 없습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        [RelayCommand]
        private void OpenFolder(SafetyEventViewModel safetyEvent)
        {
            try
            {
                string? folderPath = null;
                
                // 이미지 파일 경로가 있으면 그 폴더를 열기
                if (!string.IsNullOrEmpty(safetyEvent.ImagePath) && File.Exists(safetyEvent.ImagePath))
                {
                    folderPath = Path.GetDirectoryName(safetyEvent.ImagePath);
                }
                // 이미지가 없으면 동영상 파일 경로의 폴더를 열기
                else if (!string.IsNullOrEmpty(safetyEvent.VideoClipPath) && File.Exists(safetyEvent.VideoClipPath))
                {
                    folderPath = Path.GetDirectoryName(safetyEvent.VideoClipPath);
                }
                // 둘 다 없지만 경로 정보는 있는 경우
                else if (!string.IsNullOrEmpty(safetyEvent.ImagePath))
                {
                    folderPath = Path.GetDirectoryName(safetyEvent.ImagePath);
                }
                else if (!string.IsNullOrEmpty(safetyEvent.VideoClipPath))
                {
                    folderPath = Path.GetDirectoryName(safetyEvent.VideoClipPath);
                }
                
                if (!string.IsNullOrEmpty(folderPath))
                {
                    // 폴더가 존재하지 않으면 생성
                    if (!Directory.Exists(folderPath))
                    {
                        Directory.CreateDirectory(folderPath);
                        System.Diagnostics.Debug.WriteLine($"EventLogViewModel: Created folder - {folderPath}");
                    }
                    
                    // 폴더 열기
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = folderPath,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                    
                    System.Diagnostics.Debug.WriteLine($"EventLogViewModel: Opened folder - {folderPath}");
                }
                else
                {
                    // 기본 SafetyEvents 폴더 열기
                    var defaultPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "SafetyVisionMonitor",
                        "SafetyEvents"
                    );
                    
                    if (!Directory.Exists(defaultPath))
                    {
                        Directory.CreateDirectory(defaultPath);
                    }
                    
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = defaultPath,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                    
                    StatusMessage = "미디어 파일이 없어 기본 저장 폴더를 열었습니다.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"폴더를 열 수 없습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"EventLogViewModel: Failed to open folder - {ex.Message}");
            }
        }
        
        [RelayCommand]
        private async Task DeleteEvent(SafetyEventViewModel safetyEvent)
        {
            // 두 단계 확인으로 실수 방지
            var firstConfirm = MessageBox.Show(
                $"이벤트를 삭제하시겠습니까?\n\n" +
                $"이벤트 ID: {safetyEvent.Id}\n" +
                $"시간: {safetyEvent.Timestamp:yyyy-MM-dd HH:mm:ss}\n" +
                $"타입: {safetyEvent.EventTypeDisplay}\n" +
                $"카메라: {safetyEvent.CameraId}", 
                "이벤트 삭제 - 1단계 확인",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
                
            if (firstConfirm != MessageBoxResult.Yes)
                return;
                
            var secondConfirm = MessageBox.Show(
                "정말로 삭제하시겠습니까?\n\n" +
                "※ 이 작업은 되돌릴 수 없습니다.\n" +
                "※ 관련된 이미지와 동영상 파일도 함께 삭제됩니다.", 
                "이벤트 삭제 - 최종 확인",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
                
            if (secondConfirm == MessageBoxResult.Yes)
            {
                try
                {
                    // 실제 데이터베이스에서 삭제
                    var databaseService = SafetyVisionHistoryViewer.App.DatabaseService;
                    if (databaseService != null)
                    {
                        var deleteSuccess = await databaseService.DeleteSafetyEventAsync(safetyEvent.Id);
                        
                        if (deleteSuccess)
                        {
                            // DB 삭제 성공 시에만 UI에서 제거
                            Events.Remove(safetyEvent);
                            UpdateCounts();
                            StatusMessage = $"이벤트 ID {safetyEvent.Id}가 삭제되었습니다.";
                            
                            System.Diagnostics.Debug.WriteLine($"EventLogViewModel: Event {safetyEvent.Id} deleted successfully");
                        }
                        else
                        {
                            StatusMessage = $"이벤트 ID {safetyEvent.Id} 삭제에 실패했습니다 (이벤트를 찾을 수 없음).";
                            MessageBox.Show("삭제할 이벤트를 데이터베이스에서 찾을 수 없습니다.", "오류", 
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    else
                    {
                        StatusMessage = "데이터베이스 서비스에 연결할 수 없습니다.";
                        MessageBox.Show("데이터베이스 서비스에 연결할 수 없습니다.", "오류", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"이벤트 삭제 중 오류 발생: {ex.Message}";
                    MessageBox.Show($"이벤트 삭제 중 오류가 발생했습니다:\n{ex.Message}", "오류", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    
                    System.Diagnostics.Debug.WriteLine($"EventLogViewModel: Delete event error - {ex.Message}");
                }
            }
        }
        
        partial void OnStartDateChanged(DateTime value)
        {
            if (value > EndDate)
            {
                EndDate = value;
            }
            // 자동 조회 제거: 사용자가 조회 버튼을 눌러야 함
        }
        
        partial void OnEndDateChanged(DateTime value)
        {
            if (value < StartDate)
            {
                StartDate = value;
            }
            // 자동 조회 제거: 사용자가 조회 버튼을 눌러야 함
        }
        
        partial void OnSelectedEventTypeChanged(string? value)
        {
            // 로드된 데이터에 대해서만 필터링 적용
            if (Events.Any())
            {
                ApplyFilters();
            }
        }
        
        partial void OnSelectedCameraChanged(string? value)
        {
            // 로드된 데이터에 대해서만 필터링 적용
            if (Events.Any())
            {
                ApplyFilters();
            }
        }
        
        private void UpdateCounts()
        {
            TotalEventCount = Events.Count;
            FilteredEventCount = Events.Count(e => e.IsVisible);
        }
        
        /// <summary>
        /// 테스트용 이미지 파일 경로 생성 및 실제 파일 생성
        /// </summary>
        private string CreateTestImagePath(DateTime timestamp)
        {
            try
            {
                var baseStoragePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SafetyVisionMonitor",
                    "SafetyEvents"
                );
                
                // 새로운 폴더 구조: 년-월/일/시/분
                var yearMonth = timestamp.ToString("yyyy-MM");
                var day = timestamp.ToString("dd");
                var hour = timestamp.ToString("HH");
                var minute = timestamp.ToString("mm");
                
                var folderPath = Path.Combine(baseStoragePath, yearMonth, day, hour, minute);
                Directory.CreateDirectory(folderPath);
                
                var fileName = $"IMG_{timestamp:HH-mm-ss}_CAM001_TestZone.jpg";
                var filePath = Path.Combine(folderPath, fileName);
                
                // 테스트용 더미 이미지 파일 생성
                CreateDummyImageFile(filePath);
                
                return filePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateTestImagePath error: {ex.Message}");
                return "";
            }
        }
        
        /// <summary>
        /// 테스트용 동영상 파일 경로 생성 및 실제 파일 생성
        /// </summary>
        private string CreateTestVideoPath(DateTime timestamp)
        {
            try
            {
                var baseStoragePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SafetyVisionMonitor",
                    "SafetyEvents"
                );
                
                // 새로운 폴더 구조: 년-월/일/시/분
                var yearMonth = timestamp.ToString("yyyy-MM");
                var day = timestamp.ToString("dd");
                var hour = timestamp.ToString("HH");
                var minute = timestamp.ToString("mm");
                
                var folderPath = Path.Combine(baseStoragePath, yearMonth, day, hour, minute);
                Directory.CreateDirectory(folderPath);
                
                var fileName = $"VID_{timestamp:HH-mm-ss}_CAM001_TestZone.mp4";
                var filePath = Path.Combine(folderPath, fileName);
                
                // 테스트용 더미 동영상 파일 생성
                CreateDummyVideoFile(filePath);
                
                return filePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateTestVideoPath error: {ex.Message}");
                return "";
            }
        }
        
        /// <summary>
        /// 테스트용 더미 이미지 파일 생성
        /// </summary>
        private void CreateDummyImageFile(string filePath)
        {
            try
            {
                // 간단한 1x1 픽셀 JPEG 파일 데이터 (base64)
                var dummyImageBytes = Convert.FromBase64String(
                    "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQH/2wBDAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQH/wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAv/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAAAAX/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCdABmX/9k="
                );
                
                File.WriteAllBytes(filePath, dummyImageBytes);
                System.Diagnostics.Debug.WriteLine($"Created test image: {filePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateDummyImageFile error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 테스트용 더미 동영상 파일 생성
        /// </summary>
        private void CreateDummyVideoFile(string filePath)
        {
            try
            {
                // 최소한의 MP4 헤더를 가진 더미 파일 생성 (몇 KB)
                var dummyVideoData = new byte[] {
                    0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D,
                    0x00, 0x00, 0x02, 0x00, 0x69, 0x73, 0x6F, 0x6D, 0x69, 0x73, 0x6F, 0x32,
                    0x61, 0x76, 0x63, 0x31, 0x6D, 0x70, 0x34, 0x31, 0x00, 0x00, 0x00, 0x08
                };
                
                File.WriteAllBytes(filePath, dummyVideoData);
                System.Diagnostics.Debug.WriteLine($"Created test video: {filePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateDummyVideoFile error: {ex.Message}");
            }
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