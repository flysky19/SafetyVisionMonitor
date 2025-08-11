using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using SafetyVisionMonitor.ViewModels;
using Syncfusion.Windows.Shared;

namespace SafetyVisionMonitor;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : ChromelessWindow
{
    public static MainWindow? Instance { get; private set; }
    
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Instance = this; // 전역 인스턴스 저장
    }
    
    /// <summary>
    /// 안전 알림 표시
    /// </summary>
    public void ShowSafetyAlert(string title, string message, string alertLevel)
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                // 알림 레벨에 따른 스타일 설정
                SetAlertStyle(alertLevel);
                
                // 제목과 메시지 설정
                AlertTitle.Text = title;
                AlertMessage.Text = message;
                
                // 알림 표시
                AlertOverlay.Visibility = Visibility.Visible;
                
                System.Diagnostics.Debug.WriteLine($"MainWindow: Safety alert shown - {title}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainWindow: Alert display error - {ex.Message}");
            }
        });
    }
    
    /// <summary>
    /// 알림 레벨에 따른 스타일 적용
    /// </summary>
    private void SetAlertStyle(string alertLevel)
    {
        var (background, border, title) = alertLevel.ToLower() switch
        {
            "critical" => ("DarkRed", "Red", "🚨 긴급 위험 알림"),
            "high" => ("DarkOrange", "Orange", "⚠️ 높은 위험 알림"),
            "warning" => ("DarkGoldenrod", "Gold", "⚠️ 경고 알림"),
            _ => ("DarkBlue", "Blue", "ℹ️ 정보 알림")
        };
        
        AlertPanel.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(background)!);
        AlertPanel.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border)!);
        
        // 제목이 사용자 메시지에 포함되지 않은 경우에만 기본 제목 사용
        if (string.IsNullOrEmpty(AlertTitle.Text) || AlertTitle.Text == "🚨 긴급 위험 알림")
        {
            AlertTitle.Text = title;
        }
    }
    
    /// <summary>
    /// 알림 닫기 버튼 클릭 이벤트
    /// </summary>
    private void CloseAlert_Click(object sender, RoutedEventArgs e)
    {
        AlertOverlay.Visibility = Visibility.Collapsed;
        System.Diagnostics.Debug.WriteLine("MainWindow: Safety alert closed");
    }
    
    /// <summary>
    /// 자동 알림 닫기 (일정 시간 후)
    /// </summary>
    public void AutoCloseAlert(int delaySeconds = 10)
    {
        Task.Delay(TimeSpan.FromSeconds(delaySeconds)).ContinueWith(_ =>
        {
            Dispatcher.Invoke(() =>
            {
                if (AlertOverlay.Visibility == Visibility.Visible)
                {
                    AlertOverlay.Visibility = Visibility.Collapsed;
                    System.Diagnostics.Debug.WriteLine("MainWindow: Safety alert auto-closed");
                }
            });
        });
    }
}