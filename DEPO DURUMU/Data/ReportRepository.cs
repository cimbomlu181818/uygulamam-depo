using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;

namespace DEPO_DURUMU.Data
{
    /// <summary>Kaydedilmiş bir raporun başlık bilgisi (satırları ayrıca GetRows ile alınır).</summary>
    public class SavedReport
    {
        public int Id { get; set; }
        public string Title { get; set; }

        /// <summary>Raporun tarih aralığının başı, "yyyy-MM-dd HH:mm:ss" metni olarak.</summary>
        public string RangeFrom { get; set; }

        /// <summary>Raporun tarih aralığının sonu, "yyyy-MM-dd HH:mm:ss" metni olarak.</summary>
        public string RangeTo { get; set; }

        /// <summary>Rapora dahil edilen işlem adları, "; " ile birleştirilmiş (ör. "Zimmetlendi; Hurdaya taşındı").</summary>
        public string Operations { get; set; }

        public string CreatedAt { get; set; }
        public string CreatedBy { get; set; }
        public int RowCount { get; set; }

        /// <summary>Listede gösterilecek: "01.10.2026 08:00 - 03.10.2026 17:00".</summary>
        public string RangeText
        {
            get { return ReportRepository.FormatDate(RangeFrom) + " - " + ReportRepository.FormatDate(RangeTo); }
        }

        /// <summary>Listede gösterilecek: raporun hazırlandığı an.</summary>
        public string CreatedAtText
        {
            get { return ReportRepository.FormatDate(CreatedAt); }
        }
    }

    /// <summary>Raporun tek bir satırı. Hepsi yazıldığı anki donmuş metindir.</summary>
    public class ReportRow
    {
        /// <summary>İşlem adı, ör. "Zimmetlendi", "Hurdaya taşındı", "Tutanak düzenlendi".</summary>
        public string Operation { get; set; }

        /// <summary>İşlemin zamanı, "yyyy-MM-dd HH:mm:ss" metni olarak.</summary>
        public string EventAt { get; set; }

        public string TypeName { get; set; }

        /// <summary>Ürünün ya da kalemin tanımı (ürün adı, seri no vb.).</summary>
        public string Item { get; set; }

        /// <summary>Zimmetlenen/teslim alan kişi ya da birim (yoksa boş).</summary>
        public string Person { get; set; }

        /// <summary>Not, miktar, değişiklik açıklaması gibi geri kalan bilgiler.</summary>
        public string Detail { get; set; }

        public string EventAtText
        {
            get { return ReportRepository.FormatDate(EventAt); }
        }
    }

    /// <summary>
    /// Kaydedilen raporları saklar. Rapor donmuş bir kopyadır: satırlar yazıldığı gibi metin olarak
    /// tutulur, ürünlere/zimmetlere bağlı değildir. Sonradan bir ürün silinse ya da zimmet değişse
    /// bile rapor aynı kalır. Aynı veritabanı dosyasında durduğu için yedeğe de kendiliğinden girer.
    /// </summary>
    public static class ReportRepository
    {
        private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>Tarihi veritabanında saklanan metne çevirir.</summary>
        public static string ToRaw(DateTime value)
        {
            return value.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>Saklanan tarih metnini "dd.MM.yyyy HH:mm" olarak gösterir; çözülemezse metni olduğu gibi verir.</summary>
        public static string FormatDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "";
            }

            DateTime parsed;
            if (DateTime.TryParseExact(raw.Trim(), DateFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out parsed))
            {
                return parsed.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
            }

            return raw;
        }

        /// <summary>Kaydedilmiş tüm raporları, en yeni üstte olacak şekilde getirir.</summary>
        public static List<SavedReport> GetAll()
        {
            var list = new List<SavedReport>();

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT r.Id, r.Title, r.RangeFrom, r.RangeTo, r.Operations, r.CreatedAt, r.CreatedBy, " +
                    "(SELECT COUNT(*) FROM SavedReportRows x WHERE x.ReportId = r.Id) " +
                    "FROM SavedReports r ORDER BY r.Id DESC;";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new SavedReport
                        {
                            Id = reader.GetInt32(0),
                            Title = reader.GetString(1),
                            RangeFrom = reader.GetString(2),
                            RangeTo = reader.GetString(3),
                            Operations = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            CreatedAt = reader.GetString(5),
                            CreatedBy = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            RowCount = reader.GetInt32(7)
                        });
                    }
                }
            }

            return list;
        }

        /// <summary>Bir raporun başlık bilgisini getirir; yoksa null.</summary>
        public static SavedReport Get(int reportId)
        {
            foreach (var report in GetAll())
            {
                if (report.Id == reportId)
                {
                    return report;
                }
            }

            return null;
        }

        /// <summary>Bir raporun satırlarını, kaydedildiği sırayla getirir.</summary>
        public static List<ReportRow> GetRows(int reportId)
        {
            var list = new List<ReportRow>();

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT Operation, EventAt, TypeName, Item, Person, Detail " +
                    "FROM SavedReportRows WHERE ReportId = @id ORDER BY SortOrder, Id;";
                command.Parameters.Add(new SQLiteParameter("@id", reportId));

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new ReportRow
                        {
                            Operation = reader.GetString(0),
                            EventAt = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            TypeName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            Item = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            Person = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            Detail = reader.IsDBNull(5) ? "" : reader.GetString(5)
                        });
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// Raporu ve satırlarını tek işlemde kaydeder (ya hepsi yazılır ya hiçbiri). Yeni raporun numarasını verir.
        /// </summary>
        public static int Save(string title, DateTime rangeFrom, DateTime rangeTo,
            IEnumerable<string> operations, IList<ReportRow> rows)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                throw new ArgumentException("Rapor adı boş olamaz.", "title");
            }

            if (rows == null)
            {
                throw new ArgumentNullException("rows");
            }

            var operationText = operations == null ? "" : string.Join("; ", operations);
            int newId;

            using (var connection = Database.OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText =
                        "INSERT INTO SavedReports (Title, RangeFrom, RangeTo, Operations, CreatedAt, CreatedBy) " +
                        "VALUES (@title, @from, @to, @operations, @createdAt, @createdBy); " +
                        "SELECT last_insert_rowid();";
                    command.Parameters.Add(new SQLiteParameter("@title", title.Trim()));
                    command.Parameters.Add(new SQLiteParameter("@from", ToRaw(rangeFrom)));
                    command.Parameters.Add(new SQLiteParameter("@to", ToRaw(rangeTo)));
                    command.Parameters.Add(new SQLiteParameter("@operations", operationText));
                    command.Parameters.Add(new SQLiteParameter("@createdAt", ToRaw(DateTime.Now)));
                    command.Parameters.Add(new SQLiteParameter("@createdBy", Environment.UserName));

                    newId = Convert.ToInt32(command.ExecuteScalar());
                }

                var order = 0;
                foreach (var row in rows)
                {
                    order++;

                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText =
                            "INSERT INTO SavedReportRows (ReportId, SortOrder, Operation, EventAt, TypeName, Item, Person, Detail) " +
                            "VALUES (@reportId, @order, @operation, @eventAt, @typeName, @item, @person, @detail);";
                        command.Parameters.Add(new SQLiteParameter("@reportId", newId));
                        command.Parameters.Add(new SQLiteParameter("@order", order));
                        command.Parameters.Add(new SQLiteParameter("@operation", row.Operation ?? ""));
                        command.Parameters.Add(new SQLiteParameter("@eventAt", (object)row.EventAt ?? DBNull.Value));
                        command.Parameters.Add(new SQLiteParameter("@typeName", (object)row.TypeName ?? DBNull.Value));
                        command.Parameters.Add(new SQLiteParameter("@item", (object)row.Item ?? DBNull.Value));
                        command.Parameters.Add(new SQLiteParameter("@person", (object)row.Person ?? DBNull.Value));
                        command.Parameters.Add(new SQLiteParameter("@detail", (object)row.Detail ?? DBNull.Value));
                        command.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }

            LogRepository.Add(null,
                title.Trim() + " | " + FormatDate(ToRaw(rangeFrom)) + " - " + FormatDate(ToRaw(rangeTo)) +
                " | " + rows.Count + " satır",
                "Rapor kaydedildi");

            return newId;
        }

        /// <summary>Raporu ve satırlarını siler (satırlar veritabanı tarafından birlikte silinir).</summary>
        public static void Delete(int reportId)
        {
            var report = Get(reportId);
            if (report == null)
            {
                return;
            }

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM SavedReports WHERE Id = @id;";
                command.Parameters.Add(new SQLiteParameter("@id", reportId));
                command.ExecuteNonQuery();
            }

            LogRepository.Add(null,
                report.Title + " | " + report.RangeText + " | " + report.RowCount + " satır",
                "Rapor silindi");
        }
    }
}