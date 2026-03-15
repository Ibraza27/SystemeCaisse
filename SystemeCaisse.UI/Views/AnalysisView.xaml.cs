using System.Windows;
using System.Windows.Controls;

namespace SystemeCaisse.UI.Views
{
    public partial class AnalysisView : UserControl
    {
        public AnalysisView()
        {
            InitializeComponent();
        }

        private async void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (DataContext is ViewModels.AnalysisViewModel vm)
            {
                // v27: View is destroyed/recreated by MainWindow via DataTrigger.
                // We just need to update IsActive to signal the VM.
                vm.IsActive = (bool)e.NewValue;
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
        }
    }
}
