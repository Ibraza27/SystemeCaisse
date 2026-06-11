using System.Windows;
using System.Windows.Controls;
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
        public string Action { get; set; } = "cancel"; // "confirm", "back", "cancel"

        private bool _suppressSearch = false;

        public CommandeClientInfoWindow(string nom, string prenom, string telephone, string adresse, string ville, string codePostal)
        {
            InitializeComponent();
            ClientNom = nom;
            ClientPrenom = prenom;
            ClientTelephone = telephone;
            ClientAdresse = adresse;
            ClientVille = ville;
            ClientCodePostal = codePostal;

            tbNom.Text = nom;
            tbPrenom.Text = prenom;
            tbTelephone.Text = telephone;
            tbAdresse.Text = adresse;

            if (!string.IsNullOrWhiteSpace(codePostal) && !string.IsNullOrWhiteSpace(ville))
            {
                _suppressSearch = true;
                tbVilleSearch.Text = $"{codePostal} — {ville}";
                _suppressSearch = false;
            }
        }

        private void TbVilleSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressSearch) return;

            string query = tbVilleSearch.Text?.Trim() ?? "";
            if (query.Length >= 2)
            {
                var results = CommuneService.Search(query);
                lbVilleSuggestions.ItemsSource = results;
                lbVilleSuggestions.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                lbVilleSuggestions.Visibility = Visibility.Collapsed;
            }
        }

        private void LbVilleSuggestions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lbVilleSuggestions.SelectedItem is CommuneEntry entry)
            {
                ClientVille = entry.Ville;
                ClientCodePostal = entry.CodePostal;
                _suppressSearch = true;
                tbVilleSearch.Text = entry.Display;
                _suppressSearch = false;
                lbVilleSuggestions.Visibility = Visibility.Collapsed;
            }
        }

        private void SaveFields()
        {
            ClientNom = tbNom.Text.Trim();
            ClientPrenom = tbPrenom.Text.Trim();
            ClientTelephone = tbTelephone.Text.Trim();
            ClientAdresse = tbAdresse.Text.Trim();
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
