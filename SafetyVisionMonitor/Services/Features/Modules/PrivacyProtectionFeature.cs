using System;
using System.Collections.Generic;
using OpenCvSharp;
using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionMonitor.Services.Features
{
    /// <summary>
    /// 개인정보 보호 기능 (얼굴/몸 흐림 처리)
    /// </summary>
    public class PrivacyProtectionFeature : BaseFeature
    {
        public override string Id => "privacy_protection";
        public override string Name => "개인정보 보호";
        public override string Description => "사람의 얼굴과 몸을 흐림 처리하여 개인정보를 보호합니다";
        public override int RenderPriority => 10; // 가장 높은 우선순위 (가장 먼저 적용)

        private bool _faceBlurEnabled = true;
        private bool _bodyBlurEnabled = false;
        private int _blurIntensity = 51;

        public override FeatureConfiguration DefaultConfiguration => new()
        {
            IsEnabled = false,
            Properties = new Dictionary<string, object>
            {
                ["faceBlurEnabled"] = true,
                ["bodyBlurEnabled"] = false,
                ["blurIntensity"] = 51,
                ["showVisualIndicators"] = true // 시각적 표시 (빨간색/파란색 박스)
            }
        };

        protected override void OnConfigurationChanged(FeatureConfiguration configuration)
        {
            _faceBlurEnabled = configuration.GetProperty("faceBlurEnabled", true);
            _bodyBlurEnabled = configuration.GetProperty("bodyBlurEnabled", false);
            _blurIntensity = configuration.GetProperty("blurIntensity", 51);

            // 홀수로 보정
            if (_blurIntensity % 2 == 0) _blurIntensity++;
            _blurIntensity = Math.Max(3, Math.Min(101, _blurIntensity));

            System.Diagnostics.Debug.WriteLine(
                $"PrivacyProtectionFeature: Configuration updated - Face: {_faceBlurEnabled}, Body: {_bodyBlurEnabled}, Intensity: {_blurIntensity}");
        }

        public override Mat ProcessFrame(Mat frame, FrameProcessingContext context)
        {
            if (!IsEnabled || frame == null || frame.Empty())
                return frame;

            try
            {
                var personDetections = Array.FindAll(context.Detections, d => d.Label == "person" && d.Confidence > 0.5);
                
                if (personDetections.Length == 0)
                    return frame;

                System.Diagnostics.Debug.WriteLine(
                    $"PrivacyProtectionFeature: Processing {personDetections.Length} person detections for camera {context.CameraId}");

                // 각 사람에게 개인정보 보호 적용
                foreach (var detection in personDetections)
                {
                    if (_faceBlurEnabled)
                    {
                        ApplyFaceBlur(frame, detection, context.Scale);
                    }

                    if (_bodyBlurEnabled)
                    {
                        ApplyBodyBlur(frame, detection, context.Scale);
                    }
                }

                return frame;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PrivacyProtectionFeature: Error processing frame: {ex.Message}");
                return frame;
            }
        }

        public override bool ShouldProcess(FrameProcessingContext context)
        {
            return IsEnabled && (_faceBlurEnabled || _bodyBlurEnabled) && context.Detections.Length > 0;
        }

        private void ApplyFaceBlur(Mat frame, DetectionResult detection, float scale)
        {
            try
            {
                // 스케일링 적용
                var bbox = detection.BoundingBox;
                int faceHeight = Math.Max(1, (int)(bbox.Height * 0.3f * scale)); // 상단 30%
                int faceWidth = Math.Max(1, (int)(bbox.Width * 0.8f * scale));   // 좌우 80%
                int faceX = (int)((bbox.X + bbox.Width * 0.1f) * scale);         // 10% 여백
                int faceY = (int)(bbox.Y * scale);                               // 상단부터

                // 프레임 경계 내로 제한
                faceX = Math.Max(0, Math.Min(faceX, frame.Width - 1));
                faceY = Math.Max(0, Math.Min(faceY, frame.Height - 1));
                faceWidth = Math.Max(1, Math.Min(faceWidth, frame.Width - faceX));
                faceHeight = Math.Max(1, Math.Min(faceHeight, frame.Height - faceY));

                var faceRect = new Rect(faceX, faceY, faceWidth, faceHeight);

                // 얼굴 영역 흐림 처리
                var faceRegion = new Mat(frame, faceRect);
                Cv2.GaussianBlur(faceRegion, faceRegion, new Size(_blurIntensity, _blurIntensity), 0);

                // 시각적 표시 (설정에 따라)
                if (CurrentConfiguration?.GetProperty("showVisualIndicators", true) == true)
                {
                    // 빨간색 테두리
                    Cv2.Rectangle(frame, faceRect, new Scalar(0, 0, 255), 2);
                    
                    // 텍스트 라벨
                    var textPos = new Point(faceX, Math.Max(15, faceY - 5));
                    Cv2.PutText(frame, "FACE BLUR", textPos, HersheyFonts.HersheySimplex, 0.5, 
                               new Scalar(0, 0, 255), 1);
                }

                System.Diagnostics.Debug.WriteLine(
                    $"PrivacyProtectionFeature: Applied face blur at {faceRect} (confidence: {detection.Confidence:F2})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PrivacyProtectionFeature: Face blur error: {ex.Message}");
            }
        }

        private void ApplyBodyBlur(Mat frame, DetectionResult detection, float scale)
        {
            try
            {
                // 스케일링 적용
                var bbox = detection.BoundingBox;
                int bodyX = Math.Max(0, (int)(bbox.X * scale));
                int bodyY = Math.Max(0, (int)(bbox.Y * scale));
                int bodyWidth = Math.Min(frame.Width - bodyX, (int)(bbox.Width * scale));
                int bodyHeight = Math.Min(frame.Height - bodyY, (int)(bbox.Height * scale));

                var bodyRect = new Rect(bodyX, bodyY, bodyWidth, bodyHeight);

                if (bodyRect.Width > 0 && bodyRect.Height > 0)
                {
                    // 몸 전체 영역 흐림 처리
                    var bodyRegion = new Mat(frame, bodyRect);
                    Cv2.GaussianBlur(bodyRegion, bodyRegion, new Size(_blurIntensity - 10, _blurIntensity - 10), 0);

                    // 시각적 표시 (설정에 따라)
                    if (CurrentConfiguration?.GetProperty("showVisualIndicators", true) == true)
                    {
                        // 파란색 테두리
                        Cv2.Rectangle(frame, bodyRect, new Scalar(255, 0, 0), 2);
                        
                        // 텍스트 라벨
                        var textPos = new Point(bodyX, Math.Max(15, bodyY - 5));
                        Cv2.PutText(frame, "BODY BLUR", textPos, HersheyFonts.HersheySimplex, 0.5, 
                                   new Scalar(255, 0, 0), 1);
                    }

                    System.Diagnostics.Debug.WriteLine(
                        $"PrivacyProtectionFeature: Applied body blur at {bodyRect} (confidence: {detection.Confidence:F2})");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PrivacyProtectionFeature: Body blur error: {ex.Message}");
            }
        }

        public override FeatureStatus GetStatus()
        {
            var status = base.GetStatus();
            status.Metrics["faceBlurEnabled"] = _faceBlurEnabled;
            status.Metrics["bodyBlurEnabled"] = _bodyBlurEnabled;
            status.Metrics["blurIntensity"] = _blurIntensity;
            return status;
        }
    }
}