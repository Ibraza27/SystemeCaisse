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

        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (DataContext is ViewModels.AnalysisViewModel vm)
            {
                // v29: Simple activity signal. Software rendering prevents GPU deadlocks.
                vm.IsActive = (bool)e.NewValue;
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
        }
    }
}
