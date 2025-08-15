using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SafetyVisionMonitor.Shared.Database;
using SafetyVisionMonitor.Shared.Services;

namespace SafetyVisionMonitor
{
    /// <summary>
    /// 데이터베이스 정리 유틸리티 - 존재하지 않는 모델 파일 제거
    /// </summary>
    class CleanupDatabase
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== 데이터베이스 정리 유틸리티 ===");
            Console.WriteLine("존재하지 않는 AI 모델 파일을 데이터베이스에서 제거합니다.\n");

            try
            {
                // 데이터베이스 서비스 초기화
                var databaseService = new DatabaseService();
                
                // 데이터베이스에 저장된 AI 모델 설정 로드
                var modelConfigs = await databaseService.LoadAIModelConfigsAsync();
                
                Console.WriteLine($"데이터베이스에서 {modelConfigs?.Count ?? 0}개 모델 설정을 찾았습니다.");
                
                if (modelConfigs == null || modelConfigs.Count == 0)
                {
                    Console.WriteLine("처리할 모델이 없습니다.");
                    return;
                }
                
                int removedCount = 0;
                int validCount = 0;
                
                foreach (var config in modelConfigs)
                {
                    Console.WriteLine($"\n검사 중: {config.ModelName}");
                    Console.WriteLine($"  경로: {config.ModelPath}");
                    
                    if (string.IsNullOrEmpty(config.ModelPath))
                    {
                        Console.WriteLine($"  ❌ 모델 경로가 비어있음 - 제거");
                        await databaseService.DeleteAIModelConfigAsync(config.Id);
                        removedCount++;
                        continue;
                    }
                    
                    if (!File.Exists(config.ModelPath))
                    {
                        Console.WriteLine($"  ❌ 파일이 존재하지 않음 - 제거");
                        await databaseService.DeleteAIModelConfigAsync(config.Id);
                        removedCount++;
                        continue;
                    }
                    
                    // YOLOv11 모델 제거 (YoloDotNet에서 지원하지 않음)
                    if (config.ModelName.Contains("YOLOv11") || config.ModelPath.Contains("yolo11"))
                    {
                        Console.WriteLine($"  ❌ YOLOv11 모델 (지원하지 않음) - 제거");
                        await databaseService.DeleteAIModelConfigAsync(config.Id);
                        removedCount++;
                        continue;
                    }
                    
                    // 파일 크기 확인
                    var fileInfo = new FileInfo(config.ModelPath);
                    if (fileInfo.Length < 1024 * 1024) // 1MB 미만
                    {
                        Console.WriteLine($"  ❌ 파일 크기가 너무 작음 ({fileInfo.Length} bytes) - 제거");
                        await databaseService.DeleteAIModelConfigAsync(config.Id);
                        removedCount++;
                        continue;
                    }
                    
                    Console.WriteLine($"  ✅ 유효한 모델 ({fileInfo.Length / 1024.0 / 1024.0:F1} MB)");
                    validCount++;
                }
                
                Console.WriteLine($"\n=== 정리 완료 ===");
                Console.WriteLine($"유효한 모델: {validCount}개");
                Console.WriteLine($"제거된 모델: {removedCount}개");
                
                if (removedCount > 0)
                {
                    Console.WriteLine("\n⚠️ 제거된 모델이 있습니다. 애플리케이션을 다시 시작해주세요.");
                }
                else
                {
                    Console.WriteLine("\n✅ 모든 모델이 유효합니다.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ 오류 발생: {ex.Message}");
                Console.WriteLine($"스택 트레이스: {ex.StackTrace}");
            }
            
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}