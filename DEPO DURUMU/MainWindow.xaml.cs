using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
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
        private const string QuantityColumnName = "__Quantity";
        // Her özellik sütunu için gizli renk sütununun ön eki (ör. __Color_12).
        private const string ColorColumnPrefix = "__Color_";

        // Satır kes/kopyala panosu (uygulamanın kendi içinde tutulur).
        private List<int> _rowClipboardIds = new List<int>();
        private bool _rowClipboardIsCut;

        private ProductType _currentType;

        // Genel aramanın şu an hangi kutudan (ana sayfa / cins sayfası) yapıldığı.
        private TextBox _searchSource;
        private TextBox SearchSource { get { return _searchSource ?? HomeSearchBox; } }
        private List<Product> _currentProducts = new List<Product>();
        private List<PropertyDefinition> _currentProperties = new List<PropertyDefinition>();

        // Seçili filtreler: alan numarası -> seçilen değerler.
        private readonly Dictionary<int, HashSet<string>> _selected = new Dictionary<int, HashSet<string>>();

        // Filtre çubuğunda kullanıcının açtığı özellik başlıkları (özellik numarası).
        // Cins sayfası ilk açıldığında boştur, yani tüm başlıklar kapalı gelir.
        private readonly HashSet<int> _openFilterIds = new HashSet<int>();

        // Ana sayfadaki istatistik kutuları ve sürükleme başlangıç noktası.
        private List<HomeStatisticCard> _homeStatisticCards = new List<HomeStatisticCard>();
        private Point _dragStartPoint;

        // İstatistik kutuları için düzenleme modu: "Düzenle" ile açılıp kapanır.
        // Aktifken tüm kutuların çerçevesi mavi olur ve kutular sürüklenip taşınabilir.
        private bool _statisticsEditMode;

        // Kutu çerçeveleri için renkler: normal, düzenleme modu aktif, basılı tutulup sürüklenen kutu.
        private static readonly Brush StatisticCardNormalBrush = Brushes.Gray;
        private static readonly Brush StatisticCardEditModeBrush = Brushes.SteelBlue;
        private static readonly Brush StatisticCardHeldBrush = Brushes.OrangeRed;

        public MainWindow()
        {
            InitializeComponent();
            PreviewMouseLeftButtonDown += MainWindow_PreviewMouseLeftButtonDown;
            ProductGrid.PreviewMouseRightButtonDown += ProductGrid_PreviewMouseRightButtonDown;
            ProductGrid.PreviewKeyDown += ProductGrid_PreviewKeyDown;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            ShowHome();
        }

        /// <summary>
        /// Düzenleme modu aktifken, bir istatistik kutusunun dışında herhangi bir
        /// yere tıklanırsa (sıralama zaten her taşımada kaydedildiğinden) düzenleme
        /// modunu kapatır.
        /// </summary>
        private void MainWindow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_statisticsEditMode)
            {
                return;
            }

            var element = e.OriginalSource as DependencyObject;
            while (element != null)
            {
                var border = element as Border;
                if (border != null && border.Tag is HomeStatisticCard)
                {
                    return;
                }

                element = VisualTreeHelper.GetParent(element);
            }

            _statisticsEditMode = false;
            LoadHomeStatistics();
        }

        private void ShowHome()
        {
            CloseHomeSearch();
            TypeFilterBox.Text = "";
            LoadProductTypes();
            LoadHomeStatistics();
            LoadRecentLog();

            HomePage.Visibility = Visibility.Visible;
            TypePage.Visibility = Visibility.Collapsed;
            _currentType = null;
        }

        // Veritabanındaki tüm cinsler; "Cinste ara" kutusu bu listeyi süzer.
        private List<ProductType> _allTypes = new List<ProductType>();

        private void LoadProductTypes()
        {
            _allTypes = ProductTypeRepository.GetAll();
            ApplyTypeFilter();
        }

        /// <summary>
        /// "Cinste ara" kutusuna yazılan harflerle başlayan cinsleri listeler.
        /// Kutu boşsa tüm cinsler görünür. Büyük/küçük harf ve Türkçe I/İ farkı önemsenmez.
        /// </summary>
        private void ApplyTypeFilter()
        {
            var text = (TypeFilterBox.Text ?? "").Trim();
            var turkish = new CultureInfo("tr-TR");

            IEnumerable<ProductType> items = _allTypes;
            if (text.Length > 0)
            {
                items = _allTypes.Where(t => WordStartsWith(t.Name, text, turkish));
            }

            ProductTypeList.ItemsSource = null;
            ProductTypeList.DisplayMemberPath = "Name";
            ProductTypeList.ItemsSource = items.ToList();
        }

        /// <summary>
        /// Cins adının herhangi bir kelimesi yazılan harflerle başlıyorsa true verir.
        /// Örn. "b" yazınca hem "Bilgisayar" hem "Kişisel Bilgisayar" eşleşir.
        /// </summary>
        private static bool WordStartsWith(string name, string text, CultureInfo culture)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            for (int i = 0; i < name.Length; i++)
            {
                // Sadece bir kelimenin başlangıcında (en başta ya da harf/rakam olmayan karakterden sonra) kontrol et.
                if (i > 0 && char.IsLetterOrDigit(name[i - 1]))
                {
                    continue;
                }

                if (culture.CompareInfo.IsPrefix(name.Substring(i), text, CompareOptions.IgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void TypeFilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TypeFilterPlaceholder.Visibility = string.IsNullOrEmpty(TypeFilterBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

            ApplyTypeFilter();
        }

        /// <summary>Ana sayfadaki "Son işlem yapılan cihazlar" tablosunu, kayıt defterinin son 20 satırıyla doldurur.</summary>
        private void LoadRecentLog()
        {
            var recent = LogRepository.GetRecent(20);

            RecentLogGrid.ItemsSource = recent;
            RecentLogGrid.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            NoRecentLogText.Visibility = recent.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void LogMenu_Click(object sender, RoutedEventArgs e)
        {
            var window = new LogWindow { Owner = this };
            window.ShowDialog();
        }

        private void ColorSettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            var window = new ColorSettingsWindow { Owner = this };
            window.ShowDialog();
        }

        private void AssignmentsMenu_Click(object sender, RoutedEventArgs e)
        {
            var window = new AssignmentsWindow { Owner = this };
            window.ShowDialog();

            RefreshTypePageIfOpen();
        }

        private void HandoversMenu_Click(object sender, RoutedEventArgs e)
        {
            var window = new HandoversWindow { Owner = this };
            window.ShowDialog();
        }

        /// <summary>Zimmetler ekranında bir şey değişmiş olabilir; bir ürün cinsi sayfası açıksa tablo tazelenir.</summary>
        private void RefreshTypePageIfOpen()
        {
            if (_currentType != null && TypePage.Visibility == Visibility.Visible)
            {
                LoadProductGrid(_currentType);
            }
        }

        // ---------- ANA SAYFA İSTATİSTİKLERİ ----------

        /// <summary>
        /// Kayıtlı istatistik kutularını okuyup ana sayfada kart olarak gösterir.
        /// HomeStatisticsRepository.GetAll(), artık geçerli olmayan (özelliği cinsten
        /// kaldırılmış ya da cinsi silinmiş) kutuları veritabanından da otomatik siler.
        /// Sağ tık menüsünden "Düzenle" ile düzenleme modu açılıp kapatılır; mod
        /// aktifken kutuların çerçevesi mavi olur ve kutular basılı tutulup başka bir
        /// kutunun üzerine bırakılarak yer değiştirebilir. "Sil" kutuyu kaldırır.
        /// </summary>
        private void LoadHomeStatistics()
        {
            StatisticsPanel.Children.Clear();
            _homeStatisticCards = HomeStatisticsRepository.GetAll();

            foreach (var card in _homeStatisticCards)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                panel.Children.Add(new TextBlock
                {
                    Text = card.DisplayText,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.SemiBold
                });

                var border = new Border
                {
                    BorderBrush = _statisticsEditMode ? StatisticCardEditModeBrush : StatisticCardNormalBrush,
                    BorderThickness = _statisticsEditMode ? new Thickness(2) : new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 10, 10),
                    Background = Brushes.White,
                    AllowDrop = true,
                    Tag = card,
                    Child = panel,
                    ContextMenu = BuildStatisticContextMenu(card)
                };

                border.PreviewMouseLeftButtonDown += StatisticCard_PreviewMouseLeftButtonDown;
                border.PreviewMouseLeftButtonUp += StatisticCard_PreviewMouseLeftButtonUp;
                border.MouseMove += StatisticCard_MouseMove;
                border.Drop += StatisticCard_Drop;

                StatisticsPanel.Children.Add(border);
            }

            // Düzenleme modunda değilken, en sona tıklanınca yeni istatistik ekleyen "+" kutusu.
            if (!_statisticsEditMode)
            {
                StatisticsPanel.Children.Add(BuildAddStatisticCard());
            }
        }

        /// <summary>Son kutunun yanına eklenen, tıklanınca yeni istatistik ekleyen "+" kutusu.</summary>
        private Border BuildAddStatisticCard()
        {
            var addBorder = new Border
            {
                BorderBrush = StatisticCardNormalBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 10, 10),
                Background = Brushes.White,
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = "+ İstatistik Ekle",
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            addBorder.MouseLeftButtonUp += (s, e) => AddStatisticButton_Click(s, e);

            return addBorder;
        }

        /// <summary>Bir kartın sağ tık menüsünü (Düzenle / Sil) oluşturur.</summary>
        private ContextMenu BuildStatisticContextMenu(HomeStatisticCard card)
        {
            var menu = new ContextMenu();

            var editItem = new MenuItem { Header = _statisticsEditMode ? "Düzenlemeyi Bitir" : "Düzenle" };
            editItem.Click += (s, e) => StatisticEditMenuItem_Click();
            menu.Items.Add(editItem);

            var deleteItem = new MenuItem { Header = "Sil" };
            deleteItem.Click += (s, e) => StatisticDeleteMenuItem_Click(card);
            menu.Items.Add(deleteItem);

            return menu;
        }

        /// <summary>
        /// "Düzenle": ayrı bir pencere açmak yerine düzenleme modunu açar/kapatır.
        /// Mod aktifken tüm kutuların çerçevesi mavi olur ve kutular sürüklenip
        /// başka bir kutunun üzerine bırakılarak yer değiştirebilir.
        /// </summary>
        private void StatisticEditMenuItem_Click()
        {
            _statisticsEditMode = !_statisticsEditMode;
            LoadHomeStatistics();
        }

        /// <summary>"Sil": kutuyu kaldırır. Karttaki "x" düğmesiyle aynı işi yapar.</summary>
        private void StatisticDeleteMenuItem_Click(HomeStatisticCard card)
        {
            HomeStatisticsRepository.Remove(card.Id);
            LoadHomeStatistics();
        }

        private void AddStatisticButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new AddStatisticWindow { Owner = this };
            var result = window.ShowDialog();

            if (result == true)
            {
                LoadHomeStatistics();
            }
        }

        // ---------- İSTATİSTİK KARTLARINI SÜRÜKLEYİP BIRAKMA ----------

        private void StatisticCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_statisticsEditMode)
            {
                return;
            }

            _dragStartPoint = e.GetPosition(null);

            // Basılı tutulan kutunun çerçevesi hemen renk değiştirsin.
            var border = sender as Border;
            if (border != null)
            {
                border.BorderBrush = StatisticCardHeldBrush;
            }
        }

        private void StatisticCard_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Sürükleme başlamadan bırakılırsa çerçeveyi düzenleme modu rengine geri al.
            var border = sender as Border;
            if (border != null && _statisticsEditMode)
            {
                border.BorderBrush = StatisticCardEditModeBrush;
            }
        }

        private void StatisticCard_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_statisticsEditMode || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var position = e.GetPosition(null);

            if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            var border = sender as Border;
            var card = border == null ? null : border.Tag as HomeStatisticCard;
            if (card == null)
            {
                return;
            }

            DragDrop.DoDragDrop(border, card, DragDropEffects.Move);

            // Bırakma bir kutunun üzerine olmadıysa (LoadHomeStatistics çağrılmadıysa)
            // çerçeveyi düzenleme modu rengine geri al.
            border.BorderBrush = StatisticCardEditModeBrush;
        }

        /// <summary>
        /// Bir kart başka bir kartın üzerine bırakılınca, sürüklenen kartı bırakıldığı
        /// kartın yerine taşır ve yeni sırayı kaydeder.
        /// </summary>
        private void StatisticCard_Drop(object sender, DragEventArgs e)
        {
            var targetBorder = sender as Border;
            var targetCard = targetBorder == null ? null : targetBorder.Tag as HomeStatisticCard;
            var draggedCard = e.Data.GetData(typeof(HomeStatisticCard)) as HomeStatisticCard;

            if (targetCard == null || draggedCard == null || targetCard.Id == draggedCard.Id)
            {
                return;
            }

            var newOrder = _homeStatisticCards.Select(c => c.Id).ToList();
            newOrder.Remove(draggedCard.Id);
            newOrder.Insert(newOrder.IndexOf(targetCard.Id), draggedCard.Id);

            HomeStatisticsRepository.Reorder(newOrder);
            LoadHomeStatistics();
        }

        // ---------- ÜRÜN CİNSİ SAYFASI ----------

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
            CloseHomeSearch();

            _currentType = type;
            TypePageTitle.Text = type.Name;
            TypeSearchBox.Text = "";

            // Eski arama ve filtreler temizlenir; filtre başlıkları yine kapalı başlar.
            _selected.Clear();
            _openFilterIds.Clear();
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
            table.Columns.Add(QuantityColumnName, typeof(int));

            foreach (var property in properties)
            {
                table.Columns.Add(property.Name, typeof(string));
                table.Columns.Add(ColorColumnPrefix + property.Id, typeof(string));
            }

            // Bu cinse ait gerçek ürünleri ve değerlerini tabloya satır olarak ekle.
            // Her satırda gizli __ProductId sütunu tutulur; çift tıklama ve silme
            // işlemleri artık satır sırasına değil, doğrudan bu Id'ye bakar. Böylece
            // bir filtre uygulanmışken bile yanlış ürün açılmaz/silinmez.
            var products = ProductRepository.GetForType(type.Id);
            _currentProducts = products;

            var zimmetSummary = AssignmentRepository.GetActiveSummaryByProduct();
            var cellColors = ColorRepository.GetCellColors(type.Id);

            var rowNumber = 0;
            foreach (var product in products)
            {
                rowNumber++;
                var values = ProductRepository.GetValues(product.Id);

                var row = table.NewRow();
                row[NoColumnName] = rowNumber;
                row[SelectedColumnName] = false;
                row[IdColumnName] = product.Id;
                row[QuantityColumnName] = product.Quantity;

                foreach (var property in properties)
                {
                    if (PropertyDefinitionRepository.IsZimmet(property))
                    {
                        string zimmetText;
                        row[property.Name] = zimmetSummary.TryGetValue(product.Id, out zimmetText) ? zimmetText : "";
                    }
                    else
                    {
                        row[property.Name] = values.ContainsKey(property.Id) ? values[property.Id] : "";
                    }
                }
                Dictionary<int, string> productColors;
                cellColors.TryGetValue(product.Id, out productColors);
                foreach (var property in properties)
                {
                    string colorKey = null;
                    var choice = productColors != null && productColors.TryGetValue(property.Id, out colorKey)
                        ? ColorRepository.FindChoice(colorKey)
                        : null;
                    row[ColorColumnPrefix + property.Id] = choice == null ? "" : choice.Hex;
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

            ProductGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Adet",
                Binding = new Binding(QuantityColumnName),
                Width = new DataGridLength(60),
                IsReadOnly = true
            });

            foreach (var property in properties)
            {
                ProductGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = property.Name,
                    Binding = new Binding(property.Name),
                    Width = new DataGridLength(140),
                    IsReadOnly = true,
                    CellStyle = BuildColoredCellStyle(property.Id)
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
                MessageBox.Show("Önce silmek istediğin ürünlerin kutucuğunu işaretle.", "Depo Takip",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DeleteProducts(toDelete);
        }

        /// <summary>
        /// Verilen ürünleri siler (onay sorar). Zimmetteki ürünler silinmez, atlanır.
        /// Hem soldaki toplu "Sil" düğmesi hem de sağ tık menüsü bunu kullanır.
        /// </summary>
        private void DeleteProducts(List<int> toDelete)
        {
            if (_currentType == null || toDelete == null || toDelete.Count == 0)
            {
                return;
            }

            // Zimmetteki ürünler silinemez: önce zimmet iade alınmalı.
            var assignedToSkip = toDelete.Where(id => AssignmentRepository.GetActiveQuantity(id) > 0).ToList();
            if (assignedToSkip.Count > 0)
            {
                MessageBox.Show(
                    assignedToSkip.Count + " ürün zimmette olduğu için silinemez, atlanacak. " +
                    "Önce Zimmetler ekranından iade alın.",
                    "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Information);

                toDelete = toDelete.Where(id => !assignedToSkip.Contains(id)).ToList();

                if (toDelete.Count == 0)
                {
                    LoadProductGrid(_currentType);
                    return;
                }
            }

            var result = MessageBox.Show(
                "Seçili " + toDelete.Count + " ürün silinecek, onaylıyor musun?",
                "Depo Takip", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            // Geri Al için silinmeden önce ürünlerin tam kopyası alınır.
            var deletedSnapshots = CaptureSnapshots(_currentType.Id, toDelete);

            foreach (var productId in toDelete)
            {
                var description = BuildProductDescription(
                    ProductRepository.GetValues(productId), ProductRepository.GetQuantity(productId));

                ProductRepository.Delete(productId);
                LogRepository.Add(_currentType.Name, description, "Silindi");
            }

            if (deletedSnapshots.Count > 0)
            {
                PushUndo(BuildProductSetEntry(
                    "Silme (" + deletedSnapshots.Count + " ürün)", _currentType.Id, _currentType.Name,
                    deletedSnapshots, _currentProperties.Where(p => p.IsSerialNumber).Select(p => p.Id).ToList(),
                    true));
            }

            LoadProductGrid(_currentType);
        }

        // ---------- SAĞ TIK MENÜSÜ ----------

        /// <summary>
        /// Ürün satırına sağ tıklanınca menüyü hazırlar. İşaretli (onay kutulu) birden fazla ürün varsa
        /// ve sağ tıklanan satır da işaretliyse menü işaretli hepsi için, değilse sadece o satır için çalışır.
        /// </summary>
        private void ProductGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            ProductGrid.ContextMenu = null;

            if (_currentType == null)
            {
                return;
            }

            DataGridRow row = null;
            var element = e.OriginalSource as DependencyObject;

            while (element != null)
            {
                row = element as DataGridRow;
                if (row != null)
                {
                    break;
                }

                element = VisualTreeHelper.GetParent(element);
            }

            var rowView = row == null ? null : row.Item as DataRowView;
            if (rowView == null)
            {
                return;
            }

            var checkedIds = new List<int>();
            var view = ProductGrid.ItemsSource as DataView;
            if (view != null)
            {
                foreach (DataRowView candidate in view)
                {
                    if ((bool)candidate[SelectedColumnName])
                    {
                        checkedIds.Add((int)candidate[IdColumnName]);
                    }
                }
            }

            List<int> targets;
            if ((bool)rowView[SelectedColumnName] && checkedIds.Count > 1)
            {
                targets = checkedIds;
            }
            else
            {
                targets = new List<int> { (int)rowView[IdColumnName] };
                ProductGrid.SelectedItem = rowView;
            }

            // Menü yukarıdan aşağıya bu sırayla kurulur:
            // Düzenle, Zimmetle, Teslim-Tesellüm | Kopyala, Kes, Yapıştır, Renk | Geri Al, İleri Al | Sil
            var source = e.OriginalSource as DependencyObject;
            var menu = BuildProductContextMenu(targets);
            AddClipboardMenu(menu, rowView, targets, source);
            AddColorMenu(menu, rowView, source);
            AddHistoryMenu(menu);
            AddDeleteMenu(menu, targets);
            ProductGrid.ContextMenu = menu;
        }

        private ContextMenu BuildProductContextMenu(List<int> productIds)
        {
            var menu = new ContextMenu();
            var multiple = productIds.Count > 1;
            var suffix = multiple ? " (" + productIds.Count + " ürün)" : "";

            if (!multiple)
            {
                var edit = new MenuItem { Header = "Düzenle" };
                edit.Click += (s, e) => EditProduct(productIds[0]);
                menu.Items.Add(edit);
            }

            // Zimmetle, her ürün cinsinde ve hem tek hem çoklu seçimde görünür.
            var assign = new MenuItem { Header = "Zimmetle" + suffix };
            assign.Click += (s, e) => AssignProducts(productIds);
            menu.Items.Add(assign);

            var handover = new MenuItem { Header = "Teslim-Tesellüm oluştur" + suffix };
            handover.Click += (s, e) => CreateHandoverFor(productIds);
            menu.Items.Add(handover);

            menu.Items.Add(new Separator());

            return menu;
        }

        /// <summary>Menünün en altına, ayırıcıdan sonra "Sil" ekler (tehlikeli işlem hep en sonda durur).</summary>
        private void AddDeleteMenu(ContextMenu menu, List<int> productIds)
        {
            var suffix = productIds.Count > 1 ? " (" + productIds.Count + " ürün)" : "";

            menu.Items.Add(new Separator());

            var delete = new MenuItem { Header = "Sil" + suffix };
            delete.Click += (s, e) => DeleteProducts(productIds);
            menu.Items.Add(delete);
        }

        // ---------- KES / KOPYALA / YAPIŞTIR ----------

        /// <summary>Sütun başlığından özelliği bulur. Zimmet sütunu (otomatik hesaplanır) hariç tutulur.</summary>
        private PropertyDefinition FindEditableProperty(string header)
        {
            var property = _currentProperties.FirstOrDefault(p => p.Name == header);
            if (property == null || PropertyDefinitionRepository.IsZimmet(property))
            {
                return null;
            }

            return property;
        }

        /// <summary>Sağ tıklanan yerin hangi özellik sütununda olduğunu bulur (yoksa null).</summary>
        private PropertyDefinition FindClickedProperty(DependencyObject source)
        {
            DataGridCell cell = null;
            var element = source;

            while (element != null)
            {
                cell = element as DataGridCell;
                if (cell != null)
                {
                    break;
                }

                element = VisualTreeHelper.GetParent(element);
            }

            if (cell == null || cell.Column == null)
            {
                return null;
            }

            return FindEditableProperty(cell.Column.Header as string);
        }

        private static bool ClipboardHasText()
        {
            try
            {
                return Clipboard.ContainsText();
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Menünün en üstüne üç grup ekler: Kopyala, Kes, Yapıştır. Her grubun üzerine gelince
        /// yanında "Hücre ..." ve "Satır ..." seçenekleri açılır.
        /// </summary>
        private void AddClipboardMenu(ContextMenu menu, DataRowView rowView, List<int> targets, DependencyObject source)
        {
            var productId = (int)rowView[IdColumnName];
            var suffix = targets.Count > 1 ? " (" + targets.Count + " satır)" : "";

            // Tıklanan yer bir özellik sütunu değilse (ör. Zimmet) hücre seçenekleri soluk kalır.
            var property = FindClickedProperty(source);
            var hasCell = property != null;

            // KOPYALA
            var copyCell = new MenuItem { Header = "Hücre Kopyala", InputGestureText = "Ctrl+C", IsEnabled = hasCell };
            copyCell.Click += (s, e) => CopyCell(rowView, property, false);

            var copyRows = new MenuItem { Header = "Satır Kopyala" + suffix };
            copyRows.Click += (s, e) => CopyRows(targets, false);

            var copyMenu = new MenuItem { Header = "Kopyala" };
            copyMenu.Items.Add(copyCell);
            copyMenu.Items.Add(copyRows);

            // KES
            var cutCell = new MenuItem { Header = "Hücre Kes", InputGestureText = "Ctrl+X", IsEnabled = hasCell };
            cutCell.Click += (s, e) => CopyCell(rowView, property, true);

            var cutRows = new MenuItem { Header = "Satır Kes" + suffix };
            cutRows.Click += (s, e) => CopyRows(targets, true);

            var cutMenu = new MenuItem { Header = "Kes" };
            cutMenu.Items.Add(cutCell);
            cutMenu.Items.Add(cutRows);

            // YAPIŞTIR
            var pasteCell = new MenuItem
            {
                Header = "Hücre Yapıştır",
                InputGestureText = "Ctrl+V",
                IsEnabled = hasCell && ClipboardHasText()
            };
            pasteCell.Click += (s, e) => PasteCell(rowView, property);

            var waiting = _rowClipboardIds.Count;
            var pasteRows = new MenuItem
            {
                Header = waiting > 1 ? "Satır Yapıştır (" + waiting + " satır)" : "Satır Yapıştır",
                IsEnabled = waiting > 0
            };
            pasteRows.Click += (s, e) => PasteRows(productId);

            var pasteMenu = new MenuItem
            {
                Header = "Yapıştır",
                IsEnabled = pasteCell.IsEnabled || pasteRows.IsEnabled
            };
            pasteMenu.Items.Add(pasteCell);
            pasteMenu.Items.Add(pasteRows);

            menu.Items.Add(copyMenu);
            menu.Items.Add(cutMenu);
            menu.Items.Add(pasteMenu);
        }

        /// <summary>Ctrl+C / Ctrl+X / Ctrl+V: tıklanmış hücre bir özellik sütunundaysa hücre için çalışır.</summary>
        private void ProductGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_currentType == null || (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            {
                return;
            }

            if (e.Key != Key.C && e.Key != Key.X && e.Key != Key.V)
            {
                return;
            }

            var rowView = ProductGrid.CurrentCell.Item as DataRowView;
            var column = ProductGrid.CurrentCell.Column;
            if (rowView == null || column == null)
            {
                return;
            }

            // Özellik sütunu değilse (onay kutusu, Sıra No, Adet, Zimmet) normal davranış kalır.
            var property = FindEditableProperty(column.Header as string);
            if (property == null)
            {
                return;
            }

            e.Handled = true;

            if (e.Key == Key.C)
            {
                CopyCell(rowView, property, false);
            }
            else if (e.Key == Key.X)
            {
                CopyCell(rowView, property, true);
            }
            else if (ClipboardHasText())
            {
                PasteCell(rowView, property);
            }
        }

        /// <summary>Hücredeki yazıyı panoya alır. Kes ise hücreyi boşaltır.</summary>
        private void CopyCell(DataRowView rowView, PropertyDefinition property, bool cut)
        {
            var oldValue = rowView[property.Name] as string ?? "";

            try
            {
                Clipboard.SetText(oldValue);
            }
            catch (Exception)
            {
                MessageBox.Show("Panoya yazılamadı, tekrar dene.", "Depo Takip",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (cut)
            {
                var emptyValue = property.DataType == DynamicFieldFactory.YesNoDataType ? "Hayır" : "";
                WriteCellValue(rowView, property, emptyValue);
            }
        }

        /// <summary>Panodaki yazıyı hücreye yazar (Seri No ve Evet/Hayır kuralları kontrol edilir).</summary>
        private void PasteCell(DataRowView rowView, PropertyDefinition property)
        {
            string text;

            try
            {
                if (!Clipboard.ContainsText())
                {
                    return;
                }

                text = Clipboard.GetText() ?? "";
            }
            catch (Exception)
            {
                MessageBox.Show("Panodan okunamadı, tekrar dene.", "Depo Takip",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Excel'den kopyalanan tek hücrenin sonunda satır sonu olur, temizlenir.
            text = text.Trim();

            if (text.Contains("\n") || text.Contains("\t"))
            {
                MessageBox.Show("Birden fazla hücre kopyalanmış. Bir hücreye sadece tek hücre yapıştırılabilir.",
                    "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (property.DataType == DynamicFieldFactory.YesNoDataType)
            {
                if (string.Equals(text, "Evet", StringComparison.CurrentCultureIgnoreCase))
                {
                    text = "Evet";
                }
                else if (string.Equals(text, "Hayır", StringComparison.CurrentCultureIgnoreCase)
                    || string.Equals(text, "Hayir", StringComparison.CurrentCultureIgnoreCase))
                {
                    text = "Hayır";
                }
                else
                {
                    MessageBox.Show("Bu sütuna sadece \"Evet\" veya \"Hayır\" yapıştırılabilir.",
                        "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var productId = (int)rowView[IdColumnName];
            var oldValue = rowView[property.Name] as string ?? "";

            if (text == oldValue)
            {
                return;
            }

            if (property.IsSerialNumber && text != "")
            {
                if (ProductRepository.IsValueUsedByAnotherProduct(property.Id, text, productId))
                {
                    MessageBox.Show(
                        "\"" + property.Name + "\" için \"" + text + "\" değeri zaten başka bir üründe kullanılıyor.",
                        "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (ScrapRepository.IsSerialNumberUsed(text))
                {
                    MessageBox.Show(
                        "\"" + property.Name + "\" için \"" + text + "\" değeri hurdadaki bir üründe kullanılıyor.",
                        "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            WriteCellValue(rowView, property, text);
        }

        /// <summary>Hücre değerini veritabanına ve tabloya yazar, Log'a "Güncellendi" ekler.</summary>
        private void WriteCellValue(DataRowView rowView, PropertyDefinition property, string newValue)
        {
            var productId = (int)rowView[IdColumnName];
            var oldValue = rowView[property.Name] as string ?? "";

            if (oldValue == newValue)
            {
                return;
            }

            ProductRepository.SetValue(productId, property.Id, newValue);
            rowView[property.Name] = newValue;

            PushUndo(BuildEditEntry(productId,
                new List<ValueChange>
                {
                    new ValueChange
                    {
                        PropertyId = property.Id,
                        Name = property.Name,
                        IsSerial = property.IsSerialNumber,
                        Old = oldValue,
                        New = newValue
                    }
                }, 0, 0));

            var oldText = oldValue == "" ? "(boş)" : oldValue;
            var newText = newValue == "" ? "(boş)" : newValue;
            LogRepository.Add(_currentType.Name, property.Name + ": " + oldText + " -> " + newText, "Güncellendi");
        }

        /// <summary>
        /// Satırları panoya alır. Kes ise zimmetteki ürünler atlanır (zimmetteki ürün kesilemez,
        /// sadece kopyalanabilir).
        /// </summary>
        private void CopyRows(List<int> productIds, bool cut)
        {
            var ids = productIds.ToList();

            if (cut)
            {
                var assigned = ids.Where(id => AssignmentRepository.GetActiveQuantity(id) > 0).ToList();
                if (assigned.Count > 0)
                {
                    MessageBox.Show(
                        assigned.Count == 1
                            ? "Bu ürün zimmette olduğu için kesilemez. İstersen kopyalayabilirsin."
                            : assigned.Count + " ürün zimmette olduğu için kesilemez, atlanacak.",
                        "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Information);

                    ids = ids.Where(id => !assigned.Contains(id)).ToList();
                    if (ids.Count == 0)
                    {
                        return;
                    }
                }
            }

            _rowClipboardIds = ids;
            _rowClipboardIsCut = cut;
        }

        /// <summary>
        /// Panodaki satırları, sağ tıklanan satırın hemen altına yapıştırır.
        /// Kesilmişse aynı ürünler taşınır; kopyalanmışsa yeni ürün olarak eklenir (Seri No boş kalır).
        /// </summary>
        private void PasteRows(int targetProductId)
        {
            if (_currentType == null || _rowClipboardIds.Count == 0)
            {
                return;
            }

            var typeId = _currentType.Id;
            var existingIds = ProductRepository.GetForType(typeId).Select(p => p.Id).ToList();
            var sources = _rowClipboardIds.Where(id => existingIds.Contains(id)).ToList();

            if (sources.Count == 0)
            {
                MessageBox.Show(
                    "Kopyalanan satır bu ürün grubunda değil. Şimdilik satırlar sadece aynı grubun içinde yapıştırılabilir.",
                    "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var after = targetProductId;

            if (_rowClipboardIsCut)
            {
                var orderBefore = existingIds;
                var movedIds = new List<int>();
                var movedIndexes = new List<int>();

                foreach (var id in sources)
                {
                    if (id == targetProductId || AssignmentRepository.GetActiveQuantity(id) > 0)
                    {
                        continue;
                    }

                    movedIndexes.Add(orderBefore.IndexOf(id));
                    MoveAfter(id, typeId, after);
                    after = id;
                    movedIds.Add(id);
                }

                if (movedIds.Count > 0)
                {
                    LogRepository.Add(_currentType.Name, movedIds.Count + " satır", "Taşındı");
                    PushUndo(BuildMoveEntry(typeId, _currentType.Name, movedIds, movedIndexes, targetProductId));
                }

                _rowClipboardIds = new List<int>();
            }
            else
            {
                var colors = ColorRepository.GetCellColors(typeId);
                var serialIds = _currentProperties.Where(p => p.IsSerialNumber).Select(p => p.Id).ToList();

                var newIds = new List<int>();

                foreach (var sourceId in sources)
                {
                    var quantity = System.Math.Max(1, ProductRepository.GetQuantity(sourceId));
                    var newId = ProductRepository.Add(typeId, quantity);
                    var values = ProductRepository.GetValues(sourceId);

                    foreach (var property in _currentProperties)
                    {
                        if (PropertyDefinitionRepository.IsZimmet(property) || property.IsSerialNumber)
                        {
                            continue;
                        }

                        string value;
                        if (values.TryGetValue(property.Id, out value))
                        {
                            ProductRepository.SetValue(newId, property.Id, value);
                        }
                    }

                    Dictionary<int, string> sourceColors;
                    if (colors.TryGetValue(sourceId, out sourceColors))
                    {
                        foreach (var pair in sourceColors)
                        {
                            if (!serialIds.Contains(pair.Key))
                            {
                                ColorRepository.SetCellColor(newId, pair.Key, pair.Value);
                            }
                        }
                    }

                    MoveAfter(newId, typeId, after);
                    after = newId;
                    newIds.Add(newId);

                    LogRepository.Add(_currentType.Name,
                        BuildProductDescription(ProductRepository.GetValues(newId), quantity), "Eklendi");
                }

                var addedSnapshots = CaptureSnapshots(typeId, newIds);
                if (addedSnapshots.Count > 0)
                {
                    PushUndo(BuildProductSetEntry(
                        "Satır kopyalama (" + addedSnapshots.Count + " satır)", typeId, _currentType.Name,
                        addedSnapshots, serialIds, false));
                }
            }

            LoadProductGrid(_currentType);
        }

        /// <summary>Ürünü, aynı gruptaki başka bir ürünün hemen altına yerleştirir.</summary>
        private static void MoveAfter(int productId, int typeId, int afterProductId)
        {
            var ids = ProductRepository.GetForType(typeId).Select(p => p.Id).Where(id => id != productId).ToList();
            var index = ids.IndexOf(afterProductId);
            ProductRepository.SetPosition(productId, typeId, index < 0 ? ids.Count + 1 : index + 2);
        }

        // ---------- GERİ AL / İLERİ AL ----------

        private const int UndoLimit = 20;

        private sealed class UndoEntry
        {
            public string Title;
            public int TypeId;
            public string TypeName;

            // Başarılıysa null, yapılamadıysa nedenini yazan metin döner.
            public Func<string> Undo;
            public Func<string> Redo;
        }

        private sealed class ValueChange
        {
            public int PropertyId;
            public string Name;
            public bool IsSerial;
            public string Old;
            public string New;
        }

        private readonly List<UndoEntry> _undoStack = new List<UndoEntry>();
        private readonly List<UndoEntry> _redoStack = new List<UndoEntry>();

        private void PushUndo(UndoEntry entry)
        {
            _undoStack.Add(entry);
            if (_undoStack.Count > UndoLimit)
            {
                _undoStack.RemoveAt(0);
            }

            _redoStack.Clear();
            UpdateUndoMenu();
        }

        /// <summary>Yedekten geri yükleme ve sıfırlama gibi her şeyi değiştiren işlemlerden sonra çağrılır.</summary>
        private void ClearUndoHistory()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            UpdateUndoMenu();
        }

        private void UpdateUndoMenu()
        {
            UndoMenuItem.IsEnabled = _undoStack.Count > 0;
            UndoMenuItem.ToolTip = _undoStack.Count > 0
                ? "Geri Al: " + _undoStack[_undoStack.Count - 1].Title + " (Ctrl+Z)"
                : "Geri alınacak işlem yok";

            RedoMenuItem.IsEnabled = _redoStack.Count > 0;
            RedoMenuItem.ToolTip = _redoStack.Count > 0
                ? "İleri Al: " + _redoStack[_redoStack.Count - 1].Title + " (Ctrl+Y)"
                : "İleri alınacak işlem yok";
        }

        private void UndoMenu_Click(object sender, RoutedEventArgs e)
        {
            DoUndo();
        }

        private void RedoMenu_Click(object sender, RoutedEventArgs e)
        {
            DoRedo();
        }

        private void DoUndo()
        {
            if (_undoStack.Count == 0)
            {
                return;
            }

            var entry = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);

            if (RunHistoryStep(entry, entry.Undo, "Geri alındı", "geri alınamadı"))
            {
                _redoStack.Add(entry);
            }

            UpdateUndoMenu();
        }

        private void DoRedo()
        {
            if (_redoStack.Count == 0)
            {
                return;
            }

            var entry = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);

            if (RunHistoryStep(entry, entry.Redo, "İleri alındı", "ileri alınamadı"))
            {
                _undoStack.Add(entry);
            }

            UpdateUndoMenu();
        }

        private bool RunHistoryStep(UndoEntry entry, Func<string> action, string logType, string failText)
        {
            string problem;

            try
            {
                problem = action();
            }
            catch (Exception)
            {
                problem = "Ürün ya da bilgiler arada değişmiş.";
            }

            if (problem != null)
            {
                MessageBox.Show(problem + "\n\n\"" + entry.Title + "\" işlemi " + failText + " ve listeden çıkarıldı.",
                    "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshAfterHistory(entry);
                return false;
            }

            LogRepository.Add(entry.TypeName, entry.Title, logType);
            RefreshAfterHistory(entry);
            return true;
        }

        /// <summary>Geri/ileri alınan işlemin olduğu ürün grubunu açıp tabloyu yeniler.</summary>
        private void RefreshAfterHistory(UndoEntry entry)
        {
            if (TypePage.Visibility == Visibility.Visible && _currentType != null)
            {
                if (entry.TypeId != 0 && entry.TypeId != _currentType.Id)
                {
                    var type = ProductTypeRepository.GetById(entry.TypeId);
                    if (type != null)
                    {
                        ShowTypePage(type);
                        return;
                    }
                }

                LoadProductGrid(_currentType);
            }
            else
            {
                LoadHomeStatistics();
                LoadRecentLog();
            }
        }

        /// <summary>Ctrl+Z geri al, Ctrl+Y (ya da Ctrl+Shift+Z) ileri al. Yazı kutularında kendi geri almaları çalışır.</summary>
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            {
                return;
            }

            if (Keyboard.FocusedElement is TextBox)
            {
                return;
            }

            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            if (e.Key == Key.Z && !shift)
            {
                DoUndo();
                e.Handled = true;
            }
            else if (e.Key == Key.Y || (e.Key == Key.Z && shift))
            {
                DoRedo();
                e.Handled = true;
            }
        }

        /// <summary>Sağ tık menüsüne (ayırıcıdan sonra) Geri Al / İleri Al ekler.</summary>
        private void AddHistoryMenu(ContextMenu menu)
        {
            var hasUndo = _undoStack.Count > 0;
            var undo = new MenuItem
            {
                Header = hasUndo ? "Geri Al: " + _undoStack[_undoStack.Count - 1].Title : "Geri Al",
                InputGestureText = "Ctrl+Z",
                IsEnabled = hasUndo
            };
            undo.Click += (s, e) => DoUndo();

            var hasRedo = _redoStack.Count > 0;
            var redo = new MenuItem
            {
                Header = hasRedo ? "İleri Al: " + _redoStack[_redoStack.Count - 1].Title : "İleri Al",
                InputGestureText = "Ctrl+Y",
                IsEnabled = hasRedo
            };
            redo.Click += (s, e) => DoRedo();

            menu.Items.Add(new Separator());
            menu.Items.Add(undo);
            menu.Items.Add(redo);
        }

        // --- Hücre / ürün düzenleme ---

        private UndoEntry BuildEditEntry(int productId, List<ValueChange> changes, int oldQuantity, int newQuantity)
        {
            var title = changes.Count == 1 && oldQuantity == 0
                ? "Hücre değişikliği (" + changes[0].Name + ")"
                : "Ürün düzenleme";

            return new UndoEntry
            {
                Title = title,
                TypeId = _currentType.Id,
                TypeName = _currentType.Name,
                Undo = () => ApplyEdit(productId, changes, oldQuantity, false),
                Redo = () => ApplyEdit(productId, changes, newQuantity, true)
            };
        }

        /// <summary>Değişiklikleri eski ya da yeni haline getirir. targetQuantity 0 ise adede dokunulmaz.</summary>
        private static string ApplyEdit(int productId, List<ValueChange> changes, int targetQuantity, bool useNew)
        {
            var currentQuantity = ProductRepository.GetQuantity(productId);
            if (currentQuantity == 0)
            {
                return "Bu ürün artık yok.";
            }

            var current = ProductRepository.GetValues(productId);

            foreach (var change in changes)
            {
                var target = useNew ? change.New : change.Old;

                string now;
                current.TryGetValue(change.PropertyId, out now);
                now = now ?? "";

                if (change.IsSerial && target != "" && target != now)
                {
                    if (ProductRepository.IsValueUsedByAnotherProduct(change.PropertyId, target, productId))
                    {
                        return "\"" + target + "\" değeri şu an başka bir üründe kullanılıyor.";
                    }

                    if (ScrapRepository.IsSerialNumberUsed(target))
                    {
                        return "\"" + target + "\" değeri şu an hurdadaki bir üründe kullanılıyor.";
                    }
                }
            }

            if (targetQuantity > 0 && targetQuantity != currentQuantity
                && targetQuantity < AssignmentRepository.GetActiveQuantity(productId))
            {
                return "Bu üründen zimmette adet olduğu için adet bu kadar düşürülemez.";
            }

            foreach (var change in changes)
            {
                ProductRepository.SetValue(productId, change.PropertyId, useNew ? change.New : change.Old);
            }

            if (targetQuantity > 0 && targetQuantity != currentQuantity)
            {
                ProductRepository.SetQuantity(productId, targetQuantity);
            }

            return null;
        }

        // --- Renk ---

        private UndoEntry BuildColorEntry(int productId, int propertyId, string propertyName, string oldKey, string newKey)
        {
            return new UndoEntry
            {
                Title = "Renk (" + propertyName + ")",
                TypeId = _currentType.Id,
                TypeName = _currentType.Name,
                Undo = () => ApplyColor(productId, propertyId, oldKey),
                Redo = () => ApplyColor(productId, propertyId, newKey)
            };
        }

        private static string ApplyColor(int productId, int propertyId, string colorKey)
        {
            if (ProductRepository.GetQuantity(productId) == 0)
            {
                return "Bu ürün artık yok.";
            }

            if (string.IsNullOrEmpty(colorKey))
            {
                ColorRepository.ClearCellColor(productId, propertyId);
            }
            else
            {
                ColorRepository.SetCellColor(productId, propertyId, colorKey);
            }

            return null;
        }

        // --- Ürün silme ve satır kopyalama (ürün ekleme/çıkarma) ---

        /// <summary>Ürünlerin tam kopyasını alır (değerleri, renkleri, grup içindeki sıraları).</summary>
        private static List<ProductSnapshot> CaptureSnapshots(int typeId, List<int> productIds)
        {
            var order = ProductRepository.GetForType(typeId).Select(p => p.Id).ToList();
            var colors = ColorRepository.GetCellColors(typeId);
            var result = new List<ProductSnapshot>();

            foreach (var id in productIds)
            {
                var snapshot = ProductRepository.GetSnapshot(id);
                if (snapshot == null)
                {
                    continue;
                }

                snapshot.Rank = order.IndexOf(id) + 1;

                Dictionary<int, string> productColors;
                if (colors.TryGetValue(id, out productColors))
                {
                    snapshot.Colors = new Dictionary<int, string>(productColors);
                }

                result.Add(snapshot);
            }

            return result;
        }

        /// <summary>
        /// Ürünlerin eklenmesi ya da silinmesi için geri alma kaydı.
        /// removed = true: işlem ürünleri sildi (geri alınca geri gelirler).
        /// removed = false: işlem ürünleri ekledi (geri alınca silinirler).
        /// </summary>
        private UndoEntry BuildProductSetEntry(string title, int typeId, string typeName,
            List<ProductSnapshot> snapshots, List<int> serialPropertyIds, bool removed)
        {
            Func<string> restore = () => RestoreSnapshots(typeId, snapshots, serialPropertyIds);
            Func<string> remove = () => RemoveSnapshots(snapshots);

            return new UndoEntry
            {
                Title = title,
                TypeId = typeId,
                TypeName = typeName,
                Undo = removed ? restore : remove,
                Redo = removed ? remove : restore
            };
        }

        /// <summary>Ürünleri aynı Id, değer, renk ve sıralarıyla geri getirir.</summary>
        private static string RestoreSnapshots(int typeId, List<ProductSnapshot> snapshots, List<int> serialPropertyIds)
        {
            if (ProductTypeRepository.GetById(typeId) == null)
            {
                return "Ürün grubu artık yok.";
            }

            foreach (var snapshot in snapshots)
            {
                if (ProductRepository.GetQuantity(snapshot.Id) > 0)
                {
                    return "Ürün zaten yerinde duruyor.";
                }

                foreach (var serialId in serialPropertyIds)
                {
                    string value;
                    if (!snapshot.Values.TryGetValue(serialId, out value) || string.IsNullOrEmpty(value))
                    {
                        continue;
                    }

                    if (ProductRepository.IsValueUsedByAnotherProduct(serialId, value, snapshot.Id))
                    {
                        return "\"" + value + "\" seri numarası şu an başka bir üründe kullanılıyor.";
                    }

                    if (ScrapRepository.IsSerialNumberUsed(value))
                    {
                        return "\"" + value + "\" seri numarası şu an hurdadaki bir üründe kullanılıyor.";
                    }
                }
            }

            // Eski sıralarına, küçük sıradan büyüğe doğru yerleştirilir.
            foreach (var snapshot in snapshots.OrderBy(x => x.Rank).ToList())
            {
                ProductRepository.Restore(snapshot);

                foreach (var pair in snapshot.Colors)
                {
                    ColorRepository.SetCellColor(snapshot.Id, pair.Key, pair.Value);
                }
            }

            return null;
        }

        /// <summary>Ürünleri siler (zimmetteki ürün silinmez, hiçbirine dokunmadan uyarı döner).</summary>
        private static string RemoveSnapshots(List<ProductSnapshot> snapshots)
        {
            foreach (var snapshot in snapshots)
            {
                if (ProductRepository.GetQuantity(snapshot.Id) == 0)
                {
                    return "Ürünlerden biri artık yok.";
                }

                if (AssignmentRepository.GetActiveQuantity(snapshot.Id) > 0)
                {
                    return "Ürünlerden biri şu an zimmette, silinemez.";
                }
            }

            foreach (var snapshot in snapshots)
            {
                ProductRepository.Delete(snapshot.Id);
            }

            return null;
        }

        // --- Satır taşıma (kes + yapıştır) ---

        private UndoEntry BuildMoveEntry(int typeId, string typeName, List<int> movedIds, List<int> originalIndexes, int targetProductId)
        {
            return new UndoEntry
            {
                Title = "Satır taşıma (" + movedIds.Count + " satır)",
                TypeId = typeId,
                TypeName = typeName,
                Undo = () =>
                {
                    foreach (var id in movedIds)
                    {
                        if (ProductRepository.GetQuantity(id) == 0)
                        {
                            return "Taşınan ürünlerden biri artık yok.";
                        }
                    }

                    // Taşınanlar eski sıralarına, küçük sıradan büyüğe doğru geri konur.
                    var order = Enumerable.Range(0, movedIds.Count).OrderBy(i => originalIndexes[i]).ToList();
                    foreach (var i in order)
                    {
                        ProductRepository.SetPosition(movedIds[i], typeId, originalIndexes[i] + 1);
                    }

                    return null;
                },
                Redo = () =>
                {
                    if (ProductRepository.GetQuantity(targetProductId) == 0)
                    {
                        return "Yapıştırılan yerdeki ürün artık yok.";
                    }

                    foreach (var id in movedIds)
                    {
                        if (ProductRepository.GetQuantity(id) == 0)
                        {
                            return "Taşınan ürünlerden biri artık yok.";
                        }

                        if (AssignmentRepository.GetActiveQuantity(id) > 0)
                        {
                            return "Taşınan ürünlerden biri şu an zimmette, kesilemez.";
                        }
                    }

                    var after = targetProductId;
                    foreach (var id in movedIds)
                    {
                        MoveAfter(id, typeId, after);
                        after = id;
                    }

                    return null;
                }
            };
        }

        // ---------- HÜCRE RENKLERİ ----------

        /// <summary>
        /// Sağ tıklanan hücre bir özellik sütunundaysa menüye "Renk" alt menüsü ekler
        /// (12 renk + varsa "Rengi kaldır"). Renk sadece tıklanan hücreyi boyar.
        /// </summary>
        private void AddColorMenu(ContextMenu menu, DataRowView rowView, DependencyObject source)
        {
            DataGridCell cell = null;
            var element = source;

            while (element != null)
            {
                cell = element as DataGridCell;
                if (cell != null)
                {
                    break;
                }

                element = VisualTreeHelper.GetParent(element);
            }

            if (cell == null || cell.Column == null)
            {
                return;
            }

            var header = cell.Column.Header as string;
            var property = _currentProperties.FirstOrDefault(p => p.Name == header);
            if (property == null || PropertyDefinitionRepository.IsZimmet(property))
            {
                return;
            }

            var productId = (int)rowView[IdColumnName];
            var colorColumn = ColorColumnPrefix + property.Id;
            var currentHex = rowView[colorColumn] as string;
            var meanings = ColorRepository.GetAllMeanings();

            var colorMenu = new MenuItem { Header = "Renk" };

            foreach (var choice in ColorRepository.Palette)
            {
                var picked = choice;
                string meaning;
                meanings.TryGetValue(choice.Key, out meaning);

                var item = new MenuItem
                {
                    Header = string.IsNullOrWhiteSpace(meaning) ? choice.Name : choice.Name + " - " + meaning,
                    Icon = new Border
                    {
                        Width = 14,
                        Height = 14,
                        CornerRadius = new CornerRadius(2),
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(choice.Hex)),
                        BorderBrush = Brushes.Gray,
                        BorderThickness = new Thickness(1)
                    }
                };
                item.Click += (s, e) => ApplyCellColor(productId, property, rowView, picked);
                colorMenu.Items.Add(item);
            }

            if (!string.IsNullOrEmpty(currentHex))
            {
                colorMenu.Items.Add(new Separator());

                var clear = new MenuItem { Header = "Rengi kaldır" };
                clear.Click += (s, e) =>
                {
                    var oldChoice = ColorRepository.Palette.FirstOrDefault(c => c.Hex == currentHex);
                    ColorRepository.ClearCellColor(productId, property.Id);
                    rowView[colorColumn] = "";

                    if (oldChoice != null)
                    {
                        PushUndo(BuildColorEntry(productId, property.Id, property.Name, oldChoice.Key, null));
                    }
                };
                colorMenu.Items.Add(clear);
            }

            // Yapıştır'dan hemen sonra eklenir.
            menu.Items.Add(colorMenu);
        }

        /// <summary>
        /// Hücreyi boyar. Bu renk ilk kez kullanılıyorsa önce ne demek olduğunu sorar;
        /// kullanıcı iptal ederse hücre boyanmaz.
        /// </summary>
        private void ApplyCellColor(int productId, PropertyDefinition property, DataRowView rowView, ColorChoice choice)
        {
            if (ColorRepository.GetMeaning(choice.Key) == null)
            {
                var meaning = SimpleInputWindow.Ask(this, "Renk anlamı",
                    choice.Name + " hücre ne demek? (örn: bozuk, kayıp)");

                if (meaning == null)
                {
                    return;
                }

                ColorRepository.SetMeaning(choice.Key, meaning);
            }

            var previousHex = rowView[ColorColumnPrefix + property.Id] as string;
            var previousChoice = ColorRepository.Palette.FirstOrDefault(c => c.Hex == previousHex);

            ColorRepository.SetCellColor(productId, property.Id, choice.Key);
            rowView[ColorColumnPrefix + property.Id] = choice.Hex;

            if (previousChoice == null || previousChoice.Key != choice.Key)
            {
                PushUndo(BuildColorEntry(productId, property.Id, property.Name,
                    previousChoice == null ? null : previousChoice.Key, choice.Key));
            }
        }

        /// <summary>Hücrenin zemin ve yazı rengini gizli renk sütunundan okuyan stil.</summary>
        private static Style BuildColoredCellStyle(int propertyId)
        {
            var colorColumn = ColorColumnPrefix + propertyId;
            var style = new Style(typeof(DataGridCell));

            var background = new MultiBinding { Converter = ColorCellConverter.ForBackground };
            background.Bindings.Add(new Binding(colorColumn));
            background.Bindings.Add(new Binding("IsSelected") { RelativeSource = RelativeSource.Self });
            style.Setters.Add(new Setter(DataGridCell.BackgroundProperty, background));

            var foreground = new MultiBinding { Converter = ColorCellConverter.ForForeground };
            foreground.Bindings.Add(new Binding(colorColumn));
            foreground.Bindings.Add(new Binding("IsSelected") { RelativeSource = RelativeSource.Self });
            style.Setters.Add(new Setter(DataGridCell.ForegroundProperty, foreground));

            return style;
        }

        /// <summary>
        /// Renk kodunu (#RRGGBB) hücre zemin/yazı rengine çevirir. Hücre boyalı değilse
        /// hiçbir şey değiştirmez (normal görünüm ve seçim rengi aynen kalır).
        /// Seçili satırda renk biraz koyulaşır.
        /// </summary>
        private sealed class ColorCellConverter : IMultiValueConverter
        {
            public static readonly ColorCellConverter ForBackground = new ColorCellConverter(false);
            public static readonly ColorCellConverter ForForeground = new ColorCellConverter(true);

            private readonly bool _foreground;

            private ColorCellConverter(bool foreground)
            {
                _foreground = foreground;
            }

            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                var hex = values != null && values.Length > 0 ? values[0] as string : null;
                if (string.IsNullOrEmpty(hex))
                {
                    return DependencyProperty.UnsetValue;
                }

                var color = (Color)ColorConverter.ConvertFromString(hex);

                var selected = values.Length > 1 && values[1] is bool && (bool)values[1];
                if (selected)
                {
                    color = Color.FromRgb((byte)(color.R * 0.75), (byte)(color.G * 0.75), (byte)(color.B * 0.75));
                }

                if (_foreground)
                {
                    var brightness = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
                    return brightness < 140 ? Brushes.White : Brushes.Black;
                }

                return new SolidColorBrush(color);
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        private void EditProduct(int productId)
        {
            if (_currentType == null)
            {
                return;
            }

            var oldValues = ProductRepository.GetValues(productId);
            var oldQuantity = ProductRepository.GetQuantity(productId);

            var window = new ProductDetailWindow(_currentType, productId, true) { Owner = this };
            window.ShowDialog();

            // Pencere kapanınca neyin değiştiğine bakıp geri alma listesine ekler.
            var newValues = ProductRepository.GetValues(productId);
            var newQuantity = ProductRepository.GetQuantity(productId);
            var changes = new List<ValueChange>();

            foreach (var property in _currentProperties)
            {
                if (PropertyDefinitionRepository.IsZimmet(property))
                {
                    continue;
                }

                string before;
                string after;
                oldValues.TryGetValue(property.Id, out before);
                newValues.TryGetValue(property.Id, out after);
                before = before ?? "";
                after = after ?? "";

                if (before != after)
                {
                    changes.Add(new ValueChange
                    {
                        PropertyId = property.Id,
                        Name = property.Name,
                        IsSerial = property.IsSerialNumber,
                        Old = before,
                        New = after
                    });
                }
            }

            var quantityChanged = oldQuantity != newQuantity && oldQuantity > 0 && newQuantity > 0;
            if (changes.Count > 0 || quantityChanged)
            {
                PushUndo(BuildEditEntry(productId, changes,
                    quantityChanged ? oldQuantity : 0, quantityChanged ? newQuantity : 0));
            }

            LoadProductGrid(_currentType);
        }

        /// <summary>Ürünün Seri No ve Sistem İsmi değerlerini okur (yoksa boş metin).</summary>
        private void ReadIdentity(int productId, out string serialNo, out string systemName)
        {
            var values = ProductRepository.GetValues(productId);

            var serialProperty = _currentProperties.FirstOrDefault(p => p.IsSerialNumber);
            serialNo = serialProperty != null && values.ContainsKey(serialProperty.Id)
                ? values[serialProperty.Id]
                : "";

            var systemProperty = _currentProperties.FirstOrDefault(p => p.Name == "Sistem İsmi");
            systemName = systemProperty != null && values.ContainsKey(systemProperty.Id)
                ? values[systemProperty.Id]
                : "";
        }

        /// <summary>Teslim-tesellüm tutanağını, verilen her ürün için bir satır dolu olarak açar.</summary>
        private void CreateHandoverFor(List<int> productIds)
        {
            var items = new List<HandoverItem>();

            foreach (var productId in productIds)
            {
                string serialNo;
                string systemName;
                ReadIdentity(productId, out serialNo, out systemName);

                if (string.IsNullOrWhiteSpace(systemName))
                {
                    systemName = _currentType.Name;
                }

                items.Add(new HandoverItem
                {
                    SerialNo = serialNo,
                    ItemType = systemName,
                    Quantity = System.Math.Max(1, ProductRepository.GetQuantity(productId)).ToString()
                });
            }

            var window = new HandoverWindow(items) { Owner = this };
            window.ShowDialog();
        }

        /// <summary>
        /// Ürünleri zimmetler. Tek ürünse miktarı sorulur; birden fazlaysa hepsi tek kişiye,
        /// her birinin zimmetlenebilir miktarının tamamı kadar zimmetlenir. Tamamı zaten zimmette olanlar atlanır.
        /// </summary>
        private void AssignProducts(List<int> productIds)
        {
            var eligible = productIds.Where(id => AssignmentRepository.GetAvailableQuantity(id) > 0).ToList();
            var skipped = productIds.Count - eligible.Count;

            if (eligible.Count == 0)
            {
                MessageBox.Show(
                    productIds.Count == 1
                        ? "Bu ürünün tamamı zaten zimmette."
                        : "Seçili ürünlerin tamamı zaten zimmette.",
                    "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (skipped > 0)
            {
                MessageBox.Show(
                    skipped + " ürünün tamamı zaten zimmette olduğu için atlanacak.",
                    "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            AssignWindow window;

            if (eligible.Count == 1)
            {
                var productId = eligible[0];
                string serialNo;
                string systemName;
                ReadIdentity(productId, out serialNo, out systemName);

                window = new AssignWindow(
                    productId,
                    AssignmentRepository.DescribeProduct(_currentType.Name, systemName, serialNo),
                    AssignmentRepository.GetAvailableQuantity(productId));
            }
            else
            {
                window = new AssignWindow(eligible, eligible.Count + " ürün zimmetlenecek");
            }

            window.Owner = this;
            window.ShowDialog();

            LoadProductGrid(_currentType);
        }

        /// <summary>
        /// Log defterine yazılacak, ürünün o anki değerlerini anlatan sabit metni oluşturur:
        /// Seri No doluysa öne alınır, ardından dolu ilk birkaç özellik eklenir.
        /// </summary>
        private string BuildProductDescription(Dictionary<int, string> values, int quantity)
        {
            var parts = new List<string>();

            foreach (var property in _currentProperties.Where(p => p.IsSerialNumber))
            {
                if (values.ContainsKey(property.Id) && !string.IsNullOrEmpty(values[property.Id]))
                {
                    parts.Add(property.Name + ": " + values[property.Id]);
                }
            }

            foreach (var property in _currentProperties)
            {
                if (parts.Count >= 3)
                {
                    break;
                }

                if (property.IsSerialNumber)
                {
                    continue;
                }

                if (values.ContainsKey(property.Id) && !string.IsNullOrEmpty(values[property.Id]))
                {
                    parts.Add(property.Name + ": " + values[property.Id]);
                }
            }

            if (quantity > 1)
            {
                parts.Add("Adet: " + quantity);
            }

            return string.Join(" | ", parts);
        }

        private void MoveSelectedToScrapButton_Click(object sender, RoutedEventArgs e)
        {
            var view = ProductGrid.ItemsSource as DataView;
            if (view == null || _currentType == null)
            {
                return;
            }

            var toMove = new List<int>();

            foreach (DataRowView rowView in view)
            {
                var isSelected = (bool)rowView[SelectedColumnName];
                if (isSelected)
                {
                    toMove.Add((int)rowView[IdColumnName]);
                }
            }

            if (toMove.Count == 0)
            {
                MessageBox.Show("Önce hurdaya taşımak istediğin ürünlerin kutucuğunu işaretle.", "Depo Takip",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                "Seçili " + toMove.Count + " ürün hurdaya taşınacak, onaylıyor musun?\n\n" +
                "Hurdaya taşınan ürünler depodan kaldırılır ama silinmez; Hurda ekranından istediğin zaman geri getirebilirsin.",
                "Depo Takip", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            var skippedAssigned = 0;

            foreach (var productId in toMove)
            {
                // Zimmetteki adetler hurdaya taşınamaz.
                if (ProductRepository.GetQuantity(productId) - AssignmentRepository.GetActiveQuantity(productId) < 1)
                {
                    skippedAssigned++;
                    continue;
                }

                var moveQuantity = AskScrapQuantity(productId);
                if (moveQuantity == 0)
                {
                    // Kullanıcı bu ürün için vazgeçti; diğerlerine devam edilir.
                    continue;
                }

                try
                {
                    ScrapRepository.MoveToScrap(productId, _currentType, moveQuantity);
                }
                catch (InvalidOperationException ex)
                {
                    MessageBox.Show(ex.Message, "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            if (skippedAssigned > 0)
            {
                MessageBox.Show(
                    skippedAssigned + " ürün zimmette olduğu için hurdaya taşınmadı. " +
                    "Önce Zimmetler ekranından iade alın.",
                    "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            LoadProductGrid(_currentType);
        }

        /// <summary>
        /// Adedi 1'den fazla olan bir ürün hurdaya taşınırken kaç adedinin gideceğini sorar.
        /// Adet 1 ise sormaz ve 1 döner. Kullanıcı vazgeçerse 0 döner.
        /// </summary>
        private int AskScrapQuantity(int productId)
        {
            var totalQuantity = ProductRepository.GetQuantity(productId);
            var assignedQuantity = AssignmentRepository.GetActiveQuantity(productId);
            var available = totalQuantity - assignedQuantity;
            if (available <= 1)
            {
                return 1;
            }

            var description = BuildProductDescription(ProductRepository.GetValues(productId), 1);
            var prompt = (string.IsNullOrEmpty(description) ? "Ürün" : description) +
                         "\n\nBu üründen depoda " + totalQuantity + " adet var" +
                         (assignedQuantity > 0 ? " (" + assignedQuantity + " adedi zimmette, en fazla " + available + " adet taşınabilir)" : "") +
                         ". Kaç adedi hurdaya taşınsın?";

            while (true)
            {
                var answer = SimpleInputWindow.Ask(this, "Hurdaya taşınacak adet", prompt, available.ToString());
                if (answer == null)
                {
                    return 0;
                }

                int quantity;
                if (int.TryParse(answer, out quantity) && quantity >= 1 && quantity <= available)
                {
                    return quantity;
                }

                MessageBox.Show("1 ile " + available + " arasında bir sayı yaz.",
                    "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ---------- DIŞA AKTAR / İÇE AKTAR (CSV) ----------

        private void ExportCsvButton_Click(object sender, RoutedEventArgs e)
        {
            var view = ProductGrid.ItemsSource as DataView;
            if (view == null || _currentType == null)
            {
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Dışa Aktar",
                FileName = _currentType.Name + ".csv",
                Filter = "CSV dosyası (*.csv)|*.csv"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var headers = new List<string> { "Sıra No" };
            headers.AddRange(_currentProperties.Select(p => p.Name));
            headers.Add("Adet");

            var lines = new List<string> { string.Join(";", headers.Select(CsvEscape)) };

            foreach (DataRowView rowView in view)
            {
                var cells = new List<string> { rowView[NoColumnName].ToString() };
                cells.AddRange(_currentProperties.Select(p => (string)rowView[p.Name]));
                cells.Add(rowView[QuantityColumnName].ToString());

                lines.Add(string.Join(";", cells.Select(CsvEscape)));
            }

            File.WriteAllLines(dialog.FileName, lines, new System.Text.UTF8Encoding(true));

            LogRepository.Add(_currentType.Name, view.Count + " satır", "Dışa aktarıldı");

            MessageBox.Show(view.Count + " ürün dışa aktarıldı.", "Depo Takip",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Excel, CSV'deki uzun rakam dizilerini (IMEI, seri no, barkod...) 3,54581E+14 gibi bilimsel sayıya
        /// çevirir ve baştaki sıfırları siler. Bunu önlemek için böyle değerler ="..." biçiminde yazılır.
        /// </summary>
        private static string ProtectLongNumber(string value)
        {
            value = ExpandScientific(value);

            if (string.IsNullOrEmpty(value) || value.Length < 2)
            {
                return value;
            }

            foreach (var ch in value)
            {
                if (ch < '0' || ch > '9')
                {
                    return value;
                }
            }

            if (value.Length >= 12 || value[0] == '0')
            {
                return "=\"" + value + "\"";
            }

            return value;
        }

        /// <summary>İçe aktarırken ="123456789012345" biçimini tekrar düz değere çevirir.</summary>
        private static string UnwrapExcelText(string value)
        {
            if (value != null && value.Length >= 3 && value.StartsWith("=\"") && value.EndsWith("\""))
            {
                return value.Substring(2, value.Length - 3);
            }
            return ExpandScientific(value);
        }

        /// <summary>Bilimsel gösterime bozulmuş uzun sayıyı tam rakamlarına çevirir (Database.ExpandScientific).</summary>
        private static string ExpandScientific(string value)
        {
            return DEPO_DURUMU.Data.Database.ExpandScientific(value);
        }

        private static string CsvEscape(string value)
        {
            value = ProtectLongNumber(value ?? "");
            if (value.Contains(";") || value.Contains("\"") || value.Contains("\n"))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            return value;
        }

        private void ImportCsvButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentType == null)
            {
                return;
            }

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "İçe Aktar",
                Filter = "CSV dosyası (*.csv)|*.csv"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            List<string[]> rows;
            try
            {
                rows = ParseCsv(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Dosya okunamadı: " + ex.Message, "Depo Takip",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (rows.Count < 2)
            {
                MessageBox.Show("Dosyada, başlık satırından sonra en az bir veri satırı olmalı.",
                    "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var headers = rows[0];
            var columnToProperty = new Dictionary<int, PropertyDefinition>();
            var quantityColumn = -1;

            for (var i = 0; i < headers.Length; i++)
            {
                var header = headers[i].Trim();

                if (string.Equals(header, "Adet", StringComparison.OrdinalIgnoreCase))
                {
                    quantityColumn = i;
                    continue;
                }

                var property = _currentProperties.FirstOrDefault(p =>
                    string.Equals(p.Name, header, StringComparison.OrdinalIgnoreCase));

                if (property != null)
                {
                    columnToProperty[i] = property;
                }
            }

            var added = 0;
            var skipped = new List<string>();

            for (var r = 1; r < rows.Count; r++)
            {
                var cells = rows[r];

                var quantity = 1;
                if (quantityColumn >= 0 && quantityColumn < cells.Length)
                {
                    int.TryParse(cells[quantityColumn].Trim(), out quantity);
                }
                if (quantity < 1)
                {
                    quantity = 1;
                }

                // İçe aktarmada Seri No tekilliği aranmaz: aynı seri no'lu satırların hepsi eklenir.

                var productId = ProductRepository.Add(_currentType.Id, quantity);

                foreach (var pair in columnToProperty)
                {
                    if (pair.Key >= cells.Length)
                    {
                        continue;
                    }

                    ProductRepository.SetValue(productId, pair.Value.Id, cells[pair.Key].Trim());
                }

                added++;
            }

            LogRepository.Add(_currentType.Name, added + " satır", "İçe aktarıldı");

            var message = added + " ürün içe aktarıldı.";
            if (skipped.Count > 0)
            {
                message += "\n\nZaten kullanılan seri no olduğu için eklenmeyenler: " + string.Join(", ", skipped);
            }
            MessageBox.Show(message, "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Information);

            LoadProductGrid(_currentType);
        }

        /// <summary>Basit bir CSV okuyucu: ";" ayraçlı, çift tırnak içinde ";" ve satır sonu olabilir.</summary>
        private static List<string[]> ParseCsv(string path)
        {
            var text = File.ReadAllText(path, System.Text.Encoding.UTF8);
            var rows = new List<string[]>();
            var current = new List<string>();
            var field = new System.Text.StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (inQuotes)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else if (c == '"')
                    {
                        inQuotes = false;
                    }
                    else
                    {
                        field.Append(c);
                    }
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ';')
                {
                    current.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\r')
                {
                    // yok say, \n satırı bitirecek
                }
                else if (c == '\n')
                {
                    current.Add(field.ToString());
                    field.Clear();
                    rows.Add(current.ToArray());
                    current = new List<string>();
                }
                else
                {
                    field.Append(c);
                }
            }

            if (field.Length > 0 || current.Count > 0)
            {
                current.Add(field.ToString());
                rows.Add(current.ToArray());
            }

            return rows
                .Where(r => r.Length > 1 || !string.IsNullOrWhiteSpace(r.FirstOrDefault()))
                .Select(r => r.Select(UnwrapExcelText).ToArray())
                .ToList();
        }

        // ---------- YEDEKLE / GERİ YÜKLE ----------

        private void BackupButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Yedek Al",
                FileName = "depostok_yedek_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".db",
                Filter = "Depo Takip yedek dosyası (*.db)|*.db"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                BackupService.CreateBackup(dialog.FileName);
                LogRepository.Add(null, System.IO.Path.GetFileName(dialog.FileName), "Yedek alındı");
                MessageBox.Show("Yedek başarıyla alındı.", "Depo Takip",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Yedek alınamadı: " + ex.Message, "Depo Takip",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Yedekten Geri Yükle",
                Filter = "Depo Takip yedek dosyası (*.db)|*.db"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var confirm = MessageBox.Show(
                "Şu anki veriler, seçtiğin yedeğin üzerine yazılacak (bu işlemden önceki hâli ayrıca " +
                "güvenlik kopyası olarak saklanır). Devam etmeden önce programı kapatıp yeniden açman " +
                "gerekecek. Devam edilsin mi?",
                "Depo Takip", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                BackupService.RestoreBackup(dialog.FileName);
                ClearUndoHistory();
                MessageBox.Show(
                    "Yedek geri yüklendi. Değişikliklerin görünmesi için programı şimdi kapatıp yeniden aç.",
                    "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Geri yükleme başarısız: " + ex.Message, "Depo Takip",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// İlk açılış penceresinde "Excel dosyam var" seçilince, ana ekran göründükten sonra
        /// Dosya menüsündeki "Depoya (Excel/CSV)" içe aktarmasını doğrudan başlatır.
        /// </summary>
        public void StartExcelImportFromWelcome()
        {
            ImportAllTypesMenu_Click(this, new RoutedEventArgs());
        }

        private void ScrapMenu_Click(object sender, RoutedEventArgs e)
        {
            var window = new ScrapWindow { Owner = this };
            window.ShowDialog();

            RefreshAfterScrapWindow();
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

                var propertyId = property.Id;

                var expander = new Expander
                {
                    Header = property.Name,
                    IsExpanded = _openFilterIds.Contains(propertyId),
                    Margin = new Thickness(0, 0, 0, 6),
                    Content = content
                };

                // Kullanıcı bir başlığı açarsa, ürün ekleme/silme gibi işlemlerden sonra
                // filtre çubuğu yeniden kurulduğunda o başlık açık kalır.
                expander.Expanded += (s, e) => _openFilterIds.Add(propertyId);
                expander.Collapsed += (s, e) => _openFilterIds.Remove(propertyId);

                FilterItemsPanel.Children.Add(expander);
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

            // Cins sayfasındaki arama da ana sayfadaki gibi TÜM uygulamada (tüm cinsler + hurda) arar.
            RunGlobalSearch(TypeSearchBox);
        }

        /// <summary>
        /// Seçili filtrelerin hepsini uygular (arama artık tüm uygulamada yapıldığı için
        /// cins sayfasındaki tabloyu süzmez).
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

        private void TypesMenu_Click(object sender, RoutedEventArgs e)
        {
            var window = new TypesWindow { Owner = this };
            window.ShowDialog();

            ShowHome();
        }

        private void PropertiesMenu_Click(object sender, RoutedEventArgs e)
        {
            var window = new PropertiesWindow { Owner = this };
            window.ShowDialog();

            ShowHome();
        }

        // ---------- ANA SAYFA ARAMASI ----------

        private void HomeSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            HomeSearchPlaceholder.Visibility = string.IsNullOrEmpty(HomeSearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

            RunGlobalSearch(HomeSearchBox);
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
        private void RunGlobalSearch(TextBox source)
        {
            _searchSource = source;
            HomeSearchPopup.PlacementTarget = source;

            var text = source.Text.Trim();

            if (text.Length < 2)
            {
                CloseHomeSearch();
                return;
            }

            var groups = ProductSearchRepository.Search(text);
            var scrapGroups = ScrapRepository.Search(text);

            if (groups.Count == 0 && scrapGroups.Count == 0)
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
            BuildScrapSearchGroups(scrapGroups, text);
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

            if (e.PropertyName == ProductSearchRepository.QuantityColumn)
            {
                textColumn.Header = "Adet";
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

            var productId = (int)rowView[ProductSearchRepository.ProductIdColumn];

            SearchSource.Clear();
            CloseHomeSearch();

            ShowTypePage(type);
            SelectProductRow(productId);
        }

        /// <summary>
        /// Cins sayfasındaki tabloda, verilen ürünün satırını seçip görünür hâle getirir.
        /// </summary>
        private void SelectProductRow(int productId)
        {
            var view = ProductGrid.ItemsSource as DataView;
            if (view == null)
            {
                return;
            }

            foreach (DataRowView rowView in view)
            {
                if ((int)rowView[IdColumnName] == productId)
                {
                    ProductGrid.SelectedItem = rowView;
                    ProductGrid.ScrollIntoView(rowView);
                    break;
                }
            }
        }

        /// <summary>
        /// Hurdadaki eşleşmeleri, depo sonuçlarının altına, cinse göre gruplu tablolar olarak ekler.
        /// Her hurda satırının en sağında "Hurda" yazar.
        /// </summary>
        private void BuildScrapSearchGroups(List<ScrapSearchGroup> groups, string searchText)
        {
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
                    ScrapSearchGrid_AutoGeneratingColumn(s, e, searchText);

                var capturedGroup = group;
                var capturedGrid = grid;
                grid.MouseDoubleClick += (s, e) =>
                    ScrapSearchGrid_MouseDoubleClick(capturedGrid, capturedGroup);

                var expander = new Expander
                {
                    Header = group.TypeName + " - Hurda (" + group.View.Count + ")",
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.OrangeRed,
                    IsExpanded = true,
                    Margin = new Thickness(0, 0, 0, 8),
                    Content = grid
                };

                HomeSearchGroupsPanel.Children.Add(expander);
            }
        }

        private void ScrapSearchGrid_AutoGeneratingColumn(
            object sender, DataGridAutoGeneratingColumnEventArgs e, string searchText)
        {
            if (e.PropertyName == ScrapRepository.SearchIdColumn)
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

            if (e.PropertyName == ScrapRepository.SearchQuantityColumn)
            {
                textColumn.Header = "Adet";
                return;
            }

            if (e.PropertyName == ScrapRepository.SearchStatusColumn)
            {
                // En sağdaki "Hurda" işareti: turuncu-kırmızı ve kalın.
                textColumn.Header = "Durum";
                style.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.OrangeRed));
                style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
                textColumn.ElementStyle = style;
                return;
            }

            var binding = new Binding(e.PropertyName)
            {
                Converter = new HomeSearchHighlightConverter(searchText)
            };
            style.Setters.Add(new Setter(TextBlock.ForegroundProperty, binding));
            textColumn.ElementStyle = style;
        }

        /// <summary>
        /// Hurda sonucuna çift tıklanınca Hurda ekranını o cinsin sayfasında açar ve o ürünün satırını seçer.
        /// </summary>
        private void ScrapSearchGrid_MouseDoubleClick(DataGrid grid, ScrapSearchGroup group)
        {
            var rowView = grid.SelectedItem as DataRowView;
            if (rowView == null)
            {
                return;
            }

            var scrapId = (int)rowView[ScrapRepository.SearchIdColumn];

            SearchSource.Clear();
            CloseHomeSearch();

            var window = new ScrapWindow(group.TypeName, scrapId) { Owner = this };
            window.ShowDialog();

            RefreshAfterScrapWindow();
        }

        /// <summary>
        /// Hurda ekranı kapandıktan sonra (geri getirme/silme olmuş olabilir) ekranı yeniler.
        /// </summary>
        private void RefreshAfterScrapWindow()
        {
            if (_currentType != null && TypePage.Visibility == Visibility.Visible)
            {
                LoadProductGrid(_currentType);
            }
            else
            {
                ShowHome();
            }
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