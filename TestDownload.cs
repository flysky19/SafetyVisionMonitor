using System;
using System.Threading.Tasks;
using SafetyVisionMonitor.AI;

namespace SafetyVisionMonitor.Test
{
    /// <summary>
    /// YOLO 모델 자동 다운로드 테스트
    /// </summary>
    class TestDownload
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("YOLO 모델 자동 다운로드 테스트 시작...");
            
            var engine = new YOLOv8Engine();
            
            // 다운로드 진행률 이벤트 구독
            engine.DownloadProgressChanged += (sender, e) =>
            {
                Console.WriteLine($"다운로드 진행률: {e.ProgressPercentage:F1}% " +
                                $"({e.DownloadedBytes:N0}/{e.TotalBytes:N0} bytes)");
            };
            
            try
            {
                Console.WriteLine("기본 모델 경로: " + YOLOv8Engine.GetDefaultModelPath());
                Console.WriteLine("모델 존재 여부: " + YOLOv8Engine.IsDefaultModelAvailable());
                
                // 모델 초기화 (없으면 자동 다운로드)
                Console.WriteLine("모델 초기화 중...");
                var success = await engine.InitializeAsync();
                
                if (success)
                {
                    Console.WriteLine("✓ 모델 로드 성공!");
                    Console.WriteLine($"입력 크기: {engine.Metadata.InputSize}");
                    Console.WriteLine($"클래스 수: {engine.Metadata.ClassCount}");
                }
                else
                {
                    Console.WriteLine("✗ 모델 로드 실패!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"오류 발생: {ex.Message}");
            }
            finally
            {
                engine.Dispose();
            }
            
            Console.WriteLine("테스트 완료. 아무 키나 누르세요...");
            Console.ReadKey();
        }
    }
}