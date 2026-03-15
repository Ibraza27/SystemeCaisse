using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;

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
                bool isVisible = (bool)e.NewValue;
                vm.IsActive = isVisible;

                if (isVisible)
                {
                    // v29: Force Software Rendering globally when Analysis is active to prevent GPU deadlocks.
                    RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                }
                else
                {
                    // Restore to Default when leaving the tab
                    RenderOptions.ProcessRenderMode = RenderMode.Default;
                }
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
        }
    }
}
