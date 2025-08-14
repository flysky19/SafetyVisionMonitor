using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionMonitor.Services.Features
{
    /// <summary>
    /// 통계 오버레이 기능 (FPS, 검출 수, 시간 등)
    /// </summary>
    public class StatisticsOverlayFeature : BaseFeature
    {
        public override string Id => "statistics_overlay";
        public override string Name => "통계 표시";
        public override string Description => "프레임 통계 및 성능 정보를 화면에 표시합니다";
        public override int RenderPriority => 1000; // 가장 마지막에 렌더링

        private bool _showFps = true;
        private bool _showDetectionCount = true;
        private bool _showTimestamp = true;
        private bool _showCameraInfo = true;
        private readonly Dictionary<string, DateTime> _lastFrameTime = new();
        private readonly Dictionary<string, double> _currentFps = new();

        public override FeatureConfiguration DefaultConfiguration => new()
        {
            IsEnabled = false, // 기본적으로 비활성화
            Properties = new Dictionary<string, object>
            {
                ["showFps"] = true,
                ["showDetectionCount"] = true,
                ["showTimestamp"] = true,
                ["showCameraInfo"] = true,
                ["position"] = "top_left", // top_left, top_right, bottom_left, bottom_right
                ["textScale"] = 0.6,
                ["textColor"] = "#FFFFFF",
                ["backgroundColor"] = "#000000",
                ["backgroundOpacity"] = 0.7
            }
        };

        protected override void OnConfigurationChanged(FeatureConfiguration configuration)
        {
            _showFps = configuration.GetProperty("showFps", true);
            _showDetectionCount = configuration.GetProperty("showDetectionCount", true);
            _showTimestamp = configuration.GetProperty("showTimestamp", true);
            _showCameraInfo = configuration.GetProperty("showCameraInfo", true);

            System.Diagnostics.Debug.WriteLine(
                $"StatisticsOverlayFeature: Configuration updated - FPS: {_showFps}, Detection: {_showDetectionCount}, Time: {_showTimestamp}");
        }

        public override Mat ProcessFrame(Mat frame, FrameProcessingContext context)
        {
            if (!IsEnabled || frame == null || frame.Empty())
                return frame;

            try
            {
                UpdateFpsCalculation(context.CameraId);
                
                var statsText = BuildStatisticsText(context);
                if (string.IsNullOrEmpty(statsText))
                    return frame;

                RenderStatistics(frame, statsText);
                return frame;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StatisticsOverlayFeature: Error processing frame: {ex.Message}");
                return frame;
            }
        }

        public override bool ShouldProcess(FrameProcessingContext context)
        {
            return IsEnabled;
        }

        private void UpdateFpsCalculation(string cameraId)
        {
            var now = DateTime.Now;
            
            if (_lastFrameTime.TryGetValue(cameraId, out var lastTime))
            {
                var timeDiff = (now - lastTime).TotalSeconds;
                if (timeDiff > 0)
                {
                    var instantFps = 1.0 / timeDiff;
                    
                    // 이동 평균으로 FPS 계산 (더 부드러운 값)
                    if (_currentFps.TryGetValue(cameraId, out var currentFps))
                    {
                        _currentFps[cameraId] = currentFps * 0.9 + instantFps * 0.1;
                    }
                    else
                    {
                        _currentFps[cameraId] = instantFps;
                    }
                }
            }
            
            _lastFrameTime[cameraId] = now;
        }

        private string BuildStatisticsText(FrameProcessingContext context)
        {
            var lines = new List<string>();

            // 카메라 정보
            if (_showCameraInfo)
            {
                lines.Add($"Camera: {context.CameraId}");
            }

            // FPS 정보
            if (_showFps && _currentFps.TryGetValue(context.CameraId, out var fps))
            {
                lines.Add($"FPS: {fps:F1}");
            }

            // 검출 수 정보
            if (_showDetectionCount)
            {
                var totalDetections = context.Detections.Length;
                var personDetections = context.Detections.Count(d => d.Label == "person");
                
                if (totalDetections > 0)
                {
                    lines.Add($"Objects: {totalDetections} ({personDetections} persons)");
                }
                else
                {
                    lines.Add("Objects: 0");
                }
            }

            // 추적 정보
            if (CurrentConfiguration?.GetProperty("showTrackingInfo", false) == true)
            {
                var activeTrackers = context.TrackedPersons?.Count(t => t.IsActive) ?? 0;
                if (activeTrackers > 0)
                {
                    lines.Add($"Tracking: {activeTrackers} active");
                }
            }

            // 시간 정보
            if (_showTimestamp)
            {
                var timeFormat = CurrentConfiguration?.GetProperty("timeFormat", "HH:mm:ss") ?? "HH:mm:ss";
                lines.Add($"Time: {DateTime.Now.ToString(timeFormat)}");
            }

            // 해상도 정보
            if (CurrentConfiguration?.GetProperty("showResolution", false) == true)
            {
                var scaleInfo = context.Scale != 1.0f ? $" (x{context.Scale:F1})" : "";
                lines.Add($"Resolution: Processing{scaleInfo}");
            }

            // 성능 정보
            if (CurrentConfiguration?.GetProperty("showPerformance", false) == true)
            {
                var processingTime = (DateTime.Now - context.ProcessingStartTime).TotalMilliseconds;
                if (processingTime > 0)
                {
                    lines.Add($"Processing: {processingTime:F1}ms");
                }
            }

            return string.Join("\n", lines);
        }

        private void RenderStatistics(Mat frame, string text)
        {
            try
            {
                var textScale = CurrentConfiguration?.GetProperty("textScale", 0.6) ?? 0.6;
                var textColor = ParseTextColor();
                var backgroundColor = ParseBackgroundColor();
                var backgroundOpacity = CurrentConfiguration?.GetProperty("backgroundOpacity", 0.7) ?? 0.7;
                
                var lines = text.Split('\n');
                var lineHeight = 25;
                var padding = 10;
                var textThickness = 1;

                // 텍스트 크기 계산
                var maxWidth = 0;
                foreach (var line in lines)
                {
                    var textSize = Cv2.GetTextSize(line, HersheyFonts.HersheySimplex, textScale, textThickness, out _);
                    maxWidth = Math.Max(maxWidth, textSize.Width);
                }

                var totalHeight = lines.Length * lineHeight + padding * 2;
                var totalWidth = maxWidth + padding * 2;

                // 위치 계산
                var position = GetRenderPosition(frame, totalWidth, totalHeight);

                // 배경 그리기
                if (backgroundOpacity > 0)
                {
                    var bgRect = new Rect(position.X, position.Y, totalWidth, totalHeight);
                    
                    if (backgroundOpacity < 1.0)
                    {
                        // 반투명 배경
                        var overlay = frame.Clone();
                        Cv2.Rectangle(overlay, bgRect, backgroundColor, -1);
                        Cv2.AddWeighted(frame, 1.0 - backgroundOpacity, overlay, backgroundOpacity, 0, frame);
                        overlay.Dispose();
                    }
                    else
                    {
                        // 불투명 배경
                        Cv2.Rectangle(frame, bgRect, backgroundColor, -1);
                    }
                }

                // 텍스트 그리기
                for (int i = 0; i < lines.Length; i++)
                {
                    var linePos = new Point(
                        position.X + padding,
                        position.Y + padding + (i + 1) * lineHeight - 5
                    );
                    
                    Cv2.PutText(frame, lines[i], linePos, HersheyFonts.HersheySimplex, 
                               textScale, textColor, textThickness);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StatisticsOverlayFeature: Render statistics error: {ex.Message}");
            }
        }

        private Point GetRenderPosition(Mat frame, int width, int height)
        {
            var position = CurrentConfiguration?.GetProperty("position", "top_left") ?? "top_left";
            var margin = CurrentConfiguration?.GetProperty("margin", 10) ?? 10;

            return position switch
            {
                "top_right" => new Point(frame.Width - width - margin, margin),
                "bottom_left" => new Point(margin, frame.Height - height - margin),
                "bottom_right" => new Point(frame.Width - width - margin, frame.Height - height - margin),
                "center" => new Point((frame.Width - width) / 2, (frame.Height - height) / 2),
                _ => new Point(margin, margin) // top_left (기본값)
            };
        }

        private Scalar ParseTextColor()
        {
            try
            {
                var colorHex = CurrentConfiguration?.GetProperty("textColor", "#FFFFFF") ?? "#FFFFFF";
                var color = System.Drawing.ColorTranslator.FromHtml(colorHex);
                return new Scalar(color.B, color.G, color.R); // BGR 순서
            }
            catch
            {
                return new Scalar(255, 255, 255); // 기본값: 흰색
            }
        }

        private Scalar ParseBackgroundColor()
        {
            try
            {
                var colorHex = CurrentConfiguration?.GetProperty("backgroundColor", "#000000") ?? "#000000";
                var color = System.Drawing.ColorTranslator.FromHtml(colorHex);
                return new Scalar(color.B, color.G, color.R); // BGR 순서
            }
            catch
            {
                return new Scalar(0, 0, 0); // 기본값: 검정색
            }
        }

        public override FeatureStatus GetStatus()
        {
            var status = base.GetStatus();
            status.Metrics["showFps"] = _showFps;
            status.Metrics["showDetectionCount"] = _showDetectionCount;
            status.Metrics["showTimestamp"] = _showTimestamp;
            status.Metrics["trackedCameras"] = _currentFps.Count;
            
            // 카메라별 FPS 정보
            foreach (var kvp in _currentFps)
            {
                status.Metrics[$"fps_{kvp.Key}"] = Math.Round(kvp.Value, 1);
            }

            return status;
        }

        public override void Dispose()
        {
            _lastFrameTime.Clear();
            _currentFps.Clear();
            base.Dispose();
        }
    }
}