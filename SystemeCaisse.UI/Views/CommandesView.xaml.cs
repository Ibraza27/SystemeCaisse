using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.UI.ViewModels;

namespace SystemeCaisse.UI.Views
{
    public partial class CommandesView : System.Windows.Controls.UserControl
    {
        public CommandesView()
        {
            InitializeComponent();
        }

        private void searchTextBoxCmd_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
            {
                Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    searchTextBoxCmd.Focus();
                    Keyboard.Focus(searchTextBoxCmd);
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private void searchTextBoxCmd_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not CommandesViewModel vm) return;

            if (e.Key == Key.Down && vm.IsSearchDropDownOpen && suggestionsListBoxCmd.Items.Count > 0)
            {
                int next = suggestionsListBoxCmd.SelectedIndex + 1;
                if (next >= suggestionsListBoxCmd.Items.Count) next = 0;
                suggestionsListBoxCmd.SelectedIndex = next;
                suggestionsListBoxCmd.ScrollIntoView(suggestionsListBoxCmd.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Up && vm.IsSearchDropDownOpen && suggestionsListBoxCmd.Items.Count > 0)
            {
                int prev = suggestionsListBoxCmd.SelectedIndex - 1;
                if (prev < 0) prev = suggestionsListBoxCmd.Items.Count - 1;
                suggestionsListBoxCmd.SelectedIndex = prev;
                suggestionsListBoxCmd.ScrollIntoView(suggestionsListBoxCmd.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (vm.IsSearchDropDownOpen && suggestionsListBoxCmd.SelectedItem is Produit selectedProd)
                {
                    suggestionsListBoxCmd.SelectedIndex = -1;
                    vm.SelectedSearchProduct = selectedProd;
                }
                else if (vm.ScanNewCommandeCommand.CanExecute(null))
                {
                    vm.ScanNewCommandeCommand.Execute(null);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                suggestionsListBoxCmd.SelectedIndex = -1;
                vm.IsSearchDropDownOpen = false;
                e.Handled = true;
            }
        }

        private void suggestionsListBoxCmd_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not CommandesViewModel vm) return;

            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not ListBoxItem)
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep is ListBoxItem item && item.DataContext is Produit prod)
            {
                suggestionsListBoxCmd.SelectedIndex = -1;
                vm.SelectedSearchProduct = prod;
                searchTextBoxCmd.Focus();
                e.Handled = true;
            }
        }

        private void searchTextBoxCmd_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (DataContext is CommandesViewModel vm && !searchPopupCmd.IsMouseOver)
            {
                suggestionsListBoxCmd.SelectedIndex = -1;
                vm.IsSearchDropDownOpen = false;
            }
        }

        private void SelectAllCheckBox_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is CommandesViewModel vm && sender is CheckBox cb)
            {
                bool isChecked = cb.IsChecked == true;
                foreach (var c in vm.Commandes)
                    c.IsSelected = isChecked;
            }
        }
    }
}
