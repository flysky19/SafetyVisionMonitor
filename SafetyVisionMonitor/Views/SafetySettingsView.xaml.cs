using System.Windows.Controls;
using SafetyVisionMonitor.ViewModels;

namespace SafetyVisionMonitor.Views
{
    public partial class SafetySettingsView : UserControl
    {
        public SafetySettingsView()
        {
            InitializeComponent();
            DataContext = new SafetySettingsViewModel();
        }
    }
}