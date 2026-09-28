using System.Collections.Generic;
using System.Data.SQLite;

namespace DEPO_DURUMU.Data
{
    public class Product
    {
        public int Id { get; set; }
        public int ProductTypeId { get; set; }
        public int SortOrder { get; set; }
        public int Quantity { get; set; }
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
                    "SELECT Id, ProductTypeId, SortOrder, Quantity, CreatedAt FROM Products " +
                    "WHERE ProductTypeId = @typeId ORDER BY SortOrder, Id;";
                command.Parameters.Add(new SQLiteParameter("@typeId", productTypeId));

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new Product
                        {
                            Id = reader.GetInt32(0),
                            ProductTypeId = reader.GetInt32(1),
                            SortOrder = reader.GetInt32(2),
                            Quantity = reader.GetInt32(3),
                            CreatedAt = reader.GetString(4)
                        });
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// Yeni bir ürün satırı oluşturur ve Id'sini döner.
        /// </summary>
        public static int Add(int productTypeId, int quantity = 1)
        {
            using (var connection = Database.OpenConnection())
            {
                int nextOrder;
                using (var orderCommand = connection.CreateCommand())
                {
                    orderCommand.CommandText =
                        "SELECT COALESCE(MAX(SortOrder), 0) + 1 FROM Products WHERE ProductTypeId = @typeId;";
                    orderCommand.Parameters.Add(new SQLiteParameter("@typeId", productTypeId));
                    nextOrder = System.Convert.ToInt32(orderCommand.ExecuteScalar());
                }

                using (var insertCommand = connection.CreateCommand())
                {
                    insertCommand.CommandText =
                        "INSERT INTO Products (ProductTypeId, SortOrder, Quantity, CreatedAt) " +
                        "VALUES (@typeId, @order, @quantity, @createdAt); SELECT last_insert_rowid();";
                    insertCommand.Parameters.Add(new SQLiteParameter("@typeId", productTypeId));
                    insertCommand.Parameters.Add(new SQLiteParameter("@order", nextOrder));
                    insertCommand.Parameters.Add(new SQLiteParameter("@quantity", quantity < 1 ? 1 : quantity));
                    insertCommand.Parameters.Add(new SQLiteParameter("@createdAt",
                        System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));

                    var result = insertCommand.ExecuteScalar();
                    return System.Convert.ToInt32(result);
                }
            }
        }

        /// <summary>
        /// Bir ürünün adedini getirir. Ürün yoksa 0 döner.
        /// </summary>
        public static int GetQuantity(int productId)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Quantity FROM Products WHERE Id = @id;";
                command.Parameters.Add(new SQLiteParameter("@id", productId));

                var result = command.ExecuteScalar();
                return result == null || result == System.DBNull.Value ? 0 : System.Convert.ToInt32(result);
            }
        }

        /// <summary>
        /// Bir ürünün adedini değiştirir (en az 1).
        /// </summary>
        public static void SetQuantity(int productId, int quantity)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE Products SET Quantity = @quantity WHERE Id = @id;";
                command.Parameters.Add(new SQLiteParameter("@quantity", quantity < 1 ? 1 : quantity));
                command.Parameters.Add(new SQLiteParameter("@id", productId));
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Bir ürünün, kendi cinsindeki ürünler arasında kaçıncı sırada olduğunu
        /// (1'den başlayarak) bulur. Hurdaya taşırken "eski yerini" hatırlamak için kullanılır.
        /// </summary>
        public static int GetRank(int productId, int productTypeId)
        {
            var ordered = GetForType(productTypeId);
            for (var i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].Id == productId)
                {
                    return i + 1;
                }
            }

            return ordered.Count + 1;
        }

        /// <summary>
        /// Bir ürünü, kendi cinsindeki listede istenen sıraya (1'den başlayarak) yerleştirir.
        /// İstenen sıra listenin uzunluğunu aşıyorsa, en sona eklenir. Aradaki ürünlerin
        /// sırası buna göre otomatik kayar. Hurdadan geri getirilen bir ürünü eski
        /// yerine koymak için kullanılır.
        /// </summary>
        public static void SetPosition(int productId, int productTypeId, int desiredPosition)
        {
            using (var connection = Database.OpenConnection())
            {
                var orderedIds = new List<int>();

                using (var selectCommand = connection.CreateCommand())
                {
                    selectCommand.CommandText =
                        "SELECT Id FROM Products WHERE ProductTypeId = @typeId AND Id != @productId " +
                        "ORDER BY SortOrder, Id;";
                    selectCommand.Parameters.Add(new SQLiteParameter("@typeId", productTypeId));
                    selectCommand.Parameters.Add(new SQLiteParameter("@productId", productId));

                    using (var reader = selectCommand.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            orderedIds.Add(reader.GetInt32(0));
                        }
                    }
                }

                var insertIndex = desiredPosition - 1;
                if (insertIndex < 0)
                {
                    insertIndex = 0;
                }
                if (insertIndex > orderedIds.Count)
                {
                    insertIndex = orderedIds.Count;
                }

                orderedIds.Insert(insertIndex, productId);

                for (var i = 0; i < orderedIds.Count; i++)
                {
                    using (var updateCommand = connection.CreateCommand())
                    {
                        updateCommand.CommandText = "UPDATE Products SET SortOrder = @order WHERE Id = @id;";
                        updateCommand.Parameters.Add(new SQLiteParameter("@order", i + 1));
                        updateCommand.Parameters.Add(new SQLiteParameter("@id", orderedIds[i]));
                        updateCommand.ExecuteNonQuery();
                    }
                }
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
        /// Belirli bir özelliğin verilen değere sahip olduğu ilk ürünün Id'sini bulur (yoksa null).
        /// </summary>
        public static int? FindByValue(int propertyId, string value)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT ProductId FROM ProductValues " +
                    "WHERE PropertyId = @propertyId AND TextValue = @value LIMIT 1;";
                command.Parameters.Add(new SQLiteParameter("@propertyId", propertyId));
                command.Parameters.Add(new SQLiteParameter("@value", value));

                var result = command.ExecuteScalar();
                return result == null || result == System.DBNull.Value ? (int?)null : System.Convert.ToInt32(result);
            }
        }

        /// <summary>
        /// Bir ürünü ve tüm değerlerini siler.
        /// </summary>
        public static void Delete(int productId)
        {
            // Zimmette olan ürün silinemez: önce zimmet iade alınmalı.
            if (AssignmentRepository.GetActiveQuantity(productId) > 0)
            {
                throw new System.InvalidOperationException(
                    "Bu ürün zimmette olduğu için silinemez. Önce zimmeti iade alın.");
            }

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