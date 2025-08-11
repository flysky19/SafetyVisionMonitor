using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafetyVisionMonitor.Models;
using SafetyVisionMonitor.Services;
using SafetyVisionMonitor.ViewModels.Base;
using SkiaSharp;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point = System.Windows.Point;

namespace SafetyVisionMonitor.ViewModels
{
    public partial class AcrylicSetupViewModel : BaseViewModel
    {
        private readonly CameraService _cameraService;
        
        [ObservableProperty]
        private ObservableCollection<CameraAcrylicInfo> cameras;
        
        [ObservableProperty]
        private ObservableCollection<string> trackingModes;
        
        [ObservableProperty]
        private CameraAcrylicInfo? selectedCamera;
        
        [ObservableProperty]
        private string globalTrackingMode = "InteriorOnly";
        
        [ObservableProperty]
        private BitmapImage? previewImage;
        
        // 경계선 색상 고정 (노란색)
        public SolidColorBrush BoundaryColor { get; } = new SolidColorBrush(Colors.Yellow);
        
        [ObservableProperty]
        private double boundaryThickness = 2;
        
        [ObservableProperty]
        private double boundaryOpacity = 0.8;
        
        [ObservableProperty]
        private bool showBoundaryOnDashboard = true;
        
        public event EventHandler? BoundaryUpdateRequested;
        
        public AcrylicSetupViewModel()
        {
            Title = "아크릴 경계 설정";
            _cameraService = App.CameraService;
            
            Cameras = new ObservableCollection<CameraAcrylicInfo>();
            TrackingModes = new ObservableCollection<string>
            {
                "InteriorOnly",
                "ExteriorOnly",
                "Both",
                "InteriorAlert"
            };
        }
        
        [RelayCommand]
        private void SelectCamera(CameraAcrylicInfo camera)
        {
            // 이전 선택 해제
            if (SelectedCamera != null)
            {
                SelectedCamera.IsSelected = false;
            }
            
            // 새 카메라 선택
            camera.IsSelected = true;
            SelectedCamera = camera;
            
            StatusMessage = $"{camera.Name} 카메라를 선택했습니다.";
        }
        
        partial void OnSelectedCameraChanged(CameraAcrylicInfo? value)
        {
            if (value != null)
            {
                LoadCameraPreview(value);
                // BoundaryUpdateRequested는 LoadCameraPreview 내부에서 호출됨
            }
        }
        
        public override void OnActivated()
        {
            base.OnActivated();
            System.Diagnostics.Debug.WriteLine("AcrylicSetupViewModel: OnActivated called");
            
            // 페이지 활성화 시 데이터 새로고침
            _ = Task.Run(async () => 
            {
                try
                {
                    await LoadDataAsync();
                    
                    // UI 업데이트는 메인 스레드에서
                    await Task.Delay(200); // UI 렌더링 완료 대기 (시간을 늘림)
                    
                    if (System.Windows.Application.Current?.Dispatcher != null)
                    {
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                        {
                            // 선택된 카메라가 있고 경계선이 있으면 강제로 업데이트
                            if (SelectedCamera?.BoundaryPoints?.Count > 2)
                            {
                                System.Diagnostics.Debug.WriteLine($"AcrylicSetupViewModel: Triggering boundary update for camera {SelectedCamera.Id}");
                                BoundaryUpdateRequested?.Invoke(this, EventArgs.Empty);
                            }
                            else
                            {
                                var boundaryInfo = SelectedCamera?.BoundaryPoints == null ? "null" : $"Count={SelectedCamera.BoundaryPoints.Count}";
                                System.Diagnostics.Debug.WriteLine($"AcrylicSetupViewModel: No boundary to display - SelectedCamera: {SelectedCamera?.Id}, BoundaryPoints: {boundaryInfo}, HasBoundary: {SelectedCamera?.HasBoundary}");
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AcrylicSetupViewModel: OnActivated error: {ex.Message}");
                }
            });
        }
        
        public async Task LoadDataAsync()
        {
            try
            {
                IsLoading = true;
                System.Diagnostics.Debug.WriteLine("AcrylicSetupViewModel: LoadDataAsync started");
                
                // 카메라 목록 로드
                var connectedCameras = _cameraService.GetConnectedCameras();
                System.Diagnostics.Debug.WriteLine($"AcrylicSetupViewModel: Found {connectedCameras.Count} connected cameras");
                Cameras.Clear();
                
                foreach (var camera in connectedCameras)
                {
                    System.Diagnostics.Debug.WriteLine($"AcrylicSetupViewModel: Processing camera {camera.Id}");
                    var cameraInfo = new CameraAcrylicInfo
                    {
                        Id = camera.Id,
                        Name = camera.Name,
                        Location = $"카메라 {camera.Id}",  // Camera 모델에 Location이 없으므로 기본값 사용
                        HasBoundary = false,
                        BoundaryStatus = "미설정",
                        TrackingMode = "InteriorOnly"
                    };
                    
                    // 기존 경계선 설정 확인
                    var boundaryFile = Path.Combine("Config", "Acrylic", $"camera_{camera.Id}_boundary.json");
                    System.Diagnostics.Debug.WriteLine($"AcrylicSetupViewModel: Checking boundary file for camera {camera.Id}: {boundaryFile}");
                    
                    if (File.Exists(boundaryFile))
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"AcrylicSetupViewModel: Loading boundary file for camera {camera.Id}");
                            // JSON 파일 직접 읽어서 설정 확인
                            var jsonString = File.ReadAllText(boundaryFile);
                            System.Diagnostics.Debug.WriteLine($"AcrylicSetupViewModel: JSON content length: {jsonString.Length}");
                            var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonString);
                            
                            if (jsonDoc.RootElement.TryGetProperty("acrylicBoundary", out var boundaryArray) && 
                                boundaryArray.GetArrayLength() > 2)
                            {
                                cameraInfo.HasBoundary = true;
                                cameraInfo.BoundaryStatus = "설정됨";
                                
                                // TrackingMode 읽기 (숫자 또는 문자열 처리)
                                if (jsonDoc.RootElement.TryGetProperty("trackingMode", out var trackingMode))
                                {
                                    if (trackingMode.ValueKind == System.Text.Json.JsonValueKind.Number)
                                    {
                                        // trackingMode가 숫자인 경우 (예: 0, 1, 2, 3)
                                        var modeValue = trackingMode.GetInt32();
                                        cameraInfo.TrackingMode = modeValue switch
                                        {
                                            0 => "InteriorOnly",
                                            1 => "ExteriorOnly", 
                                            2 => "Both",
                                            3 => "InteriorAlert",
                                            _ => "InteriorOnly"
                                        };
                                        System.Diagnostics.Debug.WriteLine($"AcrylicSetupViewModel: TrackingMode from number {modeValue} -> {cameraInfo.TrackingMode}");
                                    }
                                    else if (trackingMode.ValueKind == System.Text.Json.JsonValueKind.String)
                                    {
                                        // trackingMode가 문자열인 경우
                                        cameraInfo.TrackingMode = trackingMode.GetString() ?? "InteriorOnly";
                                        System.Diagnostics.Debug.WriteLine($"AcrylicSetupViewModel: TrackingMode from string: {cameraInfo.TrackingMode}");
                                    }
                                    else
                                    {
                                        cameraInfo.TrackingMode = "InteriorOnly";
                                        System.Diagnostics.Debug.WriteLine($"AcrylicSetupViewModel: TrackingMode unknown type, using default");
                                    }
                                }
                                
                                // 경계선 포인트 읽기
                                var points = new List<Point>();
                                foreach (var pointElement in boundaryArray.EnumerateArray())
                                {
                                    System.Diagnostics.Debug.WriteLine($"AcrylicSetupViewModel: Processing boundary point: {pointElement}");
                                    
                                    if (pointElement.TryGetProperty("x", out var x) && 
                                        pointElement.TryGetProperty("y", out var y))
                                    {
                                        var pointX = x.GetInt32();
                                        var pointY = y.GetInt32();
                                        points.Add(new Point(pointX, pointY));
                                        System.Diagnostics.Debug.WriteLine($"AcrylicSetupViewModel: Added boundary point ({pointX}, {pointY})");
                                    }
                                    else
                                    {
                                        System.Diagnostics.Debug.WriteLine($"AcrylicSetupViewModel: Failed to parse boundary point - missing x or y");
                                    }
                                }
                                cameraInfo.BoundaryPoints = points;
                                
                                System.Diagnostics.Debug.WriteLine($"AcrylicSetupViewModel: Loaded {points.Count} boundary points for camera {camera.Id}");
                                
                                // 프레임 크기 정보 읽기
                                if (jsonDoc.RootElement.TryGetProperty("frameSize", out var frameSize))
                                {
                                    if (frameSize.TryGetProperty("width", out var width) && 
                                        frameSize.TryGetProperty("height", out var height) &&
                                        width.GetInt32() > 0 && height.GetInt32() > 0)
                                    {
                                        cameraInfo.ActualWidth = width.GetInt32();
                                        cameraInfo.ActualHeight = height.GetInt32();
                                    }
                                    else
                                    {
                                        // frameSize가 0x0이면 기본값 사용
                                        cameraInfo.ActualWidth = 1920;
                                        cameraInfo.ActualHeight = 1080;
                                        System.Diagnostics.Debug.WriteLine($"AcrylicSetupViewModel: frameSize is 0x0, using default 1920x1080 for camera {camera.Id}");
                                    }
                                }
                                
                                System.Diagnostics.Debug.WriteLine($"AcrylicSetupViewModel: Loaded boundary for camera {camera.Id} - {points.Count} points, frameSize: {cameraInfo.ActualWidth}x{cameraInfo.ActualHeight}");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"AcrylicSetupViewModel: 경계선 로드 실패 for camera {camera.Id}: {ex.Message}");
                            System.Diagnostics.Debug.WriteLine($"AcrylicSetupViewModel: Exception details: {ex}");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"AcrylicSetupViewModel: Boundary file not found for camera {camera.Id}: {boundaryFile}");
                    }
                    
                    Cameras.Add(cameraInfo);
                }
                
                // 시각화 설정 로드
                LoadVisualizationSettings();
                
                // 첫 번째 카메라를 자동으로 선택
                if (Cameras.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"AcrylicSetupViewModel: Auto-selecting first camera {Cameras[0].Id} with {Cameras[0].BoundaryPoints?.Count ?? 0} boundary points");
                    SelectCameraCommand.Execute(Cameras[0]);
                }
                
                StatusMessage = $"{Cameras.Count}개의 카메라를 불러왔습니다.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"데이터 로드 실패: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        private void LoadCameraPreview(CameraAcrylicInfo camera)
        {
            try
            {
                // 카메라에서 현재 프레임 가져오기
                var frame = _cameraService.GetLatestFrame(camera.Id);
                if (frame != null && !frame.Empty())
                {
                    PreviewImage = ConvertMatToBitmapImage(frame);
                    frame.Dispose(); // 메모리 해제
                    
                    // 미리보기 이미지가 로드된 후 경계선 업데이트
                    BoundaryUpdateRequested?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    CreatePlaceholderImage();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"미리보기 로드 실패: {ex.Message}");
                CreatePlaceholderImage();
            }
        }
        
        private BitmapImage ConvertMatToBitmapImage(OpenCvSharp.Mat mat)
        {
            try
            {
                using var bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(mat);
                using var memory = new MemoryStream();
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                memory.Position = 0;
                
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.DecodePixelWidth = 400; // 미리보기 크기 제한
                bitmapImage.EndInit();
                bitmapImage.Freeze();
                
                return bitmapImage;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Mat 변환 실패: {ex.Message}");
                
                // 변환 실패 시 플레이스홀더 생성
                CreatePlaceholderImage();
                return PreviewImage ?? new BitmapImage();
            }
        }
        
        private void CreatePlaceholderImage()
        {
            var width = 640;
            var height = 480;
            
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;
            
            canvas.Clear(SKColors.DarkGray);
            
            using var paint = new SKPaint
            {
                Color = SKColors.Gray,
                TextSize = 24,
                IsAntialias = true,
                TextAlign = SKTextAlign.Center
            };
            
            canvas.DrawText("카메라 미리보기 불가", width / 2, height / 2, paint);
            
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(data.ToArray());
            
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = stream;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            bitmapImage.Freeze();
            
            PreviewImage = bitmapImage;
        }
        
        [RelayCommand]
        private async Task SetupBoundary(CameraAcrylicInfo camera)
        {
            try
            {
                SelectedCamera = camera;
                StatusMessage = $"{camera.Name} 카메라의 경계선을 설정하는 중...";
                
                // 현재 프레임 가져오기
                var currentFrame = _cameraService.GetLatestFrame(camera.Id);
                if (currentFrame == null || currentFrame.Empty())
                {
                    StatusMessage = "카메라 프레임을 가져올 수 없습니다. 카메라가 연결되어 있는지 확인하세요.";
                    return;
                }
                
                // AcrylicBoundarySelector를 사용하여 경계선 설정
                var selector = new AcrylicBoundarySelector();
                var boundary = await Task.Run(() => selector.SelectBoundaryAsync(currentFrame));
                
                if (boundary != null && boundary.Length > 2)
                {
                    // 경계선 저장
                    var filter = new AcrylicRegionFilter(camera.Id);
                    var frameSize = new OpenCvSharp.Size(currentFrame.Width, currentFrame.Height);
                    
                    if (Enum.TryParse<TrackingMode>(camera.TrackingMode, out var mode))
                    {
                        // 프레임 크기를 먼저 설정해야 올바른 originalFrameSize가 저장됨
                        filter.SetFrameSize(frameSize);
                        filter.SetAcrylicBoundary(boundary.Select(p => new System.Drawing.Point(p.X, p.Y)).ToArray());
                        filter.SetTrackingMode(mode);
                        
                        var configPath = Path.Combine("Config", "Acrylic", $"camera_{camera.Id}_boundary.json");
                        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
                        filter.SaveToFile(configPath);
                        
                        // BackgroundTrackingService의 아크릴 필터 새로고침 (DashboardView 실시간 업데이트용)
                        var trackingService = App.TrackingService;
                        if (trackingService != null)
                        {
                            trackingService.RefreshAcrylicFilter(camera.Id);
                        }
                        
                        // UI 업데이트
                        camera.HasBoundary = true;
                        camera.BoundaryStatus = "설정됨";
                        camera.BoundaryPoints = boundary.Select(p => new Point(p.X, p.Y)).ToList();
                        
                        // 경계선 미리보기 업데이트
                        BoundaryUpdateRequested?.Invoke(this, EventArgs.Empty);
                        
                        StatusMessage = $"{camera.Name} 카메라의 경계선이 설정되었습니다.";
                        
                        currentFrame.Dispose(); // 메모리 해제
                    }
                }
                else
                {
                    StatusMessage = "경계선 설정이 취소되었습니다.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"경계선 설정 중 오류 발생: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"경계선 설정 실패: {ex.Message}";
            }
        }
        
        [RelayCommand]
        private async Task DeleteBoundary(CameraAcrylicInfo camera)
        {
            var result = MessageBox.Show($"{camera.Name} 카메라의 경계선 설정을 삭제하시겠습니까?", 
                "확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var configPath = Path.Combine("Config", "Acrylic", $"camera_{camera.Id}_boundary.json");
                    if (File.Exists(configPath))
                    {
                        File.Delete(configPath);
                    }
                    
                    camera.HasBoundary = false;
                    camera.BoundaryStatus = "미설정";
                    camera.BoundaryPoints = null;
                    
                    if (SelectedCamera == camera)
                    {
                        BoundaryUpdateRequested?.Invoke(this, EventArgs.Empty);
                    }
                    
                    StatusMessage = $"{camera.Name} 카메라의 경계선 설정이 삭제되었습니다.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"삭제 중 오류 발생: {ex.Message}", "오류",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusMessage = $"삭제 실패: {ex.Message}";
                }
            }
        }
        
        [RelayCommand]
        private void ApplyToAllCameras()
        {
            var result = MessageBox.Show($"모든 카메라의 추적 모드를 '{GlobalTrackingMode}'로 변경하시겠습니까?", 
                "확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    int updatedCount = 0;
                    
                    foreach (var camera in Cameras.Where(c => c.HasBoundary))
                    {
                        camera.TrackingMode = GlobalTrackingMode;
                        
                        // 설정 파일 업데이트
                        var configPath = Path.Combine("Config", "Acrylic", $"camera_{camera.Id}_boundary.json");
                        if (File.Exists(configPath))
                        {
                            try
                            {
                                // JSON 파일 직접 읽고 trackingMode만 업데이트
                                var jsonString = File.ReadAllText(configPath);
                                var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonString);
                                
                                using var stream = new MemoryStream();
                                using var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true });
                                
                                writer.WriteStartObject();
                                foreach (var property in jsonDoc.RootElement.EnumerateObject())
                                {
                                    if (property.Name.Equals("trackingMode", StringComparison.OrdinalIgnoreCase))
                                    {
                                        writer.WriteString("trackingMode", GlobalTrackingMode);
                                    }
                                    else
                                    {
                                        property.WriteTo(writer);
                                    }
                                }
                                writer.WriteEndObject();
                                writer.Flush();
                                
                                File.WriteAllBytes(configPath, stream.ToArray());
                                updatedCount++;
                            }
                            catch (Exception jsonEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"JSON 업데이트 실패: {jsonEx.Message}");
                                // JSON 업데이트 실패 시 필터 재생성으로 폴백
                                var filter = new AcrylicRegionFilter(camera.Id);
                                filter.LoadFromFile(configPath);
                                if (Enum.TryParse<TrackingMode>(GlobalTrackingMode, out var mode))
                                {
                                    filter.SetTrackingMode(mode);
                                    filter.SaveToFile(configPath);
                                    updatedCount++;
                                }
                            }
                        }
                    }
                    
                    StatusMessage = $"{updatedCount}개 카메라의 추적 모드가 업데이트되었습니다.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"일괄 적용 중 오류 발생: {ex.Message}", "오류",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusMessage = $"일괄 적용 실패: {ex.Message}";
                }
            }
        }
        
        [RelayCommand]
        private void SaveVisualizationSettings()
        {
            try
            {
                // 시각화 설정 저장 (색상은 고정이므로 제외)
                var settings = new
                {
                    BoundaryThickness = BoundaryThickness,
                    BoundaryOpacity = BoundaryOpacity,
                    ShowBoundaryOnDashboard = ShowBoundaryOnDashboard
                };
                
                var settingsPath = Path.Combine("Config", "Acrylic", "visualization_settings.json");
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                
                var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                File.WriteAllText(settingsPath, json);
                
                MessageBox.Show("시각화 설정이 저장되었습니다.", "저장 완료",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                    
                StatusMessage = "시각화 설정이 저장되었습니다.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"설정 저장 중 오류 발생: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"설정 저장 실패: {ex.Message}";
            }
        }
        
        private void LoadVisualizationSettings()
        {
            try
            {
                var settingsPath = Path.Combine("Config", "Acrylic", "visualization_settings.json");
                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    var settings = System.Text.Json.JsonSerializer.Deserialize<VisualizationSettings>(json);
                    
                    if (settings != null)
                    {
                        // BoundaryColor는 고정이므로 로드하지 않음
                        BoundaryThickness = settings.BoundaryThickness;
                        BoundaryOpacity = settings.BoundaryOpacity;
                        ShowBoundaryOnDashboard = settings.ShowBoundaryOnDashboard;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"시각화 설정 로드 실패: {ex.Message}");
            }
        }
        
        private class VisualizationSettings
        {
            public double BoundaryThickness { get; set; } = 2;
            public double BoundaryOpacity { get; set; } = 0.8;
            public bool ShowBoundaryOnDashboard { get; set; } = true;
        }
    }
    
    // 카메라별 아크릴 정보 모델
    public partial class CameraAcrylicInfo : ObservableObject
    {
        [ObservableProperty]
        private string id = string.Empty;
        
        [ObservableProperty]
        private string name = string.Empty;
        
        [ObservableProperty]
        private string location = string.Empty;
        
        [ObservableProperty]
        private bool hasBoundary;
        
        [ObservableProperty]
        private string boundaryStatus = "미설정";
        
        [ObservableProperty]
        private string trackingMode = "InteriorOnly";
        
        [ObservableProperty]
        private List<Point>? boundaryPoints;
        
        [ObservableProperty]
        private bool isSelected;
        
        [ObservableProperty]
        private int actualWidth = 1920;
        
        [ObservableProperty]
        private int actualHeight = 1080;
    }
}