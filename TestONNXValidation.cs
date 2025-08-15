using System;
using System.IO;
using Microsoft.ML.OnnxRuntime;
using System.Linq;
using SafetyVisionMonitor.Services;

namespace SafetyVisionMonitor.Test
{
    /// <summary>
    /// ONNX 모델 유효성 검사 테스트 도구
    /// </summary>
    class TestONNXValidation
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== ONNX 모델 유효성 검사 ===\n");
            
            var modelsPath = @"/mnt/d/000.Source/SafetyVisionMonitor/SafetyVisionMonitor/bin/Debug/net8.0-windows/Models";
            
            var modelFiles = new[]
            {
                "yolov8s.onnx",
                "yolov8s-pose.onnx", 
                "yolov8s-seg.onnx",
                "olov8s-cls.onnx",
                "yolov8s-obb.onnx"
            };
            
            int validCount = 0;
            int totalCount = modelFiles.Length;
            
            foreach (var modelFile in modelFiles)
            {
                var fullPath = Path.Combine(modelsPath, modelFile);
                if (TestONNXModel(fullPath))
                {
                    validCount++;
                }
                Console.WriteLine();
            }
            
            Console.WriteLine($"=== 검사 결과: {validCount}/{totalCount} 모델 유효 ===");
            
            if (validCount < totalCount)
            {
                Console.WriteLine("\n⚠️ 문제가 있는 모델이 발견되었습니다!");
                Console.WriteLine("해결 방법:");
                Console.WriteLine("1. ModelConversionView에서 모델을 다시 변환");
                Console.WriteLine("2. 변환 시 'dynamic=False' 옵션 사용");
                Console.WriteLine("3. 공식 Ultralytics 모델 다운로드");
            }
            else
            {
                Console.WriteLine("✅ 모든 모델이 정상입니다!");
                Console.WriteLine("AccessViolationException은 YoloDotNet 호환성 문제일 수 있습니다.");
                Console.WriteLine("PureONNXEngine 자동 전환이 문제를 해결할 것입니다.");
            }
            
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
        
        static void TestONNXModel(string modelPath)
        {
            try
            {
                Console.WriteLine($"=== {Path.GetFileName(modelPath)} ===");
                
                if (!File.Exists(modelPath))
                {
                    Console.WriteLine("❌ 파일이 존재하지 않습니다.");
                    return;
                }
                
                var fileInfo = new FileInfo(modelPath);
                Console.WriteLine($"파일 크기: {fileInfo.Length / 1024.0 / 1024.0:F2} MB");
                Console.WriteLine($"수정 날짜: {fileInfo.LastWriteTime}");
                
                // ONNX Runtime으로 모델 로드 테스트
                using var session = new InferenceSession(modelPath);
                
                // 모델 메타데이터
                var modelMetadata = session.ModelMetadata;
                Console.WriteLine($"프로듀서: {modelMetadata.ProducerName}");
                Console.WriteLine($"도메인: {modelMetadata.Domain}");
                Console.WriteLine($"버전: {modelMetadata.Version}");
                
                // 입력 정보
                Console.WriteLine("\n[입력 텐서]");
                foreach (var input in session.InputMetadata)
                {
                    var shape = string.Join(", ", input.Value.Dimensions);
                    Console.WriteLine($"  {input.Key}: {input.Value.ElementType} [{shape}]");
                    
                    // 동적 차원 체크
                    var hasDynamicDims = input.Value.Dimensions.Any(d => d == -1);
                    Console.WriteLine($"  동적 차원: {hasDynamicDims}");
                }
                
                // 출력 정보
                Console.WriteLine("\n[출력 텐서]");
                foreach (var output in session.OutputMetadata)
                {
                    var shape = string.Join(", ", output.Value.Dimensions);
                    Console.WriteLine($"  {output.Key}: {output.Value.ElementType} [{shape}]");
                }
                
                // YoloDotNet 호환성 체크
                var inputName = session.InputMetadata.Keys.FirstOrDefault() ?? "";
                var firstInput = session.InputMetadata.Values.FirstOrDefault();
                var isYoloDotNetCompatible = inputName == "images" && 
                                           firstInput != null && 
                                           !firstInput.Dimensions.Any(d => d == -1);
                
                Console.WriteLine($"\n[YoloDotNet 호환성]");
                Console.WriteLine($"입력명이 'images': {inputName == "images"} (실제: '{inputName}')");
                Console.WriteLine($"동적 차원 없음: {firstInput != null && !firstInput.Dimensions.Any(d => d == -1)}");
                Console.WriteLine($"✅ YoloDotNet 호환: {isYoloDotNetCompatible}");
                
                if (!isYoloDotNetCompatible)
                {
                    Console.WriteLine("⚠️ 이 모델은 YoloDotNet과 호환되지 않을 수 있습니다!");
                    Console.WriteLine("   - 입력 텐서명이 'images'가 아니거나");
                    Console.WriteLine("   - 동적 차원(-1)이 포함되어 있습니다.");
                    Console.WriteLine("   - 모델을 다시 변환해야 할 수 있습니다.");
                }
                
                Console.WriteLine("✅ 모델 로드 성공");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 모델 검사 실패: {ex.Message}");
                Console.WriteLine($"   오류 타입: {ex.GetType().Name}");
                
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"   내부 오류: {ex.InnerException.Message}");
                }
                
                // 구체적인 오류 분석
                if (ex.Message.Contains("Invalid model"))
                {
                    Console.WriteLine("   → ONNX 모델 파일이 손상되었거나 잘못된 형식입니다.");
                }
                else if (ex.Message.Contains("version"))
                {
                    Console.WriteLine("   → ONNX 버전 호환성 문제일 수 있습니다.");
                }
                else if (ex.Message.Contains("protobuf"))
                {
                    Console.WriteLine("   → Protobuf 파싱 오류 - 파일이 완전히 다운로드되지 않았을 수 있습니다.");
                }
            }
        }
    }
}