using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SystemeCaisse.UI.Services;

namespace SystemeCaisse.UI.Views
{
    public partial class VilleCPFilterWindow : Window
    {
        public ObservableCollection<string> SelectedVillesCPs { get; set; } = new();
        public bool Applied { get; private set; } = false;

        private readonly List<string> _existingVillesCPs;

        public VilleCPFilterWindow(IEnumerable<string> currentSelection, IEnumerable<string>? existingVillesCPs = null)
        {
            InitializeComponent();
            DataContext = this;
            foreach (var s in currentSelection)
                SelectedVillesCPs.Add(s);

            _existingVillesCPs = (existingVillesCPs ?? Enumerable.Empty<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct()
                .OrderBy(v => v)
                .ToList();

            // Show existing cities by default
            ShowExistingVilles();
        }

        private void ShowExistingVilles()
        {
            var filtered = _existingVillesCPs
                .Where(v => !SelectedVillesCPs.Contains(v))
                .ToList();
            lbExisting.ItemsSource = filtered;
        }

        private void TbSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = tbSearch.Text?.Trim() ?? "";
            if (query.Length >= 2)
            {
                // Search in existing cities first, then communes database
                var existingMatches = _existingVillesCPs
                    .Where(v => v.ToUpper().Contains(query.ToUpper()) && !SelectedVillesCPs.Contains(v))
                    .ToList();

                var communeMatches = CommuneService.Search(query, 20)
                    .Select(c => c.Display)
                    .Where(d => !SelectedVillesCPs.Contains(d) && !existingMatches.Contains(d))
                    .ToList();

                lbResults.ItemsSource = existingMatches.Concat(communeMatches).ToList();
            }
            else
            {
                lbResults.ItemsSource = null;
            }

            // Also filter existing list
            if (string.IsNullOrWhiteSpace(query))
            {
                ShowExistingVilles();
            }
            else
            {
                var filtered = _existingVillesCPs
                    .Where(v => v.ToUpper().Contains(query.ToUpper()) && !SelectedVillesCPs.Contains(v))
                    .ToList();
                lbExisting.ItemsSource = filtered;
            }
        }

        private void LbResults_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            AddItemFromListBox(sender, e);
        }

        private void LbExisting_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            AddItemFromListBox(sender, e);
        }

        private void AddItemFromListBox(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var element = e.OriginalSource as FrameworkElement;
            while (element != null && element is not ListBoxItem)
                element = element.Parent as FrameworkElement ?? System.Windows.Media.VisualTreeHelper.GetParent(element) as FrameworkElement;

            if (element is ListBoxItem listBoxItem && listBoxItem.Content is string item && !SelectedVillesCPs.Contains(item))
            {
                SelectedVillesCPs.Add(item);
                e.Handled = true;
                TbSearch_TextChanged(tbSearch, null!);
                ShowExistingVilles();
            }
        }

        private void RemovePill_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                SelectedVillesCPs.Remove(tag);
                TbSearch_TextChanged(tbSearch, null!);
                ShowExistingVilles();
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            Applied = true;
            DialogResult = true;
            Close();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            SelectedVillesCPs.Clear();
            Applied = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
