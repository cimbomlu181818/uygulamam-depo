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
        private const string ZimmetColumnName = "__Zimmet";

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

        /// <summary>Zimmet değişmiş olabilir; bir ürün cinsi sayfası açıksa tablodaki "Zimmet" sütunu tazelenir.</summary>
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
                    Text = "+",
                    FontWeight = FontWeights.Bold,
                    FontSize = 16,
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
            table.Columns.Add(ZimmetColumnName, typeof(string));

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

            var zimmetSummary = AssignmentRepository.GetActiveSummaryByProduct();

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

                string zimmetText;
                row[ZimmetColumnName] = zimmetSummary.TryGetValue(product.Id, out zimmetText) ? zimmetText : "";

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

            ProductGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Adet",
                Binding = new Binding(QuantityColumnName),
                Width = new DataGridLength(60),
                IsReadOnly = true
            });

            ProductGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Zimmet",
                Binding = new Binding(ZimmetColumnName),
                Width = new DataGridLength(130),
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

            // Zimmetteki ürünler silinemez: önce zimmet iade alınmalı.
            var assignedToSkip = toDelete.Where(id => AssignmentRepository.GetActiveQuantity(id) > 0).ToList();
            if (assignedToSkip.Count > 0)
            {
                MessageBox.Show(
                    assignedToSkip.Count + " ürün zimmette olduğu için silinemez, atlanacak. " +
                    "Önce Zimmetler ekranından iade alın.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Information);

                toDelete = toDelete.Where(id => !assignedToSkip.Contains(id)).ToList();

                if (toDelete.Count == 0)
                {
                    LoadProductGrid(_currentType);
                    return;
                }
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
                var description = BuildProductDescription(
                    ProductRepository.GetValues(productId), ProductRepository.GetQuantity(productId));

                ProductRepository.Delete(productId);
                LogRepository.Add(_currentType.Name, description, "Silindi");
            }

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
                MessageBox.Show("Önce hurdaya taşımak istediğin ürünlerin kutucuğunu işaretle.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                "Seçili " + toMove.Count + " ürün hurdaya taşınacak, onaylıyor musun?\n\n" +
                "Hurdaya taşınan ürünler depodan kaldırılır ama silinmez; Hurda ekranından istediğin zaman geri getirebilirsin.",
                "Depo Durumu", MessageBoxButton.YesNo, MessageBoxImage.Question);

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
                    MessageBox.Show(ex.Message, "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            if (skippedAssigned > 0)
            {
                MessageBox.Show(
                    skippedAssigned + " ürün zimmette olduğu için hurdaya taşınmadı. " +
                    "Önce Zimmetler ekranından iade alın.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            MessageBox.Show(view.Count + " ürün dışa aktarıldı.", "Depo Durumu",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static string CsvEscape(string value)
        {
            value = value ?? "";
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
                MessageBox.Show("Dosya okunamadı: " + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (rows.Count < 2)
            {
                MessageBox.Show("Dosyada, başlık satırından sonra en az bir veri satırı olmalı.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                // Seri No gibi tekil özellikleri, depoda ve hurdada önceden kontrol et.
                var conflict = false;
                foreach (var pair in columnToProperty)
                {
                    var property = pair.Value;
                    if (!property.IsSerialNumber || pair.Key >= cells.Length)
                    {
                        continue;
                    }

                    var value = cells[pair.Key].Trim();
                    if (string.IsNullOrEmpty(value))
                    {
                        continue;
                    }

                    if (ProductRepository.IsValueUsedByAnotherProduct(property.Id, value, -1) ||
                        ScrapRepository.IsSerialNumberUsed(value))
                    {
                        skipped.Add(value);
                        conflict = true;
                    }
                }

                if (conflict)
                {
                    continue;
                }

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
            MessageBox.Show(message, "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Information);

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

            return rows.Where(r => r.Length > 1 || !string.IsNullOrWhiteSpace(r.FirstOrDefault())).ToList();
        }

        // ---------- YEDEKLE / GERİ YÜKLE ----------

        private void BackupButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Yedek Al",
                FileName = "depostok_yedek_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".db",
                Filter = "Depo Durumu yedek dosyası (*.db)|*.db"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                BackupService.CreateBackup(dialog.FileName);
                LogRepository.Add(null, System.IO.Path.GetFileName(dialog.FileName), "Yedek alındı");
                MessageBox.Show("Yedek başarıyla alındı.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Yedek alınamadı: " + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Yedekten Geri Yükle",
                Filter = "Depo Durumu yedek dosyası (*.db)|*.db"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var confirm = MessageBox.Show(
                "Şu anki veriler, seçtiğin yedeğin üzerine yazılacak (bu işlemden önceki hâli ayrıca " +
                "güvenlik kopyası olarak saklanır). Devam etmeden önce programı kapatıp yeniden açman " +
                "gerekecek. Devam edilsin mi?",
                "Depo Durumu", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                BackupService.RestoreBackup(dialog.FileName);
                MessageBox.Show(
                    "Yedek geri yüklendi. Değişikliklerin görünmesi için programı şimdi kapatıp yeniden aç.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Geri yükleme başarısız: " + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
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