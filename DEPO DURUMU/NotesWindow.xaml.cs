using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    /// <summary>
    /// Notlar tablosunun bir sütunu. Hücre görünürken hesaplanmış değeri (Display), düzenlenirken
    /// yazılan metni (Raw: =A1+B2 gibi) gösterir. 1. satırdaki hücrelerde Excel'deki gibi filtre oku vardır.
    /// </summary>
    internal sealed class NoteColumn : DataGridTextColumn
    {
        private readonly Action<int, Button> _buttonCreated;
        private readonly Action<int, Button> _buttonClicked;

        public int Index { get; private set; }

        public NoteColumn(int index, double width, Action<int, Button> buttonCreated, Action<int, Button> buttonClicked)
        {
            Index = index;
            _buttonCreated = buttonCreated;
            _buttonClicked = buttonClicked;

            Header = NoteFormula.ColumnName(index);
            Width = new DataGridLength(width);
            MinWidth = 30;
            CanUserSort = false;
            CanUserReorder = false;

            // Düzenleme kutusu hücrenin yazılan metnine (Raw) bağlanır.
            Binding = new System.Windows.Data.Binding("[" + index.ToString(CultureInfo.InvariantCulture) + "].Raw")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
            };
        }

        protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
        {
            // Tablo satırları kaydırırken yeniden kullanır; bu yüzden hücre içeriği satırın türüne
            // göre değil, her satırda aynı kurulur. Ok yalnızca başlık satırında (IsHeader) görünür.
            var text = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 0, 4, 0)
            };
            text.SetBinding(TextBlock.TextProperty,
                new System.Windows.Data.Binding("[" + Index.ToString(CultureInfo.InvariantCulture) + "].Display")
                {
                    Mode = BindingMode.OneWay
                });

            var button = new Button
            {
                Content = "\u25BC",
                Width = 17,
                Height = 17,
                FontSize = 8,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0),
                Focusable = false,
                ToolTip = "Filtrele / sırala",
                Tag = Index
            };
            button.SetBinding(UIElement.VisibilityProperty,
                new System.Windows.Data.Binding("IsHeader")
                {
                    Mode = BindingMode.OneWay,
                    Converter = new BooleanToVisibilityConverter()
                });

            var columnIndex = Index;
            button.Click += delegate
            {
                if (_buttonClicked != null)
                {
                    _buttonClicked(columnIndex, button);
                }
            };

            // Ok gizliyken yer kaplamaz; görünürken metnin sağında durur.
            var panel = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(button, Dock.Right);
            panel.Children.Add(button);
            panel.Children.Add(text);

            if (_buttonCreated != null)
            {
                _buttonCreated(columnIndex, button);
            }

            return panel;
        }
    }

    /// <summary>
    /// Notlar: Excel benzeri, çok sayfalı not tablosu. Excel'den kopyala-yapıştır, hesap makinesi
    /// mantığında formüller, sütun filtresi ve alttaki sekmelerle istenen kadar sayfa. Her şey
    /// programın kendi veritabanına otomatik kaydedilir.
    /// </summary>
    public partial class NotesWindow : Window
    {
        private struct CellPos
        {
            public int Row;
            public int Col;
        }

        /// <summary>Bir sayfanın bu pencere açıkken tuttuğu geçmiş: geri al / ileri al ve filtreler.</summary>
        private sealed class SheetState
        {
            public readonly List<string> Undo = new List<string>();
            public readonly List<string> Redo = new List<string>();
            public string Last;
            public readonly Dictionary<int, HashSet<string>> Filters = new Dictionary<int, HashSet<string>>();

            public void PushUndo(string snapshot)
            {
                Undo.Add(snapshot);
                if (Undo.Count > 100)
                {
                    Undo.RemoveAt(0);
                }
            }
        }

        private sealed class ValueComparer : IComparer<NoteValue>
        {
            public int Compare(NoteValue a, NoteValue b)
            {
                return NoteSheet.CompareForSort(a, b, true);
            }
        }

        private const double DefaultColumnWidth = 90;

        // Kaydırdıkça otomatik büyüme: sona yaklaşınca bu kadar satır / sütun eklenir.
        private const int GrowRows = 50;
        private const int GrowColumns = 50;
        private const int NearBottomRows = 5;
        private const double NearRightPixels = 120;

        private readonly List<NoteSheet> _sheets = new List<NoteSheet>();
        private readonly Dictionary<NoteSheet, SheetState> _states = new Dictionary<NoteSheet, SheetState>();
        private readonly Dictionary<NoteSheet, Border> _tabBorders = new Dictionary<NoteSheet, Border>();
        private readonly HashSet<Button> _filterButtons = new HashSet<Button>();
        private readonly DispatcherTimer _saveTimer;

        private NoteSheet _current;
        private ListCollectionView _view;
        private bool _dirty;
        private bool _rebuilding;
        private bool _saveErrorShown;
        private bool _growing;

        // Formül çubuğu
        private static readonly Regex AddressRegex = new Regex(
            @"^\s*\$?([A-Za-z]{1,3})\$?(\d+)\s*(?::\s*\$?([A-Za-z]{1,3})\$?(\d+))?\s*$",
            RegexOptions.Compiled);

        private bool _syncingBar;
        private bool _barDirty;
        private bool _barHasTarget;
        private bool _lastPosValid;
        private int _lastRow;
        private int _lastCol;
        private int _barRow;
        private int _barCol;

        // Formül yazarken hücreye tıklayınca adresi ekleme
        private TextBox _refEditor;
        private int _refStart;
        private int _refLength;
        private string _refText = "";
        private bool _dragging;
        private int _dragAnchorRow;
        private int _dragAnchorCol;
        private int _dragLastRow;
        private int _dragLastCol;

        public NotesWindow()
        {
            InitializeComponent();

            SheetGrid.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(SheetGrid_ScrollChanged));

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _saveTimer.Tick += delegate
            {
                _saveTimer.Stop();
                SaveCurrent();
            };

            try
            {
                _sheets.AddRange(NoteRepository.LoadAll());

                if (_sheets.Count == 0)
                {
                    var first = NoteSheet.CreateEmpty("Notlar1");
                    NoteRepository.Insert(first);
                    _sheets.Add(first);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Notlar okunamadı:\n" + ex.Message, "Notlar",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                if (_sheets.Count == 0)
                {
                    _sheets.Add(NoteSheet.CreateEmpty("Notlar1"));
                }
            }

            foreach (var sheet in _sheets)
            {
                HookSheet(sheet);
            }

            BuildTabs();
            SelectSheet(_sheets[0]);
        }

        private SheetState State
        {
            get { return _states[_current]; }
        }

        private void HookSheet(NoteSheet sheet)
        {
            sheet.CellEdited += Sheet_CellEdited;
            _states[sheet] = new SheetState();
        }

        // =====================================================================
        // KAYIT
        // =====================================================================

        private void MarkDirty()
        {
            _dirty = true;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>Tablodaki sütun genişliklerini (kullanıcı sürüklemiş olabilir) sayfaya yazar.</summary>
        private void CaptureWidths()
        {
            if (_current == null)
            {
                return;
            }

            for (var c = 0; c < SheetGrid.Columns.Count && c < _current.ColumnWidths.Count; c++)
            {
                var width = SheetGrid.Columns[c].ActualWidth;
                if (width >= 20)
                {
                    _current.ColumnWidths[c] = Math.Round(width);
                }
            }
        }

        private void SaveCurrent()
        {
            if (_current == null || !_dirty)
            {
                return;
            }

            CaptureWidths();

            try
            {
                NoteRepository.Update(_current);
                _dirty = false;
            }
            catch (Exception ex)
            {
                ShowSaveError(ex);
            }
        }

        /// <summary>Verilen sayfayı hemen kaydeder (ad değişimi gibi durumlarda).</summary>
        private void PersistSheet(NoteSheet sheet)
        {
            if (ReferenceEquals(sheet, _current))
            {
                CaptureWidths();
            }

            try
            {
                NoteRepository.Update(sheet);
                if (ReferenceEquals(sheet, _current))
                {
                    _dirty = false;
                }
            }
            catch (Exception ex)
            {
                ShowSaveError(ex);
            }
        }

        private void ShowSaveError(Exception ex)
        {
            if (_saveErrorShown)
            {
                return;
            }

            _saveErrorShown = true;
            MessageBox.Show(this, "Notlar kaydedilemedi:\n" + ex.Message, "Notlar",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            CommitGridEdit();
            TrimCurrentToData();
            SaveCurrent();
            _saveTimer.Stop();
        }

        // =====================================================================
        // TABLO
        // =====================================================================

        private void CommitGridEdit()
        {
            CommitFormulaBar();

            try
            {
                SheetGrid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch (Exception)
            {
                // Düzenleme yoksa ya da tablo meşgulse yapılacak bir şey yok.
            }
        }

        private void SelectSheet(NoteSheet sheet)
        {
            if (ReferenceEquals(sheet, _current) && SheetGrid.ItemsSource != null)
            {
                return;
            }

            CommitGridEdit();
            TrimCurrentToData();
            SaveCurrent();

            _current = sheet;

            var state = State;
            if (state.Last == null)
            {
                state.Last = sheet.Snapshot();
            }

            RebuildGrid();
            RefreshTabVisuals();
            UpdateToolbar();
            UpdateStatus();
        }

        /// <summary>Sütunları ve satırları sayfadan yeniden kurar (sayfa değişince, satır/sütun eklenince vb.).</summary>
        private void RebuildGrid()
        {
            _lastPosValid = false;
            _rebuilding = true;
            try
            {
                SheetGrid.ItemsSource = null;
                SheetGrid.Columns.Clear();
                _filterButtons.Clear();

                for (var c = 0; c < _current.ColumnCount; c++)
                {
                    var width = c < _current.ColumnWidths.Count ? _current.ColumnWidths[c] : 0;
                    SheetGrid.Columns.Add(new NoteColumn(
                        c,
                        width > 10 ? width : DefaultColumnWidth,
                        OnFilterButtonCreated,
                        OnFilterButtonClicked));
                }

                _view = new ListCollectionView(_current.Rows);
                if (State.Filters.Count > 0)
                {
                    _view.Filter = FilterRow;
                }

                SheetGrid.ItemsSource = _view;
            }
            finally
            {
                _rebuilding = false;
            }

            UpdateToolbar();
        }

        private void Sheet_CellEdited(NoteCell cell)
        {
            if (_rebuilding || _current == null)
            {
                return;
            }

            // Kullanıcı bir hücreyi düzenleyip onayladı: önceki hâli geri alma listesine girer.
            var state = State;
            state.PushUndo(state.Last ?? _current.Snapshot());
            state.Redo.Clear();

            _current.Recalculate();
            state.Last = _current.Snapshot();

            MarkDirty();
            UpdateToolbar();
            UpdateStatus();
        }

        /// <summary>
        /// Tabloyu değiştiren her işlem buradan geçer: önceki hâl geri alma listesine girer,
        /// formüller yeniden hesaplanır, kayıt zamanlanır. mutate, tablonun yeniden kurulması
        /// gerekiyorsa true döndürür (satır/sütun sayısı değiştiyse vb.).
        /// </summary>
        private void ApplyChange(Func<bool> mutate)
        {
            if (_current == null)
            {
                return;
            }

            CommitGridEdit();
            CaptureWidths();

            var state = State;
            var before = state.Last ?? _current.Snapshot();

            bool rebuild;
            _current.Quiet = true;
            try
            {
                rebuild = mutate();
            }
            finally
            {
                _current.Quiet = false;
            }

            _current.Recalculate();

            var after = _current.Snapshot();
            if (after != before)
            {
                state.PushUndo(before);
                state.Redo.Clear();
                state.Last = after;
                MarkDirty();
            }

            if (rebuild)
            {
                RebuildGrid();
            }

            UpdateToolbar();
            UpdateStatus();
        }

        private void Undo()
        {
            if (_current == null)
            {
                return;
            }

            CommitGridEdit();

            var state = State;
            if (state.Undo.Count == 0)
            {
                return;
            }

            RestoreFromHistory(state, state.Undo, state.Redo);
        }

        private void Redo()
        {
            if (_current == null)
            {
                return;
            }

            CommitGridEdit();

            var state = State;
            if (state.Redo.Count == 0)
            {
                return;
            }

            RestoreFromHistory(state, state.Redo, state.Undo);
        }

        /// <summary>Geri al / ileri al: "from" listesinin sonundaki hâli yükler, şimdiki hâli "to" listesine koyar.</summary>
        private void RestoreFromHistory(SheetState state, List<string> from, List<string> to)
        {
            CaptureWidths();

            var columnsBefore = _current.ColumnCount;

            to.Add(state.Last);
            state.Last = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);

            _current.RestoreSnapshot(state.Last);

            if (_current.ColumnCount != columnsBefore)
            {
                state.Filters.Clear();
            }

            MarkDirty();
            RebuildGrid();
            UpdateStatus();
        }

        // =====================================================================
        // FORMÜL ÇUBUĞU (Ad kutusu + fx kutusu)
        // =====================================================================

        private bool TryGetCurrentPosition(out CellPos pos)
        {
            pos = new CellPos();

            if (_current == null)
            {
                return false;
            }

            var current = SheetGrid.CurrentCell;
            var row = current.Item as NoteRow;
            var column = current.Column as NoteColumn;

            // Tablo odağı kaybedince (formül kutusuna tıklayınca) CurrentCell boşalabilir; o zaman seçili hücreye bakılır.
            if (row == null || column == null)
            {
                foreach (var info in SheetGrid.SelectedCells)
                {
                    row = info.Item as NoteRow;
                    column = info.Column as NoteColumn;
                    if (row != null && column != null)
                    {
                        break;
                    }
                }
            }

            if (row != null && column != null && row.Number - 1 < _current.Rows.Count)
            {
                pos.Row = row.Number - 1;
                pos.Col = column.Index;

                _lastPosValid = true;
                _lastRow = pos.Row;
                _lastCol = pos.Col;
                return true;
            }

            // Hiçbiri yoksa en son seçilen hücre kullanılır.
            if (_lastPosValid && _lastRow < _current.Rows.Count && _lastCol < _current.ColumnCount)
            {
                pos.Row = _lastRow;
                pos.Col = _lastCol;
                return true;
            }

            return false;
        }

        private bool IsCellEditorFocused()
        {
            var box = Keyboard.FocusedElement as TextBox;
            return box != null &&
                   !ReferenceEquals(box, FormulaBox) &&
                   !ReferenceEquals(box, NameBox) &&
                   FindAncestor<DataGrid>(box) != null;
        }

        /// <summary>Ad kutusuna seçili hücrenin adresini, formül kutusuna hücrenin içeriğini (formül dahil) yazar.</summary>
        private void UpdateFormulaBar()
        {
            if (_syncingBar)
            {
                return;
            }

            _syncingBar = true;
            try
            {
                CellPos pos;
                if (!TryGetCurrentPosition(out pos))
                {
                    if (!NameBox.IsKeyboardFocusWithin)
                    {
                        NameBox.Text = "";
                    }

                    if (!FormulaBox.IsKeyboardFocusWithin)
                    {
                        FormulaBox.Text = "";
                        FormulaBox.IsEnabled = false;
                    }

                    return;
                }

                if (!NameBox.IsKeyboardFocusWithin)
                {
                    NameBox.Text = NoteFormula.ColumnName(pos.Col) + (pos.Row + 1).ToString(CultureInfo.InvariantCulture);
                }

                FormulaBox.IsEnabled = true;

                // Kullanıcı çubuğa ya da hücrenin içine yazıyorsa yazdığı metin ezilmez.
                if (!FormulaBox.IsKeyboardFocusWithin && !IsCellEditorFocused())
                {
                    var cell = _current.GetCell(pos.Row, pos.Col);
                    var raw = cell == null ? "" : cell.Raw;
                    if (FormulaBox.Text != raw)
                    {
                        FormulaBox.Text = raw;
                    }
                    _barDirty = false;
                }
            }
            finally
            {
                _syncingBar = false;
            }
        }

        private void SheetGrid_CurrentCellChanged(object sender, EventArgs e)
        {
            if (!_rebuilding)
            {
                UpdateFormulaBar();
            }
        }

        private void SheetGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            // Hücrenin içine yazarken formül çubuğu da birlikte güncellenir.
            var box = e.EditingElement as TextBox;
            if (box != null)
            {
                box.TextChanged += EditBox_TextChanged;
            }
        }

        private void EditBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null)
            {
                return;
            }

            var wasSyncing = _syncingBar;
            _syncingBar = true;
            try
            {
                if (FormulaBox.Text != box.Text)
                {
                    FormulaBox.Text = box.Text;
                }
            }
            finally
            {
                _syncingBar = wasSyncing;
            }
        }

        private void SheetGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // Düzenleme bitince (onay ya da Esc) çubuk hücrenin gerçek içeriğine döner.
            Dispatcher.BeginInvoke(new Action(UpdateFormulaBar), DispatcherPriority.Background);
        }

        private void FormulaBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_syncingBar)
            {
                _barDirty = true;
            }
        }

        private void FormulaBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            CellPos pos;
            _barHasTarget = TryGetCurrentPosition(out pos);
            if (_barHasTarget)
            {
                _barRow = pos.Row;
                _barCol = pos.Col;
            }
        }

        private void FormulaBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // Pencere başka programa geçtiyse yazılan metin korunur.
            if (e.NewFocus == null)
            {
                return;
            }

            CommitFormulaBar();
            Dispatcher.BeginInvoke(new Action(UpdateFormulaBar), DispatcherPriority.Input);
        }

        /// <summary>Formül kutusunda yazılanı, yazmaya başlanan hücreye işler.</summary>
        private void CommitFormulaBar()
        {
            if (!_barDirty)
            {
                return;
            }

            _barDirty = false;

            if (!_barHasTarget || _current == null)
            {
                return;
            }

            var text = FormulaBox.Text;
            var row = _barRow;
            var col = _barCol;

            ApplyChange(delegate
            {
                _current.SetRaw(row, col, text);
                return false;
            });
        }

        private void FormulaBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;

                var row = _barRow;
                var col = _barCol;
                var hadTarget = _barHasTarget;

                CommitFormulaBar();

                if (hadTarget && _current != null)
                {
                    // Excel gibi: onaylayınca bir alt hücreye geçilir.
                    var nextRow = row + 1 < _current.Rows.Count ? row + 1 : row;
                    SelectRange(nextRow, col, nextRow, col);
                }

                SheetGrid.Focus();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;

                _syncingBar = true;
                try
                {
                    var cell = _barHasTarget && _current != null ? _current.GetCell(_barRow, _barCol) : null;
                    FormulaBox.Text = cell == null ? "" : cell.Raw;
                }
                finally
                {
                    _syncingBar = false;
                }

                _barDirty = false;
                SheetGrid.Focus();
            }
        }

        private void NameBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(delegate { NameBox.SelectAll(); }), DispatcherPriority.Input);
        }

        private void NameBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                JumpToAddress(NameBox.Text);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                SheetGrid.Focus();
                UpdateFormulaBar();
            }
        }

        /// <summary>Ad kutusuna yazılan adrese (D15 ya da A1:C5) gider; gerekirse tabloyu büyütür.</summary>
        private void JumpToAddress(string text)
        {
            if (_current == null)
            {
                return;
            }

            var match = AddressRegex.Match(text ?? "");
            if (!match.Success)
            {
                SheetGrid.Focus();
                UpdateFormulaBar();
                return;
            }

            var col1 = NoteFormula.ColumnIndex(match.Groups[1].Value);
            var row1 = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) - 1;
            var col2 = col1;
            var row2 = row1;
            if (match.Groups[3].Success)
            {
                col2 = NoteFormula.ColumnIndex(match.Groups[3].Value);
                row2 = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) - 1;
            }

            var firstRow = Math.Min(row1, row2);
            var lastRow = Math.Max(row1, row2);
            var firstCol = Math.Min(col1, col2);
            var lastCol = Math.Max(col1, col2);

            if (firstRow < 0 || lastRow >= NoteSheet.MaxRows || lastCol >= NoteSheet.MaxColumns)
            {
                MessageBox.Show(this,
                    "Bu adres sayfa sınırının dışında. Bir sayfada en fazla " + NoteSheet.MaxRows.ToString("N0") +
                    " satır ve " + NoteSheet.MaxColumns + " sütun olabilir.",
                    "Notlar", MessageBoxButton.OK, MessageBoxImage.Information);
                SheetGrid.Focus();
                UpdateFormulaBar();
                return;
            }

            GrowTo(lastRow + 1, lastCol + 1);
            SelectRange(firstRow, firstCol, lastRow, lastCol);
        }

        /// <summary>Tabloyu en az bu kadar satır ve sütuna büyütür (tabloyu baştan kurmadan).</summary>
        private void GrowTo(int rows, int columns)
        {
            if (rows <= _current.Rows.Count && columns <= _current.ColumnCount)
            {
                return;
            }

            CommitGridEdit();

            var oldColumns = _current.ColumnCount;
            var growRows = rows > _current.Rows.Count;

            _current.EnsureSize(Math.Max(rows, _current.Rows.Count), Math.Max(columns, _current.ColumnCount));

            for (var c = oldColumns; c < _current.ColumnCount; c++)
            {
                SheetGrid.Columns.Add(new NoteColumn(c, DefaultColumnWidth, OnFilterButtonCreated, OnFilterButtonClicked));
            }

            if (growRows && _view != null)
            {
                _view.Refresh();
            }

            State.Last = _current.Snapshot();
            MarkDirty();
        }

        // =====================================================================
        // FORMÜL YAZARKEN HÜCREYE TIKLAYINCA ADRESİ EKLEME
        // =====================================================================

        /// <summary>Şu an formül yazılan kutu: formül çubuğu ya da hücrenin içindeki yazı kutusu.</summary>
        private TextBox GetFormulaEditor()
        {
            if (FormulaBox.IsKeyboardFocusWithin)
            {
                return FormulaBox;
            }

            var focused = Keyboard.FocusedElement as TextBox;
            if (focused != null && !ReferenceEquals(focused, NameBox) && FindAncestor<DataGrid>(focused) != null)
            {
                return focused;
            }

            return null;
        }

        /// <summary>
        /// Excel'deki gibi: yalnızca "=" ile başlayan metinde ve imleç bir işleçten (= + - * / ^ ( ; &amp; &lt; &gt;) sonra
        /// duruyorsa tıklanan hücrenin adresi eklenir. Az önce eklenen adresin üstüne yeniden tıklanırsa o adres değişir.
        /// </summary>
        private bool CanInsertReference(TextBox editor, out int start, out int length)
        {
            start = 0;
            length = 0;

            var text = editor.Text ?? "";
            if (!text.StartsWith("=", StringComparison.Ordinal))
            {
                return false;
            }

            var caret = Math.Min(editor.SelectionStart, text.Length);

            if (ReferenceEquals(_refEditor, editor) &&
                _refLength > 0 &&
                editor.SelectionLength == 0 &&
                caret == _refStart + _refLength &&
                caret <= text.Length &&
                string.CompareOrdinal(text, _refStart, _refText, 0, _refLength) == 0)
            {
                start = _refStart;
                length = _refLength;
                return true;
            }

            var before = text.Substring(0, caret).TrimEnd();
            if (before.Length == 0)
            {
                return false;
            }

            if ("=+-*/^(;,&<>".IndexOf(before[before.Length - 1]) < 0)
            {
                return false;
            }

            start = caret;
            length = Math.Min(editor.SelectionLength, text.Length - caret);
            return true;
        }

        private void ReplaceEditorSpan(TextBox editor, int start, int length, string newText)
        {
            var text = editor.Text ?? "";
            start = Math.Min(start, text.Length);
            length = Math.Min(length, text.Length - start);

            editor.Text = text.Remove(start, length).Insert(start, newText);
            editor.CaretIndex = start + newText.Length;
            editor.SelectionLength = 0;

            _refEditor = editor;
            _refStart = start;
            _refLength = newText.Length;
            _refText = newText;
        }

        private static string CellAddress(int row, int col)
        {
            return NoteFormula.ColumnName(col) + (row + 1).ToString(CultureInfo.InvariantCulture);
        }

        private static string RangeAddress(int row1, int col1, int row2, int col2)
        {
            if (row1 == row2 && col1 == col2)
            {
                return CellAddress(row1, col1);
            }

            return CellAddress(Math.Min(row1, row2), Math.Min(col1, col2)) + ":" +
                   CellAddress(Math.Max(row1, row2), Math.Max(col1, col2));
        }

        private void SheetGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_current == null)
            {
                return;
            }

            var editor = GetFormulaEditor();
            if (editor == null)
            {
                return;
            }

            var source = e.OriginalSource as DependencyObject;

            // Düzenleme kutusunun kendisine ya da filtre okuna tıklanırsa her zamanki gibi davranır.
            if (FindAncestor<TextBox>(source) != null || FindAncestor<Button>(source) != null)
            {
                return;
            }

            var cell = FindAncestor<DataGridCell>(source);
            if (cell == null)
            {
                return;
            }

            var row = cell.DataContext as NoteRow;
            var column = cell.Column as NoteColumn;
            if (row == null || column == null)
            {
                return;
            }

            int start;
            int length;
            if (!CanInsertReference(editor, out start, out length))
            {
                return;
            }

            ReplaceEditorSpan(editor, start, length, CellAddress(row.Number - 1, column.Index));

            _dragging = true;
            _dragAnchorRow = row.Number - 1;
            _dragAnchorCol = column.Index;
            _dragLastRow = _dragAnchorRow;
            _dragLastCol = _dragAnchorCol;

            // Tıklamayı tablo işlemesin: yazı kutusu odakta kalır, seçili hücre değişmez.
            e.Handled = true;
            editor.Focus();
        }

        private void SheetGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging)
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed || _refEditor == null)
            {
                _dragging = false;
                return;
            }

            var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
            if (cell == null)
            {
                return;
            }

            var row = cell.DataContext as NoteRow;
            var column = cell.Column as NoteColumn;
            if (row == null || column == null)
            {
                return;
            }

            var r = row.Number - 1;
            var c = column.Index;
            if (r == _dragLastRow && c == _dragLastCol)
            {
                return;
            }

            _dragLastRow = r;
            _dragLastCol = c;

            // Basılı tutup sürüklerken aralık yazılır: A1:C5
            ReplaceEditorSpan(_refEditor, _refStart, _refLength, RangeAddress(_dragAnchorRow, _dragAnchorCol, r, c));
        }

        private void SheetGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;
        }

        // =====================================================================
        // KAYDIRDIKÇA OTOMATİK BÜYÜME (Excel gibi)
        // =====================================================================

        private void SheetGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_rebuilding || _growing || _current == null)
            {
                return;
            }

            // Hücre düzenleme kutusunun kendi kaydırıcısını değil, tablonun kaydırıcısını dinle.
            var viewer = e.OriginalSource as ScrollViewer;
            if (viewer == null || !ReferenceEquals(viewer.TemplatedParent, SheetGrid))
            {
                return;
            }

            var addRows = e.VerticalChange > 0 &&
                          viewer.ScrollableHeight > 0 &&
                          viewer.VerticalOffset >= viewer.ScrollableHeight - NearBottomRows;

            var addColumns = e.HorizontalChange > 0 &&
                             viewer.ScrollableWidth > 0 &&
                             viewer.HorizontalOffset >= viewer.ScrollableWidth - NearRightPixels;

            if (addRows || addColumns)
            {
                GrowFromScroll(viewer, addRows, addColumns);
                return;
            }

            // Geri yukarı / sola kaydırınca, veri girilmemiş fazla satır ve sütunlar silinir.
            var trimRows = e.VerticalChange < 0;
            var trimColumns = e.HorizontalChange < 0;
            if (trimRows || trimColumns)
            {
                TrimFromScroll(viewer, trimRows, trimColumns);
            }
        }

        private void GrowFromScroll(ScrollViewer viewer, bool addRows, bool addColumns)
        {
            if (addRows && _current.Rows.Count >= NoteSheet.MaxRows)
            {
                addRows = false;
            }

            if (addColumns && _current.ColumnCount >= NoteSheet.MaxColumns)
            {
                addColumns = false;
            }

            if (!addRows && !addColumns)
            {
                return;
            }

            _growing = true;

            // Düzenleme sürerken liste yenilenemez; önce onaylanır.
            CommitGridEdit();

            var verticalOffset = viewer.VerticalOffset;
            var horizontalOffset = viewer.HorizontalOffset;
            var oldColumns = _current.ColumnCount;

            _current.EnsureSize(
                _current.Rows.Count + (addRows ? GrowRows : 0),
                _current.ColumnCount + (addColumns ? GrowColumns : 0));

            // Yeni sütunlar tabloya doğrudan eklenir; tablo baştan kurulmaz, kaydırma yeri bozulmaz.
            for (var c = oldColumns; c < _current.ColumnCount; c++)
            {
                SheetGrid.Columns.Add(new NoteColumn(c, DefaultColumnWidth, OnFilterButtonCreated, OnFilterButtonClicked));
            }

            if (addRows && _view != null)
            {
                _view.Refresh();
            }

            // Boş satır/sütun eklemek geri alma adımı sayılmaz; yalnızca kayıt güncellenir.
            State.Last = _current.Snapshot();
            MarkDirty();

            Dispatcher.BeginInvoke(new Action(delegate
            {
                viewer.ScrollToVerticalOffset(verticalOffset);
                viewer.ScrollToHorizontalOffset(horizontalOffset);
                _growing = false;
            }), DispatcherPriority.Loaded);
        }

        /// <summary>Veri olmayan sondaki satır/sütunları siler, ama ekranda görünen yerin biraz altında/sağında pay bırakır.</summary>
        private void TrimFromScroll(ScrollViewer viewer, bool rows, bool columns)
        {
            var keepRows = -1;
            var keepColumns = -1;

            if (rows && _view != null && _current.Rows.Count > NoteSheet.DefaultRowCount)
            {
                var lastVisible = (int)Math.Min(viewer.VerticalOffset + viewer.ViewportHeight, _view.Count) - 1;
                var item = lastVisible >= 0 ? _view.GetItemAt(lastVisible) as NoteRow : null;
                var bottomRow = item != null ? item.Number - 1 : 0;

                var keep = Math.Max(NoteSheet.DefaultRowCount, bottomRow + 1 + NearBottomRows + 10);
                if (keep < _current.Rows.Count)
                {
                    keep = Math.Max(keep, _current.LastUsedRow() + 1);
                    if (keep < _current.Rows.Count)
                    {
                        keepRows = keep;
                    }
                }
            }

            if (columns && _current.ColumnCount > NoteSheet.DefaultColumnCount)
            {
                var edge = viewer.HorizontalOffset + viewer.ViewportWidth;
                double x = 0;
                var edgeColumn = SheetGrid.Columns.Count - 1;
                for (var i = 0; i < SheetGrid.Columns.Count; i++)
                {
                    x += SheetGrid.Columns[i].ActualWidth;
                    if (x >= edge)
                    {
                        edgeColumn = i;
                        break;
                    }
                }

                var keep = Math.Max(NoteSheet.DefaultColumnCount, edgeColumn + 1 + 3);
                if (keep < _current.ColumnCount)
                {
                    keep = Math.Max(keep, _current.LastUsedColumn() + 1);
                    if (keep < _current.ColumnCount)
                    {
                        keepColumns = keep;
                    }
                }
            }

            if (keepRows < 0 && keepColumns < 0)
            {
                return;
            }

            _growing = true;
            CommitGridEdit();

            var verticalOffset = viewer.VerticalOffset;
            var horizontalOffset = viewer.HorizontalOffset;

            _current.TrimTo(
                keepRows >= 0 ? keepRows : _current.Rows.Count,
                keepColumns >= 0 ? keepColumns : _current.ColumnCount);

            while (SheetGrid.Columns.Count > _current.ColumnCount)
            {
                SheetGrid.Columns.RemoveAt(SheetGrid.Columns.Count - 1);
            }

            var filtersChanged = RemoveFiltersBeyondColumns();

            if (filtersChanged)
            {
                ApplyFilters();
            }
            else if (keepRows >= 0 && _view != null)
            {
                _view.Refresh();
            }

            State.Last = _current.Snapshot();
            MarkDirty();
            UpdateToolbar();

            Dispatcher.BeginInvoke(new Action(delegate
            {
                viewer.ScrollToVerticalOffset(verticalOffset);
                viewer.ScrollToHorizontalOffset(horizontalOffset);
                _growing = false;
            }), DispatcherPriority.Loaded);
        }

        /// <summary>Silinen sütunlara ait filtreleri kaldırır. Bir şey kaldırıldıysa true döner.</summary>
        private bool RemoveFiltersBeyondColumns()
        {
            var stale = State.Filters.Keys.Where(k => k >= _current.ColumnCount).ToList();
            foreach (var key in stale)
            {
                State.Filters.Remove(key);
            }

            return stale.Count > 0;
        }

        /// <summary>Sayfa kapanırken ya da değişirken, veri girilmemiş fazla satır/sütunlar (varsayılan boyutun üstü) atılır.</summary>
        private void TrimCurrentToData()
        {
            if (_current == null || !_states.ContainsKey(_current))
            {
                return;
            }

            var rows = Math.Max(NoteSheet.DefaultRowCount, _current.LastUsedRow() + 1);
            var columns = Math.Max(NoteSheet.DefaultColumnCount, _current.LastUsedColumn() + 1);

            if (_current.TrimTo(rows, columns))
            {
                RemoveFiltersBeyondColumns();
                State.Last = _current.Snapshot();
                _dirty = true;
            }
        }

        // =====================================================================
        // SEÇİM
        // =====================================================================

        private List<CellPos> GetSelectedPositions()
        {
            var list = new List<CellPos>();

            foreach (var info in SheetGrid.SelectedCells)
            {
                var row = info.Item as NoteRow;
                var column = info.Column as NoteColumn;
                if (row == null || column == null)
                {
                    continue;
                }

                list.Add(new CellPos { Row = row.Number - 1, Col = column.Index });
            }

            if (list.Count == 0)
            {
                var current = SheetGrid.CurrentCell;
                var row = current.Item as NoteRow;
                var column = current.Column as NoteColumn;
                if (row != null && column != null)
                {
                    list.Add(new CellPos { Row = row.Number - 1, Col = column.Index });
                }
            }

            return list;
        }

        private void SelectRange(int row1, int col1, int row2, int col2)
        {
            if (_current == null || SheetGrid.Columns.Count == 0)
            {
                return;
            }

            row2 = Math.Min(row2, _current.Rows.Count - 1);
            col2 = Math.Min(col2, SheetGrid.Columns.Count - 1);
            if (row1 > row2 || col1 > col2)
            {
                return;
            }

            SheetGrid.SelectedCells.Clear();

            var first = _current.Rows[row1];
            SheetGrid.CurrentCell = new DataGridCellInfo(first, SheetGrid.Columns[col1]);

            // Çok büyük seçimlerde tablo yavaşlamasın diye yalnızca ilk hücre seçilir.
            if ((long)(row2 - row1 + 1) * (col2 - col1 + 1) <= 2000)
            {
                for (var r = row1; r <= row2; r++)
                {
                    var row = _current.Rows[r];
                    if (_view != null && !_view.Contains(row))
                    {
                        continue;
                    }

                    for (var c = col1; c <= col2; c++)
                    {
                        SheetGrid.SelectedCells.Add(new DataGridCellInfo(row, SheetGrid.Columns[c]));
                    }
                }
            }
            else
            {
                SheetGrid.SelectedCells.Add(new DataGridCellInfo(first, SheetGrid.Columns[col1]));
            }

            SheetGrid.ScrollIntoView(first, SheetGrid.Columns[col1]);
            SheetGrid.Focus();
        }

        private void SheetGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            if (_rebuilding)
            {
                return;
            }

            UpdateStatus();
        }

        private void SheetGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Sağ tıklanan hücre seçili değilse önce onu seç (Excel'deki gibi).
            var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
            if (cell == null || cell.IsSelected)
            {
                return;
            }

            SheetGrid.SelectedCells.Clear();
            SheetGrid.CurrentCell = new DataGridCellInfo(cell);
            cell.IsSelected = true;
        }

        private static T FindAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            var node = start;
            while (node != null)
            {
                var found = node as T;
                if (found != null)
                {
                    return found;
                }

                node = node is Visual || node is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(node)
                    : LogicalTreeHelper.GetParent(node);
            }

            return null;
        }

        private void UpdateStatus()
        {
            UpdateFormulaBar();

            if (_current == null)
            {
                StatusText.Text = "";
                return;
            }

            var selected = GetSelectedPositions();
            if (selected.Count == 0)
            {
                StatusText.Text = "Hücreleri seçince toplam, ortalama ve adet burada görünür.";
                return;
            }

            double sum = 0, min = 0, max = 0;
            var count = 0;

            foreach (var pos in selected)
            {
                var cell = _current.GetCell(pos.Row, pos.Col);
                if (cell == null || cell.Value.Kind != NoteValueKind.Number)
                {
                    continue;
                }

                var number = cell.Value.Number;
                if (count == 0)
                {
                    min = number;
                    max = number;
                }
                else
                {
                    if (number < min) { min = number; }
                    if (number > max) { max = number; }
                }

                sum += number;
                count++;
            }

            var parts = new List<string> { selected.Count.ToString("N0", NoteFormula.TurkishCulture) + " hücre seçili" };
            if (count > 0)
            {
                parts.Add("Toplam: " + NoteFormula.FormatNumberGrouped(sum));
                parts.Add("Ortalama: " + NoteFormula.FormatNumberGrouped(sum / count));
                parts.Add("Adet: " + count.ToString("N0", NoteFormula.TurkishCulture));
                parts.Add("Min: " + NoteFormula.FormatNumberGrouped(min));
                parts.Add("Maks: " + NoteFormula.FormatNumberGrouped(max));
            }

            StatusText.Text = string.Join("        ", parts);
        }

        private void UpdateToolbar()
        {
            var hasSheet = _current != null && _states.ContainsKey(_current);

            UndoButton.IsEnabled = hasSheet && State.Undo.Count > 0;
            RedoButton.IsEnabled = hasSheet && State.Redo.Count > 0;
            ClearFiltersButton.IsEnabled = hasSheet && State.Filters.Count > 0;
        }

        // =====================================================================
        // KLAVYE: KOPYALA / KES / YAPIŞTIR / SİL / GERİ AL
        // =====================================================================

        private void SheetGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Hücre düzenlenirken kutunun kendi kısayolları çalışsın (kopyala, yapıştır, geri al).
            if (e.OriginalSource is TextBox)
            {
                return;
            }

            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

            if (ctrl && e.Key == Key.C)
            {
                CopySelection(false);
                e.Handled = true;
            }
            else if (ctrl && e.Key == Key.X)
            {
                CopySelection(true);
                e.Handled = true;
            }
            else if (ctrl && e.Key == Key.V)
            {
                PasteFromClipboard();
                e.Handled = true;
            }
            else if (ctrl && e.Key == Key.Z)
            {
                Undo();
                e.Handled = true;
            }
            else if (ctrl && e.Key == Key.Y)
            {
                Redo();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                ClearPositions(GetSelectedPositions());
                e.Handled = true;
            }
        }

        private string BuildCopyText(List<CellPos> selected)
        {
            // Yalnızca seçili satırlar kopyalanır; filtre yüzünden gizli satırlar zaten seçilemez.
            var rows = selected.Select(p => p.Row).Distinct().OrderBy(r => r).ToList();
            var minCol = selected.Min(p => p.Col);
            var maxCol = selected.Max(p => p.Col);

            var lines = new List<string[]>();
            foreach (var r in rows)
            {
                var cells = new string[maxCol - minCol + 1];
                for (var c = minCol; c <= maxCol; c++)
                {
                    var cell = _current.GetCell(r, c);
                    cells[c - minCol] = cell == null ? "" : cell.Display;
                }
                lines.Add(cells);
            }

            return NoteClipboard.FormatTsv(lines);
        }

        private void CopySelection(bool cut)
        {
            if (_current == null)
            {
                return;
            }

            CommitGridEdit();

            var selected = GetSelectedPositions();
            if (selected.Count == 0)
            {
                return;
            }

            try
            {
                Clipboard.SetDataObject(BuildCopyText(selected), true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Panoya kopyalanamadı:\n" + ex.Message, "Notlar",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (cut)
            {
                ClearPositions(selected);
            }
        }

        private void ClearPositions(List<CellPos> positions)
        {
            if (_current == null || positions.Count == 0)
            {
                return;
            }

            ApplyChange(delegate
            {
                foreach (var pos in positions)
                {
                    _current.SetRaw(pos.Row, pos.Col, "");
                }
                return false;
            });
        }

        private void PasteFromClipboard()
        {
            if (_current == null)
            {
                return;
            }

            CommitGridEdit();

            string text;
            try
            {
                if (!Clipboard.ContainsText())
                {
                    return;
                }
                text = Clipboard.GetText();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Panodan okunamadı:\n" + ex.Message, "Notlar",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var data = NoteClipboard.ParseTsv(text);
            if (data.Count == 0)
            {
                return;
            }

            var selected = GetSelectedPositions();
            if (selected.Count == 0)
            {
                return;
            }

            var anchorRow = selected.Min(p => p.Row);
            var anchorCol = selected.Min(p => p.Col);
            var width = Math.Max(1, data.Max(r => r.Length));

            // Tek değer, birden çok hücre seçiliyken: hepsine yazılır (Excel gibi).
            var fill = data.Count == 1 && width == 1 && selected.Count > 1;

            var needRows = anchorRow + data.Count;
            var needCols = anchorCol + width;
            if (!fill && (needRows > NoteSheet.MaxRows || needCols > NoteSheet.MaxColumns))
            {
                MessageBox.Show(this,
                    "Yapıştırılan veri çok büyük. Bir sayfada en fazla " + NoteSheet.MaxRows.ToString("N0") +
                    " satır ve " + NoteSheet.MaxColumns + " sütun olabilir.",
                    "Notlar", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var grew = false;

            ApplyChange(delegate
            {
                if (fill)
                {
                    foreach (var pos in selected)
                    {
                        _current.SetRaw(pos.Row, pos.Col, data[0][0]);
                    }
                    return false;
                }

                grew = _current.EnsureSize(needRows, needCols);

                for (var r = 0; r < data.Count; r++)
                {
                    for (var c = 0; c < data[r].Length; c++)
                    {
                        _current.SetRaw(anchorRow + r, anchorCol + c, data[r][c]);
                    }
                }

                return grew;
            });

            if (!fill)
            {
                SelectRange(anchorRow, anchorCol, anchorRow + data.Count - 1, anchorCol + width - 1);
            }
        }

        // =====================================================================
        // SAĞ TIK MENÜSÜ
        // =====================================================================

        private void CutMenu_Click(object sender, RoutedEventArgs e)
        {
            CopySelection(true);
        }

        private void CopyMenu_Click(object sender, RoutedEventArgs e)
        {
            CopySelection(false);
        }

        private void PasteMenu_Click(object sender, RoutedEventArgs e)
        {
            PasteFromClipboard();
        }

        private void ClearMenu_Click(object sender, RoutedEventArgs e)
        {
            ClearPositions(GetSelectedPositions());
        }

        private void InsertRowAboveMenu_Click(object sender, RoutedEventArgs e)
        {
            InsertRow(false);
        }

        private void InsertRowBelowMenu_Click(object sender, RoutedEventArgs e)
        {
            InsertRow(true);
        }

        private void InsertRow(bool below)
        {
            var selected = GetSelectedPositions();
            if (_current == null || selected.Count == 0)
            {
                return;
            }

            var at = below ? selected.Max(p => p.Row) + 1 : selected.Min(p => p.Row);

            ApplyChange(delegate
            {
                _current.InsertRows(at, 1);
                return true;
            });
        }

        private void DeleteRowsMenu_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedPositions();
            if (_current == null || selected.Count == 0)
            {
                return;
            }

            var rows = selected.Select(p => p.Row).Distinct().ToList();

            ApplyChange(delegate
            {
                _current.DeleteRows(rows);
                return true;
            });
        }

        private void InsertColumnLeftMenu_Click(object sender, RoutedEventArgs e)
        {
            InsertColumn(false);
        }

        private void InsertColumnRightMenu_Click(object sender, RoutedEventArgs e)
        {
            InsertColumn(true);
        }

        private void InsertColumn(bool right)
        {
            var selected = GetSelectedPositions();
            if (_current == null || selected.Count == 0)
            {
                return;
            }

            var at = right ? selected.Max(p => p.Col) + 1 : selected.Min(p => p.Col);

            ApplyChange(delegate
            {
                _current.InsertColumns(at, 1);
                ShiftFiltersForInsert(at);
                return true;
            });
        }

        private void DeleteColumnsMenu_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedPositions();
            if (_current == null || selected.Count == 0)
            {
                return;
            }

            var columns = selected.Select(p => p.Col).Distinct().OrderBy(c => c).ToList();

            ApplyChange(delegate
            {
                _current.DeleteColumns(columns);
                ShiftFiltersForDelete(columns);
                return true;
            });
        }

        private void ShiftFiltersForInsert(int at)
        {
            var filters = State.Filters;
            var moved = new Dictionary<int, HashSet<string>>();

            foreach (var pair in filters)
            {
                moved[pair.Key >= at ? pair.Key + 1 : pair.Key] = pair.Value;
            }

            filters.Clear();
            foreach (var pair in moved)
            {
                filters[pair.Key] = pair.Value;
            }
        }

        private void ShiftFiltersForDelete(List<int> removed)
        {
            var filters = State.Filters;
            var moved = new Dictionary<int, HashSet<string>>();

            foreach (var pair in filters)
            {
                if (removed.Contains(pair.Key))
                {
                    continue;
                }

                var shift = removed.Count(c => c < pair.Key);
                moved[pair.Key - shift] = pair.Value;
            }

            filters.Clear();
            foreach (var pair in moved)
            {
                filters[pair.Key] = pair.Value;
            }
        }

        // =====================================================================
        // ARAÇ ÇUBUĞU
        // =====================================================================

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            Undo();
            SheetGrid.Focus();
        }

        private void RedoButton_Click(object sender, RoutedEventArgs e)
        {
            Redo();
            SheetGrid.Focus();
        }

        private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null)
            {
                return;
            }

            State.Filters.Clear();
            ApplyFilters();
        }

        // =====================================================================
        // FİLTRE
        // =====================================================================

        private bool FilterRow(object item)
        {
            var row = item as NoteRow;
            if (row == null || row.IsHeader)
            {
                return true;
            }

            foreach (var pair in State.Filters)
            {
                var cell = row[pair.Key];
                var value = cell == null ? "" : cell.Display;
                if (!pair.Value.Contains(value))
                {
                    return false;
                }
            }

            return true;
        }

        private void ApplyFilters()
        {
            if (_view == null || _current == null)
            {
                return;
            }

            CommitGridEdit();

            _view.Filter = State.Filters.Count > 0 ? (Predicate<object>)FilterRow : null;
            _view.Refresh();

            UpdateAllFilterButtons();
            UpdateToolbar();
            UpdateStatus();
        }

        private void OnFilterButtonCreated(int column, Button button)
        {
            // Düğme ekrana girince listeye alınır, çıkınca bırakılır (kaydırmada sürekli yenilenir).
            button.Loaded += delegate
            {
                _filterButtons.Add(button);
                UpdateFilterButton(button);
            };
            button.Unloaded += delegate { _filterButtons.Remove(button); };
        }

        private void UpdateAllFilterButtons()
        {
            foreach (var button in _filterButtons.ToList())
            {
                UpdateFilterButton(button);
            }
        }

        private void UpdateFilterButton(Button button)
        {
            if (_current == null || !_states.ContainsKey(_current) || !(button.Tag is int))
            {
                return;
            }

            var active = State.Filters.ContainsKey((int)button.Tag);
            button.Background = active ? new SolidColorBrush(Color.FromRgb(0xBF, 0xDB, 0xF7)) : SystemColors.ControlBrush;
            button.Foreground = active ? new SolidColorBrush(Color.FromRgb(0x1F, 0x5F, 0xA8)) : Brushes.Black;
            button.ToolTip = active ? "Filtre uygulanmış" : "Filtrele / sırala";
        }

        private void OnFilterButtonClicked(int column, Button button)
        {
            if (_current == null)
            {
                return;
            }

            CommitGridEdit();

            var state = State;

            // Sütundaki farklı değerler. 1. satır başlıktır, listeye girmez.
            var groups = new Dictionary<string, NoteValue>(NoteFormula.TextComparer);
            for (var r = 1; r < _current.Rows.Count; r++)
            {
                var cell = _current.Rows[r][column];
                if (cell == null)
                {
                    continue;
                }

                if (!groups.ContainsKey(cell.Display))
                {
                    groups[cell.Display] = cell.Value;
                }
            }

            var values = groups.OrderBy(p => p.Value, new ValueComparer()).Select(p => p.Key).ToList();

            HashSet<string> existing;
            state.Filters.TryGetValue(column, out existing);

            var dialog = new NoteFilterWindow(NoteFormula.ColumnName(column), values, existing) { Owner = this };
            PositionNear(dialog, button);

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            switch (dialog.Outcome)
            {
                case NoteFilterWindow.FilterOutcome.SortAscending:
                    ApplyChange(delegate
                    {
                        _current.SortByColumn(column, true);
                        return true;
                    });
                    break;

                case NoteFilterWindow.FilterOutcome.SortDescending:
                    ApplyChange(delegate
                    {
                        _current.SortByColumn(column, false);
                        return true;
                    });
                    break;

                case NoteFilterWindow.FilterOutcome.Apply:
                    state.Filters[column] = dialog.Allowed;
                    ApplyFilters();
                    break;

                case NoteFilterWindow.FilterOutcome.Clear:
                    state.Filters.Remove(column);
                    ApplyFilters();
                    break;
            }
        }

        /// <summary>Filtre penceresini, tıklanan okun hemen altına yerleştirir (ekranın dışına taşırmaz).</summary>
        private static void PositionNear(Window dialog, FrameworkElement anchor)
        {
            try
            {
                var point = anchor.PointToScreen(new Point(0, anchor.ActualHeight));

                var source = PresentationSource.FromVisual(anchor);
                if (source != null && source.CompositionTarget != null)
                {
                    point = source.CompositionTarget.TransformFromDevice.Transform(point);
                }

                var area = SystemParameters.WorkArea;
                var left = Math.Min(point.X, area.Right - dialog.Width);
                var top = Math.Min(point.Y, area.Bottom - dialog.Height);

                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Left = Math.Max(area.Left, left);
                dialog.Top = Math.Max(area.Top, top);
            }
            catch (Exception)
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
        }

        // =====================================================================
        // SEKMELER (SAYFALAR)
        // =====================================================================

        private void BuildTabs()
        {
            TabsPanel.Children.Clear();
            _tabBorders.Clear();

            foreach (var sheet in _sheets)
            {
                var tab = CreateTab(sheet);
                _tabBorders[sheet] = tab;
                TabsPanel.Children.Add(tab);
            }

            // + düğmesi hep son sekmenin hemen sağında durur.
            var add = new Button
            {
                Content = "+",
                Width = 30,
                Height = 22,
                Margin = new Thickness(8, 2, 4, 4),
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Padding = new Thickness(0),
                Focusable = false,
                ToolTip = "Yeni sayfa ekle"
            };
            add.Click += delegate { AddSheet(); };
            TabsPanel.Children.Add(add);

            RefreshTabVisuals();
        }

        private Border CreateTab(NoteSheet sheet)
        {
            var label = new TextBlock
            {
                Text = sheet.Name,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 3, 12, 3)
            };

            var border = new Border
            {
                Child = label,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xB5, 0xB5, 0xB5)),
                BorderThickness = new Thickness(1, 0, 1, 1),
                Margin = new Thickness(0, 0, 2, 0),
                Cursor = Cursors.Hand,
                ContextMenu = BuildTabMenu(sheet)
            };

            border.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                // Ad yazılırken (kutu açıkken) tıklamalar kutuya aittir.
                if (!(border.Child is TextBlock))
                {
                    return;
                }

                if (e.ClickCount >= 2)
                {
                    BeginRename(sheet);
                }
                else
                {
                    SelectSheet(sheet);
                }

                e.Handled = true;
            };

            border.PreviewMouseRightButtonDown += delegate
            {
                if (border.Child is TextBlock)
                {
                    SelectSheet(sheet);
                }
            };

            return border;
        }

        private void RefreshTabVisuals()
        {
            foreach (var pair in _tabBorders)
            {
                var selected = ReferenceEquals(pair.Key, _current);
                pair.Value.Background = selected
                    ? Brushes.White
                    : new SolidColorBrush(Color.FromRgb(0xD6, 0xD6, 0xD6));

                var label = pair.Value.Child as TextBlock;
                if (label != null)
                {
                    label.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
                    label.Foreground = selected
                        ? new SolidColorBrush(Color.FromRgb(0x1F, 0x5F, 0xA8))
                        : Brushes.Black;
                }
            }
        }

        private ContextMenu BuildTabMenu(NoteSheet sheet)
        {
            var index = _sheets.IndexOf(sheet);

            var menu = new ContextMenu();

            var rename = new MenuItem { Header = "Yeniden adlandır" };
            rename.Click += delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate { BeginRename(sheet); }), DispatcherPriority.Background);
            };

            var copy = new MenuItem { Header = "Sayfayı kopyala" };
            copy.Click += delegate { CopySheet(sheet); };

            var left = new MenuItem { Header = "Sola taşı", IsEnabled = index > 0 };
            left.Click += delegate { MoveSheet(sheet, -1); };

            var right = new MenuItem { Header = "Sağa taşı", IsEnabled = index >= 0 && index < _sheets.Count - 1 };
            right.Click += delegate { MoveSheet(sheet, 1); };

            var delete = new MenuItem { Header = "Sil" };
            delete.Click += delegate { DeleteSheet(sheet); };

            menu.Items.Add(rename);
            menu.Items.Add(copy);
            menu.Items.Add(new Separator());
            menu.Items.Add(left);
            menu.Items.Add(right);
            menu.Items.Add(new Separator());
            menu.Items.Add(delete);

            return menu;
        }

        /// <summary>Sekmenin adını yerinde değiştirmek için yazı kutusu açar. Enter onaylar, Esc vazgeçer.</summary>
        private void BeginRename(NoteSheet sheet)
        {
            Border border;
            if (!_tabBorders.TryGetValue(sheet, out border) || !(border.Child is TextBlock))
            {
                return;
            }

            var box = new TextBox
            {
                Text = sheet.Name,
                MinWidth = 90,
                MaxLength = 60,
                Margin = new Thickness(6, 2, 6, 2),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            border.Child = box;

            var finished = false;
            Action<bool> finish = delegate (bool commit)
            {
                if (finished)
                {
                    return;
                }
                finished = true;

                var text = box.Text.Trim();
                if (commit && text.Length > 0 && text != sheet.Name)
                {
                    sheet.Name = text;
                    PersistSheet(sheet);
                }

                BuildTabs();
                SheetGrid.Focus();
            };

            box.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter)
                {
                    finish(true);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    finish(false);
                    e.Handled = true;
                }
            };
            box.LostKeyboardFocus += delegate { finish(true); };

            Dispatcher.BeginInvoke(new Action(delegate
            {
                box.Focus();
                box.SelectAll();
            }), DispatcherPriority.Input);
        }

        private string NextSheetName()
        {
            var number = _sheets.Count + 1;
            while (_sheets.Any(s => string.Equals(s.Name, "Notlar" + number, StringComparison.CurrentCultureIgnoreCase)))
            {
                number++;
            }

            return "Notlar" + number;
        }

        private string UniqueName(string baseName)
        {
            var name = baseName;
            var number = 2;
            while (_sheets.Any(s => string.Equals(s.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            {
                name = baseName + " " + number;
                number++;
            }

            return name;
        }

        /// <summary>+ düğmesi: yeni boş sayfa ekler, adı hemen yazılabilir hâle getirir (varsayılan Notlar1, Notlar2...).</summary>
        private void AddSheet()
        {
            CommitGridEdit();
            SaveCurrent();

            var sheet = NoteSheet.CreateEmpty(NextSheetName());

            try
            {
                NoteRepository.Insert(sheet);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Yeni sayfa eklenemedi:\n" + ex.Message, "Notlar",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            HookSheet(sheet);
            _sheets.Add(sheet);

            BuildTabs();
            SelectSheet(sheet);

            Border border;
            if (_tabBorders.TryGetValue(sheet, out border))
            {
                border.BringIntoView();
            }

            BeginRename(sheet);
        }

        private void CopySheet(NoteSheet sheet)
        {
            CommitGridEdit();
            SaveCurrent();

            if (ReferenceEquals(sheet, _current))
            {
                CaptureWidths();
            }

            var copy = sheet.Clone(UniqueName(sheet.Name + " (kopya)"));

            try
            {
                NoteRepository.Insert(copy);
                HookSheet(copy);
                _sheets.Insert(_sheets.IndexOf(sheet) + 1, copy);
                NoteRepository.SaveOrder(_sheets);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Sayfa kopyalanamadı:\n" + ex.Message, "Notlar",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                if (!_sheets.Contains(copy))
                {
                    return;
                }
            }

            BuildTabs();
            SelectSheet(copy);
        }

        private void MoveSheet(NoteSheet sheet, int delta)
        {
            var index = _sheets.IndexOf(sheet);
            var target = index + delta;
            if (index < 0 || target < 0 || target >= _sheets.Count)
            {
                return;
            }

            _sheets.RemoveAt(index);
            _sheets.Insert(target, sheet);

            try
            {
                NoteRepository.SaveOrder(_sheets);
            }
            catch (Exception ex)
            {
                ShowSaveError(ex);
            }

            BuildTabs();
        }

        private void DeleteSheet(NoteSheet sheet)
        {
            var answer = MessageBox.Show(this,
                "\"" + sheet.Name + "\" sayfası ve içindeki tüm notlar silinsin mi?\n\nBu işlem geri alınamaz.",
                "Notlar", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            CommitGridEdit();

            var index = _sheets.IndexOf(sheet);
            var wasCurrent = ReferenceEquals(sheet, _current);

            try
            {
                NoteRepository.Delete(sheet.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Sayfa silinemedi:\n" + ex.Message, "Notlar",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _sheets.Remove(sheet);
            _states.Remove(sheet);

            if (wasCurrent)
            {
                // Silinen sayfa artık kaydedilmesin.
                _current = null;
                _dirty = false;
                _saveTimer.Stop();
            }

            // Hiç sayfa kalmadıysa boş bir tane açılır; Excel'de de en az bir sayfa vardır.
            if (_sheets.Count == 0)
            {
                var fresh = NoteSheet.CreateEmpty("Notlar1");
                try
                {
                    NoteRepository.Insert(fresh);
                }
                catch (Exception ex)
                {
                    ShowSaveError(ex);
                }

                HookSheet(fresh);
                _sheets.Add(fresh);
            }

            BuildTabs();

            if (wasCurrent)
            {
                SelectSheet(_sheets[Math.Min(index, _sheets.Count - 1)]);
            }
        }
    }
}