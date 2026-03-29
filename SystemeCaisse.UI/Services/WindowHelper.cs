using System.Linq;
using System.Windows;

namespace SystemeCaisse.UI.Services
{
    /// <summary>
    /// Centralized helper to always find the correct Admin window (MainWindow).
    /// This prevents MessageBox and dialogs from appearing on the Customer Display.
    /// In WPF, Application.Current.MainWindow can incorrectly point to the
    /// CustomerDisplayWindow or SplashScreen, so we explicitly search by type.
    /// </summary>
    public static class WindowHelper
    {
        /// <summary>
        /// Returns the Admin window (MainWindow) for use as MessageBox/dialog owner.
        /// Falls back to Application.Current.MainWindow if MainWindow is not found.
        /// </summary>
        public static Window GetAdminWindow()
        {
            return Application.Current.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w is SystemeCaisse.UI.MainWindow)
                ?? Application.Current.MainWindow;
        }
    }
}
