using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;

namespace DEPO_DURUMU.Data
{
    /// <summary>
    /// Notlar defterinin (Excel benzeri sayfalar) veritabanı işlemleri.
    /// Her sayfa NoteSheets tablosunda tek satırdır; hücreler tek metin olarak saklanır.
    /// Veritabanı dosyasının içinde olduğu için Yedekleme'ye otomatik girer.
    /// </summary>
    public static class NoteRepository
    {
        private static string Now()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>Tüm defterlerdeki bütün sayfaları getirir.</summary>
        public static List<NoteSheet> LoadAll()
        {
            return Load(null);
        }

        /// <summary>Yalnızca verilen defterin (bardaki bir Excel butonunun) sayfalarını getirir.</summary>
        public static List<NoteSheet> LoadAll(int notebookId)
        {
            return Load(notebookId);
        }

        private static List<NoteSheet> Load(int? notebookId)
        {
            var list = new List<NoteSheet>();

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT Id, Name, SortOrder, ColumnCount, ColumnWidths, Data " +
                    "FROM NoteSheets " +
                    (notebookId.HasValue ? "WHERE NotebookId = @book " : "") +
                    "ORDER BY SortOrder, Id;";

                if (notebookId.HasValue)
                {
                    command.Parameters.Add(new SQLiteParameter("@book", notebookId.Value));
                }

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var sheet = new NoteSheet
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            SortOrder = reader.GetInt32(2)
                        };

                        sheet.LoadData(reader.IsDBNull(5) ? "" : reader.GetString(5), reader.GetInt32(3), false);
                        sheet.SetWidthsText(reader.IsDBNull(4) ? "" : reader.GetString(4));
                        list.Add(sheet);
                    }
                }
            }

            return list;
        }

        /// <summary>Yeni sayfayı hiçbir defterine bağlamadan ekler (eski kullanım; program açılışında bir deftere bağlanır).</summary>
        public static void Insert(NoteSheet sheet)
        {
            Insert(sheet, 0);
        }

        /// <summary>Yeni sayfayı verilen defterin sonuna ekler; sayfanın Id ve SortOrder değerlerini doldurur.</summary>
        public static void Insert(NoteSheet sheet, int notebookId)
        {
            using (var connection = Database.OpenConnection())
            {
                var order = 1;
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "SELECT COALESCE(MAX(SortOrder), 0) + 1 FROM NoteSheets WHERE NotebookId = @book;";
                    command.Parameters.Add(new SQLiteParameter("@book", notebookId));
                    order = Convert.ToInt32(command.ExecuteScalar());
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "INSERT INTO NoteSheets (Name, SortOrder, ColumnCount, ColumnWidths, Data, UpdatedAt, NotebookId) " +
                        "VALUES (@name, @order, @cols, @widths, @data, @now, @book);";
                    command.Parameters.Add(new SQLiteParameter("@book", notebookId));
                    command.Parameters.Add(new SQLiteParameter("@name", sheet.Name));
                    command.Parameters.Add(new SQLiteParameter("@order", order));
                    command.Parameters.Add(new SQLiteParameter("@cols", sheet.ColumnCount));
                    command.Parameters.Add(new SQLiteParameter("@widths", sheet.GetWidthsText()));
                    command.Parameters.Add(new SQLiteParameter("@data", sheet.SerializeData()));
                    command.Parameters.Add(new SQLiteParameter("@now", Now()));
                    command.ExecuteNonQuery();
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT last_insert_rowid();";
                    sheet.Id = Convert.ToInt32(command.ExecuteScalar());
                }

                sheet.SortOrder = order;
            }
        }

        /// <summary>Sayfanın adını, sütun genişliklerini ve tüm hücrelerini kaydeder.</summary>
        public static void Update(NoteSheet sheet)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "UPDATE NoteSheets SET Name = @name, ColumnCount = @cols, ColumnWidths = @widths, " +
                    "Data = @data, UpdatedAt = @now WHERE Id = @id;";
                command.Parameters.Add(new SQLiteParameter("@name", sheet.Name));
                command.Parameters.Add(new SQLiteParameter("@cols", sheet.ColumnCount));
                command.Parameters.Add(new SQLiteParameter("@widths", sheet.GetWidthsText()));
                command.Parameters.Add(new SQLiteParameter("@data", sheet.SerializeData()));
                command.Parameters.Add(new SQLiteParameter("@now", Now()));
                command.Parameters.Add(new SQLiteParameter("@id", sheet.Id));
                command.ExecuteNonQuery();
            }
        }

        public static void Delete(int id)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM NoteSheets WHERE Id = @id;";
                command.Parameters.Add(new SQLiteParameter("@id", id));
                command.ExecuteNonQuery();
            }
        }

        /// <summary>Sekmelerin sırasını (listedeki sıraya göre) kaydeder.</summary>
        public static void SaveOrder(IList<NoteSheet> sheets)
        {
            using (var connection = Database.OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                for (var i = 0; i < sheets.Count; i++)
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "UPDATE NoteSheets SET SortOrder = @order WHERE Id = @id;";
                        command.Parameters.Add(new SQLiteParameter("@order", i + 1));
                        command.Parameters.Add(new SQLiteParameter("@id", sheets[i].Id));
                        command.ExecuteNonQuery();
                    }
                    sheets[i].SortOrder = i + 1;
                }

                transaction.Commit();
            }
        }
    }

    /// <summary>Bardaki bir Excel butonu: bir defter. İçinde istenen kadar sayfa (NoteSheet) bulunur.</summary>
    public class NoteBook
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int SortOrder { get; set; }
    }

    /// <summary>Defterlerin (bardaki Excel butonlarının) veritabanı işlemleri.</summary>
    public static class NoteBookRepository
    {
        private static string Now()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        public static List<NoteBook> LoadAll()
        {
            var list = new List<NoteBook>();

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Id, Name, SortOrder FROM NoteBooks ORDER BY SortOrder, Id;";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new NoteBook
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            SortOrder = reader.GetInt32(2)
                        });
                    }
                }
            }

            return list;
        }

        /// <summary>Yeni defteri en sona ekler ve Id'siyle birlikte geri verir.</summary>
        public static NoteBook Insert(string name)
        {
            var book = new NoteBook { Name = name };

            using (var connection = Database.OpenConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COALESCE(MAX(SortOrder), 0) + 1 FROM NoteBooks;";
                    book.SortOrder = Convert.ToInt32(command.ExecuteScalar());
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "INSERT INTO NoteBooks (Name, SortOrder, CreatedAt) VALUES (@name, @order, @now);";
                    command.Parameters.Add(new SQLiteParameter("@name", name));
                    command.Parameters.Add(new SQLiteParameter("@order", book.SortOrder));
                    command.Parameters.Add(new SQLiteParameter("@now", Now()));
                    command.ExecuteNonQuery();
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT last_insert_rowid();";
                    book.Id = Convert.ToInt32(command.ExecuteScalar());
                }
            }

            return book;
        }

        public static void Rename(int id, string name)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE NoteBooks SET Name = @name WHERE Id = @id;";
                command.Parameters.Add(new SQLiteParameter("@name", name));
                command.Parameters.Add(new SQLiteParameter("@id", id));
                command.ExecuteNonQuery();
            }
        }

        /// <summary>Defteri ve içindeki bütün sayfaları siler.</summary>
        public static void Delete(int id)
        {
            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "DELETE FROM NoteSheets WHERE NotebookId = @id; " +
                    "DELETE FROM NoteBooks WHERE Id = @id;";
                command.Parameters.Add(new SQLiteParameter("@id", id));
                command.ExecuteNonQuery();
            }
        }
    }
}