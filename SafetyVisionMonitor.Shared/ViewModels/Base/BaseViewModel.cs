using CommunityToolkit.Mvvm.ComponentModel;

namespace SafetyVisionMonitor.Shared.ViewModels.Base
{
    public abstract class BaseViewModel : ObservableObject
    {
        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }
        
        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }
        
        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }
        
        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }
        
        // 뷰가 처음 로드될 때 한 번만 호출
        public virtual void OnLoaded()
        {
        }
        
        // 뷰가 활성화될 때마다 호출 (화면 전환 시)
        public virtual void OnActivated()
        {
            IsActive = true;
        }
        
        // 뷰가 비활성화될 때마다 호출 (다른 화면으로 전환 시)
        public virtual void OnDeactivated()
        {
            IsActive = false;
        }
        
        // 프로그램 종료 시 호출 (리소스 정리)
        public virtual void Cleanup()
        {
            OnDeactivated();
        }
    }
}