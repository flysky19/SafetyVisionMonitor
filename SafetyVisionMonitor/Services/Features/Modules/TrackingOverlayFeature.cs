using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionMonitor.Services.Features
{
    /// <summary>
    /// 추적 오버레이 기능 (경로 표시, ID 표시 등)
    /// </summary>
    public class TrackingOverlayFeature : BaseFeature
    {
        public override string Id => "tracking_overlay";
        public override string Name => "추적 표시";
        public override string Description => "사람 추적 경로와 ID를 표시합니다";
        public override int RenderPriority => 300;

        private bool _showTrackingPath = true;
        private bool _showTrackingId = true;
        private int _pathDisplayLength = 20;
        private readonly Scalar[] _trackingColors = 
        {
            new(255, 0, 0),    // 빨강
            new(0, 255, 0),    // 초록
            new(0, 0, 255),    // 파랑
            new(255, 255, 0),  // 노랑
            new(255, 0, 255),  // 마젠타
            new(0, 255, 255),  // 시안
            new(255, 128, 0),  // 주황
            new(128, 0, 255)   // 보라
        };

        public override FeatureConfiguration DefaultConfiguration => new()
        {
            IsEnabled = true,
            Properties = new Dictionary<string, object>
            {
                ["showTrackingPath"] = true,
                ["showTrackingId"] = true,
                ["pathDisplayLength"] = 20,
                ["pathThickness"] = 2,
                ["idTextScale"] = 0.7,
                ["showCurrentPosition"] = true
            }
        };

        protected override void OnConfigurationChanged(FeatureConfiguration configuration)
        {
            _showTrackingPath = configuration.GetProperty("showTrackingPath", true);
            _showTrackingId = configuration.GetProperty("showTrackingId", true);
            _pathDisplayLength = configuration.GetProperty("pathDisplayLength", 20);

            System.Diagnostics.Debug.WriteLine(
                $"TrackingOverlayFeature: Configuration updated - Path: {_showTrackingPath}, ID: {_showTrackingId}, Length: {_pathDisplayLength}");
        }

        public override Mat ProcessFrame(Mat frame, FrameProcessingContext context)
        {
            if (!IsEnabled || frame == null || frame.Empty())
                return frame;

            try
            {
                var trackedPersons = context.TrackedPersons?.Where(p => p.IsActive).ToList();
                if (trackedPersons == null || !trackedPersons.Any())
                    return frame;

                System.Diagnostics.Debug.WriteLine(
                    $"TrackingOverlayFeature: Rendering {trackedPersons.Count} tracked persons for camera {context.CameraId}");

                foreach (var person in trackedPersons)
                {
                    RenderTrackedPerson(frame, person, context.Scale);
                }

                return frame;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TrackingOverlayFeature: Error processing frame: {ex.Message}");
                return frame;
            }
        }

        public override bool ShouldProcess(FrameProcessingContext context)
        {
            return IsEnabled && context.TrackedPersons?.Count > 0;
        }

        private void RenderTrackedPerson(Mat frame, TrackedPerson person, float scale)
        {
            try
            {
                var trackingColor = GetTrackingColor(person.TrackingId);

                // 추적 경로 그리기
                if (_showTrackingPath && person.TrackingHistory != null && person.TrackingHistory.Count > 1)
                {
                    RenderTrackingPath(frame, person, trackingColor, scale);
                }

                // 현재 위치 표시
                if (CurrentConfiguration?.GetProperty("showCurrentPosition", true) == true)
                {
                    RenderCurrentPosition(frame, person, trackingColor, scale);
                }

                // 트래킹 ID 표시
                if (_showTrackingId)
                {
                    RenderTrackingId(frame, person, trackingColor, scale);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TrackingOverlayFeature: Render tracked person error: {ex.Message}");
            }
        }

        private void RenderTrackingPath(Mat frame, TrackedPerson person, Scalar color, float scale)
        {
            try
            {
                var pathLength = Math.Min(person.TrackingHistory!.Count, _pathDisplayLength);
                var recentPath = person.TrackingHistory.TakeLast(pathLength).ToList();

                if (recentPath.Count < 2) return;

                var thickness = CurrentConfiguration?.GetProperty("pathThickness", 2) ?? 2;

                // 경로 선 그리기
                for (int i = 0; i < recentPath.Count - 1; i++)
                {
                    var startPoint = new Point(
                        (int)(recentPath[i].X * scale), 
                        (int)(recentPath[i].Y * scale)
                    );
                    var endPoint = new Point(
                        (int)(recentPath[i + 1].X * scale), 
                        (int)(recentPath[i + 1].Y * scale)
                    );

                    // 선의 두께는 최신 경로일수록 두껍게
                    var lineThickness = Math.Max(1, thickness - (recentPath.Count - i - 1) / 3);
                    
                    Cv2.Line(frame, startPoint, endPoint, color, lineThickness);
                }

                // 경로 점들 표시 (선택사항)
                if (CurrentConfiguration?.GetProperty("showPathPoints", false) == true)
                {
                    foreach (var point in recentPath)
                    {
                        var scaledPoint = new Point((int)(point.X * scale), (int)(point.Y * scale));
                        Cv2.Circle(frame, scaledPoint, 2, color, -1);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TrackingOverlayFeature: Render path error: {ex.Message}");
            }
        }

        private void RenderCurrentPosition(Mat frame, TrackedPerson person, Scalar color, float scale)
        {
            try
            {
                // 현재 위치에 원 그리기
                var centerX = (int)((person.BoundingBox.X + person.BoundingBox.Width / 2) * scale);
                var centerY = (int)((person.BoundingBox.Y + person.BoundingBox.Height / 2) * scale);
                var centerPoint = new Point(centerX, centerY);

                var circleRadius = CurrentConfiguration?.GetProperty("currentPositionRadius", 5) ?? 5;
                Cv2.Circle(frame, centerPoint, circleRadius, color, -1);

                // 외곽선 추가
                Cv2.Circle(frame, centerPoint, circleRadius + 1, new Scalar(255, 255, 255), 1);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TrackingOverlayFeature: Render current position error: {ex.Message}");
            }
        }

        private void RenderTrackingId(Mat frame, TrackedPerson person, Scalar color, float scale)
        {
            try
            {
                var idText = $"#{person.TrackingId}";
                var textScale = CurrentConfiguration?.GetProperty("idTextScale", 0.7) ?? 0.7;

                // ID 텍스트 위치 (바운딩 박스 위쪽)
                var textX = (int)((person.BoundingBox.X + 10) * scale);
                var textY = (int)((person.BoundingBox.Y - 10) * scale);
                var textPos = new Point(textX, Math.Max(15, textY));

                // 배경 그리기 (가독성 향상)
                if (CurrentConfiguration?.GetProperty("showIdBackground", true) == true)
                {
                    var textSize = Cv2.GetTextSize(idText, HersheyFonts.HersheySimplex, textScale, 2, out _);
                    var bgRect = new Rect(textPos.X - 2, textPos.Y - textSize.Height - 2, 
                                         textSize.Width + 4, textSize.Height + 4);
                    
                    Cv2.Rectangle(frame, bgRect, color, -1);
                }

                // 텍스트 그리기
                Cv2.PutText(frame, idText, textPos, HersheyFonts.HersheySimplex, 
                           textScale, new Scalar(255, 255, 255), 2);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TrackingOverlayFeature: Render tracking ID error: {ex.Message}");
            }
        }

        private Scalar GetTrackingColor(int trackingId)
        {
            // 트래킹 ID에 따른 고유 색상 반환
            var colorIndex = trackingId % _trackingColors.Length;
            return _trackingColors[colorIndex];
        }

        public override FeatureStatus GetStatus()
        {
            var status = base.GetStatus();
            status.Metrics["showTrackingPath"] = _showTrackingPath;
            status.Metrics["showTrackingId"] = _showTrackingId;
            status.Metrics["pathDisplayLength"] = _pathDisplayLength;
            status.Metrics["availableColors"] = _trackingColors.Length;
            return status;
        }
    }
}