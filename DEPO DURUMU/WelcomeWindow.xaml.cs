using System.Windows;

namespace DEPO_DURUMU
{
    /// <summary>Karşılama penceresinde kullanıcının seçtiği başlangıç yolu.</summary>
    public enum WelcomeChoice
    {
        /// <summary>Seçim yapılmadan pencere kapatıldı: program uyarı verir, seçim yapılmadan içeri alınmaz.</summary>
        None,

        /// <summary>"Yeni bir depoya başlıyorum": doğrudan ana ekran.</summary>
        StartFresh,

        /// <summary>"Elimde Excel dosyası var": Excel'den depoya aktarma.</summary>
        ImportExcel,

        /// <summary>"Yedek dosyam var": yedekten geri yükleme.</summary>
        RestoreBackup
    }

    /// <summary>
    /// Program ilk kez açıldığında gösterilen karşılama penceresi.
    /// Kullanıcı nasıl başlayacağını seçer; sonucu Choice özelliğinden okunur.
    /// </summary>
    public partial class WelcomeWindow : Window
    {
        public WelcomeChoice Choice { get; private set; }

        public WelcomeWindow()
        {
            InitializeComponent();
            Choice = WelcomeChoice.None;
        }

        private void FreshButton_Click(object sender, RoutedEventArgs e)
        {
            Choose(WelcomeChoice.StartFresh);
        }

        private void ExcelButton_Click(object sender, RoutedEventArgs e)
        {
            Choose(WelcomeChoice.ImportExcel);
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            Choose(WelcomeChoice.RestoreBackup);
        }

        private void Choose(WelcomeChoice choice)
        {
            Choice = choice;
            DialogResult = true;
        }
    }
}