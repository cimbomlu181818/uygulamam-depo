using System.Collections.Generic;
using System.Data.SQLite;

namespace DEPO_DURUMU.Data
{
    public class ProductType
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// Ürün cinsleri (ör. Bilgisayar, Telsiz) için veritabanı işlemleri.
    /// </summary>
    public static class ProductTypeRepository
    {
        public static List<ProductType> GetAll()
        {
            var list = new List<ProductType>();

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Id, Name FROM ProductTypes ORDER BY Name;";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new ProductType
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1)
                        });
                    }
                }
            }

            return list;
        }

        public static int Add(string name)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "INSERT INTO ProductTypes (Name) VALUES (@name); SELECT last_insert_rowid();";
                command.Parameters.Add(new SQLiteParameter("@name", name));

                var result = command.ExecuteScalar();
                return System.Convert.ToInt32(result);
            }
        }

        public static void Rename(int id, string newName)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE ProductTypes SET Name = @name WHERE Id = @id;";
                command.Parameters.Add(new SQLiteParameter("@name", newName));
                command.Parameters.Add(new SQLiteParameter("@id", id));
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Bir ürün cinsini siler.
        /// Kural: bu cinse ait en az bir ürün varsa silmeyi engeller (false döner) —
        /// önce o ürünlerin silinmesi gerekir. Ürün kalmadıysa, cinsin özellik
        /// bağlantıları da temizlenerek cins tamamen silinir.
        /// </summary>
        public static bool Delete(int id)
        {
            using (var connection = Database.OpenConnection())
            {
                using (var checkCommand = connection.CreateCommand())
                {
                    checkCommand.CommandText =
                        "SELECT COUNT(*) FROM Products WHERE ProductTypeId = @id;";
                    checkCommand.Parameters.Add(new SQLiteParameter("@id", id));

                    var productCount = System.Convert.ToInt32(checkCommand.ExecuteScalar());
                    if (productCount > 0)
                    {
                        return false;
                    }
                }

                using (var deleteCommand = connection.CreateCommand())
                {
                    deleteCommand.CommandText =
                        "DELETE FROM TypeProperties WHERE ProductTypeId = @id; " +
                        "DELETE FROM ProductTypes WHERE Id = @id;";
                    deleteCommand.Parameters.Add(new SQLiteParameter("@id", id));
                    deleteCommand.ExecuteNonQuery();
                }
            }

            return true;
        }
    }
}