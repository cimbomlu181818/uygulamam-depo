using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace DEPO_DURUMU.Data
{
    public class LogEntry
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public string UserName { get; set; }

        /// <summary>İlgili olduğu ürün cinsi (yoksa boş olabilir, ör. genel işlemler için).</summary>
        public string TypeName { get; set; }

        /// <summary>O anki durumu anlatan, donmuş/sabit metin (ör. "Seri No: ABC123 | Marka: Dell").</summary>
        public string Description { get; set; }

        /// <summary>Ör. "Eklendi", "Güncellendi", "Silindi", "Cins eklendi" gibi serbest metin.</summary>
        public string ActionType { get; set; }
    }

    /// <summary>
    /// Kayıt defteri (log): her ekleme/güncelleme/silme anında o anki bilgiyi
    /// SABİT bir metin olarak saklar. Sonradan ilgili ürün/cins/özellik silinse
    /// bile buradaki kayıtlar hiç değişmeden kalır.
    /// </summary>
    public static class LogRepository
    {
        /// <summary>Yeni bir log satırı ekler. Kullanıcı adı otomatik olarak Windows oturum adından alınır.</summary>
        public static void Add(string typeName, string description, string actionType)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "INSERT INTO ActionLogs (CreatedAt, UserName, TypeName, Description, ActionType) " +
                    "VALUES (@createdAt, @userName, @typeName, @description, @actionType);";
                command.Parameters.Add(new SQLiteParameter("@createdAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
                command.Parameters.Add(new SQLiteParameter("@userName", Environment.UserName));
                command.Parameters.Add(new SQLiteParameter("@typeName", (object)typeName ?? DBNull.Value));
                command.Parameters.Add(new SQLiteParameter("@description", description));
                command.Parameters.Add(new SQLiteParameter("@actionType", actionType));
                command.ExecuteNonQuery();
            }
        }

        /// <summary>Ana sayfadaki özet için: en yeniden en eskiye, en fazla <paramref name="count"/> kayıt.</summary>
        public static List<LogEntry> GetRecent(int count)
        {
            return RunQuery(
                "SELECT Id, CreatedAt, UserName, TypeName, Description, ActionType " +
                "FROM ActionLogs ORDER BY Id DESC LIMIT @count;",
                new SQLiteParameter("@count", count));
        }

        /// <summary>Log penceresi için: tüm kayıtlar, sınırsız, en yeniden en eskiye.</summary>
        public static List<LogEntry> GetAll()
        {
            return RunQuery(
                "SELECT Id, CreatedAt, UserName, TypeName, Description, ActionType " +
                "FROM ActionLogs ORDER BY Id DESC;");
        }

        /// <summary>Log penceresindeki arama kutusu için: zaman, cins, açıklama, işlem türü veya kullanıcıda arar.</summary>
        public static List<LogEntry> Search(string text)
        {
            return RunQuery(
                "SELECT Id, CreatedAt, UserName, TypeName, Description, ActionType " +
                "FROM ActionLogs " +
                "WHERE CreatedAt LIKE @pattern OR UserName LIKE @pattern OR TypeName LIKE @pattern " +
                "OR Description LIKE @pattern OR ActionType LIKE @pattern " +
                "ORDER BY Id DESC;",
                new SQLiteParameter("@pattern", "%" + text + "%"));
        }

        private static List<LogEntry> RunQuery(string sql, params SQLiteParameter[] parameters)
        {
            var list = new List<LogEntry>();

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                if (parameters != null)
                {
                    command.Parameters.AddRange(parameters);
                }

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new LogEntry
                        {
                            Id = reader.GetInt32(0),
                            CreatedAt = DateTime.Parse(reader.GetString(1)),
                            UserName = reader.GetString(2),
                            TypeName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            Description = reader.GetString(4),
                            ActionType = reader.GetString(5)
                        });
                    }
                }
            }

            return list;
        }
    }
}