using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    public partial class TypesWindow : Window
    {
        /// <summary>Listedeki bir satır: cins + işaret kutusunun durumu.</summary>
        public class TypeRow : INotifyPropertyChanged
        {
            private bool _isChecked;

            public int Id { get; set; }
            public string Name { get; set; }
            public int ProductCount { get; set; }

            /// <summary>Adın yanında gri görünen bilgi: kaç ürünü var.</summary>
            public string Info
            {
                get
                {
                    return ProductCount > 0
                        ? "(" + ProductCount.ToString("N0", CultureInfo.CurrentCulture) + " ürün)"
                        : "(ürün yok)";
                }
            }

            public bool IsChecked
            {
                get { return _isChecked; }
                set
                {
                    if (_isChecked == value)
                    {
                        return;
                    }

                    _isChecked = value;
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs("IsChecked"));
                    }
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        public TypesWindow()
        {
            InitializeComponent();
            LoadTypes(false);
        }

        /// <summary>Listeyi yeniler. keepChecks doğruysa daha önce işaretli olanlar işaretli kalır.</summary>
        private void LoadTypes(bool keepChecks = true)
        {
            var checkedIds = new HashSet<int>();
            var currentRows = TypeList.ItemsSource as List<TypeRow>;
            if (keepChecks && currentRows != null)
            {
                foreach (var oldRow in currentRows)
                {
                    if (oldRow.IsChecked)
                    {
                        checkedIds.Add(oldRow.Id);
                    }
                }
            }

            var counts = LoadProductCounts();
            var rows = new List<TypeRow>();

            foreach (var type in ProductTypeRepository.GetAll())
            {
                int count;
                counts.TryGetValue(type.Id, out count);

                var row = new TypeRow
                {
                    Id = type.Id,
                    Name = type.Name,
                    ProductCount = count,
                    IsChecked = checkedIds.Contains(type.Id)
                };
                row.PropertyChanged += (sender, args) => UpdateSelectedCount();
                rows.Add(row);
            }

            TypeList.ItemsSource = rows;
            UpdateSelectedCount();
        }

        private static Dictionary<int, int> LoadProductCounts()
        {
            var counts = new Dictionary<int, int>();

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT ProductTypeId, COUNT(*) FROM Products GROUP BY ProductTypeId;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        counts[Convert.ToInt32(reader.GetValue(0))] = Convert.ToInt32(reader.GetValue(1));
                    }
                }
            }

            return counts;
        }

        private void UpdateSelectedCount()
        {
            var rows = TypeList.ItemsSource as List<TypeRow>;
            var count = rows == null ? 0 : rows.Count(r => r.IsChecked);
            SelectedCountText.Text = count + " seçili";
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            SetAllChecked(true);
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            SetAllChecked(false);
        }

        private void SetAllChecked(bool value)
        {
            var rows = TypeList.ItemsSource as List<TypeRow>;
            if (rows == null)
            {
                return;
            }

            foreach (var row in rows)
            {
                row.IsChecked = value;
            }
        }

        private void AddTypeButton_Click(object sender, RoutedEventArgs e)
        {
            var name = NewTypeNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Lütfen bir ürün grubu adı yazın.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var existingTypeNames = ProductTypeRepository.GetAll().Select(t => t.Name).ToList();
            var turkish = new CultureInfo("tr-TR");

            var typeExists = existingTypeNames.Any(n =>
                string.Compare(n, name, turkish, CompareOptions.IgnoreCase) == 0);
            if (typeExists)
            {
                MessageBox.Show("Bu ürün grubu zaten var.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var closeType = SimilarityHelper.FindClosestMatch(name, existingTypeNames);
            if (closeType != null)
            {
                var confirm = MessageBox.Show(
                    "\"" + closeType + "\" mi demek istediniz?\n\n" +
                    "Yine de \"" + name + "\" adında yeni bir ürün grubu eklemek istiyor musunuz?",
                    "Şunu mu demek istediniz?", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            ProductTypeRepository.Add(name);
            LogRepository.Add(name, "", "Cins eklendi");
            NewTypeNameBox.Text = "";
            LoadTypes();
        }

        private void RenameTypeButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = TypeList.SelectedItem as TypeRow;
            if (selected == null)
            {
                MessageBox.Show("Önce listeden bir ürün grubu seçin (adına tıklayın).", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var newName = SimpleInputWindow.Ask(this, "Yeniden Adlandır", "Yeni ad:", selected.Name);

            if (newName == null)
            {
                return;
            }

            ProductTypeRepository.Rename(selected.Id, newName);
            LogRepository.Add(newName, "Eski ad: " + selected.Name, "Cins yeniden adlandırıldı");
            LoadTypes();
        }

        // ---------- TOPLU SİLME ----------

        private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var rows = TypeList.ItemsSource as List<TypeRow>;
            var selected = rows == null ? new List<TypeRow>() : rows.Where(r => r.IsChecked).ToList();

            if (selected.Count == 0)
            {
                MessageBox.Show("Önce silmek istediğin grupları işaretle.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var alsoProducts = DeleteProductsCheck.IsChecked == true;
            var totalProducts = selected.Sum(r => r.ProductCount);

            var question = new StringBuilder();
            question.AppendLine(selected.Count + " ürün grubu silinecek:");
            question.AppendLine(string.Join(", ", selected.Take(10).Select(r => r.Name)) +
                                (selected.Count > 10 ? " ... (+" + (selected.Count - 10) + " tane daha)" : ""));
            question.AppendLine();

            if (alsoProducts)
            {
                if (totalProducts > 0)
                {
                    question.AppendLine("DİKKAT: Bu gruplardaki toplam " +
                                        totalProducts.ToString("N0", CultureInfo.CurrentCulture) +
                                        " ürün de silinecek. Bu işlem geri alınamaz.");
                }
                else
                {
                    question.AppendLine("Seçili gruplarda ürün yok.");
                }
                question.AppendLine("Zimmette ürünü olan gruplar silinmez.");
            }
            else if (totalProducts > 0)
            {
                question.AppendLine("İçinde ürün olan gruplar silinmeden atlanır.");
            }

            question.AppendLine();
            question.AppendLine("Devam edilsin mi?");

            var answer = MessageBox.Show(this, question.ToString(), "Depo Durumu",
                MessageBoxButton.YesNo,
                alsoProducts && totalProducts > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            var deleted = new List<string>();
            var skipped = new List<string>();
            var logs = new List<string[]>();
            var productsDeleted = 0;

            try
            {
                // Hepsi tek işlemde: hata olursa hiçbir şey silinmez.
                using (var connection = Database.OpenConnection())
                using (var transaction = connection.BeginTransaction())
                {
                    foreach (var row in selected)
                    {
                        var productCount = Convert.ToInt32(Scalar(connection,
                            "SELECT COUNT(*) FROM Products WHERE ProductTypeId = @id;", row.Id));

                        if (productCount > 0)
                        {
                            if (!alsoProducts)
                            {
                                skipped.Add(row.Name + " (içinde " + productCount.ToString("N0", CultureInfo.CurrentCulture) + " ürün var)");
                                continue;
                            }

                            // Zimmette olan ürün silinemez (uygulamadaki mevcut kural).
                            var assigned = Convert.ToInt32(Scalar(connection,
                                "SELECT COALESCE(SUM(Quantity), 0) FROM Assignments " +
                                "WHERE IsReturned = 0 AND ProductId IN " +
                                "(SELECT Id FROM Products WHERE ProductTypeId = @id);", row.Id));
                            if (assigned > 0)
                            {
                                skipped.Add(row.Name + " (zimmette ürünü var)");
                                continue;
                            }

                            Run(connection,
                                "DELETE FROM ProductValues WHERE ProductId IN " +
                                "(SELECT Id FROM Products WHERE ProductTypeId = @id); " +
                                "DELETE FROM Products WHERE ProductTypeId = @id;", row.Id);
                            productsDeleted += productCount;

                            logs.Add(new[] { row.Name, productCount + " ürün (cinsle birlikte)", "Silindi" });
                        }

                        Run(connection,
                            "DELETE FROM TypeProperties WHERE ProductTypeId = @id; " +
                            "DELETE FROM HomeStatistics WHERE ProductTypeId = @id; " +
                            "DELETE FROM ProductTypes WHERE Id = @id;", row.Id);

                        deleted.Add(row.Name);
                        logs.Add(new[] { row.Name, "", "Cins silindi" });
                    }

                    transaction.Commit();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Silme işlemi tamamlanamadı; hiçbir şey silinmedi.\n\nNeden: " + ex.Message,
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Error);
                LoadTypes(false);
                return;
            }

            foreach (var log in logs)
            {
                try
                {
                    LogRepository.Add(log[0], log[1], log[2]);
                }
                catch (Exception)
                {
                    // Günlük yazılamadıysa silme işlemi zaten tamamlandı; sessizce devam et.
                }
            }

            var message = new StringBuilder();
            message.AppendLine(deleted.Count + " ürün grubu silindi.");
            if (productsDeleted > 0)
            {
                message.AppendLine(productsDeleted.ToString("N0", CultureInfo.CurrentCulture) + " ürün de silindi.");
            }
            if (skipped.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("Silinemeyenler (" + skipped.Count + "):");
                foreach (var item in skipped.Take(10))
                {
                    message.AppendLine("   • " + item);
                }
                if (skipped.Count > 10)
                {
                    message.AppendLine("   ... ve " + (skipped.Count - 10) + " tane daha");
                }
            }

            MessageBox.Show(this, message.ToString().TrimEnd(), "Depo Durumu",
                MessageBoxButton.OK, skipped.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

            DeleteProductsCheck.IsChecked = false;   // kazara tekrar tehlikeli silme olmasın
            LoadTypes(false);
        }

        private static object Scalar(SQLiteConnection connection, string sql, int id)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.Parameters.Add(new SQLiteParameter("@id", id));
                return command.ExecuteScalar();
            }
        }

        private static void Run(SQLiteConnection connection, string sql, int id)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.Parameters.Add(new SQLiteParameter("@id", id));
                command.ExecuteNonQuery();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
