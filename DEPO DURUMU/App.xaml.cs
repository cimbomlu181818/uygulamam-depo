using System;
using System.Windows;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    public partial class App : Application
    {
        /// <summary>Veritabanı bu oturumda sorunsuz açıldıysa true. Açılamamış bir veritabanı yedeğin üzerine yazılmasın diye.</summary>
        private bool _databaseReady;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Database.Initialize();
            _databaseReady = true;
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

            base.OnExit(e);
        }
    }
}