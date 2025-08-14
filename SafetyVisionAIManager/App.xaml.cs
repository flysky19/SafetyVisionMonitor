using System.Windows;
using Syncfusion.SfSkinManager;
using SafetyVisionMonitor.Shared.Services;
using SafetyVisionMonitor.Shared.Models;

namespace SafetyVisionAIManager
{
    public partial class App : Application
    {
        public static DatabaseService DatabaseService { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Syncfusion 테마 설정
            SfSkinManager.SetTheme(this, new Theme("FluentDark"));

            // 데이터베이스 서비스 초기화
            DatabaseService = new DatabaseService();
            
            // Zone3D 서비스 의존성 주입 (알림 서비스는 불필요)
            Zone3D.DatabaseService = DatabaseService;

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
        }
    }
}