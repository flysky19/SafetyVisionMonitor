using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 순수 ONNX Runtime을 사용하는 안전한 YOLOv8 추론 엔진
    /// YoloDotNet 없이 직접 ONNX 모델을 처리하여 AccessViolationException 방지
    /// </summary>
    public class PureONNXEngine : IDisposable
    {
        private InferenceSession? _session;
        private string? _inputName;
        private string? _outputName;
        private bool _disposed = false;
        private readonly object _sessionLock = new object();
        
        // 모델 정보
        private int _inputWidth = 640;
        private int _inputHeight = 640;
        private int _numClasses = 80;
        
        // COCO 클래스 이름
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
        
        public bool IsLoaded => _session != null;
        public string ExecutionProvider { get; private set; } = "CPU";
        
        /// <summary>
        /// ONNX 모델 초기화
        /// </summary>
        public async Task<bool> InitializeAsync(string modelPath, bool useGpu = false)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"PureONNXEngine: 모델 로드 시작 - {Path.GetFileName(modelPath)}");
                
                if (!File.Exists(modelPath))
                {
                    System.Diagnostics.Debug.WriteLine($"PureONNXEngine: 모델 파일 없음 - {modelPath}");
                    return false;
                }
                
                // ONNX Runtime 세션 옵션 설정
                var sessionOptions = new SessionOptions();
                
                // 안전성을 위해 CPU 우선 사용
                if (useGpu)
                {
                    try
                    {
                        sessionOptions.AppendExecutionProvider_CUDA(0);
                        ExecutionProvider = "CUDA";
                        System.Diagnostics.Debug.WriteLine("PureONNXEngine: CUDA 실행 공급자 설정");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"PureONNXEngine: CUDA 설정 실패, CPU로 대체: {ex.Message}");
                        ExecutionProvider = "CPU";
                    }
                }
                else
                {
                    ExecutionProvider = "CPU";
                    System.Diagnostics.Debug.WriteLine("PureONNXEngine: CPU 실행 공급자 사용");
                }
                
                // 세션 생성
                _session = new InferenceSession(modelPath, sessionOptions);
                
                // 입출력 메타데이터 확인
                var inputMeta = _session.InputMetadata.FirstOrDefault();
                var outputMeta = _session.OutputMetadata.FirstOrDefault();
                
                if (inputMeta.Key == null || outputMeta.Key == null)
                {
                    System.Diagnostics.Debug.WriteLine("PureONNXEngine: 입출력 메타데이터를 찾을 수 없음");
                    _session?.Dispose();
                    _session = null;
                    return false;
                }
                
                _inputName = inputMeta.Key;
                _outputName = outputMeta.Key;
                
                // 입력 크기 확인
                var inputShape = inputMeta.Value.Dimensions;
                if (inputShape.Length >= 4)
                {
                    _inputHeight = inputShape[2] > 0 ? (int)inputShape[2] : 640;
                    _inputWidth = inputShape[3] > 0 ? (int)inputShape[3] : 640;
                }
                
                System.Diagnostics.Debug.WriteLine($"PureONNXEngine: 모델 로드 성공");
                System.Diagnostics.Debug.WriteLine($"  입력: {_inputName} - [{string.Join(", ", inputShape)}]");
                System.Diagnostics.Debug.WriteLine($"  출력: {_outputName} - [{string.Join(", ", outputMeta.Value.Dimensions)}]");
                System.Diagnostics.Debug.WriteLine($"  입력 크기: {_inputWidth}x{_inputHeight}");
                System.Diagnostics.Debug.WriteLine($"  실행 공급자: {ExecutionProvider}");
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PureONNXEngine: 초기화 실패 - {ex.Message}");
                _session?.Dispose();
                _session = null;
                return false;
            }
        }
        
        /// <summary>
        /// 객체 검출 추론
        /// </summary>
        public async Task<DetectionResult[]> RunDetectionAsync(Mat frame, float confidenceThreshold = 0.7f)
        {
            if (_session == null || _inputName == null || _outputName == null)
            {
                return Array.Empty<DetectionResult>();
            }
            
            try
            {
                return await Task.Run(() =>
                {
                    lock (_sessionLock)
                    {
                        return RunDetectionSafe(frame, confidenceThreshold);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PureONNXEngine: 추론 오류 - {ex.Message}");
                return Array.Empty<DetectionResult>();
            }
        }
        
        private DetectionResult[] RunDetectionSafe(Mat frame, float confidenceThreshold)
        {
            try
            {
                if (frame == null || frame.Empty())
                {
                    return Array.Empty<DetectionResult>();
                }
                
                // 원본 이미지 크기 저장
                var originalWidth = frame.Width;
                var originalHeight = frame.Height;
                
                // 전처리: 이미지 크기 조정
                using var resized = new Mat();
                Cv2.Resize(frame, resized, new OpenCvSharp.Size(_inputWidth, _inputHeight));
                
                // BGR을 RGB로 변환
                using var rgb = new Mat();
                Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);
                
                // 정규화 (0-1 범위)
                var inputTensor = CreateInputTensor(rgb);
                
                // 추론 실행
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(_inputName, inputTensor)
                };
                
                using var results = _session!.Run(inputs);
                var outputTensor = results.FirstOrDefault()?.AsEnumerable<float>().ToArray();
                
                if (outputTensor == null)
                {
                    return Array.Empty<DetectionResult>();
                }
                
                // 후처리: 검출 결과 파싱
                return ParseDetections(outputTensor, confidenceThreshold, originalWidth, originalHeight);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PureONNXEngine: 안전 추론 오류 - {ex.Message}");
                return Array.Empty<DetectionResult>();
            }
        }
        
        private Tensor<float> CreateInputTensor(Mat rgbImage)
        {
            var tensor = new DenseTensor<float>(new[] { 1, 3, _inputHeight, _inputWidth });
            
            // Mat을 바이트 배열로 변환
            rgbImage.GetArray(out byte[] imageBytes);
            
            // CHW 형식으로 변환 (Channel-Height-Width)
            for (int y = 0; y < _inputHeight; y++)
            {
                for (int x = 0; x < _inputWidth; x++)
                {
                    var pixelIndex = (y * _inputWidth + x) * 3;
                    
                    if (pixelIndex + 2 < imageBytes.Length)
                    {
                        // 정규화 (0-255를 0-1로)
                        tensor[0, 0, y, x] = imageBytes[pixelIndex] / 255.0f;     // R
                        tensor[0, 1, y, x] = imageBytes[pixelIndex + 1] / 255.0f; // G
                        tensor[0, 2, y, x] = imageBytes[pixelIndex + 2] / 255.0f; // B
                    }
                }
            }
            
            return tensor;
        }
        
        private DetectionResult[] ParseDetections(float[] output, float confidenceThreshold, int originalWidth, int originalHeight)
        {
            var detections = new List<DetectionResult>();
            
            try
            {
                // YOLOv8 출력 형식: [1, 84, 8400] 또는 [1, 8400, 84]
                // 84 = 4(bbox) + 80(classes)
                var numDetections = output.Length / 84;
                
                for (int i = 0; i < numDetections; i++)
                {
                    var baseIndex = i * 84;
                    
                    if (baseIndex + 83 >= output.Length) break;
                    
                    // 바운딩 박스 좌표 (중심점 + 크기)
                    var centerX = output[baseIndex];
                    var centerY = output[baseIndex + 1];
                    var width = output[baseIndex + 2];
                    var height = output[baseIndex + 3];
                    
                    // 클래스별 확률 확인
                    var maxConfidence = 0.0f;
                    var maxClassIndex = 0;
                    
                    for (int classIndex = 0; classIndex < _numClasses; classIndex++)
                    {
                        var confidence = output[baseIndex + 4 + classIndex];
                        if (confidence > maxConfidence)
                        {
                            maxConfidence = confidence;
                            maxClassIndex = classIndex;
                        }
                    }
                    
                    // 신뢰도 필터링
                    if (maxConfidence < confidenceThreshold) continue;
                    
                    // 좌표 변환 (모델 크기 → 원본 크기)
                    var scaleX = (float)originalWidth / _inputWidth;
                    var scaleY = (float)originalHeight / _inputHeight;
                    
                    var x1 = (centerX - width / 2) * scaleX;
                    var y1 = (centerY - height / 2) * scaleY;
                    var x2 = (centerX + width / 2) * scaleX;
                    var y2 = (centerY + height / 2) * scaleY;
                    
                    // 경계 확인
                    x1 = Math.Max(0, Math.Min(x1, originalWidth));
                    y1 = Math.Max(0, Math.Min(y1, originalHeight));
                    x2 = Math.Max(0, Math.Min(x2, originalWidth));
                    y2 = Math.Max(0, Math.Min(y2, originalHeight));
                    
                    var detection = new DetectionResult
                    {
                        ClassId = maxClassIndex,
                        Label = GetClassName(maxClassIndex),
                        ClassName = GetClassName(maxClassIndex),
                        Confidence = maxConfidence,
                        BoundingBox = new RectangleF(x1, y1, x2 - x1, y2 - y1)
                    };
                    
                    detections.Add(detection);
                }
                
                // NMS (Non-Maximum Suppression) 적용
                return ApplyNMS(detections.ToArray(), 0.45f);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PureONNXEngine: 검출 결과 파싱 오류 - {ex.Message}");
                return Array.Empty<DetectionResult>();
            }
        }
        
        private string GetClassName(int classIndex)
        {
            if (classIndex >= 0 && classIndex < CocoClassNames.Length)
            {
                return CocoClassNames[classIndex];
            }
            return "unknown";
        }
        
        private DetectionResult[] ApplyNMS(DetectionResult[] detections, float iouThreshold)
        {
            if (detections.Length == 0) return detections;
            
            // 신뢰도 기준 내림차순 정렬
            var sortedDetections = detections.OrderByDescending(d => d.Confidence).ToList();
            var keepDetections = new List<DetectionResult>();
            
            while (sortedDetections.Count > 0)
            {
                var current = sortedDetections[0];
                keepDetections.Add(current);
                sortedDetections.RemoveAt(0);
                
                // IoU가 임계값보다 높은 박스들 제거
                sortedDetections = sortedDetections.Where(d => 
                    CalculateIoU(current.BoundingBox, d.BoundingBox) < iouThreshold).ToList();
            }
            
            return keepDetections.ToArray();
        }
        
        private float CalculateIoU(RectangleF box1, RectangleF box2)
        {
            var intersectionX = Math.Max(box1.X, box2.X);
            var intersectionY = Math.Max(box1.Y, box2.Y);
            var intersectionWidth = Math.Max(0, Math.Min(box1.Right, box2.Right) - intersectionX);
            var intersectionHeight = Math.Max(0, Math.Min(box1.Bottom, box2.Bottom) - intersectionY);
            
            var intersectionArea = intersectionWidth * intersectionHeight;
            var unionArea = box1.Width * box1.Height + box2.Width * box2.Height - intersectionArea;
            
            return unionArea > 0 ? intersectionArea / unionArea : 0;
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            lock (_sessionLock)
            {
                _session?.Dispose();
                _session = null;
            }
            
            _disposed = true;
            System.Diagnostics.Debug.WriteLine("PureONNXEngine: 해제됨");
        }
    }
}