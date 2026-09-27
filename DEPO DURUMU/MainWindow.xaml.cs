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
        // Tabloda "Sıra No" ve "Seçili" (toplu silme onay kutusu) için kullanılan
        // dahili sütun adları. Bir özellik yanlışlıkla bu isimle çakışmasın diye
        // normal isimlerden farklı seçildi.
        private const string NoColumnName = "__No";
        private const string SelectedColumnName = "__Selected";

        private ProductType _currentType;
        private List<Product> _currentProducts = new List<Product>();

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

            HomePage.Visibility = Visibility.Collapsed;
            TypePage.Visibility = Visibility.Visible;

            LoadProductGrid(type);
        }

        private void LoadProductGrid(ProductType type)
        {
            var properties = TypePropertyRepository.GetForType(type.Id);

            var table = new DataTable();
            table.Columns.Add(NoColumnName, typeof(int));
            table.Columns.Add(SelectedColumnName, typeof(bool));

            foreach (var property in properties)
            {
                table.Columns.Add(property.Name, typeof(string));
            }

            // Bu cinse ait gerçek ürünleri ve değerlerini tabloya satır olarak ekle.
            // _currentProducts, tablodaki satır sırasıyla birebir aynı sırada tutulur;
            // böylece bir satıra çift tıklandığında ya da işaretlendiğinde hangi ürün
            // olduğunu buluruz.
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

                foreach (var property in properties)
                {
                    row[property.Name] = values.ContainsKey(property.Id) ? values[property.Id] : "";
                }
                table.Rows.Add(row);
            }

            BuildGridColumns(properties);
            ProductGrid.ItemsSource = table.DefaultView;
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

            var index = ProductGrid.Items.IndexOf(rowView);
            if (index < 0 || index >= _currentProducts.Count)
            {
                return;
            }

            var product = _currentProducts[index];

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

            var toDelete = new List<Product>();

            for (var i = 0; i < view.Count; i++)
            {
                var isSelected = (bool)view[i][SelectedColumnName];
                if (isSelected && i < _currentProducts.Count)
                {
                    toDelete.Add(_currentProducts[i]);
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

            foreach (var product in toDelete)
            {
                ProductRepository.Delete(product.Id);
            }

            LoadProductGrid(_currentType);
        }

        private void TypeSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TypeSearchPlaceholder.Visibility = string.IsNullOrEmpty(TypeSearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

            ApplyTypeSearch();
        }

        /// <summary>
        /// Cins sayfasındaki arama kutusuna yazılan metni, o cinsin TÜM özellik
        /// sütunlarında arar (2 harften itibaren). Sıra No ve Seçili sütunlarını hariç tutar.
        /// </summary>
        private void ApplyTypeSearch()
        {
            var view = ProductGrid.ItemsSource as DataView;
            if (view == null)
            {
                return;
            }

            var text = TypeSearchBox.Text.Trim();

            if (text.Length < 2)
            {
                view.RowFilter = "";
                return;
            }

            var pattern = EscapeForLike(text);

            var conditions = view.Table.Columns.Cast<DataColumn>()
                .Where(c => c.ColumnName != NoColumnName && c.ColumnName != SelectedColumnName)
                .Select(c => "[" + c.ColumnName + "] LIKE '%" + pattern + "%'");

            view.RowFilter = string.Join(" OR ", conditions);
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