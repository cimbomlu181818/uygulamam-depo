using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    public partial class App : Application
    {
        private const string AppTitle = "Depo Takip";

        /// <summary>Veritabanı bu oturumda sorunsuz açıldıysa true. Açılamamış bir veritabanı yedeğin üzerine yazılmasın diye.</summary>
        private bool _databaseReady;

        /// <summary>Programın aynı anda iki kez açılmasını engelleyen kilit. Sadece ilk açılan program tutar.</summary>
        private Mutex _singleInstanceMutex;
        private bool _ownsMutex;

        /// <summary>
        /// Karşılama penceresinin bir kez gösterildiğini belirten küçük dosya. Veritabanının içinde değil,
        /// Belgelerim\Depo Takip klasöründe durur; böylece yedekten geri yüklenince silinmez.
        /// </summary>
        private static string FirstRunMarkerPath
        {
            get { return Path.Combine(Database.RootFolder, "ilk_acilis_tamam.txt"); }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Tema: açılan her pencerenin arka plan ve yazı rengi, pencerede ayrıca belirtilmemişse temadan (App.xaml) alınır.
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(ApplyThemeToWindow));

            // Program zaten açıksa ikincisi açılmasın; iki pencere aynı veritabanına yazınca "kilitli" hatası çıkar.
            // "Local\" ile bu Windows oturumuna özel olur (başka bir kullanıcı kendi programını açabilir).
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, "Local\\DepoTakip_TekPencere", out createdNew);
            _ownsMutex = createdNew;

            if (!createdNew)
            {
                MessageBox.Show(
                    "Depo Takip zaten açık. Görev çubuğundaki açık pencereyi kullan.",
                    AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);

                Shutdown();
                return;
            }

            // Beklenmeyen bir hata olursa program sessizce kapanmasın, anlaşılır bir mesaj çıksın.
            DispatcherUnhandledException += OnUnhandledException;

            try
            {
                Database.Initialize();
                _databaseReady = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Program açılamadı, veritabanı açılırken bir sorun oldu.\n\n" +
                    "Veri klasörü:\n" + Database.DataFolder + "\n\n" +
                    "Neden: " + ex.Message + "\n\n" +
                    "Veriler bozulduysa, Yedek klasöründeki en son yedeği (depostok_...db) " +
                    "Veri klasörüne kopyalayıp adını depostok.db yapabilirsin. " +
                    "Önce bozuk depostok.db dosyasını başka bir yere sakla.\n\n" +
                    "Yedek klasörü:\n" + BackupService.AutoBackupFolder,
                    AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);

                Shutdown(1);
                return;
            }

            // İlk açılışta kullanıcıya nasıl başlamak istediği sorulur.
            var choice = WelcomeChoice.None;

            if (ShouldShowWelcome())
            {
                // Karşılama penceresi kapanınca program kendiliğinden kapanmasın.
                ShutdownMode = ShutdownMode.OnExplicitShutdown;

                while (true)
                {
                    var welcome = new WelcomeWindow();
                    welcome.ShowDialog();

                    if (welcome.Choice == WelcomeChoice.RestoreBackup)
                    {
                        if (RestoreFromBackupAndRestart())
                        {
                            return;
                        }

                        // Vazgeçildi ya da geri yükleme olmadı: karşılama penceresine geri dönülür.
                        continue;
                    }

                    // Hiçbir seçenek seçilmeden pencere kapatıldıysa (X ya da Alt+F4) içeri alınmaz.
                    // Program çıkışta işaret dosyası yazmaz, bu yüzden tekrar açılınca seçim ekranı yine gelir.
                    if (welcome.Choice == WelcomeChoice.None)
                    {
                        var answer = MessageBox.Show(
                            "Devam etmek için bir seçenek seçmelisin.\n\n" +
                            "Programdan çıkmak istiyor musun?\n" +
                            "(Evet dersen program kapanır, tekrar açtığında bu seçenekler yine karşına gelir. " +
                            "Hayır dersen seçim ekranına dönersin.)",
                            AppTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning);

                        if (answer == MessageBoxResult.Yes)
                        {
                            // Henüz hiçbir şey seçilmedi: kapanışta boş veritabanının yedeği alınmasın.
                            _databaseReady = false;
                            Shutdown();
                            return;
                        }

                        continue;
                    }

                    choice = welcome.Choice;
                    break;
                }

                MarkFirstRunDone();
                ShutdownMode = ShutdownMode.OnLastWindowClose;
            }

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();

            // "Excel dosyam var" seçildiyse, ana ekran görününce içe aktarma başlar.
            if (choice == WelcomeChoice.ImportExcel)
            {
                mainWindow.Dispatcher.BeginInvoke(
                    new Action(mainWindow.StartExcelImportFromWelcome),
                    DispatcherPriority.ContextIdle);
            }
        }

        /// <summary>
        /// Pencerenin arka plan ve yazı rengini temadan verir. XAML'de ya da kodda elle renk verilmiş pencereye dokunmaz.
        /// (Window için yazılan örtük stil, MainWindow gibi türetilmiş pencerelere uygulanmadığı için bu yol kullanılır.)
        /// </summary>
        private static void ApplyThemeToWindow(object sender, RoutedEventArgs e)
        {
            var window = sender as Window;
            if (window == null)
            {
                return;
            }

            if (window.ReadLocalValue(Control.BackgroundProperty) == DependencyProperty.UnsetValue)
            {
                window.SetResourceReference(Control.BackgroundProperty, "ThemeBgBrush");
            }

            if (window.ReadLocalValue(Control.ForegroundProperty) == DependencyProperty.UnsetValue)
            {
                window.SetResourceReference(Control.ForegroundProperty, "ThemeTextBrush");
            }
        }

        /// <summary>
        /// Karşılama penceresi yalnızca ilk açılışta gösterilir: işaret dosyası yoksa ve veritabanında
        /// henüz hiç ürün cinsi yoksa. Zaten kullanılmış bir veritabanı varsa pencere çıkmaz.
        /// </summary>
        private static bool ShouldShowWelcome()
        {
            try
            {
                if (File.Exists(FirstRunMarkerPath))
                {
                    return false;
                }

                if (ProductTypeRepository.GetAll().Count > 0)
                {
                    MarkFirstRunDone();
                    return false;
                }

                return true;
            }
            catch (Exception)
            {
                // Kontrol edilemiyorsa kullanıcıyı rahatsız etmeden normal açılır.
                return false;
            }
        }

        /// <summary>Karşılama penceresinin bir daha gösterilmemesi için işaret dosyasını yazar.</summary>
        private static void MarkFirstRunDone()
        {
            try
            {
                Directory.CreateDirectory(Database.RootFolder);
                File.WriteAllText(FirstRunMarkerPath, "Depo Takip ilk açılış penceresi gösterildi.");
            }
            catch (Exception)
            {
                // İşaret yazılamazsa program yine de açılır.
            }
        }

        /// <summary>
        /// "Yedek dosyam var" seçimi: yedek dosyası seçtirilir, geri yüklenir ve program kendiliğinden yeniden açılır.
        /// Başarılıysa true döner (program kapanıyordur). Vazgeçilirse ya da hata olursa false döner.
        /// </summary>
        private bool RestoreFromBackupAndRestart()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Yedek Dosyasını Seç",
                Filter = "Depo Takip yedek dosyası (*.db)|*.db"
            };

            if (dialog.ShowDialog() != true)
            {
                return false;
            }

            try
            {
                BackupService.RestoreBackup(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Geri yükleme başarısız: " + ex.Message, AppTitle,
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            MarkFirstRunDone();

            MessageBox.Show("Yedek geri yüklendi. Program şimdi yeniden açılacak.", AppTitle,
                MessageBoxButton.OK, MessageBoxImage.Information);

            // Geri yüklenen veritabanının üzerine kapanışta yedek alınmasın;
            // yeni açılan program "zaten açık" demesin diye kilit de bırakılır.
            _databaseReady = false;
            ReleaseSingleInstanceLock();

            try
            {
                Process.Start(Assembly.GetExecutingAssembly().Location);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Program kendiliğinden açılamadı. Programı elle yeniden aç.\n\nNeden: " + ex.Message,
                    AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            Shutdown();
            return true;
        }

        /// <summary>"Program zaten açık" kilidini bırakır. Birden fazla çağrılsa da sorun çıkmaz.</summary>
        private void ReleaseSingleInstanceLock()
        {
            if (_singleInstanceMutex == null)
            {
                return;
            }

            if (_ownsMutex)
            {
                _singleInstanceMutex.ReleaseMutex();
                _ownsMutex = false;
            }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        /// <summary>Yakalanmamış bir hata olursa mesaj gösterilir ve log defterine yazılır; program kapanmaz.</summary>
        private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                LogRepository.Add(null, "Beklenmeyen hata: " + e.Exception.Message, "Program hatası");
            }
            catch (Exception)
            {
                // Log yazılamıyorsa mesaj yine de gösterilir.
            }

            MessageBox.Show(
                "Beklenmeyen bir hata oldu.\n\nNeden: " + e.Exception.Message + "\n\n" +
                "Yaptığın son işlemi kontrol et. Sorun devam ederse programı kapatıp yeniden aç.",
                AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);

            e.Handled = true;
        }

        /// <summary>Uygulama her kapanırken veritabanının o günün yedeği alınır (aynı günkü yedeğin üzerine yazılır).</summary>
        protected override void OnExit(ExitEventArgs e)
        {
            if (_databaseReady)
            {
                try
                {
                    BackupService.CreateAutoBackup();
                }
                catch (Exception ex)
                {
                    // Yedek alınamaması kapanışı engellemez; hata log defterine yazılır.
                    try
                    {
                        LogRepository.Add(null, "Otomatik yedek alınamadı: " + ex.Message, "Yedek hatası");
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            ReleaseSingleInstanceLock();

            base.OnExit(e);
        }
    }
}