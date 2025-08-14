using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SafetyVisionMonitor.Shared.Models
{
    public partial class AIModel : ObservableObject
    {
        [ObservableProperty]
        private string id = Guid.NewGuid().ToString();
        
        [ObservableProperty]
        private string name = string.Empty;
        
        [ObservableProperty]
        private string version = string.Empty;
        
        [ObservableProperty]
        private string modelPath = string.Empty;
        
        [ObservableProperty]
        private ModelType type = ModelType.YOLOv8;
        
        [ObservableProperty]
        private double confidence = 0.7;
        
        [ObservableProperty]
        private bool isActive = false;
        
        [ObservableProperty]
        private DateTime uploadedDate = DateTime.Now;
        
        [ObservableProperty]
        private long fileSize = 0;
        
        [ObservableProperty]
        private ModelStatus status = ModelStatus.Ready;
        
        [ObservableProperty]
        private string description = string.Empty;
        public string FileSizeText => FormatFileSize(FileSize);
        
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
    
    public enum ModelType
    {
        YOLOv12,
        YOLOv11,
        YOLOv8,
        YOLOv7,
        YOLOv5,
        Detectron2,
        Custom
    }
    
    public enum ModelStatus
    {
        Ready,
        Loading,
        Running,
        Error
    }
}