using System.Collections.Generic;
using System.Data.SQLite;

namespace DEPO_DURUMU.Data
{
    public class PropertyDefinition
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string DataType { get; set; }   // "Metin", "Sayı", "Tarih"
        public bool IsSerialNumber { get; set; }
    }

    /// <summary>
    /// Özellik tanımları (ör. RAM, Seri No, Marka) için veritabanı işlemleri.
    /// Bu tanımlar bir kütüphane gibidir; bir ürün cinsine TypeProperties
    /// tablosu üzerinden bağlanır.
    /// </summary>
    public static class PropertyDefinitionRepository
    {
        public static List<PropertyDefinition> GetAll()
        {
            var list = new List<PropertyDefinition>();

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT Id, Name, DataType, IsSerialNumber FROM PropertyDefinitions ORDER BY Name;";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new PropertyDefinition
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            DataType = reader.GetString(2),
                            IsSerialNumber = reader.GetInt32(3) == 1
                        });
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// Verilen Id'ye sahip özelliği getirir, yoksa null döner.
        /// </summary>
        public static PropertyDefinition GetById(int id)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT Id, Name, DataType, IsSerialNumber FROM PropertyDefinitions WHERE Id = @id;";
                command.Parameters.Add(new SQLiteParameter("@id", id));

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new PropertyDefinition
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            DataType = reader.GetString(2),
                            IsSerialNumber = reader.GetInt32(3) == 1
                        };
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Verilen isme sahip özelliği getirir, yoksa null döner.
        /// </summary>
        public static PropertyDefinition GetByName(string name)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT Id, Name, DataType, IsSerialNumber FROM PropertyDefinitions WHERE Name = @name;";
                command.Parameters.Add(new SQLiteParameter("@name", name));

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new PropertyDefinition
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            DataType = reader.GetString(2),
                            IsSerialNumber = reader.GetInt32(3) == 1
                        };
                    }
                }
            }

            return null;
        }

        public static int Add(string name, string dataType, bool isSerialNumber)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "INSERT INTO PropertyDefinitions (Name, DataType, IsSerialNumber) " +
                    "VALUES (@name, @dataType, @isSerial); SELECT last_insert_rowid();";
                command.Parameters.Add(new SQLiteParameter("@name", name));
                command.Parameters.Add(new SQLiteParameter("@dataType", dataType));
                command.Parameters.Add(new SQLiteParameter("@isSerial", isSerialNumber ? 1 : 0));

                var result = command.ExecuteScalar();
                return System.Convert.ToInt32(result);
            }
        }

        public static void Rename(int id, string newName)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE PropertyDefinitions SET Name = @name WHERE Id = @id;";
                command.Parameters.Add(new SQLiteParameter("@name", newName));
                command.Parameters.Add(new SQLiteParameter("@id", id));
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Bir özelliği kütüphaneden siler.
        /// Kural: bu özellik için en az bir üründe DOLU (boş olmayan) bir değer varsa
        /// silmeyi engeller (false döner) — önce o verilerin temizlenmesi/ürünlerin
        /// silinmesi gerekir. Dolu veri yoksa, özellik hangi ürün cinslerine bağlı
        /// olursa olsun (bağlantılar dahil) tamamen silinir.
        /// </summary>
        public static bool Delete(int id)
        {
            using (var connection = Database.OpenConnection())
            {
                using (var checkCommand = connection.CreateCommand())
                {
                    checkCommand.CommandText =
                        "SELECT COUNT(*) FROM ProductValues " +
                        "WHERE PropertyId = @id AND TextValue IS NOT NULL AND TextValue != '';";
                    checkCommand.Parameters.Add(new SQLiteParameter("@id", id));

                    var usageCount = System.Convert.ToInt32(checkCommand.ExecuteScalar());
                    if (usageCount > 0)
                    {
                        return false;
                    }
                }

                using (var deleteCommand = connection.CreateCommand())
                {
                    // Dolu veri yok; boş kalan değer kayıtlarını, cins bağlantılarını
                    // ve özelliğin kendisini temizle.
                    deleteCommand.CommandText =
                        "DELETE FROM ProductValues WHERE PropertyId = @id; " +
                        "DELETE FROM TypeProperties WHERE PropertyId = @id; " +
                        "DELETE FROM PropertyDefinitions WHERE Id = @id;";
                    deleteCommand.Parameters.Add(new SQLiteParameter("@id", id));
                    deleteCommand.ExecuteNonQuery();
                }
            }

            return true;
        }

        /// <summary>
        /// Uygulama ilk açıldığında, her zaman var olması gereken sabit özellikleri
        /// (Seri No, Sistem İsmi) kütüphaneye ekler. Zaten varsa tekrar eklemez.
        /// </summary>
        public static void EnsureDefaults()
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "INSERT OR IGNORE INTO PropertyDefinitions (Name, DataType, IsSerialNumber) " +
                    "VALUES ('Seri No', 'Metin', 1);" +
                    "INSERT OR IGNORE INTO PropertyDefinitions (Name, DataType, IsSerialNumber) " +
                    "VALUES ('Sistem İsmi', 'Metin', 0);";
                command.ExecuteNonQuery();
            }
        }
    }
}