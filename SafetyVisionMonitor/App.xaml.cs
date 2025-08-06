using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using SafetyVisionMonitor.Database;
using SafetyVisionMonitor.Services;
using SafetyVisionMonitor.ViewModels;
using SafetyVisionMonitor.Views;
using System.Runtime.InteropServices;

namespace SafetyVisionMonitor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);
    

    public static IConfiguration Configuration { get; private set; } = null!;
    public static CameraService CameraService { get; private set; } = null!;
    public static DatabaseService DatabaseService { get; private set; } = null!;
    public static MonitoringService MonitoringService { get; private set; } = null!;
    public static ApplicationData AppData { get; private set; } = null!;

    // AI 서비스
    public static AIInferenceService AIInferenceService { get; private set; } = null!;
    public static AIProcessingPipeline AIPipeline { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        ConfigureCudaEnvironment();

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

            // AI 서비스 초기화 (CameraService보다 먼저)
            splash.UpdateStatus("AI 서비스 초기화 중...");
            AIInferenceService = new AIInferenceService();
            AIPipeline = new AIProcessingPipeline(AIInferenceService);

            // CameraService 초기화 (AIPipeline 이후)
            CameraService = new CameraService();

            MonitoringService = new MonitoringService();

            // 전역 데이터 로드
            splash.UpdateStatus("데이터 로드 중...");
            AppData = new ApplicationData();
            await AppData.LoadAllDataAsync();

            Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(
                "Ngo9BigBOggjHTQxAR8/V1JEaF5cXmRCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdmWXhccnRVR2ddVU12XENWYEk=");

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

    private void ConfigureCudaEnvironment()
    {
        try
        {
            // CUDA 12 경로 찾기 (12.6, 12.2 등 설치된 버전)
            string? cudaPath = null;
            var cudaVersions = new[] { "v12.6", "v12.5", "v12.4", "v12.3", "v12.2", "v12.1", "v12.0" };
                
            foreach (var version in cudaVersions)
            {
                var path = $@"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\{version}\bin";
                if (Directory.Exists(path))
                {
                    cudaPath = path;
                    System.Diagnostics.Debug.WriteLine($"Found CUDA {version} at: {path}");
                    break;
                }
            }

            if (cudaPath != null)
            {
                // DLL 검색 경로 설정
                SetDllDirectory(cudaPath);
                    
                // PATH 환경 변수에 추가
                var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                Environment.SetEnvironmentVariable("PATH", $"{cudaPath};{currentPath}");
                    
                System.Diagnostics.Debug.WriteLine($"CUDA environment configured: {cudaPath}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("CUDA 12.x not found");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error configuring CUDA: {ex.Message}");
        }
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
         
        // AI 서비스 정리
        if (AIPipeline != null)
        {
            await AIPipeline.StopAsync();
            AIPipeline.Dispose();
        }
        AIInferenceService?.Dispose();
        
        // 서비스 정리
        CameraService?.Dispose();
        
        base.OnExit(e);
    }
}