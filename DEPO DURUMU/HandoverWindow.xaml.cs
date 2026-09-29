using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    /// <summary>
    /// Birimler arası malzeme teslim-tesellüm tutanağı. Yazdır'a basılınca tutanak basılır ve
    /// Tutanaklar defterine kaydedilir; eski bir kayıt açıldıysa aynı kayıt güncellenir (çoğaltılmaz).
    /// Kaydet düğmesi yazdırmadan sadece defteri günceller. Stok ve zimmeti etkilemez.
    /// </summary>
    public partial class HandoverWindow : Window
    {
        private static readonly CultureInfo Turkish = new CultureInfo("tr-TR");

        private int _handoverId;

        public ObservableCollection<HandoverItem> Items { get; } = new ObservableCollection<HandoverItem>();

        /// <summary>Pencerede kayıt oluşturuldu ya da güncellendi mi? Açan ekran buna bakıp listeyi yeniler.</summary>
        public bool Changed { get; private set; }

        /// <summary>Boş, yeni bir tutanak açar.</summary>
        public HandoverWindow() : this(null, null)
        {
        }

        /// <summary>
        /// Bir ürün detayından açılınca ilk satırı o ürünün seri no ve sistem adıyla önceden doldurur.
        /// İkisi de boşsa normal boş pencere gibi açılır.
        /// </summary>
        public HandoverWindow(string serialNo, string itemType)
        {
            InitializeComponent();

            DateBox.Text = DateTime.Now.ToString("dd.MM.yyyy", Turkish);
            ItemsGrid.ItemsSource = Items;
            ItemsGrid.LayoutUpdated += (s, e) => UpdateFillHandle();
            ItemsGrid.CurrentCellChanged += (s, e) => UpdateFillHandle();
            RecordText.Text = "Yeni tutanak (yazdırılınca deftere kaydedilir)";

            if (!string.IsNullOrWhiteSpace(serialNo) || !string.IsNullOrWhiteSpace(itemType))
            {
                Items.Add(new HandoverItem
                {
                    SerialNo = serialNo,
                    ItemType = itemType,
                    Quantity = "1"
                });
            }
        }

        /// <summary>Ürün listesinde sağ tıkla açılınca, seçili her ürün için bir satır dolu gelir.</summary>
        public HandoverWindow(IEnumerable<HandoverItem> items) : this(null, null)
        {
            if (items == null)
            {
                return;
            }

            foreach (var item in items)
            {
                Items.Add(item);
            }
        }

        /// <summary>Tutanaklar defterindeki kayıtlı bir tutanağı açar; yazdırılınca ya da kaydedilince aynı kayıt güncellenir.</summary>
        public HandoverWindow(Handover existing) : this(null, null)
        {
            _handoverId = existing.Id;

            FromUnitBox.Text = existing.FromUnit;
            ToUnitBox.Text = existing.ToUnit;
            CategoryBox.Text = existing.Category;
            DateBox.Text = existing.HandoverDate;

            foreach (var line in existing.Items)
            {
                Items.Add(new HandoverItem
                {
                    SerialNo = line.SerialNo,
                    ItemType = line.ItemType,
                    Quantity = line.Quantity,
                    Note = line.Note
                });
            }

            RecordText.Text = "Kayıtlı tutanak — No: " + existing.Id;
        }

        // ---------- SERİ NO / SİSTEM ADI ARAMA ----------

        /// <summary>Bir arama kutusunun kendi Popup/Liste referanslarını ve arama türünü tutar.</summary>
        private class AutoCompleteRefs
        {
            public Popup Popup;
            public ListBox List;
            public bool SerialOnly;
        }

        private void SerialBox_Loaded(object sender, RoutedEventArgs e)
        {
            WireSearchBox((TextBox)sender, true);
        }

        private void TypeBox_Loaded(object sender, RoutedEventArgs e)
        {
            WireSearchBox((TextBox)sender, false);
        }

        private void WireSearchBox(TextBox box, bool serialOnly)
        {
            try
            {
                var container = (Grid)box.Parent;
                var popup = container.Children.OfType<Popup>().First();
                var list = (ListBox)((Border)popup.Child).Child;

                box.Tag = new AutoCompleteRefs { Popup = popup, List = list, SerialOnly = serialOnly };
                list.Tag = box;

                box.Focus();
                box.SelectAll();
            }
            catch (Exception)
            {
                // Öneri kutusu hazırlanamazsa yazı yazmaya devam edilir; sadece otomatik öneri çıkmaz.
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var box = (TextBox)sender;
            var refs = box.Tag as AutoCompleteRefs;

            if (refs == null)
            {
                return;
            }

            var query = box.Text.Trim();

            if (query.Length < 2)
            {
                refs.Popup.IsOpen = false;
                return;
            }

            List<ProductSearchResult> results;

            try
            {
                results = refs.SerialOnly
                    ? HandoverSearchRepository.SearchBySerial(query)
                    : HandoverSearchRepository.SearchGeneral(query);
            }
            catch (Exception)
            {
                refs.Popup.IsOpen = false;
                return;
            }

            if (results.Count == 0)
            {
                refs.Popup.IsOpen = false;
                return;
            }

            refs.List.ItemsSource = results;
            refs.Popup.IsOpen = true;
        }

        /// <summary>
        /// Açılır listede bir ürüne tıklanınca çalışır. Popup StaysOpen="False" olduğu için
        /// tıklama anında kapanmaya çalışır; bu yüzden PreviewMouseLeftButtonDown kullanılıyor.
        /// </summary>
        private void SuggestionsList_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var listBox = (ListBox)sender;
            var element = e.OriginalSource as DependencyObject;

            while (element != null && !(element is ListBoxItem))
            {
                element = VisualTreeHelper.GetParent(element);
            }

            if (element == null)
            {
                return;
            }

            var result = listBox.ItemContainerGenerator.ItemFromContainer(element) as ProductSearchResult;
            var box = listBox.Tag as TextBox;

            if (result == null || box == null)
            {
                return;
            }

            ApplySelection(box, result);
            e.Handled = true;
        }

        private void ApplySelection(TextBox box, ProductSearchResult result)
        {
            var refs = (AutoCompleteRefs)box.Tag;
            var row = box.DataContext as HandoverItem;

            if (row == null)
            {
                return;
            }

            row.SerialNo = result.SerialNo;
            row.ItemType = result.TypeName;

            refs.Popup.IsOpen = false;

            ItemsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            if (Items.Count > 0 && Items[Items.Count - 1] == row)
            {
                Items.Add(new HandoverItem());
            }
        }

        // ---------- EXCEL GİBİ AŞAĞI DOLDURMA ----------
        // Bir hücreye yazı yazılınca hücrenin sağ alt köşesinde küçük bir kare çıkar. Kareyi aşağı
        // sürükleyince, gerekirse yeni satırlar açılır ve sadece o sütuna hücredeki değer yazılır;
        // diğer sütunlar boş kalır. Sürüklenen yerde dolu satır varsa sadece o sütunun değeri değişir.

        private HandoverItem _fillSource;
        private int _fillColumn;
        private int _fillSteps;
        private bool _filling;

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var typed = child as T;
                if (typed != null)
                {
                    return typed;
                }

                var deeper = FindVisualChild<T>(child);
                if (deeper != null)
                {
                    return deeper;
                }
            }

            return null;
        }

        private static string GetColumnValue(HandoverItem item, int column)
        {
            switch (column)
            {
                case 0: return item.SerialNo;
                case 1: return item.ItemType;
                case 2: return item.Quantity;
                case 3: return item.Note;
                default: return null;
            }
        }

        private static void SetColumnValue(HandoverItem item, int column, string value)
        {
            switch (column)
            {
                case 0: item.SerialNo = value; break;
                case 1: item.ItemType = value; break;
                case 2: item.Quantity = value; break;
                case 3: item.Note = value; break;
            }
        }

        private DataGridCell GetCell(HandoverItem item, int columnIndex)
        {
            var row = ItemsGrid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
            if (row == null)
            {
                return null;
            }

            var presenter = FindVisualChild<DataGridCellsPresenter>(row);
            if (presenter == null)
            {
                return null;
            }

            return presenter.ItemContainerGenerator.ContainerFromIndex(columnIndex) as DataGridCell;
        }

        /// <summary>Şu an seçili hücreyi, satırını ve sütun sırasını verir. Yoksa null döner.</summary>
        private DataGridCell GetCurrentCell(out HandoverItem item, out int columnIndex)
        {
            item = null;
            columnIndex = -1;

            var info = ItemsGrid.CurrentCell;
            if (!info.IsValid || info.Column == null)
            {
                return null;
            }

            item = info.Item as HandoverItem;
            if (item == null)
            {
                return null;
            }

            columnIndex = ItemsGrid.Columns.IndexOf(info.Column);
            if (columnIndex < 0)
            {
                return null;
            }

            return GetCell(item, columnIndex);
        }

        private void HideFillHandle()
        {
            if (FillHandle.Visibility != Visibility.Collapsed)
            {
                FillHandle.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>Doldurma tutamacını seçili hücrenin sağ alt köşesine koyar; hücre boşsa ya da görünmüyorsa gizler.</summary>
        private void UpdateFillHandle()
        {
            if (_filling || GridHost.ActualHeight <= 0)
            {
                return;
            }

            HandoverItem item;
            int column;
            var cell = GetCurrentCell(out item, out column);

            if (cell == null || !cell.IsVisible || Items.IndexOf(item) < 0 ||
                string.IsNullOrEmpty(GetColumnValue(item, column)))
            {
                HideFillHandle();
                return;
            }

            Point corner;
            try
            {
                corner = cell.TransformToAncestor(GridHost).Transform(new Point(cell.ActualWidth, cell.ActualHeight));
            }
            catch (InvalidOperationException)
            {
                HideFillHandle();
                return;
            }

            var headers = FindVisualChild<DataGridColumnHeadersPresenter>(ItemsGrid);
            var headerHeight = headers != null ? headers.ActualHeight : 22;

            // Hücrenin sağ kenarı tablonun sağ kenarına değiyorsa (son sütun) kare taşmasın diye içeri çekilir.
            if (corner.Y < headerHeight + 2 || corner.Y > GridHost.ActualHeight - 2 ||
                corner.X < 0 || corner.X > GridHost.ActualWidth + 1)
            {
                HideFillHandle();
                return;
            }

            var left = System.Math.Min(corner.X - FillHandle.Width / 2, GridHost.ActualWidth - FillHandle.Width - 1);
            var top = corner.Y - FillHandle.Height / 2;

            if (Canvas.GetLeft(FillHandle) != left)
            {
                Canvas.SetLeft(FillHandle, left);
            }

            if (Canvas.GetTop(FillHandle) != top)
            {
                Canvas.SetTop(FillHandle, top);
            }

            if (FillHandle.Visibility != Visibility.Visible)
            {
                FillHandle.Visibility = Visibility.Visible;
            }
        }

        private void FillHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Yazılmakta olan hücre/satır varsa önce kaydet ki değer satıra işlensin.
            ItemsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            HandoverItem item;
            int column;
            var cell = GetCurrentCell(out item, out column);

            if (cell == null || Items.IndexOf(item) < 0)
            {
                return;
            }

            _fillSource = item;
            _fillColumn = column;
            _fillSteps = 0;
            _filling = true;

            FillHandle.CaptureMouse();
            e.Handled = true;
        }

        private void FillHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_filling || _fillSource == null)
            {
                return;
            }

            var cell = GetCell(_fillSource, _fillColumn);
            if (cell == null || cell.ActualHeight <= 0)
            {
                return;
            }

            var topLeft = cell.TransformToAncestor(GridHost).Transform(new Point(0, 0));
            var cellBottom = topLeft.Y + cell.ActualHeight;
            var delta = e.GetPosition(GridHost).Y - cellBottom;

            _fillSteps = delta <= 0 ? 0 : (int)Math.Ceiling(delta / cell.ActualHeight);
            if (_fillSteps > 500)
            {
                _fillSteps = 500;
            }

            if (_fillSteps == 0)
            {
                FillPreview.Visibility = Visibility.Collapsed;
                return;
            }

            var height = cell.ActualHeight * (_fillSteps + 1);
            var available = GridHost.ActualHeight - topLeft.Y;
            if (height > available)
            {
                height = available;
            }

            Canvas.SetLeft(FillPreview, topLeft.X);
            Canvas.SetTop(FillPreview, topLeft.Y);
            FillPreview.Width = cell.ActualWidth;
            FillPreview.Height = height > 0 ? height : 0;
            FillPreview.Visibility = Visibility.Visible;
        }

        private void FillHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_filling)
            {
                return;
            }

            var source = _fillSource;
            var column = _fillColumn;
            var steps = _fillSteps;

            _filling = false;
            _fillSource = null;
            _fillSteps = 0;
            FillPreview.Visibility = Visibility.Collapsed;
            FillHandle.ReleaseMouseCapture();

            if (source != null && steps > 0)
            {
                FillDown(source, column, steps);
            }

            UpdateFillHandle();
        }

        private void FillHandle_LostMouseCapture(object sender, MouseEventArgs e)
        {
            // Sürükleme başka bir nedenle yarıda kesilirse (örneğin pencere değişirse) iptal et.
            if (_filling)
            {
                _filling = false;
                _fillSource = null;
                _fillSteps = 0;
                FillPreview.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Kaynak satırın verilen sütundaki değerini, altındaki "steps" satıra yazar. Yeterli satır yoksa
        /// boş satırlar açılır. Diğer sütunlara dokunulmaz.
        /// </summary>
        private void FillDown(HandoverItem source, int column, int steps)
        {
            var value = GetColumnValue(source, column);
            var start = Items.IndexOf(source);

            if (start < 0)
            {
                return;
            }

            for (var i = 1; i <= steps; i++)
            {
                var index = start + i;

                while (Items.Count <= index)
                {
                    Items.Add(new HandoverItem());
                }

                SetColumnValue(Items[index], column, value);
            }
        }

        // ---------- KAYDET / YAZDIR ----------

        /// <summary>Formdaki bilgileri doğrular; sorun yoksa deftere yazılacak tutanağı verir, varsa uyarıp null döner.</summary>
        private Handover CollectHandover()
        {
            if (string.IsNullOrWhiteSpace(FromUnitBox.Text))
            {
                ShowWarning("Teslim eden birim boş bırakılamaz.");
                FromUnitBox.Focus();
                return null;
            }

            if (string.IsNullOrWhiteSpace(ToUnitBox.Text))
            {
                ShowWarning("Teslim alan birim boş bırakılamaz.");
                ToUnitBox.Focus();
                return null;
            }

            if (string.IsNullOrWhiteSpace(DateBox.Text))
            {
                ShowWarning("Tarih boş bırakılamaz.");
                DateBox.Focus();
                return null;
            }

            ItemsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            var rows = Items
                .Where(i => !string.IsNullOrWhiteSpace(i.SerialNo) ||
                            !string.IsNullOrWhiteSpace(i.ItemType) ||
                            !string.IsNullOrWhiteSpace(i.Quantity) ||
                            !string.IsNullOrWhiteSpace(i.Note))
                .ToList();

            if (rows.Count == 0)
            {
                ShowWarning("En az bir malzeme satırı girmelisin.");
                return null;
            }

            return new Handover
            {
                Id = _handoverId,
                FromUnit = FromUnitBox.Text.Trim(),
                ToUnit = ToUnitBox.Text.Trim(),
                Category = CategoryBox.Text.Trim(),
                HandoverDate = DateBox.Text.Trim(),
                Items = rows.Select(r => new HandoverLine
                {
                    SerialNo = r.SerialNo,
                    ItemType = r.ItemType,
                    Quantity = r.Quantity,
                    Note = r.Note
                }).ToList()
            };
        }

        private bool SaveToLedger(Handover handover)
        {
            try
            {
                _handoverId = HandoverRepository.Save(handover);
                RecordText.Text = "Kayıtlı tutanak — No: " + _handoverId;
                Changed = true;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Deftere kaydedilemedi:\n" + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var handover = CollectHandover();
            if (handover == null)
            {
                return;
            }

            if (SaveToLedger(handover))
            {
                MessageBox.Show(this, "Tutanak deftere kaydedildi (No: " + _handoverId + ").", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            var handover = CollectHandover();
            if (handover == null)
            {
                return;
            }

            var printDialog = new PrintDialog();

            if (printDialog.ShowDialog() != true)
            {
                return;
            }

            FlowDocument document = BuildDocument(handover, printDialog.PrintableAreaWidth);
            IDocumentPaginatorSource paginatorSource = document;

            try
            {
                printDialog.PrintDocument(paginatorSource.DocumentPaginator, "Teslim-Tesellüm Tutanağı");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Yazdırılamadı (deftere kaydedilmedi):\n" + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Yazdırma başarılı: tutanağı deftere yaz (kayıtlıysa güncelle).
            SaveToLedger(handover);
        }

        // ---------- YAZDIRILACAK BELGE ----------

        private FlowDocument BuildDocument(Handover handover, double pageWidth)
        {
            double effectivePageWidth = pageWidth > 0 ? pageWidth : 750;

            var document = new FlowDocument
            {
                PageWidth = effectivePageWidth,
                ColumnWidth = effectivePageWidth,
                FontSize = 11,
                PagePadding = new Thickness(25)
            };

            document.Blocks.Add(new Paragraph(new Run("DEMİRBAŞ / TÜKETİM MALZEME TESLİM TUTANAĞI"))
            {
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 14)
            });

            var infoTable = new Table();
            infoTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            infoTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            var infoGroup = new TableRowGroup();
            infoTable.RowGroups.Add(infoGroup);

            var infoRow1 = new TableRow();
            infoRow1.Cells.Add(MakePlainCell("Teslim Eden Birim: " + handover.FromUnit));
            infoRow1.Cells.Add(MakePlainCell("Kategori: " + handover.Category));
            infoGroup.Rows.Add(infoRow1);

            var infoRow2 = new TableRow();
            infoRow2.Cells.Add(MakePlainCell("Teslim Alan Birim: " + handover.ToUnit));
            infoRow2.Cells.Add(MakePlainCell("Tarih: " + handover.HandoverDate));
            infoGroup.Rows.Add(infoRow2);

            document.Blocks.Add(infoTable);
            document.Blocks.Add(new Paragraph { Margin = new Thickness(0, 0, 0, 6) });

            var table = new Table();
            var columnWidths = new[] { 0.5, 1.5, 2.5, 1.0, 3.0 }; // S.N, Seri No, Sistem adı, Miktarı, Düşünceler
            foreach (var w in columnWidths)
            {
                table.Columns.Add(new TableColumn { Width = new GridLength(w, GridUnitType.Star) });
            }

            var rowGroup = new TableRowGroup();
            table.RowGroups.Add(rowGroup);

            var headerRow = new TableRow { Background = Brushes.LightGray };
            headerRow.Cells.Add(MakeCell("S.N", true));
            headerRow.Cells.Add(MakeCell("SERİ NO", true));
            headerRow.Cells.Add(MakeCell("Sistem adı", true));
            headerRow.Cells.Add(MakeCell("Miktarı", true));
            headerRow.Cells.Add(MakeCell("DÜŞÜNCELER", true));
            rowGroup.Rows.Add(headerRow);

            for (int i = 0; i < handover.Items.Count; i++)
            {
                var item = handover.Items[i];
                var tableRow = new TableRow();

                tableRow.Cells.Add(MakeCell((i + 1).ToString(Turkish), false));
                tableRow.Cells.Add(MakeCell(item.SerialNo, false));
                tableRow.Cells.Add(MakeCell(item.ItemType, false));
                tableRow.Cells.Add(MakeCell(item.Quantity, false));
                tableRow.Cells.Add(MakeCell(item.Note, false));
                rowGroup.Rows.Add(tableRow);
            }

            document.Blocks.Add(table);
            document.Blocks.Add(new Paragraph { Margin = new Thickness(0, 30, 0, 0) });

            var signTable = new Table();
            for (int i = 0; i < 3; i++)
            {
                signTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            }

            var signGroup = new TableRowGroup();
            signTable.RowGroups.Add(signGroup);

            var signRow = new TableRow();
            signRow.Cells.Add(MakeSignatureCell("TESLİM EDEN"));
            signRow.Cells.Add(MakeSignatureCell("HAZURUN"));
            signRow.Cells.Add(MakeSignatureCell("TESLİM ALAN"));
            signGroup.Rows.Add(signRow);

            document.Blocks.Add(signTable);

            return document;
        }

        private static TableCell MakePlainCell(string text)
        {
            return new TableCell(new Paragraph(new Run(text)) { Margin = new Thickness(0) })
            {
                Padding = new Thickness(0, 2, 0, 2)
            };
        }

        private static TableCell MakeCell(string text, bool bold)
        {
            var paragraph = new Paragraph(new Run(text ?? "")) { Margin = new Thickness(0) };

            if (bold)
            {
                paragraph.FontWeight = FontWeights.Bold;
                paragraph.TextAlignment = TextAlignment.Center;
            }

            return new TableCell(paragraph)
            {
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 4, 4, 20)
            };
        }

        private static TableCell MakeSignatureCell(string title)
        {
            var paragraph = new Paragraph(new Run(title))
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(20, 40, 20, 0)
            };

            return new TableCell(paragraph)
            {
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 8, 0, 0)
            };
        }

        private void ShowWarning(string message)
        {
            MessageBox.Show(this, message, "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Teslim-tesellüm tutanağındaki bir malzeme satırı. Arama sonucundan seçilince hem tablodaki
    /// hücrelerin hem de düzenlenmekte olan hücrenin anında güncellenebilmesi için değişiklik bildirimi yapar.
    /// </summary>
    public class HandoverItem : INotifyPropertyChanged
    {
        private string _serialNo;
        private string _itemType;
        private string _quantity;
        private string _note;

        public string SerialNo
        {
            get { return _serialNo; }
            set { _serialNo = value; OnChanged("SerialNo"); }
        }

        public string ItemType
        {
            get { return _itemType; }
            set { _itemType = value; OnChanged("ItemType"); }
        }

        public string Quantity
        {
            get { return _quantity; }
            set { _quantity = value; OnChanged("Quantity"); }
        }

        public string Note
        {
            get { return _note; }
            set { _note = value; OnChanged("Note"); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}