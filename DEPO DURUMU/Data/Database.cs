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

            EnsureProductsSortOrderColumn();
            EnsureQuantityColumns();
            PropertyDefinitionRepository.EnsureDefaults();
        }

        /// <summary>
        /// Products tablosuna, daha önce oluşturulmuş eski veritabanlarında eksik
        /// olabilecek "SortOrder" (sıra) sütununu ekler ve mevcut ürünlere,
        /// şu anki sıralarına göre bir başlangıç değeri atar. Sütun zaten varsa
        /// hiçbir şey yapmaz.
        /// </summary>
        private static void EnsureProductsSortOrderColumn()
        {
            using (var connection = OpenConnection())
            {
                var hasColumn = false;

                using (var checkCommand = connection.CreateCommand())
                {
                    checkCommand.CommandText = "PRAGMA table_info(Products);";
                    using (var reader = checkCommand.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (string.Equals(reader["name"].ToString(), "SortOrder", StringComparison.OrdinalIgnoreCase))
                            {
                                hasColumn = true;
                                break;
                            }
                        }
                    }
                }

                if (hasColumn)
                {
                    return;
                }

                using (var alterCommand = connection.CreateCommand())
                {
                    alterCommand.CommandText = "ALTER TABLE Products ADD COLUMN SortOrder INTEGER NOT NULL DEFAULT 0;";
                    alterCommand.ExecuteNonQuery();
                }

                // Mevcut ürünlere, o anki (Id'ye göre) sıralarını başlangıç sırası olarak ata.
                using (var selectCommand = connection.CreateCommand())
                {
                    selectCommand.CommandText =
                        "SELECT Id, ProductTypeId FROM Products ORDER BY ProductTypeId, Id;";

                    var updates = new System.Collections.Generic.List<System.Tuple<int, int>>();
                    var counters = new System.Collections.Generic.Dictionary<int, int>();

                    using (var reader = selectCommand.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var productId = reader.GetInt32(0);
                            var typeId = reader.GetInt32(1);

                            if (!counters.ContainsKey(typeId))
                            {
                                counters[typeId] = 0;
                            }
                            counters[typeId]++;

                            updates.Add(System.Tuple.Create(productId, counters[typeId]));
                        }
                    }

                    foreach (var update in updates)
                    {
                        using (var updateCommand = connection.CreateCommand())
                        {
                            updateCommand.CommandText = "UPDATE Products SET SortOrder = @order WHERE Id = @id;";
                            updateCommand.Parameters.Add(new SQLiteParameter("@order", update.Item2));
                            updateCommand.Parameters.Add(new SQLiteParameter("@id", update.Item1));
                            updateCommand.ExecuteNonQuery();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Products ve ScrapProducts tablolarına, eski veritabanlarında eksik olabilecek
        /// "Quantity" (adet) sütununu ekler. Mevcut tüm kayıtlar 1 adet sayılır.
        /// Sütun zaten varsa hiçbir şey yapmaz.
        /// </summary>
        private static void EnsureQuantityColumns()
        {
            using (var connection = OpenConnection())
            {
                foreach (var table in new[] { "Products", "ScrapProducts" })
                {
                    if (ColumnExists(connection, table, "Quantity"))
                    {
                        continue;
                    }

                    using (var alterCommand = connection.CreateCommand())
                    {
                        alterCommand.CommandText =
                            "ALTER TABLE " + table + " ADD COLUMN Quantity INTEGER NOT NULL DEFAULT 1;";
                        alterCommand.ExecuteNonQuery();
                    }
                }
            }
        }

        private static bool ColumnExists(SQLiteConnection connection, string table, string column)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(" + table + ");";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(reader["name"].ToString(), column, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
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

CREATE TABLE IF NOT EXISTS Products (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductTypeId  INTEGER NOT NULL REFERENCES ProductTypes(Id),
    SortOrder      INTEGER NOT NULL DEFAULT 0,
    Quantity       INTEGER NOT NULL DEFAULT 1,
    CreatedAt      TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS ProductValues (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductId      INTEGER NOT NULL REFERENCES Products(Id),
    PropertyId     INTEGER NOT NULL REFERENCES PropertyDefinitions(Id),
    TextValue      TEXT,
    UNIQUE (ProductId, PropertyId)
);

CREATE TABLE IF NOT EXISTS HomeStatistics (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductTypeId  INTEGER NOT NULL REFERENCES ProductTypes(Id),
    PropertyId     INTEGER NOT NULL REFERENCES PropertyDefinitions(Id),
    SortOrder      INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS ActionLogs (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    CreatedAt    TEXT    NOT NULL,
    UserName     TEXT    NOT NULL,
    TypeName     TEXT,
    Description  TEXT    NOT NULL,
    ActionType   TEXT    NOT NULL
);

-- Hurda: gerçek depodan bağımsız, donmuş ürün kayıtları.
-- TypeId/PropertyId burada BİLİNÇLİ olarak veritabanı ilişkisi (foreign key)
-- OLARAK TANIMLANMADI: gerçek depoda o cins/özellik silinebilsin diye,
-- hurdadaki kayıtlar bundan hiç etkilenmemeli. TypeId/PropertyId sadece
-- ""hâlâ orada duruyor mu"" diye kontrol etmek için saklanan referanslardır.
CREATE TABLE IF NOT EXISTS ScrapProducts (
    Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    TypeId             INTEGER,
    TypeName           TEXT    NOT NULL,
    OriginalSortOrder  INTEGER NOT NULL DEFAULT 0,
    Quantity           INTEGER NOT NULL DEFAULT 1,
    ScrappedAt         TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS ScrapProductValues (
    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
    ScrapProductId   INTEGER NOT NULL REFERENCES ScrapProducts(Id),
    PropertyId       INTEGER,
    PropertyName     TEXT    NOT NULL,
    DataType         TEXT    NOT NULL,
    IsSerialNumber   INTEGER NOT NULL DEFAULT 0,
    TextValue        TEXT
);
";
    }
}