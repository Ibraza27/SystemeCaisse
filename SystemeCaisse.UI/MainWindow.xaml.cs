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
using Window = System.Windows.Window;
using UserControl = System.Windows.Controls.UserControl;

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
        Loaded += (s, e) => viewModel.InitializeCustomerDisplay();
    }

    public void ReinitializeCustomerDisplay()
    {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.InitializeCustomerDisplay();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Application.Current.Shutdown();
    }
}
