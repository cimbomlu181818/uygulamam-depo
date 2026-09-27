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
    public partial class MainWindow : Window
    {
        // Tabloda "Sıra No", "Seçili" (toplu silme onay kutusu) ve gizli "Ürün Id"
        // için kullanılan dahili sütun adları. Bir özellik yanlışlıkla bu isimle
        // çakışmasın diye normal isimlerden farklı seçildi.
        private const string NoColumnName = "__No";
        private const string SelectedColumnName = "__Selected";
        private const string IdColumnName = "__ProductId";

        private ProductType _currentType;
        private List<Product> _currentProducts = new List<Product>();
        private List<PropertyDefinition> _currentProperties = new List<PropertyDefinition>();

        // Seçili filtreler: alan numarası -> seçilen değerler.
        private readonly Dictionary<int, HashSet<string>> _selected = new Dictionary<int, HashSet<string>>();

        public MainWindow()
        {
            InitializeComponent();
            ShowHome();
        }

        private void ShowHome()
        {
            LoadProductTypes();

            HomePage.Visibility = Visibility.Visible;
            TypePage.Visibility = Visibility.Collapsed;
            _currentType = null;
        }

        private void LoadProductTypes()
        {
            ProductTypeList.ItemsSource = null;
            ProductTypeList.DisplayMemberPath = "Name";
            ProductTypeList.ItemsSource = ProductTypeRepository.GetAll();
        }

        private void ProductTypeList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var selected = ProductTypeList.SelectedItem as ProductType;
            if (selected == null)
            {
                return;
            }

            ShowTypePage(selected);
        }

        private void ShowTypePage(ProductType type)
        {
            _currentType = type;
            TypePageTitle.Text = type.Name;
            TypeSearchBox.Text = "";

            // Eski arama ve filtreler temizlenir.
            _selected.Clear();
            ShowNormalBar();

            HomePage.Visibility = Visibility.Collapsed;
            TypePage.Visibility = Visibility.Visible;

            LoadProductGrid(type);
        }

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

        private void LoadProductGrid(ProductType type)
        {
            var properties = TypePropertyRepository.GetForType(type.Id);
            _currentProperties = properties;

            var table = new DataTable();
            table.Columns.Add(NoColumnName, typeof(int));
            table.Columns.Add(SelectedColumnName, typeof(bool));
            table.Columns.Add(IdColumnName, typeof(int));

            foreach (var property in properties)
            {
                table.Columns.Add(property.Name, typeof(string));
            }

            // Bu cinse ait gerçek ürünleri ve değerlerini tabloya satır olarak ekle.
            // Her satırda gizli __ProductId sütunu tutulur; çift tıklama ve silme
            // işlemleri artık satır sırasına değil, doğrudan bu Id'ye bakar. Böylece
            // bir filtre uygulanmışken bile yanlış ürün açılmaz/silinmez.
            var products = ProductRepository.GetForType(type.Id);
            _currentProducts = products;

            var rowNumber = 0;
            foreach (var product in products)
            {
                rowNumber++;
                var values = ProductRepository.GetValues(product.Id);

                var row = table.NewRow();
                row[NoColumnName] = rowNumber;
                row[SelectedColumnName] = false;
                row[IdColumnName] = product.Id;

                foreach (var property in properties)
                {
                    row[property.Name] = values.ContainsKey(property.Id) ? values[property.Id] : "";
                }
                table.Rows.Add(row);
            }

            BuildGridColumns(properties);
            ProductGrid.ItemsSource = table.DefaultView;

            BuildFilterPanel(table, properties);
            ApplyFilters();
        }

        /// <summary>
        /// Tablonun sütunlarını elle kurar: onay kutusu, sıra no, sonra her özellik için bir sütun.
        /// </summary>
        private void BuildGridColumns(List<PropertyDefinition> properties)
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

            foreach (var property in properties)
            {
                ProductGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = property.Name,
                    Binding = new Binding(property.Name),
                    Width = new DataGridLength(140)
                });
            }
        }

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

            var productId = (int)rowView[IdColumnName];
            var product = _currentProducts.FirstOrDefault(p => p.Id == productId);
            if (product == null)
            {
                return;
            }

            var window = new ProductDetailWindow(_currentType, product.Id) { Owner = this };
            window.ShowDialog();

            LoadProductGrid(_currentType);
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

        private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var view = ProductGrid.ItemsSource as DataView;
            if (view == null || _currentType == null)
            {
                return;
            }

            var toDelete = new List<int>();

            foreach (DataRowView rowView in view)
            {
                var isSelected = (bool)rowView[SelectedColumnName];
                if (isSelected)
                {
                    toDelete.Add((int)rowView[IdColumnName]);
                }
            }

            if (toDelete.Count == 0)
            {
                MessageBox.Show("Önce silmek istediğin ürünlerin kutucuğunu işaretle.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                "Seçili " + toDelete.Count + " ürün silinecek, onaylıyor musun?",
                "Depo Durumu", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            foreach (var productId in toDelete)
            {
                ProductRepository.Delete(productId);
            }

            LoadProductGrid(_currentType);
        }

        // ---------- FİLTRE BARI ----------

        /// <summary>
        /// Tipin alanlarından ve ürünlerdeki mevcut değerlerden filtre onay kutularını oluşturur.
        /// </summary>
        private void BuildFilterPanel(DataTable table, List<PropertyDefinition> properties)
        {
            FilterItemsPanel.Children.Clear();

            bool anyProperty = false;

            foreach (var property in properties)
            {
                anyProperty = true;
                string column = property.Name;

                var distinct = new HashSet<string>();
                foreach (DataRow row in table.Rows)
                {
                    if (row.IsNull(column))
                    {
                        continue;
                    }

                    string value = (string)row[column];
                    if (value.Length > 0)
                    {
                        distinct.Add(value);
                    }
                }

                // Artık var olmayan değerler seçimden düşer (örneğin ürün silindiyse).
                HashSet<string> chosen;
                if (_selected.TryGetValue(property.Id, out chosen))
                {
                    chosen.IntersectWith(distinct);
                    if (chosen.Count == 0)
                    {
                        _selected.Remove(property.Id);
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
                    // Değer sayısı fazla olabileceği için, listenin üstüne küçük bir arama
                    // kutusu koyuyoruz. Bu kutu sadece hangi kutucukların görüneceğini
                    // belirler; işaretli değerler görünürlükten bağımsız olarak seçili kalır.
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

                foreach (string value in SortValues(distinct, property.DataType))
                {
                    var box = new CheckBox
                    {
                        Content = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap },
                        Tag = Tuple.Create(property.Id, value),
                        Margin = new Thickness(0, 2, 0, 2),
                        IsChecked = chosen != null && chosen.Contains(value)
                    };

                    box.Checked += FilterCheckBox_Changed;
                    box.Unchecked += FilterCheckBox_Changed;
                    content.Children.Add(box);
                }

                FilterItemsPanel.Children.Add(new Expander
                {
                    Header = property.Name,
                    IsExpanded = true,
                    Margin = new Thickness(0, 0, 0, 6),
                    Content = content
                });
            }

            if (!anyProperty)
            {
                FilterItemsPanel.Children.Add(new TextBlock
                {
                    Text = "Bu tipte filtrelenecek alan yok.",
                    Foreground = Brushes.Gray,
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }

        /// <summary>
        /// Bir özelliğin değer arama kutusuna yazıldıkça, o özelliğin altındaki onay kutularından
        /// yazıyla eşleşmeyenleri gizler. Sadece görünürlüğü değiştirir; işaretli kutuların
        /// seçimini (_selected) etkilemez.
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
                    bool okX = double.TryParse(a, NumberStyles.Float, turkish, out x);
                    bool okY = double.TryParse(b, NumberStyles.Float, turkish, out y);

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
                    bool okX = DateTime.TryParseExact(a, "dd.MM.yyyy",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out x);
                    bool okY = DateTime.TryParseExact(b, "dd.MM.yyyy",
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
        /// Arama kutusuna yazılan metni (tüm özellik sütunlarında, 2 harften itibaren) ve
        /// seçili filtrelerin hepsini birlikte uygular.
        /// Farklı alanlar "ve", aynı alandaki değerler "veya" ile birleşir.
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
                var property = _currentProperties.FirstOrDefault(p => p.Id == pair.Key);
                if (property == null)
                {
                    continue;
                }

                var quoted = pair.Value.Select(v => "'" + v.Replace("'", "''") + "'");
                conditions.Add("[" + property.Name + "] IN (" + string.Join(",", quoted) + ")");
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

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            ShowHome();
        }

        private void HomeMenu_Click(object sender, RoutedEventArgs e)
        {
            ShowHome();
        }

        private void SettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            var window = new SettingsWindow { Owner = this };
            window.ShowDialog();

            ShowHome();
        }

        // ---------- ANA SAYFA ARAMASI ----------

        private void HomeSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            HomeSearchPlaceholder.Visibility = string.IsNullOrEmpty(HomeSearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

            RunHomeSearch();
        }

        private void HomeSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CloseHomeSearch();
            }
        }

        private void HomeSearchCloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseHomeSearch();
        }

        /// <summary>
        /// Kutudaki yazıyı tüm ürün cinslerindeki tüm özelliklerde arar (2 harften itibaren).
        /// Sonuçları, ürün cinsi listesinin altına açılan kutuda, cinse göre gruplanmış
        /// tablolar halinde gösterir.
        /// </summary>
        private void RunHomeSearch()
        {
            var text = HomeSearchBox.Text.Trim();

            if (text.Length < 2)
            {
                CloseHomeSearch();
                return;
            }

            var groups = ProductSearchRepository.Search(text);

            if (groups.Count == 0)
            {
                HomeSearchGroupsPanel.Children.Clear();
                HomeSearchGroupsPanel.Children.Add(new TextBlock
                {
                    Text = "Sonuç bulunamadı.",
                    Margin = new Thickness(4),
                    Foreground = Brushes.Gray
                });
                HomeSearchPopup.IsOpen = true;
                return;
            }

            BuildHomeSearchGroups(groups, text);
            HomeSearchPopup.IsOpen = true;
        }

        private void CloseHomeSearch()
        {
            HomeSearchPopup.IsOpen = false;
            HomeSearchGroupsPanel.Children.Clear();
        }

        /// <summary>
        /// Her ürün cinsi grubu için bir başlık (Expander) ve altında o cinsin
        /// sütunlarını gösteren bir tablo (DataGrid) oluşturur.
        /// </summary>
        private void BuildHomeSearchGroups(List<HomeSearchGroup> groups, string searchText)
        {
            HomeSearchGroupsPanel.Children.Clear();

            foreach (var group in groups)
            {
                var grid = new DataGrid
                {
                    ItemsSource = group.View,
                    AutoGenerateColumns = true,
                    IsReadOnly = true,
                    CanUserAddRows = false,
                    CanUserDeleteRows = false,
                    CanUserReorderColumns = false,
                    CanUserSortColumns = false,
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                    GridLinesVisibility = DataGridGridLinesVisibility.All,
                    MaxHeight = 220
                };

                grid.AutoGeneratingColumn += (s, e) =>
                    HomeSearchGrid_AutoGeneratingColumn(s, e, searchText);

                var capturedGroup = group;
                var capturedGrid = grid;
                grid.MouseDoubleClick += (s, e) =>
                    HomeSearchGrid_MouseDoubleClick(capturedGrid, capturedGroup);

                var expander = new Expander
                {
                    Header = group.TypeName + " (" + group.View.Count + ")",
                    FontWeight = FontWeights.SemiBold,
                    IsExpanded = true,
                    Margin = new Thickness(0, 0, 0, 8),
                    Content = grid
                };

                HomeSearchGroupsPanel.Children.Add(expander);
            }
        }

        /// <summary>
        /// Tablo sütunları otomatik oluşurken, gizli "__ProductId" sütununu saklar ve
        /// aranan yazıyla eşleşen hücrelerin kırmızı görünmesini sağlar.
        /// </summary>
        private void HomeSearchGrid_AutoGeneratingColumn(
            object sender, DataGridAutoGeneratingColumnEventArgs e, string searchText)
        {
            if (e.PropertyName == ProductSearchRepository.ProductIdColumn)
            {
                e.Cancel = true;
                return;
            }

            var textColumn = e.Column as DataGridTextColumn;
            if (textColumn == null)
            {
                return;
            }

            var style = new Style(typeof(TextBlock));
            var binding = new Binding(e.PropertyName)
            {
                Converter = new HomeSearchHighlightConverter(searchText)
            };
            style.Setters.Add(new Setter(TextBlock.ForegroundProperty, binding));
            textColumn.ElementStyle = style;
        }

        /// <summary>
        /// Sonuç tablosunda bir satıra çift tıklanınca o ürünün cinsinin sayfasını açar
        /// ve arama kutusundaki yazıyı o sayfanın kendi arama kutusuna da yazar.
        /// </summary>
        private void HomeSearchGrid_MouseDoubleClick(DataGrid grid, HomeSearchGroup group)
        {
            var rowView = grid.SelectedItem as DataRowView;
            if (rowView == null)
            {
                return;
            }

            var type = ProductTypeRepository.GetAll().FirstOrDefault(t => t.Id == group.ProductTypeId);
            if (type == null)
            {
                return;
            }

            var text = HomeSearchBox.Text.Trim();

            HomeSearchBox.Clear();
            CloseHomeSearch();

            ShowTypePage(type);
            TypeSearchBox.Text = text;
        }

        /// <summary>
        /// Bir hücrenin yazısı aranan yazıyı içeriyorsa kırmızı, içermiyorsa siyah verir.
        /// </summary>
        private class HomeSearchHighlightConverter : IValueConverter
        {
            private readonly string _searchText;

            public HomeSearchHighlightConverter(string searchText)
            {
                _searchText = searchText;
            }

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var text = value as string;
                if (!string.IsNullOrEmpty(text) &&
                    text.IndexOf(_searchText, StringComparison.CurrentCultureIgnoreCase) >= 0)
                {
                    return Brushes.Red;
                }

                return Brushes.Black;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>
        /// Sonuç kutusunun sağ-alt köşesindeki tutamaç sürüklenince kutuyu büyütür/küçültür.
        /// </summary>
        private void HomeSearchResizeThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            var newWidth = HomeSearchOverlayBorder.Width + e.HorizontalChange;
            var newHeight = HomeSearchOverlayBorder.Height + e.VerticalChange;

            if (newWidth >= 400)
            {
                HomeSearchOverlayBorder.Width = newWidth;
            }

            if (newHeight >= 200)
            {
                HomeSearchOverlayBorder.Height = newHeight;
            }
        }

        private void AssignPropertiesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentType == null)
            {
                return;
            }

            var window = new AssignPropertiesWindow(_currentType.Id, _currentType.Name) { Owner = this };
            window.ShowDialog();

            // Pencere kapanınca tablo sütunlarını güncel özelliklerle yeniden çiz.
            LoadProductGrid(_currentType);
        }

        private void AddProductButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentType == null)
            {
                return;
            }

            var window = new AddProductWindow(_currentType.Id, _currentType.Name) { Owner = this };
            var result = window.ShowDialog();

            if (result == true)
            {
                LoadProductGrid(_currentType);
            }
        }
    }
}