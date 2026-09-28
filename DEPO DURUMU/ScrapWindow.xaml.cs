using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    public partial class ScrapWindow : Window
    {
        // Tablodaki dahili sütun adları (özellik sütunları "p0", "p1"... diye adlandırılır).
        private const string NoColumnName = "__No";
        private const string SelectedColumnName = "__Selected";
        private const string IdColumnName = "__ScrapId";
        private const string DateColumnName = "__Date";

        private class TypeEntry
        {
            public string Name { get; set; }
            public int Count { get; set; }
            public string Display { get { return Name + " (" + Count + ")"; } }
        }

        private List<ScrapProduct> _items = new List<ScrapProduct>();
        private Dictionary<int, List<ScrapValue>> _values = new Dictionary<int, List<ScrapValue>>();
        private string _currentTypeName;

        // Şu an açık cinsin sütunları (özellik adları ve veri tipleri, sütun sırasıyla).
        private List<string> _propertyNames = new List<string>();
        private List<string> _propertyTypes = new List<string>();

        // Seçili filtreler: sütun numarası -> seçilen değerler.
        private readonly Dictionary<int, HashSet<string>> _selected = new Dictionary<int, HashSet<string>>();

        public ScrapWindow()
            : this(null, 0)
        {
        }

        /// <summary>
        /// openTypeName verilirse pencere doğrudan o cinsin sayfasında açılır ve selectScrapId
        /// verilmişse o ürünün satırı seçilip görünür hâle getirilir
        /// (ana sayfadaki aramadan bir hurda sonucuna çift tıklanınca kullanılır).
        /// </summary>
        public ScrapWindow(string openTypeName, int selectScrapId)
        {
            InitializeComponent();
            ShowHome();

            if (openTypeName != null && _items.Any(i => i.TypeName == openTypeName))
            {
                ShowTypePage(openTypeName, "");

                if (selectScrapId > 0)
                {
                    SelectScrapRow(selectScrapId);
                }
            }
        }

        /// <summary>
        /// Cins sayfasındaki tabloda, verilen hurda kaydının satırını seçip görünür hâle getirir.
        /// </summary>
        private void SelectScrapRow(int scrapId)
        {
            var view = ProductGrid.ItemsSource as DataView;
            if (view == null)
            {
                return;
            }

            foreach (DataRowView rowView in view)
            {
                if ((int)rowView[IdColumnName] == scrapId)
                {
                    ProductGrid.SelectedItem = rowView;
                    ProductGrid.ScrollIntoView(rowView);
                    break;
                }
            }
        }

        private void LoadData()
        {
            _items = ScrapRepository.GetAll().OrderBy(i => i.Id).ToList();
            _values = ScrapRepository.GetAllValuesGrouped();
        }

        // ---------- ANA SAYFA ----------

        private void ShowHome()
        {
            LoadData();

            var types = _items
                .GroupBy(i => i.TypeName)
                .Select(g => new TypeEntry { Name = g.Key, Count = g.Count() })
                .OrderBy(t => t.Name)
                .ToList();

            HomeTypeList.ItemsSource = null;
            HomeTypeList.ItemsSource = types;

            HomeInfoText.Text = _items.Count == 0
                ? "Hurdada hiç ürün yok."
                : "Hurdada toplam " + _items.Count + " ürün var. Bir cinse çift tıklayarak içindeki ürünleri görebilirsiniz.";

            HomePage.Visibility = Visibility.Visible;
            TypePage.Visibility = Visibility.Collapsed;
            _currentTypeName = null;
        }

        private void HomeTypeList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var selected = HomeTypeList.SelectedItem as TypeEntry;
            if (selected == null)
            {
                return;
            }

            ShowTypePage(selected.Name, "");
        }

        // ---------- CİNS SAYFASI ----------

        private void ShowTypePage(string typeName, string searchText)
        {
            _currentTypeName = typeName;
            TypePageTitle.Text = typeName + " (Hurda)";

            _selected.Clear();
            ProductGrid.ItemsSource = null;

            TypeSearchBox.Text = searchText;
            TypeSearchPlaceholder.Visibility = searchText.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            ShowNormalBar();

            HomePage.Visibility = Visibility.Collapsed;
            TypePage.Visibility = Visibility.Visible;

            LoadTypeGrid();
        }

        /// <summary>
        /// Seçili cinsin hurdadaki ürünlerini tabloya doldurur, filtre panelini kurar, filtreleri uygular.
        /// Sütunlar, o cinsin hurdadaki ürünlerinde geçen özelliklerin birleşimidir.
        /// </summary>
        private void LoadTypeGrid()
        {
            if (_currentTypeName == null)
            {
                return;
            }

            var typeItems = _items.Where(i => i.TypeName == _currentTypeName).ToList();

            // Sütunlar: ürünlerde geçen özellik adları, ilk görülme sırasıyla.
            _propertyNames = new List<string>();
            _propertyTypes = new List<string>();

            foreach (var item in typeItems)
            {
                List<ScrapValue> values;
                if (!_values.TryGetValue(item.Id, out values))
                {
                    continue;
                }

                foreach (var value in values)
                {
                    if (!_propertyNames.Contains(value.PropertyName))
                    {
                        _propertyNames.Add(value.PropertyName);
                        _propertyTypes.Add(value.DataType);
                    }
                }
            }

            var table = new DataTable();
            table.Columns.Add(NoColumnName, typeof(int));
            table.Columns.Add(SelectedColumnName, typeof(bool));
            table.Columns.Add(IdColumnName, typeof(int));
            table.Columns.Add(DateColumnName, typeof(string));

            // Özellik adlarında özel karakter olabileceği için sütunlara güvenli
            // adlar (p0, p1...) veriyoruz; ekranda görünen başlık gerçek addır.
            for (var i = 0; i < _propertyNames.Count; i++)
            {
                table.Columns.Add("p" + i, typeof(string));
            }

            var rowNumber = 0;
            foreach (var item in typeItems)
            {
                List<ScrapValue> values;
                if (!_values.TryGetValue(item.Id, out values))
                {
                    values = new List<ScrapValue>();
                }

                rowNumber++;

                var row = table.NewRow();
                row[NoColumnName] = rowNumber;
                row[SelectedColumnName] = false;
                row[IdColumnName] = item.Id;
                row[DateColumnName] = item.ScrappedAt;

                for (var i = 0; i < _propertyNames.Count; i++)
                {
                    var found = values.FirstOrDefault(v => v.PropertyName == _propertyNames[i]);
                    row["p" + i] = found != null ? found.TextValue : "";
                }

                table.Rows.Add(row);
            }

            BuildGridColumns();
            BuildFilterPanel(table);

            ProductGrid.ItemsSource = table.DefaultView;
            ApplyFilters();
        }

        /// <summary>
        /// Tablonun sütunlarını kurar: onay kutusu, sıra no, her özellik için bir sütun, hurdaya taşınma tarihi.
        /// </summary>
        private void BuildGridColumns()
        {
            ProductGrid.Columns.Clear();

            var checkFactory = new FrameworkElementFactory(typeof(CheckBox));
            checkFactory.SetBinding(CheckBox.IsCheckedProperty, new Binding(SelectedColumnName)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            checkFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);

            ProductGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "",
                Width = new DataGridLength(36),
                CellTemplate = new DataTemplate { VisualTree = checkFactory }
            });

            ProductGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Sıra No",
                Binding = new Binding(NoColumnName),
                Width = new DataGridLength(70),
                IsReadOnly = true
            });

            for (var i = 0; i < _propertyNames.Count; i++)
            {
                ProductGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = _propertyNames[i],
                    Binding = new Binding("p" + i),
                    Width = new DataGridLength(140),
                    IsReadOnly = true
                });
            }

            ProductGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Hurdaya taşındı",
                Binding = new Binding(DateColumnName),
                Width = new DataGridLength(140),
                IsReadOnly = true
            });
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            ShowHome();
        }

        // ---------- FİLTRE BARI ----------

        private void ShowNormalBar()
        {
            NormalLeftPanel.Visibility = Visibility.Visible;
            FilterLeftPanel.Visibility = Visibility.Collapsed;
        }

        private void ShowFilterBar()
        {
            NormalLeftPanel.Visibility = Visibility.Collapsed;
            FilterLeftPanel.Visibility = Visibility.Visible;
        }

        private void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            ShowFilterBar();
        }

        private void FilterBackButton_Click(object sender, RoutedEventArgs e)
        {
            ShowNormalBar();
        }

        private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
        {
            _selected.Clear();

            // Onay kutularının işaretini de temizlemek için paneli yeniden kur.
            var view = ProductGrid.ItemsSource as DataView;
            if (view != null)
            {
                BuildFilterPanel(view.Table);
            }

            ApplyFilters();
        }

        /// <summary>
        /// Cinsin sütunlarından ve hurdadaki mevcut değerlerden filtre onay kutularını oluşturur.
        /// </summary>
        private void BuildFilterPanel(DataTable table)
        {
            FilterItemsPanel.Children.Clear();

            for (var index = 0; index < _propertyNames.Count; index++)
            {
                var column = "p" + index;
                var dataType = _propertyTypes[index];

                var distinct = new HashSet<string>();
                foreach (DataRow row in table.Rows)
                {
                    if (row.IsNull(column))
                    {
                        continue;
                    }

                    var value = (string)row[column];
                    if (value.Length > 0)
                    {
                        distinct.Add(value);
                    }
                }

                // Artık var olmayan değerler seçimden düşer (örneğin ürün geri getirildiyse).
                HashSet<string> chosen;
                if (_selected.TryGetValue(index, out chosen))
                {
                    chosen.IntersectWith(distinct);
                    if (chosen.Count == 0)
                    {
                        _selected.Remove(index);
                        chosen = null;
                    }
                }
                else
                {
                    chosen = null;
                }

                var content = new StackPanel { Margin = new Thickness(4, 4, 0, 4) };

                if (distinct.Count == 0)
                {
                    content.Children.Add(new TextBlock
                    {
                        Text = "(kayıtlı değer yok)",
                        Foreground = Brushes.Gray
                    });
                }
                else
                {
                    var valueSearchBox = new TextBox
                    {
                        Height = 24,
                        Margin = new Thickness(0, 0, 0, 4),
                        VerticalContentAlignment = VerticalAlignment.Center,
                        ToolTip = "Değerlerde ara"
                    };

                    valueSearchBox.TextChanged += (s, e) =>
                        FilterValueSearchBox_TextChanged(valueSearchBox, content);

                    content.Children.Add(valueSearchBox);
                }

                foreach (var value in SortValues(distinct, dataType))
                {
                    var box = new CheckBox
                    {
                        Content = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap },
                        Tag = Tuple.Create(index, value),
                        Margin = new Thickness(0, 2, 0, 2),
                        IsChecked = chosen != null && chosen.Contains(value)
                    };

                    box.Checked += FilterCheckBox_Changed;
                    box.Unchecked += FilterCheckBox_Changed;
                    content.Children.Add(box);
                }

                FilterItemsPanel.Children.Add(new Expander
                {
                    Header = _propertyNames[index],
                    IsExpanded = true,
                    Margin = new Thickness(0, 0, 0, 6),
                    Content = content
                });
            }

            if (_propertyNames.Count == 0)
            {
                FilterItemsPanel.Children.Add(new TextBlock
                {
                    Text = "Bu cinste filtrelenecek alan yok.",
                    Foreground = Brushes.Gray,
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }

        /// <summary>
        /// Bir özelliğin değer arama kutusuna yazıldıkça, altındaki onay kutularından
        /// yazıyla eşleşmeyenleri gizler (işaretli kutuların seçimini etkilemez).
        /// </summary>
        private static void FilterValueSearchBox_TextChanged(TextBox searchBox, StackPanel content)
        {
            var text = searchBox.Text.Trim();

            foreach (var child in content.Children)
            {
                var box = child as CheckBox;
                if (box == null)
                {
                    continue;
                }

                var label = ((TextBlock)box.Content).Text;

                box.Visibility = text.Length == 0 ||
                                  label.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Değerleri alanın türüne göre sıralar: sayılar sayı gibi, tarihler tarih gibi, yazılar Türkçe alfabeyle.
        /// </summary>
        private static List<string> SortValues(HashSet<string> values, string dataType)
        {
            var list = values.ToList();
            var turkish = new CultureInfo("tr-TR");

            if (dataType == "Sayı")
            {
                list.Sort((a, b) =>
                {
                    double x, y;
                    var okX = double.TryParse(a, NumberStyles.Float, turkish, out x);
                    var okY = double.TryParse(b, NumberStyles.Float, turkish, out y);

                    if (okX && okY)
                    {
                        return x.CompareTo(y);
                    }

                    return string.Compare(a, b, turkish, CompareOptions.IgnoreCase);
                });
            }
            else if (dataType == "Tarih")
            {
                list.Sort((a, b) =>
                {
                    DateTime x, y;
                    var okX = DateTime.TryParseExact(a, "dd.MM.yyyy",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out x);
                    var okY = DateTime.TryParseExact(b, "dd.MM.yyyy",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out y);

                    if (okX && okY)
                    {
                        return x.CompareTo(y);
                    }

                    return string.Compare(a, b, turkish, CompareOptions.IgnoreCase);
                });
            }
            else
            {
                list.Sort((a, b) => string.Compare(a, b, turkish, CompareOptions.IgnoreCase));
            }

            return list;
        }

        /// <summary>
        /// Bir onay kutusu işaretlenince ya da işareti kalkınca çalışır.
        /// </summary>
        private void FilterCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var box = (CheckBox)sender;
            var tag = (Tuple<int, string>)box.Tag;

            HashSet<string> set;

            if (box.IsChecked == true)
            {
                if (!_selected.TryGetValue(tag.Item1, out set))
                {
                    set = new HashSet<string>();
                    _selected[tag.Item1] = set;
                }

                set.Add(tag.Item2);
            }
            else if (_selected.TryGetValue(tag.Item1, out set))
            {
                set.Remove(tag.Item2);

                if (set.Count == 0)
                {
                    _selected.Remove(tag.Item1);
                }
            }

            ApplyFilters();
        }

        // ---------- ARAMA VE FİLTRELERİ UYGULAMA ----------

        private void TypeSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TypeSearchPlaceholder.Visibility = string.IsNullOrEmpty(TypeSearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

            ApplyFilters();
        }

        /// <summary>
        /// Arama kutusuna yazılan metni (tüm sütunlarda, 2 harften itibaren) ve seçili
        /// filtrelerin hepsini birlikte uygular. Farklı alanlar "ve", aynı alandaki değerler "veya" ile birleşir.
        /// </summary>
        private void ApplyFilters()
        {
            var view = ProductGrid.ItemsSource as DataView;
            if (view == null)
            {
                return;
            }

            var conditions = new List<string>();

            var text = TypeSearchBox.Text.Trim();
            if (text.Length >= 2)
            {
                var pattern = EscapeForLike(text);

                var searchConditions = view.Table.Columns.Cast<DataColumn>()
                    .Where(c => c.ColumnName != NoColumnName
                             && c.ColumnName != SelectedColumnName
                             && c.ColumnName != IdColumnName)
                    .Select(c => "[" + c.ColumnName + "] LIKE '%" + pattern + "%'");

                conditions.Add("(" + string.Join(" OR ", searchConditions) + ")");
            }

            foreach (var pair in _selected)
            {
                if (pair.Key >= _propertyNames.Count)
                {
                    continue;
                }

                var quoted = pair.Value.Select(v => "'" + v.Replace("'", "''") + "'");
                conditions.Add("[p" + pair.Key + "] IN (" + string.Join(",", quoted) + ")");
            }

            view.RowFilter = string.Join(" AND ", conditions);

            var total = view.Table.Rows.Count;

            ProductCountText.Text = conditions.Count > 0
                ? view.Count + " / " + total + " ürün"
                : total + " ürün";

            FilterButton.Content = _selected.Count > 0
                ? "Filtrele (" + _selected.Count + ")"
                : "Filtrele";
        }

        /// <summary>
        /// Aramaya yazılan özel karakterlerin (' * % [ ]) süzme ifadesini bozmasını engeller.
        /// </summary>
        private static string EscapeForLike(string text)
        {
            var result = new System.Text.StringBuilder();

            foreach (var c in text)
            {
                if (c == '\'')
                {
                    result.Append("''");
                }
                else if (c == '*' || c == '%' || c == '[' || c == ']')
                {
                    result.Append('[').Append(c).Append(']');
                }
                else
                {
                    result.Append(c);
                }
            }

            return result.ToString();
        }

        // ---------- DETAY PENCERESİ ----------

        private void ProductGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Onay kutusuna çift tıklandıysa detay penceresini açma.
            if (IsCheckBoxClick(e.OriginalSource))
            {
                return;
            }

            var rowView = ProductGrid.SelectedItem as DataRowView;
            if (rowView == null)
            {
                return;
            }

            var scrapId = (int)rowView[IdColumnName];
            var item = _items.FirstOrDefault(i => i.Id == scrapId);
            if (item == null)
            {
                return;
            }

            var window = new ScrapDetailWindow(item) { Owner = this };
            window.ShowDialog();

            if (window.Changed)
            {
                RefreshAfterChange();
            }
        }

        /// <summary>
        /// Tıklanan yerin bir onay kutusunun (ya da içindeki bir şeyin) üzerinde olup olmadığını bulur.
        /// </summary>
        private static bool IsCheckBoxClick(object originalSource)
        {
            var element = originalSource as DependencyObject;

            while (element != null)
            {
                if (element is CheckBox)
                {
                    return true;
                }

                if (element is DataGridRow)
                {
                    return false;
                }

                element = VisualTreeHelper.GetParent(element);
            }

            return false;
        }

        // ---------- TOPLU İŞLEMLER ----------

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            var view = ProductGrid.ItemsSource as DataView;
            if (view == null || view.Count == 0)
            {
                return;
            }

            // Hepsi zaten işaretliyse hepsini kaldır; değilse hepsini işaretle.
            var allSelected = true;
            for (var i = 0; i < view.Count; i++)
            {
                if (!(bool)view[i][SelectedColumnName])
                {
                    allSelected = false;
                    break;
                }
            }

            var newValue = !allSelected;

            for (var i = 0; i < view.Count; i++)
            {
                view[i][SelectedColumnName] = newValue;
            }
        }

        private List<int> GetSelectedIds()
        {
            var ids = new List<int>();

            var view = ProductGrid.ItemsSource as DataView;
            if (view == null)
            {
                return ids;
            }

            foreach (DataRowView rowView in view)
            {
                if ((bool)rowView[SelectedColumnName])
                {
                    ids.Add((int)rowView[IdColumnName]);
                }
            }

            return ids;
        }

        private void RestoreSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var ids = GetSelectedIds();
            if (ids.Count == 0)
            {
                MessageBox.Show("Önce geri getirmek istediğin ürünlerin kutucuğunu işaretle.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                "Seçili " + ids.Count + " ürün depoya geri getirilecek, onaylıyor musun?",
                "Depo Durumu", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            var restoredCount = 0;
            var errors = new List<string>();

            foreach (var id in ids)
            {
                var error = ScrapRepository.Restore(id);
                if (error == null)
                {
                    restoredCount++;
                }
                else
                {
                    errors.Add(error);
                }
            }

            var message = restoredCount + " ürün depoya geri getirildi.";
            if (errors.Count > 0)
            {
                message += "\n\nGeri getirilemeyen " + errors.Count + " ürün var:\n- " + string.Join("\n- ", errors);
            }

            MessageBox.Show(message, "Depo Durumu", MessageBoxButton.OK,
                errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

            RefreshAfterChange();
        }

        private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var ids = GetSelectedIds();
            if (ids.Count == 0)
            {
                MessageBox.Show("Önce silmek istediğin ürünlerin kutucuğunu işaretle.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                "Seçili " + ids.Count + " ürün hurdadan KALICI olarak silinecek.\n\nBu işlem geri alınamaz, onaylıyor musun?",
                "Depo Durumu", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            foreach (var id in ids)
            {
                ScrapRepository.DeletePermanently(id);
            }

            RefreshAfterChange();
        }

        /// <summary>
        /// Bir işlemden sonra verileri yeniler; cinsin hurdada hiç ürünü kalmadıysa ana sayfaya döner.
        /// </summary>
        private void RefreshAfterChange()
        {
            var typeName = _currentTypeName;

            LoadData();

            if (typeName != null && _items.Any(i => i.TypeName == typeName))
            {
                LoadTypeGrid();
            }
            else
            {
                ShowHome();
            }
        }
    }
}