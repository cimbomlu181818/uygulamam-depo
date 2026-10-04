using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

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
            RepairScientificNumbers();
        }

        private static readonly Regex ScientificRegex =
            new Regex(@"^(\d+)(?:[.,](\d+))?[eE]\+?(\d{1,2})$", RegexOptions.Compiled);

        /// <summary>
        /// "2,78389041700206E+15" gibi bilimsel gösterime bozulmuş uzun sayıyı tam rakam dizisine
        /// çevirir (2783890417002060). Sadece sonuç 12 hane ve üzeriyse ve rakamların hepsi metinde
        /// varsa dokunur; "2,78E+15" gibi rakamları kaybolmuş değerlere rakam uydurmaz.
        /// </summary>
        public static string ExpandScientific(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            var match = ScientificRegex.Match(value.Trim());
            if (!match.Success)
            {
                return value;
            }

            var mantissaDigits = match.Groups[1].Length + match.Groups[2].Length;
            var exponent = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

            // Excel en fazla 15 anlamlı hane saklar; daha azı varsa rakamlar zaten kaybolmuştur.
            if (mantissaDigits < 15 && mantissaDigits <= exponent)
            {
                return value;
            }

            decimal number;
            var normalized = match.Groups[1].Value +
                (match.Groups[2].Success ? "." + match.Groups[2].Value : "") + "E" + exponent;
            if (!decimal.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                return value;
            }

            var digits = number.ToString("0", CultureInfo.InvariantCulture);
            return digits.Length >= 12 ? digits : value;
        }

        /// <summary>
        /// Daha önce bilimsel gösterime bozulup kaydedilmiş uzun sayıları (seri no, MAC vb.)
        /// tam rakamlarına çevirir. Zaten düzgün olan kayıtlara dokunmaz.
        /// </summary>
        private static void RepairScientificNumbers()
        {
            try
            {
                using (var connection = OpenConnection())
                using (var transaction = connection.BeginTransaction())
                {
                    RepairColumn(connection, "ProductValues", "TextValue");
                    RepairColumn(connection, "ScrapProductValues", "TextValue");
                    RepairColumn(connection, "Assignments", "SerialNo");
                    RepairColumn(connection, "HandoverItems", "SerialNo");
                    transaction.Commit();
                }
            }
            catch
            {
                // Onarım başarısız olsa da program açılmaya devam etsin.
            }
        }

        private static void RepairColumn(SQLiteConnection connection, string table, string column)
        {
            var fixes = new List<KeyValuePair<long, string>>();

            using (var select = connection.CreateCommand())
            {
                select.CommandText =
                    "SELECT Id, " + column + " FROM " + table + " WHERE " + column + " LIKE '%E+%';";
                using (var reader = select.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.IsDBNull(1))
                        {
                            continue;
                        }

                        var text = reader.GetString(1);
                        var expanded = ExpandScientific(text);
                        if (expanded != text)
                        {
                            fixes.Add(new KeyValuePair<long, string>(reader.GetInt64(0), expanded));
                        }
                    }
                }
            }

            foreach (var fix in fixes)
            {
                using (var update = connection.CreateCommand())
                {
                    update.CommandText = "UPDATE " + table + " SET " + column + " = @v WHERE Id = @id;";
                    update.Parameters.Add(new SQLiteParameter("@v", fix.Value));
                    update.Parameters.Add(new SQLiteParameter("@id", fix.Key));
                    update.ExecuteNonQuery();
                }
            }
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

-- Renk özelliği: renklerin anlamları ve hücre renkleri.
CREATE TABLE IF NOT EXISTS ColorMeanings (
    ColorKey  TEXT PRIMARY KEY,
    Meaning   TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS CellColors (
    ProductId   INTEGER NOT NULL REFERENCES Products(Id) ON DELETE CASCADE,
    PropertyId  INTEGER NOT NULL REFERENCES PropertyDefinitions(Id) ON DELETE CASCADE,
    ColorKey    TEXT    NOT NULL,
    PRIMARY KEY (ProductId, PropertyId)
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

-- Zimmetler defteri: ürün tablosundan bağımsız kayıt defteri.
-- ProductId BİLİNÇLİ olarak veritabanı ilişkisi (foreign key) OLARAK TANIMLANMADI:
-- ürün silinse ya da hurdaya gitse bile defterdeki kayıt bozulmasın diye TypeName,
-- SystemName ve SerialNo kayda donmuş metin olarak yazılır.
CREATE TABLE IF NOT EXISTS Assignments (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductId     INTEGER,
    TypeName      TEXT    NOT NULL,
    SystemName    TEXT,
    SerialNo      TEXT,
    Quantity      INTEGER NOT NULL DEFAULT 1,
    PersonName    TEXT    NOT NULL,
    RegistryNo    TEXT,
    Department    TEXT,
    AssignedAt    TEXT    NOT NULL,
    AssignedNote  TEXT,
    IsReturned    INTEGER NOT NULL DEFAULT 0,
    ReturnedAt    TEXT,
    ReturnedNote  TEXT
);

CREATE INDEX IF NOT EXISTS IX_Assignments_Product
    ON Assignments (ProductId, IsReturned);

-- Tutanaklar defteri: teslim-tesellüm tutanakları. Ürünlere bağlı değildir;
-- malzeme satırları yazıldığı gibi (saf metin) saklanır.
CREATE TABLE IF NOT EXISTS Handovers (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    FromUnit      TEXT    NOT NULL,
    ToUnit        TEXT    NOT NULL,
    Category      TEXT,
    HandoverDate  TEXT    NOT NULL,
    CreatedAt     TEXT    NOT NULL,
    UpdatedAt     TEXT
);

CREATE TABLE IF NOT EXISTS HandoverItems (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    HandoverId   INTEGER NOT NULL REFERENCES Handovers(Id),
    SortOrder    INTEGER NOT NULL DEFAULT 0,
    SerialNo     TEXT,
    ItemType     TEXT,
    Quantity     TEXT,
    Note         TEXT
);
-- Notlar defteri: Excel benzeri sayfalar. Her sayfa tek satırdır; hücreler Data sütununda tek metin olarak durur.
CREATE TABLE IF NOT EXISTS NoteSheets (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    Name          TEXT    NOT NULL,
    SortOrder     INTEGER NOT NULL DEFAULT 0,
    ColumnCount   INTEGER NOT NULL DEFAULT 20,
    ColumnWidths  TEXT,
    Data          TEXT,
    UpdatedAt     TEXT
);

";
    }
}