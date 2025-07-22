using System;

namespace SafetyVisionMonitor.Models
{
    public class SafetyEvent
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string CameraId { get; set; } = string.Empty;
        public string? PersonTrackingId { get; set; }
        public double Confidence { get; set; }
        public string? ImagePath { get; set; }
        public string? VideoClipPath { get; set; }
        public string? ZoneId { get; set; }
        public string? Description { get; set; }
        public string? BoundingBoxJson { get; set; }
    }
}