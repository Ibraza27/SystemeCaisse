using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Win32;
using System.Diagnostics;

namespace SystemeCaisse.Installer
{
    public partial class MainWindow : Window
    {
        private string _targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hippocampe");
        
        public MainWindow()
        {
            InitializeComponent();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (PnlProgress.Visibility == Visibility.Visible)
            {
                if (MessageBox.Show("L'installation est en cours. Voulez-vous vraiment annuler ?", "Confirmation", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                    return;
            }
            Close();
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            string sourceDb = "";
            if (RbImport.IsChecked == true)
            {
                var openDlg = new OpenFileDialog
                {
                    Filter = "Base de données Python (database.db)|database.db|Tous les fichiers (*.*)|*.*",
                    Title = "Sélectionner la base de données à importer"
                };
                if (openDlg.ShowDialog() == true)
                {
                    sourceDb = openDlg.FileName;
                }
                else return; // User cancelled import choice
            }

            BtnInstall.IsEnabled = false;
            BtnCancel.IsEnabled = false;
            PnlStep1.Visibility = Visibility.Collapsed;
            PnlProgress.Visibility = Visibility.Visible;

            try
            {
                // 1. Create Target Directory
                TxtStatus.Text = "Création des dossiers...";
                PrgBar.Value = 10;
                if (!Directory.Exists(_targetDir)) Directory.CreateDirectory(_targetDir);

                // 2. Extract Payload
                TxtStatus.Text = "Extraction des fichiers...";
                PrgBar.Value = 30;
                var assembly = Assembly.GetExecutingAssembly();
                
                string payloadRes = "SystemeCaisse.Installer.Resources.payload.exe";
                using (Stream? stream = assembly.GetManifestResourceStream(payloadRes))
                {
                    if (stream == null) throw new Exception($"Ressource introuvable : {payloadRes}");
                    using (FileStream fileStream = new FileStream(Path.Combine(_targetDir, "SystemeCaisse.exe"), FileMode.Create))
                    {
                        await stream.CopyToAsync(fileStream);
                    }
                }

                // Copy Logo for shortcuts
                string logoRes = "SystemeCaisse.Installer.Resources.logo.png";
                using (Stream? stream = assembly.GetManifestResourceStream(logoRes))
                {
                    if (stream == null) 
                    {
                        // Fallback or just ignore if it's only for the icon
                    }
                    else 
                    {
                        var imgDir = Path.Combine(_targetDir, "Images");
                        if (!Directory.Exists(imgDir)) Directory.CreateDirectory(imgDir);
                        using (FileStream fileStream = new FileStream(Path.Combine(imgDir, "logo.png"), FileMode.Create))
                        {
                            await stream.CopyToAsync(fileStream);
                        }
                    }
                }

                // 3. Handle Database Import
                if (!string.IsNullOrEmpty(sourceDb))
                {
                    TxtStatus.Text = "Importation de la base de données...";
                    PrgBar.Value = 60;
                    // We'll rename it to caisse.db for the new app
                    File.Copy(sourceDb, Path.Combine(_targetDir, "caisse.db"), true);
                }

                // 4. Create Shortcuts
                TxtStatus.Text = "Création des raccourcis...";
                PrgBar.Value = 85;
                if (ChkDesktop.IsChecked == true) CreateShortcut("Desktop");
                if (ChkStart.IsChecked == true) CreateShortcut("StartMenu");

                // 5. Register Uninstaller
                TxtStatus.Text = "Finalisation de l'installation...";
                PrgBar.Value = 95;
                RegisterUninstaller();

                // 6. Finish
                PrgBar.Value = 100;
                PnlProgress.Visibility = Visibility.Collapsed;
                PnlFinished.Visibility = Visibility.Visible;
                BtnFinish.Visibility = Visibility.Visible;
                BtnCancel.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur d'installation : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                BtnInstall.IsEnabled = true;
                BtnCancel.IsEnabled = true;
                PnlStep1.Visibility = Visibility.Visible;
                PnlProgress.Visibility = Visibility.Collapsed;
            }
        }

        private void CreateShortcut(string type)
        {
            try 
            {
                object shFolder = type == "Desktop" 
                    ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");

                string linkPath = Path.Combine(shFolder.ToString()!, "Hippocampe.lnk");
                
                // Use WshShell via dynamic to avoid COM reference headache in scripts
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) throw new Exception("WScript.Shell introuvable (Indispensable pour les raccourcis)");
                
                dynamic shell = Activator.CreateInstance(shellType) ?? throw new Exception("Impossible de créer l'instance WScript.Shell");
                var shortcut = shell.CreateShortcut(linkPath);
                shortcut.TargetPath = Path.Combine(_targetDir, "SystemeCaisse.exe");
                shortcut.WorkingDirectory = _targetDir;
                shortcut.Description = "Système de Caisse Hippocampe";
                shortcut.Save();
            }
            catch { /* Ignore shortcut errors */ }
        }

        private void RegisterUninstaller()
        {
            try
            {
                string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\HippocampeSystemeCaisse";
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string startMenuPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");

                using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
                {
                    key.SetValue("DisplayName", "Hippocampe Système de Caisse");
                    
                    // Improved UninstallString with evaluated paths and force-cleanup
                    string uninstallCmd = $"powershell.exe -NoProfile -WindowStyle Hidden -Command \"" +
                        $"Stop-Process -Name SystemeCaisse -Force -ErrorAction SilentlyContinue; " +
                        $"Remove-Item -Path '{_targetDir}' -Recurse -Force -ErrorAction SilentlyContinue; " +
                        $"Remove-Item -Path '{Path.Combine(desktopPath, "Hippocampe.lnk")}' -Force -ErrorAction SilentlyContinue; " +
                        $"Remove-Item -Path '{Path.Combine(startMenuPath, "Hippocampe.lnk")}' -Force -ErrorAction SilentlyContinue; " +
                        $"Remove-Item -Path 'HKCU:\\{keyPath}' -Recurse -Force -ErrorAction SilentlyContinue;\"";

                    key.SetValue("UninstallString", uninstallCmd);
                    key.SetValue("DisplayIcon", Path.Combine(_targetDir, "SystemeCaisse.exe"));
                    key.SetValue("Publisher", "Hippocampe");
                    key.SetValue("DisplayVersion", "1.1.2");
                }
            }
            catch { }
        }

        private void BtnFinish_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(Path.Combine(_targetDir, "SystemeCaisse.exe"));
            Close();
        }
    }
}
