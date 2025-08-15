using System.Drawing;
using System.IO;
using System.Net.Http;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;
using SafetyVisionMonitor.Shared.Models;
using SkiaSharp;
using YoloDotNet;
using YoloDotNet.Core;
using YoloDotNet.Enums;
using YoloDotNet.Models;
using Size = System.Drawing.Size;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// YOLOv8 YoloDotNet 추론 엔진
    /// </summary>
    public class YOLOv8Engine : IDisposable
    {
        private Yolo? _yolo;
        private PureONNXEngine? _pureEngine;
        private ModelMetadata _metadata;
        private bool _disposed = false;
        private static readonly HttpClient _httpClient = new HttpClient();
        private bool _isUsingGpu = false;
        private bool _usePureEngine = false;
        private int _accessViolationCount = 0;
        
        // 모델 다운로드 URL 및 기본 경로
        // YoloDotNet 호환 모델 (동적 축 없는 버전)
        private const string DefaultModelUrl = "https://github.com/ultralytics/assets/releases/download/v8.2.0/yolov8s.onnx";
        private const string DefaultModelFileName = "yolov8s.onnx";
        private static readonly string DefaultModelsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models");
        private static readonly string DefaultModelPath = Path.Combine(DefaultModelsDirectory, DefaultModelFileName);
        
        // 이벤트
        public event EventHandler<ModelDownloadProgressEventArgs>? DownloadProgressChanged;
        
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
        
        public ModelMetadata Metadata => _metadata;
        public bool IsLoaded => _yolo != null || (_usePureEngine && _pureEngine?.IsLoaded == true);
        public bool IsUsingGpu => _isUsingGpu;
        public string ExecutionProvider => _isUsingGpu ? "CUDA GPU" : "CPU";
        
        /// <summary>
        /// YOLO 모델 초기화 (모델이 없으면 자동 다운로드)
        /// </summary>
        /// <param name="modelPath">ONNX 모델 파일 경로 (null이면 기본 모델 사용)</param>
        /// <param name="useGpu">GPU 사용 여부</param>
        /// <returns>초기화 성공 여부</returns>
        public async Task<bool> InitializeAsync(string? modelPath = null, bool useGpu = true)
        {
            try
            {
                // 모델 경로 결정
                var finalModelPath = modelPath ?? DefaultModelPath;
                System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: Attempting to load model from: {finalModelPath}");
                
                // ONNX 모델 검사
                try
                {
                    if (File.Exists(finalModelPath))
                    {
                        ONNXModelInspector.PrintFullMetadata(finalModelPath);
                    }
                }
                catch (Exception inspectEx)
                {
                    System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: 모델 검사 실패: {inspectEx.Message}");
                }
                
                // 모델 파일이 없으면 자동 다운로드
                if (!File.Exists(finalModelPath))
                {
                    System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: Model not found at {finalModelPath}, downloading...");
                    
                    var downloadSuccess = await DownloadDefaultModelAsync(finalModelPath);
                    if (!downloadSuccess)
                    {
                        System.Diagnostics.Debug.WriteLine("YOLOv8Engine: Failed to download model");
                        return false;
                    }
                }
                
                // CUDA 환경 체크
                bool cudaAvailable = false;
                if (useGpu)
                {
                    cudaAvailable = CheckCudaAvailability();
                    System.Diagnostics.Debug.WriteLine($"CUDA Available: {cudaAvailable}");
                }
                
                // GPU 사용 시도, 실패하면 CPU로 자동 전환
                if (useGpu && cudaAvailable)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine("Attempting GPU initialization...");
                        
                        var gpuOptions = new YoloOptions
                        {
                            OnnxModel = finalModelPath,
                            ImageResize = ImageResize.Proportional,
                            ExecutionProvider = new CudaExecutionProvider(GpuId: 0, PrimeGpu: true)
                        };
                        
                        _yolo = new Yolo(gpuOptions);
                        _isUsingGpu = true;
                        System.Diagnostics.Debug.WriteLine("YOLOv8Engine: Successfully initialized with CUDA GPU");
                    }
                    catch (Exception gpuEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: GPU initialization failed: {gpuEx.Message}");
                        System.Diagnostics.Debug.WriteLine("YOLOv8Engine: Falling back to CPU...");
                        
                        // CPU로 재시도
                        var cpuOptions = new YoloOptions
                        {
                            OnnxModel = finalModelPath,
                            ImageResize = ImageResize.Proportional,
                            ExecutionProvider = new CpuExecutionProvider()
                        };
                        
                        _yolo = new Yolo(cpuOptions);
                        _isUsingGpu = false;
                        System.Diagnostics.Debug.WriteLine("YOLOv8Engine: Successfully initialized with CPU");
                    }
                }
                else
                {
                    // CPU 명시적 사용
                    try
                    {
                        var options = new YoloOptions
                        {
                            OnnxModel = finalModelPath,
                            ImageResize = ImageResize.Proportional,
                            ExecutionProvider = new CpuExecutionProvider()
                        };
                        
                        _yolo = new Yolo(options);
                        _isUsingGpu = false;
                        System.Diagnostics.Debug.WriteLine("YOLOv8Engine: Successfully initialized with CPU");
                    }
                    catch (Exception cpuEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: CPU initialization failed: {cpuEx.Message}");
                        throw; // 상위로 예외 전파
                    }
                }
                
                // 모델 파일 확인
                if (!File.Exists(finalModelPath))
                {
                    throw new FileNotFoundException($"Model file not found after download: {finalModelPath}");
                }
                
                var fileInfo = new FileInfo(finalModelPath);
                System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: Model file size: {fileInfo.Length:N0} bytes");
                
                // 파일 크기 검증 (YOLO 모델은 일반적으로 10MB 이상)
                if (fileInfo.Length < 1024 * 1024) // 1MB 미만
                {
                    throw new InvalidOperationException($"Model file seems too small: {fileInfo.Length} bytes. It might be corrupted.");
                }
                
                // 모델 메타데이터 추출
                ExtractModelMetadata();
                
                System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: Model loaded successfully from {finalModelPath}");
                System.Diagnostics.Debug.WriteLine($"Input shape: {_metadata.InputSize}");
                System.Diagnostics.Debug.WriteLine($"Classes: {_metadata.ClassCount}");
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: Failed to load model: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: Inner exception: {ex.InnerException.Message}");
                }
                return false;
            }
        }
        
        private bool CheckCudaEnvironment()
        {
            try
            {
                // CUDA 경로 확인
                var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
                if (string.IsNullOrEmpty(cudaPath))
                {
                    System.Diagnostics.Debug.WriteLine("CUDA_PATH not found in environment variables");
                    return false;
                }
        
                System.Diagnostics.Debug.WriteLine($"CUDA_PATH: {cudaPath}");
        
                // ONNX Runtime으로 CUDA 프로바이더 확인
                var providers = OrtEnv.Instance().GetAvailableProviders();
                var hasCuda = providers.Contains("CUDAExecutionProvider");
        
                System.Diagnostics.Debug.WriteLine($"Available providers: {string.Join(", ", providers)}");
                System.Diagnostics.Debug.WriteLine($"CUDA Provider available: {hasCuda}");
        
                return hasCuda;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking CUDA environment: {ex.Message}");
                return false;
            }
        }
        
        private bool CheckCudaAvailability()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== CUDA 환경 체크 시작 ===");
                
                // 1. 환경 변수 확인
                var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
                var cudaPathV12 = Environment.GetEnvironmentVariable("CUDA_PATH_V12_6");
                var cudaPathV11 = Environment.GetEnvironmentVariable("CUDA_PATH_V11_8");
                
                System.Diagnostics.Debug.WriteLine($"CUDA_PATH: {cudaPath ?? "없음"}");
                System.Diagnostics.Debug.WriteLine($"CUDA_PATH_V12_6: {cudaPathV12 ?? "없음"}");
                System.Diagnostics.Debug.WriteLine($"CUDA_PATH_V11_8: {cudaPathV11 ?? "없음"}");
                
                // 2. PATH 환경변수에서 CUDA 바이너리 확인
                var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
                var hasCudaInPath = pathEnv.Contains("CUDA") || pathEnv.Contains("cuda");
                System.Diagnostics.Debug.WriteLine($"PATH에 CUDA 포함: {hasCudaInPath}");
                
                // 3. ONNX Runtime 프로바이더 확인 (더 안전한 방식)
                string[] availableProviders;
                try
                {
                    // OrtEnv 사용 없이 직접 확인
                    availableProviders = OrtEnv.Instance().GetAvailableProviders().ToArray();
                    System.Diagnostics.Debug.WriteLine($"사용 가능한 ONNX Runtime 프로바이더: {string.Join(", ", availableProviders)}");
                }
                catch (Exception ortEx)
                {
                    System.Diagnostics.Debug.WriteLine($"OrtEnv 초기화 실패: {ortEx.Message}");
                    // 대체 방법: 직접 DLL 확인
                    return CheckCudaDllsDirectly();
                }
                
                // 4. CUDA 프로바이더 확인
                bool hasCudaProvider = availableProviders.Contains("CUDAExecutionProvider");
                System.Diagnostics.Debug.WriteLine($"CUDAExecutionProvider 사용 가능: {hasCudaProvider}");
                
                // 5. nvidia-smi 실행 테스트
                bool nvidiaSmiWorking = false;
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "nvidia-smi",
                        Arguments = "--query-gpu=name,driver_version,memory.total --format=csv,noheader",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    
                    using var process = System.Diagnostics.Process.Start(psi);
                    if (process != null)
                    {
                        process.WaitForExit(5000); // 5초 타임아웃
                        if (process.ExitCode == 0)
                        {
                            var output = process.StandardOutput.ReadToEnd().Trim();
                            if (!string.IsNullOrEmpty(output))
                            {
                                System.Diagnostics.Debug.WriteLine($"GPU 정보: {output}");
                                nvidiaSmiWorking = true;
                            }
                        }
                        else
                        {
                            var error = process.StandardError.ReadToEnd();
                            System.Diagnostics.Debug.WriteLine($"nvidia-smi 오류: {error}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"nvidia-smi 실행 실패: {ex.Message}");
                }
                
                System.Diagnostics.Debug.WriteLine($"nvidia-smi 작동: {nvidiaSmiWorking}");
                
                // 6. 최종 판단
                bool cudaAvailable = hasCudaProvider && nvidiaSmiWorking;
                System.Diagnostics.Debug.WriteLine($"=== CUDA 최종 사용 가능: {cudaAvailable} ===");
                
                return cudaAvailable;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CUDA 환경 체크 중 오류: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"스택 트레이스: {ex.StackTrace}");
                return false;
            }
        }
        
        /// <summary>
        /// 직접 CUDA DLL 파일 확인 (ONNX Runtime 실패 시 대체 방법)
        /// </summary>
        private bool CheckCudaDllsDirectly()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("직접 CUDA DLL 확인 시작...");
                
                var possibleCudaPaths = new[]
                {
                    @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.6\bin",
                    @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.0\bin",
                    @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v11.8\bin",
                    Environment.GetEnvironmentVariable("CUDA_PATH") + @"\bin"
                }.Where(path => !string.IsNullOrEmpty(path) && Directory.Exists(path));
                
                foreach (var path in possibleCudaPaths)
                {
                    System.Diagnostics.Debug.WriteLine($"CUDA 경로 확인: {path}");
                    
                    // 필수 CUDA DLL 확인
                    var requiredDlls = new[] { "cudart64_12.dll", "cudart64_11.dll", "nvcuda.dll" };
                    
                    foreach (var dll in requiredDlls)
                    {
                        var dllPath = Path.Combine(path, dll);
                        if (File.Exists(dllPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"CUDA DLL 발견: {dllPath}");
                            return true;
                        }
                    }
                }
                
                System.Diagnostics.Debug.WriteLine("CUDA DLL을 찾을 수 없음");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CUDA DLL 직접 확인 중 오류: {ex.Message}");
                return false;
            }
        }

        private bool CheckCudnnDlls()
        {
            try
            {
                var requiredDlls = new[]
                {
                    "cudnn64_8.dll",
                    "cudnn64_9.dll",
                    "cudnn_ops_infer64_8.dll",
                    "cudnn_cnn_infer64_8.dll"
                };

                var cudaPaths = new[]
                {
                    @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.0\bin",
                    @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.8\bin",
                    @"C:\Program Files\NVIDIA\CUDNN\v9.5\bin",
                    @"C:\Program Files\NVIDIA\CUDNN\v8.9\bin"
                };

                foreach (var path in cudaPaths)
                {
                    if (Directory.Exists(path))
                    {
                        foreach (var dll in requiredDlls)
                        {
                            var dllPath = Path.Combine(path, dll);
                            if (File.Exists(dllPath))
                            {
                                System.Diagnostics.Debug.WriteLine($"Found: {dllPath}");
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// 동기 초기화 (기존 호환성 유지)
        /// </summary>
        public bool Initialize(string modelPath, bool useGpu = true)
        {
            return InitializeAsync(modelPath, useGpu).GetAwaiter().GetResult();
        }
        
        /// <summary>
        /// 모델 메타데이터 추출
        /// </summary>
        private void ExtractModelMetadata()
        {
            if (_yolo == null) return;
            
            _metadata = new ModelMetadata();
            
            // YoloDotNet에서 메타데이터 추출
            _metadata.InputSize = new Size(640, 640); // YOLOv8 표준 입력 크기 (현장 안전용 고해상도 유지)
            _metadata.ClassCount = CocoClassNames.Length;
            _metadata.AnchorCount = 8400; // YOLOv8 기본 앵커 수
            
            // COCO 클래스 이름 설정
            _metadata.ClassNames = CocoClassNames;
            
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
            if (frame.Empty())
                return Array.Empty<DetectionResult>();
            
            // PureONNXEngine 사용 중인 경우
            if (_usePureEngine && _pureEngine != null)
            {
                return InferFrameWithPureEngine(frame, confidenceThreshold).GetAwaiter().GetResult();
            }
            
            // YoloDotNet 사용 시도
            if (_yolo == null)
                return Array.Empty<DetectionResult>();
            
            try
            {
                // OpenCV Mat을 SKBitmap으로 변환
                using var bitmap = MatToSKBitmap(frame);
                
                // YoloDotNet으로 추론 실행 (confidenceThreshold만 사용, NMS는 내부적으로 처리됨)
                var results = _yolo.RunObjectDetection(bitmap, confidenceThreshold);
                
                // 결과를 DetectionResult 배열로 변환
                var detections = new List<DetectionResult>();
                
                foreach (var detection in results)
                {
                    var result = ConvertToDetectionResult(detection);
                    detections.Add(result);
                }
                
                return detections.ToArray();
            }
            catch (AccessViolationException avEx)
            {
                _accessViolationCount++;
                System.Diagnostics.Debug.WriteLine($"❌ YOLOv8Engine AccessViolationException #{_accessViolationCount}: {avEx.Message}");
                
                // AccessViolationException이 발생하면 PureONNXEngine으로 전환
                if (_accessViolationCount >= 2) // 2번 실패하면 전환
                {
                    System.Diagnostics.Debug.WriteLine("🔄 PureONNXEngine으로 자동 전환 중...");
                    return SwitchToPureEngine(frame, confidenceThreshold);
                }
                
                return Array.Empty<DetectionResult>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: Inference error: {ex.Message}");
                return Array.Empty<DetectionResult>();
            }
        }
        
        /// <summary>
        /// PureONNXEngine으로 전환하고 추론 실행
        /// </summary>
        private DetectionResult[] SwitchToPureEngine(Mat frame, float confidenceThreshold)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("PureONNXEngine 초기화 중...");
                
                // 현재 모델 경로 찾기
                var currentModelPath = GetCurrentModelPath();
                if (string.IsNullOrEmpty(currentModelPath))
                {
                    System.Diagnostics.Debug.WriteLine("현재 모델 경로를 찾을 수 없음");
                    return Array.Empty<DetectionResult>();
                }
                
                // PureONNXEngine 초기화
                _pureEngine = new PureONNXEngine();
                var initSuccess = _pureEngine.InitializeAsync(currentModelPath, _isUsingGpu).GetAwaiter().GetResult();
                
                if (initSuccess)
                {
                    _usePureEngine = true;
                    
                    // YoloDotNet 리소스 정리
                    _yolo?.Dispose();
                    _yolo = null;
                    
                    System.Diagnostics.Debug.WriteLine("✅ PureONNXEngine으로 전환 완료");
                    
                    // 추론 실행
                    return InferFrameWithPureEngine(frame, confidenceThreshold).GetAwaiter().GetResult();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("❌ PureONNXEngine 초기화 실패");
                    _pureEngine?.Dispose();
                    _pureEngine = null;
                    return Array.Empty<DetectionResult>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PureONNXEngine 전환 실패: {ex.Message}");
                _pureEngine?.Dispose();
                _pureEngine = null;
                return Array.Empty<DetectionResult>();
            }
        }
        
        /// <summary>
        /// PureONNXEngine으로 추론 실행
        /// </summary>
        private async Task<DetectionResult[]> InferFrameWithPureEngine(Mat frame, float confidenceThreshold)
        {
            if (_pureEngine == null)
                return Array.Empty<DetectionResult>();
            
            try
            {
                return await _pureEngine.RunDetectionAsync(frame, confidenceThreshold);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PureONNXEngine 추론 오류: {ex.Message}");
                return Array.Empty<DetectionResult>();
            }
        }
        
        /// <summary>
        /// 현재 로드된 모델의 경로 반환
        /// </summary>
        private string GetCurrentModelPath()
        {
            // 기본 모델 경로들을 순서대로 확인
            var possiblePaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "yolov8s.onnx"),
                DefaultModelPath
            };
            
            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
            
            return string.Empty;
        }
        
        /// <summary>
        /// YoloDotNet Detection 객체를 DetectionResult로 변환
        /// </summary>
        private DetectionResult ConvertToDetectionResult(object detection)
        {
            try
            {
                var detectionType = detection.GetType();
                
                // 바운딩 박스 추출 시도
                var boundingBox = ExtractBoundingBox(detection, detectionType);
                
                // 신뢰도 추출
                var confidence = ExtractConfidence(detection, detectionType);
                
                // 클래스 정보 추출
                var (classId, className) = ExtractClassInfo(detection, detectionType);
                
                return new DetectionResult
                {
                    BoundingBox = boundingBox,
                    Confidence = confidence,
                    ClassId = classId,
                    ClassName = className,
                    Label = ExtractLabelName(className) ?? "",
                    Timestamp = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: Error converting detection: {ex.Message}");
                return new DetectionResult
                {
                    BoundingBox = new RectangleF(0, 0, 0, 0),
                    Confidence = 0,
                    ClassId = 0,
                    ClassName = "Unknown",
                    Label = "unknown",
                    Timestamp = DateTime.Now
                };
            }
        }
        
        /// <summary>
        /// 바운딩 박스 추출
        /// </summary>
        private RectangleF ExtractBoundingBox(object detection, Type detectionType)
        {
            // 가능한 바운딩 박스 속성 이름들
            var possibleBoxProperties = new[] { "BoundingBox", "Rectangle", "Bounds", "Box", "Rect" };
            
            foreach (var propName in possibleBoxProperties)
            {
                var prop = detectionType.GetProperty(propName);
                if (prop != null)
                {
                    var boxValue = prop.GetValue(detection);
                    if (boxValue != null)
                    {
                        return ExtractRectangleFromObject(boxValue);
                    }
                }
            }
            
            // 직접 X, Y, Width, Height 속성 찾기
            var x = GetFloatProperty(detection, detectionType, new[] { "X", "Left" });
            var y = GetFloatProperty(detection, detectionType, new[] { "Y", "Top" });
            var width = GetFloatProperty(detection, detectionType, new[] { "Width", "W" });
            var height = GetFloatProperty(detection, detectionType, new[] { "Height", "H" });
            
            return new RectangleF(x, y, width, height);
        }
        
        /// <summary>
        /// 객체에서 Rectangle 정보 추출
        /// </summary>
        private RectangleF ExtractRectangleFromObject(object boxObject)
        {
            var boxType = boxObject.GetType();
            
            var x = GetFloatProperty(boxObject, boxType, new[] { "X", "Left" });
            var y = GetFloatProperty(boxObject, boxType, new[] { "Y", "Top" });
            var width = GetFloatProperty(boxObject, boxType, new[] { "Width", "W" });
            var height = GetFloatProperty(boxObject, boxType, new[] { "Height", "H" });
            
            return new RectangleF(x, y, width, height);
        }
        
        /// <summary>
        /// 신뢰도 추출
        /// </summary>
        private float ExtractConfidence(object detection, Type detectionType)
        {
            return GetFloatProperty(detection, detectionType, new[] { "Confidence", "Score", "Probability" });
        }
        
        /// <summary>
        /// 클래스 정보 추출
        /// </summary>
        private (int classId, string className) ExtractClassInfo(object detection, Type detectionType)
        {
            // 클래스 ID 추출
            var classId = (int)GetFloatProperty(detection, detectionType, new[] { "ClassId", "LabelId", "Id", "Class" });
            
            // 클래스 이름 추출
            var className = GetStringProperty(detection, detectionType, new[] { "ClassName", "LabelName", "Label", "Name" });
            
            // 클래스 객체에서 추출 시도
            if (string.IsNullOrEmpty(className))
            {
                var classObj = GetObjectProperty(detection, detectionType, new[] { "Class", "Label"});
                if (classObj != null)
                {
                    var classType = classObj.GetType();
                    className = GetStringProperty(classObj, classType, new[] { "Name", "Label" });
                    
                    if (classId == 0)
                    {
                        classId = (int)GetFloatProperty(classObj, classType, new[] { "Id", "Index" });
                    }
                }
            }
            
            // 기본값 설정
            if (string.IsNullOrEmpty(className) && classId >= 0 && classId < CocoClassNames.Length)
            {
                className = CocoClassNames[classId];
            }
            
            return (classId, className ?? "Unknown");
        }
        
        /// <summary>
        /// float 속성 값 가져오기
        /// </summary>
        private float GetFloatProperty(object obj, Type objType, string[] possibleNames)
        {
            foreach (var name in possibleNames)
            {
                var prop = objType.GetProperty(name);
                if (prop != null)
                {
                    var value = prop.GetValue(obj);
                    if (value != null)
                    {
                        if (float.TryParse(value.ToString(), out float result))
                        {
                            return result;
                        }
                    }
                }
            }
            return 0f;
        }
        
        /// <summary>
        /// string 속성 값 가져오기
        /// </summary>
        private string? GetStringProperty(object obj, Type objType, string[] possibleNames)
        {
            foreach (var name in possibleNames)
            {
                var prop = objType.GetProperty(name);
                if (prop != null)
                {
                    var value = prop.GetValue(obj);
                    return value?.ToString();
                }
            }
            return null;
        }
        
        /// <summary>
        /// object 속성 값 가져오기
        /// </summary>
        private object? GetObjectProperty(object obj, Type objType, string[] possibleNames)
        {
            foreach (var name in possibleNames)
            {
                var prop = objType.GetProperty(name);
                if (prop != null)
                {
                    return prop.GetValue(obj);
                }
            }
            return null;
        }
        
        /// <summary>
        /// OpenCV Mat을 SKBitmap으로 변환 (안전한 방식)
        /// </summary>
        private static SKBitmap MatToSKBitmap(Mat mat)
        {
            try
            {
                // Mat이 비어있거나 유효하지 않은 경우 체크
                if (mat == null || mat.Empty() || mat.Width <= 0 || mat.Height <= 0)
                {
                    throw new ArgumentException("Invalid Mat object");
                }
                
                // BGR을 RGB로 변환 (OpenCV는 BGR, SkiaSharp는 RGB 사용)
                using var rgbMat = new Mat();
                if (mat.Channels() == 3)
                {
                    Cv2.CvtColor(mat, rgbMat, ColorConversionCodes.BGR2RGB);
                }
                else if (mat.Channels() == 4)
                {
                    Cv2.CvtColor(mat, rgbMat, ColorConversionCodes.BGRA2RGBA);
                }
                else
                {
                    // 그레이스케일인 경우 RGB로 변환
                    Cv2.CvtColor(mat, rgbMat, ColorConversionCodes.GRAY2RGB);
                }
                
                // Mat을 byte 배열로 변환
                var width = rgbMat.Width;
                var height = rgbMat.Height;
                var channels = rgbMat.Channels();
                var pixelData = new byte[width * height * channels];
                
                // Mat 데이터를 byte 배열로 복사
                System.Runtime.InteropServices.Marshal.Copy(rgbMat.Data, pixelData, 0, pixelData.Length);
                
                // SKBitmap 생성
                var bitmap = new SKBitmap(width, height, SKColorType.Rgb888x, SKAlphaType.Opaque);
                
                // 픽셀 데이터 설정
                using (var pixmap = bitmap.PeekPixels())
                {
                    var destPtr = pixmap.GetPixels();
                    
                    unsafe
                    {
                        byte* dest = (byte*)destPtr.ToPointer();
                        
                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                var srcIndex = (y * width + x) * channels;
                                var dstIndex = (y * width + x) * 4;
                                
                                dest[dstIndex] = pixelData[srcIndex];       // R
                                dest[dstIndex + 1] = pixelData[srcIndex + 1]; // G
                                dest[dstIndex + 2] = pixelData[srcIndex + 2]; // B
                                dest[dstIndex + 3] = 255;                     // A
                            }
                        }
                    }
                }
                
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MatToSKBitmap error: {ex.Message}");
                
                // 오류 발생 시 빈 비트맵 반환
                return new SKBitmap(1, 1, SKColorType.Rgb888x, SKAlphaType.Opaque);
            }
        }
        
        
        /// <summary>
        /// 기본 YOLOv8x 모델 다운로드
        /// </summary>
        private async Task<bool> DownloadDefaultModelAsync(string destinationPath)
        {
            try
            {
                // 모델 디렉토리 생성
                var directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: Created directory {directory}");
                }
                
                System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: Downloading model from {DefaultModelUrl}");
                
                // 진행률 보고를 위한 HttpClient 설정
                using var response = await _httpClient.GetAsync(DefaultModelUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                
                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var downloadedBytes = 0L;
                
                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                
                var buffer = new byte[8192];
                int bytesRead;
                
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    downloadedBytes += bytesRead;
                    
                    // 진행률 보고 (1MB마다)
                    if (downloadedBytes % (1024 * 1024) == 0 || downloadedBytes == totalBytes)
                    {
                        var progress = totalBytes > 0 ? (double)downloadedBytes / totalBytes * 100 : 0;
                        
                        DownloadProgressChanged?.Invoke(this, new ModelDownloadProgressEventArgs
                        {
                            ProgressPercentage = progress,
                            DownloadedBytes = downloadedBytes,
                            TotalBytes = totalBytes,
                            ModelName = DefaultModelFileName
                        });
                        
                        System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: Download progress: {progress:F1}% ({downloadedBytes:N0}/{totalBytes:N0} bytes)");
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: Model downloaded successfully to {destinationPath}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: Download failed: {ex.Message}");
                
                // 실패한 경우 부분 파일 삭제
                try
                {
                    if (File.Exists(destinationPath))
                    {
                        File.Delete(destinationPath);
                    }
                }
                catch
                {
                    // 삭제 실패는 무시
                }
                
                return false;
            }
        }
        
        /// <summary>
        /// 기본 모델 경로 조회
        /// </summary>
        public static string GetDefaultModelPath() => DefaultModelPath;
        
        /// <summary>
        /// 모델 파일 존재 여부 확인
        /// </summary>
        public static bool IsDefaultModelAvailable() => File.Exists(DefaultModelPath);
        
        /// <summary>
        /// Label 이름 추출 (LabelModel {Index=0,Name=person} 형태에서 Name 추출)
        /// </summary>
        private static string? ExtractLabelName(string? className)
        {
            if (string.IsNullOrEmpty(className))
                return null;
                
            System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: ExtractLabelName input: '{className}'");
                
            // "LabelModel { Index = 0, Name = person }" 형태 파싱 (공백 포함)
            if (className.Contains("LabelModel") && className.Contains("Name"))
            {
                // "Name = " 또는 "Name=" 패턴 찾기
                var namePattern1 = "Name = ";
                var namePattern2 = "Name=";
                
                int nameStart = -1;
                if (className.Contains(namePattern1))
                {
                    nameStart = className.IndexOf(namePattern1) + namePattern1.Length;
                }
                else if (className.Contains(namePattern2))
                {
                    nameStart = className.IndexOf(namePattern2) + namePattern2.Length;
                }
                
                if (nameStart > 0)
                {
                    // "}" 또는 문자열 끝까지 찾기
                    var nameEnd = className.IndexOf("}", nameStart);
                    if (nameEnd == -1) nameEnd = className.Length;
                    
                    if (nameEnd > nameStart)
                    {
                        var name = className.Substring(nameStart, nameEnd - nameStart).Trim();
                        var result = name.ToLower();
                        System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: ExtractLabelName parsed: '{result}'");
                        return result;
                    }
                }
            }
            
            // 일반적인 문자열인 경우 - 단순히 "person" 등의 값인 경우
            if (!className.Contains("{") && !className.Contains("}"))
            {
                var simple = className.ToLower().Trim();
                System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: ExtractLabelName simple: '{simple}'");
                return simple;
            }
            
            // 파싱 실패 시 기본값
            System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: ExtractLabelName failed to parse, returning 'unknown'");
            return "unknown";
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _yolo?.Dispose();
            _yolo = null;
            
            _pureEngine?.Dispose();
            _pureEngine = null;
            
            _disposed = true;
            
            System.Diagnostics.Debug.WriteLine("YOLOv8Engine: Disposed");
        }
    }
    
    /// <summary>
    /// 모델 다운로드 진행률 이벤트 인자
    /// </summary>
    public class ModelDownloadProgressEventArgs : EventArgs
    {
        public double ProgressPercentage { get; set; }
        public long DownloadedBytes { get; set; }
        public long TotalBytes { get; set; }
        public string ModelName { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// 모델 메타데이터 클래스 (로컬 정의)
    /// </summary>
    public class ModelMetadata
    {
        public Size InputSize { get; set; }
        public int ClassCount { get; set; }
        public int AnchorCount { get; set; }
        public string[] ClassNames { get; set; } = Array.Empty<string>();
    }
}