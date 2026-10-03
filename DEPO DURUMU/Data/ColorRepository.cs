using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace DEPO_DURUMU.Data
{
    /// <summary>Paletteki bir renk: kısa kod (Key), görünen ad ve renk kodu (Hex).</summary>
    public class ColorChoice
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Hex { get; set; }
    }

    /// <summary>
    /// Renk özelliği: 12 renklik sabit palet, her rengin anlamı (kullanıcı yazar)
    /// ve hangi hücrenin (ürün + özellik) hangi renge boyandığı burada tutulur.
    /// </summary>
    public static class ColorRepository
    {
        public static readonly List<ColorChoice> Palette = new List<ColorChoice>
        {
            new ColorChoice { Key = "kirmizi",     Name = "Kırmızı",     Hex = "#E24B4A" },
            new ColorChoice { Key = "turuncu",     Name = "Turuncu",     Hex = "#F28C28" },
            new ColorChoice { Key = "sari",        Name = "Sarı",        Hex = "#F2D230" },
            new ColorChoice { Key = "acikyesil",   Name = "Açık yeşil",  Hex = "#97C459" },
            new ColorChoice { Key = "yesil",       Name = "Yeşil",       Hex = "#3B8F3B" },
            new ColorChoice { Key = "turkuaz",     Name = "Turkuaz",     Hex = "#1D9E75" },
            new ColorChoice { Key = "acikmavi",    Name = "Açık mavi",   Hex = "#85B7EB" },
            new ColorChoice { Key = "mavi",        Name = "Mavi",        Hex = "#378ADD" },
            new ColorChoice { Key = "mor",         Name = "Mor",         Hex = "#7F77DD" },
            new ColorChoice { Key = "pembe",       Name = "Pembe",       Hex = "#D4537E" },
            new ColorChoice { Key = "kahverengi",  Name = "Kahverengi",  Hex = "#8B5A2B" },
            new ColorChoice { Key = "gri",         Name = "Gri",         Hex = "#A0A0A0" }
        };

        /// <summary>Kısa koddan renk bilgisini bulur; yoksa null.</summary>
        public static ColorChoice FindChoice(string key)
        {
            foreach (var choice in Palette)
            {
                if (choice.Key == key)
                {
                    return choice;
                }
            }
            return null;
        }

        /// <summary>Bu rengin anlamını verir. Henüz yazılmadıysa null döner.</summary>
        public static string GetMeaning(string colorKey)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Meaning FROM ColorMeanings WHERE ColorKey = @key;";
                command.Parameters.Add(new SQLiteParameter("@key", colorKey));
                var result = command.ExecuteScalar();
                var text = result as string;
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }

        /// <summary>Anlamı yazılmış tüm renkler: renk kodu → anlam.</summary>
        public static Dictionary<string, string> GetAllMeanings()
        {
            var result = new Dictionary<string, string>();

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT ColorKey, Meaning FROM ColorMeanings;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result[reader.GetString(0)] = reader.GetString(1);
                    }
                }
            }

            return result;
        }

        /// <summary>Bir rengin anlamını yazar (varsa değiştirir).</summary>
        public static void SetMeaning(string colorKey, string meaning)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "INSERT OR REPLACE INTO ColorMeanings (ColorKey, Meaning) VALUES (@key, @meaning);";
                command.Parameters.Add(new SQLiteParameter("@key", colorKey));
                command.Parameters.Add(new SQLiteParameter("@meaning", (meaning ?? "").Trim()));
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Bir ürün cinsindeki boyalı hücreler: ürün no → (özellik no → renk kodu).
        /// </summary>
        public static Dictionary<int, Dictionary<int, string>> GetCellColors(int productTypeId)
        {
            var result = new Dictionary<int, Dictionary<int, string>>();

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT c.ProductId, c.PropertyId, c.ColorKey " +
                    "FROM CellColors c INNER JOIN Products p ON p.Id = c.ProductId " +
                    "WHERE p.ProductTypeId = @typeId;";
                command.Parameters.Add(new SQLiteParameter("@typeId", productTypeId));
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var productId = reader.GetInt32(0);
                        Dictionary<int, string> perProperty;
                        if (!result.TryGetValue(productId, out perProperty))
                        {
                            perProperty = new Dictionary<int, string>();
                            result[productId] = perProperty;
                        }
                        perProperty[reader.GetInt32(1)] = reader.GetString(2);
                    }
                }
            }

            return result;
        }

        /// <summary>Bir hücreyi boyar (zaten boyalıysa rengini değiştirir).</summary>
        public static void SetCellColor(int productId, int propertyId, string colorKey)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "INSERT OR REPLACE INTO CellColors (ProductId, PropertyId, ColorKey) " +
                    "VALUES (@productId, @propertyId, @key);";
                command.Parameters.Add(new SQLiteParameter("@productId", productId));
                command.Parameters.Add(new SQLiteParameter("@propertyId", propertyId));
                command.Parameters.Add(new SQLiteParameter("@key", colorKey));
                command.ExecuteNonQuery();
            }
        }

        /// <summary>Hücrenin rengini kaldırır.</summary>
        public static void ClearCellColor(int productId, int propertyId)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "DELETE FROM CellColors WHERE ProductId = @productId AND PropertyId = @propertyId;";
                command.Parameters.Add(new SQLiteParameter("@productId", productId));
                command.Parameters.Add(new SQLiteParameter("@propertyId", propertyId));
                command.ExecuteNonQuery();
            }
        }
    }
}