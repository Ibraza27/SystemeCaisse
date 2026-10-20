using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SystemeCaisse.UI.Services;

namespace SystemeCaisse.UI.Views
{
    public partial class CommandeClientInfoWindow : Window
    {
        public string ClientNom { get; set; }
        public string ClientPrenom { get; set; }
        public string ClientTelephone { get; set; }
        public string ClientAdresse { get; set; }
        public string ClientVille { get; set; }
        public string ClientCodePostal { get; set; }
        public string ClientCommentaire { get; set; }
        public string Action { get; set; } = "cancel"; // "confirm", "back", "cancel"

        private bool _suppressSearch = false;

        public CommandeClientInfoWindow(
            string nom,
            string prenom,
            string telephone,
            string adresse,
            string ville,
            string codePostal,
            string commentaire = "")
        {
            InitializeComponent();
            ClientNom = nom;
            ClientPrenom = prenom;
            ClientTelephone = telephone;
            ClientAdresse = adresse;
            ClientVille = ville;
            ClientCodePostal = codePostal;
            ClientCommentaire = commentaire;

            tbNom.Text = nom;
            tbPrenom.Text = prenom;
            tbTelephone.Text = telephone;
            tbAdresse.Text = adresse;
            tbCommentaire.Text = commentaire;

            _suppressSearch = true;
            tbCodePostal.Text = codePostal ?? "";
            tbVilleSearch.Text = ville ?? "";
            _suppressSearch = false;

            Loaded += (s, e) =>
            {
                tbNom.Focus();
                tbNom.CaretIndex = tbNom.Text.Length;
            };
        }

        private void TbCodePostal_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressSearch) return;

            string cpQuery = tbCodePostal.Text?.Trim() ?? "";
            if (cpQuery.Length >= 1)
            {
                var results = CommuneService.Search(cpQuery, 30);
                lbVilleSuggestions.ItemsSource = results;
                lbVilleSuggestions.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                // Auto-fill city if exact 5-digit postal code has a single match
                if (cpQuery.Length == 5 && results.Count == 1 && results[0].CodePostal == cpQuery)
                {
                    _suppressSearch = true;
                    tbVilleSearch.Text = results[0].Ville;
                    ClientVille = results[0].Ville;
                    ClientCodePostal = results[0].CodePostal;
                    _suppressSearch = false;
                    lbVilleSuggestions.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                lbVilleSuggestions.Visibility = Visibility.Collapsed;
            }
        }

        private void TbVilleSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressSearch) return;

            string villeQuery = tbVilleSearch.Text?.Trim() ?? "";
            string cpQuery = tbCodePostal.Text?.Trim() ?? "";

            if (villeQuery.Length >= 1)
            {
                string combinedQuery = (!string.IsNullOrEmpty(cpQuery) && cpQuery.Length < 5 && !villeQuery.Any(char.IsDigit))
                    ? $"{cpQuery} {villeQuery}"
                    : villeQuery;

                var results = CommuneService.Search(combinedQuery, 30);
                if (results.Count == 0 && combinedQuery != villeQuery)
                {
                    results = CommuneService.Search(villeQuery, 30);
                }

                lbVilleSuggestions.ItemsSource = results;
                lbVilleSuggestions.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                lbVilleSuggestions.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyCommuneSelection(CommuneEntry entry)
        {
            ClientVille = entry.Ville;
            ClientCodePostal = entry.CodePostal;
            _suppressSearch = true;
            tbCodePostal.Text = entry.CodePostal;
            tbVilleSearch.Text = entry.Ville;
            _suppressSearch = false;
            lbVilleSuggestions.Visibility = Visibility.Collapsed;
        }

        private void LbVilleSuggestions_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var element = e.OriginalSource as FrameworkElement;
            while (element != null && element is not ListBoxItem)
                element = element.Parent as FrameworkElement ?? System.Windows.Media.VisualTreeHelper.GetParent(element) as FrameworkElement;

            if (element is ListBoxItem item && item.DataContext is CommuneEntry entry)
            {
                ApplyCommuneSelection(entry);
                e.Handled = true;
            }
        }

        private void VilleInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (lbVilleSuggestions.Visibility != Visibility.Visible || lbVilleSuggestions.Items.Count == 0)
                return;

            if (e.Key == Key.Down)
            {
                int next = lbVilleSuggestions.SelectedIndex + 1;
                if (next >= lbVilleSuggestions.Items.Count) next = 0;
                lbVilleSuggestions.SelectedIndex = next;
                lbVilleSuggestions.ScrollIntoView(lbVilleSuggestions.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                int prev = lbVilleSuggestions.SelectedIndex - 1;
                if (prev < 0) prev = lbVilleSuggestions.Items.Count - 1;
                lbVilleSuggestions.SelectedIndex = prev;
                lbVilleSuggestions.ScrollIntoView(lbVilleSuggestions.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                if (lbVilleSuggestions.SelectedItem is CommuneEntry selected)
                {
                    ApplyCommuneSelection(selected);
                    if (e.Key == Key.Enter) e.Handled = true;
                }
                else if (e.Key == Key.Enter && lbVilleSuggestions.Items.Count > 0 && lbVilleSuggestions.Items[0] is CommuneEntry first)
                {
                    ApplyCommuneSelection(first);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                lbVilleSuggestions.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
        }

        private void LbVilleSuggestions_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && lbVilleSuggestions.SelectedItem is CommuneEntry entry)
            {
                ApplyCommuneSelection(entry);
                e.Handled = true;
            }
        }

        private void SaveFields()
        {
            ClientNom = tbNom.Text.Trim();
            ClientPrenom = tbPrenom.Text.Trim();
            ClientTelephone = tbTelephone.Text.Trim();
            ClientAdresse = tbAdresse.Text.Trim();
            ClientCommentaire = tbCommentaire.Text.Trim();

            string cpText = tbCodePostal.Text?.Trim() ?? "";
            string villeText = tbVilleSearch.Text?.Trim() ?? "";

            if (!string.IsNullOrWhiteSpace(cpText) && string.IsNullOrWhiteSpace(villeText))
            {
                var matches = CommuneService.Search(cpText, 5);
                if (matches.Count > 0)
                {
                    ClientCodePostal = matches[0].CodePostal;
                    ClientVille = matches[0].Ville;
                }
                else
                {
                    ClientCodePostal = cpText;
                    ClientVille = string.Empty;
                }
            }
            else if (string.IsNullOrWhiteSpace(cpText) && !string.IsNullOrWhiteSpace(villeText))
            {
                var matches = CommuneService.Search(villeText, 5);
                if (matches.Count == 1 || (matches.Count > 0 && CommuneService.NormalizeText(matches[0].Ville) == CommuneService.NormalizeText(villeText)))
                {
                    ClientVille = matches[0].Ville;
                    ClientCodePostal = matches[0].CodePostal;
                }
                else
                {
                    ClientVille = villeText;
                    ClientCodePostal = string.Empty;
                }
            }
            else
            {
                ClientCodePostal = cpText;
                ClientVille = villeText;
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            SaveFields();

            if (string.IsNullOrWhiteSpace(ClientNom) || string.IsNullOrWhiteSpace(ClientPrenom) || string.IsNullOrWhiteSpace(ClientTelephone))
            {
                MessageBox.Show(this, "Veuillez remplir les champs obligatoires (Nom, Prénom, Téléphone).", "Champs requis", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Action = "confirm";
            DialogResult = true;
            Close();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            SaveFields();
            Action = "back";
            DialogResult = false;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Action = "cancel";
            DialogResult = false;
            Close();
        }
    }
}
