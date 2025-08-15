using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;
using SafetyVisionMonitor.Shared.Models;
using YoloDotNet;
using YoloDotNet.Core;
using YoloDotNet.Enums;
using YoloDotNet.Models;
using YoloDotNet.Extensions;
using SkiaSharp;
using Size = System.Drawing.Size;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// YOLOv8 멀티태스크 추론 엔진 - Detection, Pose, Segmentation, Classification, OBB 지원
    /// </summary>
    public class YOLOv8MultiTaskEngine : IDisposable
    {
        // 각 태스크별 모델 인스턴스
        private Yolo? _detectionModel;
        private Yolo? _poseModel;
        private Yolo? _segmentationModel;
        private Yolo? _classificationModel;
        private Yolo? _obbModel; // Oriented Bounding Box
        
        private bool _disposed = false;
        private bool _isUsingGpu = false;
        
        // 스레드 안전성을 위한 락 객체들
        private readonly object _detectionLock = new object();
        private readonly object _poseLock = new object();
        private readonly object _segmentationLock = new object();
        private readonly object _classificationLock = new object();
        private readonly object _obbLock = new object();
        
        // 연속 오류 추적
        private int _consecutiveErrors = 0;
        private const int MaxConsecutiveErrors = 5;
        
        // 모델 파일 경로
        private static readonly string ModelsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models");
        private const string DetectionModelFile = "yolov8s.onnx";
        private const string PoseModelFile = "yolov8s-pose.onnx";
        private const string SegmentationModelFile = "yolov8s-seg.onnx";
        private const string ClassificationModelFile = "yolov8s-cls.onnx";
        private const string OBBModelFile = "yolov8s-obb.onnx";
        
        // 모델 메타데이터
        private readonly Dictionary<YoloTask, ModelMetadata> _modelMetadata = new();
        
        // 이벤트
        public event EventHandler<ModelLoadProgressEventArgs>? ModelLoadProgress;
        
        // COCO 데이터셋 클래스 이름 (YOLOv8)
        private static readonly string[] CocoClassNames = {
            "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck",
            "boat", "traffic light", "fire hydrant", "stop sign", "parking meter", "bench",
            "bird", "cat", "dog", "horse", "sheep", "cow", "elephant", "bear", "zebra", "giraffe",
            "backpack", "umbrella", "handbag", "tie", "suitcase", "frisbee", "skis", "snowboard",
            "sports ball", "kite", "baseball bat", "baseball glove", "skateboard", "surfboard",
            "tennis racket", "bottle", "wine glass", "cup", "fork", "knife", "spoon", "bowl",
            "banana", "apple", "sandwich", "orange", "broccoli", "carrot", "hot dog", "pizza",
            "donut", "cake", "chair", "couch", "potted plant", "bed", "dining table", "toilet",
            "tv", "laptop", "mouse", "remote", "keyboard", "cell phone", "microwave", "oven",
            "toaster", "sink", "refrigerator", "book", "clock", "vase", "scissors", "teddy bear",
            "hair drier", "toothbrush"
        };
        
        // 17개 인체 키포인트 정의 (COCO Pose format)
        private static readonly string[] PoseKeypoints = {
            "nose", "left_eye", "right_eye", "left_ear", "right_ear",
            "left_shoulder", "right_shoulder", "left_elbow", "right_elbow",
            "left_wrist", "right_wrist", "left_hip", "right_hip",
            "left_knee", "right_knee", "left_ankle", "right_ankle"
        };
        
        // 프로퍼티
        public bool IsDetectionModelLoaded => _detectionModel != null;
        public bool IsPoseModelLoaded => _poseModel != null;
        public bool IsSegmentationModelLoaded => _segmentationModel != null;
        public bool IsClassificationModelLoaded => _classificationModel != null;
        public bool IsOBBModelLoaded => _obbModel != null;
        public bool IsUsingGpu => _isUsingGpu;
        public string ExecutionProvider => _isUsingGpu ? "CUDA GPU" : "CPU";
        
        public ModelMetadata? GetModelMetadata(YoloTask task)
        {
            return _modelMetadata.TryGetValue(task, out var metadata) ? metadata : null;
        }
        
        /// <summary>
        /// 모든 YOLOv8 모델을 초기화
        /// </summary>
        /// <param name="useGpu">GPU 사용 여부</param>
        /// <returns>로드된 모델 수</returns>
        public async Task<int> InitializeAllModelsAsync(bool useGpu = true)
        {
            System.Diagnostics.Debug.WriteLine("YOLOv8MultiTaskEngine: 모든 모델 초기화 시작...");
            
            var loadedCount = 0;
            var totalModels = 5; // Detection, Pose, Segmentation, Classification, OBB
            var currentModel = 0;
            
            try
            {
                _isUsingGpu = useGpu && CheckCudaAvailability();
                System.Diagnostics.Debug.WriteLine($"GPU 사용: {_isUsingGpu}");
                
                // Detection 모델 로드
                currentModel++;
                ReportProgress("Detection 모델 로드 중...", currentModel, totalModels);
                if (await LoadModelAsync(YoloTask.ObjectDetection, DetectionModelFile))
                {
                    loadedCount++;
                    System.Diagnostics.Debug.WriteLine("✅ Detection 모델 로드 성공");
                }
                
                // Pose 모델 로드
                currentModel++;
                ReportProgress("Pose 모델 로드 중...", currentModel, totalModels);
                if (await LoadModelAsync(YoloTask.PoseEstimation, PoseModelFile))
                {
                    loadedCount++;
                    System.Diagnostics.Debug.WriteLine("✅ Pose 모델 로드 성공");
                }
                
                // Segmentation 모델 로드
                currentModel++;
                ReportProgress("Segmentation 모델 로드 중...", currentModel, totalModels);
                if (await LoadModelAsync(YoloTask.InstanceSegmentation, SegmentationModelFile))
                {
                    loadedCount++;
                    System.Diagnostics.Debug.WriteLine("✅ Segmentation 모델 로드 성공");
                }
                
                // Classification 모델 로드
                currentModel++;
                ReportProgress("Classification 모델 로드 중...", currentModel, totalModels);
                if (await LoadModelAsync(YoloTask.ImageClassification, ClassificationModelFile))
                {
                    loadedCount++;
                    System.Diagnostics.Debug.WriteLine("✅ Classification 모델 로드 성공");
                }
                
                // OBB 모델 로드
                currentModel++;
                ReportProgress("OBB 모델 로드 중...", currentModel, totalModels);
                if (await LoadModelAsync(YoloTask.OrientedBoundingBox, OBBModelFile))
                {
                    loadedCount++;
                    System.Diagnostics.Debug.WriteLine("✅ OBB 모델 로드 성공");
                }
                
                ReportProgress($"모델 로드 완료: {loadedCount}/{totalModels}", totalModels, totalModels);
                System.Diagnostics.Debug.WriteLine($"YOLOv8MultiTaskEngine: {loadedCount}/{totalModels} 모델 로드 완료");
                
                return loadedCount;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"YOLOv8MultiTaskEngine 초기화 오류: {ex.Message}");
                return loadedCount;
            }
        }
        
        /// <summary>
        /// 특정 태스크의 모델 로드
        /// </summary>
        private async Task<bool> LoadModelAsync(YoloTask task, string modelFileName)
        {
            try
            {
                var modelPath = Path.Combine(ModelsDirectory, modelFileName);
                
                if (!File.Exists(modelPath))
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ 모델 파일 없음: {modelPath}");
                    return false;
                }
                
                System.Diagnostics.Debug.WriteLine($"=== {task} 모델 로드: {modelPath} ===");
                
                Yolo model;
                
                // AccessViolationException 방지를 위해 CPU 우선 사용
                System.Diagnostics.Debug.WriteLine($"{task} 모델 CPU로 로드 중... (안정성 우선)");
                
                try
                {
                    var cpuOptions = new YoloOptions
                    {
                        OnnxModel = modelPath,
                        ImageResize = ImageResize.Proportional,
                        ExecutionProvider = new CpuExecutionProvider()
                    };
                    
                    model = new Yolo(cpuOptions);
                    System.Diagnostics.Debug.WriteLine($"✅ {task} 모델 CPU로 로드 성공");
                    _isUsingGpu = false; // CPU로 실행
                }
                catch (Exception cpuEx)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ {task} 모델 CPU 로드 실패: {cpuEx.Message}");
                    
                    // GPU 시도 (최후 수단)
                    if (_isUsingGpu)
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"{task} 모델 GPU로 재시도 중...");
                            
                            var gpuOptions = new YoloOptions
                            {
                                OnnxModel = modelPath,
                                ImageResize = ImageResize.Proportional,
                                ExecutionProvider = new CudaExecutionProvider(GpuId: 0, PrimeGpu: false) // PrimeGpu false로 변경
                            };
                            
                            model = new Yolo(gpuOptions);
                            System.Diagnostics.Debug.WriteLine($"✅ {task} 모델 GPU로 로드 성공");
                        }
                        catch (Exception gpuEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ {task} 모델 GPU 로드도 실패: {gpuEx.Message}");
                            throw; // 모든 시도 실패
                        }
                    }
                    else
                    {
                        throw; // CPU만 시도했는데 실패
                    }
                }
                
                // 태스크별로 모델 할당
                switch (task)
                {
                    case YoloTask.ObjectDetection:
                        _detectionModel = model;
                        break;
                    case YoloTask.PoseEstimation:
                        _poseModel = model;
                        break;
                    case YoloTask.InstanceSegmentation:
                        _segmentationModel = model;
                        break;
                    case YoloTask.ImageClassification:
                        _classificationModel = model;
                        break;
                    case YoloTask.OrientedBoundingBox:
                        _obbModel = model;
                        break;
                }
                
                // 메타데이터 추출
                ExtractModelMetadata(task, model);
                
                await Task.Delay(100); // UI 업데이트를 위한 짧은 지연
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ {task} 모델 로드 실패: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Object Detection 추론
        /// </summary>
        public async Task<DetectionResult[]> RunObjectDetectionAsync(Mat frame, float confidenceThreshold = 0.7f)
        {
            // 연속 오류가 너무 많으면 일시적으로 비활성화
            if (_consecutiveErrors >= MaxConsecutiveErrors)
            {
                System.Diagnostics.Debug.WriteLine($"연속 오류 {_consecutiveErrors}회로 인해 Detection 일시 비활성화");
                return Array.Empty<DetectionResult>();
            }
            
            if (_detectionModel == null)
            {
                System.Diagnostics.Debug.WriteLine("Detection 모델이 로드되지 않음");
                return Array.Empty<DetectionResult>();
            }
            
            if (frame == null || frame.Empty())
            {
                System.Diagnostics.Debug.WriteLine("프레임이 null이거나 비어있음");
                return Array.Empty<DetectionResult>();
            }
            
            try
            {
                return await Task.Run(() =>
                {
                    // 전용 락 사용
                    lock (_detectionLock)
                    {
                        return RunDetectionSafely(frame, confidenceThreshold);
                    }
                });
            }
            catch (Exception ex)
            {
                _consecutiveErrors++;
                System.Diagnostics.Debug.WriteLine($"Object Detection 추론 오류 ({_consecutiveErrors}/{MaxConsecutiveErrors}): {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Exception type: {ex.GetType().Name}");
                
                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                
                return Array.Empty<DetectionResult>();
            }
        }
        
        private DetectionResult[] RunDetectionSafely(Mat frame, float confidenceThreshold)
        {
            try
            {
                using var bitmap = ConvertMatToSKBitmap(frame);
                if (bitmap == null)
                {
                    System.Diagnostics.Debug.WriteLine("SKBitmap 변환 실패");
                    return Array.Empty<DetectionResult>();
                }
                
                System.Diagnostics.Debug.WriteLine($"Running detection on {bitmap.Width}x{bitmap.Height} image");
                
                // 모델 상태 확인
                if (_detectionModel == null || _disposed)
                {
                    System.Diagnostics.Debug.WriteLine("모델이 null이거나 dispose됨");
                    return Array.Empty<DetectionResult>();
                }
                
                // ONNX 추론 호출을 try-catch로 격리
                try
                {
                    var detections = _detectionModel.RunObjectDetection(bitmap, confidenceThreshold);
                    var results = ConvertToDetectionResults(detections);
                    
                    // 성공 시 오류 카운터 초기화
                    _consecutiveErrors = 0;
                    
                    return results;
                }
                catch (AccessViolationException avEx)
                {
                    _consecutiveErrors++;
                    System.Diagnostics.Debug.WriteLine($"❌ AccessViolationException (#{_consecutiveErrors}): {avEx.Message}");
                    System.Diagnostics.Debug.WriteLine("ONNX Runtime 메모리 손상 또는 모델 호환성 문제");
                    
                    // 메모리 정리 시도
                    try
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                    }
                    catch { }
                    
                    return Array.Empty<DetectionResult>();
                }
                catch (Exception modelEx)
                {
                    _consecutiveErrors++;
                    System.Diagnostics.Debug.WriteLine($"❌ 모델 추론 오류 (#{_consecutiveErrors}): {modelEx.Message}");
                    return Array.Empty<DetectionResult>();
                }
            }
            catch (Exception ex)
            {
                _consecutiveErrors++;
                System.Diagnostics.Debug.WriteLine($"❌ Detection 전체 오류 (#{_consecutiveErrors}): {ex.Message}");
                return Array.Empty<DetectionResult>();
            }
        }
        
        /// <summary>
        /// Pose Estimation 추론
        /// </summary>
        public async Task<PoseResult[]> RunPoseEstimationAsync(Mat frame, float confidenceThreshold = 0.7f)
        {
            if (_poseModel == null)
            {
                System.Diagnostics.Debug.WriteLine("Pose 모델이 로드되지 않음");
                return Array.Empty<PoseResult>();
            }
            
            try
            {
                return await Task.Run(() =>
                {
                    using var bitmap = ConvertMatToSKBitmap(frame);
                    var detections = _poseModel.RunObjectDetection(bitmap, confidenceThreshold);
                    return ConvertToPoseResults(detections);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Pose Estimation 추론 오류: {ex.Message}");
                return Array.Empty<PoseResult>();
            }
        }
        
        /// <summary>
        /// Instance Segmentation 추론
        /// </summary>
        public async Task<SegmentationResult[]> RunInstanceSegmentationAsync(Mat frame, float confidenceThreshold = 0.7f)
        {
            if (_segmentationModel == null)
            {
                System.Diagnostics.Debug.WriteLine("Segmentation 모델이 로드되지 않음");
                return Array.Empty<SegmentationResult>();
            }
            
            try
            {
                return await Task.Run(() =>
                {
                    using var bitmap = ConvertMatToSKBitmap(frame);
                    var detections = _segmentationModel.RunObjectDetection(bitmap, confidenceThreshold);
                    return ConvertToSegmentationResults(detections);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Instance Segmentation 추론 오류: {ex.Message}");
                return Array.Empty<SegmentationResult>();
            }
        }
        
        /// <summary>
        /// Image Classification 추론
        /// </summary>
        public async Task<ClassificationResult[]> RunClassificationAsync(Mat frame)
        {
            if (_classificationModel == null)
            {
                System.Diagnostics.Debug.WriteLine("Classification 모델이 로드되지 않음");
                return Array.Empty<ClassificationResult>();
            }
            
            try
            {
                return await Task.Run(() =>
                {
                    using var bitmap = ConvertMatToSKBitmap(frame);
                    var results = _classificationModel.RunClassification(bitmap, 5); // Top 5 결과
                    return ConvertToClassificationResults(results);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Classification 추론 오류: {ex.Message}");
                return Array.Empty<ClassificationResult>();
            }
        }
        
        /// <summary>
        /// Oriented Bounding Box Detection 추론
        /// </summary>
        public async Task<OBBResult[]> RunOBBDetectionAsync(Mat frame, float confidenceThreshold = 0.7f)
        {
            if (_obbModel == null)
            {
                System.Diagnostics.Debug.WriteLine("OBB 모델이 로드되지 않음");
                return Array.Empty<OBBResult>();
            }
            
            try
            {
                return await Task.Run(() =>
                {
                    using var bitmap = ConvertMatToSKBitmap(frame);
                    var detections = _obbModel.RunObjectDetection(bitmap, confidenceThreshold);
                    return ConvertToOBBResults(detections);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OBB Detection 추론 오류: {ex.Message}");
                return Array.Empty<OBBResult>();
            }
        }
        
        // 변환 메서드들
        private DetectionResult[] ConvertToDetectionResults(IEnumerable<ObjectDetection> detections)
        {
            return detections.Select(d => new DetectionResult
            {
                Label = d.Label.Name,
                ClassName = d.Label.Name,
                Confidence = (float)d.Confidence,
                ClassId = ExtractClassId(d.Label),
                BoundingBox = ConvertSKRectToRectangleF(d.BoundingBox)
            }).ToArray();
        }
        
        private PoseResult[] ConvertToPoseResults(IEnumerable<ObjectDetection> detections)
        {
            return detections.Where(d => d.Label.Name == "person").Select(d => new PoseResult
            {
                PersonBoundingBox = ConvertSKRectToRectangle(d.BoundingBox),
                Confidence = (float)d.Confidence,
                Keypoints = ExtractKeypoints(d),
                KeypointNames = PoseKeypoints
            }).ToArray();
        }
        
        private SegmentationResult[] ConvertToSegmentationResults(IEnumerable<ObjectDetection> detections)
        {
            return detections.Select(d => new SegmentationResult
            {
                Label = d.Label.Name,
                ClassName = d.Label.Name,
                Confidence = (float)d.Confidence,
                BoundingBox = ConvertSKRectToRectangle(d.BoundingBox),
                Mask = null // 마스크 추출 로직 필요
            }).ToArray();
        }
        
        private ClassificationResult[] ConvertToClassificationResults(IEnumerable<Classification> classifications)
        {
            return classifications.Select(c => new ClassificationResult
            {
                Label = ExtractLabelFromClassification(c),
                Confidence = (float)c.Confidence
            }).ToArray();
        }
        
        private OBBResult[] ConvertToOBBResults(IEnumerable<ObjectDetection> detections)
        {
            return detections.Select(d => new OBBResult
            {
                Label = d.Label.Name,
                ClassName = d.Label.Name,
                Confidence = (float)d.Confidence,
                BoundingBox = ConvertSKRectToRectangle(d.BoundingBox),
                Rotation = 0.0f // OBB 회전각 추출 로직 필요
            }).ToArray();
        }
        
        private PointF[] ExtractKeypoints(ObjectDetection detection)
        {
            // TODO: YoloDotNet에서 키포인트 추출 로직 구현
            // 현재는 빈 배열 반환
            return new PointF[17];
        }
        
        private string ExtractLabelFromClassification(Classification classification)
        {
            try
            {
                var labelProperty = classification.GetType().GetProperty("Label");
                if (labelProperty != null)
                {
                    var labelValue = labelProperty.GetValue(classification);
                    
                    if (labelValue is string directLabel)
                    {
                        return directLabel;
                    }
                    else if (labelValue != null)
                    {
                        var nameProperty = labelValue.GetType().GetProperty("Name");
                        if (nameProperty != null)
                        {
                            var nameValue = nameProperty.GetValue(labelValue);
                            return nameValue?.ToString() ?? "Unknown";
                        }
                        return labelValue.ToString() ?? "Unknown";
                    }
                }
                return "Unknown";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Classification 레이블 추출 오류: {ex.Message}");
                return "Unknown";
            }
        }
        
        private int ExtractClassId(LabelModel label)
        {
            try
            {
                var labelType = label.GetType();
                var possibleNames = new[] { "Id", "Index", "ClassId", "LabelId" };
                
                foreach (var propName in possibleNames)
                {
                    var property = labelType.GetProperty(propName);
                    if (property != null && property.PropertyType == typeof(int))
                    {
                        var value = property.GetValue(label);
                        if (value is int intValue)
                        {
                            return intValue;
                        }
                    }
                }
                
                return GetClassIdFromName(label.Name);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ClassId 추출 오류: {ex.Message}");
                return GetClassIdFromName(label.Name);
            }
        }
        
        private int GetClassIdFromName(string className)
        {
            if (string.IsNullOrEmpty(className))
                return 0;
            
            for (int i = 0; i < CocoClassNames.Length; i++)
            {
                if (string.Equals(CocoClassNames[i], className, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return 0;
        }
        
        private RectangleF ConvertSKRectToRectangleF(SKRectI skRect)
        {
            return new RectangleF(
                skRect.Left,
                skRect.Top,
                skRect.Width,
                skRect.Height
            );
        }
        
        private Rectangle ConvertSKRectToRectangle(SKRectI skRect)
        {
            return new Rectangle(
                skRect.Left,
                skRect.Top,
                skRect.Width,
                skRect.Height
            );
        }
        
        private SKBitmap ConvertMatToSKBitmap(Mat mat)
        {
            if (mat == null || mat.Empty())
            {
                System.Diagnostics.Debug.WriteLine("Mat이 null이거나 비어있음");
                return CreateDefaultBitmap();
            }
            
            // 이미지 크기 검증
            if (mat.Width <= 0 || mat.Height <= 0 || mat.Width > 4096 || mat.Height > 4096)
            {
                System.Diagnostics.Debug.WriteLine($"Mat 크기가 비정상적임: {mat.Width}x{mat.Height}");
                return CreateDefaultBitmap();
            }
            
            try
            {
                // 메모리 기반 변환 우선 시도 (파일 I/O 회피)
                return ConvertMatToSKBitmapMemory(mat);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"메모리 기반 변환 실패, 파일 기반으로 재시도: {ex.Message}");
                
                try
                {
                    var tempPath = Path.GetTempFileName() + ".png";
                    
                    try
                    {
                        Cv2.ImWrite(tempPath, mat);
                        var bitmap = SKBitmap.Decode(tempPath);
                        File.Delete(tempPath);
                        return bitmap ?? CreateDefaultBitmap();
                    }
                    catch (Exception fileEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"파일 기반 변환 실패: {fileEx.Message}");
                        
                        if (File.Exists(tempPath))
                        {
                            try { File.Delete(tempPath); } catch { }
                        }
                        
                        return CreateDefaultBitmap();
                    }
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"모든 변환 방법 실패: {fallbackEx.Message}");
                    return CreateDefaultBitmap();
                }
            }
        }
        
        private SKBitmap ConvertMatToSKBitmapMemory(Mat mat)
        {
            try
            {
                if (mat == null || mat.Empty())
                {
                    System.Diagnostics.Debug.WriteLine("ConvertMatToSKBitmapMemory: Mat이 null이거나 비어있음");
                    return CreateDefaultBitmap();
                }
                
                // OpenCV 인코딩
                bool encodeResult = Cv2.ImEncode(".png", mat, out var imageBytes);
                if (!encodeResult || imageBytes == null || imageBytes.Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine("OpenCV 인코딩 실패");
                    return CreateDefaultBitmap();
                }
                
                // SkiaSharp 디코딩
                using var stream = new MemoryStream(imageBytes);
                var bitmap = SKBitmap.Decode(stream);
                
                if (bitmap == null)
                {
                    System.Diagnostics.Debug.WriteLine("SkiaSharp 디코딩 실패");
                    return CreateDefaultBitmap();
                }
                
                // 크기 검증
                if (bitmap.Width <= 0 || bitmap.Height <= 0)
                {
                    System.Diagnostics.Debug.WriteLine($"디코딩된 비트맵 크기가 비정상적임: {bitmap.Width}x{bitmap.Height}");
                    bitmap.Dispose();
                    return CreateDefaultBitmap();
                }
                
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"메모리 기반 변환 실패: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Exception type: {ex.GetType().Name}");
                return CreateDefaultBitmap();
            }
        }
        
        private SKBitmap CreateDefaultBitmap()
        {
            return new SKBitmap(640, 640, SKColorType.Rgb888x, SKAlphaType.Opaque);
        }
        
        private void ExtractModelMetadata(YoloTask task, Yolo model)
        {
            var metadata = new ModelMetadata
            {
                InputSize = new Size(640, 640),
                ClassCount = CocoClassNames.Length,
                ClassNames = CocoClassNames
            };
            
            _modelMetadata[task] = metadata;
        }
        
        private bool CheckCudaAvailability()
        {
            try
            {
                var providers = OrtEnv.Instance().GetAvailableProviders();
                return providers.Contains("CUDAExecutionProvider");
            }
            catch
            {
                return false;
            }
        }
        
        private void ReportProgress(string message, int current, int total)
        {
            var progress = (double)current / total * 100;
            ModelLoadProgress?.Invoke(this, new ModelLoadProgressEventArgs
            {
                Message = message,
                ProgressPercentage = progress,
                CurrentModel = current,
                TotalModels = total
            });
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            _detectionModel?.Dispose();
            _poseModel?.Dispose();
            _segmentationModel?.Dispose();
            _classificationModel?.Dispose();
            _obbModel?.Dispose();
            
            _disposed = true;
            System.Diagnostics.Debug.WriteLine("YOLOv8MultiTaskEngine 해제됨");
        }
    }
    
    // 열거형 정의
    public enum YoloTask
    {
        ObjectDetection,
        PoseEstimation,
        InstanceSegmentation,
        ImageClassification,
        OrientedBoundingBox
    }
    
    // 결과 클래스들
    public class PoseResult
    {
        public Rectangle PersonBoundingBox { get; set; }
        public float Confidence { get; set; }
        public PointF[] Keypoints { get; set; } = Array.Empty<PointF>();
        public string[] KeypointNames { get; set; } = Array.Empty<string>();
    }
    
    public class SegmentationResult
    {
        public string Label { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public float Confidence { get; set; }
        public Rectangle BoundingBox { get; set; }
        public byte[]? Mask { get; set; }
    }
    
    public class ClassificationResult
    {
        public string Label { get; set; } = string.Empty;
        public float Confidence { get; set; }
    }
    
    public class OBBResult
    {
        public string Label { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public float Confidence { get; set; }
        public Rectangle BoundingBox { get; set; }
        public float Rotation { get; set; } // 회전각 (도 단위)
    }
    
    // 이벤트 인자 클래스
    public class ModelLoadProgressEventArgs : EventArgs
    {
        public string Message { get; set; } = string.Empty;
        public double ProgressPercentage { get; set; }
        public int CurrentModel { get; set; }
        public int TotalModels { get; set; }
    }
}