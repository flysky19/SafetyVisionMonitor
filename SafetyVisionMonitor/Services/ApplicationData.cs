using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using SafetyVisionMonitor.Database;
using SafetyVisionMonitor.Models;

namespace SafetyVisionMonitor.Services
{
    public class ApplicationData
    {
        // 카메라 설정
        public ObservableCollection<Camera> Cameras { get; }
        
        // AI 모델 설정
        public ObservableCollection<AIModelConfig> AIModels { get; }
        
        // 3D 구역 설정
        public ObservableCollection<Zone3DConfig> Zones { get; }
        
        // 구역 업데이트 이벤트
        public event EventHandler<ZoneUpdateEventArgs>? ZoneUpdated;
        
        // 구역 시각화 업데이트 이벤트
        public event EventHandler? ZoneVisualizationUpdateRequested;
        
        // 최근 이벤트 (최근 100개만 메모리에 유지)
        public ObservableCollection<SafetyEvent> RecentEvents { get; }
        
        public ApplicationData()
        {
            Cameras = new ObservableCollection<Camera>();
            AIModels = new ObservableCollection<AIModelConfig>();
            Zones = new ObservableCollection<Zone3DConfig>();
            RecentEvents = new ObservableCollection<SafetyEvent>();
        }
        
        public async Task LoadAllDataAsync()
        {
            // 병렬로 데이터 로드
            var tasks = new List<Task>
            {
                LoadCamerasAsync(),
                LoadAIModelsAsync(),
                LoadZonesAsync(),
                LoadRecentEventsAsync()
            };
            
            await Task.WhenAll(tasks);
        }
        
        private async Task LoadCamerasAsync()
        {
            try
            {
                var cameras = await App.DatabaseService.LoadCameraConfigsAsync();
                
                // 4개 슬롯 유지
                for (int i = 0; i < 4; i++)
                {
                    var camera = cameras.FirstOrDefault(c => c.Id == $"CAM{i + 1:D3}");
                    if (camera == null)
                    {
                        camera = new Camera
                        {
                            Id = $"CAM{i + 1:D3}",
                            Name = $"카메라 {i + 1}",
                            Type = CameraType.RTSP,
                            Width = 1920,
                            Height = 1080,
                            Fps = 25
                        };
                    }
                    Cameras.Add(camera);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"카메라 로드 실패: {ex.Message}");
            }
        }
        
        private async Task LoadAIModelsAsync()
        {
            try
            {
                using var context = new AppDbContext();
                var models = context.AIModelConfigs.ToList();
                
                foreach (var model in models)
                {
                    App.Current.Dispatcher.Invoke(() => AIModels.Add(model));
                }
                
                // 기본 모델이 없으면 샘플 추가
                if (!AIModels.Any())
                {
                    AIModels.Add(new AIModelConfig
                    {
                        ModelName = "YOLOv8n",
                        ModelVersion = "1.0.0",
                        ModelType = "YOLO",
                        ModelPath = "Models/yolov8n.onnx",
                        DefaultConfidence = 0.7,
                        IsActive = true,
                        UploadedTime = DateTime.Now
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AI 모델 로드 실패: {ex.Message}");
            }
        }
        
        private async Task LoadZonesAsync()
        {
            try
            {
                using var context = new AppDbContext();
                var zones = context.Zone3DConfigs
                    .Where(z => z.IsEnabled)
                    .ToList();
                
                foreach (var zone in zones)
                {
                    App.Current.Dispatcher.Invoke(() => Zones.Add(zone));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"구역 로드 실패: {ex.Message}");
            }
        }
        
        private async Task LoadRecentEventsAsync()
        {
            try
            {
                var recentEvents = await App.DatabaseService.GetSafetyEventsAsync(
                    startDate: DateTime.Now.AddDays(-1),
                    limit: 100
                );
                
                foreach (var evt in recentEvents)
                {
                    RecentEvents.Add(evt);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"이벤트 로드 실패: {ex.Message}");
            }
        }
        
        // 카메라 설정 저장
        public async Task SaveCameraAsync(Camera camera)
        {
            await App.DatabaseService.SaveCameraConfigAsync(camera);
            
            // 메모리 데이터 업데이트
            var existing = Cameras.FirstOrDefault(c => c.Id == camera.Id);
            if (existing != null)
            {
                var index = Cameras.IndexOf(existing);
                Cameras[index] = camera;
            }
        }
        
        // 구역 상태 업데이트 알림
        public void NotifyZoneUpdated(Zone3D zone)
        {
            ZoneUpdated?.Invoke(this, new ZoneUpdateEventArgs(zone));
        }
        
        // 구역 시각화 업데이트 요청
        public void NotifyZoneVisualizationUpdate()
        {
            ZoneVisualizationUpdateRequested?.Invoke(this, EventArgs.Empty);
        }
        
        // 새 이벤트 추가
        public async Task AddSafetyEventAsync(SafetyEvent safetyEvent)
        {
            await App.DatabaseService.SaveSafetyEventAsync(safetyEvent);
            
            // 최근 이벤트에 추가 (최대 100개 유지)
            RecentEvents.Insert(0, safetyEvent);
            if (RecentEvents.Count > 100)
            {
                RecentEvents.RemoveAt(RecentEvents.Count - 1);
            }
        }
    }
}