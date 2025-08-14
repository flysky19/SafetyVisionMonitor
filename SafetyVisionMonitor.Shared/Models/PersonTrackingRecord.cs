using System;
using System.ComponentModel.DataAnnotations;

namespace SafetyVisionMonitor.Shared.Models
{
    /// <summary>
    /// 사람 추적 기록 모델
    /// </summary>
    public class PersonTrackingRecord
    {
        [Key]
        public int Id { get; set; }
        
        /// <summary>
        /// 추적 ID (카메라별 로컬 ID)
        /// </summary>
        public int TrackingId { get; set; }
        
        /// <summary>
        /// 글로벌 추적 ID (다중 카메라 간 동일 인물)
        /// </summary>
        public int? GlobalTrackingId { get; set; }
        
        /// <summary>
        /// 카메라 ID
        /// </summary>
        [Required]
        public string CameraId { get; set; } = string.Empty;
        
        /// <summary>
        /// 바운딩 박스 X 좌표
        /// </summary>
        public float BoundingBoxX { get; set; }
        
        /// <summary>
        /// 바운딩 박스 Y 좌표
        /// </summary>
        public float BoundingBoxY { get; set; }
        
        /// <summary>
        /// 바운딩 박스 너비
        /// </summary>
        public float BoundingBoxWidth { get; set; }
        
        /// <summary>
        /// 바운딩 박스 높이
        /// </summary>
        public float BoundingBoxHeight { get; set; }
        
        /// <summary>
        /// 중심점 X 좌표
        /// </summary>
        public float CenterX { get; set; }
        
        /// <summary>
        /// 중심점 Y 좌표
        /// </summary>
        public float CenterY { get; set; }
        
        /// <summary>
        /// 검출 신뢰도
        /// </summary>
        public float Confidence { get; set; }
        
        /// <summary>
        /// 추적 히스토리 (JSON 형태로 저장)
        /// </summary>
        public string TrackingHistoryJson { get; set; } = "[]";
        
        /// <summary>
        /// 위치 정보
        /// </summary>
        public string Location { get; set; } = "Unknown";
        
        /// <summary>
        /// 활성 상태
        /// </summary>
        public bool IsActive { get; set; } = true;
        
        /// <summary>
        /// 최초 검출 시간
        /// </summary>
        public DateTime CreatedTime { get; set; } = DateTime.Now;
        
        /// <summary>
        /// 최초 검출 시간 (FirstDetectedTime 별칭)
        /// </summary>
        public DateTime FirstDetectedTime { get; set; } = DateTime.Now;
        
        /// <summary>
        /// 마지막 목격 시간
        /// </summary>
        public DateTime LastSeenTime { get; set; } = DateTime.Now;
        
        /// <summary>
        /// 마지막 업데이트 시간
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        
        /// <summary>
        /// 추적 지속 시간 (초)
        /// </summary>
        public double TrackingDuration => (LastSeenTime - FirstDetectedTime).TotalSeconds;
    }
}