using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.UI.ViewModels;

namespace SystemeCaisse.UI.Views
{
    public partial class SalesView : System.Windows.Controls.UserControl
    {
        public SalesView()
        {
            InitializeComponent();
        }

        private void searchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            if (e.Key == Key.Down && vm.IsSearchDropDownOpen && suggestionsListBox.Items.Count > 0)
            {
                int next = suggestionsListBox.SelectedIndex + 1;
                if (next >= suggestionsListBox.Items.Count) next = 0;
                suggestionsListBox.SelectedIndex = next;
                suggestionsListBox.ScrollIntoView(suggestionsListBox.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Up && vm.IsSearchDropDownOpen && suggestionsListBox.Items.Count > 0)
            {
                int prev = suggestionsListBox.SelectedIndex - 1;
                if (prev < 0) prev = suggestionsListBox.Items.Count - 1;
                suggestionsListBox.SelectedIndex = prev;
                suggestionsListBox.ScrollIntoView(suggestionsListBox.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (vm.IsSearchDropDownOpen && suggestionsListBox.SelectedItem is Produit selectedProd)
                {
                    suggestionsListBox.SelectedIndex = -1;
                    vm.SelectedSearchProduct = selectedProd;
                }
                else if (vm.ScanCommand.CanExecute(null))
                {
                    vm.ScanCommand.Execute(null);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                suggestionsListBox.SelectedIndex = -1;
                vm.IsSearchDropDownOpen = false;
                e.Handled = true;
            }
        }

        private void suggestionsListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not ListBoxItem)
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep is ListBoxItem item && item.DataContext is Produit prod)
            {
                suggestionsListBox.SelectedIndex = -1;
                vm.SelectedSearchProduct = prod;
                searchTextBox.Focus();
                e.Handled = true;
            }
        }

        private void searchTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (DataContext is MainViewModel vm && !searchPopup.IsMouseOver)
            {
                suggestionsListBox.SelectedIndex = -1;
                vm.IsSearchDropDownOpen = false;
            }
        }
    }
}
