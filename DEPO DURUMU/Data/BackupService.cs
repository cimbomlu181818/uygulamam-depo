using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DEPO_DURUMU.Data
{
    /// <summary>
    /// Tüm veritabanını (tek dosya) yedekleme ve yedekten geri yükleme.
    /// SQLite dosya kilitlerinin serbest kalması için kopyalamadan önce
    /// bağlantı havuzu temizlenir.
    /// </summary>
    public static class BackupService
    {
        // ---------- OTOMATİK YEDEK (uygulama her kapanırken) ----------

        private const string AutoBackupPrefix = "depostok_";
        private const string AutoBackupDateFormat = "yyyy-MM-dd";

        /// <summary>Saklanacak en fazla yedek günü sayısı. 11. gün en eski olan silinir.</summary>
        private const int AutoBackupKeepCount = 10;

        /// <summary>Otomatik yedeklerin durduğu klasör: programın klasöründeki Yedek klasörü (Veri klasörünün yanında).</summary>
        public static string AutoBackupFolder
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Yedek"); }
        }

        /// <summary>
        /// Veritabanının o günün yedeğini alır: depostok_2026-10-01.db gibi, günün tarihini taşır.
        /// Aynı gün için yedek zaten varsa üzerine yazılır (gün içindeki son kapanış kalır).
        /// Sonra yedek günü sayısı 10'u aşmışsa en eski günlerin yedekleri silinir.
        /// Yedek günü sayılır, takvim günü değil: uygulama uzun süre açılmasa da eski yedekler silinmez.
        /// </summary>
        public static void CreateAutoBackup()
        {
            if (!File.Exists(Database.DatabasePath))
            {
                return;
            }

            Directory.CreateDirectory(AutoBackupFolder);

            var finalPath = Path.Combine(AutoBackupFolder,
                AutoBackupPrefix + DateTime.Now.ToString(AutoBackupDateFormat, CultureInfo.InvariantCulture) + ".db");

            // Önce geçici dosyaya kopyalanır; kopyalama yarıda kesilirse o günün eski yedeği bozulmaz.
            var tempPath = finalPath + ".tmp";

            SQLiteConnection.ClearAllPools();
            File.Copy(Database.DatabasePath, tempPath, true);

            if (File.Exists(finalPath))
            {
                File.Delete(finalPath);
            }

            File.Move(tempPath, finalPath);

            DeleteOldAutoBackups();
        }

        /// <summary>Tarihli yedek dosyalarından en yeni 10'unu bırakır, daha eskilerini siler.</summary>
        private static void DeleteOldAutoBackups()
        {
            var dated = new List<KeyValuePair<DateTime, string>>();

            foreach (var file in Directory.GetFiles(AutoBackupFolder, AutoBackupPrefix + "*.db"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var datePart = name.Substring(AutoBackupPrefix.Length);

                DateTime date;
                if (DateTime.TryParseExact(datePart, AutoBackupDateFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out date))
                {
                    dated.Add(new KeyValuePair<DateTime, string>(date, file));
                }
            }

            // Adı bu düzene uymayan dosyalara (elle konulmuş vb.) dokunulmaz.
            foreach (var old in dated.OrderByDescending(d => d.Key).Skip(AutoBackupKeepCount))
            {
                try
                {
                    File.Delete(old.Value);
                }
                catch (Exception)
                {
                    // Silinemeyen eski yedek, yeni yedeğin alınmasını engellemez.
                }
            }
        }

        // ---------- ELLE YEDEK / GERİ YÜKLEME ----------

        /// <summary>Veritabanı dosyasının o anki bir kopyasını, seçilen konuma yazar.</summary>
        public static void CreateBackup(string destinationPath)
        {
            SQLiteConnection.ClearAllPools();
            File.Copy(Database.DatabasePath, destinationPath, true);
        }

        /// <summary>
        /// Seçilen yedek dosyasını, geçerli veritabanının üzerine yazar.
        /// Program bu işlemden sonra yeniden başlatılmalıdır.
        /// </summary>
        public static void RestoreBackup(string backupFilePath)
        {
            SQLiteConnection.ClearAllPools();

            var safetyCopyPath = Database.DatabasePath + ".oncesi_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak";

            if (File.Exists(Database.DatabasePath))
            {
                File.Copy(Database.DatabasePath, safetyCopyPath, true);
            }

            File.Copy(backupFilePath, Database.DatabasePath, true);
        }
    }
}