using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionMonitor.Services.Features
{
    /// <summary>
    /// 객체 검출 오버레이 기능
    /// </summary>
    public class ObjectDetectionOverlayFeature : BaseFeature
    {
        public override string Id => "object_detection";
        public override string Name => "객체 검출 표시";
        public override string Description => "검출된 객체에 바운딩 박스와 정보를 표시합니다";
        public override int RenderPriority => 200; // 개인정보 보호 다음

        private bool _showBoundingBox = true;
        private bool _showConfidence = true;
        private bool _showObjectName = true;
        private double _confidenceThreshold = 0.5;
        private readonly Dictionary<string, Scalar> _classColors = new();
        // private readonly KoreanTextRenderer _koreanTextRenderer = new(); // 한글 렌더러 제거

        public override FeatureConfiguration DefaultConfiguration => new()
        {
            IsEnabled = true,
            Properties = new Dictionary<string, object>
            {
                ["showBoundingBox"] = true,
                ["showConfidence"] = true,
                ["showObjectName"] = true,
                ["confidenceThreshold"] = 0.5,
                ["boxThickness"] = 2,
                ["textScale"] = 0.6
            }
        };

        protected override void OnConfigurationChanged(FeatureConfiguration configuration)
        {
            _showBoundingBox = configuration.GetProperty("showBoundingBox", true);
            _showConfidence = configuration.GetProperty("showConfidence", true);
            _showObjectName = configuration.GetProperty("showObjectName", true);
            _confidenceThreshold = configuration.GetProperty("confidenceThreshold", 0.5);

            InitializeClassColors();

            System.Diagnostics.Debug.WriteLine(
                $"ObjectDetectionOverlayFeature: Configuration updated - Box: {_showBoundingBox}, Confidence: {_showConfidence}, Threshold: {_confidenceThreshold}");
        }

        public override Mat ProcessFrame(Mat frame, FrameProcessingContext context)
        {
            if (!IsEnabled || frame == null || frame.Empty())
                return frame;

            try
            {
                // 신뢰도 임계값을 만족하는 검출 결과만 필터링
                var validDetections = context.Detections
                    .Where(d => d.Confidence >= _confidenceThreshold)
                    .ToArray();

                if (validDetections.Length == 0)
                    return frame;

                System.Diagnostics.Debug.WriteLine(
                    $"ObjectDetectionOverlayFeature: Rendering {validDetections.Length} detections for camera {context.CameraId}");

                foreach (var detection in validDetections)
                {
                    RenderDetection(frame, detection, context.Scale);
                }

                return frame;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ObjectDetectionOverlayFeature: Error processing frame: {ex.Message}");
                return frame;
            }
        }

        public override bool ShouldProcess(FrameProcessingContext context)
        {
            return IsEnabled && context.Detections.Length > 0;
        }

        private void RenderDetection(Mat frame, DetectionResult detection, float scale)
        {
            try
            {
                // 스케일링 적용
                var bbox = detection.BoundingBox;
                int x = (int)(bbox.X * scale);
                int y = (int)(bbox.Y * scale);
                int width = (int)(bbox.Width * scale);
                int height = (int)(bbox.Height * scale);

                var rect = new Rect(x, y, width, height);
                
                // 검출 결과 나이에 따른 투명도 계산 (페이드아웃 효과)
                var age = DateTime.Now - detection.Timestamp;
                double alpha = 1.0; // 기본 불투명도
                
                if (age.TotalMilliseconds > 400) // 400ms 후부터 페이드아웃 시작
                {
                    var fadeTime = age.TotalMilliseconds - 400;
                    alpha = Math.Max(0.3, 1.0 - (fadeTime / 400.0)); // 400ms에 걸쳐 서서히 투명해짐
                }
                
                var baseColor = GetClassColor(detection.Label);
                var color = new Scalar(baseColor.Val0 * alpha, baseColor.Val1 * alpha, baseColor.Val2 * alpha);

                // 바운딩 박스 그리기 (사람은 얇은 선)
                if (_showBoundingBox)
                {
                    var defaultThickness = detection.Label == "person" ? 1 : 2;
                    var thickness = CurrentConfiguration?.GetProperty("boxThickness", defaultThickness) ?? defaultThickness;
                    Cv2.Rectangle(frame, rect, color, thickness);
                }

                // 라벨 텍스트 생성
                var labelText = BuildLabelText(detection);
                if (!string.IsNullOrEmpty(labelText))
                {
                    RenderLabel(frame, rect, labelText, color);
                }

                // 중심점 표시 (선택사항)
                if (CurrentConfiguration?.GetProperty("showCenterPoint", false) == true)
                {
                    var center = new Point(x + width / 2, y + height / 2);
                    Cv2.Circle(frame, center, 3, color, -1);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ObjectDetectionOverlayFeature: Render detection error: {ex.Message}");
            }
        }

        private string BuildLabelText(DetectionResult detection)
        {
            var parts = new List<string>();

            if (_showObjectName)
            {
                parts.Add(detection.DisplayName);
            }

            if (_showConfidence)
            {
                parts.Add($"({detection.Confidence:P0})");
            }

            // 트래킹 ID가 있으면 표시
            if (detection.TrackingId.HasValue && 
                CurrentConfiguration?.GetProperty("showTrackingId", true) == true)
            {
                parts.Add($"ID:{detection.TrackingId}");
            }

            return string.Join(" ", parts);
        }

        private void RenderLabel(Mat frame, Rect boundingBox, string text, Scalar color)
        {
            try
            {
                var textScale = CurrentConfiguration?.GetProperty("textScale", 0.6) ?? 0.6;
                var textThickness = 1;
                int baseline = 0;

                // OpenCV 기본 텍스트 크기 계산
                var textSize = Cv2.GetTextSize(text, HersheyFonts.HersheySimplex, textScale, textThickness, out baseline);

                // 텍스트 위치 계산
                var textPos = new Point(boundingBox.X + 4, boundingBox.Y - 4);
                
                // 텍스트가 화면 밖으로 나가지 않도록 조정
                if (textPos.Y - textSize.Height < 0)
                {
                    textPos.Y = boundingBox.Y + boundingBox.Height + textSize.Height + 4;
                }

                // 텍스트 색상 설정
                var textColor = CurrentConfiguration?.GetProperty("useWhiteText", true) == true 
                    ? new Scalar(255, 255, 255) 
                    : new Scalar(0, 0, 0);
                
                // 배경 표시 여부
                bool showBackground = CurrentConfiguration?.GetProperty("showTextBackground", true) ?? true;
                
                if (showBackground)
                {
                    // 텍스트 배경 그리기
                    var bgRect = new Rect(
                        textPos.X - 2, 
                        textPos.Y - textSize.Height - 2,
                        textSize.Width + 4, 
                        textSize.Height + 4
                    );
                    Cv2.Rectangle(frame, bgRect, color, -1);
                }
                
                // OpenCV 기본 텍스트 렌더링
                Cv2.PutText(frame, text, textPos, HersheyFonts.HersheySimplex, textScale, textColor, textThickness, LineTypes.AntiAlias);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ObjectDetectionOverlayFeature: Render label error: {ex.Message}");
            }
        }

        private Scalar GetClassColor(string className)
        {
            if (_classColors.TryGetValue(className, out var color))
                return color;

            // 클래스 이름에 따른 기본 색상
            color = className switch
            {
                "person" => new Scalar(0, 255, 0),    // 초록색
                "car" => new Scalar(255, 0, 0),       // 빨간색
                "truck" => new Scalar(0, 0, 255),     // 파란색
                "bicycle" => new Scalar(255, 255, 0), // 노란색
                "motorcycle" => new Scalar(255, 0, 255), // 마젠타
                _ => new Scalar(128, 128, 128)         // 회색 (기본)
            };

            _classColors[className] = color;
            return color;
        }

        private void InitializeClassColors()
        {
            _classColors.Clear();
            
            // 사용자 정의 색상이 있으면 적용
            var customColors = CurrentConfiguration?.Properties
                .Where(kvp => kvp.Key.StartsWith("color_"))
                .ToList();

            if (customColors != null)
            {
                foreach (var colorConfig in customColors)
                {
                    var className = colorConfig.Key.Substring(6); // "color_" 제거
                    if (colorConfig.Value is string colorHex && !string.IsNullOrEmpty(colorHex))
                    {
                        try
                        {
                            // 16진수 색상 코드를 BGR Scalar로 변환
                            var color = System.Drawing.ColorTranslator.FromHtml(colorHex);
                            _classColors[className] = new Scalar(color.B, color.G, color.R);
                        }
                        catch
                        {
                            // 잘못된 색상 코드는 무시
                        }
                    }
                }
            }
        }

        public override FeatureStatus GetStatus()
        {
            var status = base.GetStatus();
            status.Metrics["showBoundingBox"] = _showBoundingBox;
            status.Metrics["showConfidence"] = _showConfidence;
            status.Metrics["confidenceThreshold"] = _confidenceThreshold;
            status.Metrics["registeredColors"] = _classColors.Count;
            return status;
        }

        public override void Dispose()
        {
            // _koreanTextRenderer?.Dispose(); // 한글 렌더러 제거됨
            base.Dispose();
        }
    }
}