using System;
using System.IO;
using YoloDotNet;
using YoloDotNet.Core;
using YoloDotNet.Enums;

namespace SafetyVisionMonitor
{
    /// <summary>
    /// ONNX 모델 파일 테스트 유틸리티
    /// </summary>
    public static class TestONNXModel
    {
        public static void TestModel(string modelPath)
        {
            Console.WriteLine($"Testing ONNX model: {modelPath}");
            
            if (!File.Exists(modelPath))
            {
                Console.WriteLine("Error: Model file not found!");
                return;
            }
            
            var fileInfo = new FileInfo(modelPath);
            Console.WriteLine($"File size: {fileInfo.Length:N0} bytes ({fileInfo.Length / 1024.0 / 1024.0:F2} MB)");
            
            try
            {
                // CPU로만 테스트
                var options = new YoloOptions
                {
                    OnnxModel = modelPath,
                    ImageResize = ImageResize.Proportional,
                    ExecutionProvider = new CpuExecutionProvider()
                };
                
                using var yolo = new Yolo(options);
                Console.WriteLine("✓ Model loaded successfully with CPU!");
                
                // 모델 정보 출력 (가능한 경우)
                Console.WriteLine("Model is ready for inference.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Failed to load model: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"  Inner exception: {ex.InnerException.Message}");
                }
            }
        }
    }
}