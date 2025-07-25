using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SafetyVisionMonitor.Models;
using SafetyVisionMonitor.ViewModels.Base;

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
            
            // 샘플 모델 추가 (실제로는 DB에서 로드)
            LoadSavedModels();
        }
        
        public override async void OnLoaded()
        {
            base.OnLoaded();
            
            // AppData에서 AI 모델 정보 로드
            Models.Clear();
            foreach (var config in App.AppData.AIModels)
            {
                Models.Add(new AIModel
                {
                    Id = config.Id.ToString(),
                    Name = config.ModelName,
                    Version = config.ModelVersion,
                    ModelPath = config.ModelPath,
                    Type = Enum.Parse<ModelType>(config.ModelType),
                    Confidence = config.DefaultConfidence,
                    IsActive = config.IsActive,
                    UploadedDate = config.UploadedTime
                });
            }
    
            SelectedModel = Models.FirstOrDefault(m => m.IsActive);
            
        }
        
        
        private void LoadSavedModels()
        {
            // 임시 샘플 데이터
            Models.Add(new AIModel
            {
                Name = "YOLOv8n - 안전모 검출",
                Version = "1.0.0",
                Type = ModelType.YOLOv8,
                ModelPath = "Models/yolov8n_helmet.onnx",
                FileSize = 6291456, // 6MB
                Confidence = 0.7,
                IsActive = true,
                Status = ModelStatus.Ready
            });
            
            Models.Add(new AIModel
            {
                Name = "YOLOv8s - 사람 검출",
                Version = "1.0.0",
                Type = ModelType.YOLOv8,
                ModelPath = "Models/yolov8s_person.onnx",
                FileSize = 11534336, // 11MB
                Confidence = 0.6,
                IsActive = false,
                Status = ModelStatus.Ready
            });
            
            SelectedModel = Models.FirstOrDefault(m => m.IsActive);
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
        private void DeleteModel(AIModel model)
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
                
            // 기존 활성 모델 비활성화
            foreach (var m in Models)
            {
                m.IsActive = false;
                m.Status = ModelStatus.Ready;
            }
            
            // 새 모델 활성화
            model.IsActive = true;
            model.Status = ModelStatus.Loading;
            
            // TODO: 실제 모델 로드 로직
            await Task.Delay(1000); // 시뮬레이션
            
            model.Status = ModelStatus.Ready;
            CurrentConfidence = model.Confidence;
            
            StatusMessage = $"'{model.Name}' 모델이 활성화되었습니다.";
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
                
                try
                {
                    // TODO: 실제 추론 로직
                    foreach (var file in openFileDialog.FileNames)
                    {
                        await Task.Delay(100); // 시뮬레이션
                        ProcessedFrames++;
                        InferenceTime = $"{Random.Shared.Next(10, 50)} ms";
                    }
                    
                    MessageBox.Show($"테스트 완료!\n처리된 이미지: {openFileDialog.FileNames.Length}개",
                        "테스트 결과", MessageBoxButton.OK, MessageBoxImage.Information);
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
            }
        }
    }
}