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

        public static void Delete(int id)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM TypeProperties WHERE ProductTypeId = @id; DELETE FROM ProductTypes WHERE Id = @id;";
                command.Parameters.Add(new SQLiteParameter("@id", id));
                command.ExecuteNonQuery();
            }
        }
    }
}