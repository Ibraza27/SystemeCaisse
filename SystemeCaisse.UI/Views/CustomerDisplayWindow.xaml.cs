using System.Windows;
using SystemeCaisse.UI.ViewModels;

namespace SystemeCaisse.UI.Views
{
    public partial class CustomerDisplayWindow : Window
    {
        public CustomerDisplayWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // Auto-scroll when the cart changes
            if (viewModel.LignesVente is System.Collections.Specialized.INotifyCollectionChanged collection)
            {
                collection.CollectionChanged += (s, e) =>
                {
                    if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                    {
                        _ = Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
                        {
                            if (CartListView.Items.Count > 0)
                            {
                                CartListView.ScrollIntoView(CartListView.Items[CartListView.Items.Count - 1]);
                            }
                            if (CartListViewCompact.Items.Count > 0)
                            {
                                CartListViewCompact.ScrollIntoView(CartListViewCompact.Items[CartListViewCompact.Items.Count - 1]);
                            }
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                };
            }
        }
    }
}
