using System.Collections.Generic;
using System.Data.SQLite;

namespace DEPO_DURUMU.Data
{
    public class Product
    {
        public int Id { get; set; }
        public int ProductTypeId { get; set; }
        public string CreatedAt { get; set; }
    }

    /// <summary>
    /// Ürünler (Products) ve ürünlerin özellik değerleri (ProductValues)
    /// için veritabanı işlemleri.
    /// </summary>
    public static class ProductRepository
    {
        /// <summary>
        /// Bir ürün cinsine ait tüm ürünleri getirir.
        /// </summary>
        public static List<Product> GetForType(int productTypeId)
        {
            var list = new List<Product>();

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT Id, ProductTypeId, CreatedAt FROM Products " +
                    "WHERE ProductTypeId = @typeId ORDER BY Id;";
                command.Parameters.Add(new SQLiteParameter("@typeId", productTypeId));

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new Product
                        {
                            Id = reader.GetInt32(0),
                            ProductTypeId = reader.GetInt32(1),
                            CreatedAt = reader.GetString(2)
                        });
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// Yeni bir ürün satırı oluşturur ve Id'sini döner.
        /// </summary>
        public static int Add(int productTypeId)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "INSERT INTO Products (ProductTypeId, CreatedAt) " +
                    "VALUES (@typeId, @createdAt); SELECT last_insert_rowid();";
                command.Parameters.Add(new SQLiteParameter("@typeId", productTypeId));
                command.Parameters.Add(new SQLiteParameter("@createdAt",
                    System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));

                var result = command.ExecuteScalar();
                return System.Convert.ToInt32(result);
            }
        }

        /// <summary>
        /// Bir ürünün bir özelliği için değeri kaydeder (varsa üzerine yazar).
        /// </summary>
        public static void SetValue(int productId, int propertyId, string textValue)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "INSERT INTO ProductValues (ProductId, PropertyId, TextValue) " +
                    "VALUES (@productId, @propertyId, @value) " +
                    "ON CONFLICT(ProductId, PropertyId) DO UPDATE SET TextValue = @value;";
                command.Parameters.Add(new SQLiteParameter("@productId", productId));
                command.Parameters.Add(new SQLiteParameter("@propertyId", propertyId));
                command.Parameters.Add(new SQLiteParameter("@value", (object)textValue ?? System.DBNull.Value));
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Bir ürünün tüm değerlerini, ÖzellikId -> Değer sözlüğü olarak getirir.
        /// </summary>
        public static Dictionary<int, string> GetValues(int productId)
        {
            var values = new Dictionary<int, string>();

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT PropertyId, TextValue FROM ProductValues WHERE ProductId = @productId;";
                command.Parameters.Add(new SQLiteParameter("@productId", productId));

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var propertyId = reader.GetInt32(0);
                        var value = reader.IsDBNull(1) ? "" : reader.GetString(1);
                        values[propertyId] = value;
                    }
                }
            }

            return values;
        }

        /// <summary>
        /// Belirli bir özellik için, verilen değerin başka bir üründe zaten
        /// kullanılıp kullanılmadığını kontrol eder (Seri No tekillik kuralı için).
        /// </summary>
        public static bool IsValueUsedByAnotherProduct(int propertyId, string value, int excludeProductId)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT COUNT(*) FROM ProductValues " +
                    "WHERE PropertyId = @propertyId AND TextValue = @value AND ProductId != @excludeId;";
                command.Parameters.Add(new SQLiteParameter("@propertyId", propertyId));
                command.Parameters.Add(new SQLiteParameter("@value", value));
                command.Parameters.Add(new SQLiteParameter("@excludeId", excludeProductId));

                var count = System.Convert.ToInt32(command.ExecuteScalar());
                return count > 0;
            }
        }

        /// <summary>
        /// Bir ürünü ve tüm değerlerini siler.
        /// </summary>
        public static void Delete(int productId)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "DELETE FROM ProductValues WHERE ProductId = @id; " +
                    "DELETE FROM Products WHERE Id = @id;";
                command.Parameters.Add(new SQLiteParameter("@id", productId));
                command.ExecuteNonQuery();
            }
        }
    }
}