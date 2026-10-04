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

        /// <summary>Otomatik yedeklerin durduğu klasör: Belgelerim\Depo Takip\Yedek (Veri klasörünün yanında).</summary>
        public static string AutoBackupFolder
        {
            get { return Path.Combine(Database.RootFolder, "Yedek"); }
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

        /// <summary>Geçerli bir Depo Takip yedeğinde mutlaka bulunması gereken tablolar.</summary>
        private static readonly string[] RequiredTables =
            { "ProductTypes", "PropertyDefinitions", "Products", "ProductValues" };

        /// <summary>
        /// Seçilen dosyanın gerçekten sağlam bir Depo Takip yedeği olup olmadığına bakar.
        /// Değilse anlaşılır bir mesajla hata verir; hiçbir şeyin üzerine yazılmaz.
        /// </summary>
        public static void ValidateBackupFile(string backupFilePath)
        {
            if (!File.Exists(backupFilePath))
            {
                throw new InvalidOperationException("Seçilen dosya bulunamadı.");
            }

            if (string.Equals(Path.GetFullPath(backupFilePath), Path.GetFullPath(Database.DatabasePath),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Bu dosya zaten programın şu an kullandığı veritabanı. Başka bir yedek dosyası seç.");
            }

            try
            {
                var connectionString = "Data Source=" + backupFilePath + ";Version=3;Read Only=True;Pooling=False;";

                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "PRAGMA integrity_check;";
                        var result = Convert.ToString(command.ExecuteScalar());
                        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException(
                                "Bu yedek dosyası bozulmuş görünüyor. Başka bir yedek dosyası dene.");
                        }
                    }

                    foreach (var table in RequiredTables)
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText =
                                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;";
                            command.Parameters.Add(new SQLiteParameter("@name", table));

                            if (Convert.ToInt32(command.ExecuteScalar()) == 0)
                            {
                                throw new InvalidOperationException(
                                    "Bu dosya bir Depo Takip yedeği değil (\"" + table + "\" tablosu yok).");
                            }
                        }
                    }
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (SQLiteException)
            {
                throw new InvalidOperationException(
                    "Bu dosya bir Depo Takip yedeği değil ya da bozulmuş. Başka bir yedek dosyası seç.");
            }
        }

        /// <summary>
        /// Seçilen yedek dosyasını, geçerli veritabanının üzerine yazar.
        /// Önce dosyanın sağlam bir yedek olduğu kontrol edilir; sağlam değilse hiçbir şey değişmez.
        /// Program bu işlemden sonra yeniden başlatılmalıdır.
        /// </summary>
        public static void RestoreBackup(string backupFilePath)
        {
            ValidateBackupFile(backupFilePath);

            SQLiteConnection.ClearAllPools();

            var safetyCopyPath = Database.DatabasePath + ".oncesi_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak";

            if (File.Exists(Database.DatabasePath))
            {
                File.Copy(Database.DatabasePath, safetyCopyPath, true);
            }

            // Önce geçici dosyaya kopyalanır; kopyalama yarıda kesilirse mevcut veritabanı bozulmaz.
            var tempPath = Database.DatabasePath + ".yeni";
            File.Copy(backupFilePath, tempPath, true);

            if (File.Exists(Database.DatabasePath))
            {
                File.Delete(Database.DatabasePath);
            }

            File.Move(tempPath, Database.DatabasePath);
        }
    }
}