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
                if ((bool)e.NewValue == true)
                {
                    RootGrid.Visibility = Visibility.Visible;
                    vm.IsActive = true;
                    
                    // On ajoute un petit délai pour assurer le rendu complet avant le chargement
                    await System.Threading.Tasks.Task.Delay(50);
                    
                    if (vm.LoadAnalysisCommand.CanExecute(null))
                    {
                        vm.LoadAnalysisCommand.Execute(null);
                    }
                }
                else
                {
                    vm.IsActive = false;
                    // Nuclear Stability Fix (Plan v24): Collapse the root grid to stop all rendering
                    RootGrid.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
        }
    }
}
