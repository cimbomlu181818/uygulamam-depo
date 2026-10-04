using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;

namespace DEPO_DURUMU.Data
{
    /// <summary>İşlem seçme penceresindeki bir satır: işlemin adı, grubu ve seçilen aralıktaki sayısı.</summary>
    public class ReportOperation
    {
        public string Name { get; set; }

        /// <summary>"Zimmet", "Hurda", "Tutanak", "Depo" ya da "Diğer".</summary>
        public string Group { get; set; }

        public int Count { get; set; }
    }

    /// <summary>
    /// Seçilen tarih aralığındaki işlemleri toplar. Hem işlem sayılarını (tik penceresi için) hem de
    /// seçilen işlemlerin rapor satırlarını aynı yoldan üretir; böylece penceredeki sayı ile rapordaki
    /// satır sayısı her zaman aynı olur.
    ///
    /// Kaynaklar:
    ///  - Zimmetlendi ve Zimmet iade alındı: Zimmetler defterinden (kişi, sicil, birim, not tam gelir).
    ///  - Tutanak oluşturuldu: Tutanaklar defterinden (teslim eden, teslim alan, kalemler tam gelir).
    ///  - Diğer her işlem (hurdaya taşındı, hurdadan geri getirildi, zimmet düzenlendi, tutanak düzenlendi/silindi,
    ///    ürün eklendi/güncellendi/silindi vb.): kayıt defterinden (ActionLogs).
    /// Zimmetlendi, Zimmet iade alındı ve Tutanak oluşturuldu kayıt defterinden ayrıca okunmaz; yoksa iki kez sayılırdı.
    /// </summary>
    public static class ReportCollector
    {
        public const string OpAssigned = "Zimmetlendi";
        public const string OpReturned = "Zimmet iade alındı";
        public const string OpHandoverCreated = "Tutanak oluşturuldu";

        private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

        private static readonly string[] DepotOperations =
        {
            "Eklendi", "Güncellendi", "Silindi", "Taşındı", "İçe aktarıldı", "Dışa aktarıldı"
        };

        /// <summary>Bir işlem adının hangi grupta gösterileceğini söyler.</summary>
        public static string GetGroup(string operation)
        {
            if (string.IsNullOrEmpty(operation))
            {
                return "Diğer";
            }

            if (operation.StartsWith("Zimmet", StringComparison.OrdinalIgnoreCase))
            {
                return "Zimmet";
            }

            if (operation.IndexOf("Hurda", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Hurda";
            }

            if (operation.StartsWith("Tutanak", StringComparison.OrdinalIgnoreCase))
            {
                return "Tutanak";
            }

            if (DepotOperations.Contains(operation))
            {
                return "Depo";
            }

            return "Diğer";
        }

        /// <summary>
        /// Aralıkta en az bir kez yapılmış işlemleri ve sayılarını verir. Sıra: Zimmet, Hurda, Tutanak, Depo, Diğer;
        /// her grubun içinde alfabetik.
        /// </summary>
        public static List<ReportOperation> GetOperations(DateTime from, DateTime to)
        {
            var groupOrder = new List<string> { "Zimmet", "Hurda", "Tutanak", "Depo", "Diğer" };

            return LoadAll(from, to)
                .GroupBy(r => r.Operation)
                .Select(g => new ReportOperation
                {
                    Name = g.Key,
                    Group = GetGroup(g.Key),
                    Count = g.Count()
                })
                .OrderBy(o => groupOrder.IndexOf(o.Group))
                .ThenBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Seçilen işlemlerin aralıktaki satırlarını, zamana göre eskiden yeniye sıralı verir.
        /// Aralığın iki ucu da dahildir.
        /// </summary>
        public static List<ReportRow> Collect(DateTime from, DateTime to, IEnumerable<string> operations)
        {
            var wanted = new HashSet<string>(operations ?? new string[0], StringComparer.Ordinal);

            return LoadAll(from, to)
                .Where(r => wanted.Contains(r.Operation))
                .ToList();
        }

        // ---------- TOPLAMA ----------

        private static List<ReportRow> LoadAll(DateTime from, DateTime to)
        {
            var rows = new List<ReportRow>();

            AddAssignments(rows, from, to);
            AddHandovers(rows, from, to);
            AddLogs(rows, from, to);

            // OrderBy kararlıdır: aynı saniyedeki satırlar toplandıkları sırada kalır.
            return rows.OrderBy(r => r.EventAt, StringComparer.Ordinal).ToList();
        }

        private static bool InRange(string raw, DateTime from, DateTime to)
        {
            DateTime time;
            if (!DateTime.TryParseExact(raw, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
            {
                return false;
            }

            return time >= from && time <= to;
        }

        private static void AddAssignments(List<ReportRow> rows, DateTime from, DateTime to)
        {
            foreach (var a in AssignmentRepository.GetAll(false))
            {
                if (InRange(a.AssignedAt, from, to))
                {
                    var detail = new List<string> { "Miktar: " + a.Quantity };
                    if (!string.IsNullOrWhiteSpace(a.AssignedNote))
                    {
                        detail.Add("Not: " + a.AssignedNote.Trim());
                    }

                    rows.Add(new ReportRow
                    {
                        Operation = OpAssigned,
                        EventAt = a.AssignedAt,
                        TypeName = a.TypeName,
                        Item = a.ProductText,
                        Person = PersonText(a),
                        Detail = string.Join(" | ", detail)
                    });
                }

                if (a.IsReturned && InRange(a.ReturnedAt, from, to))
                {
                    var detail = new List<string>
                    {
                        "Miktar: " + a.Quantity,
                        "Zimmet tarihi: " + a.AssignedAtText
                    };
                    if (!string.IsNullOrWhiteSpace(a.ReturnedNote))
                    {
                        detail.Add("İade notu: " + a.ReturnedNote.Trim());
                    }

                    rows.Add(new ReportRow
                    {
                        Operation = OpReturned,
                        EventAt = a.ReturnedAt,
                        TypeName = a.TypeName,
                        Item = a.ProductText,
                        Person = PersonText(a),
                        Detail = string.Join(" | ", detail)
                    });
                }
            }
        }

        /// <summary>Kişiyi tanıtan yazı. Örnek: "Ali Yılmaz (Sicil: 1234, Bilgi İşlem)".</summary>
        private static string PersonText(Assignment a)
        {
            var extra = new List<string>();
            if (!string.IsNullOrWhiteSpace(a.RegistryNo))
            {
                extra.Add("Sicil: " + a.RegistryNo.Trim());
            }

            if (!string.IsNullOrWhiteSpace(a.Department))
            {
                extra.Add(a.Department.Trim());
            }

            var name = (a.PersonName ?? "").Trim();
            return extra.Count == 0 ? name : name + " (" + string.Join(", ", extra) + ")";
        }

        private static void AddHandovers(List<ReportRow> rows, DateTime from, DateTime to)
        {
            foreach (var h in HandoverRepository.GetAll())
            {
                if (!InRange(h.CreatedAt, from, to))
                {
                    continue;
                }

                rows.Add(new ReportRow
                {
                    Operation = OpHandoverCreated,
                    EventAt = h.CreatedAt,
                    TypeName = h.Category ?? "",
                    Item = "No: " + h.Id + " | " + h.ItemCount + " kalem: " + h.ItemsSummary,
                    Person = "Teslim eden: " + h.FromUnit + " | Teslim alan: " + h.ToUnit,
                    Detail = "Tutanak tarihi: " + h.HandoverDate
                });
            }
        }

        private static void AddLogs(List<ReportRow> rows, DateTime from, DateTime to)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT CreatedAt, UserName, TypeName, Description, ActionType FROM ActionLogs " +
                    "WHERE CreatedAt >= @from AND CreatedAt <= @to " +
                    "AND ActionType NOT IN (@assigned, @returned, @handover) " +
                    "ORDER BY CreatedAt, Id;";
                command.Parameters.Add(new SQLiteParameter("@from", from.ToString(TimeFormat, CultureInfo.InvariantCulture)));
                command.Parameters.Add(new SQLiteParameter("@to", to.ToString(TimeFormat, CultureInfo.InvariantCulture)));
                command.Parameters.Add(new SQLiteParameter("@assigned", OpAssigned));
                command.Parameters.Add(new SQLiteParameter("@returned", OpReturned));
                command.Parameters.Add(new SQLiteParameter("@handover", OpHandoverCreated));

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        rows.Add(new ReportRow
                        {
                            EventAt = reader.GetString(0),
                            TypeName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            Item = reader.GetString(3),
                            Person = "",
                            Detail = "İşlemi yapan: " + reader.GetString(1),
                            Operation = reader.GetString(4)
                        });
                    }
                }
            }
        }
    }
}