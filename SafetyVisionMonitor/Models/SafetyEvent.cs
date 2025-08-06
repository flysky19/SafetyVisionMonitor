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
        public string? PersonBoundingBox { get; set; }
        public string? Severity { get; set; }
        public bool IsAcknowledged { get; set; }
        
        // 확장된 메타데이터 필드들
        /// <summary>
        /// 처리 시간 (밀리초)
        /// </summary>
        public double ProcessingTimeMs { get; set; }
        
        /// <summary>
        /// 이벤트 처리 상태 (JSON)
        /// </summary>
        public string? ProcessingStatus { get; set; }
        
        /// <summary>
        /// 이미지 파일 크기 (바이트)
        /// </summary>
        public long? ImageFileSize { get; set; }
        
        /// <summary>
        /// 동영상 파일 크기 (바이트)
        /// </summary>
        public long? VideoFileSize { get; set; }
        
        /// <summary>
        /// 동영상 길이 (초)
        /// </summary>
        public double? VideoDurationSeconds { get; set; }
        
        /// <summary>
        /// 알림 발송 상태 (JSON)
        /// </summary>
        public string? NotificationStatus { get; set; }
        
        /// <summary>
        /// 추가 메타데이터 (JSON)
        /// </summary>
        public string? Metadata { get; set; }
        
        /// <summary>
        /// 이벤트 해결 시간
        /// </summary>
        public DateTime? ResolvedTime { get; set; }
        
        /// <summary>
        /// 해결한 사용자
        /// </summary>
        public string? ResolvedBy { get; set; }
        
        /// <summary>
        /// 해결 메모
        /// </summary>
        public string? ResolutionNotes { get; set; }
    }
}