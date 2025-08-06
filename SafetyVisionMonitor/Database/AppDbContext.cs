using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using SafetyVisionMonitor.Models;

namespace SafetyVisionMonitor.Database
{
    public class AppDbContext : DbContext
    {
        public DbSet<SafetyEvent> SafetyEvents { get; set; } = null!;
        public DbSet<CameraConfig> CameraConfigs { get; set; } = null!;
        public DbSet<Zone3DConfig> Zone3DConfigs { get; set; } = null!;
        public DbSet<PersonTrackingRecord> PersonTrackingRecords { get; set; } = null!;
        public DbSet<AIModelConfig> AIModelConfigs { get; set; } = null!;
        public DbSet<TrackingConfig> TrackingConfigs { get; set; } = null!;
        public DbSet<TrackingZoneConfig> TrackingZoneConfigs { get; set; } = null!;
        
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SafetyVisionMonitor",
                "safety_monitor.db"
            );
            
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // SafetyEvent 인덱스 설정
            modelBuilder.Entity<SafetyEvent>()
                .HasIndex(e => e.Timestamp)
                .HasDatabaseName("IX_SafetyEvent_Timestamp");
                
            modelBuilder.Entity<SafetyEvent>()
                .HasIndex(e => e.EventType)
                .HasDatabaseName("IX_SafetyEvent_EventType");
                
            modelBuilder.Entity<SafetyEvent>()
                .HasIndex(e => e.CameraId)
                .HasDatabaseName("IX_SafetyEvent_CameraId");
                
            // PersonTrackingRecord 인덱스
            modelBuilder.Entity<PersonTrackingRecord>()
                .HasIndex(e => e.GlobalTrackingId)
                .HasDatabaseName("IX_PersonTracking_GlobalId");
                
            modelBuilder.Entity<PersonTrackingRecord>()
                .HasIndex(e => e.FirstDetectedTime)
                .HasDatabaseName("IX_PersonTracking_FirstDetected");
        }
    }
    
    // 카메라 설정 저장용
    public class CameraConfig
    {
        public int Id { get; set; }
        public string CameraId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ConnectionString { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public double Fps { get; set; }
        public bool IsEnabled { get; set; } = true;
        public DateTime LastModified { get; set; }
        
        // 캘리브레이션 정보
        public double CalibrationPixelsPerMeter { get; set; } = 100.0;
        public bool IsCalibrated { get; set; } = false;
        
        // 화질 설정
        public double Brightness { get; set; } = 128.0;
        public double Contrast { get; set; } = 32.0;
        public double Saturation { get; set; } = 64.0;
        public double Exposure { get; set; } = -1.0;
        public double Gain { get; set; } = 0.0;
        public double Hue { get; set; } = 0.0;
        public double Gamma { get; set; } = 1.0;
        public double Sharpness { get; set; } = 0.0;
        public bool AutoExposure { get; set; } = true;
        public bool AutoWhiteBalance { get; set; } = true;
    }
    
    // 3D 구역 설정 저장용
    public class Zone3DConfig
    {
        public int Id { get; set; }
        public string ZoneId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string CameraId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // Warning, Danger
        public string VerticesJson { get; set; } = string.Empty; // 3D 좌표 JSON
        public string Color { get; set; } = string.Empty;
        public double Opacity { get; set; }
        public bool IsEnabled { get; set; }
        public DateTime CreatedTime { get; set; }
        
        // 캘리브레이션 정보
        public double CalibrationPixelsPerMeter { get; set; } = 100.0;
        public double CalibrationFrameWidth { get; set; } = 640.0;
        public double CalibrationFrameHeight { get; set; } = 480.0;
    }
    
    // 사람 추적 기록
    public class PersonTrackingRecord
    {
        public int Id { get; set; }
        public string GlobalTrackingId { get; set; } = string.Empty;
        public DateTime FirstDetectedTime { get; set; }
        public DateTime LastSeenTime { get; set; }
        public string CameraHistory { get; set; } = string.Empty; // JSON 배열
        public int TotalDetectionCount { get; set; }
        public string EventSummary { get; set; } = string.Empty; // JSON
    }
    
    // AI 모델 설정
    public class AIModelConfig
    {
        public int Id { get; set; }
        public string ModelName { get; set; } = string.Empty;
        public string ModelVersion { get; set; } = string.Empty;
        public string ModelPath { get; set; } = string.Empty;
        public string ModelType { get; set; } = string.Empty; // YOLO, etc
        public double DefaultConfidence { get; set; } = 0.7;
        public string ConfigJson { get; set; } = string.Empty;
        public bool IsActive { get; set; } = false;
        public DateTime UploadedTime { get; set; } = DateTime.Now;
        public long FileSize { get; set; } = 0;
        public string? Description { get; set; }
    }
    
    // 추적 설정
    public class TrackingConfig
    {
        public int Id { get; set; }
        public bool IsEnabled { get; set; } = true;
        public int MaxTrackingDistance { get; set; } = 50;
        public int MaxDisappearFrames { get; set; } = 30;
        public double IouThreshold { get; set; } = 0.3;
        public double SimilarityThreshold { get; set; } = 0.7;
        public bool EnableReIdentification { get; set; } = true;
        public bool EnableMultiCameraTracking { get; set; } = true;
        public int TrackHistoryLength { get; set; } = 50;
        public bool ShowTrackingId { get; set; } = true;
        public bool ShowTrackingPath { get; set; } = true;
        public int PathDisplayLength { get; set; } = 20;
        public bool AutoSaveTracking { get; set; } = true;
        public int AutoSaveInterval { get; set; } = 60;
        public string TrackingMethod { get; set; } = "SORT";
        public DateTime LastModified { get; set; } = DateTime.Now;
    }
    
    // 추적 구역 설정
    public class TrackingZoneConfig
    {
        public int Id { get; set; }
        public string ZoneId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsEntryZone { get; set; }
        public bool IsExitZone { get; set; }
        public bool CountingEnabled { get; set; }
        public string PolygonJson { get; set; } = string.Empty; // 구역 좌표 JSON
        public string CameraId { get; set; } = string.Empty;
        public DateTime CreatedTime { get; set; } = DateTime.Now;
    }
}