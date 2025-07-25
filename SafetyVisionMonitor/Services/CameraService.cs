﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OpenCvSharp;
using SafetyVisionMonitor.Helpers;
using SafetyVisionMonitor.Models;

namespace SafetyVisionMonitor.Services
{
    public class CameraService : IDisposable
    {
        private readonly ConcurrentDictionary<string, CameraConnection> _connections = new();
        private readonly int _maxCameras;
        public event EventHandler<CameraFrameEventArgs>? FrameReceived;
        public event EventHandler<CameraFrameEventArgs>? FrameReceivedForAI; // AI용 원본 프레임
        public event EventHandler<CameraFrameEventArgs>? FrameReceivedForUI; // UI용 저화질 프레임
        public event EventHandler<CameraConnectionEventArgs>? ConnectionChanged;
        
        public CameraService()
        {
            _maxCameras = App.Configuration.GetValue<int>("AppSettings:MaxCameras", 4);
        }
        
        public async Task<bool> ConnectCamera(Camera camera)
        {
            // 미사용 카메라는 연결할 수 없음
            if (!camera.IsEnabled)
            {
                var cameraName = string.IsNullOrEmpty(camera.Name) ? camera.Id : camera.Name;
                throw new InvalidOperationException($"카메라 '{cameraName}'은(는) 미사용 상태입니다. 먼저 사용함으로 변경해주세요.");
            }
            
            if (_connections.Count >= _maxCameras)
            {
                throw new InvalidOperationException($"최대 {_maxCameras}대까지만 연결 가능합니다.");
            }
            
            if (_connections.ContainsKey(camera.Id))
            {
                return false;
            }
            
            var connection = new CameraConnection(camera);
            connection.FrameReceived += OnFrameReceived;
            
            var success = await connection.ConnectAsync();
            
            if (success)
            {
                _connections[camera.Id] = connection;
                camera.IsConnected = true;
                ConnectionChanged?.Invoke(this, new CameraConnectionEventArgs(camera.Id, true));
            }
            
            return success;
        }
        
        public void DisconnectCamera(string cameraId)
        {
            if (_connections.TryRemove(cameraId, out var connection))
            {
                connection.FrameReceived -= OnFrameReceived;
                connection.Dispose();
                ConnectionChanged?.Invoke(this, new CameraConnectionEventArgs(cameraId, false));
            }
        }
        
        public List<Camera> GetConnectedCameras()
        {
            return _connections.Values
                .Select(c => c.Camera)
                .ToList();
        }
        
        /// <summary>
        /// 활성화된 카메라 목록 반환 (AI/트래킹 처리용)
        /// </summary>
        public List<Camera> GetActiveCameras()
        {
            return _connections.Values
                .Where(c => c.Camera.IsEnabled)
                .Select(c => c.Camera)
                .ToList();
        }
        
        public void UpdateCameraSettings(string cameraId, Camera settings)
        {
            if (_connections.TryGetValue(cameraId, out var connection))
            {
                connection.UpdateSettings(settings);
            }
        }
        
        public Camera? GetCameraSettings(string cameraId)
        {
            if (_connections.TryGetValue(cameraId, out var connection))
            {
                return connection.GetCurrentSettings();
            }
            return null;
        }
        
        private void OnFrameReceived(object? sender, CameraFrameEventArgs e)
        {
            //System.Diagnostics.Debug.WriteLine($"CameraService: Frame received from {e.CameraId}");
            
            // 미사용 카메라의 프레임은 무시
            if (sender is CameraConnection connection && !connection.Camera.IsEnabled)
            {
                e.Frame?.Dispose();
                return;
            }
    
            if (e.Frame != null && !e.Frame.Empty())
            {
                // 하이브리드 프레임 배포 시스템
                ProcessHybridFrameDistribution(e);
                
                // 기존 호환성을 위한 레거시 이벤트 (Deprecated 예정)
                var legacyHandlers = FrameReceived?.GetInvocationList();
                if (legacyHandlers != null)
                {
                    foreach (var handler in legacyHandlers)
                    {
                        var frameClone = e.Frame.Clone();
                        try
                        {
                            ((EventHandler<CameraFrameEventArgs>)handler).Invoke(this, 
                                new CameraFrameEventArgs(e.CameraId, frameClone));
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Legacy frame handler error: {ex.Message}");
                            frameClone.Dispose();
                        }
                    }
                }
                
                // 원본 프레임 해제
                e.Frame.Dispose();
            }
        }
        
        private void ProcessHybridFrameDistribution(CameraFrameEventArgs originalFrame)
        {
            try
            {
                // 1. AI용 원본 프레임 배포 (고화질, 원본 FPS)
                DistributeAIFrame(originalFrame);
                
                // 2. UI용 저화질 프레임 배포 (저화질, 제한된 FPS)
                DistributeUIFrame(originalFrame);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Hybrid frame distribution error: {ex.Message}");
            }
        }
        
        private void DistributeAIFrame(CameraFrameEventArgs originalFrame)
        {
            var aiHandlers = FrameReceivedForAI?.GetInvocationList();
            if (aiHandlers != null)
            {
                foreach (var handler in aiHandlers)
                {
                    // AI용은 원본 해상도 유지
                    var aiFrame = originalFrame.Frame.Clone();
                    try
                    {
                        ((EventHandler<CameraFrameEventArgs>)handler).Invoke(this, 
                            new CameraFrameEventArgs(originalFrame.CameraId, aiFrame));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"AI frame handler error: {ex.Message}");
                        aiFrame.Dispose();
                    }
                }
            }
        }
        
        private void DistributeUIFrame(CameraFrameEventArgs originalFrame)
        {
            var uiHandlers = FrameReceivedForUI?.GetInvocationList();
            if (uiHandlers != null)
            {
                // UI용 프레임 스키핑 (3프레임마다 1번)
                if (!ShouldUpdateUI(originalFrame.CameraId))
                    return;
                
                foreach (var handler in uiHandlers)
                {
                    // UI용은 해상도 축소 (1/4 크기)
                    var uiFrame = CreateLowResolutionFrame(originalFrame.Frame);
                    try
                    {
                        ((EventHandler<CameraFrameEventArgs>)handler).Invoke(this, 
                            new CameraFrameEventArgs(originalFrame.CameraId, uiFrame));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"UI frame handler error: {ex.Message}");
                        uiFrame.Dispose();
                    }
                }
            }
        }
        
        private readonly Dictionary<string, int> _uiFrameCounters = new();
        private bool ShouldUpdateUI(string cameraId)
        {
            if (!_uiFrameCounters.ContainsKey(cameraId))
                _uiFrameCounters[cameraId] = 0;
            
            _uiFrameCounters[cameraId]++;
            
            // 3프레임마다 1번 UI 업데이트 (FPS 1/3로 감소)
            return _uiFrameCounters[cameraId] % 3 == 0;
        }
        
        private Mat CreateLowResolutionFrame(Mat originalFrame)
        {
            // 해상도 1/2로 축소 (면적은 1/4로 감소)
            var targetWidth = originalFrame.Width / 2;
            var targetHeight = originalFrame.Height / 2;
            
            var resizedFrame = new Mat();
            Cv2.Resize(originalFrame, resizedFrame, new OpenCvSharp.Size(targetWidth, targetHeight), 
                      interpolation: InterpolationFlags.Linear);
            
            //System.Diagnostics.Debug.WriteLine($"CameraService: UI frame resized: {originalFrame.Width}x{originalFrame.Height} → {targetWidth}x{targetHeight}");
            
            return resizedFrame;
        }
        
        public void Dispose()
        {
            foreach (var connection in _connections.Values)
            {
                connection.FrameReceived -= OnFrameReceived;
                connection.Dispose();
            }
            _connections.Clear();
        }
    }
    
    // 카메라 연결 관리 클래스
    internal class CameraConnection : IDisposable
    {
        private VideoCapture? _capture;
        private Thread? _captureThread;
        private readonly CancellationTokenSource _cancellationToken = new();
        private bool _isRunning;
        private object _captureLock = new();
        private bool _disposed = false;
        public Camera Camera { get; }
        public event EventHandler<CameraFrameEventArgs>? FrameReceived;
        
        public CameraConnection(Camera camera)
        {
            Camera = camera;
        }
        
        private void ConfigureCamera()
        {
            if (_capture == null || !_capture.IsOpened()) return;
    
            try
            {
                // 먼저 카메라의 네이티브 해상도 확인
                var nativeWidth = _capture.Get(VideoCaptureProperties.FrameWidth);
                var nativeHeight = _capture.Get(VideoCaptureProperties.FrameHeight);
                
                System.Diagnostics.Debug.WriteLine($"Native resolution: {nativeWidth}x{nativeHeight}");
                
                // 카메라가 지원하는 해상도로 설정
                if (Camera.Type == CameraType.USB)
                {
                    // 2560x1440을 먼저 시도
                    _capture.Set(VideoCaptureProperties.FrameWidth, Camera.Width);
                    _capture.Set(VideoCaptureProperties.FrameHeight, (Camera.Height));
                    
                    var actualWidth = _capture.Get(VideoCaptureProperties.FrameWidth);
                    var actualHeight = _capture.Get(VideoCaptureProperties.FrameHeight);
                    
                    // 설정이 안되면 1920x1080 시도
                    if (actualWidth != 2560 || actualHeight != 1440)
                    {
                        _capture.Set(VideoCaptureProperties.FrameWidth, 1920);
                        _capture.Set(VideoCaptureProperties.FrameHeight, 1080);
                        
                        actualWidth = _capture.Get(VideoCaptureProperties.FrameWidth);
                        actualHeight = _capture.Get(VideoCaptureProperties.FrameHeight);
                    }
                    
                    // 최종 설정된 해상도로 Camera 객체 업데이트
                    Camera.Width = (int)actualWidth;
                    Camera.Height = (int)actualHeight;
                }
                
                // 1. 포맷 설정 (MJPEG는 대부분의 USB 카메라에서 지원)
                // 1. 다른 픽셀 포맷 시도
                var formats = new[]
                {
                    VideoWriter.FourCC('Y', 'U', 'Y', '2'),  // YUY2
                    VideoWriter.FourCC('M', 'J', 'P', 'G'),  // MJPEG
                    VideoWriter.FourCC('Y', 'V', '1', '2'),  // YV12
                    VideoWriter.FourCC('B', 'G', 'R', '3'),  // BGR3
                };
        
                foreach (var format in formats)
                {
                    _capture.Set(VideoCaptureProperties.FourCC, format);
            
                    // 테스트 프레임 읽기
                    using (var test = new Mat())
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            _capture.Read(test);
                            Thread.Sleep(100);
                    
                            if (!test.Empty())
                            {
                                var mean = Cv2.Mean(test);
                                var brightness = (mean.Val0 + mean.Val1 + mean.Val2) / 3;
                        
                                System.Diagnostics.Debug.WriteLine(
                                    $"Format {FourCCToString(format)}: Brightness={brightness:F2}");
                        
                                if (brightness > 5) // 검은색이 아니면
                                {
                                    System.Diagnostics.Debug.WriteLine($"Working format found: {FourCCToString(format)}");
                                    break;
                                }
                            }
                        }
                    }
                }
                
                    
                // FPS 설정
                _capture.Set(VideoCaptureProperties.Fps, Camera.Fps);
                
                // 자동 노출 설정
                _capture.Set(VideoCaptureProperties.AutoExposure, 0.75); // 0.25 = manual, 0.75 = auto
                
                // 명도 조정 (22.36%가 좀 어두운 편이므로)
                _capture.Set(VideoCaptureProperties.Brightness, 150); // 기본값 보다 높게
                //AutoAdjustBrightness();
                //TestExposureModes();
                
                _capture.Set(VideoCaptureProperties.Contrast, 40);
                _capture.Set(VideoCaptureProperties.Saturation, 60);
                
                // 버퍼 크기
                _capture.Set(VideoCaptureProperties.BufferSize, 1);
                
                var finalWidth = _capture.Get(VideoCaptureProperties.FrameWidth);
                var finalHeight = _capture.Get(VideoCaptureProperties.FrameHeight);
                var finalFps = _capture.Get(VideoCaptureProperties.Fps);
                
                System.Diagnostics.Debug.WriteLine(
                    $"Camera {Camera.Id} final settings: {finalWidth}x{finalHeight} @ {finalFps}fps");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Configure error: {ex.Message}");
            }
        }
        private void TestExposureModes()
        {
            System.Diagnostics.Debug.WriteLine("Testing exposure modes...");
    
            // 자동 노출 끄기
            _capture.Set(VideoCaptureProperties.AutoExposure, 0.25); // Manual mode
            Thread.Sleep(500);
    
            // 다양한 노출 값 테스트
            int[] exposureValues = { -4, -3, -2, -1, 0 };
    
            foreach (var exp in exposureValues)
            {
                _capture.Set(VideoCaptureProperties.Exposure, exp);
                Thread.Sleep(200);
        
                using (var test = new Mat())
                {
                    _capture.Read(test);
                    if (!test.Empty())
                    {
                        var mean = Cv2.Mean(test);
                        var brightness = (mean.Val0 + mean.Val1 + mean.Val2) / 3;
                        System.Diagnostics.Debug.WriteLine($"Exposure {exp}: Brightness={brightness:F2}");
                    }
                }
            }
        }
        private void AutoAdjustBrightness()
        {
            using (var testFrame = new Mat())
            {
                for (int i = 0; i < 5; i++)
                {
                    _capture.Read(testFrame);
                    if (!testFrame.Empty())
                    {
                        var mean = Cv2.Mean(testFrame);
                        var avgBrightness = (mean.Val0 + mean.Val1 + mean.Val2) / 3;
                
                        if (avgBrightness < 50) // 너무 어두움
                        {
                            var currentBrightness = _capture.Get(VideoCaptureProperties.Brightness);
                            _capture.Set(VideoCaptureProperties.Brightness, currentBrightness + 20);
                            _capture.Set(VideoCaptureProperties.Gain, _capture.Get(VideoCaptureProperties.Gain) + 10);
                        }
                        else if (avgBrightness > 200) // 너무 밝음
                        {
                            var currentBrightness = _capture.Get(VideoCaptureProperties.Brightness);
                            _capture.Set(VideoCaptureProperties.Brightness, currentBrightness - 20);
                        }
                
                        System.Diagnostics.Debug.WriteLine($"Auto-adjust: Brightness={avgBrightness:F2}");
                        break;
                    }
                    Thread.Sleep(100);
                }
            }
        }
        
        public async Task<bool> ConnectAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    lock (_captureLock)
                    {
                        if (_disposed) return false;
                        
                        // USB 카메라인 경우
                        if (Camera.Type == CameraType.USB)
                        {
                            int cameraIndex = int.Parse(Camera.ConnectionString);
                            
                            // DirectShow 백엔드 사용 (Windows에서 안정적)
                            _capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.MSMF);
                            
                            if (!_capture.IsOpened())
                            {
                                _capture.Dispose();
                                _capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.ANY);
                            }
                            
                            if (!_capture.IsOpened())
                            {
                                _capture.Dispose();
                                _capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);
                            }
                        }
                        else
                        {
                            _capture = new VideoCapture(Camera.ConnectionString);
                        }
                        
                        if (_capture?.IsOpened() != true)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to open camera {Camera.Id}");
                            _capture?.Dispose();
                            _capture = null;
                            return false;
                        }
                        
                        // 카메라 정보 출력
                        var backend = _capture.GetBackendName();
                        System.Diagnostics.Debug.WriteLine($"Camera backend: {backend}");
                        
                        // 카메라 설정
                        ConfigureCamera();
                        
                        // 워밍업 및 검증
                        if (!VerifyCamera())
                        {
                            System.Diagnostics.Debug.WriteLine("Camera verification failed");
                            _capture.Dispose();
                            _capture = null;
                            return false;
                        }
                        
                        _isRunning = true;
                    }
                    
                    // 스레드 시작
                    _captureThread = new Thread(CaptureLoop) 
                    { 
                        IsBackground = true,
                        Name = $"Camera_{Camera.Id}_Thread"
                    };
                    _captureThread.Start();
                    
                    System.Diagnostics.Debug.WriteLine($"Camera {Camera.Id} connected successfully");
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Camera connection error: {ex.Message}");
                    return false;
                }
            });
        }

        private bool VerifyCamera()
        {
            if (_capture == null || !_capture.IsOpened()) return false;

            try
            {
                System.Diagnostics.Debug.WriteLine("Warming up camera...");
        
                // 카메라 워밍업 (일부 카메라는 초기화 시간이 필요)
                for (int warmup = 0; warmup < 3; warmup++)
                {
                    using (var dummy = new Mat())
                    {
                        _capture.Read(dummy);
                        Thread.Sleep(200); // 각 프레임 간 대기
                
                        if (!dummy.Empty())
                        {
                            var mean = Cv2.Mean(dummy);
                            System.Diagnostics.Debug.WriteLine(
                                $"Warmup {warmup}: RGB=({mean.Val0:F2}, {mean.Val1:F2}, {mean.Val2:F2})");
                        }
                    }
                }
        
                // 실제 검증
                using (var testFrame = new Mat())
                {
                    for (int i = 0; i < 3; i++)
                    {
                        _capture.Read(testFrame);
                        Thread.Sleep(200);
                
                        if (!testFrame.Empty())
                        {
                            var mean = Cv2.Mean(testFrame);
                            var totalMean = (mean.Val0 + mean.Val1 + mean.Val2) / 3;
                    
                            System.Diagnostics.Debug.WriteLine(
                                $"Verify frame {i}: Size={testFrame.Width}x{testFrame.Height}, " +
                                $"Mean={totalMean:F2}");
                    
                            if (totalMean > 5)
                            {
                                return true;
                            }
                        }
                    }
                }
        
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Verify error: {ex.Message}");
                return false;
            }
        }
        
        private string FourCCToString(int fourcc)
        {
            return $"{(char)(fourcc & 255)}{(char)((fourcc >> 8) & 255)}{(char)((fourcc >> 16) & 255)}{(char)((fourcc >> 24) & 255)}";
        }
        
        private void LogFrameInfo(Mat frame)
        {
            System.Diagnostics.Debug.WriteLine($"=== Camera {Camera.Id} Frame Info ===");
            System.Diagnostics.Debug.WriteLine($"Size: {frame.Width}x{frame.Height}");
            System.Diagnostics.Debug.WriteLine($"Channels: {frame.Channels()}");
            System.Diagnostics.Debug.WriteLine($"Type: {frame.Type()}");
            System.Diagnostics.Debug.WriteLine($"Depth: {frame.Depth()}");
    
            // 픽셀 샘플링
            var mean = Cv2.Mean(frame);
            System.Diagnostics.Debug.WriteLine($"Mean RGB: ({mean.Val0:F2}, {mean.Val1:F2}, {mean.Val2:F2})");
    
            // 히스토그램 분석
            using (var hist = new Mat())
            {
                Cv2.CalcHist(new[] { frame }, new[] { 0 }, null, hist, 1, new[] { 256 }, new[] { new[] { 0f, 256f } });
                double minVal, maxVal;
                Cv2.MinMaxLoc(hist, out minVal, out maxVal);
                System.Diagnostics.Debug.WriteLine($"Histogram range: {minVal} - {maxVal}");
            }
        }

        private void LogCameraSettings()
        {
            System.Diagnostics.Debug.WriteLine($"=== Camera {Camera.Id} Settings ===");
            System.Diagnostics.Debug.WriteLine($"Resolution: {_capture.Get(VideoCaptureProperties.FrameWidth)}x{_capture.Get(VideoCaptureProperties.FrameHeight)}");
            System.Diagnostics.Debug.WriteLine($"FPS: {_capture.Get(VideoCaptureProperties.Fps)}");
            System.Diagnostics.Debug.WriteLine($"Brightness: {_capture.Get(VideoCaptureProperties.Brightness)}");
            System.Diagnostics.Debug.WriteLine($"Contrast: {_capture.Get(VideoCaptureProperties.Contrast)}");
            System.Diagnostics.Debug.WriteLine($"Saturation: {_capture.Get(VideoCaptureProperties.Saturation)}");
            System.Diagnostics.Debug.WriteLine($"Exposure: {_capture.Get(VideoCaptureProperties.Exposure)}");
            System.Diagnostics.Debug.WriteLine($"Gain: {_capture.Get(VideoCaptureProperties.Gain)}");
            System.Diagnostics.Debug.WriteLine($"Backend: {_capture.GetBackendName()}");
        }
        
        public void UpdateSettings(Camera settings)
        {
            lock (_captureLock)
            {
                if (_capture == null || !_capture.IsOpened()) return;
                
                try
                {
                    // 밝기
                    //if (Math.Abs(Camera.Brightness - settings.Brightness) > 0.1)
                    {
                        _capture.Set(VideoCaptureProperties.Brightness, settings.Brightness);
                        Camera.Brightness = settings.Brightness;
                    }
                    
                    // 대비
                    //if (Math.Abs(Camera.Contrast - settings.Contrast) > 0.1)
                    {
                        _capture.Set(VideoCaptureProperties.Contrast, settings.Contrast);
                        Camera.Contrast = settings.Contrast;
                    }
                    
                    // 채도
                    //if (Math.Abs(Camera.Saturation - settings.Saturation) > 0.1)
                    {
                        _capture.Set(VideoCaptureProperties.Saturation, settings.Saturation);
                        Camera.Saturation = settings.Saturation;
                    }
                    
                    // 노출
                    //if (Math.Abs(Camera.Exposure - settings.Exposure) > 0.1)
                    {
                        _capture.Set(VideoCaptureProperties.Exposure, settings.Exposure);
                        Camera.Exposure = settings.Exposure;
                    }
                    
                    // 게인
                    //if (Math.Abs(Camera.Gain - settings.Gain) > 0.1)
                    {
                        _capture.Set(VideoCaptureProperties.Gain, settings.Gain);
                        Camera.Gain = settings.Gain;
                    }
                    
                    // 색조
                    //if (Math.Abs(Camera.Hue - settings.Hue) > 0.1)
                    {
                        _capture.Set(VideoCaptureProperties.Hue, settings.Hue);
                        Camera.Hue = settings.Hue;
                    }
                    
                    // 감마
                    //if (Math.Abs(Camera.Gamma - settings.Gamma) > 0.1)
                    // {
                    //     _capture.Set(VideoCaptureProperties.Gamma, settings.Gamma);
                    //     Camera.Gamma = settings.Gamma;
                    // }
                    
                    // 선명도
                    //if (Math.Abs(Camera.Sharpness - settings.Sharpness) > 0.1)
                    {
                        _capture.Set(VideoCaptureProperties.Sharpness, settings.Sharpness);
                        Camera.Sharpness = settings.Sharpness;
                    }
                    
                    // 자동 노출
                    if (settings.AutoExposure == true)
                    {
                        _capture.Set(VideoCaptureProperties.AutoExposure, settings.AutoExposure ? 0.75 : 0.25);
                        Camera.AutoExposure = settings.AutoExposure;
                    }
                    
                    // 자동 화이트 밸런스
                    if (settings.AutoWhiteBalance == true)
                    {
                        _capture.Set(VideoCaptureProperties.AutoWB, settings.AutoWhiteBalance ? 1 : 0);
                        Camera.AutoWhiteBalance = settings.AutoWhiteBalance;
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"Camera {Camera.Id} settings updated");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to update camera settings: {ex.Message}");
                }
            }
        }
        
        public Camera GetCurrentSettings()
        {
            lock (_captureLock)
            {
                if (_capture == null || !_capture.IsOpened()) return Camera;
                
                try
                {
                    // 현재 카메라 설정값 읽기
                    Camera.Brightness = _capture.Get(VideoCaptureProperties.Brightness);
                    Camera.Contrast = _capture.Get(VideoCaptureProperties.Contrast);
                    Camera.Saturation = _capture.Get(VideoCaptureProperties.Saturation);
                    Camera.Exposure = _capture.Get(VideoCaptureProperties.Exposure);
                    Camera.Gain = _capture.Get(VideoCaptureProperties.Gain);
                    Camera.Hue = _capture.Get(VideoCaptureProperties.Hue);
                    Camera.Gamma = _capture.Get(VideoCaptureProperties.Gamma);
                    Camera.Sharpness = _capture.Get(VideoCaptureProperties.Sharpness);
                    
                    var autoExp = _capture.Get(VideoCaptureProperties.AutoExposure);
                    Camera.AutoExposure = autoExp > 0.5;
                    
                    var autoWb = _capture.Get(VideoCaptureProperties.AutoWB);
                    Camera.AutoWhiteBalance = autoWb > 0.5;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to get camera settings: {ex.Message}");
                }
                
                return Camera;
            }
        }
        
        private void CaptureLoop()
        {
            var frame = new Mat();
            var frameCount = 0;
            bool formatLogged = false;
    
            while (_isRunning && !_cancellationToken.Token.IsCancellationRequested)
            {
                try
                {
                    if (_capture != null && _capture.Read(frame) && !frame.Empty())
                    {
                        // 첫 프레임에서 포맷 정보 로깅
                        if (!formatLogged)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"Camera {Camera.Id} format: " +
                                $"Size={frame.Width}x{frame.Height}, " +
                                $"Channels={frame.Channels()}, " +
                                $"Type={frame.Type()}, " +
                                $"Depth={frame.Depth()}");
                            formatLogged = true;
                        }
                
                        // 프레임이 비정상적인 경우 체크
                        if (frame.Width <= 0 || frame.Height <= 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"Invalid frame size for {Camera.Id}");
                            continue;
                        }
                
                        // 프레임을 CameraService로 전달 (CameraService에서 구독자별 복사 관리)
                        FrameReceived?.Invoke(this, new CameraFrameEventArgs(Camera.Id, frame.Clone()));
                        frameCount++;
                
                        // if (frameCount % 30 == 0) // 30프레임마다 로그
                        // {
                        //     System.Diagnostics.Debug.WriteLine($"Camera {Camera.Id}: {frameCount} frames processed");
                        // }
                    }
            
                    Thread.Sleep(1000 / (int)Camera.Fps);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Capture loop error: {ex.Message}");
                }
            }
    
            frame.Dispose();
        }
        
        public void Dispose()
        {
            if (_disposed) return;
    
            _disposed = true;
            _isRunning = false;
    
            // 캡처 스레드 종료 대기
            _cancellationToken.Cancel();
            
            // 스레드가 종료될 때까지 최대 3초 대기
            if (_captureThread?.IsAlive == true)
            {
                if (!_captureThread.Join(3000))
                {
                    // 타임아웃 시 로그만 남기고 계속 진행
                    System.Diagnostics.Debug.WriteLine($"Warning: Camera {Camera.Id} capture thread did not terminate within timeout");
                }
            }
    
            // 카메라 해제
            lock (_captureLock)
            {
                _capture?.Release();
                _capture?.Dispose();
                _capture = null;
            }
    
            _cancellationToken.Dispose();
        }
    }
    
    // 이벤트 인자 클래스들
    public class CameraFrameEventArgs : EventArgs
    {
        public string CameraId { get; }
        public Mat Frame { get; }
        
        public CameraFrameEventArgs(string cameraId, Mat frame)
        {
            CameraId = cameraId;
            Frame = frame;
        }
    }
    
    public class CameraConnectionEventArgs : EventArgs
    {
        public string CameraId { get; }
        public bool IsConnected { get; }
        
        public CameraConnectionEventArgs(string cameraId, bool isConnected)
        {
            CameraId = cameraId;
            IsConnected = isConnected;
        }
    }
}