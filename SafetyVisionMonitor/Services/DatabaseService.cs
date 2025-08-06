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
            try
            {
                // DB 초기화
                using var context = new AppDbContext();
                
                // 데이터베이스 생성 또는 업데이트
                context.Database.EnsureCreated();
                
                // 테이블 존재 확인 (디버깅용)
                var tables = context.Database.GetPendingMigrations();
                System.Diagnostics.Debug.WriteLine($"DatabaseService: Database initialized. Pending migrations: {tables.Count()}");
                
                // 각 테이블 확인
                try
                {
                    var safetyEventCount = context.SafetyEvents.Count();
                    System.Diagnostics.Debug.WriteLine($"SafetyEvents table exists with {safetyEventCount} records");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SafetyEvents table error: {ex.Message}");
                }
                
                try
                {
                    var aiModelCount = context.AIModelConfigs.Count();
                    System.Diagnostics.Debug.WriteLine($"AIModelConfigs table exists with {aiModelCount} records");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AIModelConfigs table error: {ex.Message}");
                    
                    // AIModelConfigs 테이블이 없으면 강제로 생성
                    try
                    {
                        context.Database.ExecuteSqlRaw(@"
                            CREATE TABLE IF NOT EXISTS AIModelConfigs (
                                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                ModelName TEXT NOT NULL,
                                ModelVersion TEXT NOT NULL,
                                ModelPath TEXT NOT NULL,
                                ModelType TEXT NOT NULL,
                                DefaultConfidence REAL NOT NULL,
                                ConfigJson TEXT NOT NULL,
                                IsActive INTEGER NOT NULL,
                                UploadedTime TEXT NOT NULL,
                                FileSize INTEGER NOT NULL,
                                Description TEXT
                            )
                        ");
                        System.Diagnostics.Debug.WriteLine("AIModelConfigs table created manually");
                    }
                    catch (Exception createEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to create AIModelConfigs table: {createEx.Message}");
                    }
                }
                
                // 추적 설정 테이블 확인 및 생성
                try
                {
                    var trackingConfigCount = context.TrackingConfigs.Count();
                    System.Diagnostics.Debug.WriteLine($"TrackingConfigs table exists with {trackingConfigCount} records");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"TrackingConfigs table error: {ex.Message}");
                    
                    // TrackingConfigs 테이블이 없으면 강제로 생성
                    try
                    {
                        context.Database.ExecuteSqlRaw(@"
                            CREATE TABLE IF NOT EXISTS TrackingConfigs (
                                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                IsEnabled INTEGER NOT NULL,
                                MaxTrackingDistance INTEGER NOT NULL,
                                MaxDisappearFrames INTEGER NOT NULL,
                                IouThreshold REAL NOT NULL,
                                SimilarityThreshold REAL NOT NULL,
                                EnableReIdentification INTEGER NOT NULL,
                                EnableMultiCameraTracking INTEGER NOT NULL,
                                TrackHistoryLength INTEGER NOT NULL,
                                ShowTrackingId INTEGER NOT NULL,
                                ShowTrackingPath INTEGER NOT NULL,
                                PathDisplayLength INTEGER NOT NULL,
                                AutoSaveTracking INTEGER NOT NULL,
                                AutoSaveInterval INTEGER NOT NULL,
                                TrackingMethod TEXT NOT NULL,
                                LastModified TEXT NOT NULL
                            )
                        ");
                        System.Diagnostics.Debug.WriteLine("TrackingConfigs table created manually");
                    }
                    catch (Exception createEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to create TrackingConfigs table: {createEx.Message}");
                    }
                }
                
                // 추적 구역 테이블 확인 및 생성
                try
                {
                    var trackingZoneCount = context.TrackingZoneConfigs.Count();
                    System.Diagnostics.Debug.WriteLine($"TrackingZoneConfigs table exists with {trackingZoneCount} records");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"TrackingZoneConfigs table error: {ex.Message}");
                    
                    // TrackingZoneConfigs 테이블이 없으면 강제로 생성
                    try
                    {
                        context.Database.ExecuteSqlRaw(@"
                            CREATE TABLE IF NOT EXISTS TrackingZoneConfigs (
                                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                ZoneId TEXT NOT NULL,
                                Name TEXT NOT NULL,
                                IsEntryZone INTEGER NOT NULL,
                                IsExitZone INTEGER NOT NULL,
                                CountingEnabled INTEGER NOT NULL,
                                PolygonJson TEXT NOT NULL,
                                CameraId TEXT NOT NULL,
                                CreatedTime TEXT NOT NULL
                            )
                        ");
                        System.Diagnostics.Debug.WriteLine("TrackingZoneConfigs table created manually");
                    }
                    catch (Exception createEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to create TrackingZoneConfigs table: {createEx.Message}");
                    }
                }
                
                // 화질 설정 컬럼 마이그레이션
                MigrateCameraQualitySettings(context);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DatabaseService initialization error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
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
                // 마이그레이션 실패는 로그만 남기고 계속 진행 (데이터 보존)
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
            try
            {
                using var context = new AppDbContext();
                
                // 테이블 존재 확인 및 생성 (기존 데이터 유지)
                await context.Database.EnsureCreatedAsync();
                
                // 데이터 로드
                var configs = await context.AIModelConfigs.ToListAsync();
                
                System.Diagnostics.Debug.WriteLine($"LoadAIModelConfigsAsync: Loaded {configs.Count} AI model configs");
                
                return configs;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadAIModelConfigsAsync Error: {ex.GetType().Name} - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                
                // 오류 발생 시 빈 리스트 반환 (데이터 삭제 없음)
                return new List<Database.AIModelConfig>();
            }
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
        
        // 추적 설정 저장
        public async Task SaveTrackingConfigAsync(TrackingConfig config)
        {
            using var context = new AppDbContext();
            
            try
            {
                var existing = await context.TrackingConfigs.FirstOrDefaultAsync();
                
                if (existing != null)
                {
                    // 기존 설정 업데이트
                    existing.IsEnabled = config.IsEnabled;
                    existing.MaxTrackingDistance = config.MaxTrackingDistance;
                    existing.MaxDisappearFrames = config.MaxDisappearFrames;
                    existing.IouThreshold = config.IouThreshold;
                    existing.SimilarityThreshold = config.SimilarityThreshold;
                    existing.EnableReIdentification = config.EnableReIdentification;
                    existing.EnableMultiCameraTracking = config.EnableMultiCameraTracking;
                    existing.TrackHistoryLength = config.TrackHistoryLength;
                    existing.ShowTrackingId = config.ShowTrackingId;
                    existing.ShowTrackingPath = config.ShowTrackingPath;
                    existing.PathDisplayLength = config.PathDisplayLength;
                    existing.AutoSaveTracking = config.AutoSaveTracking;
                    existing.AutoSaveInterval = config.AutoSaveInterval;
                    existing.TrackingMethod = config.TrackingMethod;
                    existing.LastModified = DateTime.Now;
                }
                else
                {
                    // 새 설정 추가
                    context.TrackingConfigs.Add(config);
                }
                
                await context.SaveChangesAsync();
                System.Diagnostics.Debug.WriteLine("Tracking config saved successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save tracking config: {ex.Message}");
                throw;
            }
        }
        
        // 추적 설정 로드
        public async Task<TrackingConfig?> LoadTrackingConfigAsync()
        {
            try
            {
                using var context = new AppDbContext();
                await context.Database.EnsureCreatedAsync();
                
                var config = await context.TrackingConfigs.FirstOrDefaultAsync();
                System.Diagnostics.Debug.WriteLine($"Loaded tracking config: {config?.TrackingMethod ?? "null"}");
                
                return config;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load tracking config: {ex.Message}");
                return null;
            }
        }
        
        // 추적 구역 저장
        public async Task SaveTrackingZonesAsync(List<TrackingZoneConfig> zones)
        {
            using var context = new AppDbContext();
            
            try
            {
                // 기존 구역들 삭제
                var existingZones = await context.TrackingZoneConfigs.ToListAsync();
                context.TrackingZoneConfigs.RemoveRange(existingZones);
                
                // 새 구역들 추가
                foreach (var zone in zones)
                {
                    context.TrackingZoneConfigs.Add(zone);
                }
                
                await context.SaveChangesAsync();
                System.Diagnostics.Debug.WriteLine($"Saved {zones.Count} tracking zones");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save tracking zones: {ex.Message}");
                throw;
            }
        }
        
        // 추적 구역 로드
        public async Task<List<TrackingZoneConfig>> LoadTrackingZonesAsync()
        {
            try
            {
                using var context = new AppDbContext();
                await context.Database.EnsureCreatedAsync();
                
                var zones = await context.TrackingZoneConfigs.ToListAsync();
                System.Diagnostics.Debug.WriteLine($"Loaded {zones.Count} tracking zones");
                
                return zones;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load tracking zones: {ex.Message}");
                return new List<TrackingZoneConfig>();
            }
        }
        
        // 추적 구역 삭제
        public async Task DeleteTrackingZoneAsync(string zoneId)
        {
            using var context = new AppDbContext();
            
            try
            {
                var zone = await context.TrackingZoneConfigs
                    .FirstOrDefaultAsync(z => z.ZoneId == zoneId);
                    
                if (zone != null)
                {
                    context.TrackingZoneConfigs.Remove(zone);
                    await context.SaveChangesAsync();
                    System.Diagnostics.Debug.WriteLine($"Deleted tracking zone: {zoneId}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to delete tracking zone {zoneId}: {ex.Message}");
                throw;
            }
        }
    }
}