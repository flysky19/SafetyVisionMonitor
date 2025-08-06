using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;
using SafetyVisionMonitor.Models;
using SkiaSharp;
using YoloDotNet;
using YoloDotNet.Core;
using YoloDotNet.Enums;
using YoloDotNet.Models;
using Size = System.Drawing.Size;

namespace SafetyVisionMonitor.AI
{
    /// <summary>
    /// YOLOv8 YoloDotNet 추론 엔진
    /// </summary>
    public class YOLOv8Engine : IDisposable
    {
        private Yolo? _yolo;
        private Models.ModelMetadata _metadata;
        private bool _disposed = false;
        private static readonly HttpClient _httpClient = new HttpClient();
        private bool _isUsingGpu = false;
        
        // 모델 다운로드 URL 및 기본 경로
        // YoloDotNet 호환 모델 (동적 축 없는 버전)
        private const string DefaultModelUrl = "https://github.com/ultralytics/assets/releases/download/v8.2.0/yolov8n.onnx";
        private const string DefaultModelFileName = "yolov8n.onnx";
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
        
        public Models.ModelMetadata Metadata => _metadata;
        public bool IsLoaded => _yolo != null;
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
                // ONNX Runtime의 사용 가능한 프로바이더 확인
                using var env = OrtEnv.Instance();
                var providers = env.GetAvailableProviders();
        
                System.Diagnostics.Debug.WriteLine($"Available ONNX Runtime providers: {string.Join(", ", providers)}");
        
                // CUDA 프로바이더 확인
                bool hasCuda = providers.Contains("CUDAExecutionProvider");
        
                if (hasCuda)
                {
                    // CUDA 버전 정보 출력
                    var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
                    System.Diagnostics.Debug.WriteLine($"CUDA_PATH: {cudaPath}");
            
                    // nvidia-smi로 GPU 정보 확인
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "nvidia-smi",
                            Arguments = "--query-gpu=name,driver_version,memory.total --format=csv,noheader",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                
                        using var process = System.Diagnostics.Process.Start(psi);
                        if (process != null)
                        {
                            var output = process.StandardOutput.ReadToEnd();
                            System.Diagnostics.Debug.WriteLine($"GPU Info: {output.Trim()}");
                        }
                    }
                    catch { }
                }
        
                return hasCuda;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking CUDA: {ex.Message}");
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
            
            _metadata = new Models.ModelMetadata();
            
            // YoloDotNet에서 메타데이터 추출
            _metadata.InputSize = new Size(640, 640); // YOLOv8 기본 입력 크기
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
            if (_yolo == null || frame.Empty())
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"YOLOv8Engine: Inference error: {ex.Message}");
                return Array.Empty<DetectionResult>();
            }
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
        
        public void Dispose()
        {
            if (_disposed) return;
            
            _yolo?.Dispose();
            _yolo = null;
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
}