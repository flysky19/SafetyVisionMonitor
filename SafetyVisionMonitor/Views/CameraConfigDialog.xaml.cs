using System.Windows;
using SafetyVisionMonitor.ViewModels;

namespace SafetyVisionMonitor.Views
{
    public partial class CameraConfigDialog : Window
    {
        public CameraConfigDialogViewModel ViewModel { get; }
        
        public CameraConfigDialog(Models.Camera camera)
        {
            InitializeComponent();
            ViewModel = new CameraConfigDialogViewModel(camera);
            DataContext = ViewModel;
        }
        
        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
        
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}