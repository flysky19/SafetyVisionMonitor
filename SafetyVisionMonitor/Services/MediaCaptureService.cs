using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using OpenCvSharp;
using SafetyVisionMonitor.Models;
using SafetyVisionMonitor.Services;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 안전 이벤트 발생 시 이미지/동영상 캡처 및 저장 서비스
    /// </summary>
    public class MediaCaptureService : IDisposable
    {
        private readonly Dictionary<string, VideoRecorder> _videoRecorders = new();
        private readonly object _recordersLock = new object();
        private readonly string _baseStoragePath;
        private readonly int _maxVideoLengthSeconds = 10;
        private readonly int _preEventBufferSeconds = 3;
        private readonly long _maxStorageSizeMB = 10240; // 10GB
        private bool _disposed = false;

        public MediaCaptureService(string? customStoragePath = null)
        {
            _baseStoragePath = customStoragePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SafetyVisionMonitor",
                "SafetyEvents"
            );

            EnsureDirectoryExists(_baseStoragePath);
            System.Diagnostics.Debug.WriteLine($"MediaCaptureService: Storage path = {_baseStoragePath}");
        }

        /// <summary>
        /// 위반 사건 발생 시 이미지 캡처
        /// </summary>
        public async Task<string?> CaptureViolationImageAsync(string cameraId, Mat frame, ZoneViolation violation)
        {
            if (frame == null || frame.Empty())
            {
                System.Diagnostics.Debug.WriteLine("MediaCaptureService: Cannot capture image - frame is null or empty");
                return null;
            }

            try
            {
                var timestamp = violation.Timestamp;
                var fileName = GenerateImageFileName(cameraId, violation.Zone.Id, timestamp);
                var filePath = Path.Combine(GetDailyStoragePath(timestamp), fileName);

                // 위반 정보를 프레임에 오버레이
                var annotatedFrame = await AnnotateViolationFrame(frame.Clone(), violation);

                // 이미지 저장
                await Task.Run(() =>
                {
                    Cv2.ImWrite(filePath, annotatedFrame);
                });

                annotatedFrame.Dispose();

                var fileInfo = new FileInfo(filePath);
                System.Diagnostics.Debug.WriteLine($"MediaCaptureService: Image captured - {filePath} ({fileInfo.Length} bytes)");

                return filePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MediaCaptureService: Failed to capture image - {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 위반 사건 발생 시 동영상 녹화 시작
        /// </summary>
        public async Task<string?> RecordViolationVideoAsync(string cameraId, ZoneViolation violation)
        {
            try
            {
                var timestamp = violation.Timestamp;
                var fileName = GenerateVideoFileName(cameraId, violation.Zone.Id, timestamp);
                var filePath = Path.Combine(GetDailyStoragePath(timestamp), fileName);

                lock (_recordersLock)
                {
                    // 기존 녹화가 있다면 중지
                    if (_videoRecorders.ContainsKey(cameraId))
                    {
                        _videoRecorders[cameraId].Dispose();
                        _videoRecorders.Remove(cameraId);
                    }

                    // 새 녹화 시작
                    var recorder = new VideoRecorder(filePath, _maxVideoLengthSeconds);
                    _videoRecorders[cameraId] = recorder;

                    System.Diagnostics.Debug.WriteLine($"MediaCaptureService: Video recording started - {filePath}");
                }

                // 카메라 프레임 이벤트에 구독하여 프레임 저장
                SubscribeToCameraFrames(cameraId, violation);

                return filePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MediaCaptureService: Failed to start video recording - {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 카메라 프레임에 구독하여 동영상 녹화
        /// </summary>
        private void SubscribeToCameraFrames(string cameraId, ZoneViolation violation)
        {
            // CameraService의 AI용 원본 프레임 이벤트에 구독
            if (App.CameraService != null)
            {
                void OnFrameReceived(object? sender, CameraFrameEventArgs e)
                {
                    if (e.CameraId != cameraId) return;

                    lock (_recordersLock)
                    {
                        if (_videoRecorders.TryGetValue(cameraId, out var recorder))
                        {
                            // 위반 정보를 프레임에 오버레이하여 저장
                            var annotatedFrame = AnnotateViolationFrame(e.Frame, violation).Result;
                            recorder.AddFrame(annotatedFrame);
                            annotatedFrame.Dispose();

                            // 녹화 완료 체크
                            if (recorder.IsCompleted)
                            {
                                recorder.Dispose();
                                _videoRecorders.Remove(cameraId);
                                App.CameraService.FrameReceivedForAI -= OnFrameReceived;
                                System.Diagnostics.Debug.WriteLine($"MediaCaptureService: Video recording completed for camera {cameraId}");
                            }
                        }
                        else
                        {
                            // 녹화기가 없으면 구독 해제
                            App.CameraService.FrameReceivedForAI -= OnFrameReceived;
                        }
                    }
                }

                App.CameraService.FrameReceivedForAI += OnFrameReceived;
            }
        }

        /// <summary>
        /// 프레임에 위반 정보 오버레이
        /// </summary>
        private async Task<Mat> AnnotateViolationFrame(Mat frame, ZoneViolation violation)
        {
            return await Task.Run(() =>
            {
                var annotated = frame.Clone();

                try
                {
                    // 위반자 바운딩박스 그리기
                    var bbox = violation.Detection.BoundingBox;
                    var rect = new Rect((int)bbox.X, (int)bbox.Y, (int)bbox.Width, (int)bbox.Height);
                    var color = violation.ViolationType == ViolationType.DangerZoneEntry 
                        ? new Scalar(0, 0, 255) // 빨강 (위험)
                        : new Scalar(0, 165, 255); // 주황 (경고)

                    Cv2.Rectangle(annotated, rect, color, 3);

                    // 정보 텍스트 배경
                    var infoLines = new[]
                    {
                        $"VIOLATION: {violation.ViolationType}",
                        $"Zone: {violation.Zone.Name}",
                        $"Confidence: {violation.Confidence:P1}",
                        $"Time: {violation.Timestamp:yyyy-MM-dd HH:mm:ss}",
                        $"Camera: {violation.Detection.CameraId}"
                    };

                    // 정보 패널 그리기
                    var panelHeight = infoLines.Length * 25 + 20;
                    var panelRect = new Rect(10, 10, 400, panelHeight);
                    Cv2.Rectangle(annotated, panelRect, new Scalar(0, 0, 0, 180), -1); // 반투명 검은 배경

                    // 텍스트 그리기
                    for (int i = 0; i < infoLines.Length; i++)
                    {
                        var textPos = new Point(20, 35 + i * 25);
                        Cv2.PutText(annotated, infoLines[i], textPos, 
                                   HersheyFonts.HersheySimplex, 0.7, 
                                   new Scalar(255, 255, 255), 2);
                    }

                    // 위반 심각도에 따른 테두리
                    var borderColor = violation.ViolationType == ViolationType.DangerZoneEntry 
                        ? new Scalar(0, 0, 255) : new Scalar(0, 165, 255);
                    Cv2.Rectangle(annotated, new Rect(0, 0, annotated.Width, annotated.Height), borderColor, 5);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"MediaCaptureService: Annotation error - {ex.Message}");
                }

                return annotated;
            });
        }

        /// <summary>
        /// 저장소 용량 관리 (오래된 파일 삭제)
        /// </summary>
        public async Task ManageStorageAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    var totalSize = GetDirectorySizeInMB(_baseStoragePath);
                    if (totalSize <= _maxStorageSizeMB) return;

                    System.Diagnostics.Debug.WriteLine($"MediaCaptureService: Storage cleanup needed - Current: {totalSize}MB, Max: {_maxStorageSizeMB}MB");

                    // 날짜별 폴더를 오래된 순으로 정렬
                    var directories = Directory.GetDirectories(_baseStoragePath)
                        .Select(d => new DirectoryInfo(d))
                        .OrderBy(d => d.CreationTime)
                        .ToArray();

                    foreach (var dir in directories)
                    {
                        if (GetDirectorySizeInMB(_baseStoragePath) <= _maxStorageSizeMB * 0.8) // 80%까지 정리
                            break;

                        System.Diagnostics.Debug.WriteLine($"MediaCaptureService: Deleting old directory - {dir.Name}");
                        Directory.Delete(dir.FullName, true);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MediaCaptureService: Storage management error - {ex.Message}");
            }
        }

        /// <summary>
        /// 이미지 파일명 생성
        /// </summary>
        private string GenerateImageFileName(string cameraId, string zoneId, DateTime timestamp)
        {
            return $"violation_img_{cameraId}_{zoneId}_{timestamp:yyyyMMdd_HHmmss_fff}.jpg";
        }

        /// <summary>
        /// 동영상 파일명 생성
        /// </summary>
        private string GenerateVideoFileName(string cameraId, string zoneId, DateTime timestamp)
        {
            return $"violation_vid_{cameraId}_{zoneId}_{timestamp:yyyyMMdd_HHmmss_fff}.mp4";
        }

        /// <summary>
        /// 날짜별 저장 경로 생성
        /// </summary>
        private string GetDailyStoragePath(DateTime date)
        {
            var dailyPath = Path.Combine(_baseStoragePath, date.ToString("yyyy-MM-dd"));
            EnsureDirectoryExists(dailyPath);
            return dailyPath;
        }

        /// <summary>
        /// 디렉토리 존재 확인 및 생성
        /// </summary>
        private void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        /// <summary>
        /// 디렉토리 크기 계산 (MB)
        /// </summary>
        private long GetDirectorySizeInMB(string path)
        {
            if (!Directory.Exists(path)) return 0;

            var size = Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                              .Sum(file => new FileInfo(file).Length);
            return size / (1024 * 1024);
        }

        public void Dispose()
        {
            if (_disposed) return;

            lock (_recordersLock)
            {
                foreach (var recorder in _videoRecorders.Values)
                {
                    recorder?.Dispose();
                }
                _videoRecorders.Clear();
            }

            _disposed = true;
            System.Diagnostics.Debug.WriteLine("MediaCaptureService: Disposed");
        }
    }

    /// <summary>
    /// 동영상 녹화기
    /// </summary>
    internal class VideoRecorder : IDisposable
    {
        private readonly VideoWriter _writer;
        private readonly string _filePath;
        private readonly DateTime _startTime;
        private readonly int _maxLengthSeconds;
        private int _frameCount = 0;
        private bool _disposed = false;

        public bool IsCompleted { get; private set; }

        public VideoRecorder(string filePath, int maxLengthSeconds)
        {
            _filePath = filePath;
            _maxLengthSeconds = maxLengthSeconds;
            _startTime = DateTime.Now;

            // H.264 코덱으로 MP4 파일 생성
            var fourcc = VideoWriter.FourCC('H', '2', '6', '4');
            _writer = new VideoWriter(_filePath, fourcc, 15.0, new Size(640, 480)); // 15 FPS

            if (!_writer.IsOpened())
            {
                throw new InvalidOperationException($"Failed to create video writer for {filePath}");
            }
        }

        public void AddFrame(Mat frame)
        {
            if (_disposed || IsCompleted) return;

            try
            {
                // 프레임 크기 조정 (필요시)
                using var resized = new Mat();
                Cv2.Resize(frame, resized, new Size(640, 480));
                
                _writer.Write(resized);
                _frameCount++;

                // 최대 길이 체크
                var elapsed = DateTime.Now - _startTime;
                if (elapsed.TotalSeconds >= _maxLengthSeconds)
                {
                    IsCompleted = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"VideoRecorder: Frame write error - {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _writer?.Dispose();
            _disposed = true;
            IsCompleted = true;

            System.Diagnostics.Debug.WriteLine($"VideoRecorder: Completed - {_filePath} ({_frameCount} frames)");
        }
    }
}