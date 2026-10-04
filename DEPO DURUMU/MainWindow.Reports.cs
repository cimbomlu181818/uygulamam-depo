using System.Windows;

namespace DEPO_DURUMU
{
    // ---------- RAPORLAR ----------
    // Barda "Raporlar" düğmesi vardır. Basınca kayıtlı raporların listesi açılır;
    // yeni rapor o listedeki "Yeni Ekle" düğmesiyle hazırlanır.
    public partial class MainWindow
    {
        private void ReportsMenu_Click(object sender, RoutedEventArgs e)
        {
            var window = new ReportsWindow { Owner = this };
            window.ShowDialog();
        }
    }
}
