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
                }
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
        }
    }
}
