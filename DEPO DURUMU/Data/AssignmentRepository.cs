using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;

namespace DEPO_DURUMU.Data
{
    /// <summary>
    /// Zimmetler defterindeki bir kayıt: bir ürünün (tamamının ya da bir kısmının) bir kişiye verilmesi.
    /// Kayıt kendi içinde "donmuş" ürün bilgisi taşır (ürün tipi, sistem ismi, seri no); böylece ürün
    /// sonradan silinse ya da hurdaya gitse bile defterdeki kayıt bozulmaz.
    /// Zimmet AKTİFSE ve ürün hâlâ depodaysa (IsLinked) bu bilgiler ürün kartından canlı okunur.
    /// </summary>
    public class Assignment
    {
        public int Id { get; set; }

        /// <summary>Ürünün depodaki numarası. Ürün silinmişse numara kalır ama artık depoda yoktur.</summary>
        public int? ProductId { get; set; }

        public string TypeName { get; set; }
        public string SystemName { get; set; }
        public string SerialNo { get; set; }
        public int Quantity { get; set; }

        public string PersonName { get; set; }
        public string RegistryNo { get; set; }
        public string Department { get; set; }

        /// <summary>Veritabanındaki ham hâli: yyyy-MM-dd HH:mm:ss</summary>
        public string AssignedAt { get; set; }
        public string AssignedNote { get; set; }

        public bool IsReturned { get; set; }
        public string ReturnedAt { get; set; }
        public string ReturnedNote { get; set; }

        /// <summary>Zimmet aktif ve ürün depoda: ürün bilgileri üründen canlı geliyor (düzenlenemez).</summary>
        public bool IsLinked { get; set; }

        public string StatusText
        {
            get { return IsReturned ? "İade edildi" : "Zimmette"; }
        }

        /// <summary>"Sistem adı" sütunu: sistem ismi boşsa ürün tipinin adı.</summary>
        public string SystemNameText
        {
            get { return string.IsNullOrWhiteSpace(SystemName) ? TypeName : SystemName; }
        }

        public string AssignedAtText
        {
            get { return AssignmentRepository.FormatTime(AssignedAt); }
        }

        public string ReturnedAtText
        {
            get { return AssignmentRepository.FormatTime(ReturnedAt); }
        }

        /// <summary>Ürünü tanıtan kısa yazı. Örnek: "Telsiz X, Seri No: ABC123".</summary>
        public string ProductText
        {
            get { return AssignmentRepository.DescribeProduct(TypeName, SystemName, SerialNo); }
        }
    }

    /// <summary>Daha önce zimmet yapılmış bir kişi (önerilen kişi listesi için).</summary>
    public class KnownPerson
    {
        public string Name { get; set; }
        public string RegistryNo { get; set; }
        public string Department { get; set; }
    }

    /// <summary>
    /// Zimmetler defterinin veri işlemleri. Zimmetler ürün tablosundan bağımsız kayıt defteridir:
    /// aktif zimmetli ürün silinemez/hurdaya taşınamaz, ama defter kayıtları düzenlenebilir ve silinebilir.
    /// </summary>
    public static class AssignmentRepository
    {
        private const string SystemNameFieldName = "Sistem İsmi";
        private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

        private const string SelectColumns =
            "SELECT Id, ProductId, TypeName, SystemName, SerialNo, Quantity, PersonName, RegistryNo, " +
            "Department, AssignedAt, AssignedNote, IsReturned, ReturnedAt, ReturnedNote FROM Assignments";

        private static readonly CultureInfo Turkish = new CultureInfo("tr-TR");

        // ---------- TARİH VE METİN YARDIMCILARI ----------

        /// <summary>Veritabanındaki ham tarihi ekranda gösterilecek "gg.aa.yyyy SS:dd" biçimine çevirir.</summary>
        public static string FormatTime(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return "";
            }

            DateTime time;
            if (DateTime.TryParseExact(raw, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
            {
                return time.ToString("dd.MM.yyyy HH:mm", Turkish);
            }

            return raw;
        }

        /// <summary>
        /// Kullanıcının yazdığı tarihi (gg.aa.yyyy, gg.aa.yyyy SS:dd ya da SS:dd:ss) veritabanı biçimine çevirir.
        /// Geçersizse false döner.
        /// </summary>
        public static bool TryParseUiTime(string text, out string raw)
        {
            raw = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var formats = new[] { "dd.MM.yyyy HH:mm", "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy" };

            DateTime time;
            if (!DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
            {
                return false;
            }

            raw = time.ToString(TimeFormat, CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>Şu anki zamanı veritabanı biçiminde verir.</summary>
        public static string NowRaw()
        {
            return DateTime.Now.ToString(TimeFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>Ürünü tanıtan kısa yazı üretir. Sistem ismi boşsa ürün tipinin adı kullanılır.</summary>
        public static string DescribeProduct(string typeName, string systemName, string serialNo)
        {
            var name = string.IsNullOrWhiteSpace(systemName) ? typeName : systemName;

            if (!string.IsNullOrWhiteSpace(serialNo))
            {
                return name + ", Seri No: " + serialNo;
            }

            return name;
        }

        private static object NullIfEmpty(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? (object)DBNull.Value : text.Trim();
        }

        private static string Clean(string text)
        {
            return (text ?? "").Trim();
        }

        // ---------- ÜRÜNDEN CANLI BİLGİ OKUMA ----------

        private class ProductInfo
        {
            public bool Exists;
            public string TypeName;
            public string SystemName;
            public string SerialNo;
            public int Quantity;
        }

        private static ProductInfo ReadProductInfo(SQLiteConnection connection, int productId)
        {
            var info = new ProductInfo();

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT t.Name, p.Quantity FROM Products p " +
                    "JOIN ProductTypes t ON t.Id = p.ProductTypeId WHERE p.Id = @id;";
                command.Parameters.Add(new SQLiteParameter("@id", productId));

                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return info;
                    }

                    info.Exists = true;
                    info.TypeName = reader.GetString(0);
                    info.Quantity = reader.GetInt32(1);
                }
            }

            info.SystemName = ReadProductText(connection,
                "SELECT v.TextValue FROM ProductValues v " +
                "JOIN PropertyDefinitions d ON d.Id = v.PropertyId " +
                "WHERE v.ProductId = @id AND d.Name = @name LIMIT 1;",
                productId, SystemNameFieldName);

            info.SerialNo = ReadProductText(connection,
                "SELECT v.TextValue FROM ProductValues v " +
                "JOIN PropertyDefinitions d ON d.Id = v.PropertyId " +
                "WHERE v.ProductId = @id AND d.IsSerialNumber = 1 " +
                "AND v.TextValue IS NOT NULL AND v.TextValue != '' LIMIT 1;",
                productId, null);

            return info;
        }

        private static string ReadProductText(SQLiteConnection connection, string sql, int productId, string name)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.Parameters.Add(new SQLiteParameter("@id", productId));
                if (name != null)
                {
                    command.Parameters.Add(new SQLiteParameter("@name", name));
                }

                var result = command.ExecuteScalar();
                return result == null || result == DBNull.Value ? "" : Convert.ToString(result);
            }
        }

        /// <summary>Aktif zimmetin ürün bilgilerini, ürün hâlâ depodaysa üründen canlı olarak doldurur.</summary>
        private static void ApplyLiveInfo(SQLiteConnection connection, Assignment assignment)
        {
            if (assignment.IsReturned || !assignment.ProductId.HasValue)
            {
                return;
            }

            var info = ReadProductInfo(connection, assignment.ProductId.Value);
            if (!info.Exists)
            {
                return;
            }

            assignment.TypeName = info.TypeName;
            assignment.SystemName = info.SystemName;
            assignment.SerialNo = info.SerialNo;
            assignment.IsLinked = true;
        }

        // ---------- MİKTAR SORGULARI ----------

        private static int GetActiveQuantity(SQLiteConnection connection, int productId, int excludeAssignmentId)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT COALESCE(SUM(Quantity), 0) FROM Assignments " +
                    "WHERE ProductId = @id AND IsReturned = 0 AND Id != @exclude;";
                command.Parameters.Add(new SQLiteParameter("@id", productId));
                command.Parameters.Add(new SQLiteParameter("@exclude", excludeAssignmentId));

                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        /// <summary>Bir ürün için şu an zimmette olan (iade edilmemiş) toplam miktar. 0 ise ürün serbesttir.</summary>
        public static int GetActiveQuantity(int productId)
        {
            using (var connection = Database.OpenConnection())
            {
                return GetActiveQuantity(connection, productId, 0);
            }
        }

        /// <summary>Ürünün depodaki adedinden zimmette olanı düşüp zimmetlenebilir miktarı verir.</summary>
        public static int GetAvailableQuantity(int productId)
        {
            using (var connection = Database.OpenConnection())
            {
                var info = ReadProductInfo(connection, productId);
                if (!info.Exists)
                {
                    return 0;
                }

                var available = info.Quantity - GetActiveQuantity(connection, productId, 0);
                return available < 0 ? 0 : available;
            }
        }

        /// <summary>
        /// Ürün listesindeki "Zimmet" sütunu için: ürün numarası -> "Ali Yılmaz" ya da "Ali Yılmaz (2), Veli Kaya (1)".
        /// Sadece aktif zimmetler dahildir.
        /// </summary>
        public static Dictionary<int, string> GetActiveSummaryByProduct()
        {
            var summary = new Dictionary<int, string>();

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT ProductId, PersonName, Quantity FROM Assignments " +
                    "WHERE IsReturned = 0 AND ProductId IS NOT NULL ORDER BY Id;";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var productId = reader.GetInt32(0);
                        var person = reader.GetString(1);
                        var quantity = reader.GetInt32(2);
                        var part = quantity > 1 ? person + " (" + quantity + ")" : person;

                        string existing;
                        if (summary.TryGetValue(productId, out existing))
                        {
                            summary[productId] = existing + ", " + part;
                        }
                        else
                        {
                            summary[productId] = part;
                        }
                    }
                }
            }

            return summary;
        }

        // ---------- LİSTELEME ----------

        private static string ReadText(SQLiteDataReader reader, int index)
        {
            return reader.IsDBNull(index) ? "" : reader.GetString(index);
        }

        private static Assignment ReadAssignment(SQLiteDataReader reader)
        {
            return new Assignment
            {
                Id = reader.GetInt32(0),
                ProductId = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1),
                TypeName = ReadText(reader, 2),
                SystemName = ReadText(reader, 3),
                SerialNo = ReadText(reader, 4),
                Quantity = reader.GetInt32(5),
                PersonName = ReadText(reader, 6),
                RegistryNo = ReadText(reader, 7),
                Department = ReadText(reader, 8),
                AssignedAt = ReadText(reader, 9),
                AssignedNote = ReadText(reader, 10),
                IsReturned = reader.GetInt32(11) != 0,
                ReturnedAt = ReadText(reader, 12),
                ReturnedNote = ReadText(reader, 13)
            };
        }

        private static List<Assignment> ReadList(SQLiteConnection connection, string sql, int productId)
        {
            var list = new List<Assignment>();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                if (productId > 0)
                {
                    command.Parameters.Add(new SQLiteParameter("@productId", productId));
                }

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(ReadAssignment(reader));
                    }
                }
            }

            foreach (var assignment in list)
            {
                ApplyLiveInfo(connection, assignment);
            }

            return list;
        }

        /// <summary>Defterdeki kayıtlar, en yeni zimmet en üstte. activeOnly=true ise sadece şu an zimmette olanlar.</summary>
        public static List<Assignment> GetAll(bool activeOnly)
        {
            using (var connection = Database.OpenConnection())
            {
                return ReadList(connection,
                    SelectColumns + (activeOnly ? " WHERE IsReturned = 0" : "") +
                    " ORDER BY AssignedAt DESC, Id DESC;", 0);
            }
        }

        /// <summary>Bir ürünün şu an zimmette olan kayıtları (adetli üründe birden fazla kişi olabilir).</summary>
        public static List<Assignment> GetActiveForProduct(int productId)
        {
            using (var connection = Database.OpenConnection())
            {
                return ReadList(connection,
                    SelectColumns + " WHERE ProductId = @productId AND IsReturned = 0 " +
                    "ORDER BY AssignedAt DESC, Id DESC;", productId);
            }
        }

        private static Assignment ReadOne(SQLiteConnection connection, int id)
        {
            Assignment found = null;

            using (var command = connection.CreateCommand())
            {
                command.CommandText = SelectColumns + " WHERE Id = @id;";
                command.Parameters.Add(new SQLiteParameter("@id", id));

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        found = ReadAssignment(reader);
                    }
                }
            }

            if (found != null)
            {
                ApplyLiveInfo(connection, found);
            }

            return found;
        }

        /// <summary>Tek bir zimmet kaydını verir; yoksa null.</summary>
        public static Assignment GetById(int id)
        {
            using (var connection = Database.OpenConnection())
            {
                return ReadOne(connection, id);
            }
        }

        // ---------- ÖNERİLEN KİŞİ / BİRİM LİSTESİ ----------

        /// <summary>Daha önce zimmet yapılmış kişiler (en güncel sicil/birim bilgisiyle), ada göre sıralı.</summary>
        public static List<KnownPerson> GetKnownPeople()
        {
            var comparer = StringComparer.Create(Turkish, true);
            var byName = new Dictionary<string, KnownPerson>(comparer);

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT PersonName, RegistryNo, Department FROM Assignments ORDER BY Id DESC;";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var name = ReadText(reader, 0).Trim();
                        if (name.Length == 0 || byName.ContainsKey(name))
                        {
                            continue;
                        }

                        byName[name] = new KnownPerson
                        {
                            Name = name,
                            RegistryNo = ReadText(reader, 1),
                            Department = ReadText(reader, 2)
                        };
                    }
                }
            }

            var list = new List<KnownPerson>(byName.Values);
            list.Sort((a, b) => comparer.Compare(a.Name, b.Name));
            return list;
        }

        /// <summary>Defterde daha önce yazılmış birim adları, alfabetik sıralı.</summary>
        public static List<string> GetKnownDepartments()
        {
            var comparer = StringComparer.Create(Turkish, true);
            var set = new HashSet<string>(comparer);

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT DISTINCT Department FROM Assignments " +
                    "WHERE Department IS NOT NULL AND Department != '';";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var department = reader.GetString(0).Trim();
                        if (department.Length > 0)
                        {
                            set.Add(department);
                        }
                    }
                }
            }

            var list = new List<string>(set);
            list.Sort(comparer);
            return list;
        }

        // ---------- ZİMMETLE / İADE AL ----------

        /// <summary>
        /// Bir ürünü (ya da adetli üründe bir kısmını) bir kişiye zimmetler ve yeni kaydın numarasını verir.
        /// Elde yeterli miktar yoksa ya da kişi adı boşsa hata fırlatır.
        /// </summary>
        public static int Assign(int productId, int quantity, string personName,
            string registryNo, string department, string note, string assignedAt = null)
        {
            if (string.IsNullOrWhiteSpace(personName))
            {
                throw new InvalidOperationException("Kişi adı boş bırakılamaz.");
            }

            if (quantity < 1)
            {
                throw new InvalidOperationException("Miktar 1 veya daha büyük olmalı.");
            }

            ProductInfo info;
            int newId;

            using (var connection = Database.OpenConnection())
            {
                info = ReadProductInfo(connection, productId);
                if (!info.Exists)
                {
                    throw new InvalidOperationException("Ürün depoda bulunamadı.");
                }

                var available = info.Quantity - GetActiveQuantity(connection, productId, 0);
                if (quantity > available)
                {
                    throw new InvalidOperationException(
                        "Bu üründen zimmetlenebilecek miktar en fazla " + (available < 0 ? 0 : available) + ".");
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "INSERT INTO Assignments (ProductId, TypeName, SystemName, SerialNo, Quantity, " +
                        "PersonName, RegistryNo, Department, AssignedAt, AssignedNote, IsReturned) " +
                        "VALUES (@productId, @typeName, @systemName, @serialNo, @quantity, " +
                        "@personName, @registryNo, @department, @assignedAt, @note, 0); " +
                        "SELECT last_insert_rowid();";
                    command.Parameters.Add(new SQLiteParameter("@productId", productId));
                    command.Parameters.Add(new SQLiteParameter("@typeName", info.TypeName));
                    command.Parameters.Add(new SQLiteParameter("@systemName", NullIfEmpty(info.SystemName)));
                    command.Parameters.Add(new SQLiteParameter("@serialNo", NullIfEmpty(info.SerialNo)));
                    command.Parameters.Add(new SQLiteParameter("@quantity", quantity));
                    command.Parameters.Add(new SQLiteParameter("@personName", personName.Trim()));
                    command.Parameters.Add(new SQLiteParameter("@registryNo", NullIfEmpty(registryNo)));
                    command.Parameters.Add(new SQLiteParameter("@department", NullIfEmpty(department)));
                    command.Parameters.Add(new SQLiteParameter("@assignedAt",
                        string.IsNullOrWhiteSpace(assignedAt) ? NowRaw() : assignedAt));
                    command.Parameters.Add(new SQLiteParameter("@note", NullIfEmpty(note)));

                    newId = Convert.ToInt32(command.ExecuteScalar());
                }
            }

            LogRepository.Add(info.TypeName,
                DescribeProduct(info.TypeName, info.SystemName, info.SerialNo) +
                " | Miktar: " + quantity + " -> " + personName.Trim(),
                "Zimmetlendi");

            return newId;
        }

        /// <summary>
        /// Zimmet tutanağını KAYDETMEDEN önizlemek için, Assign'ın yazacağı kaydın aynısını hazırlar
        /// (defterde hiçbir şey değişmez; Id 0 kalır). Assign ile aynı kontrolleri yapar ve aynı hatayı fırlatır.
        /// </summary>
        public static Assignment BuildDraft(int productId, int quantity, string personName,
            string registryNo, string department, string note, string assignedAt)
        {
            if (string.IsNullOrWhiteSpace(personName))
            {
                throw new InvalidOperationException("Kişi adı boş bırakılamaz.");
            }

            if (quantity < 1)
            {
                throw new InvalidOperationException("Miktar 1 veya daha büyük olmalı.");
            }

            using (var connection = Database.OpenConnection())
            {
                var info = ReadProductInfo(connection, productId);
                if (!info.Exists)
                {
                    throw new InvalidOperationException("Ürün depoda bulunamadı.");
                }

                var available = info.Quantity - GetActiveQuantity(connection, productId, 0);
                if (quantity > available)
                {
                    throw new InvalidOperationException(
                        "Bu üründen zimmetlenebilecek miktar en fazla " + (available < 0 ? 0 : available) + ".");
                }

                return new Assignment
                {
                    Id = 0,
                    ProductId = productId,
                    TypeName = info.TypeName,
                    SystemName = info.SystemName,
                    SerialNo = info.SerialNo,
                    Quantity = quantity,
                    PersonName = personName.Trim(),
                    RegistryNo = Clean(registryNo),
                    Department = Clean(department),
                    AssignedAt = string.IsNullOrWhiteSpace(assignedAt) ? NowRaw() : assignedAt,
                    AssignedNote = Clean(note),
                    IsReturned = false,
                    IsLinked = true
                };
            }
        }

        /// <summary>
        /// Bir zimmeti iade alınmış olarak işaretler; ürün bilgisi o anki hâliyle kayda donar.
        /// returnedAt verilirse iade saati olarak o yazılır (tutanakta gösterilenle aynı olsun diye).
        /// </summary>
        public static void Return(int assignmentId, string note, string returnedAt = null)
        {
            Assignment assignment;

            using (var connection = Database.OpenConnection())
            {
                assignment = ReadOne(connection, assignmentId);
                if (assignment == null)
                {
                    throw new InvalidOperationException("Zimmet kaydı bulunamadı.");
                }

                if (assignment.IsReturned)
                {
                    throw new InvalidOperationException("Bu zimmet zaten iade alınmış.");
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "UPDATE Assignments SET IsReturned = 1, ReturnedAt = @returnedAt, ReturnedNote = @note, " +
                        "TypeName = @typeName, SystemName = @systemName, SerialNo = @serialNo WHERE Id = @id;";
                    command.Parameters.Add(new SQLiteParameter("@returnedAt",
                        string.IsNullOrWhiteSpace(returnedAt) ? NowRaw() : returnedAt));
                    command.Parameters.Add(new SQLiteParameter("@note", NullIfEmpty(note)));
                    command.Parameters.Add(new SQLiteParameter("@typeName", assignment.TypeName));
                    command.Parameters.Add(new SQLiteParameter("@systemName", NullIfEmpty(assignment.SystemName)));
                    command.Parameters.Add(new SQLiteParameter("@serialNo", NullIfEmpty(assignment.SerialNo)));
                    command.Parameters.Add(new SQLiteParameter("@id", assignmentId));
                    command.ExecuteNonQuery();
                }
            }

            LogRepository.Add(assignment.TypeName,
                assignment.ProductText + " | " + assignment.PersonName + " -> depo, miktar: " + assignment.Quantity,
                "Zimmet iade alındı");
        }

        // ---------- DÜZENLE ----------

        private static void AddChange(List<string> parts, string label, string oldValue, string newValue)
        {
            oldValue = Clean(oldValue);
            newValue = Clean(newValue);

            if (oldValue == newValue)
            {
                return;
            }

            parts.Add(label + ": " + (oldValue.Length == 0 ? "(boş)" : oldValue) +
                      " -> " + (newValue.Length == 0 ? "(boş)" : newValue));
        }

        /// <summary>
        /// Defterdeki bir kaydı düzeltir (kişi, sicil, birim, miktar, tarihler, notlar, iade durumu).
        /// Zimmet aktifse ve ürün depodaysa ürün bilgileri üründen alınır; iade edilmiş ya da ürünü
        /// depoda olmayan kayıtlarda ürün bilgileri de elle düzeltilebilir.
        /// Kurallar bozulacaksa (ör. miktar depodaki adedi aşıyorsa) hata fırlatır.
        /// </summary>
        public static void Update(Assignment edited)
        {
            if (string.IsNullOrWhiteSpace(edited.PersonName))
            {
                throw new InvalidOperationException("Kişi adı boş bırakılamaz.");
            }

            if (edited.Quantity < 1)
            {
                throw new InvalidOperationException("Miktar 1 veya daha büyük olmalı.");
            }

            if (string.IsNullOrWhiteSpace(edited.AssignedAt))
            {
                throw new InvalidOperationException("Zimmet tarihi boş bırakılamaz.");
            }

            if (edited.IsReturned && string.IsNullOrWhiteSpace(edited.ReturnedAt))
            {
                throw new InvalidOperationException("İade edildi olarak işaretlenen kayıtta iade tarihi olmalı.");
            }

            var changes = new List<string>();
            Assignment old;

            using (var connection = Database.OpenConnection())
            {
                old = ReadOne(connection, edited.Id);
                if (old == null)
                {
                    throw new InvalidOperationException("Zimmet kaydı bulunamadı.");
                }

                ProductInfo info = null;
                if (edited.ProductId.HasValue)
                {
                    info = ReadProductInfo(connection, edited.ProductId.Value);
                }

                var productExists = info != null && info.Exists;

                if (!edited.IsReturned)
                {
                    if (!productExists)
                    {
                        throw new InvalidOperationException(
                            "Ürün depoda bulunmadığı için bu kayıt aktif zimmet olamaz; 'İade edildi' olarak kalmalı.");
                    }

                    var others = GetActiveQuantity(connection, edited.ProductId.Value, edited.Id);
                    if (others + edited.Quantity > info.Quantity)
                    {
                        var left = info.Quantity - others;
                        throw new InvalidOperationException(
                            "Depoda bu üründen toplam " + info.Quantity + " adet var; diğer zimmetlerle birlikte " +
                            "bu miktar aşılamaz (bu kayıt için en fazla " + (left < 0 ? 0 : left) + ").");
                    }
                }

                // Zimmet aktifse ya da şimdi iade ediliyorsa ürün bilgisi üründen (canlı) alınır.
                if (productExists && (!edited.IsReturned || !old.IsReturned))
                {
                    edited.TypeName = info.TypeName;
                    edited.SystemName = info.SystemName;
                    edited.SerialNo = info.SerialNo;
                }

                if (string.IsNullOrWhiteSpace(edited.TypeName))
                {
                    throw new InvalidOperationException("Ürün tipi boş bırakılamaz.");
                }

                AddChange(changes, "Kişi", old.PersonName, edited.PersonName);
                AddChange(changes, "Sicil", old.RegistryNo, edited.RegistryNo);
                AddChange(changes, "Birim", old.Department, edited.Department);
                AddChange(changes, "Miktar", old.Quantity.ToString(), edited.Quantity.ToString());
                AddChange(changes, "Zimmet tarihi", FormatTime(old.AssignedAt), FormatTime(edited.AssignedAt));
                AddChange(changes, "Not", old.AssignedNote, edited.AssignedNote);
                AddChange(changes, "Durum", old.StatusText, edited.IsReturned ? "İade edildi" : "Zimmette");
                AddChange(changes, "İade tarihi", FormatTime(old.ReturnedAt), FormatTime(edited.ReturnedAt));
                AddChange(changes, "İade notu", old.ReturnedNote, edited.ReturnedNote);
                AddChange(changes, "Ürün tipi", old.TypeName, edited.TypeName);
                AddChange(changes, "Sistem adı", old.SystemName, edited.SystemName);
                AddChange(changes, "Seri No", old.SerialNo, edited.SerialNo);

                if (changes.Count == 0)
                {
                    return;
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "UPDATE Assignments SET TypeName = @typeName, SystemName = @systemName, " +
                        "SerialNo = @serialNo, Quantity = @quantity, PersonName = @personName, " +
                        "RegistryNo = @registryNo, Department = @department, AssignedAt = @assignedAt, " +
                        "AssignedNote = @assignedNote, IsReturned = @isReturned, ReturnedAt = @returnedAt, " +
                        "ReturnedNote = @returnedNote WHERE Id = @id;";
                    command.Parameters.Add(new SQLiteParameter("@typeName", edited.TypeName.Trim()));
                    command.Parameters.Add(new SQLiteParameter("@systemName", NullIfEmpty(edited.SystemName)));
                    command.Parameters.Add(new SQLiteParameter("@serialNo", NullIfEmpty(edited.SerialNo)));
                    command.Parameters.Add(new SQLiteParameter("@quantity", edited.Quantity));
                    command.Parameters.Add(new SQLiteParameter("@personName", edited.PersonName.Trim()));
                    command.Parameters.Add(new SQLiteParameter("@registryNo", NullIfEmpty(edited.RegistryNo)));
                    command.Parameters.Add(new SQLiteParameter("@department", NullIfEmpty(edited.Department)));
                    command.Parameters.Add(new SQLiteParameter("@assignedAt", edited.AssignedAt));
                    command.Parameters.Add(new SQLiteParameter("@assignedNote", NullIfEmpty(edited.AssignedNote)));
                    command.Parameters.Add(new SQLiteParameter("@isReturned", edited.IsReturned ? 1 : 0));
                    command.Parameters.Add(new SQLiteParameter("@returnedAt",
                        edited.IsReturned ? NullIfEmpty(edited.ReturnedAt) : DBNull.Value));
                    command.Parameters.Add(new SQLiteParameter("@returnedNote",
                        edited.IsReturned ? NullIfEmpty(edited.ReturnedNote) : DBNull.Value));
                    command.Parameters.Add(new SQLiteParameter("@id", edited.Id));
                    command.ExecuteNonQuery();
                }
            }

            LogRepository.Add(edited.TypeName,
                DescribeProduct(edited.TypeName, edited.SystemName, edited.SerialNo) + " | " +
                string.Join(" | ", changes),
                "Zimmet düzenlendi");
        }

        // ---------- SİL ----------

        /// <summary>
        /// Bir zimmet kaydını defterden kalıcı siler. Aktif bir zimmet silinirse ürünün zimmetlenebilir
        /// miktarı geri açılır. Kaydın özeti işlem kaydına (Log) yazılır.
        /// </summary>
        public static void Delete(int assignmentId)
        {
            Assignment assignment;

            using (var connection = Database.OpenConnection())
            {
                assignment = ReadOne(connection, assignmentId);
                if (assignment == null)
                {
                    throw new InvalidOperationException("Zimmet kaydı bulunamadı.");
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "DELETE FROM Assignments WHERE Id = @id;";
                    command.Parameters.Add(new SQLiteParameter("@id", assignmentId));
                    command.ExecuteNonQuery();
                }
            }

            LogRepository.Add(assignment.TypeName,
                assignment.ProductText + " | " + assignment.PersonName + ", miktar: " + assignment.Quantity +
                ", zimmet tarihi: " + assignment.AssignedAtText +
                (assignment.IsReturned ? ", iade edilmişti (" + assignment.ReturnedAtText + ")" : ", zimmetteydi"),
                "Zimmet kaydı silindi");
        }
    }
}