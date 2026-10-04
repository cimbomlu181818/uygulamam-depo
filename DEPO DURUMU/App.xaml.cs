using System;
using System.Threading;
using System.Windows;
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

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
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

            if (_singleInstanceMutex != null)
            {
                if (_ownsMutex)
                {
                    _singleInstanceMutex.ReleaseMutex();
                }

                _singleInstanceMutex.Dispose();
            }

            base.OnExit(e);
        }
    }
}