using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionMonitor.Shared.Services
{
    /// <summary>
    /// Zone 데이터베이스 작업을 위한 인터페이스
    /// </summary>
    public interface IZoneDatabaseService
    {
        /// <summary>
        /// Zone 설정 저장
        /// </summary>
        Task SaveZone3DConfigsAsync(List<Zone3D> zones);
    }
}