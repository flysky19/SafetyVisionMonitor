using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SafetyVisionMonitor.Database;
using SafetyVisionMonitor.Models;
using System.Text.Json;
using System.Windows.Media;

namespace SafetyVisionMonitor.Services
{
    public class DatabaseService
    {
        public DatabaseService()
        {
            // DB 초기화
            using var context = new AppDbContext();
            context.Database.EnsureCreated();
            
            // 화질 설정 컬럼 마이그레이션
            MigrateCameraQualitySettings(context);
        }
        
        private void MigrateCameraQualitySettings(AppDbContext context)
        {
            try
            {
                // 화질 컬럼이 존재하는지 확인하고 기본값으로 업데이트
                var connection = context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }
                
                using var command = connection.CreateCommand();
                
                // 화질 컬럼들을 기본값으로 업데이트 (NULL인 경우에만)
                command.CommandText = @"
                    UPDATE CameraConfigs 
                    SET 
                        Brightness = COALESCE(Brightness, 128.0),
                        Contrast = COALESCE(Contrast, 32.0),
                        Saturation = COALESCE(Saturation, 64.0),
                        Exposure = COALESCE(Exposure, -1.0),
                        Gain = COALESCE(Gain, 0.0),
                        Hue = COALESCE(Hue, 0.0),
                        Gamma = COALESCE(Gamma, 1.0),
                        Sharpness = COALESCE(Sharpness, 0.0),
                        AutoExposure = COALESCE(AutoExposure, 1),
                        AutoWhiteBalance = COALESCE(AutoWhiteBalance, 1)
                    WHERE Brightness IS NULL OR Contrast IS NULL;
                ";
                
                var rowsAffected = command.ExecuteNonQuery();
                
                if (rowsAffected > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Migrated {rowsAffected} camera records with default quality settings");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Camera quality migration failed: {ex.Message}");
                // 마이그레이션 실패 시 DB 재생성을 위해 기존 DB 삭제
                try
                {
                    context.Database.EnsureDeleted();
                    context.Database.EnsureCreated();
                    System.Diagnostics.Debug.WriteLine("Database recreated due to migration failure");
                }
                catch (Exception recreateEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Database recreation failed: {recreateEx.Message}");
                }
            }
        }
        
        // 안전 이벤트 저장
        public async Task<int> SaveSafetyEventAsync(SafetyEvent safetyEvent)
        {
            using var context = new AppDbContext();
            context.SafetyEvents.Add(safetyEvent);
            await context.SaveChangesAsync();
            return safetyEvent.Id;
        }
        
        // 안전 이벤트 조회
        public async Task<List<SafetyEvent>> GetSafetyEventsAsync(
            DateTime? startDate = null, 
            DateTime? endDate = null,
            string? eventType = null,
            string? cameraId = null,
            int? limit = null)
        {
            using var context = new AppDbContext();
            
            var query = context.SafetyEvents.AsQueryable();
            
            if (startDate.HasValue)
                query = query.Where(e => e.Timestamp >= startDate.Value);
                
            if (endDate.HasValue)
                query = query.Where(e => e.Timestamp <= endDate.Value);
                
            if (!string.IsNullOrEmpty(eventType))
                query = query.Where(e => e.EventType == eventType);
                
            if (!string.IsNullOrEmpty(cameraId))
                query = query.Where(e => e.CameraId == cameraId);
                
            query = query.OrderByDescending(e => e.Timestamp);
            
            if (limit.HasValue)
                query = query.Take(limit.Value);
                
            return await query.ToListAsync();
        }
        
        // 카메라 설정 저장
        public async Task SaveCameraConfigsAsync(List<Camera> cameras)
        {
            using var context = new AppDbContext();
            
            try
            {
                // 기존 설정 삭제 (안전하게)
                var existingConfigs = await context.CameraConfigs.ToListAsync();
                if (existingConfigs.Any())
                {
                    context.CameraConfigs.RemoveRange(existingConfigs);
                }
                
                // 새 설정 저장
                foreach (var camera in cameras)
                {
                    context.CameraConfigs.Add(new CameraConfig
                    {
                        CameraId = camera.Id,
                        Name = camera.Name,
                        ConnectionString = camera.ConnectionString,
                        Type = camera.Type.ToString(),
                        Width = camera.Width,
                        Height = camera.Height,
                        Fps = camera.Fps,
                        IsEnabled = camera.IsEnabled,
                        CalibrationPixelsPerMeter = camera.CalibrationPixelsPerMeter,
                        IsCalibrated = camera.IsCalibrated,
                        // 화질 설정
                        Brightness = camera.Brightness,
                        Contrast = camera.Contrast,
                        Saturation = camera.Saturation,
                        Exposure = camera.Exposure,
                        Gain = camera.Gain,
                        Hue = camera.Hue,
                        Gamma = camera.Gamma,
                        Sharpness = camera.Sharpness,
                        AutoExposure = camera.AutoExposure,
                        AutoWhiteBalance = camera.AutoWhiteBalance,
                        LastModified = DateTime.Now
                    });
                }
                
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Camera configuration error: {ex}");
                throw;
            }
        }
        
        // 단일 카메라 설정 저장
        public async Task SaveCameraConfigAsync(Camera camera)
        {
            using var context = new AppDbContext();
    
            // 기존 설정 찾기
            var existing = await context.CameraConfigs
                .FirstOrDefaultAsync(c => c.CameraId == camera.Id);
        
            if (existing != null)
            {
                // 업데이트
                existing.Name = camera.Name;
                existing.ConnectionString = camera.ConnectionString;
                existing.Type = camera.Type.ToString();
                existing.Width = camera.Width;
                existing.Height = camera.Height;
                existing.Fps = camera.Fps;
                existing.IsEnabled = camera.IsEnabled;
                existing.CalibrationPixelsPerMeter = camera.CalibrationPixelsPerMeter;
                existing.IsCalibrated = camera.IsCalibrated;
                // 화질 설정 업데이트
                existing.Brightness = camera.Brightness;
                existing.Contrast = camera.Contrast;
                existing.Saturation = camera.Saturation;
                existing.Exposure = camera.Exposure;
                existing.Gain = camera.Gain;
                existing.Hue = camera.Hue;
                existing.Gamma = camera.Gamma;
                existing.Sharpness = camera.Sharpness;
                existing.AutoExposure = camera.AutoExposure;
                existing.AutoWhiteBalance = camera.AutoWhiteBalance;
                existing.LastModified = DateTime.Now;
            }
            else
            {
                // 새로 추가
                context.CameraConfigs.Add(new CameraConfig
                {
                    CameraId = camera.Id,
                    Name = camera.Name,
                    ConnectionString = camera.ConnectionString,
                    Type = camera.Type.ToString(),
                    Width = camera.Width,
                    Height = camera.Height,
                    Fps = camera.Fps,
                    IsEnabled = camera.IsEnabled,
                    CalibrationPixelsPerMeter = camera.CalibrationPixelsPerMeter,
                    IsCalibrated = camera.IsCalibrated,
                    // 화질 설정
                    Brightness = camera.Brightness,
                    Contrast = camera.Contrast,
                    Saturation = camera.Saturation,
                    Exposure = camera.Exposure,
                    Gain = camera.Gain,
                    Hue = camera.Hue,
                    Gamma = camera.Gamma,
                    Sharpness = camera.Sharpness,
                    AutoExposure = camera.AutoExposure,
                    AutoWhiteBalance = camera.AutoWhiteBalance,
                    LastModified = DateTime.Now
                });
            }
    
            await context.SaveChangesAsync();
        }
        
        // 카메라 설정 불러오기
        public async Task<List<Camera>> LoadCameraConfigsAsync()
        {
            using var context = new AppDbContext();
            
            var configs = await context.CameraConfigs.ToListAsync();
            
            return configs.Select(config => new Camera
            {
                Id = config.CameraId,
                Name = config.Name,
                ConnectionString = config.ConnectionString,
                Type = Enum.Parse<CameraType>(config.Type),
                Width = config.Width,
                Height = config.Height,
                Fps = config.Fps,
                IsEnabled = config.IsEnabled,
                CalibrationPixelsPerMeter = config.CalibrationPixelsPerMeter,
                IsCalibrated = config.IsCalibrated,
                // 화질 설정 로드
                Brightness = config.Brightness,
                Contrast = config.Contrast,
                Saturation = config.Saturation,
                Exposure = config.Exposure,
                Gain = config.Gain,
                Hue = config.Hue,
                Gamma = config.Gamma,
                Sharpness = config.Sharpness,
                AutoExposure = config.AutoExposure,
                AutoWhiteBalance = config.AutoWhiteBalance
            }).ToList();
        }
        
        // 통계 데이터 조회
        public async Task<Dictionary<string, int>> GetEventStatisticsAsync(DateTime startDate, DateTime endDate)
        {
            using var context = new AppDbContext();
            
            var statistics = await context.SafetyEvents
                .Where(e => e.Timestamp >= startDate && e.Timestamp <= endDate)
                .GroupBy(e => e.EventType)
                .Select(g => new { EventType = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.EventType, x => x.Count);
                
            return statistics;
        }
        
        // 오래된 데이터 정리
        public async Task CleanupOldDataAsync(int daysToKeep)
        {
            using var context = new AppDbContext();
            
            var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
            
            // 오래된 이벤트 삭제
            var oldEvents = context.SafetyEvents.Where(e => e.Timestamp < cutoffDate);
            context.SafetyEvents.RemoveRange(oldEvents);
            
            // 오래된 추적 기록 삭제
            var oldTracking = context.PersonTrackingRecords.Where(t => t.LastSeenTime < cutoffDate);
            context.PersonTrackingRecords.RemoveRange(oldTracking);
            
            await context.SaveChangesAsync();
        }
        
        // 3D 구역 저장
        public async Task SaveZone3DConfigsAsync(List<Zone3D> zones)
        {
            using var context = new AppDbContext();
            
            foreach (var zone in zones)
            {
                // 기존 구역 찾기
                var existing = await context.Zone3DConfigs
                    .FirstOrDefaultAsync(z => z.ZoneId == zone.Id);
                
                System.Diagnostics.Debug.WriteLine($"DatabaseService: Saving zone {zone.Name}, Zone IsEnabled={zone.IsEnabled}");
                
                var config = new Zone3DConfig
                {
                    ZoneId = zone.Id,
                    Name = zone.Name,
                    CameraId = zone.CameraId,
                    Type = zone.Type.ToString(),
                    VerticesJson = JsonSerializer.Serialize(new
                    {
                        FloorPoints = zone.FloorPoints.Select(p => new { X = p.X, Y = p.Y }).ToList(),
                        Height = zone.Height
                    }),
                    Color = ColorToHex(zone.DisplayColor),
                    Opacity = zone.Opacity,
                    IsEnabled = zone.IsEnabled,
                    CreatedTime = zone.CreatedDate,
                    CalibrationPixelsPerMeter = zone.CalibrationPixelsPerMeter,
                    CalibrationFrameWidth = zone.CalibrationFrameWidth,
                    CalibrationFrameHeight = zone.CalibrationFrameHeight
                };
                
                System.Diagnostics.Debug.WriteLine($"DatabaseService: Config IsEnabled will be saved as {config.IsEnabled}");
                
                if (existing != null)
                {
                    // 업데이트
                    existing.Name = config.Name;
                    existing.CameraId = config.CameraId;
                    existing.Type = config.Type;
                    existing.VerticesJson = config.VerticesJson;
                    existing.Color = config.Color;
                    existing.Opacity = config.Opacity;
                    existing.IsEnabled = config.IsEnabled;
                    existing.CalibrationPixelsPerMeter = config.CalibrationPixelsPerMeter;
                    existing.CalibrationFrameWidth = config.CalibrationFrameWidth;
                    existing.CalibrationFrameHeight = config.CalibrationFrameHeight;
                }
                else
                {
                    // 새로 추가
                    context.Zone3DConfigs.Add(config);
                }
            }
            
            await context.SaveChangesAsync();
        }
        
        // 3D 구역 로드
        public async Task<List<Zone3D>> LoadZone3DConfigsAsync(string? cameraId = null)
        {
            using var context = new AppDbContext();
            
            var query = context.Zone3DConfigs.AsQueryable();
            
            if (!string.IsNullOrEmpty(cameraId))
            {
                query = query.Where(z => z.CameraId == cameraId);
            }
            
            var configs = await query.ToListAsync();
            System.Diagnostics.Debug.WriteLine($"DatabaseService: Found {configs.Count} zone configs in database");
            
            var zones = new List<Zone3D>();
            
            foreach (var config in configs)
            {
                var zone = new Zone3D();
                
                // 로딩 플래그 설정 (자동 저장 방지)
                zone.IsLoading = true;
                
                // 먼저 이름 설정 (로깅을 위해)
                zone.Name = config.Name;
                
                // 그 다음 다른 속성들 설정
                zone.Id = config.ZoneId;
                zone.CameraId = config.CameraId;
                zone.Type = Enum.Parse<ZoneType>(config.Type);
                zone.DisplayColor = HexToColor(config.Color);
                zone.Opacity = config.Opacity;
                zone.CreatedDate = config.CreatedTime;
                zone.CalibrationPixelsPerMeter = config.CalibrationPixelsPerMeter;
                zone.CalibrationFrameWidth = config.CalibrationFrameWidth;
                zone.CalibrationFrameHeight = config.CalibrationFrameHeight;
                
                // 마지막에 IsEnabled 설정
                System.Diagnostics.Debug.WriteLine($"DatabaseService: About to set IsEnabled={config.IsEnabled} for zone {config.Name}");
                
                // 임시: 모든 구역을 활성화 상태로 강제 설정 (디버깅용)
                // zone.IsEnabled = true; // 이 줄의 주석을 해제하면 모든 구역이 활성화됩니다
                
                zone.IsEnabled = config.IsEnabled;
                
                System.Diagnostics.Debug.WriteLine($"DatabaseService: Loading zone {config.Name}, DB IsEnabled={config.IsEnabled}");
                System.Diagnostics.Debug.WriteLine($"DatabaseService: After zone creation, Zone IsEnabled={zone.IsEnabled}");
                
                // JSON에서 좌표 복원
                if (!string.IsNullOrEmpty(config.VerticesJson))
                {
                    try
                    {
                        var data = JsonSerializer.Deserialize<JsonElement>(config.VerticesJson);
                        
                        if (data.TryGetProperty("Height", out var heightElement))
                        {
                            zone.Height = heightElement.GetDouble();
                        }
                        
                        if (data.TryGetProperty("FloorPoints", out var pointsElement))
                        {
                            foreach (var pointElement in pointsElement.EnumerateArray())
                            {
                                if (pointElement.TryGetProperty("X", out var xElement) &&
                                    pointElement.TryGetProperty("Y", out var yElement))
                                {
                                    zone.FloorPoints.Add(new Point2D(
                                        xElement.GetDouble(),
                                        yElement.GetDouble()
                                    ));
                                }
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // JSON 파싱 실패 시 기본값 유지
                    }
                }
                
                // 로딩 완료, 이제 자동 저장 활성화
                zone.IsLoading = false;
                System.Diagnostics.Debug.WriteLine($"DatabaseService: Zone {config.Name} loading completed, IsLoading=false");
                
                zones.Add(zone);
            }
            
            return zones;
        }
        
        // 3D 구역 삭제
        public async Task DeleteZone3DConfigAsync(string zoneId)
        {
            using var context = new AppDbContext();
            
            var existing = await context.Zone3DConfigs
                .FirstOrDefaultAsync(z => z.ZoneId == zoneId);
                
            if (existing != null)
            {
                context.Zone3DConfigs.Remove(existing);
                await context.SaveChangesAsync();
            }
        }
        
        // 색상 변환 헬퍼 메소드
        private static string ColorToHex(Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        
        private static Color HexToColor(string hex)
        {
            if (string.IsNullOrEmpty(hex) || !hex.StartsWith("#") || hex.Length != 7)
                return Colors.Red; // 기본색
                
            try
            {
                var r = Convert.ToByte(hex.Substring(1, 2), 16);
                var g = Convert.ToByte(hex.Substring(3, 2), 16);
                var b = Convert.ToByte(hex.Substring(5, 2), 16);
                return Color.FromRgb(r, g, b);
            }
            catch
            {
                return Colors.Red; // 변환 실패 시 기본색
            }
        }
        
        // AI 모델 설정 저장
        public async Task SaveAIModelConfigsAsync(List<Database.AIModelConfig> models)
        {
            using var context = new AppDbContext();
            
            foreach (var model in models)
            {
                var existing = await context.AIModelConfigs
                    .FirstOrDefaultAsync(m => m.Id == model.Id);
                    
                if (existing != null)
                {
                    // 업데이트
                    existing.ModelName = model.ModelName;
                    existing.ModelVersion = model.ModelVersion;
                    existing.ModelPath = model.ModelPath;
                    existing.ModelType = model.ModelType;
                    existing.DefaultConfidence = model.DefaultConfidence;
                    existing.IsActive = model.IsActive;
                    existing.ConfigJson = model.ConfigJson;
                    existing.FileSize = model.FileSize;
                    existing.Description = model.Description;
                }
                else
                {
                    // 새로 추가
                    context.AIModelConfigs.Add(model);
                }
            }
            
            await context.SaveChangesAsync();
        }
        
        // AI 모델 설정 로드
        public async Task<List<Database.AIModelConfig>> LoadAIModelConfigsAsync()
        {
            using var context = new AppDbContext();
            return await context.AIModelConfigs.ToListAsync();
        }
        
        // 안전 이벤트 조회
        public async Task<List<SafetyEvent>> GetSafetyEventsAsync(DateTime startDate, int limit = 100)
        {
            using var context = new AppDbContext();
            
            return await context.SafetyEvents
                .Where(e => e.Timestamp >= startDate)
                .OrderByDescending(e => e.Timestamp)
                .Take(limit)
                .ToListAsync();
        }
    }
}