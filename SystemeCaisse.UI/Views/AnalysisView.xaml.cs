using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using SystemeCaisse.UI;

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
                // v30: Simple status update. Global Software Rendering is now 
                // handle by App.xaml.cs to avoid transition deadlocks.
                vm.IsActive = (bool)e.NewValue;
            }
        }
    }
}
