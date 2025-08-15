using System;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// ONNX 모델 메타데이터 검사 유틸리티
    /// </summary>
    public static class ONNXModelInspector
    {
        /// <summary>
        /// ONNX 모델의 입력 이름 확인
        /// </summary>
        public static string GetInputName(string modelPath)
        {
            try
            {
                using var session = new InferenceSession(modelPath);
                var inputMeta = session.InputMetadata;
                return inputMeta.Keys.FirstOrDefault() ?? "unknown";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"모델 검사 오류: {ex.Message}");
                return "unknown";
            }
        }
        
        /// <summary>
        /// ONNX 모델의 출력 이름들 확인
        /// </summary>
        public static string[] GetOutputNames(string modelPath)
        {
            try
            {
                using var session = new InferenceSession(modelPath);
                var outputMeta = session.OutputMetadata;
                return outputMeta.Keys.ToArray();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"모델 검사 오류: {ex.Message}");
                return new string[0];
            }
        }
        
        /// <summary>
        /// 모델의 전체 메타데이터 출력
        /// </summary>
        public static void PrintModelInfo(string modelPath)
        {
            try
            {
                using var session = new InferenceSession(modelPath);
                
                System.Diagnostics.Debug.WriteLine($"=== {modelPath} ===");
                
                System.Diagnostics.Debug.WriteLine("입력:");
                foreach (var input in session.InputMetadata)
                {
                    var shape = string.Join(", ", input.Value.Dimensions);
                    System.Diagnostics.Debug.WriteLine($"  {input.Key}: {input.Value.ElementType} [{shape}]");
                }
                
                System.Diagnostics.Debug.WriteLine("출력:");
                foreach (var output in session.OutputMetadata)
                {
                    var shape = string.Join(", ", output.Value.Dimensions);
                    System.Diagnostics.Debug.WriteLine($"  {output.Key}: {output.Value.ElementType} [{shape}]");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"모델 검사 오류: {ex.Message}");
            }
        }
        
        /// <summary>
        /// ONNX 모델의 상세 메타데이터 및 호환성 검사
        /// </summary>
        public static void PrintFullMetadata(string modelPath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"\n=== ONNX 모델 상세 검사: {Path.GetFileName(modelPath)} ===");
                
                // 파일 정보
                var fileInfo = new System.IO.FileInfo(modelPath);
                System.Diagnostics.Debug.WriteLine($"파일 크기: {fileInfo.Length / 1024.0 / 1024.0:F2} MB");
                System.Diagnostics.Debug.WriteLine($"수정 날짜: {fileInfo.LastWriteTime}");
                
                using var session = new InferenceSession(modelPath);
                
                // 모델 메타데이터
                var modelMetadata = session.ModelMetadata;
                System.Diagnostics.Debug.WriteLine($"\n[모델 메타데이터]");
                System.Diagnostics.Debug.WriteLine($"프로듀서: {modelMetadata.ProducerName}");
                System.Diagnostics.Debug.WriteLine($"도메인: {modelMetadata.Domain}");
                System.Diagnostics.Debug.WriteLine($"설명: {modelMetadata.Description}");
                System.Diagnostics.Debug.WriteLine($"버전: {modelMetadata.Version}");
                
                // 커스텀 메타데이터
                if (modelMetadata.CustomMetadataMap.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"\n[커스텀 메타데이터]");
                    foreach (var kvp in modelMetadata.CustomMetadataMap)
                    {
                        System.Diagnostics.Debug.WriteLine($"  {kvp.Key}: {kvp.Value}");
                    }
                }
                
                // 입력 텐서 정보
                System.Diagnostics.Debug.WriteLine($"\n[입력 텐서]");
                foreach (var input in session.InputMetadata)
                {
                    System.Diagnostics.Debug.WriteLine($"이름: {input.Key}");
                    System.Diagnostics.Debug.WriteLine($"  타입: {input.Value.ElementType}");
                    System.Diagnostics.Debug.WriteLine($"  차원: [{string.Join(", ", input.Value.Dimensions)}]");
                    System.Diagnostics.Debug.WriteLine($"  동적 차원: {input.Value.Dimensions.Any(d => d == -1)}");
                }
                
                // 출력 텐서 정보
                System.Diagnostics.Debug.WriteLine($"\n[출력 텐서]");
                foreach (var output in session.OutputMetadata)
                {
                    System.Diagnostics.Debug.WriteLine($"이름: {output.Key}");
                    System.Diagnostics.Debug.WriteLine($"  타입: {output.Value.ElementType}");
                    System.Diagnostics.Debug.WriteLine($"  차원: [{string.Join(", ", output.Value.Dimensions)}]");
                }
                
                // YoloDotNet 호환성 체크
                System.Diagnostics.Debug.WriteLine($"\n[YoloDotNet 호환성 체크]");
                var inputName = session.InputMetadata.Keys.FirstOrDefault() ?? "";
                var isYoloDotNetCompatible = inputName == "images" && !session.InputMetadata.Values.First().Dimensions.Any(d => d == -1);
                System.Diagnostics.Debug.WriteLine($"입력 텐서명 'images': {inputName == "images"}");
                System.Diagnostics.Debug.WriteLine($"동적 차원 없음: {!session.InputMetadata.Values.First().Dimensions.Any(d => d == -1)}");
                System.Diagnostics.Debug.WriteLine($"YoloDotNet 호환: {isYoloDotNetCompatible}");
                
                System.Diagnostics.Debug.WriteLine("=== 검사 완료 ===\n");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"모델 상세 검사 오류: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"오류 타입: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"내부 오류: {ex.InnerException.Message}");
                }
            }
        }
    }
}