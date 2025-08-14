using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SafetyVisionMonitor.Shared.Models
{
    public partial class TrackingZone : ObservableObject
    {
        [ObservableProperty]
        private string id = string.Empty;
        
        [ObservableProperty]
        private string name = string.Empty;
        
        [ObservableProperty]
        private string cameraId = string.Empty;
        
        [ObservableProperty]
        private bool isEntryZone;
        
        [ObservableProperty]
        private bool isExitZone;
        
        [ObservableProperty]
        private bool countingEnabled;
        
        [ObservableProperty]
        private List<PointF>? polygonPoints;

        public DateTime CreatedTime { get; set; } = DateTime.Now;
    }
}