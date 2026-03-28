using System.Windows;
using SystemeCaisse.UI.ViewModels;

namespace SystemeCaisse.UI.Views
{
    public partial class DiscountWindow : System.Windows.Window
    {
        public DiscountWindow(DiscountViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.CloseAction = new System.Action(() => this.Close());
        }
    }
}
