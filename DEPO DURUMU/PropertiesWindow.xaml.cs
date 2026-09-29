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
    public partial class PropertiesWindow : Window
    {
        /// <summary>Listedeki bir satır: özellik + işaret kutusunun durumu.</summary>
        public class PropertyRow : INotifyPropertyChanged
        {
            private bool _isChecked;

            public int Id { get; set; }
            public string Name { get; set; }
            public bool IsFixed { get; set; }
            public int UsageCount { get; set; }

            /// <summary>Sabit özellikler (Seri No, Sistem İsmi) silinemez: kutusu kapalıdır.</summary>
            public bool CanDelete
            {
                get { return !IsFixed; }
            }

            /// <summary>Adın yanında gri görünen bilgi: kaç üründe dolu değeri var.</summary>
            public string Info
            {
                get
                {
                    if (IsFixed)
                    {
                        return "(sabit özellik)";
                    }

                    return UsageCount > 0
                        ? "(" + UsageCount.ToString("N0", CultureInfo.CurrentCulture) + " dolu değer)"
                        : "(dolu değer yok)";
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

        public PropertiesWindow()
        {
            InitializeComponent();
            LoadProperties(false);
        }

        /// <summary>Listeyi yeniler. keepChecks doğruysa daha önce işaretli olanlar işaretli kalır.</summary>
        private void LoadProperties(bool keepChecks = true)
        {
            var checkedIds = new HashSet<int>();
            var currentRows = PropertyList.ItemsSource as List<PropertyRow>;
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

            var usage = LoadUsageCounts();
            var rows = new List<PropertyRow>();

            foreach (var property in PropertyDefinitionRepository.GetAll())
            {
                int count;
                usage.TryGetValue(property.Id, out count);

                var row = new PropertyRow
                {
                    Id = property.Id,
                    Name = property.Name,
                    IsFixed = IsFixedName(property.Name),
                    UsageCount = count
                };
                row.IsChecked = row.CanDelete && checkedIds.Contains(property.Id);
                row.PropertyChanged += (sender, args) => UpdateSelectedCount();
                rows.Add(row);
            }

            PropertyList.ItemsSource = rows;
            UpdateSelectedCount();
        }

        /// <summary>Her özellik için, kaç üründe dolu (boş olmayan) değeri olduğunu bir seferde okur.</summary>
        private static Dictionary<int, int> LoadUsageCounts()
        {
            var counts = new Dictionary<int, int>();

            using (var connection = Database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT PropertyId, COUNT(*) FROM ProductValues " +
                    "WHERE TextValue IS NOT NULL AND TextValue != '' GROUP BY PropertyId;";
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
            var rows = PropertyList.ItemsSource as List<PropertyRow>;
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
            var rows = PropertyList.ItemsSource as List<PropertyRow>;
            if (rows == null)
            {
                return;
            }

            foreach (var row in rows)
            {
                // Sabit özellikler hiçbir zaman işaretlenmez.
                row.IsChecked = value && row.CanDelete;
            }
        }

        private void AddPropertyButton_Click(object sender, RoutedEventArgs e)
        {
            var name = NewPropertyNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Lütfen bir özellik adı yazın.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dataTypeItem = NewPropertyDataTypeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
            var dataType = dataTypeItem != null ? dataTypeItem.Content.ToString() : "Metin";

            var existingProperties = PropertyDefinitionRepository.GetAll();
            var turkishCulture = new CultureInfo("tr-TR");

            var propertyExists = existingProperties.Any(p =>
                string.Compare(p.Name, name, turkishCulture, CompareOptions.IgnoreCase) == 0);
            if (propertyExists)
            {
                MessageBox.Show("Bu özellik zaten var.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var closeProperty = SimilarityHelper.FindClosestMatch(name, existingProperties.Select(p => p.Name));
            if (closeProperty != null)
            {
                var confirm = MessageBox.Show(
                    "\"" + closeProperty + "\" mi demek istediniz?\n\n" +
                    "Yine de \"" + name + "\" adında yeni bir özellik eklemek istiyor musunuz?",
                    "Şunu mu demek istediniz?", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            PropertyDefinitionRepository.Add(name, dataType, false);
            LogRepository.Add(null, name + " (" + dataType + ")", "Özellik eklendi");

            NewPropertyNameBox.Text = "";
            LoadProperties();
        }

        private void RenamePropertyButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = PropertyList.SelectedItem as PropertyRow;
            if (selected == null)
            {
                MessageBox.Show("Önce listeden bir özellik seçin (adına tıklayın).", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (selected.IsFixed)
            {
                MessageBox.Show("\"" + selected.Name + "\" sabit bir özelliktir, adı değiştirilemez.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var newName = SimpleInputWindow.Ask(this, "Yeniden Adlandır", "Yeni ad:", selected.Name);

            if (newName == null)
            {
                return;
            }

            PropertyDefinitionRepository.Rename(selected.Id, newName);
            LogRepository.Add(null, "Eski ad: " + selected.Name + " -> " + newName, "Özellik yeniden adlandırıldı");
            LoadProperties();
        }

        // ---------- TOPLU SİLME ----------

        private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var rows = PropertyList.ItemsSource as List<PropertyRow>;
            var selected = rows == null
                ? new List<PropertyRow>()
                : rows.Where(r => r.IsChecked && r.CanDelete).ToList();

            if (selected.Count == 0)
            {
                MessageBox.Show("Önce silmek istediğin özellikleri işaretle.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var alsoValues = DeleteValuesCheck.IsChecked == true;
            var totalValues = selected.Sum(r => r.UsageCount);

            var question = new StringBuilder();
            question.AppendLine(selected.Count + " özellik silinecek:");
            question.AppendLine(string.Join(", ", selected.Take(10).Select(r => r.Name)) +
                                (selected.Count > 10 ? " ... (+" + (selected.Count - 10) + " tane daha)" : ""));
            question.AppendLine();

            if (alsoValues)
            {
                if (totalValues > 0)
                {
                    question.AppendLine("DİKKAT: Bu özelliklere ait toplam " +
                                        totalValues.ToString("N0", CultureInfo.CurrentCulture) +
                                        " dolu değer de silinecek. Bu işlem geri alınamaz.");
                }
                else
                {
                    question.AppendLine("Seçili özelliklerde dolu değer yok.");
                }
            }
            else if (totalValues > 0)
            {
                question.AppendLine("Dolu değeri olan özellikler silinmeden atlanır.");
            }

            question.AppendLine();
            question.AppendLine("Devam edilsin mi?");

            var answer = MessageBox.Show(this, question.ToString(), "Depo Durumu",
                MessageBoxButton.YesNo,
                alsoValues && totalValues > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            var deleted = new List<string>();
            var skipped = new List<string>();
            var logs = new List<string[]>();
            var valuesDeleted = 0;

            try
            {
                // Hepsi tek işlemde: hata olursa hiçbir şey silinmez.
                using (var connection = Database.OpenConnection())
                using (var transaction = connection.BeginTransaction())
                {
                    foreach (var row in selected)
                    {
                        var usage = Convert.ToInt32(Scalar(connection,
                            "SELECT COUNT(*) FROM ProductValues " +
                            "WHERE PropertyId = @id AND TextValue IS NOT NULL AND TextValue != '';", row.Id));

                        if (usage > 0 && !alsoValues)
                        {
                            skipped.Add(row.Name + " (" + usage.ToString("N0", CultureInfo.CurrentCulture) + " üründe dolu değer var)");
                            continue;
                        }

                        Run(connection,
                            "DELETE FROM ProductValues WHERE PropertyId = @id; " +
                            "DELETE FROM TypeProperties WHERE PropertyId = @id; " +
                            "DELETE FROM HomeStatistics WHERE PropertyId = @id; " +
                            "DELETE FROM PropertyDefinitions WHERE Id = @id;", row.Id);

                        valuesDeleted += usage;
                        deleted.Add(row.Name);
                        logs.Add(new[]
                        {
                            row.Name + (usage > 0 ? " (" + usage + " dolu değerle birlikte)" : ""),
                            "Özellik silindi"
                        });
                    }

                    transaction.Commit();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Silme işlemi tamamlanamadı; hiçbir şey silinmedi.\n\nNeden: " + ex.Message,
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Error);
                LoadProperties(false);
                return;
            }

            foreach (var log in logs)
            {
                try
                {
                    LogRepository.Add(null, log[0], log[1]);
                }
                catch (Exception)
                {
                    // Günlük yazılamadıysa silme işlemi zaten tamamlandı; sessizce devam et.
                }
            }

            var message = new StringBuilder();
            message.AppendLine(deleted.Count + " özellik silindi.");
            if (valuesDeleted > 0)
            {
                message.AppendLine(valuesDeleted.ToString("N0", CultureInfo.CurrentCulture) + " dolu değer de silindi.");
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

            DeleteValuesCheck.IsChecked = false;   // kazara tekrar tehlikeli silme olmasın
            LoadProperties(false);
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

        /// <summary>
        /// "Seri No", "Sistem İsmi" ve "Zimmet" her zaman var olması gereken sabit özelliklerdir;
        /// yeniden adlandırılamaz ve silinemezler.
        /// </summary>
        private static bool IsFixedName(string name)
        {
            return name == "Seri No" || name == "Sistem İsmi" || name == PropertyDefinitionRepository.ZimmetName;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
