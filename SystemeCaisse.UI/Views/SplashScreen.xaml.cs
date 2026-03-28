using System.Windows;

namespace SystemeCaisse.UI.Views
{
    public partial class SplashScreen : System.Windows.Window
    {
        public SplashScreen()
        {
            InitializeComponent();
        }

        public void SetLogo(string path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;
            try
            {
                LogoImage.Source = new System.Windows.Media.Imaging.BitmapImage(new System.Uri(path));
            }
            catch { }
        }
    }
}
