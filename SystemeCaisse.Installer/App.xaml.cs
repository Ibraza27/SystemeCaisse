using System.Configuration;
using System.Data;
using System.Windows;
using System.IO;

namespace SystemeCaisse.Installer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (s, args) =>
        {
            File.WriteAllText("installer_crash.txt", args.Exception.ToString());
            MessageBox.Show($"Une erreur est survenue au lancement de l'installateur : {args.Exception.Message}\nVoir installer_crash.txt pour plus de détails.", "Erreur Fatale", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown();
        };

        base.OnStartup(e);
    }
}

