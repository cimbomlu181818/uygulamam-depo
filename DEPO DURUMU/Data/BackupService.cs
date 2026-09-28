using System;
using System.Data.SQLite;
using System.IO;

namespace DEPO_DURUMU.Data
{
    /// <summary>
    /// Tüm veritabanını (tek dosya) yedekleme ve yedekten geri yükleme.
    /// SQLite dosya kilitlerinin serbest kalması için kopyalamadan önce
    /// bağlantı havuzu temizlenir.
    /// </summary>
    public static class BackupService
    {
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
