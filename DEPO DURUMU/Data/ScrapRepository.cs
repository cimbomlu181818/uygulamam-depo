using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;

namespace DEPO_DURUMU.Data
{
    public class ScrapProduct
    {
        public int Id { get; set; }
        public int? TypeId { get; set; }
        public string TypeName { get; set; }
        public int OriginalSortOrder { get; set; }
        public int Quantity { get; set; }
        public string ScrappedAt { get; set; }
    }

    public class ScrapValue
    {
        public int? PropertyId { get; set; }
        public string PropertyName { get; set; }
        public string DataType { get; set; }
        public bool IsSerialNumber { get; set; }
        public string TextValue { get; set; }
    }

    /// <summary>
    /// Hurdadan geri getirirken, aynı Seri No'lu bir ürün depoda zaten duruyorsa
    /// kullanıcının verebileceği karar.
    /// </summary>
    public enum RestoreConflictChoice
    {
        /// <summary>Geri getirmeden vazgeç.</summary>
        Cancel,

        /// <summary>Depodaki ürünün bilgilerini ve adedini hurdadaki kayıtla değiştir.</summary>
        Overwrite,

        /// <summary>Ayrı, yeni bir ürün olarak getir (çakışan Seri No boş gelir).</summary>
        SeparateProduct
    }

    /// <summary>
    /// Ana sayfa aramasında, hurdadaki bir cinse ait sonuçları (o cinsin sütunlarıyla) tutar.
    /// </summary>
    public class ScrapSearchGroup
    {
        public string TypeName { get; set; }

        /// <summary>Gizli "__ScrapId" sütunu + özellik sütunları + en sağda "Hurda" yazan durum sütunu.</summary>
        public DataView View { get; set; }
    }

    /// <summary>
    /// Hurda: gerçek depodan tamamen bağımsız, donmuş ürün kayıtları.
    /// Bir ürün hurdaya taşındığında o anki tüm bilgisi (cins adı, özellik adları,
    /// değerleri) burada ayrı bir kopya olarak saklanır; gerçek depoda o cins ya da
    /// özellik silinse/değişse bile bu kayıt etkilenmez. Geri getirilirken gerçek
    /// depoyla (gerekirse eksikleri otomatik oluşturarak) yeniden bağlanır.
    /// </summary>
    public static class ScrapRepository
    {
        public const string SearchIdColumn = "__ScrapId";
        public const string SearchStatusColumn = "__Status";
        public const string SearchQuantityColumn = "__Adet";

        /// <summary>Restore, kullanıcı çakışma sorusunda vazgeçerse bu metni döner (hata sayılmaz).</summary>
        public const string RestoreCancelled = "__RestoreCancelled";

        /// <summary>
        /// Bir ürünü (ya da adedinin bir kısmını) hurdaya taşır: önce o anki tüm bilgisini
        /// donmuş bir kopya olarak buraya yazar. Ürünün tamamı taşınıyorsa gerçek depodan
        /// tamamen silinir; sadece bir kısmı taşınıyorsa ürün depoda kalır ve adedi düşer.
        /// quantity verilmezse (ya da ürünün adedinden büyükse) ürünün tamamı taşınır.
        /// </summary>
        public static void MoveToScrap(int productId, ProductType type, int quantity = 0)
        {
            var currentQuantity = ProductRepository.GetQuantity(productId);
            if (currentQuantity < 1)
            {
                currentQuantity = 1;
            }

            var moveQuantity = (quantity < 1 || quantity > currentQuantity) ? currentQuantity : quantity;
            var isPartial = moveQuantity < currentQuantity;

            var rank = ProductRepository.GetRank(productId, type.Id);
            var properties = TypePropertyRepository.GetForType(type.Id);
            var values = ProductRepository.GetValues(productId);
            int scrapProductIdForLog;

            using (var connection = Database.OpenConnection())
            {
                int scrapProductId;

                using (var insertCommand = connection.CreateCommand())
                {
                    insertCommand.CommandText =
                        "INSERT INTO ScrapProducts (TypeId, TypeName, OriginalSortOrder, Quantity, ScrappedAt) " +
                        "VALUES (@typeId, @typeName, @order, @quantity, @scrappedAt); SELECT last_insert_rowid();";
                    insertCommand.Parameters.Add(new SQLiteParameter("@typeId", type.Id));
                    insertCommand.Parameters.Add(new SQLiteParameter("@typeName", type.Name));
                    insertCommand.Parameters.Add(new SQLiteParameter("@order", rank));
                    insertCommand.Parameters.Add(new SQLiteParameter("@quantity", moveQuantity));
                    insertCommand.Parameters.Add(new SQLiteParameter("@scrappedAt",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));

                    scrapProductId = Convert.ToInt32(insertCommand.ExecuteScalar());
                    scrapProductIdForLog = scrapProductId;
                }

                foreach (var property in properties)
                {
                    var value = values.ContainsKey(property.Id) ? values[property.Id] : "";

                    using (var valueCommand = connection.CreateCommand())
                    {
                        valueCommand.CommandText =
                            "INSERT INTO ScrapProductValues " +
                            "(ScrapProductId, PropertyId, PropertyName, DataType, IsSerialNumber, TextValue) " +
                            "VALUES (@scrapProductId, @propertyId, @propertyName, @dataType, @isSerial, @value);";
                        valueCommand.Parameters.Add(new SQLiteParameter("@scrapProductId", scrapProductId));
                        valueCommand.Parameters.Add(new SQLiteParameter("@propertyId", property.Id));
                        valueCommand.Parameters.Add(new SQLiteParameter("@propertyName", property.Name));
                        valueCommand.Parameters.Add(new SQLiteParameter("@dataType", property.DataType));
                        valueCommand.Parameters.Add(new SQLiteParameter("@isSerial", property.IsSerialNumber ? 1 : 0));
                        valueCommand.Parameters.Add(new SQLiteParameter("@value", value));
                        valueCommand.ExecuteNonQuery();
                    }
                }
            }

            // Donmuş kopya güvenle yazıldı. Tamamı taşındıysa gerçek depodan tamamen kaldır,
            // kısmen taşındıysa ürün depoda kalır, sadece adedi düşer.
            if (isPartial)
            {
                ProductRepository.SetQuantity(productId, currentQuantity - moveQuantity);
            }
            else
            {
                ProductRepository.Delete(productId);
            }

            LogRepository.Add(
                type.Name,
                BuildDescription(GetValues(scrapProductIdForLog), moveQuantity),
                isPartial ? "Hurdaya taşındı (kısmi)" : "Hurdaya taşındı");
        }

        /// <summary>
        /// Hurdadaki tüm kayıtları, en yeni taşınan en üstte olacak şekilde getirir.
        /// </summary>
        public static List<ScrapProduct> GetAll()
        {
            var list = new List<ScrapProduct>();

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT Id, TypeId, TypeName, OriginalSortOrder, Quantity, ScrappedAt " +
                    "FROM ScrapProducts ORDER BY Id DESC;";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new ScrapProduct
                        {
                            Id = reader.GetInt32(0),
                            TypeId = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1),
                            TypeName = reader.GetString(2),
                            OriginalSortOrder = reader.GetInt32(3),
                            Quantity = reader.GetInt32(4),
                            ScrappedAt = reader.GetString(5)
                        });
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// Bir hurda kaydının, o an donmuş olan özellik/değerlerini getirir.
        /// </summary>
        public static List<ScrapValue> GetValues(int scrapProductId)
        {
            var list = new List<ScrapValue>();

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT PropertyId, PropertyName, DataType, IsSerialNumber, TextValue " +
                    "FROM ScrapProductValues WHERE ScrapProductId = @id ORDER BY Id;";
                command.Parameters.Add(new SQLiteParameter("@id", scrapProductId));

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new ScrapValue
                        {
                            PropertyId = reader.IsDBNull(0) ? (int?)null : reader.GetInt32(0),
                            PropertyName = reader.GetString(1),
                            DataType = reader.GetString(2),
                            IsSerialNumber = reader.GetInt32(3) == 1,
                            TextValue = reader.IsDBNull(4) ? "" : reader.GetString(4)
                        });
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// Hurdadaki TÜM kayıtların değerlerini tek sorguda getirir (hurda kaydı Id'sine göre gruplu).
        /// Hurda tablosunu gerçek depo gibi listelerken kullanılır.
        /// </summary>
        public static Dictionary<int, List<ScrapValue>> GetAllValuesGrouped()
        {
            var result = new Dictionary<int, List<ScrapValue>>();

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT ScrapProductId, PropertyId, PropertyName, DataType, IsSerialNumber, TextValue " +
                    "FROM ScrapProductValues ORDER BY Id;";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var scrapProductId = reader.GetInt32(0);

                        List<ScrapValue> list;
                        if (!result.TryGetValue(scrapProductId, out list))
                        {
                            list = new List<ScrapValue>();
                            result[scrapProductId] = list;
                        }

                        list.Add(new ScrapValue
                        {
                            PropertyId = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1),
                            PropertyName = reader.GetString(2),
                            DataType = reader.GetString(3),
                            IsSerialNumber = reader.GetInt32(4) == 1,
                            TextValue = reader.IsDBNull(5) ? "" : reader.GetString(5)
                        });
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Ana sayfa araması için: yazılan metni hurdadaki TÜM kayıtların TÜM değerlerinde arar,
        /// sonuçları cinse göre gruplar. Her satırın en sağında "Hurda" yazan bir durum sütunu bulunur.
        /// </summary>
        public static List<ScrapSearchGroup> Search(string text)
        {
            var groups = new List<ScrapSearchGroup>();
            var matchedIds = new HashSet<int>();

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT DISTINCT ScrapProductId FROM ScrapProductValues WHERE TextValue LIKE @pattern;";
                command.Parameters.Add(new SQLiteParameter("@pattern", "%" + text + "%"));

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        matchedIds.Add(reader.GetInt32(0));
                    }
                }
            }

            if (matchedIds.Count == 0)
            {
                return groups;
            }

            var items = GetAll().Where(i => matchedIds.Contains(i.Id)).OrderBy(i => i.Id).ToList();
            var allValues = GetAllValuesGrouped();

            foreach (var typeGroup in items.GroupBy(i => i.TypeName).OrderBy(g => g.Key))
            {
                var propertyNames = new List<string>();
                foreach (var item in typeGroup)
                {
                    List<ScrapValue> values;
                    if (!allValues.TryGetValue(item.Id, out values))
                    {
                        continue;
                    }

                    foreach (var value in values)
                    {
                        if (!propertyNames.Contains(value.PropertyName))
                        {
                            propertyNames.Add(value.PropertyName);
                        }
                    }
                }

                var table = new DataTable();
                table.Columns.Add(SearchIdColumn, typeof(int));
                foreach (var name in propertyNames)
                {
                    table.Columns.Add(name, typeof(string));
                }
                table.Columns.Add(SearchQuantityColumn, typeof(string));
                table.Columns.Add(SearchStatusColumn, typeof(string));

                foreach (var item in typeGroup)
                {
                    List<ScrapValue> values;
                    if (!allValues.TryGetValue(item.Id, out values))
                    {
                        values = new List<ScrapValue>();
                    }

                    var row = table.NewRow();
                    row[SearchIdColumn] = item.Id;
                    foreach (var name in propertyNames)
                    {
                        var found = values.FirstOrDefault(v => v.PropertyName == name);
                        row[name] = found != null ? found.TextValue : "";
                    }
                    row[SearchQuantityColumn] = item.Quantity.ToString();
                    row[SearchStatusColumn] = "Hurda";
                    table.Rows.Add(row);
                }

                groups.Add(new ScrapSearchGroup { TypeName = typeGroup.Key, View = table.DefaultView });
            }

            return groups;
        }

        /// <summary>
        /// Verilen Seri No değerinin, hurdada duran bir üründe kullanılıp
        /// kullanılmadığını kontrol eder. Depoda yeni ürün eklerken/düzenlerken de
        /// bu kontrol yapılmalı ki hurdadaki bir Seri No tekrar kullanılmasın.
        /// </summary>
        public static bool IsSerialNumberUsed(string value)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT COUNT(*) FROM ScrapProductValues " +
                    "WHERE IsSerialNumber = 1 AND TextValue = @value;";
                command.Parameters.Add(new SQLiteParameter("@value", value));

                var count = Convert.ToInt32(command.ExecuteScalar());
                return count > 0;
            }
        }

        /// <summary>
        /// Bir hurda kaydını gerçek depoya geri getirir.
        /// Cins/özellikler hâlâ (Id üzerinden) duruyorsa doğrudan onlar kullanılır;
        /// siliniyorsa saklı isimle yeniden oluşturulur. Ürün, hurdadaki adediyle ve eski
        /// sırasına (mümkün değilse listenin sonuna) yerleştirilir.
        /// Aynı Seri No'lu bir ürün depoda zaten duruyorsa karar kullanıcıya bırakılır:
        /// onConflict verilmişse ona sorulur (eskinin üzerine yaz / ayrı ürün olarak getir /
        /// vazgeç); verilmemişse geri getirme durdurulur.
        /// Sonuç: başarılıysa null, vazgeçildiyse RestoreCancelled, başarısızsa kullanıcıya
        /// gösterilecek hata mesajı.
        /// </summary>
        public static string Restore(int scrapProductId, Func<string, RestoreConflictChoice> onConflict = null)
        {
            List<ScrapValue> scrapValuesForLog = null;
            string typeNameForLog = null;
            var quantityForLog = 1;
            var actionForLog = "Hurdadan geri getirildi";

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT TypeId, TypeName, OriginalSortOrder, Quantity FROM ScrapProducts WHERE Id = @id;";
                command.Parameters.Add(new SQLiteParameter("@id", scrapProductId));

                int? storedTypeId = null;
                string typeName = null;
                var originalSortOrder = 1;
                var scrapQuantity = 1;

                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return "Bu hurda kaydı bulunamadı.";
                    }

                    storedTypeId = reader.IsDBNull(0) ? (int?)null : reader.GetInt32(0);
                    typeName = reader.GetString(1);
                    originalSortOrder = reader.GetInt32(2);
                    scrapQuantity = reader.GetInt32(3);
                }

                var scrapValues = GetValues(scrapProductId);

                // 1) Seri No çakışması var mı diye, hiçbir şeyi değiştirmeden önce kontrol et.
                var conflictPropertyIds = new HashSet<int>();
                var conflictTargets = new HashSet<int>();
                string conflictText = null;

                foreach (var scrapValue in scrapValues)
                {
                    if (!scrapValue.IsSerialNumber || string.IsNullOrEmpty(scrapValue.TextValue) ||
                        !scrapValue.PropertyId.HasValue)
                    {
                        continue;
                    }

                    var existingId = ProductRepository.FindByValue(scrapValue.PropertyId.Value, scrapValue.TextValue);
                    if (!existingId.HasValue)
                    {
                        continue;
                    }

                    conflictPropertyIds.Add(scrapValue.PropertyId.Value);
                    conflictTargets.Add(existingId.Value);
                    if (conflictText == null)
                    {
                        conflictText = scrapValue.PropertyName + ": " + scrapValue.TextValue;
                    }
                }

                var conflictChoice = RestoreConflictChoice.SeparateProduct;
                var hasConflict = conflictPropertyIds.Count > 0;

                if (hasConflict)
                {
                    if (onConflict == null)
                    {
                        return "\"" + conflictText + "\" artık depoda başka bir üründe kullanılıyor. " +
                               "Geri getirilemedi; önce o üründeki çakışmayı çözün.";
                    }

                    if (conflictTargets.Count > 1)
                    {
                        return "\"" + conflictText + "\" birden fazla depo ürünüyle çakışıyor. " +
                               "Geri getirilemedi; önce çakışmaları çözün.";
                    }

                    conflictChoice = onConflict(conflictText);
                    if (conflictChoice == RestoreConflictChoice.Cancel)
                    {
                        return RestoreCancelled;
                    }
                }

                // 2) Ürün cinsini bul: önce eski Id hâlâ geçerli mi bak, yoksa isme
                //    göre ara, o da yoksa aynı isimle yeniden oluştur.
                ProductType type = null;
                if (storedTypeId.HasValue)
                {
                    type = ProductTypeRepository.GetById(storedTypeId.Value);
                }
                if (type == null)
                {
                    type = ProductTypeRepository.GetByName(typeName);
                }
                if (type == null)
                {
                    var newTypeId = ProductTypeRepository.Add(typeName);
                    type = new ProductType { Id = newTypeId, Name = typeName };
                }

                scrapValuesForLog = scrapValues;
                typeNameForLog = typeName;
                quantityForLog = scrapQuantity;

                if (hasConflict && conflictChoice == RestoreConflictChoice.Overwrite)
                {
                    // 3a) Depodaki ürünün üzerine yaz: bilgileri ve adedi hurdadaki gibi olur.
                    var targetId = conflictTargets.First();

                    if (!ProductRepository.GetForType(type.Id).Any(p => p.Id == targetId))
                    {
                        return "Çakışan ürün başka bir ürün cinsinde duruyor, üzerine yazılamaz. " +
                               "İstersen \"ayrı ürün olarak getir\" seçeneğini kullan.";
                    }

                    WriteValues(type, targetId, scrapValues, null);
                    ProductRepository.SetQuantity(targetId, scrapQuantity);
                    actionForLog = "Hurdadan geri getirildi (depodaki ürünün üzerine yazıldı)";
                }
                else
                {
                    // 3b) Yeni ürün oluştur (listenin sonuna eklenir, sonra eski sırasına taşınır).
                    //     Ayrı ürün seçildiyse çakışan Seri No boş bırakılır (Seri No tekil kalsın).
                    var newProductId = ProductRepository.Add(type.Id, scrapQuantity);
                    ProductRepository.SetPosition(newProductId, type.Id, originalSortOrder);

                    WriteValues(type, newProductId, scrapValues, conflictPropertyIds);

                    if (hasConflict)
                    {
                        actionForLog = "Hurdadan geri getirildi (ayrı ürün, Seri No boş)";
                    }
                }
            }

            // 4) Hurdadan kaldır (artık gerçek depoda duruyor). Bu iç silme ayrıca loglanmaz.
            var restoredDescription = BuildDescription(scrapValuesForLog, quantityForLog);
            var restoredTypeName = typeNameForLog;
            DeleteRows(scrapProductId);

            LogRepository.Add(restoredTypeName, restoredDescription, actionForLog);
            return null;
        }

        /// <summary>
        /// Hurdadaki değerleri gerçek depodaki bir ürüne yazar; özellik yoksa saklı isimle
        /// oluşturur. blankPropertyIds içindeki özelliklerin değeri boş bırakılır.
        /// </summary>
        private static void WriteValues(
            ProductType type, int productId, List<ScrapValue> scrapValues, HashSet<int> blankPropertyIds)
        {
            foreach (var scrapValue in scrapValues)
            {
                PropertyDefinition property = null;
                if (scrapValue.PropertyId.HasValue)
                {
                    property = PropertyDefinitionRepository.GetById(scrapValue.PropertyId.Value);
                }
                if (property == null)
                {
                    property = PropertyDefinitionRepository.GetByName(scrapValue.PropertyName);
                }
                if (property == null)
                {
                    var newPropertyId = PropertyDefinitionRepository.Add(
                        scrapValue.PropertyName, scrapValue.DataType, scrapValue.IsSerialNumber);
                    property = new PropertyDefinition
                    {
                        Id = newPropertyId,
                        Name = scrapValue.PropertyName,
                        DataType = scrapValue.DataType,
                        IsSerialNumber = scrapValue.IsSerialNumber
                    };
                }

                TypePropertyRepository.AddPropertyToTypeIfMissing(type.Id, property.Id);

                var blank = blankPropertyIds != null &&
                            scrapValue.PropertyId.HasValue &&
                            blankPropertyIds.Contains(scrapValue.PropertyId.Value);

                ProductRepository.SetValue(productId, property.Id, blank ? "" : scrapValue.TextValue);
            }
        }

        /// <summary>
        /// Bir hurda kaydını, geri getirmeden, kalıcı olarak siler.
        /// </summary>
        public static void DeletePermanently(int scrapProductId)
        {
            var typeName = GetTypeName(scrapProductId);
            var description = BuildDescription(GetValues(scrapProductId), GetScrapQuantity(scrapProductId));

            DeleteRows(scrapProductId);

            LogRepository.Add(typeName, description, "Hurdadan kalıcı silindi");
        }

        /// <summary>Hurda kaydının satırlarını siler (log yazmaz; iç kullanım).</summary>
        private static void DeleteRows(int scrapProductId)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "DELETE FROM ScrapProductValues WHERE ScrapProductId = @id; " +
                    "DELETE FROM ScrapProducts WHERE Id = @id;";
                command.Parameters.Add(new SQLiteParameter("@id", scrapProductId));
                command.ExecuteNonQuery();
            }
        }

        private static int GetScrapQuantity(int scrapProductId)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Quantity FROM ScrapProducts WHERE Id = @id;";
                command.Parameters.Add(new SQLiteParameter("@id", scrapProductId));
                var result = command.ExecuteScalar();
                return result == null || result == DBNull.Value ? 1 : Convert.ToInt32(result);
            }
        }

        private static string GetTypeName(int scrapProductId)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT TypeName FROM ScrapProducts WHERE Id = @id;";
                command.Parameters.Add(new SQLiteParameter("@id", scrapProductId));
                var result = command.ExecuteScalar();
                return result == null || result == DBNull.Value ? null : Convert.ToString(result);
            }
        }

        /// <summary>
        /// Log defterine yazılacak, o anki değerleri anlatan sabit metni oluşturur:
        /// Seri No doluysa öne alınır, ardından dolu ilk birkaç özellik eklenir.
        /// </summary>
        private static string BuildDescription(List<ScrapValue> values, int quantity)
        {
            var parts = new List<string>();

            foreach (var v in values.Where(v => v.IsSerialNumber && !string.IsNullOrEmpty(v.TextValue)))
            {
                parts.Add(v.PropertyName + ": " + v.TextValue);
            }

            foreach (var v in values)
            {
                if (parts.Count >= 3)
                {
                    break;
                }

                if (v.IsSerialNumber || string.IsNullOrEmpty(v.TextValue))
                {
                    continue;
                }

                parts.Add(v.PropertyName + ": " + v.TextValue);
            }

            if (quantity > 1)
            {
                parts.Add("Adet: " + quantity);
            }

            return string.Join(" | ", parts);
        }
    }
}