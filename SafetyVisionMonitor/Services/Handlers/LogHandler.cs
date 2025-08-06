using System;
using System.IO;
using System.Threading.Tasks;
using SafetyVisionMonitor.Services;

namespace SafetyVisionMonitor.Services.Handlers
{
    /// <summary>
    /// 로그 파일 기록 처리기
    /// </summary>
    public class LogHandler : BaseSafetyEventHandler, IDisposable
    {
        public override string Name => "Log Handler";
        public override int Priority => 200; // 중간 우선순위
        
        private readonly string _logDirectory;
        private readonly object _writeLock = new object();
        private bool _disposed = false;

        public LogHandler(string? customLogPath = null)
        {
            _logDirectory = customLogPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SafetyVisionMonitor",
                "Logs"
            );

            EnsureDirectoryExists(_logDirectory);
        }

        public override async Task HandleAsync(SafetyEventContext context)
        {
            try
            {
                var logEntry = await GenerateLogEntryAsync(context);
                var logFileName = GetLogFileName(context.Violation.Timestamp);
                var logFilePath = Path.Combine(_logDirectory, logFileName);

                await WriteLogEntryAsync(logFilePath, logEntry);

                System.Diagnostics.Debug.WriteLine($"LogHandler: Event logged to {logFilePath}");
                context.SetProperty("LogFilePath", logFilePath);
                context.SetProperty("LogEntry", logEntry);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LogHandler: Error - {ex.Message}");
            }
        }

        private async Task<string> GenerateLogEntryAsync(SafetyEventContext context)
        {
            return await Task.Run(() =>
            {
                var violation = context.Violation;
                var safetyEvent = context.SafetyEvent;
                var timestamp = violation.Timestamp;

                var logEntry = $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] " +
                              $"SAFETY_EVENT | " +
                              $"Type: {violation.ViolationType} | " +
                              $"Zone: {violation.Zone.Name} ({violation.Zone.Id}) | " +
                              $"Camera: {violation.Detection.CameraId} | " +
                              $"Confidence: {violation.Confidence:F3} | " +
                              $"Person_Bbox: {safetyEvent.PersonBoundingBox} | " +
                              $"Severity: {safetyEvent.Severity} | " +
                              $"Description: {safetyEvent.Description}";

                // 추가 컨텍스트 정보가 있으면 포함
                if (context.Properties.Count > 0)
                {
                    var properties = string.Join(", ", 
                        context.Properties.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
                    logEntry += $" | Context: {properties}";
                }

                return logEntry;
            });
        }

        private async Task WriteLogEntryAsync(string logFilePath, string logEntry)
        {
            await Task.Run(() =>
            {
                lock (_writeLock)
                {
                    try
                    {
                        using var writer = new StreamWriter(logFilePath, append: true);
                        writer.WriteLine(logEntry);
                        writer.Flush();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"LogHandler: File write error - {ex.Message}");
                        // 파일 쓰기 실패 시 디버그 출력으로 대체
                        System.Diagnostics.Debug.WriteLine($"SAFETY_LOG: {logEntry}");
                    }
                }
            });
        }

        private string GetLogFileName(DateTime date)
        {
            return $"safety_events_{date:yyyy-MM-dd}.log";
        }

        private void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        /// <summary>
        /// 오래된 로그 파일 정리
        /// </summary>
        public async Task CleanupOldLogsAsync(int keepDays = 30)
        {
            await Task.Run(() =>
            {
                try
                {
                    var cutoffDate = DateTime.Now.AddDays(-keepDays);
                    var logFiles = Directory.GetFiles(_logDirectory, "safety_events_*.log");

                    foreach (var logFile in logFiles)
                    {
                        var fileInfo = new FileInfo(logFile);
                        if (fileInfo.CreationTime < cutoffDate)
                        {
                            File.Delete(logFile);
                            System.Diagnostics.Debug.WriteLine($"LogHandler: Deleted old log file - {fileInfo.Name}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"LogHandler: Cleanup error - {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 로그 검색 (향후 기능)
        /// </summary>
        public async Task<string[]> SearchLogsAsync(DateTime startDate, DateTime endDate, string? keyword = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var results = new List<string>();
                    var logFiles = Directory.GetFiles(_logDirectory, "safety_events_*.log")
                                           .Where(f => IsLogFileInDateRange(f, startDate, endDate));

                    foreach (var logFile in logFiles)
                    {
                        var lines = File.ReadAllLines(logFile);
                        var matchingLines = lines.Where(line => 
                            IsLineInDateRange(line, startDate, endDate) &&
                            (keyword == null || line.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
                        
                        results.AddRange(matchingLines);
                    }

                    return results.ToArray();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"LogHandler: Search error - {ex.Message}");
                    return Array.Empty<string>();
                }
            });
        }

        private bool IsLogFileInDateRange(string logFilePath, DateTime startDate, DateTime endDate)
        {
            var fileName = Path.GetFileNameWithoutExtension(logFilePath);
            if (!fileName.StartsWith("safety_events_")) return false;

            var datePart = fileName.Substring("safety_events_".Length);
            if (DateTime.TryParseExact(datePart, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var fileDate))
            {
                return fileDate >= startDate.Date && fileDate <= endDate.Date;
            }

            return false;
        }

        private bool IsLineInDateRange(string logLine, DateTime startDate, DateTime endDate)
        {
            if (logLine.Length < 23) return false;

            var timestampStr = logLine.Substring(1, 23); // [yyyy-MM-dd HH:mm:ss.fff]
            if (DateTime.TryParseExact(timestampStr, "yyyy-MM-dd HH:mm:ss.fff", null, 
                                     System.Globalization.DateTimeStyles.None, out var logTime))
            {
                return logTime >= startDate && logTime <= endDate;
            }

            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;

            // 필요시 마지막 정리 작업
            _disposed = true;
            System.Diagnostics.Debug.WriteLine("LogHandler: Disposed");
        }
    }
}