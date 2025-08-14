using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using SafetyVisionMonitor.Shared.Database;
using SafetyVisionMonitor.Services;
using SafetyVisionMonitor.Shared.Services;
using DatabaseServiceShared = SafetyVisionMonitor.Shared.Services.DatabaseService;
using SafetyVisionMonitor.ViewModels;
using SafetyVisionMonitor.Views;
using SafetyVisionMonitor.Shared.Models;
using System.Runtime.InteropServices;

namespace SafetyVisionMonitor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);
    
    public static IConfiguration Configuration { get; private set; } = null!;
    public static CameraService CameraService { get; private set; } = null!;
    public static DatabaseServiceShared DatabaseService { get; private set; } = null!;
    public static MonitoringService MonitoringService { get; private set; } = null!;
    public static ApplicationData AppData { get; private set; } = null!;

    // AI 서비스
    public static AIInferenceService AIInferenceService { get; private set; } = null!;
    public static AIProcessingPipeline AIPipeline { get; private set; } = null!;
    
    // 백그라운드 추적 서비스 (MonitoringService를 통해 접근)
    public static BackgroundTrackingService TrackingService => MonitoringService.GetTrackingService();

    protected override async void OnStartup(StartupEventArgs e)
    {
        ConfigureCudaEnvironment();
        ConfigureOpenCVEnvironment();

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
            DatabaseService = new DatabaseServiceShared();
            
            // Zone3D 서비스 의존성 주입
            Zone3D.DatabaseService = DatabaseService as IZoneDatabaseService;
            Zone3D.NotificationService = new ZoneNotificationService();

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

            // 백그라운드 모니터링 및 추적 서비스 시작
            splash.UpdateStatus("백그라운드 서비스 시작 중...");
            await MonitoringService.StartAsync(); // 추적 서비스도 함께 시작됨

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

    private void ConfigureOpenCVEnvironment()
    {
        try
        {
            // OpenCV 로그 레벨을 Error로 설정하여 불필요한 경고 메시지 줄이기
            OpenCvSharp.Cv2.SetLogLevel(OpenCvSharp.LogLevel.ERROR);
            
            // OpenCV 백엔드 환경 변수 설정
            Environment.SetEnvironmentVariable("OPENCV_FFMPEG_CAPTURE_OPTIONS", "rtsp_transport;udp");
            Environment.SetEnvironmentVariable("OPENCV_VIDEOIO_PRIORITY_MSMF", "0"); // MSMF 우선순위 낮춤
            Environment.SetEnvironmentVariable("OPENCV_VIDEOIO_PRIORITY_DSHOW", "1000"); // DirectShow 우선순위 높임
            
            System.Diagnostics.Debug.WriteLine("OpenCV environment configured successfully");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error configuring OpenCV environment: {ex.Message}");
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