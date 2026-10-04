using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    // ---------- TÜM VERİLERİ SIFIRLA ----------
    // Ayarlar menüsündeki "Tüm Verileri Sıfırla..." komutu. Önce ne kadar verinin silineceğini
    // gösterir, isteğe bağlı güvenlik yedeği alır ve kutuya "sıfırla" yazılmadan düğmeyi açmaz.
    // Sıfırlama: bütün tabloların içi boşaltılır, sıra numaraları başa döner, dosya küçültülür,
    // sonra program ilk kurulumdaki gibi yalnızca sabit özellikleri (Seri No, Sistem İsmi) ile kalır.
    public partial class MainWindow
    {
        private const string ResetWord = "sıfırla";

        private void ResetMenu_Click(object sender, RoutedEventArgs e)
        {
            bool makeBackup;
            if (!AskResetConfirmation(out makeBackup))
            {
                return;
            }

            string backupPath = null;

            try
            {
                if (makeBackup)
                {
                    backupPath = Path.Combine(Database.DataFolder,
                        "sifirlama_oncesi_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".db");
                    BackupService.CreateBackup(backupPath);
                }

                ResetDatabase();
                ClearUndoHistory();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Sıfırlama tamamlanamadı.\n\nNeden: " + ex.Message +
                    (backupPath != null ? "\n\nGüvenlik yedeği şurada duruyor:\n" + backupPath : ""),
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Error);

                try
                {
                    _selected.Clear();
                    ShowHome();
                }
                catch (Exception)
                {
                    // Ekran yenilenemediyse programı kapatıp açmak yeterli.
                }
                return;
            }

            _selected.Clear();
            ShowHome();
            RefreshNotebookButtons();

            var message = "Sıfırlama tamamlandı. Program ilk kurulmuş gibi boş.";
            if (backupPath != null)
            {
                message += "\n\nGüvenlik yedeği:\n" + backupPath +
                           "\n\nGeri dönmek istersen Ayarlar → Yedekten Geri Yükle ile bu dosyayı seç " +
                           "(sonra programı kapatıp yeniden aç). Artık gerekmiyorsa dosyayı silebilirsin.";
            }

            MessageBox.Show(this, message, "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>Bütün tabloları boşaltır ve varsayılanları yeniden kurar.</summary>
        private static void ResetDatabase()
        {
            using (var connection = Database.OpenConnection())
            {
                // Tabloların birbirine bağlılığı (foreign key) yüzünden silme sırası önemli olmasın diye
                // bu bağlantıda kontrol geçici kapatılır. (İşlemin dışında yapılmalıdır.)
                RunResetSql(connection, "PRAGMA foreign_keys = OFF;");

                var tables = new List<string>();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            tables.Add(reader.GetString(0));
                        }
                    }
                }

                var hasSequenceTable = false;
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name = 'sqlite_sequence';";
                    hasSequenceTable = Convert.ToInt32(command.ExecuteScalar()) > 0;
                }

                using (var transaction = connection.BeginTransaction())
                {
                    foreach (var table in tables)
                    {
                        RunResetSql(connection, "DELETE FROM \"" + table.Replace("\"", "\"\"") + "\";");
                    }

                    // Numaralar (Id) yeniden 1'den başlasın.
                    if (hasSequenceTable)
                    {
                        RunResetSql(connection, "DELETE FROM sqlite_sequence;");
                    }

                    transaction.Commit();
                }

                RunResetSql(connection, "PRAGMA foreign_keys = ON;");

                // Silinen verilerin dosyada artık yer kaplamaması ve dosyanın küçülmesi için.
                RunResetSql(connection, "VACUUM;");
            }

            // Sabit özellikleri (Seri No, Sistem İsmi) ve gerekirse eksik tabloları yeniden kurar.
            Database.Initialize();
        }

        private static void RunResetSql(SQLiteConnection connection, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        private static int CountRowsSafe(SQLiteConnection connection, string table)
        {
            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM " + table + ";";
                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>"sıfırla" / "SIFIRLA" / "sifirla" gibi yazımların hepsini kabul eder.</summary>
        private static bool IsResetWord(string typed)
        {
            return NormalizeResetText(typed) == NormalizeResetText(ResetWord);
        }

        private static string NormalizeResetText(string text)
        {
            return (text ?? "").Trim()
                .Replace('ı', 'i').Replace('İ', 'i').Replace('I', 'i')
                .ToLowerInvariant();
        }

        /// <summary>
        /// Uyarı penceresi: neyin silineceğini gösterir, yedek kutusu sunar ve "sıfırla" yazılmadan
        /// onay düğmesini açmaz. Onaylanırsa true döner.
        /// </summary>
        private bool AskResetConfirmation(out bool makeBackup)
        {
            makeBackup = true;

            int types, products, properties, scrap, assignments, handovers, logs;
            using (var connection = Database.OpenConnection())
            {
                types = CountRowsSafe(connection, "ProductTypes");
                products = CountRowsSafe(connection, "Products");
                properties = CountRowsSafe(connection, "PropertyDefinitions");
                scrap = CountRowsSafe(connection, "ScrapProducts");
                assignments = CountRowsSafe(connection, "Assignments");
                handovers = CountRowsSafe(connection, "Handovers");
                logs = CountRowsSafe(connection, "ActionLogs");
            }

            var window = new Window
            {
                Title = "Tüm Verileri Sıfırla",
                Width = 480,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ShowInTaskbar = false
            };

            var panel = new StackPanel { Margin = new Thickness(18) };

            panel.Children.Add(new TextBlock
            {
                Text = "DİKKAT: TÜM VERİLER SİLİNECEK",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Firebrick,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var lines = new List<string>
            {
                "Şunların hepsi kalıcı olarak silinir:",
                "",
                "   •  " + types.ToString("N0") + " ürün grubu",
                "   •  " + products.ToString("N0") + " ürün",
                "   •  " + properties.ToString("N0") + " bilgi",
                "   •  " + scrap.ToString("N0") + " hurda kaydı",
                "   •  " + assignments.ToString("N0") + " zimmet kaydı",
                "   •  " + handovers.ToString("N0") + " tutanak",
                "   •  " + logs.ToString("N0") + " log kaydı",
                "",
                "Program ilk kurulmuş gibi boş açılır (yalnızca sabit bilgiler olan " +
                "Seri No ve Sistem İsmi yeniden oluşturulur).",
                "Bu işlem geri alınamaz."
            };

            panel.Children.Add(new TextBlock
            {
                Text = string.Join("\n", lines),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var backupCheck = new CheckBox
            {
                Content = "Sıfırlamadan önce otomatik yedek al (önerilir)",
                IsChecked = true,
                Margin = new Thickness(0, 0, 0, 14)
            };
            panel.Children.Add(backupCheck);

            panel.Children.Add(new TextBlock
            {
                Text = "Onaylamak için aşağıdaki kutuya  " + ResetWord + "  yazın:",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var input = new TextBox { Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
            panel.Children.Add(input);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var resetButton = new Button
            {
                Content = "Her Şeyi Sıfırla",
                Width = 130,
                Height = 28,
                Margin = new Thickness(0, 0, 8, 0),
                IsEnabled = false
            };
            var cancelButton = new Button
            {
                Content = "Vazgeç",
                Width = 90,
                Height = 28,
                IsCancel = true,
                IsDefault = true
            };
            buttons.Children.Add(resetButton);
            buttons.Children.Add(cancelButton);
            panel.Children.Add(buttons);

            input.TextChanged += delegate { resetButton.IsEnabled = IsResetWord(input.Text); };
            resetButton.Click += delegate { window.DialogResult = true; };
            cancelButton.Click += delegate { window.DialogResult = false; };
            window.Loaded += delegate { input.Focus(); };

            window.Content = panel;

            var confirmed = window.ShowDialog() == true;
            makeBackup = backupCheck.IsChecked == true;
            return confirmed;
        }
    }
}