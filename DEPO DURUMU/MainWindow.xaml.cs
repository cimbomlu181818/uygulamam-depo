using System.Collections.Generic;
using System.Data;
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