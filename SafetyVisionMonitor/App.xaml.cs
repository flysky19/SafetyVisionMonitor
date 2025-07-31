using System.Configuration;
using System.Data;
using System.Windows;
using Microsoft.Extensions.Configuration;
using SafetyVisionMonitor.Database;
using SafetyVisionMonitor.Services;
using SafetyVisionMonitor.ViewModels;
using SafetyVisionMonitor.Views;

namespace SafetyVisionMonitor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static IConfiguration Configuration { get; private set; } = null!;
    public static CameraService CameraService { get; private set; } = null!;
    public static DatabaseService DatabaseService { get; private set; } = null!;
    public static MonitoringService MonitoringService { get; private set; } = null!;
    public static ApplicationData AppData { get; private set; } = null!;
    
    protected override async void OnStartup(StartupEventArgs e)
    {
        // 스플래시 화면 표시 (선택사항)
        var splash = new SplashWindow();
        splash.Show();
        try
        {
            // 설정 파일 로드
            Configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();
                
            // DB 초기화
            splash.UpdateStatus("데이터베이스 초기화 중...");
            using (var context = new AppDbContext())
            {
                await context.Database.EnsureCreatedAsync();
            }
            
            // 서비스 초기화
            DatabaseService = new DatabaseService();
            CameraService = new CameraService();
            MonitoringService = new MonitoringService();
            
            // 전역 데이터 로드
            splash.UpdateStatus("데이터 로드 중...");
            AppData = new ApplicationData();
            await AppData.LoadAllDataAsync();
            
            Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjHTQxAR8/V1JEaF5cXmRCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdmWXhccnRVR2ddVU12XENWYEk=");
            
            // 모니터링 서비스 시작
            splash.UpdateStatus("모니터링 서비스 시작 중...");
            await MonitoringService.StartAsync();
            
            splash.UpdateStatus("시작 중...");
            await Task.Delay(500); // 잠시 대기
            
            // 로딩 완료 후 TopMost 해제
            splash.SetLoadingComplete();
            await Task.Delay(300); // 완료 메시지 표시 시간
        }
        catch (Exception ex)
        {
            splash.DisableTopMost(); // 오류 발생 시에도 TopMost 해제
            MessageBox.Show($"프로그램 초기화 중 오류 발생:\n{ex.Message}", 
                "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }
        finally
        {
            splash.Close();
        }
        
        base.OnStartup(e);
    }
    
    protected override async void OnExit(ExitEventArgs e)
    {
        // 모니터링 서비스 중지
        if (MonitoringService != null)
        {
            await MonitoringService.StopAsync();
            MonitoringService.Dispose();
        }
        
        // MainViewModel 정리 (모든 하위 ViewModel도 정리됨)
        if (MainWindow?.DataContext is MainViewModel mainViewModel)
        {
            mainViewModel.Cleanup();
        }
         
        // 서비스 정리
        CameraService?.Dispose();
        
        base.OnExit(e);
    }
}