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
    public static EnhancedAIProcessingPipeline AIPipeline { get; private set; } = null!;
    
    // 백그라운드 추적 서비스 (MonitoringService를 통해 접근)
    public static BackgroundTrackingService TrackingService => MonitoringService.GetTrackingService();

    // 새로운 기능 관리 시스템
    public static Services.Features.FeatureManager FeatureManager { get; private set; } = null!;
    public static Services.Features.OverlayRenderingPipeline OverlayPipeline { get; private set; } = null!;

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
            splash.UpdateStatus("스마트 AI 서비스 초기화 중...");
            AIInferenceService = new AIInferenceService();
            AIPipeline = new EnhancedAIProcessingPipeline(AIInferenceService);

            // YOLOv8 멀티태스크 엔진 초기화 - 일시적으로 비활성화
            splash.UpdateStatus("AI 모델 초기화 중...");
            
            // AccessViolationException 문제로 인해 멀티태스크 엔진 사용 중단
            System.Diagnostics.Debug.WriteLine("App: 멀티태스크 엔진 비활성화 - 단일 엔진 모드로 시작");
            splash.UpdateStatus("단일 AI 엔진 모드로 시작");
            
            // 검출 표시는 사용자가 체크박스로 제어
            System.Diagnostics.Debug.WriteLine("App: 객체 검출 표시는 사용자 설정에 따라 제어됨");
            
            // 기본 YOLOv8 모델 자동 로드 시도
            try
            {
                var defaultModelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "yolov8s.onnx");
                if (File.Exists(defaultModelPath))
                {
                    var aiModel = new AIModel
                    {
                        Id = "default_yolov8",
                        Name = "YOLOv8s (기본)",
                        ModelPath = defaultModelPath,
                        Type = ModelType.YOLOv8,
                        Confidence = 0.7f,
                        IsActive = true
                    };
                    
                    var success = await AIInferenceService.LoadModelAsync(aiModel);
                    if (success)
                    {
                        splash.UpdateStatus("기본 AI 모델 로드 완료");
                        System.Diagnostics.Debug.WriteLine("App: 기본 YOLOv8 모델 로드 성공");
                    }
                    else
                    {
                        splash.UpdateStatus("AI 모델 수동 로드 필요");
                        System.Diagnostics.Debug.WriteLine("App: 기본 모델 로드 실패");
                    }
                }
            }
            catch (Exception modelEx)
            {
                System.Diagnostics.Debug.WriteLine($"App: 기본 모델 로드 오류: {modelEx.Message}");
            }

            // CameraService 초기화 (AIPipeline 이후)
            CameraService = new CameraService();

            MonitoringService = new MonitoringService();

            // 새로운 기능 관리 시스템 초기화
            splash.UpdateStatus("기능 관리 시스템 초기화 중...");
            FeatureManager = Services.Features.FeatureManager.Instance;
            OverlayPipeline = new Services.Features.OverlayRenderingPipeline(FeatureManager);

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
            
            // 모든 서비스 초기화 완료 후 MainWindow 생성
            splash.UpdateStatus("메인 화면 생성 중...");
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
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
            // OpenCV 로그 레벨을 FATAL로 설정하여 YUV420p 경고 메시지 숨기기
            OpenCvSharp.Cv2.SetLogLevel(OpenCvSharp.LogLevel.FATAL);
            
            // OpenCV 백엔드 환경 변수 설정 - YUV420p 문제 해결
            Environment.SetEnvironmentVariable("OPENCV_FFMPEG_CAPTURE_OPTIONS", "rtsp_transport;udp");
            Environment.SetEnvironmentVariable("OPENCV_VIDEOIO_PRIORITY_MSMF", "0"); // MSMF 우선순위 낮춤
            Environment.SetEnvironmentVariable("OPENCV_VIDEOIO_PRIORITY_DSHOW", "1000"); // DirectShow 우선순위 높임
            
            // FFmpeg 백엔드 YUV420p 처리 개선
            Environment.SetEnvironmentVariable("OPENCV_FFMPEG_LOGLEVEL", "-8"); // AV_LOG_QUIET
            Environment.SetEnvironmentVariable("OPENCV_VIDEOIO_PRIORITY_FFMPEG", "500"); // FFmpeg 우선순위 중간
            Environment.SetEnvironmentVariable("OPENCV_FFMPEG_CAPTURE_OPTIONS", "pixel_format;bgr24"); // BGR24 강제
            
            // FFmpeg 로깅 완전 비활성화
            Environment.SetEnvironmentVariable("AV_LOG_FORCE_NOCOLOR", "1");
            Environment.SetEnvironmentVariable("AV_LOG_FORCE_COLOR", "0");
            
            System.Diagnostics.Debug.WriteLine("OpenCV environment configured successfully (YUV420p warnings suppressed)");
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