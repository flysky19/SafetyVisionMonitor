using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafetyVisionMonitor.Shared.ViewModels.Base;

namespace SafetyVisionMonitor.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly Dictionary<string, BaseViewModel> _viewModels;
        private readonly DispatcherTimer _statusTimer;
        private BaseViewModel? _previousView;
        
        [ObservableProperty]
        private BaseViewModel? currentView;
        
        [ObservableProperty]
        private int connectedCamerasCount = 0;
        
        [ObservableProperty]
        private bool isAIModelLoaded = false;
        
        [ObservableProperty]
        private string statusMessage = "준비됨";
        
        [ObservableProperty]
        private double cpuUsage;
        
        [ObservableProperty]
        private double memoryUsage;
        
        [ObservableProperty]
        private string currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        
        // DashboardViewModel에 대한 직접 참조
        public DashboardViewModel? DashboardViewModel => 
            _viewModels.TryGetValue("Dashboard", out var vm) ? vm as DashboardViewModel : null;
        
        public MainViewModel()
        {
            // 모든 ViewModel을 미리 생성하여 저장
            _viewModels = new Dictionary<string, BaseViewModel>
            {
                 ["Dashboard"] = new DashboardViewModel(),
                 ["CameraManage"] = new CameraManageViewModel(),
                 ["AIModel"] = new AIModelViewModel(),
                 ["ModelConversion"] = new ModelConversionViewModel(),
                 ["ZoneSetup"] = new ZoneSetupViewModel(),
                 ["AcrylicSetup"] = new AcrylicSetupViewModel(),
                 ["History"] = new HistoryViewModel(),
                 //["EventLog"] = new EventLogViewModel(),
                 ["TrackingSetup"] = new TrackingSetupViewModel(),
            };
            
            // 기본 화면은 대시보드
            Navigate("Dashboard");
            
            // 상태 업데이트 타이머
            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _statusTimer.Tick += UpdateStatus;
            _statusTimer.Start();
        }
        
        [RelayCommand]
        private void Navigate(string viewName)
        {
            if (!_viewModels.ContainsKey(viewName))
            {
                StatusMessage = $"{viewName} 화면은 아직 준비 중입니다.";
                return;
            }
            
            // 이전 화면 비활성화
            if (CurrentView != null)
            {
                CurrentView.OnDeactivated();
                _previousView = CurrentView;
            }
            
            // 새 화면 활성화
            CurrentView = _viewModels[viewName];
            CurrentView.OnActivated();
            
            StatusMessage = $"{viewName} 화면으로 이동했습니다.";
        }
        
        [RelayCommand]
        private void OpenSettings()
        {
            StatusMessage = "설정 화면을 준비 중입니다...";
            // TODO: 설정 다이얼로그 열기
        }
        
        private void UpdateStatus(object? sender, EventArgs e)
        {
            CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            
            // 카메라 연결 상태 업데이트 (나중에 실제 서비스에서 가져옴)
            // ConnectedCamerasCount = App.CameraService.GetConnectedCameras().Count;
            
            // AI 모델 로드 상태 (나중에 실제 서비스에서 가져옴)
            // IsAIModelLoaded = App.DetectionService.IsModelLoaded;
            
            // CPU/메모리 사용량 (간단한 예시)
            UpdateSystemMetrics();
        }
        
        private void UpdateSystemMetrics()
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            MemoryUsage = (process.WorkingSet64 / 1024.0 / 1024.0); // MB
            
            // CPU는 복잡하므로 일단 더미 데이터
            CpuUsage = Random.Shared.Next(10, 40);
        }
        
        // 프로그램 종료 시 정리
        public void Cleanup()
        {
            _statusTimer?.Stop();
            
            // 모든 ViewModel 정리
            foreach (var viewModel in _viewModels.Values)
            {
                viewModel.Cleanup();
            }
        }
    }
}