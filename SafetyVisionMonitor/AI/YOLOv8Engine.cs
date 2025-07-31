using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using SafetyVisionMonitor.Models;
using Size = System.Drawing.Size;

namespace SafetyVisionMonitor.AI
{
    /// <summary>
    /// YOLOv8 ONNX 추론 엔진
    /// </summary>
    public class YOLOv8Engine : IDisposable
    {
        private InferenceSession? _session;
        private Models.ModelMetadata _metadata;
        private bool _disposed = false;
        
        // COCO 데이터셋 클래스 이름 (YOLOv8 기본)
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
        
        public Models.ModelMetadata Metadata => _metadata;
        public bool IsLoaded => _session != null;
        
        /// <summary>
        /// YOLO 모델 초기화
        /// </summary>
        /// <param name="modelPath">ONNX 모델 파일 경로</param>
        /// <param name="useGpu">GPU 사용 여부</param>
        /// <returns>초기화 성공 여부</returns>
        public bool Initialize(string modelPath, bool useGpu = true)
        {
            try
            {
                // ONNX Runtime 세션 옵션 설정
                var sessionOptions = new SessionOptions();
                
                if (useGpu)
                {
                    try
                    {
                        // GPU 사용 시도 (CUDA 또는 DirectML)
                        sessionOptions.AppendExecutionProvider_CUDA();
                        System.Diagnostics.Debug.WriteLine("YOLOv8Engine: CUDA provider enabled");
                    }
                    catch
                    {
                        try
                        {
                            sessionOptions.AppendExecutionProvider_DML();
                            System.Diagnostics.Debug.WriteLine("YOLOv8Engine: DirectML provider enabled");
                        }
                        catch
                        {
                            System.Diagnostics.Debug.WriteLine("YOLOv8Engine: GPU providers not available, using CPU");
                        }
                    }
                }
                
                // 세션 생성
                _session = new InferenceSession(modelPath, sessionOptions);
                
                // 모델 메타데이터 추출
                ExtractModelMetadata();
                
                System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: Model loaded successfully from {modelPath}");
                System.Diagnostics.Debug.WriteLine($"Input shape: {_metadata.InputSize}");
                System.Diagnostics.Debug.WriteLine($"Classes: {_metadata.ClassCount}");
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: Failed to load model: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 모델 메타데이터 추출
        /// </summary>
        private void ExtractModelMetadata()
        {
            if (_session == null) return;
            
            _metadata = new Models.ModelMetadata();
            
            // 입력 텐서 정보에서 크기 추출
            var inputMeta = _session.InputMetadata.First();
            var inputShape = inputMeta.Value.Dimensions;
            
            if (inputShape.Length >= 4)
            {
                // [batch, channels, height, width] 형식 가정
                _metadata.InputSize = new Size(inputShape[3], inputShape[2]);
            }
            
            // 출력 텐서 정보에서 클래스 수 추출
            var outputMeta = _session.OutputMetadata.First();
            var outputShape = outputMeta.Value.Dimensions;
            
            if (outputShape.Length >= 2)
            {
                // YOLOv8 출력: [1, classes+4, anchors] 형식
                _metadata.ClassCount = outputShape[1] - 4; // x,y,w,h 제외
                _metadata.AnchorCount = outputShape[2];
            }
            
            // COCO 클래스 이름 설정
            _metadata.ClassNames = CocoClassNames.Take(_metadata.ClassCount).ToArray();
            
            System.Diagnostics.Debug.WriteLine($"Extracted metadata - Input: {_metadata.InputSize}, Classes: {_metadata.ClassCount}, Anchors: {_metadata.AnchorCount}");
        }
        
        /// <summary>
        /// 단일 프레임 추론
        /// </summary>
        /// <param name="frame">입력 이미지</param>
        /// <param name="confidenceThreshold">신뢰도 임계값</param>
        /// <param name="nmsThreshold">NMS 임계값</param>
        /// <returns>검출 결과</returns>
        public DetectionResult[] InferFrame(Mat frame, float confidenceThreshold = 0.7f, float nmsThreshold = 0.45f)
        {
            if (_session == null || frame.Empty())
                return Array.Empty<DetectionResult>();
            
            try
            {
                // 1. 전처리
                var inputTensor = PreprocessImage(frame);
                
                // 2. 추론 실행
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(_session.InputMetadata.Keys.First(), inputTensor)
                };
                
                using var results = _session.Run(inputs);
                var outputTensor = results.First().AsTensor<float>();
                
                // 3. 후처리
                var detections = PostprocessResults(outputTensor, frame.Size(), confidenceThreshold);
                
                // 4. NMS 적용
                var finalDetections = ApplyNMS(detections, nmsThreshold);
                
                return finalDetections;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: Inference error: {ex.Message}");
                return Array.Empty<DetectionResult>();
            }
        }
        
        /// <summary>
        /// 이미지 전처리 (Mat → 정규화된 텐서)
        /// </summary>
        private Tensor<float> PreprocessImage(Mat image)
        {
            // 1. 모델 입력 크기로 리사이즈 (letterbox)
            var resized = new Mat();
            var scale = Math.Min((float)_metadata.InputSize.Width / image.Width,
                               (float)_metadata.InputSize.Height / image.Height);
            
            var newWidth = (int)(image.Width * scale);
            var newHeight = (int)(image.Height * scale);
            
            Cv2.Resize(image, resized, new OpenCvSharp.Size(newWidth, newHeight));
            
            // 2. 패딩 추가 (중앙 정렬)
            var deltaW = _metadata.InputSize.Width - newWidth;
            var deltaH = _metadata.InputSize.Height - newHeight;
            var top = deltaH / 2;
            var bottom = deltaH - top;
            var left = deltaW / 2;
            var right = deltaW - left;
            
            var padded = new Mat();
            Cv2.CopyMakeBorder(resized, padded, top, bottom, left, right, 
                BorderTypes.Constant, new Scalar(114, 114, 114));
            
            // 3. BGR → RGB 변환 (YOLO는 RGB 입력)
            var rgb = new Mat();
            Cv2.CvtColor(padded, rgb, ColorConversionCodes.BGR2RGB);
            
            // 4. 정규화 및 텐서 변환
            var inputTensor = new DenseTensor<float>(new[] { 1, 3, _metadata.InputSize.Height, _metadata.InputSize.Width });
            
            for (int y = 0; y < _metadata.InputSize.Height; y++)
            {
                for (int x = 0; x < _metadata.InputSize.Width; x++)
                {
                    var pixel = rgb.At<Vec3b>(y, x);
                    
                    // 0~255 → 0~1 정규화
                    inputTensor[0, 0, y, x] = pixel.Item2 / 255.0f; // R
                    inputTensor[0, 1, y, x] = pixel.Item1 / 255.0f; // G
                    inputTensor[0, 2, y, x] = pixel.Item0 / 255.0f; // B
                }
            }
            
            // 메모리 해제
            resized.Dispose();
            padded.Dispose();
            rgb.Dispose();
            
            return inputTensor;
        }
        
        /// <summary>
        /// 추론 결과 후처리
        /// </summary>
        private DetectionResult[] PostprocessResults(Tensor<float> output, OpenCvSharp.Size originalSize, float confidenceThreshold)
        {
            var detections = new List<DetectionResult>();
            
            // YOLOv8 출력 형식: [1, classes+4, anchors]
            var outputShape = output.Dimensions.ToArray();
            var numClasses = outputShape[1] - 4;
            var numAnchors = outputShape[2];
            
            // 스케일링 팩터 계산
            var scaleX = (float)originalSize.Width / _metadata.InputSize.Width;
            var scaleY = (float)originalSize.Height / _metadata.InputSize.Height;
            
            for (int i = 0; i < numAnchors; i++)
            {
                // 바운딩 박스 좌표 (중심점 기준)
                var centerX = output[0, 0, i];
                var centerY = output[0, 1, i];
                var width = output[0, 2, i];
                var height = output[0, 3, i];
                
                // 클래스별 신뢰도 중 최대값 찾기
                var maxConfidence = 0f;
                var maxClassId = 0;
                
                for (int c = 0; c < numClasses; c++)
                {
                    var confidence = output[0, 4 + c, i];
                    if (confidence > maxConfidence)
                    {
                        maxConfidence = confidence;
                        maxClassId = c;
                    }
                }
                
                // 신뢰도 임계값 검사
                if (maxConfidence < confidenceThreshold)
                    continue;
                
                // 좌표 변환 (중심점 → 좌상단 모서리)
                var x1 = (centerX - width / 2) * scaleX;
                var y1 = (centerY - height / 2) * scaleY;
                var x2 = (centerX + width / 2) * scaleX;
                var y2 = (centerY + height / 2) * scaleY;
                
                // 경계 검사
                x1 = Math.Max(0, Math.Min(originalSize.Width - 1, x1));
                y1 = Math.Max(0, Math.Min(originalSize.Height - 1, y1));
                x2 = Math.Max(0, Math.Min(originalSize.Width - 1, x2));
                y2 = Math.Max(0, Math.Min(originalSize.Height - 1, y2));
                
                var detection = new DetectionResult
                {
                    BoundingBox = new RectangleF(x1, y1, x2 - x1, y2 - y1),
                    Confidence = maxConfidence,
                    ClassId = maxClassId,
                    ClassName = maxClassId < _metadata.ClassNames.Length ? _metadata.ClassNames[maxClassId] : $"Class_{maxClassId}",
                    Timestamp = DateTime.Now
                };
                
                detections.Add(detection);
            }
            
            return detections.ToArray();
        }
        
        /// <summary>
        /// Non-Maximum Suppression 적용
        /// </summary>
        private DetectionResult[] ApplyNMS(DetectionResult[] detections, float nmsThreshold)
        {
            if (detections.Length == 0)
                return detections;
            
            // 신뢰도 순으로 정렬
            var sorted = detections.OrderByDescending(d => d.Confidence).ToList();
            var keep = new List<DetectionResult>();
            
            while (sorted.Count > 0)
            {
                var current = sorted[0];
                keep.Add(current);
                sorted.RemoveAt(0);
                
                // 현재 박스와 겹치는 박스들 제거
                sorted.RemoveAll(other => 
                    current.ClassId == other.ClassId && 
                    CalculateIoU(current.BoundingBox, other.BoundingBox) > nmsThreshold);
            }
            
            return keep.ToArray();
        }
        
        /// <summary>
        /// IoU (Intersection over Union) 계산
        /// </summary>
        private static float CalculateIoU(RectangleF box1, RectangleF box2)
        {
            var intersectionArea = RectangleF.Intersect(box1, box2);
            if (intersectionArea.IsEmpty)
                return 0f;
            
            var intersection = intersectionArea.Width * intersectionArea.Height;
            var union = box1.Width * box1.Height + box2.Width * box2.Height - intersection;
            
            return union > 0 ? intersection / union : 0f;
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            _session?.Dispose();
            _session = null;
            _disposed = true;
            
            System.Diagnostics.Debug.WriteLine("YOLOv8Engine: Disposed");
        }
    }
}