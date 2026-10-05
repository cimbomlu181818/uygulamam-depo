using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace DEPO_DURUMU.Data
{
    /// <summary>
    /// Ana sayfada gösterilen bir istatistik kutusu: seçilen ürün cinsi/özellik ve
    /// o özelliğin kaç üründe doldurulmuş olduğu.
    /// </summary>
    public class HomeStatisticCard
    {
        public int Id { get; set; }
        public int ProductTypeId { get; set; }
        public string ProductTypeName { get; set; }
        public int PropertyId { get; set; }
        public string PropertyName { get; set; }

        public int Count { get; set; }

        /// <summary>Kutuda gösterilecek tam yazı. Örnek: "Bilgisayar / Seri No: 15".</summary>
        public string DisplayText
        {
            get { return ProductTypeName + " / " + PropertyName + ": " + Count; }
        }
    }

    /// <summary>Ana sayfada logonun altında gösterilen sabit genel durum sayıları.</summary>
    public class HomeSummary
    {
        public int Total { get; set; }
        public int Assigned { get; set; }
        public int Depot { get; set; }
        public int Scrap { get; set; }
    }

    /// <summary>
    /// Ana sayfadaki istatistik kutusu seçimlerini kalıcı olarak saklar ve sayılarını hesaplar.
    /// Bir kutunun baktığı özellik artık o ürün cinsine atanmış değilse (Özellik Ata'dan
    /// kaldırılmışsa) ya da cinsin kendisi silinmişse, kutu GetAll() çağrıldığında
    /// veritabanından da otomatik olarak kaldırılır.
    /// </summary>
    public static class HomeStatisticsRepository
    {
        /// <summary>
        /// Kayıtlı tüm istatistik kutularını, güncel sayılarıyla verir. Önce artık
        /// geçerli olmayan kutuları temizler.
        /// </summary>
        public static List<HomeStatisticCard> GetAll()
        {
            var list = new List<HomeStatisticCard>();

            using (var connection = Database.OpenConnection())
            {
                using (var cleanupCommand = connection.CreateCommand())
                {
                    cleanupCommand.CommandText =
                        "DELETE FROM HomeStatistics " +
                        "WHERE NOT EXISTS (" +
                        "  SELECT 1 FROM TypeProperties tp " +
                        "  WHERE tp.ProductTypeId = HomeStatistics.ProductTypeId " +
                        "    AND tp.PropertyId = HomeStatistics.PropertyId" +
                        ");";
                    cleanupCommand.ExecuteNonQuery();
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "SELECT h.Id, h.ProductTypeId, t.Name, h.PropertyId, p.Name " +
                        "FROM HomeStatistics h " +
                        "JOIN ProductTypes t ON t.Id = h.ProductTypeId " +
                        "JOIN PropertyDefinitions p ON p.Id = h.PropertyId " +
                        "ORDER BY h.SortOrder, h.Id;";

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new HomeStatisticCard
                            {
                                Id = reader.GetInt32(0),
                                ProductTypeId = reader.GetInt32(1),
                                ProductTypeName = reader.GetString(2),
                                PropertyId = reader.GetInt32(3),
                                PropertyName = reader.GetString(4)
                            });
                        }
                    }
                }

                foreach (var card in list)
                {
                    card.Count = CountFilled(connection, card.ProductTypeId, card.PropertyId);
                }
            }

            return list;
        }

        /// <summary>
        /// Genel durum: Depoda = hurdaya gitmemiş tüm ürün adedi - şu an zimmette olan adet,
        /// Zimmetli = iade edilmemiş zimmet adedi, Hurda = hurdadaki adet,
        /// Toplam = Depoda + Zimmetli + Hurda.
        /// </summary>
        public static HomeSummary GetSummary()
        {
            var summary = new HomeSummary();

            using (var connection = Database.OpenConnection())
            {
                var stock = ScalarInt(connection, "SELECT COALESCE(SUM(Quantity), 0) FROM Products;");

                summary.Assigned = ScalarInt(connection,
                    "SELECT COALESCE(SUM(a.Quantity), 0) FROM Assignments a " +
                    "JOIN Products p ON p.Id = a.ProductId WHERE a.IsReturned = 0;");

                summary.Scrap = ScalarInt(connection, "SELECT COALESCE(SUM(Quantity), 0) FROM ScrapProducts;");

                summary.Depot = Math.Max(0, stock - summary.Assigned);
                summary.Total = summary.Depot + summary.Assigned + summary.Scrap;
            }

            return summary;
        }

        private static int ScalarInt(SQLiteConnection connection, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        /// <summary>
        /// Bir ürün cinsinde, bir özelliğin kaç üründe doldurulmuş (boş olmayan) olduğunu sayar.
        /// </summary>
        private static int CountFilled(SQLiteConnection connection, int productTypeId, int propertyId)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT COUNT(*) FROM ProductValues v " +
                    "JOIN Products pr ON pr.Id = v.ProductId " +
                    "WHERE pr.ProductTypeId = @typeId AND v.PropertyId = @propertyId " +
                    "AND v.TextValue IS NOT NULL AND v.TextValue <> '';";
                command.Parameters.Add(new SQLiteParameter("@typeId", productTypeId));
                command.Parameters.Add(new SQLiteParameter("@propertyId", propertyId));

                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        /// <summary>
        /// Yeni bir istatistik kutusu ekler, listenin en sonuna koyar.
        /// </summary>
        public static void Add(int productTypeId, int propertyId)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "INSERT INTO HomeStatistics (ProductTypeId, PropertyId, SortOrder) " +
                    "VALUES (@typeId, @propertyId, " +
                    "COALESCE((SELECT MAX(SortOrder) FROM HomeStatistics), 0) + 1);";
                command.Parameters.Add(new SQLiteParameter("@typeId", productTypeId));
                command.Parameters.Add(new SQLiteParameter("@propertyId", propertyId));
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Kutuların yeni sırasını kaydeder. idsInOrder, kutuların görünmesini istediğin
        /// sırada Id listesidir (ilk eleman en başta gösterilir).
        /// </summary>
        public static void Reorder(List<int> idsInOrder)
        {
            using (var connection = Database.OpenConnection())
            {
                for (var i = 0; i < idsInOrder.Count; i++)
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText =
                            "UPDATE HomeStatistics SET SortOrder = @order WHERE Id = @id;";
                        command.Parameters.Add(new SQLiteParameter("@order", i));
                        command.Parameters.Add(new SQLiteParameter("@id", idsInOrder[i]));
                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        /// <summary>
        /// Bir istatistik kutusunu kalıcı olarak kaldırır.
        /// </summary>
        public static void Remove(int id)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM HomeStatistics WHERE Id = @id;";
                command.Parameters.Add(new SQLiteParameter("@id", id));
                command.ExecuteNonQuery();
            }
        }
    }
}