using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SafetyVisionMonitor.Database;
using SafetyVisionMonitor.Models;

namespace SafetyVisionMonitor.Services
{
    public class DatabaseService
    {
        public DatabaseService()
        {
            // DB 초기화
            using var context = new AppDbContext();
            context.Database.EnsureCreated();
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
            
            // 기존 설정 삭제
            context.CameraConfigs.RemoveRange(context.CameraConfigs);
            
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
                    LastModified = DateTime.Now
                });
            }
            
            await context.SaveChangesAsync();
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
                Fps = config.Fps
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
    }
}