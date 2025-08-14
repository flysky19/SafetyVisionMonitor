using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 유튜브 영상 URL을 실제 스트림 URL로 변환하는 서비스
    /// </summary>
    public class YouTubeVideoService
    {
        private static readonly string YtDlpPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe");
        
        /// <summary>
        /// 유튜브 URL에서 실제 스트림 URL 추출 (기본 1080p)
        /// </summary>
        /// <param name="youtubeUrl">유튜브 URL</param>
        /// <returns>실제 스트림 URL</returns>
        public static async Task<string?> GetStreamUrlAsync(string youtubeUrl)
        {
            return await GetStreamUrlAsync(youtubeUrl, VideoQuality.HD1080);
        }
        
        /// <summary>
        /// 유튜브 URL에서 실제 스트림 URL 추출 (화질 지정)
        /// </summary>
        /// <param name="youtubeUrl">유튜브 URL</param>
        /// <param name="quality">원하는 화질</param>
        /// <returns>실제 스트림 URL</returns>
        public static async Task<string?> GetStreamUrlAsync(string youtubeUrl, VideoQuality quality)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"YouTubeVideoService: 유튜브 URL 처리 중... {youtubeUrl}");
                
                // yt-dlp가 없으면 다운로드
                await EnsureYtDlpExists();
                
                // 화질에 따른 포맷 문자열 생성
                var formatString = GetFormatString(quality);
                
                // yt-dlp를 사용해서 스트림 URL 추출
                var startInfo = new ProcessStartInfo
                {
                    FileName = YtDlpPath,
                    Arguments = $"--get-url --format \"{formatString}\" \"{youtubeUrl}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = new Process { StartInfo = startInfo };
                process.Start();
                
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                
                await process.WaitForExitAsync();
                
                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    var streamUrl = output.Trim();
                    System.Diagnostics.Debug.WriteLine($"YouTubeVideoService: 스트림 URL 추출 성공");
                    return streamUrl;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"YouTubeVideoService: yt-dlp 오류 - {error}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"YouTubeVideoService: 스트림 URL 추출 실패 - {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 유튜브 영상 정보 가져오기
        /// </summary>
        /// <param name="youtubeUrl">유튜브 URL</param>
        /// <returns>영상 정보</returns>
        public static async Task<YouTubeVideoInfo?> GetVideoInfoAsync(string youtubeUrl)
        {
            try
            {
                await EnsureYtDlpExists();
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = YtDlpPath,
                    Arguments = $"--dump-json \"{youtubeUrl}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = new Process { StartInfo = startInfo };
                process.Start();
                
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                
                await process.WaitForExitAsync();
                
                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    var jsonDoc = JsonDocument.Parse(output);
                    var root = jsonDoc.RootElement;
                    
                    return new YouTubeVideoInfo
                    {
                        Title = root.GetProperty("title").GetString() ?? "Unknown",
                        Duration = root.GetProperty("duration").GetInt32(),
                        Width = root.GetProperty("width").GetInt32(),
                        Height = root.GetProperty("height").GetInt32(),
                        Fps = root.GetProperty("fps").GetInt32()
                    };
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"YouTubeVideoService: 영상 정보 가져오기 실패 - {error}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"YouTubeVideoService: 영상 정보 가져오기 실패 - {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// yt-dlp 실행 파일이 존재하는지 확인하고 없으면 다운로드
        /// </summary>
        private static async Task EnsureYtDlpExists()
        {
            if (File.Exists(YtDlpPath))
                return;
                
            System.Diagnostics.Debug.WriteLine("YouTubeVideoService: yt-dlp.exe 다운로드 중...");
            
            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(5);
                
                const string downloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
                var response = await httpClient.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();
                
                var directory = Path.GetDirectoryName(YtDlpPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                await using var fileStream = File.Create(YtDlpPath);
                await response.Content.CopyToAsync(fileStream);
                
                System.Diagnostics.Debug.WriteLine("YouTubeVideoService: yt-dlp.exe 다운로드 완료");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"YouTubeVideoService: yt-dlp.exe 다운로드 실패 - {ex.Message}");
                throw new InvalidOperationException("yt-dlp 다운로드에 실패했습니다. 인터넷 연결을 확인해주세요.", ex);
            }
        }
        
        /// <summary>
        /// URL이 유튜브 URL인지 확인
        /// </summary>
        /// <param name="url">확인할 URL</param>
        /// <returns>유튜브 URL 여부</returns>
        public static bool IsYouTubeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;
                
            url = url.ToLower().Trim();
            
            return url.Contains("youtube.com/watch") || 
                   url.Contains("youtu.be/") || 
                   url.Contains("youtube.com/live/") ||
                   url.Contains("m.youtube.com/watch");
        }
        
        /// <summary>
        /// 화질에 따른 yt-dlp 포맷 문자열 생성
        /// </summary>
        /// <param name="quality">원하는 화질</param>
        /// <returns>yt-dlp 포맷 문자열</returns>
        private static string GetFormatString(VideoQuality quality)
        {
            return quality switch
            {
                VideoQuality.Best => "best",                           // 최고 화질 (제한 없음)
                VideoQuality.HD4K => "best[height<=2160]",            // 4K (2160p)
                VideoQuality.HD1440 => "best[height<=1440]",          // 1440p
                VideoQuality.HD1080 => "best[height<=1080]",          // 1080p (Full HD)
                VideoQuality.HD720 => "best[height<=720]",            // 720p (HD)
                VideoQuality.SD480 => "best[height<=480]",            // 480p
                VideoQuality.SD360 => "best[height<=360]",            // 360p
                VideoQuality.Worst => "worst",                        // 최저 화질
                _ => "best[height<=1080]"                              // 기본값: 1080p
            };
        }
    }
    
    /// <summary>
    /// 유튜브 영상 화질 옵션
    /// </summary>
    public enum VideoQuality
    {
        /// <summary>최고 화질 (제한 없음, 네트워크 및 성능 부하 높음)</summary>
        Best,
        /// <summary>4K (2160p)</summary>
        HD4K,
        /// <summary>1440p</summary>
        HD1440,
        /// <summary>1080p (Full HD) - 권장</summary>
        HD1080,
        /// <summary>720p (HD)</summary>
        HD720,
        /// <summary>480p (SD)</summary>
        SD480,
        /// <summary>360p</summary>
        SD360,
        /// <summary>최저 화질</summary>
        Worst
    }
    
    /// <summary>
    /// 유튜브 영상 정보
    /// </summary>
    public class YouTubeVideoInfo
    {
        public string Title { get; set; } = string.Empty;
        public int Duration { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Fps { get; set; }
    }
}