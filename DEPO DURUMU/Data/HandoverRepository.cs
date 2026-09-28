using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;

namespace DEPO_DURUMU.Data
{
    /// <summary>Teslim-tesellüm tutanağındaki bir malzeme satırı (defterde saf metin olarak saklanır).</summary>
    public class HandoverLine
    {
        public string SerialNo { get; set; }
        public string ItemType { get; set; }
        public string Quantity { get; set; }
        public string Note { get; set; }
    }

    /// <summary>
    /// Tutanaklar defterindeki bir teslim-tesellüm tutanağı. Ürünlere hiç bağlı değildir:
    /// içindeki her şey yazıldığı gibi saklanır, stok ve zimmet etkilenmez.
    /// </summary>
    public class Handover
    {
        public int Id { get; set; }
        public string FromUnit { get; set; }
        public string ToUnit { get; set; }
        public string Category { get; set; }

        /// <summary>Tutanakta basılan tarih, gg.aa.yyyy biçiminde.</summary>
        public string HandoverDate { get; set; }

        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }

        public List<HandoverLine> Items { get; set; } = new List<HandoverLine>();

        public int ItemCount
        {
            get { return Items.Count; }
        }

        public string CreatedAtText
        {
            get { return AssignmentRepository.FormatTime(CreatedAt); }
        }

        /// <summary>Listede gösterilecek kısa malzeme özeti. Örnek: "Telsiz (AB12), Bilgisayar, +2".</summary>
        public string ItemsSummary
        {
            get
            {
                var parts = new List<string>();

                foreach (var item in Items.Take(4))
                {
                    var name = string.IsNullOrWhiteSpace(item.ItemType) ? "" : item.ItemType.Trim();
                    var serial = string.IsNullOrWhiteSpace(item.SerialNo) ? "" : item.SerialNo.Trim();

                    if (name.Length > 0 && serial.Length > 0)
                    {
                        parts.Add(name + " (" + serial + ")");
                    }
                    else
                    {
                        parts.Add(name.Length > 0 ? name : serial);
                    }
                }

                var text = string.Join(", ", parts);

                if (Items.Count > 4)
                {
                    text += ", +" + (Items.Count - 4);
                }

                return text;
            }
        }

        /// <summary>Listedeki arama kutusu için: tutanağın tüm yazılarını tek metinde toplar.</summary>
        public string SearchText
        {
            get
            {
                var parts = new List<string>
                {
                    Id.ToString(), FromUnit, ToUnit, Category, HandoverDate
                };

                foreach (var item in Items)
                {
                    parts.Add(item.SerialNo);
                    parts.Add(item.ItemType);
                    parts.Add(item.Quantity);
                    parts.Add(item.Note);
                }

                return string.Join(" | ", parts.Where(p => !string.IsNullOrEmpty(p)));
            }
        }
    }

    /// <summary>
    /// Tutanaklar defterinin veri işlemleri. Kayıtlar düzenlenebilir ve silinebilir (kâğıt üzerinde
    /// yapılan düzeltmeler sisteme de işlenebilsin diye); her değişiklik işlem kaydına (Log) yazılır.
    /// </summary>
    public static class HandoverRepository
    {
        private static string Clean(string text)
        {
            return (text ?? "").Trim();
        }

        private static object NullIfEmpty(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? (object)DBNull.Value : text.Trim();
        }

        private static string ReadText(SQLiteDataReader reader, int index)
        {
            return reader.IsDBNull(index) ? "" : reader.GetString(index);
        }

        // ---------- OKUMA ----------

        private static Handover ReadHeader(SQLiteDataReader reader)
        {
            return new Handover
            {
                Id = reader.GetInt32(0),
                FromUnit = ReadText(reader, 1),
                ToUnit = ReadText(reader, 2),
                Category = ReadText(reader, 3),
                HandoverDate = ReadText(reader, 4),
                CreatedAt = ReadText(reader, 5),
                UpdatedAt = ReadText(reader, 6)
            };
        }

        private const string HeaderColumns =
            "SELECT Id, FromUnit, ToUnit, Category, HandoverDate, CreatedAt, UpdatedAt FROM Handovers";

        private static void LoadItems(SQLiteConnection connection, Dictionary<int, Handover> byId)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT HandoverId, SerialNo, ItemType, Quantity, Note FROM HandoverItems " +
                    "ORDER BY HandoverId, SortOrder, Id;";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Handover handover;
                        if (!byId.TryGetValue(reader.GetInt32(0), out handover))
                        {
                            continue;
                        }

                        handover.Items.Add(new HandoverLine
                        {
                            SerialNo = ReadText(reader, 1),
                            ItemType = ReadText(reader, 2),
                            Quantity = ReadText(reader, 3),
                            Note = ReadText(reader, 4)
                        });
                    }
                }
            }
        }

        /// <summary>Defterdeki tüm tutanaklar, en yeni en üstte.</summary>
        public static List<Handover> GetAll()
        {
            var list = new List<Handover>();
            var byId = new Dictionary<int, Handover>();

            using (var connection = Database.OpenConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = HeaderColumns + " ORDER BY Id DESC;";

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var handover = ReadHeader(reader);
                            list.Add(handover);
                            byId[handover.Id] = handover;
                        }
                    }
                }

                LoadItems(connection, byId);
            }

            return list;
        }

        /// <summary>Tek bir tutanağı malzeme satırlarıyla birlikte verir; yoksa null.</summary>
        public static Handover GetById(int id)
        {
            Handover handover = null;

            using (var connection = Database.OpenConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = HeaderColumns + " WHERE Id = @id;";
                    command.Parameters.Add(new SQLiteParameter("@id", id));

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            handover = ReadHeader(reader);
                        }
                    }
                }

                if (handover != null)
                {
                    LoadItems(connection, new Dictionary<int, Handover> { { handover.Id, handover } });
                }
            }

            return handover;
        }

        // ---------- KAYDET ----------

        private static bool SameItems(List<HandoverLine> a, List<HandoverLine> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (var i = 0; i < a.Count; i++)
            {
                if (Clean(a[i].SerialNo) != Clean(b[i].SerialNo) ||
                    Clean(a[i].ItemType) != Clean(b[i].ItemType) ||
                    Clean(a[i].Quantity) != Clean(b[i].Quantity) ||
                    Clean(a[i].Note) != Clean(b[i].Note))
                {
                    return false;
                }
            }

            return true;
        }

        private static void AddChange(List<string> parts, string label, string oldValue, string newValue)
        {
            oldValue = Clean(oldValue);
            newValue = Clean(newValue);

            if (oldValue != newValue)
            {
                parts.Add(label + ": " + (oldValue.Length == 0 ? "(boş)" : oldValue) +
                          " -> " + (newValue.Length == 0 ? "(boş)" : newValue));
            }
        }

        private static void InsertItems(SQLiteConnection connection, SQLiteTransaction transaction,
            int handoverId, List<HandoverLine> items)
        {
            for (var i = 0; i < items.Count; i++)
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText =
                        "INSERT INTO HandoverItems (HandoverId, SortOrder, SerialNo, ItemType, Quantity, Note) " +
                        "VALUES (@handoverId, @sortOrder, @serialNo, @itemType, @quantity, @note);";
                    command.Parameters.Add(new SQLiteParameter("@handoverId", handoverId));
                    command.Parameters.Add(new SQLiteParameter("@sortOrder", i));
                    command.Parameters.Add(new SQLiteParameter("@serialNo", NullIfEmpty(items[i].SerialNo)));
                    command.Parameters.Add(new SQLiteParameter("@itemType", NullIfEmpty(items[i].ItemType)));
                    command.Parameters.Add(new SQLiteParameter("@quantity", NullIfEmpty(items[i].Quantity)));
                    command.Parameters.Add(new SQLiteParameter("@note", NullIfEmpty(items[i].Note)));
                    command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Tutanağı kaydeder: Id 0 ise yeni kayıt açar, değilse mevcut kaydı günceller (hiçbir şey
        /// değişmediyse dokunmaz). Kaydın numarasını verir.
        /// </summary>
        public static int Save(Handover handover)
        {
            if (string.IsNullOrWhiteSpace(handover.FromUnit))
            {
                throw new InvalidOperationException("Teslim eden birim boş bırakılamaz.");
            }

            if (string.IsNullOrWhiteSpace(handover.ToUnit))
            {
                throw new InvalidOperationException("Teslim alan birim boş bırakılamaz.");
            }

            if (string.IsNullOrWhiteSpace(handover.HandoverDate))
            {
                throw new InvalidOperationException("Tarih boş bırakılamaz.");
            }

            if (handover.Items == null || handover.Items.Count == 0)
            {
                throw new InvalidOperationException("En az bir malzeme satırı olmalı.");
            }

            var now = AssignmentRepository.NowRaw();
            int id = handover.Id;
            string actionText;
            string description;

            using (var connection = Database.OpenConnection())
            {
                if (id == 0)
                {
                    using (var transaction = connection.BeginTransaction())
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.Transaction = transaction;
                            command.CommandText =
                                "INSERT INTO Handovers (FromUnit, ToUnit, Category, HandoverDate, CreatedAt) " +
                                "VALUES (@fromUnit, @toUnit, @category, @date, @createdAt); " +
                                "SELECT last_insert_rowid();";
                            command.Parameters.Add(new SQLiteParameter("@fromUnit", handover.FromUnit.Trim()));
                            command.Parameters.Add(new SQLiteParameter("@toUnit", handover.ToUnit.Trim()));
                            command.Parameters.Add(new SQLiteParameter("@category", NullIfEmpty(handover.Category)));
                            command.Parameters.Add(new SQLiteParameter("@date", handover.HandoverDate.Trim()));
                            command.Parameters.Add(new SQLiteParameter("@createdAt", now));

                            id = Convert.ToInt32(command.ExecuteScalar());
                        }

                        InsertItems(connection, transaction, id, handover.Items);
                        transaction.Commit();
                    }

                    actionText = "Tutanak oluşturuldu";
                    description = "No: " + id + " | " + handover.FromUnit.Trim() + " -> " + handover.ToUnit.Trim() +
                                  " | " + handover.HandoverDate.Trim() + " | " + handover.Items.Count + " kalem";
                }
                else
                {
                    Handover old = null;

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = HeaderColumns + " WHERE Id = @id;";
                        command.Parameters.Add(new SQLiteParameter("@id", id));

                        using (var reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                old = ReadHeader(reader);
                            }
                        }
                    }

                    if (old == null)
                    {
                        throw new InvalidOperationException("Tutanak kaydı bulunamadı (silinmiş olabilir).");
                    }

                    LoadItems(connection, new Dictionary<int, Handover> { { old.Id, old } });

                    var changes = new List<string>();
                    AddChange(changes, "Teslim eden", old.FromUnit, handover.FromUnit);
                    AddChange(changes, "Teslim alan", old.ToUnit, handover.ToUnit);
                    AddChange(changes, "Kategori", old.Category, handover.Category);
                    AddChange(changes, "Tarih", old.HandoverDate, handover.HandoverDate);

                    var itemsChanged = !SameItems(old.Items, handover.Items);
                    if (itemsChanged)
                    {
                        changes.Add("Malzeme satırları değişti (" + old.Items.Count + " -> " +
                                    handover.Items.Count + " kalem)");
                    }

                    if (changes.Count == 0)
                    {
                        return id;
                    }

                    using (var transaction = connection.BeginTransaction())
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.Transaction = transaction;
                            command.CommandText =
                                "UPDATE Handovers SET FromUnit = @fromUnit, ToUnit = @toUnit, Category = @category, " +
                                "HandoverDate = @date, UpdatedAt = @updatedAt WHERE Id = @id;";
                            command.Parameters.Add(new SQLiteParameter("@fromUnit", handover.FromUnit.Trim()));
                            command.Parameters.Add(new SQLiteParameter("@toUnit", handover.ToUnit.Trim()));
                            command.Parameters.Add(new SQLiteParameter("@category", NullIfEmpty(handover.Category)));
                            command.Parameters.Add(new SQLiteParameter("@date", handover.HandoverDate.Trim()));
                            command.Parameters.Add(new SQLiteParameter("@updatedAt", now));
                            command.Parameters.Add(new SQLiteParameter("@id", id));
                            command.ExecuteNonQuery();
                        }

                        if (itemsChanged)
                        {
                            using (var command = connection.CreateCommand())
                            {
                                command.Transaction = transaction;
                                command.CommandText = "DELETE FROM HandoverItems WHERE HandoverId = @id;";
                                command.Parameters.Add(new SQLiteParameter("@id", id));
                                command.ExecuteNonQuery();
                            }

                            InsertItems(connection, transaction, id, handover.Items);
                        }

                        transaction.Commit();
                    }

                    actionText = "Tutanak düzenlendi";
                    description = "No: " + id + " | " + string.Join(" | ", changes);
                }
            }

            LogRepository.Add(null, description, actionText);
            return id;
        }

        // ---------- SİL ----------

        /// <summary>Tutanağı ve malzeme satırlarını defterden kalıcı siler; özeti işlem kaydına yazılır.</summary>
        public static void Delete(int id)
        {
            var handover = GetById(id);
            if (handover == null)
            {
                throw new InvalidOperationException("Tutanak kaydı bulunamadı.");
            }

            using (var connection = Database.OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "DELETE FROM HandoverItems WHERE HandoverId = @id;";
                    command.Parameters.Add(new SQLiteParameter("@id", id));
                    command.ExecuteNonQuery();
                }

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "DELETE FROM Handovers WHERE Id = @id;";
                    command.Parameters.Add(new SQLiteParameter("@id", id));
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }

            LogRepository.Add(null,
                "No: " + handover.Id + " | " + handover.FromUnit + " -> " + handover.ToUnit + " | " +
                handover.HandoverDate + " | " + handover.ItemCount + " kalem: " + handover.ItemsSummary,
                "Tutanak silindi");
        }
    }

    /// <summary>Teslim-tesellüm penceresindeki arama kutularının döndürdüğü tek bir sonuç.</summary>
    public class ProductSearchResult
    {
        public string SerialNo { get; set; }
        public string TypeName { get; set; }

        /// <summary>Listede gösterilecek hazır yazı. Örnek: "Telsiz — AB1234".</summary>
        public string DisplayText
        {
            get { return TypeName + (string.IsNullOrEmpty(SerialNo) ? "" : " — " + SerialNo); }
        }
    }

    /// <summary>
    /// Teslim-tesellüm penceresindeki "Seri No" ve "Sistem adı" kutularının arama sorguları.
    /// Depodaki tüm ürünler (zimmetli olanlar dahil) aranır; hurdadakiler aranmaz.
    /// </summary>
    public static class HandoverSearchRepository
    {
        private const int MaxResults = 15;
        private const string SystemNameFieldName = "Sistem İsmi";

        /// <summary>Sadece seri numarasına göre arar (Seri No kutusu için).</summary>
        public static List<ProductSearchResult> SearchBySerial(string query)
        {
            return Search(query, true);
        }

        /// <summary>Ürün cinsi, sistem ismi ya da seri numarasında arar (Sistem adı kutusu için).</summary>
        public static List<ProductSearchResult> SearchGeneral(string query)
        {
            return Search(query, false);
        }

        private static List<ProductSearchResult> Search(string query, bool serialOnly)
        {
            var list = new List<ProductSearchResult>();

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT DISTINCT t.Name, sn.TextValue, sys.TextValue " +
                    "FROM Products p " +
                    "JOIN ProductTypes t ON t.Id = p.ProductTypeId " +
                    "LEFT JOIN ProductValues sn ON sn.ProductId = p.Id " +
                    "  AND sn.PropertyId IN (SELECT Id FROM PropertyDefinitions WHERE IsSerialNumber = 1) " +
                    "LEFT JOIN ProductValues sys ON sys.ProductId = p.Id " +
                    "  AND sys.PropertyId IN (SELECT Id FROM PropertyDefinitions WHERE Name = @sysField) " +
                    "WHERE " +
                    (serialOnly
                        ? "sn.TextValue LIKE @q "
                        : "t.Name LIKE @q OR sn.TextValue LIKE @q OR sys.TextValue LIKE @q ") +
                    "ORDER BY t.Name " +
                    "LIMIT " + MaxResults + ";";
                command.Parameters.Add(new SQLiteParameter("@q", "%" + query + "%"));
                command.Parameters.Add(new SQLiteParameter("@sysField", SystemNameFieldName));

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var typeName = reader.GetString(0);
                        var systemName = reader.IsDBNull(2) ? "" : reader.GetString(2);

                        list.Add(new ProductSearchResult
                        {
                            TypeName = string.IsNullOrWhiteSpace(systemName) ? typeName : systemName,
                            SerialNo = reader.IsDBNull(1) ? "" : reader.GetString(1)
                        });
                    }
                }
            }

            return list;
        }
    }
}
