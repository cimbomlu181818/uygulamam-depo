using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;

namespace DEPO_DURUMU.Data
{
    /// <summary>
    /// Bir ürün cinsine ait arama sonuçlarını, o cinsin sütunlarıyla birlikte tutar.
    /// </summary>
    public class HomeSearchGroup
    {
        public int ProductTypeId { get; set; }
        public string TypeName { get; set; }

        /// <summary>Gizli "__ProductId" sütunu + o cinsin özellik sütunlarını içeren tablo.</summary>
        public DataView View { get; set; }
    }

    /// <summary>
    /// Ana sayfadaki genel arama kutusu için: yazılan metni TÜM ürün cinslerindeki
    /// TÜM özellik değerlerinde arar, sonuçları cinse göre gruplar.
    /// </summary>
    public static class ProductSearchRepository
    {
        public const string ProductIdColumn = "__ProductId";
        public const string QuantityColumn = "__Adet";

        public static List<HomeSearchGroup> Search(string text)
        {
            var groups = new List<HomeSearchGroup>();

            // 1) Metnin geçtiği ürünlerin Id ve cins Id'lerini bul.
            var matches = new List<KeyValuePair<int, int>>(); // ProductId, ProductTypeId

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT DISTINCT p.Id, p.ProductTypeId " +
                    "FROM Products p " +
                    "JOIN ProductValues v ON v.ProductId = p.Id " +
                    "WHERE v.TextValue LIKE @pattern " +
                    "ORDER BY p.ProductTypeId, p.Id;";
                command.Parameters.Add(new SQLiteParameter("@pattern", "%" + text + "%"));

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        matches.Add(new KeyValuePair<int, int>(reader.GetInt32(0), reader.GetInt32(1)));
                    }
                }
            }

            if (matches.Count == 0)
            {
                return groups;
            }

            var allTypes = ProductTypeRepository.GetAll().ToDictionary(t => t.Id, t => t.Name);

            // 2) Cinse göre grupla, her cins için tablo kur.
            var byType = matches.GroupBy(m => m.Value);

            foreach (var typeGroup in byType)
            {
                var typeId = typeGroup.Key;
                if (!allTypes.ContainsKey(typeId))
                {
                    continue;
                }

                var properties = TypePropertyRepository.GetForType(typeId);

                var table = new DataTable();
                table.Columns.Add(ProductIdColumn, typeof(int));
                foreach (var property in properties)
                {
                    table.Columns.Add(property.Name, typeof(string));
                }
                table.Columns.Add(QuantityColumn, typeof(string));

                foreach (var match in typeGroup)
                {
                    var values = ProductRepository.GetValues(match.Key);

                    var row = table.NewRow();
                    row[ProductIdColumn] = match.Key;
                    foreach (var property in properties)
                    {
                        row[property.Name] = values.ContainsKey(property.Id) ? values[property.Id] : "";
                    }
                    row[QuantityColumn] = ProductRepository.GetQuantity(match.Key).ToString();
                    table.Rows.Add(row);
                }

                groups.Add(new HomeSearchGroup
                {
                    ProductTypeId = typeId,
                    TypeName = allTypes[typeId],
                    View = table.DefaultView
                });
            }

            return groups;
        }
    }
}