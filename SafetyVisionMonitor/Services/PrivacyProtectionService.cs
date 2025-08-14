using System;
using System.Drawing;
using System.Drawing.Imaging;
using OpenCvSharp;
using SafetyVisionMonitor.Models;
using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 개인정보 보호를 위한 얼굴/몸 흐림 처리 서비스
    /// </summary>
    public class PrivacyProtectionService : IDisposable
    {
        private SafetySettings _settings;
        private CascadeClassifier? _faceClassifier;
        private HOGDescriptor? _bodyDetector;
        private bool _isInitialized = false;

        public PrivacyProtectionService(SafetySettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            InitializeDetectors();
        }

        /// <summary>
        /// OpenCV 검출기 초기화
        /// </summary>
        private void InitializeDetectors()
        {
            try
            {
                // 얼굴 검출기 초기화 - 실제 검출을 위해 올바른 파일 로드 시도
                try
                {
                    // 다양한 Haar Cascade 파일 경로 시도
                    var possiblePaths = new[]
                    {
                        @"C:\opencv\sources\data\haarcascades\haarcascade_frontalface_default.xml",
                        @"haarcascade_frontalface_default.xml",
                        @"opencv_data\haarcascades\haarcascade_frontalface_default.xml",
                        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "haarcascade_frontalface_default.xml")
                    };

                    bool loaded = false;
                    foreach (var path in possiblePaths)
                    {
                        try
                        {
                            if (System.IO.File.Exists(path))
                            {
                                _faceClassifier = new CascadeClassifier(path);
                                if (!_faceClassifier.Empty())
                                {
                                    System.Diagnostics.Debug.WriteLine($"얼굴 검출기 로드 성공: {path}");
                                    loaded = true;
                                    break;
                                }
                            }
                        }
                        catch { continue; }
                    }

                    if (!loaded)
                    {
                        // 파일이 없으면 내장 검출기 시도
                        _faceClassifier = new CascadeClassifier();
                        System.Diagnostics.Debug.WriteLine("얼굴 검출기 생성 (파일 없이 시도)");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"얼굴 검출기 초기화 실패: {ex.Message}");
                    _faceClassifier = null;
                }

                // HOG 검출기
                try
                {
                    _bodyDetector = new HOGDescriptor();
                    System.Diagnostics.Debug.WriteLine("HOG 검출기 생성 완료");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"HOG 검출기 초기화 실패: {ex.Message}");
                    _bodyDetector = null;
                }

                _isInitialized = true;
                System.Diagnostics.Debug.WriteLine("PrivacyProtectionService 초기화 완료");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PrivacyProtectionService 초기화 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 프레임에 개인정보 보호 처리 적용 (검출된 사람 정보 기반)
        /// </summary>
        /// <param name="frame">원본 프레임</param>
        /// <param name="detections">AI가 검출한 사람들 정보</param>
        /// <returns>처리된 프레임</returns>
        public Mat ProcessFrame(Mat frame, DetectionResult[]? detections = null)
        {
            if (frame == null || frame.Empty())
                return frame;

            // 글로벌 설정을 직접 참조
            var globalSettings = SafetySettingsManager.Instance.CurrentSettings;
            System.Diagnostics.Debug.WriteLine($"PrivacyProtectionService: ProcessFrame 시작 - Face: {globalSettings.IsFaceBlurEnabled}, Body: {globalSettings.IsFullBodyBlurEnabled}, Detections: {detections?.Length ?? 0}");

            // 설정에 따라 처리 여부 결정
            if (!globalSettings.IsFaceBlurEnabled && !globalSettings.IsFullBodyBlurEnabled)
            {
                System.Diagnostics.Debug.WriteLine("PrivacyProtectionService: 프라이버시 보호 비활성화 - 원본 반환");
                return frame;
            }

            if (!_isInitialized)
            {
                System.Diagnostics.Debug.WriteLine("PrivacyProtectionService: 초기화 안됨 - 원본 반환");
                return frame;
            }

            try
            {
                var processedFrame = frame.Clone();

                // 검출된 사람이 있는 경우 정확한 영역에 흐림 적용
                if (detections != null && detections.Length > 0)
                {
                    foreach (var detection in detections)
                    {
                        // Label 속성 사용 (효율적인 비교)
                        if (detection.Label == "person" && detection.Confidence > 0.5)
                        {
                            // 얼굴 흐림 처리 (사람 검출 영역의 상단 30% 부분)
                            if (globalSettings.IsFaceBlurEnabled)
                            {
                                processedFrame = ApplyFaceBlurToPersonRegion(processedFrame, detection);
                            }

                            // 몸 전체 흐림 처리 (전체 사람 검출 영역)
                            if (globalSettings.IsFullBodyBlurEnabled)
                            {
                                processedFrame = ApplyBodyBlurToPersonRegion(processedFrame, detection);
                            }
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"PrivacyProtectionService: {detections.Length}명의 검출된 사람에게 프라이버시 보호 적용");
                }
                else
                {
                    // 검출 정보가 없는 경우 기존 방식 사용 (전체 화면 처리)
                    System.Diagnostics.Debug.WriteLine("PrivacyProtectionService: 검출 정보 없음 - 기존 방식 사용");

                    // 얼굴 흐림 처리
                    if (globalSettings.IsFaceBlurEnabled)
                    {
                        System.Diagnostics.Debug.WriteLine("PrivacyProtectionService: 얼굴 흐림 적용 중 (전체 상반부)");
                        processedFrame = ApplyFaceBlur(processedFrame);
                    }

                    // 몸 전체 흐림 처리
                    if (globalSettings.IsFullBodyBlurEnabled)
                    {
                        System.Diagnostics.Debug.WriteLine("PrivacyProtectionService: 몸 전체 흐림 적용 중 (전체 화면)");
                        processedFrame = ApplyBodyBlur(processedFrame);
                    }
                }

                System.Diagnostics.Debug.WriteLine("PrivacyProtectionService: ProcessFrame 완료");
                return processedFrame;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PrivacyProtectionService: 프레임 개인정보 처리 오류: {ex.Message}");
                return frame; // 오류 시 원본 반환
            }
        }

        /// <summary>
        /// 얼굴 영역에 흐림 효과 적용
        /// </summary>
        private Mat ApplyFaceBlur(Mat frame)
        {
            System.Diagnostics.Debug.WriteLine($"ApplyFaceBlur 시작 - 검출기 상태: {(_faceClassifier != null ? "존재" : "null")}, Empty: {(_faceClassifier?.Empty() ?? true)}");

            // 테스트용 - 확실한 얼굴 흐림 처리 (간단하고 강한 효과)
            try
            {
                var result = frame.Clone();
                
                // 화면의 상반부 절반 영역에 매우 강한 흐림 적용
                int blurHeight = frame.Height / 2;
                var faceArea = new OpenCvSharp.Rect(0, 0, frame.Width, blurHeight);
                
                // 상반부 영역 추출
                var topRegion = new Mat(result, faceArea);
                
                // 매우 강한 흐림 효과 적용
                Cv2.GaussianBlur(topRegion, topRegion, new OpenCvSharp.Size(99, 99), 0);
                
                System.Diagnostics.Debug.WriteLine($"얼굴 매우 강한 흐림 처리 완료 (상반부 절반: {blurHeight}px)");
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"얼굴 흐림 처리 오류: {ex.Message}");
                return frame;
            }

            // 실제 얼굴 검출 코드 (나중에 활성화)
            /*
            if (_faceClassifier == null || _faceClassifier.Empty())
            {
                System.Diagnostics.Debug.WriteLine("얼굴 검출기가 로드되지 않음 - 전체 흐림 처리 적용");
                
                try
                {
                    using var blurred = new Mat();
                    Cv2.GaussianBlur(frame, blurred, new OpenCvSharp.Size(25, 25), 0);
                    return blurred;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"기본 흐림 처리 오류: {ex.Message}");
                    return frame;
                }
            }

            try
            {
                // 그레이스케일 변환 (얼굴 검출용)
                using var grayFrame = new Mat();
                Cv2.CvtColor(frame, grayFrame, ColorConversionCodes.BGR2GRAY);

                // 얼굴 검출
                var faces = _faceClassifier.DetectMultiScale(
                    grayFrame,
                    scaleFactor: 1.1,
                    minNeighbors: 3,
                    flags: HaarDetectionTypes.ScaleImage,
                    minSize: new OpenCvSharp.Size(30, 30)
                );

                if (faces.Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine("얼굴이 검출되지 않음");
                    return frame;
                }

                var result = frame.Clone();
                
                // 검출된 각 얼굴에 흐림 효과 적용
                foreach (var face in faces)
                {
                    // 얼굴 영역을 약간 확장
                    var expandedFace = new OpenCvSharp.Rect(
                        Math.Max(0, face.X - 10),
                        Math.Max(0, face.Y - 10),
                        Math.Min(frame.Width - Math.Max(0, face.X - 10), face.Width + 20),
                        Math.Min(frame.Height - Math.Max(0, face.Y - 10), face.Height + 20)
                    );

                    // 얼굴 영역 추출
                    using var faceRegion = new Mat(result, expandedFace);
                    
                    // 가우시안 블러 적용
                    using var blurredFace = new Mat();
                    Cv2.GaussianBlur(faceRegion, blurredFace, new OpenCvSharp.Size(51, 51), 0);
                    
                    // 원본 프레임에 블러된 얼굴 복사
                    blurredFace.CopyTo(new Mat(result, expandedFace));
                }

                System.Diagnostics.Debug.WriteLine($"얼굴 {faces.Length}개에 흐림 처리 적용");
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"얼굴 흐림 처리 오류: {ex.Message}");
                return frame;
            }
            */
        }

        /// <summary>
        /// 사람 몸 전체에 흐림 효과 적용
        /// </summary>
        private Mat ApplyBodyBlur(Mat frame)
        {
            System.Diagnostics.Debug.WriteLine("ApplyBodyBlur 시작");

            try
            {
                // 안전한 방법으로 전체 프레임에 중간 강도 흐림 적용
                var result = frame.Clone();
                Cv2.GaussianBlur(result, result, new OpenCvSharp.Size(35, 35), 0);
                System.Diagnostics.Debug.WriteLine("몸 전체 흐림 처리 완료 (전체 프레임)");
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"몸 전체 흐림 처리 오류: {ex.Message}");
                return frame;
            }
        }

        /// <summary>
        /// 검출된 사람 영역의 얼굴 부분에 흐림 적용
        /// </summary>
        private Mat ApplyFaceBlurToPersonRegion(Mat frame, DetectionResult detection)
        {
            try
            {
                // RectangleF에서 int로 안전하게 변환
                var bbox = detection.BoundingBox;
                
                // 사람 검출 영역에서 얼굴이 위치할 상단 30% 영역 계산
                int faceHeight = Math.Max(1, (int)(bbox.Height * 0.3f)); // 상단 30%
                int faceWidth = Math.Max(1, (int)(bbox.Width * 0.8f));   // 좌우 80%
                int faceX = (int)(bbox.X + bbox.Width * 0.1f);           // 10% 여백
                int faceY = (int)bbox.Y;                                 // 상단부터 시작

                // 프레임 경계 내로 제한
                faceX = Math.Max(0, Math.Min(faceX, frame.Width - 1));
                faceY = Math.Max(0, Math.Min(faceY, frame.Height - 1));
                faceWidth = Math.Max(1, Math.Min(faceWidth, frame.Width - faceX));
                faceHeight = Math.Max(1, Math.Min(faceHeight, frame.Height - faceY));

                var faceRect = new OpenCvSharp.Rect(faceX, faceY, faceWidth, faceHeight);
                
                // 얼굴 영역에 강한 흐림 적용
                var faceRegion = new Mat(frame, faceRect);
                Cv2.GaussianBlur(faceRegion, faceRegion, new OpenCvSharp.Size(51, 51), 0);
                
                // 흐림 처리된 얼굴 영역에 시각적 표시 (빨간색 네모 박스)
                Cv2.Rectangle(frame, faceRect, new Scalar(0, 0, 255), 3); // 빨간색 테두리 (BGR)
                
                // 텍스트 라벨 추가 (선택사항)
                var labelText = "FACE BLUR";
                var textPos = new OpenCvSharp.Point(faceX, Math.Max(10, faceY - 5));
                Cv2.PutText(frame, labelText, textPos, HersheyFonts.HersheySimplex, 0.6, 
                           new Scalar(0, 0, 255), 2); // 빨간색 텍스트
                
                System.Diagnostics.Debug.WriteLine($"사람 영역 기반 얼굴 흐림 적용 + 시각적 표시: {faceRect} (신뢰도: {detection.Confidence:F2})");
                return frame;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"사람 영역 기반 얼굴 흐림 오류: {ex.Message}");
                return frame;
            }
        }

        /// <summary>
        /// 검출된 사람 영역 전체에 흐림 적용
        /// </summary>
        private Mat ApplyBodyBlurToPersonRegion(Mat frame, DetectionResult detection)
        {
            try
            {
                // RectangleF에서 int로 안전하게 변환
                var bbox = detection.BoundingBox;
                
                // 사람 검출 영역 전체에 흐림 적용
                int bodyX = Math.Max(0, (int)bbox.X);
                int bodyY = Math.Max(0, (int)bbox.Y);
                int bodyWidth = Math.Min(frame.Width - bodyX, (int)bbox.Width);
                int bodyHeight = Math.Min(frame.Height - bodyY, (int)bbox.Height);
                
                var bodyRect = new OpenCvSharp.Rect(bodyX, bodyY, bodyWidth, bodyHeight);

                if (bodyRect.Width > 0 && bodyRect.Height > 0)
                {
                    var bodyRegion = new Mat(frame, bodyRect);
                    Cv2.GaussianBlur(bodyRegion, bodyRegion, new OpenCvSharp.Size(35, 35), 0);
                    
                    // 흐림 처리된 몸 전체 영역에 시각적 표시 (파란색 네모 박스)
                    Cv2.Rectangle(frame, bodyRect, new Scalar(255, 0, 0), 3); // 파란색 테두리 (BGR)
                    
                    // 텍스트 라벨 추가 (선택사항)
                    var labelText = "BODY BLUR";
                    var textPos = new OpenCvSharp.Point(bodyX, Math.Max(10, bodyY - 5));
                    Cv2.PutText(frame, labelText, textPos, HersheyFonts.HersheySimplex, 0.6, 
                               new Scalar(255, 0, 0), 2); // 파란색 텍스트
                    
                    System.Diagnostics.Debug.WriteLine($"사람 영역 기반 몸 전체 흐림 적용 + 시각적 표시: {bodyRect} (신뢰도: {detection.Confidence:F2})");
                }
                
                return frame;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"사람 영역 기반 몸 전체 흐림 오류: {ex.Message}");
                return frame;
            }
        }

        /// <summary>
        /// 사용자 정의 흐림 강도 적용
        /// </summary>
        /// <param name="region">흐림 처리할 영역</param>
        /// <param name="blurIntensity">흐림 강도 (홀수, 기본값: 51)</param>
        /// <returns>흐림 처리된 영역</returns>
        private Mat ApplyCustomBlur(Mat region, int blurIntensity = 51)
        {
            // 홀수로 보정
            if (blurIntensity % 2 == 0)
                blurIntensity++;

            // 최소/최대값 제한
            blurIntensity = Math.Max(3, Math.Min(101, blurIntensity));

            using var blurred = new Mat();
            Cv2.GaussianBlur(region, blurred, new OpenCvSharp.Size(blurIntensity, blurIntensity), 0);
            return blurred;
        }

        /// <summary>
        /// 모자이크 효과 적용 (블러 대신 사용 가능)
        /// </summary>
        private Mat ApplyMosaic(Mat region, int mosaicSize = 20)
        {
            try
            {
                using var small = new Mat();
                using var mosaic = new Mat();
                
                // 이미지를 작게 축소
                Cv2.Resize(region, small, new OpenCvSharp.Size(region.Width / mosaicSize, region.Height / mosaicSize));
                
                // 다시 원본 크기로 확대 (픽셀화 효과)
                Cv2.Resize(small, mosaic, region.Size(), interpolation: InterpolationFlags.Nearest);
                
                return mosaic;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"모자이크 처리 오류: {ex.Message}");
                return region;
            }
        }

        /// <summary>
        /// 설정 업데이트
        /// </summary>
        public void UpdateSettings(SafetySettings newSettings)
        {
            if (newSettings == null) return;
            
            // 설정이 변경된 경우 디버그 로그
            if (_settings.IsFaceBlurEnabled != newSettings.IsFaceBlurEnabled)
            {
                System.Diagnostics.Debug.WriteLine($"얼굴 흐림 설정 변경: {_settings.IsFaceBlurEnabled} -> {newSettings.IsFaceBlurEnabled}");
            }
            
            if (_settings.IsFullBodyBlurEnabled != newSettings.IsFullBodyBlurEnabled)
            {
                System.Diagnostics.Debug.WriteLine($"몸 전체 흐림 설정 변경: {_settings.IsFullBodyBlurEnabled} -> {newSettings.IsFullBodyBlurEnabled}");
            }
            
            // 설정 업데이트
            _settings = newSettings;
        }

        /// <summary>
        /// 리소스 정리
        /// </summary>
        public void Dispose()
        {
            _faceClassifier?.Dispose();
            _bodyDetector?.Dispose();
            System.Diagnostics.Debug.WriteLine("PrivacyProtectionService 리소스 정리 완료");
        }

        /// <summary>
        /// 서비스 상태 확인
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// 현재 활성화된 보호 기능 확인
        /// </summary>
        public string GetActiveProtections()
        {
            var protections = new System.Collections.Generic.List<string>();
            
            // 글로벌 설정을 직접 참조
            var globalSettings = SafetySettingsManager.Instance.CurrentSettings;
            System.Diagnostics.Debug.WriteLine($"PrivacyProtectionService: GetActiveProtections 호출 - Face: {globalSettings.IsFaceBlurEnabled}, Body: {globalSettings.IsFullBodyBlurEnabled}");
            
            if (globalSettings.IsFaceBlurEnabled)
                protections.Add("얼굴 흐림");
                
            if (globalSettings.IsFullBodyBlurEnabled)
                protections.Add("몸 전체 흐림");
            
            var result = protections.Count > 0 ? string.Join(", ", protections) : "없음";
            System.Diagnostics.Debug.WriteLine($"PrivacyProtectionService: GetActiveProtections 결과 - {result}");
            
            return result;
        }
    }
}