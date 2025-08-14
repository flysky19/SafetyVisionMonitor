using System;
using System.Threading.Tasks;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// YouTube 비디오 서비스 테스트 클래스
    /// </summary>
    public static class YouTubeVideoServiceTest
    {
        /// <summary>
        /// 테스트용 유튜브 URL들
        /// </summary>
        public static readonly string[] TestUrls = {
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ",  // Rick Astley - Never Gonna Give You Up
            "https://youtu.be/dQw4w9WgXcQ",                 // 단축 URL
            "https://www.youtube.com/watch?v=9bZkp7q19f0",  // PSY - GANGNAM STYLE
            "https://www.youtube.com/watch?v=kJQP7kiw5Fk"   // Despacito
        };
        
        /// <summary>
        /// 유튜브 URL 유효성 검사 테스트
        /// </summary>
        public static void TestUrlValidation()
        {
            System.Diagnostics.Debug.WriteLine("=== YouTube URL 유효성 검사 테스트 ===");
            
            foreach (var url in TestUrls)
            {
                var isValid = YouTubeVideoService.IsYouTubeUrl(url);
                System.Diagnostics.Debug.WriteLine($"{url} -> {(isValid ? "유효" : "무효")}");
            }
            
            // 무효한 URL 테스트
            var invalidUrls = new[] {
                "https://www.google.com",
                "rtsp://192.168.1.100:554/stream1",
                "not_a_url",
                ""
            };
            
            System.Diagnostics.Debug.WriteLine("\n=== 무효한 URL 테스트 ===");
            foreach (var url in invalidUrls)
            {
                var isValid = YouTubeVideoService.IsYouTubeUrl(url);
                System.Diagnostics.Debug.WriteLine($"{url} -> {(isValid ? "유효" : "무효")}");
            }
        }
        
        /// <summary>
        /// 유튜브 스트림 URL 추출 테스트 (실제 네트워크 연결 필요)
        /// </summary>
        public static async Task TestStreamUrlExtraction(string youtubeUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ")
        {
            System.Diagnostics.Debug.WriteLine($"=== 스트림 URL 추출 테스트: {youtubeUrl} ===");
            
            try
            {
                var streamUrl = await YouTubeVideoService.GetStreamUrlAsync(youtubeUrl);
                
                if (!string.IsNullOrEmpty(streamUrl))
                {
                    System.Diagnostics.Debug.WriteLine($"✓ 스트림 URL 추출 성공");
                    System.Diagnostics.Debug.WriteLine($"스트림 URL (일부): {streamUrl.Substring(0, Math.Min(100, streamUrl.Length))}...");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("✗ 스트림 URL 추출 실패");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ 오류: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 유튜브 영상 정보 가져오기 테스트 (실제 네트워크 연결 필요)
        /// </summary>
        public static async Task TestVideoInfoExtraction(string youtubeUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ")
        {
            System.Diagnostics.Debug.WriteLine($"=== 영상 정보 추출 테스트: {youtubeUrl} ===");
            
            try
            {
                var videoInfo = await YouTubeVideoService.GetVideoInfoAsync(youtubeUrl);
                
                if (videoInfo != null)
                {
                    System.Diagnostics.Debug.WriteLine($"✓ 영상 정보 추출 성공");
                    System.Diagnostics.Debug.WriteLine($"제목: {videoInfo.Title}");
                    System.Diagnostics.Debug.WriteLine($"길이: {videoInfo.Duration}초");
                    System.Diagnostics.Debug.WriteLine($"해상도: {videoInfo.Width}x{videoInfo.Height}");
                    System.Diagnostics.Debug.WriteLine($"FPS: {videoInfo.Fps}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("✗ 영상 정보 추출 실패");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ 오류: {ex.Message}");
            }
        }
    }
}