using System.Collections.Generic;
using System.Data.SQLite;

namespace DEPO_DURUMU.Data
{
    /// <summary>
    /// Ürün cinsi ile özellik tanımı arasındaki bağlantıyı (ve sıralamayı) yönetir.
    /// </summary>
    public static class TypePropertyRepository
    {
        /// <summary>
        /// Bir ürün cinsine bağlı özellikleri, ekrandaki sütun sırasına göre getirir.
        /// </summary>
        public static List<PropertyDefinition> GetForType(int productTypeId)
        {
            var list = new List<PropertyDefinition>();

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT p.Id, p.Name, p.DataType, p.IsSerialNumber " +
                    "FROM TypeProperties tp " +
                    "JOIN PropertyDefinitions p ON p.Id = tp.PropertyId " +
                    "WHERE tp.ProductTypeId = @typeId " +
                    "ORDER BY tp.SortOrder;";
                command.Parameters.Add(new SQLiteParameter("@typeId", productTypeId));

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
        /// Bir özelliği bir ürün cinsine bağlar, en sona ekler.
        /// </summary>
        public static void AddPropertyToType(int productTypeId, int propertyId)
        {
            using (var connection = Database.OpenConnection())
            {
                int nextOrder;
                using (var orderCommand = connection.CreateCommand())
                {
                    orderCommand.CommandText =
                        "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM TypeProperties WHERE ProductTypeId = @typeId;";
                    orderCommand.Parameters.Add(new SQLiteParameter("@typeId", productTypeId));
                    nextOrder = System.Convert.ToInt32(orderCommand.ExecuteScalar());
                }

                using (var insertCommand = connection.CreateCommand())
                {
                    insertCommand.CommandText =
                        "INSERT INTO TypeProperties (ProductTypeId, PropertyId, SortOrder) " +
                        "VALUES (@typeId, @propId, @order);";
                    insertCommand.Parameters.Add(new SQLiteParameter("@typeId", productTypeId));
                    insertCommand.Parameters.Add(new SQLiteParameter("@propId", propertyId));
                    insertCommand.Parameters.Add(new SQLiteParameter("@order", nextOrder));
                    insertCommand.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Bir özelliğin bir ürün cinsiyle bağlantısını kaldırır (özelliğin
        /// kendisini kütüphaneden silmez, sadece bu cinsten kaldırır).
        /// </summary>
        public static void RemovePropertyFromType(int productTypeId, int propertyId)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "DELETE FROM TypeProperties WHERE ProductTypeId = @typeId AND PropertyId = @propId;";
                command.Parameters.Add(new SQLiteParameter("@typeId", productTypeId));
                command.Parameters.Add(new SQLiteParameter("@propId", propertyId));
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Bir ürün cinsindeki özelliklerin sırasını, verilen sıraya göre günceller.
        /// </summary>
        public static void UpdateOrder(int productTypeId, List<int> propertyIdsInOrder)
        {
            using (var connection = Database.OpenConnection())
            {
                for (int i = 0; i < propertyIdsInOrder.Count; i++)
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText =
                            "UPDATE TypeProperties SET SortOrder = @order " +
                            "WHERE ProductTypeId = @typeId AND PropertyId = @propId;";
                        command.Parameters.Add(new SQLiteParameter("@order", i));
                        command.Parameters.Add(new SQLiteParameter("@typeId", productTypeId));
                        command.Parameters.Add(new SQLiteParameter("@propId", propertyIdsInOrder[i]));
                        command.ExecuteNonQuery();
                    }
                }
            }
        }
    }
}