using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenCvSharp;
using SafetyVisionMonitor.AI;
using SafetyVisionMonitor.Models;
using SafetyVisionMonitor.ViewModels.Base;
using YoloDotNet;
using YoloDotNet.Models;

namespace SafetyVisionMonitor.ViewModels
{
    public partial class AIModelViewModel : BaseViewModel
    {
        [ObservableProperty]
        private ObservableCollection<AIModel> models;
        
        [ObservableProperty]
        private AIModel? selectedModel;
        
        [ObservableProperty]
        private bool isModelRunning = false;
        
        [ObservableProperty]
        private string inferenceTime = "0 ms";
        
        [ObservableProperty]
        private int processedFrames = 0;
        
        [ObservableProperty]
        private double currentConfidence = 0.7;
        
        public AIModelViewModel()
        {
            Title = "AI 모델 관리";
            Models = new ObservableCollection<AIModel>();
        }
        
        public override async void OnLoaded()
        {
            base.OnLoaded();
            
            // 데이터베이스에서 AI 모델 정보 로드
            await LoadSavedModels();
        }
        
        
        private async Task LoadSavedModels()
        {
            try
            {
                // 데이터베이스에서 AI 모델 로드
                var modelConfigs = await App.DatabaseService.LoadAIModelConfigsAsync();
                
                if (modelConfigs != null && modelConfigs.Count > 0)
                {
                    // 데이터베이스에서 로드한 모델 추가
                    foreach (var config in modelConfigs)
                    {
                        // 파일 크기 확인
                        long fileSize = 0;
                        if (!string.IsNullOrEmpty(config.ModelPath) && File.Exists(config.ModelPath))
                        {
                            try
                            {
                                var fileInfo = new FileInfo(config.ModelPath);
                                fileSize = fileInfo.Length;
                            }
                            catch { }
                        }
                        
                        var model = new AIModel
                        {
                            Id = config.Id.ToString(),
                            Name = config.ModelName,
                            Version = config.ModelVersion ?? "1.0.0",
                            Type = Enum.TryParse<ModelType>(config.ModelType, out var modelType) ? modelType : ModelType.YOLOv8,
                            ModelPath = config.ModelPath,
                            FileSize = fileSize > 0 ? fileSize : config.FileSize,
                            Confidence = config.DefaultConfidence,
                            IsActive = config.IsActive,
                            Status = File.Exists(config.ModelPath) ? ModelStatus.Ready : ModelStatus.Loading,
                            UploadedDate = config.UploadedTime,
                            Description = config.Description ?? (File.Exists(config.ModelPath) ? "사용자 정의 모델" : "모델 파일이 없습니다. 활성화하면 자동으로 다운로드됩니다.")
                        };
                        
                        Models.Add(model);
                    }
                }
                // 데이터베이스에 모델이 없으면 안내 메시지만 표시
                
                SelectedModel = Models.FirstOrDefault(m => m.IsActive);
                
                System.Diagnostics.Debug.WriteLine($"LoadSavedModels: Loaded {Models.Count} models from database");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadSavedModels Error: {ex.Message}");

                // 오류 발생 시 안내 메시지
                StatusMessage = "AI 모델을 불러오는 중 오류가 발생했습니다. 모델을 추가해 주세요.";
            }
        }
        
        
        [RelayCommand]
        private async Task AddModel()
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "AI 모델 파일 선택",
                Filter = "ONNX 모델 (*.onnx)|*.onnx|모든 파일 (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            
            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    var fileInfo = new FileInfo(openFileDialog.FileName);
                    var modelName = Path.GetFileNameWithoutExtension(fileInfo.Name);
                    
                    // 모델 폴더로 복사
                    var modelsPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "SafetyVisionMonitor",
                        "Models"
                    );
                    Directory.CreateDirectory(modelsPath);
                    
                    var targetPath = Path.Combine(modelsPath, fileInfo.Name);
                    File.Copy(openFileDialog.FileName, targetPath, true);
                    
                    var newModel = new AIModel
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = modelName,
                        Version = "1.0.0",
                        Type = ModelType.YOLOv8,
                        ModelPath = targetPath,
                        FileSize = fileInfo.Length,
                        Confidence = 0.7,
                        UploadedDate = DateTime.Now,
                        Status = ModelStatus.Ready
                    };
                    
                    Models.Add(newModel);
                    SelectedModel = newModel;
                    
                    // 데이터베이스에 저장
                    var modelConfig = new Database.AIModelConfig
                    {
                        ModelName = newModel.Name,
                        ModelVersion = newModel.Version,
                        ModelType = newModel.Type.ToString(),
                        ModelPath = newModel.ModelPath,
                        DefaultConfidence = newModel.Confidence,
                        IsActive = newModel.IsActive,
                        FileSize = newModel.FileSize,
                        UploadedTime = newModel.UploadedDate,
                        Description = newModel.Description ?? "사용자 추가 모델",
                        ConfigJson = "{}"
                    };
                    
                    await App.DatabaseService.SaveAIModelConfigsAsync(new List<Database.AIModelConfig> { modelConfig });
                    
                    StatusMessage = $"모델 '{modelName}'이(가) 추가되었습니다.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"모델 추가 중 오류 발생: {ex.Message}", "오류", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        [RelayCommand]
        private async Task DeleteModel(AIModel model)
        {
            if (model.IsActive)
            {
                MessageBox.Show("활성화된 모델은 삭제할 수 없습니다.", "알림", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var result = MessageBox.Show($"'{model.Name}' 모델을 삭제하시겠습니까?", "확인",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                // 데이터베이스에서 삭제
                try
                {
                    var allConfigs = await App.DatabaseService.LoadAIModelConfigsAsync();
                    var configToDelete = allConfigs.FirstOrDefault(c => c.Id.ToString() == model.Id);
                    if (configToDelete != null)
                    {
                        allConfigs.Remove(configToDelete);
                        await App.DatabaseService.SaveAIModelConfigsAsync(allConfigs);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to delete model from database: {ex.Message}");
                }
                
                // 파일 삭제
                if (File.Exists(model.ModelPath))
                {
                    try
                    {
                        File.Delete(model.ModelPath);
                    }
                    catch { }
                }
                
                Models.Remove(model);
                StatusMessage = $"모델 '{model.Name}'이(가) 삭제되었습니다.";
            }
        }
        
        [RelayCommand]
        private async Task ActivateModel(AIModel model)
        {
            if (model.IsActive)
                return;
                
            try
            {
                // 기존 활성 모델 비활성화
                foreach (var m in Models)
                {
                    m.IsActive = false;
                    m.Status = ModelStatus.Ready;
                }
                
                // 새 모델 활성화
                model.IsActive = true;
                model.Status = ModelStatus.Loading;
                
                // 실제 AI 서비스를 통한 모델 로드
                var aiModel = new Models.AIModel
                {
                    Id = model.Id,
                    Name = model.Name,
                    ModelPath = model.ModelPath,
                    Type = model.Type,
                    Confidence = (float)model.Confidence
                };
                
                // 다운로드 진행률 이벤트 구독
                void OnDownloadProgress(object? sender, ModelDownloadProgressEventArgs e)
                {
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = $"모델 다운로드 중... {e.ProgressPercentage:F1}% ({e.DownloadedBytes / 1024 / 1024}MB / {e.TotalBytes / 1024 / 1024}MB)";
                        model.Description = $"다운로드 중... {e.ProgressPercentage:F1}%";
                    });
                }
                
                App.AIInferenceService.ModelDownloadProgress += OnDownloadProgress;
                
                try
                {
                    var success = await App.AIInferenceService.LoadModelAsync(aiModel);
                    
                    if (success)
                    {
                        model.Status = ModelStatus.Ready;
                        CurrentConfidence = model. Confidence;
                        
                        // 파일 크기 업데이트
                        if (File.Exists(model.ModelPath))
                        {
                            var fileInfo = new FileInfo(model.ModelPath);
                            model.FileSize = fileInfo.Length;
                        }
                        
                        model.Description = "모델 로드 완료";
                        
                        // DB에 활성 모델 상태 저장
                        await SaveActiveModelToDatabase(model);
                        
                        StatusMessage = $"'{model.Name}' 모델이 활성화되었습니다.";
                    }
                    else
                    {
                        model.IsActive = false;
                        model.Status = ModelStatus.Error;
                        StatusMessage = $"'{model.Name}' 모델 로드에 실패했습니다.";
                        
                        MessageBox.Show($"모델 로드 실패: {model.Name}\n파일 경로를 확인해주세요.", "오류",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                finally
                {
                    App.AIInferenceService.ModelDownloadProgress -= OnDownloadProgress;
                }
            }
            catch (Exception ex)
            {
                model.IsActive = false;
                model.Status = ModelStatus.Error;
                StatusMessage = $"모델 활성화 중 오류 발생: {ex.Message}";
                
                MessageBox.Show($"모델 활성화 중 오류 발생:\n{ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        [RelayCommand]
        private async Task TestModel()
        {
            if (SelectedModel == null)
            {
                MessageBox.Show("테스트할 모델을 선택해주세요.", "알림",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            if (!SelectedModel.IsActive)
            {
                MessageBox.Show("모델을 먼저 활성화해주세요.", "알림",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var openFileDialog = new OpenFileDialog
            {
                Title = "테스트 이미지 선택",
                Filter = "이미지 파일 (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                Multiselect = true
            };
            
            if (openFileDialog.ShowDialog() == true)
            {
                IsModelRunning = true;
                SelectedModel.Status = ModelStatus.Running;
                
                var totalDetections = 0;
                var totalTime = 0.0;
                
                try
                {
                    StatusMessage = "이미지 테스트 중...";
                    
                    foreach (var filePath in openFileDialog.FileNames)
                    {
                        try
                        {
                            // OpenCV를 사용하여 이미지 로드
                            using var image = OpenCvSharp.Cv2.ImRead(filePath);
                            if (image.Empty())
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to load image: {filePath}");
                                continue;
                            }
                            
                            var startTime = DateTime.Now;
                            
                            // AI 서비스를 통한 실제 추론
                            var detections = await App.AIInferenceService.InferFrameAsync("test_camera", image);
                            
                            var processingTime = (DateTime.Now - startTime).TotalMilliseconds;
                            totalTime += processingTime;
                            totalDetections += detections.Length;
                            
                            ProcessedFrames++;
                            InferenceTime = $"{processingTime:F1} ms";
                            
                            // 검출 결과를 디버그 로그로 출력
                            if (detections.Length > 0)
                            {
                                System.Diagnostics.Debug.WriteLine(
                                    $"Test image {Path.GetFileName(filePath)}: {detections.Length} objects detected " +
                                    $"in {processingTime:F1}ms");
                                
                                foreach (var detection in detections)
                                {
                                    System.Diagnostics.Debug.WriteLine(
                                        $"  - {detection.ClassName}: {detection.Confidence:P1} " +
                                        $"at ({detection.BoundingBox.X}, {detection.BoundingBox.Y})");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error processing image {filePath}: {ex.Message}");
                        }
                        
                        // UI 업데이트를 위한 짧은 지연
                        await Task.Delay(50);
                    }
                    
                    var avgTime = openFileDialog.FileNames.Length > 0 ? totalTime / openFileDialog.FileNames.Length : 0;
                    
                    var resultMessage = $"테스트 완료!\n" +
                                      $"처리된 이미지: {openFileDialog.FileNames.Length}개\n" +
                                      $"총 검출 객체: {totalDetections}개\n" +
                                      $"평균 처리 시간: {avgTime:F1}ms";
                    
                    MessageBox.Show(resultMessage, "테스트 결과", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    StatusMessage = $"테스트 완료 - {totalDetections}개 객체 검출";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"테스트 중 오류 발생:\n{ex.Message}", "오류",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusMessage = $"테스트 실패: {ex.Message}";
                }
                finally
                {
                    IsModelRunning = false;
                    SelectedModel.Status = ModelStatus.Ready;
                }
            }
        }
        
        partial void OnCurrentConfidenceChanged(double value)
        {
            if (SelectedModel != null)
            {
                SelectedModel.Confidence = value;
                
                // 실시간으로 AI 서비스의 신뢰도 임계값 업데이트
                if (SelectedModel.IsActive)
                {
                    try
                    {
                        App.AIInferenceService.UpdateConfidenceThreshold((float)value);
                        StatusMessage = $"신뢰도 임계값이 {value:P1}로 변경되었습니다.";
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to update confidence threshold: {ex.Message}");
                    }
                }
            }
        }
        
        /// <summary>
        /// 활성 모델 정보를 데이터베이스에 저장
        /// </summary>
        private async Task SaveActiveModelToDatabase(AIModel model)
        {
            try
            {
                // AppData의 AI 모델 설정 업데이트
                foreach (var config in App.AppData.AIModels)
                {
                    config.IsActive = config.Id.ToString() == model.Id;
                    if (config.IsActive)
                    {
                        config.DefaultConfidence = model.Confidence;
                    }
                }
                
                // 데이터베이스에 저장
                await App.DatabaseService.SaveAIModelConfigsAsync(App.AppData.AIModels.ToList());
                
                System.Diagnostics.Debug.WriteLine($"Active model saved to database: {model.Name}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save active model to database: {ex.Message}");
            }
        }
    }
}