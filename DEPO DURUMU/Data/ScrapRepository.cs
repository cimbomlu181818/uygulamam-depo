using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace DEPO_DURUMU.Data
{
    public class ScrapProduct
    {
        public int Id { get; set; }
        public int? TypeId { get; set; }
        public string TypeName { get; set; }
        public int OriginalSortOrder { get; set; }
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
    /// Hurda: gerçek depodan tamamen bağımsız, donmuş ürün kayıtları.
    /// Bir ürün hurdaya taşındığında o anki tüm bilgisi (cins adı, özellik adları,
    /// değerleri) burada ayrı bir kopya olarak saklanır; gerçek depoda o cins ya da
    /// özellik silinse/değişse bile bu kayıt etkilenmez. Geri getirilirken gerçek
    /// depoyla (gerekirse eksikleri otomatik oluşturarak) yeniden bağlanır.
    /// </summary>
    public static class ScrapRepository
    {
        /// <summary>
        /// Bir ürünü hurdaya taşır: önce tüm bilgisini donmuş bir kopya olarak
        /// buraya yazar, sonra gerçek depodan (Products/ProductValues) tamamen siler.
        /// </summary>
        public static void MoveToScrap(int productId, ProductType type)
        {
            var rank = ProductRepository.GetRank(productId, type.Id);
            var properties = TypePropertyRepository.GetForType(type.Id);
            var values = ProductRepository.GetValues(productId);

            using (var connection = Database.OpenConnection())
            {
                int scrapProductId;

                using (var insertCommand = connection.CreateCommand())
                {
                    insertCommand.CommandText =
                        "INSERT INTO ScrapProducts (TypeId, TypeName, OriginalSortOrder, ScrappedAt) " +
                        "VALUES (@typeId, @typeName, @order, @scrappedAt); SELECT last_insert_rowid();";
                    insertCommand.Parameters.Add(new SQLiteParameter("@typeId", type.Id));
                    insertCommand.Parameters.Add(new SQLiteParameter("@typeName", type.Name));
                    insertCommand.Parameters.Add(new SQLiteParameter("@order", rank));
                    insertCommand.Parameters.Add(new SQLiteParameter("@scrappedAt",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));

                    scrapProductId = Convert.ToInt32(insertCommand.ExecuteScalar());
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

            // Donmuş kopya güvenle yazıldı; şimdi gerçek depodan tamamen kaldır.
            ProductRepository.Delete(productId);
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
                    "SELECT Id, TypeId, TypeName, OriginalSortOrder, ScrappedAt " +
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
                            ScrappedAt = reader.GetString(4)
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
        /// siliniyorsa saklı isimle yeniden oluşturulur. Ürün eski sırasına
        /// (mümkün değilse listenin sonuna) yerleştirilir.
        /// Sonuç: başarılıysa null, başarısızsa kullanıcıya gösterilecek hata mesajı.
        /// </summary>
        public static string Restore(int scrapProductId)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT TypeId, TypeName, OriginalSortOrder FROM ScrapProducts WHERE Id = @id;";
                command.Parameters.Add(new SQLiteParameter("@id", scrapProductId));

                int? storedTypeId = null;
                string typeName = null;
                var originalSortOrder = 1;

                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return "Bu hurda kaydı bulunamadı.";
                    }

                    storedTypeId = reader.IsDBNull(0) ? (int?)null : reader.GetInt32(0);
                    typeName = reader.GetString(1);
                    originalSortOrder = reader.GetInt32(2);
                }

                // 1) Ürün cinsini bul: önce eski Id hâlâ geçerli mi bak, yoksa isme
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

                var scrapValues = GetValues(scrapProductId);

                // 2) Seri No çakışması var mı diye önceden kontrol et (hiçbir şeyi
                //    değiştirmeden önce), varsa geri getirmeyi tamamen durdur.
                foreach (var scrapValue in scrapValues)
                {
                    if (!scrapValue.IsSerialNumber || string.IsNullOrEmpty(scrapValue.TextValue))
                    {
                        continue;
                    }

                    if (scrapValue.PropertyId.HasValue &&
                        ProductRepository.IsValueUsedByAnotherProduct(
                            scrapValue.PropertyId.Value, scrapValue.TextValue, -1))
                    {
                        return "\"" + scrapValue.TextValue + "\" Seri No'su artık depoda başka bir üründe " +
                               "kullanılıyor. Geri getirilemedi; önce o üründeki çakışmayı çözün.";
                    }
                }

                // 3) Ürünü oluştur (listenin sonuna eklenir, sonra eski sırasına taşınır).
                var newProductId = ProductRepository.Add(type.Id);
                ProductRepository.SetPosition(newProductId, type.Id, originalSortOrder);

                // 4) Her değeri, özelliği bulup/oluşturup gerçek depoya yaz.
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
                    ProductRepository.SetValue(newProductId, property.Id, scrapValue.TextValue);
                }
            }

            // 5) Hurdadan kalıcı olarak kaldır (artık gerçek depoda duruyor).
            DeletePermanently(scrapProductId);
            return null;
        }

        /// <summary>
        /// Bir hurda kaydını, geri getirmeden, kalıcı olarak siler.
        /// </summary>
        public static void DeletePermanently(int scrapProductId)
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
    }
}