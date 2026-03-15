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

namespace SystemeCaisse.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(ViewModels.MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        TicketPlaceholder.Content = new Views.TicketView();
    }

    private int _lastTabIndex = -1;

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Only handle selection changes for the main tab control itself
        if (e.OriginalSource != mainTabs) return;

        if (DataContext is ViewModels.MainViewModel vm)
        {
            int currentIndex = mainTabs.SelectedIndex;
            int previousIndex = _lastTabIndex;

            if (currentIndex != previousIndex)
            {
                _lastTabIndex = currentIndex;

                // v28: DEFER tab-specific logic to ApplicationIdle priority.
                // This is the "Magic Bullet" for stability - it wait until WPF is 100% idle
                // after the view swap before doing any work.
                Dispatcher.BeginInvoke(new Action(() => 
                {
                    try 
                    {
                        // 1. CLEANUP: If we left Analysis (5)
                        if (previousIndex == 5)
                        {
                            vm.AnalysisVM.Cleanup();
                        }

                        // 2. LOAD: If we entered Analysis (5)
                        if (currentIndex == 5)
                        {
                            _ = vm.AnalysisVM.LoadAnalysis();
                        }

                        // 3. PROMOTIONS: If we entered Caisse (1)
                        if (currentIndex == 1)
                        {
                            vm.RefreshPromotions();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Tab Transition Error: {ex.Message}");
                    }
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }
    }
}