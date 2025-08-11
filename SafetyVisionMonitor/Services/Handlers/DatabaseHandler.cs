using System;
using System.Threading.Tasks;
using SafetyVisionMonitor.Models;
using SafetyVisionMonitor.Services;

namespace SafetyVisionMonitor.Services.Handlers
{
    /// <summary>
    /// 데이터베이스 저장 처리기
    /// </summary>
    public class DatabaseHandler : BaseSafetyEventHandler
    {
        public override string Name => "Database Handler";
        public override int Priority => 500; // MediaCaptureHandler(50) 완료 후 실행되도록 낮은 우선순위

        public override async Task HandleAsync(SafetyEventContext context)
        {
            try
            {
                var safetyEvent = context.SafetyEvent;
                var violation = context.Violation;

                // SafetyEvent 객체 완성
                CompleteEventData(safetyEvent, violation, context);

                // 데이터베이스에 저장
                await App.DatabaseService.SaveSafetyEventAsync(safetyEvent);

                System.Diagnostics.Debug.WriteLine($"DatabaseHandler: Event saved to database - ID: {safetyEvent.Id}");
                context.SetProperty("DatabaseEventId", safetyEvent.Id);
                context.SetProperty("DatabaseSaved", true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DatabaseHandler: Database save error - {ex.Message}");
                context.SetProperty("DatabaseError", ex.Message);
                context.SetProperty("DatabaseSaved", false);
            }
        }

        private void CompleteEventData(SafetyEvent safetyEvent, ZoneViolation violation, SafetyEventContext context)
        {
            // 기본 정보는 이미 SafetyDetectionService에서 설정됨
            // 여기서는 추가 정보나 컨텍스트에서 얻은 정보를 보완

            // 미디어 파일 경로 (MediaCaptureHandler에서 설정됨)
            if (string.IsNullOrEmpty(safetyEvent.ImagePath))
            {
                var capturedImagePath = context.GetProperty<string>("CapturedImagePath");
                if (!string.IsNullOrEmpty(capturedImagePath))
                {
                    safetyEvent.ImagePath = capturedImagePath;
                    System.Diagnostics.Debug.WriteLine($"DatabaseHandler: Updated ImagePath from context: {capturedImagePath}");
                }
            }

            if (string.IsNullOrEmpty(safetyEvent.VideoClipPath))
            {
                var recordedVideoPath = context.GetProperty<string>("RecordedVideoPath");
                if (!string.IsNullOrEmpty(recordedVideoPath))
                {
                    safetyEvent.VideoClipPath = recordedVideoPath;
                    System.Diagnostics.Debug.WriteLine($"DatabaseHandler: Updated VideoClipPath from context: {recordedVideoPath}");
                }
            }

            // 파일 경로 최종 확인 로그
            System.Diagnostics.Debug.WriteLine($"DatabaseHandler: Final paths - Image: '{safetyEvent.ImagePath}', Video: '{safetyEvent.VideoClipPath}'");

            // 추적 ID 정보
            if (violation.Detection.TrackingId.HasValue)
            {
                safetyEvent.PersonTrackingId = violation.Detection.TrackingId.ToString();
            }

            // 바운딩박스 JSON 형태로 저장
            if (string.IsNullOrEmpty(safetyEvent.BoundingBoxJson))
            {
                var bbox = violation.Detection.BoundingBox;
                safetyEvent.BoundingBoxJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    X = bbox.X,
                    Y = bbox.Y,
                    Width = bbox.Width,
                    Height = bbox.Height,
                    CenterX = bbox.X + bbox.Width / 2,
                    CenterY = bbox.Y + bbox.Height / 2,
                    Area = bbox.Width * bbox.Height
                });
            }

            // 추가 메타데이터 저장 (JSON 형태)
            var metadata = new
            {
                ProcessingTime = (DateTime.Now - context.ProcessingStartTime).TotalMilliseconds,
                ZoneType = violation.Zone.Type.ToString(),
                ZoneArea = violation.Zone.FloorPoints?.Count ?? 0,
                DetectionMethod = "YOLO", // 향후 동적으로 설정
                FrameSize = context.GetProperty<string>("FrameSize"),
                AlertLevel = context.GetProperty<string>("AlertLevel"),
                HandlerChain = context.Properties.Keys.ToArray()
            };

            // Description에 메타데이터 추가
            if (!string.IsNullOrEmpty(safetyEvent.Description))
            {
                safetyEvent.Description += $" | Metadata: {System.Text.Json.JsonSerializer.Serialize(metadata)}";
            }
        }

        public override bool CanHandle(SafetyEventContext context)
        {
            // 모든 안전 이벤트는 데이터베이스에 저장
            return true;
        }

        /// <summary>
        /// 관련 통계 업데이트 (향후 구현)
        /// </summary>
        private async Task UpdateStatisticsAsync(SafetyEvent safetyEvent)
        {
            try
            {
                // 일일/월별/연간 통계 업데이트
                // 구역별 위반 횟수 업데이트
                // 카메라별 위반 통계 업데이트
                await Task.CompletedTask; // 향후 구현
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DatabaseHandler: Statistics update error - {ex.Message}");
            }
        }
    }
}