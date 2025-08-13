using System.Windows;
using Syncfusion.Windows.Shared;

namespace SafetyVisionMonitor.Views
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
        }
        
        public void UpdateStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;
            });
        }
        
        /// <summary>
        /// 로딩 완료 후 TopMost 해제
        /// </summary>
        public void DisableTopMost()
        {
            Dispatcher.Invoke(() =>
            {
                Topmost = false;
            });
        }
        
        /// <summary>
        /// 로딩 완료 표시 및 TopMost 해제
        /// </summary>
        public void SetLoadingComplete()
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "로딩 완료";
                LoadingProgress.IsIndeterminate = false;
                LoadingProgress.Value = 100;
                Topmost = false;
            });
        }
    }
}