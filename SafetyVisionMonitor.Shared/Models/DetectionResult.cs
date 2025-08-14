using System;
using System.Drawing;

namespace SafetyVisionMonitor.Shared.Models
{
    /// <summary>
    /// 사람의 아크릴 영역 내 위치
    /// </summary>
    public enum PersonLocation
    {
        Unknown,    // 판단 불가
        Interior,   // 아크릴 내부
        Exterior    // 아크릴 외부
    }

    /// <summary>
    /// 아크릴 영역 추적 모드
    /// </summary>
    public enum TrackingMode
    {
        InteriorOnly,    // 내부만 추적
        ExteriorOnly,    // 외부만 추적  
        Both,           // 둘 다 추적 (구분해서 표시)
        InteriorAlert   // 내부에 있을 때만 알림
    }

    /// <summary>
    /// YOLO 객체 검출 결과
    /// </summary>
    public class DetectionResult
    {
        /// <summary>
        /// 검출된 객체의 바운딩 박스 (픽셀 좌표)
        /// </summary>
        public RectangleF BoundingBox { get; set; }
        
        /// <summary>
        /// 검출 신뢰도 (0.0 ~ 1.0)
        /// </summary>
        public float Confidence { get; set; }
        
        /// <summary>
        /// 클래스 ID (0: person, 1: bicycle, 2: car, ...)
        /// </summary>
        public int ClassId { get; set; }
        
        /// <summary>
        /// 클래스 이름
        /// </summary>
        public string ClassName { get; set; } = string.Empty;
        
        /// <summary>
        /// 표준화된 레이블 (효율적인 객체 타입 비교용)
        /// </summary>
        public string Label { get; set; } = string.Empty;
        
        /// <summary>
        /// 화면 표시용 간결한 이름
        /// </summary>
        public string DisplayName => GetDisplayName();
        
        /// <summary>
        /// 검출된 카메라 ID
        /// </summary>
        public string CameraId { get; set; } = string.Empty;
        
        /// <summary>
        /// 검출 시간
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
        
        /// <summary>
        /// 객체 추적 ID (연속 프레임에서 동일 객체 식별용)
        /// </summary>
        public int? TrackingId { get; set; }
        
        /// <summary>
        /// 사람의 아크릴 영역 내 위치 (사람 객체에만 적용)
        /// </summary>
        public PersonLocation Location { get; set; } = PersonLocation.Unknown;
        
        /// <summary>
        /// 바운딩 박스의 중심점
        /// </summary>
        public PointF Center => new PointF(
            BoundingBox.X + BoundingBox.Width / 2,
            BoundingBox.Y + BoundingBox.Height / 2
        );
        
        /// <summary>
        /// 바운딩 박스 면적
        /// </summary>
        public float Area => BoundingBox.Width * BoundingBox.Height;
        
        /// <summary>
        /// 화면 표시용 간결한 이름 생성
        /// </summary>
        private string GetDisplayName()
        {
            // Label이 있으면 Label 사용, 없으면 ClassName에서 추출
            if (!string.IsNullOrEmpty(Label))
            {
                return Label switch
                {
                    "person" => "사람",
                    "car" => "자동차",
                    "truck" => "트럭",
                    "bicycle" => "자전거",
                    "motorcycle" => "오토바이",
                    "bus" => "버스",
                    _ => Label.Substring(0, Math.Min(Label.Length, 10)) // 최대 10글자
                };
            }
            
            // ClassName에서 LabelModel 형태 파싱
            if (!string.IsNullOrEmpty(ClassName) && ClassName.Contains("Name="))
            {
                var nameStart = ClassName.IndexOf("Name=") + 5;
                var nameEnd = ClassName.IndexOf("}", nameStart);
                
                if (nameStart > 5 && nameEnd > nameStart)
                {
                    var name = ClassName.Substring(nameStart, nameEnd - nameStart).Trim();
                    return name switch
                    {
                        "person" => "사람",
                        "car" => "자동차",
                        "truck" => "트럭",
                        "bicycle" => "자전거",
                        "motorcycle" => "오토바이",
                        "bus" => "버스",
                        _ => name.Substring(0, Math.Min(name.Length, 10))
                    };
                }
            }
            
            // 기본값
            return !string.IsNullOrEmpty(ClassName) 
                ? ClassName.Substring(0, Math.Min(ClassName.Length, 10)) 
                : "객체";
        }

        public override string ToString()
        {
            return $"{DisplayName} ({Confidence:F2}) at ({BoundingBox.X:F0}, {BoundingBox.Y:F0})";
        }
    }
    
    /// <summary>
    /// 모델 메타데이터
    /// </summary>
    public class ModelMetadata
    {
        /// <summary>
        /// 입력 이미지 크기
        /// </summary>
        public Size InputSize { get; set; } = new Size(640, 640);
        
        /// <summary>
        /// 클래스 개수
        /// </summary>
        public int ClassCount { get; set; } = 80;
        
        /// <summary>
        /// 클래스 이름 목록
        /// </summary>
        public string[] ClassNames { get; set; } = Array.Empty<string>();
        
        /// <summary>
        /// 앵커 박스 개수
        /// </summary>
        public int AnchorCount { get; set; } = 8400;
        
        /// <summary>
        /// 모델 입력 형식 (RGB/BGR)
        /// </summary>
        public bool IsRgbInput { get; set; } = true;
        
        /// <summary>
        /// 정규화 평균값
        /// </summary>
        public float[] Mean { get; set; } = { 0.485f, 0.456f, 0.406f };
        
        /// <summary>
        /// 정규화 표준편차
        /// </summary>
        public float[] Std { get; set; } = { 0.229f, 0.224f, 0.225f };
    }
    
    /// <summary>
    /// AI 처리 성능 지표
    /// </summary>
    public class ModelPerformance
    {
        /// <summary>
        /// 전처리 시간 (ms)
        /// </summary>
        public double PreprocessTime { get; set; }
        
        /// <summary>
        /// 추론 시간 (ms)
        /// </summary>
        public double InferenceTime { get; set; }
        
        /// <summary>
        /// 후처리 시간 (ms)
        /// </summary>
        public double PostprocessTime { get; set; }
        
        /// <summary>
        /// 전체 처리 시간 (ms)
        /// </summary>
        public double TotalTime => PreprocessTime + InferenceTime + PostprocessTime;
        
        /// <summary>
        /// 처리된 프레임 수
        /// </summary>
        public int ProcessedFrames { get; set; }
        
        /// <summary>
        /// 검출된 객체 수
        /// </summary>
        public int DetectedObjects { get; set; }
        
        /// <summary>
        /// GPU 메모리 사용량 (MB)
        /// </summary>
        public long GpuMemoryUsage { get; set; }
        
        /// <summary>
        /// CPU 사용률 (%)
        /// </summary>
        public double CpuUsage { get; set; }
        
        public override string ToString()
        {
            return $"Total: {TotalTime:F1}ms (Inference: {InferenceTime:F1}ms, Objects: {DetectedObjects})";
        }
    }
}