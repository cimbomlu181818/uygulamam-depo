using System.Windows;

namespace DEPO_DURUMU
{
    // ---------- NOTLAR ----------
    // Üst menüdeki "Notlar" düğmesi: Excel benzeri, çok sayfalı not tablosunu açar.
    public partial class MainWindow
    {
        private void NotesMenu_Click(object sender, RoutedEventArgs e)
        {
            var window = new NotesWindow { Owner = this };
            window.ShowDialog();
        }
    }
}
