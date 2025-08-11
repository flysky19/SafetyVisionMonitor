using System;
using System.Threading.Tasks;
using OpenCvSharp;
using SafetyVisionMonitor.Services;

namespace SafetyVisionMonitor.Services.Handlers
{
    /// <summary>
    /// 미디어 캡처 처리기 (이미지/동영상 저장)
    /// </summary>
    public class MediaCaptureHandler : BaseSafetyEventHandler, IDisposable
    {
        public override string Name => "Media Capture Handler";
        public override int Priority => 50; // 높은 우선순위로 빠른 캡처
        
        private readonly MediaCaptureService _mediaCaptureService;
        private bool _disposed = false;

        public MediaCaptureHandler()
        {
            _mediaCaptureService = new MediaCaptureService();
        }

        public override async Task HandleAsync(SafetyEventContext context)
        {
            try
            {
                var violation = context.Violation;
                var cameraId = violation.Detection.CameraId;

                // 현재 프레임 가져오기 (CameraService에서)
                var currentFrame = await GetCurrentFrameAsync(cameraId);
                if (currentFrame == null || currentFrame.Empty())
                {
                    System.Diagnostics.Debug.WriteLine("MediaCaptureHandler: No current frame available for capture");
                    return;
                }

                // 이미지 캡처와 동영상 녹화를 동시에 처리
                var captureTask = CaptureImageAsync(cameraId, currentFrame, violation);
                Task<string?> videoTask = null;

                // 위험구역인 경우 동영상 녹화도 시작
                if (violation.ViolationType == ViolationType.DangerZoneEntry)
                {
                    videoTask = StartVideoRecordingAsync(cameraId, violation);
                }

                // 모든 미디어 캡처 작업 완료 대기
                var imagePath = await captureTask;
                string? videoPath = null;
                
                if (videoTask != null)
                {
                    videoPath = await videoTask;
                }

                // 파일 경로 업데이트 (완전히 완료된 후)
                if (!string.IsNullOrEmpty(imagePath))
                {
                    context.SetProperty("CapturedImagePath", imagePath);
                    context.SafetyEvent.ImagePath = imagePath;
                    System.Diagnostics.Debug.WriteLine($"MediaCaptureHandler: Image captured and path set: {imagePath}");
                }

                if (!string.IsNullOrEmpty(videoPath))
                {
                    context.SetProperty("RecordedVideoPath", videoPath);
                    context.SafetyEvent.VideoClipPath = videoPath;
                    System.Diagnostics.Debug.WriteLine($"MediaCaptureHandler: Video recording started and path set: {videoPath}");
                }

                // 저장소 용량 관리 (비동기로 실행)
                _ = Task.Run(_mediaCaptureService.ManageStorageAsync);

                System.Diagnostics.Debug.WriteLine($"MediaCaptureHandler: Media capture completed for {cameraId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MediaCaptureHandler: Error - {ex.Message}");
                context.SetProperty("MediaCaptureError", ex.Message);
            }
        }

        public override bool CanHandle(SafetyEventContext context)
        {
            // 모든 구역 위반에 대해 이미지 캡처, 위험구역은 동영상도 녹화
            return context.Violation.ViolationType == ViolationType.DangerZoneEntry ||
                   context.Violation.ViolationType == ViolationType.WarningZoneEntry;
        }

        /// <summary>
        /// 카메라에서 현재 프레임 가져오기
        /// </summary>
        private async Task<Mat?> GetCurrentFrameAsync(string cameraId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // CameraService의 최신 프레임 캐시에서 가져오기
                    if (App.CameraService != null)
                    {
                        var latestFrame = App.CameraService.GetLatestFrame(cameraId);
                        if (latestFrame != null && !latestFrame.Empty())
                        {
                            System.Diagnostics.Debug.WriteLine($"MediaCaptureHandler: Retrieved latest frame for {cameraId} - Size: {latestFrame.Width}x{latestFrame.Height}");
                            return latestFrame;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"MediaCaptureHandler: No latest frame available for camera {cameraId}");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("MediaCaptureHandler: CameraService not available");
                    }
                    
                    return null;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"MediaCaptureHandler: Frame retrieval error - {ex.Message}");
                    return null;
                }
            });
        }

        /// <summary>
        /// 이미지 캡처
        /// </summary>
        private async Task<string?> CaptureImageAsync(string cameraId, Mat frame, ZoneViolation violation)
        {
            try
            {
                return await _mediaCaptureService.CaptureViolationImageAsync(cameraId, frame, violation);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MediaCaptureHandler: Image capture error - {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 동영상 녹화 시작
        /// </summary>
        private async Task<string?> StartVideoRecordingAsync(string cameraId, ZoneViolation violation)
        {
            try
            {
                return await _mediaCaptureService.RecordViolationVideoAsync(cameraId, violation);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MediaCaptureHandler: Video recording error - {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 특정 카메라의 동영상 녹화 중지
        /// </summary>
        public void StopVideoRecording(string cameraId)
        {
            try
            {
                // MediaCaptureService에서 특정 카메라의 녹화 중지
                // 향후 MediaCaptureService에 StopRecording(cameraId) 메소드 추가 가능
                System.Diagnostics.Debug.WriteLine($"MediaCaptureHandler: Stop video recording requested for {cameraId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MediaCaptureHandler: Stop recording error - {ex.Message}");
            }
        }

        /// <summary>
        /// 저장된 미디어 파일 정보 조회
        /// </summary>
        public async Task<MediaFileInfo[]> GetStoredMediaFilesAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // 저장소에서 미디어 파일 목록 조회
                    // 향후 구현
                    return Array.Empty<MediaFileInfo>();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"MediaCaptureHandler: File listing error - {ex.Message}");
                    return Array.Empty<MediaFileInfo>();
                }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;

            _mediaCaptureService?.Dispose();
            _disposed = true;
            
            System.Diagnostics.Debug.WriteLine("MediaCaptureHandler: Disposed");
        }
    }

    /// <summary>
    /// 미디어 파일 정보
    /// </summary>
    public class MediaFileInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public MediaFileType FileType { get; set; }
        public long FileSizeBytes { get; set; }
        public DateTime CreatedTime { get; set; }
        public string CameraId { get; set; } = string.Empty;
        public string ZoneId { get; set; } = string.Empty;
        public ViolationType ViolationType { get; set; }
    }

    /// <summary>
    /// 미디어 파일 타입
    /// </summary>
    public enum MediaFileType
    {
        Image,
        Video
    }
}