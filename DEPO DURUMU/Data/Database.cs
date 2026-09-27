using System;
using System.Data.SQLite;
using System.IO;

namespace DEPO_DURUMU.Data
{
    /// <summary>
    /// SQLite veritabanı dosyasını oluşturur ve bağlantı açar.
    /// Veritabanı, programın bulunduğu klasördeki "Veri" klasöründe tutulur.
    /// </summary>
    public static class Database
    {
        public static string DataFolder
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Veri"); }
        }

        public static string DatabasePath
        {
            get { return Path.Combine(DataFolder, "depostok.db"); }
        }

        private static string ConnectionString
        {
            get { return "Data Source=" + DatabasePath + ";Version=3;Foreign Keys=True;"; }
        }

        /// <summary>
        /// Açık bir bağlantı verir. Kullanan kişi "using" ile kapatmalıdır.
        /// </summary>
        public static SQLiteConnection OpenConnection()
        {
            var connection = new SQLiteConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        /// <summary>
        /// Klasörü ve tabloları oluşturur. Zaten varsa hiçbir şeyi silmez veya bozmaz.
        /// </summary>
        public static void Initialize()
        {
            Directory.CreateDirectory(DataFolder);

            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = CreateTablesSql;
                command.ExecuteNonQuery();
            }

            PropertyDefinitionRepository.EnsureDefaults();
        }

        private const string CreateTablesSql = @"
CREATE TABLE IF NOT EXISTS ProductTypes (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Name        TEXT    NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS PropertyDefinitions (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Name            TEXT    NOT NULL UNIQUE,
    DataType        TEXT    NOT NULL,
    IsSerialNumber  INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS TypeProperties (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductTypeId  INTEGER NOT NULL REFERENCES ProductTypes(Id),
    PropertyId     INTEGER NOT NULL REFERENCES PropertyDefinitions(Id),
    SortOrder      INTEGER NOT NULL DEFAULT 0,
    UNIQUE (ProductTypeId, PropertyId)
);
";
    }
}