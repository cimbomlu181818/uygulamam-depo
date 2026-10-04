using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DEPO_DURUMU.Data
{
    /// <summary>Notlar sayfasındaki tek bir hücre. Raw: yazılan metin (formül dahil), Display: ekranda görünen.</summary>
    public class NoteCell : INotifyPropertyChanged
    {
        private string _raw = "";
        private string _display = "";
        private NoteValue _value = NoteValue.Empty;
        private bool _plainCached;

        internal NoteSheet Owner;
        internal int EvalState;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Raw
        {
            get { return _raw; }
            set
            {
                value = value ?? "";
                if (_raw == value)
                {
                    return;
                }

                _raw = value;
                _plainCached = false;
                Raise("Raw");

                if (Owner != null)
                {
                    Owner.NotifyEdited(this);
                }
            }
        }

        public string Display
        {
            get { return _display; }
        }

        public NoteValue Value
        {
            get { return _value; }
        }

        public bool IsFormula
        {
            get { return _raw.Length > 1 && _raw[0] == '='; }
        }

        internal bool PlainCached
        {
            get { return _plainCached; }
        }

        internal void SetComputed(NoteValue value, string display, bool plain)
        {
            _value = value;
            _plainCached = plain;

            if (_display != display)
            {
                _display = display;
                Raise("Display");
            }
        }

        private void Raise(string name)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }
    }

    /// <summary>Tablonun bir satırı. Ekranda satır numarası ve "1. satır başlıktır" bilgisi buradan gelir.</summary>
    public class NoteRow
    {
        public List<NoteCell> Cells = new List<NoteCell>();

        /// <summary>1 tabanlı satır numarası (Excel'deki gibi).</summary>
        public int Number { get; set; }

        public bool IsHeader
        {
            get { return Number == 1; }
        }

        public NoteCell this[int index]
        {
            get { return index >= 0 && index < Cells.Count ? Cells[index] : null; }
        }
    }

    /// <summary>
    /// Notlar defterindeki bir sayfa (alttaki sekmelerden biri). Hücreler, formül hesabı,
    /// satır/sütun ekleme-silme, sıralama ve kayıt metni burada. Ekrana (WPF) bağlı değildir.
    /// </summary>
    public class NoteSheet
    {
        public const int DefaultRowCount = 100;
        public const int DefaultColumnCount = 20;
        public const int MaxRows = 20000;
        public const int MaxColumns = 200;

        public int Id { get; set; }
        public string Name { get; set; }
        public int SortOrder { get; set; }

        public List<NoteRow> Rows { get; private set; }
        public int ColumnCount { get; private set; }

        /// <summary>Sütun genişlikleri (piksel). 0 = varsayılan genişlik.</summary>
        public List<double> ColumnWidths { get; private set; }

        /// <summary>Kullanıcı bir hücreyi düzenleyip onayladığında tetiklenir.</summary>
        public event Action<NoteCell> CellEdited;

        /// <summary>true iken hücre değişiklikleri CellEdited olayını tetiklemez (toplu işlemler için).</summary>
        public bool Quiet { get; set; }

        public NoteSheet()
        {
            Rows = new List<NoteRow>();
            ColumnWidths = new List<double>();
            ColumnCount = 0;
        }

        public static NoteSheet CreateEmpty(string name)
        {
            var sheet = new NoteSheet { Name = name };
            sheet.ResetEmpty(DefaultRowCount, DefaultColumnCount);
            return sheet;
        }

        internal void NotifyEdited(NoteCell cell)
        {
            if (Quiet)
            {
                return;
            }

            var handler = CellEdited;
            if (handler != null)
            {
                handler(cell);
            }
        }

        // ---------- YAPI ----------

        private NoteCell NewCell()
        {
            return new NoteCell { Owner = this };
        }

        private NoteRow NewRow()
        {
            var row = new NoteRow();
            for (var c = 0; c < ColumnCount; c++)
            {
                row.Cells.Add(NewCell());
            }
            return row;
        }

        private void ResetEmpty(int rows, int columns)
        {
            Rows.Clear();
            ColumnCount = columns;
            ColumnWidths = Enumerable.Repeat(0.0, columns).ToList();
            for (var r = 0; r < rows; r++)
            {
                Rows.Add(NewRow());
            }
            Renumber();
            Recalculate();
        }

        public void Renumber()
        {
            for (var i = 0; i < Rows.Count; i++)
            {
                Rows[i].Number = i + 1;
            }
        }

        public NoteCell GetCell(int row, int column)
        {
            if (row < 0 || row >= Rows.Count || column < 0 || column >= ColumnCount)
            {
                return null;
            }
            return Rows[row].Cells[column];
        }

        /// <summary>Hücreye metin yazar (olay tetiklenmez; çağıran Quiet'i açmış olmalıdır).</summary>
        public void SetRaw(int row, int column, string text)
        {
            var cell = GetCell(row, column);
            if (cell != null)
            {
                cell.Raw = text;
            }
        }

        /// <summary>Sayfayı en az bu kadar satır ve sütuna büyütür. Büyüdüyse true döner.</summary>
        public bool EnsureSize(int rows, int columns)
        {
            rows = Math.Min(rows, MaxRows);
            columns = Math.Min(columns, MaxColumns);
            var grew = false;

            while (ColumnCount < columns)
            {
                foreach (var row in Rows)
                {
                    row.Cells.Add(NewCell());
                }
                ColumnWidths.Add(0);
                ColumnCount++;
                grew = true;
            }

            while (Rows.Count < rows)
            {
                Rows.Add(NewRow());
                grew = true;
            }

            if (grew)
            {
                Renumber();
            }
            return grew;
        }

        // ---------- BOŞ SATIR / SÜTUN BUDAMA ----------

        /// <summary>İçinde veri olan son satırın sırası (0 tabanlı). Hiç veri yoksa -1.</summary>
        public int LastUsedRow()
        {
            for (var r = Rows.Count - 1; r >= 0; r--)
            {
                var cells = Rows[r].Cells;
                for (var c = 0; c < cells.Count; c++)
                {
                    if (cells[c].Raw.Length > 0)
                    {
                        return r;
                    }
                }
            }

            return -1;
        }

        /// <summary>İçinde veri olan son sütunun sırası (0 tabanlı). Hiç veri yoksa -1.</summary>
        public int LastUsedColumn()
        {
            var last = -1;
            foreach (var row in Rows)
            {
                for (var c = row.Cells.Count - 1; c > last; c--)
                {
                    if (row.Cells[c].Raw.Length > 0)
                    {
                        last = c;
                        break;
                    }
                }
            }

            return last;
        }

        /// <summary>
        /// Sayfanın sonundaki fazla satır ve sütunları siler. Çağıran, silinecek kısmın boş olduğundan
        /// emin olmalıdır (LastUsedRow / LastUsedColumn ile). Bir şey silindiyse true döner.
        /// </summary>
        public bool TrimTo(int rows, int columns)
        {
            rows = Math.Max(1, rows);
            columns = Math.Max(1, columns);
            var changed = false;

            if (Rows.Count > rows)
            {
                Rows.RemoveRange(rows, Rows.Count - rows);
                changed = true;
            }

            if (ColumnCount > columns)
            {
                foreach (var row in Rows)
                {
                    row.Cells.RemoveRange(columns, row.Cells.Count - columns);
                }

                if (ColumnWidths.Count > columns)
                {
                    ColumnWidths.RemoveRange(columns, ColumnWidths.Count - columns);
                }

                ColumnCount = columns;
                changed = true;
            }

            return changed;
        }

        // ---------- KAYIT METNİ ----------

        private static string Escape(string text)
        {
            if (text.IndexOfAny(new[] { '\\', '\t', '\n', '\r' }) < 0)
            {
                return text;
            }

            var sb = new StringBuilder(text.Length + 8);
            foreach (var ch in text)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        private static string Unescape(string text)
        {
            if (text.IndexOf('\\') < 0)
            {
                return text;
            }

            var sb = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == '\\' && i + 1 < text.Length)
                {
                    i++;
                    switch (text[i])
                    {
                        case 't': sb.Append('\t'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        default: sb.Append(text[i]); break;
                    }
                }
                else
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }

        /// <summary>Hücre içeriklerini tek metne çevirir: satırlar \n, hücreler \t ile ayrılır.</summary>
        public string SerializeData()
        {
            var sb = new StringBuilder();
            for (var r = 0; r < Rows.Count; r++)
            {
                if (r > 0)
                {
                    sb.Append('\n');
                }

                var cells = Rows[r].Cells;
                for (var c = 0; c < cells.Count; c++)
                {
                    if (c > 0)
                    {
                        sb.Append('\t');
                    }
                    sb.Append(Escape(cells[c].Raw));
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Kayıt metninden hücreleri kurar. exact=false iken boş metin "yeni sayfa" sayılır
        /// ve varsayılan boyutta boş tablo kurulur.
        /// </summary>
        public void LoadData(string data, int columnCount, bool exact)
        {
            var wasQuiet = Quiet;
            Quiet = true;
            try
            {
                data = data ?? "";

                if (data.Length == 0 && !exact)
                {
                    ResetEmpty(DefaultRowCount, columnCount > 0 ? Math.Min(columnCount, MaxColumns) : DefaultColumnCount);
                    return;
                }

                var parsed = new List<string[]>();
                var columns = Math.Max(columnCount, 1);
                foreach (var line in data.Split('\n'))
                {
                    var parts = line.Split('\t');
                    parsed.Add(parts);
                    if (parts.Length > columns)
                    {
                        columns = parts.Length;
                    }
                }
                columns = Math.Min(columns, MaxColumns);

                Rows.Clear();
                ColumnCount = columns;
                while (ColumnWidths.Count < columns) { ColumnWidths.Add(0); }
                while (ColumnWidths.Count > columns) { ColumnWidths.RemoveAt(ColumnWidths.Count - 1); }

                foreach (var parts in parsed)
                {
                    var row = NewRow();
                    for (var c = 0; c < columns && c < parts.Length; c++)
                    {
                        row.Cells[c].Raw = Unescape(parts[c]);
                    }
                    Rows.Add(row);
                }

                if (Rows.Count == 0)
                {
                    Rows.Add(NewRow());
                }

                Renumber();
                Recalculate();
            }
            finally
            {
                Quiet = wasQuiet;
            }
        }

        /// <summary>Geri al / ileri al için tüm içeriğin anlık görüntüsü (sütun sayısı + hücreler).</summary>
        public string Snapshot()
        {
            return ColumnCount.ToString(CultureInfo.InvariantCulture) + "\n" + SerializeData();
        }

        public void RestoreSnapshot(string snapshot)
        {
            var newline = snapshot.IndexOf('\n');
            var columns = int.Parse(snapshot.Substring(0, newline), CultureInfo.InvariantCulture);
            LoadData(snapshot.Substring(newline + 1), columns, true);
        }

        public string GetWidthsText()
        {
            return string.Join(",", ColumnWidths.Select(w => w.ToString("0.#", CultureInfo.InvariantCulture)));
        }

        public void SetWidthsText(string text)
        {
            var list = new List<double>();
            if (!string.IsNullOrEmpty(text))
            {
                foreach (var part in text.Split(','))
                {
                    double w;
                    list.Add(double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out w) && w > 0 ? w : 0);
                }
            }

            while (list.Count < ColumnCount) { list.Add(0); }
            while (list.Count > ColumnCount) { list.RemoveAt(list.Count - 1); }
            ColumnWidths = list;
        }

        public NoteSheet Clone(string name)
        {
            var copy = new NoteSheet { Name = name };
            copy.RestoreSnapshot(Snapshot());
            copy.SetWidthsText(GetWidthsText());
            return copy;
        }

        // ---------- HESAPLAMA ----------

        /// <summary>Tüm hücrelerin değerini ve ekranda görünen metnini yeniden hesaplar.</summary>
        public void Recalculate()
        {
            foreach (var row in Rows)
            {
                foreach (var cell in row.Cells)
                {
                    cell.EvalState = 0;
                }
            }

            foreach (var row in Rows)
            {
                foreach (var cell in row.Cells)
                {
                    Compute(cell);
                }
            }
        }

        private NoteValue GetValueForFormula(int row, int column)
        {
            var cell = GetCell(row, column);
            return cell == null ? NoteValue.Empty : Compute(cell);
        }

        private NoteValue Compute(NoteCell cell)
        {
            if (cell.EvalState == 2)
            {
                return cell.Value;
            }

            if (cell.EvalState == 1)
            {
                return NoteValue.FromError("#DÖNGÜ!");
            }

            cell.EvalState = 1;

            var raw = cell.Raw;
            NoteValue value;
            string display;
            var plain = false;

            if (cell.IsFormula)
            {
                value = NoteFormula.Evaluate(raw.Substring(1), Rows.Count, ColumnCount, GetValueForFormula);
                switch (value.Kind)
                {
                    case NoteValueKind.Number: display = NoteFormula.FormatNumber(value.Number); break;
                    case NoteValueKind.Empty: display = ""; break;
                    default: display = value.Text; break;
                }
            }
            else if (cell.PlainCached)
            {
                value = cell.Value;
                display = raw;
                plain = true;
            }
            else
            {
                plain = true;
                display = raw;
                double number;
                if (raw.Length == 0)
                {
                    value = NoteValue.Empty;
                }
                else if (NoteFormula.TryParseNumber(raw, out number))
                {
                    value = NoteValue.FromNumber(number);
                }
                else
                {
                    value = NoteValue.FromText(raw);
                }
            }

            cell.SetComputed(value, display, plain);
            cell.EvalState = 2;
            return value;
        }

        // ---------- SATIR / SÜTUN EKLE - SİL ----------

        private void RewriteFormulas(Func<string, string> rewrite)
        {
            var wasQuiet = Quiet;
            Quiet = true;
            try
            {
                foreach (var row in Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        if (cell.IsFormula)
                        {
                            cell.Raw = rewrite(cell.Raw);
                        }
                    }
                }
            }
            finally
            {
                Quiet = wasQuiet;
            }
        }

        public void InsertRows(int at, int count)
        {
            at = Math.Max(0, Math.Min(at, Rows.Count));
            count = Math.Min(count, MaxRows - Rows.Count);
            if (count <= 0)
            {
                return;
            }

            for (var i = 0; i < count; i++)
            {
                Rows.Insert(at, NewRow());
            }

            RewriteFormulas(f => NoteFormula.AdjustForInsert(f, true, at, count));
            Renumber();
            Recalculate();
        }

        public void DeleteRows(IEnumerable<int> indices)
        {
            foreach (var index in indices.Distinct().OrderByDescending(i => i))
            {
                if (index < 0 || index >= Rows.Count)
                {
                    continue;
                }

                Rows.RemoveAt(index);
                var at = index;
                RewriteFormulas(f => NoteFormula.AdjustForDelete(f, true, at, 1));
            }

            if (Rows.Count == 0)
            {
                Rows.Add(NewRow());
            }

            Renumber();
            Recalculate();
        }

        public void InsertColumns(int at, int count)
        {
            at = Math.Max(0, Math.Min(at, ColumnCount));
            count = Math.Min(count, MaxColumns - ColumnCount);
            if (count <= 0)
            {
                return;
            }

            for (var i = 0; i < count; i++)
            {
                foreach (var row in Rows)
                {
                    row.Cells.Insert(at, NewCell());
                }
                ColumnWidths.Insert(at, 0);
                ColumnCount++;
            }

            RewriteFormulas(f => NoteFormula.AdjustForInsert(f, false, at, count));
            Recalculate();
        }

        public void DeleteColumns(IEnumerable<int> indices)
        {
            foreach (var index in indices.Distinct().OrderByDescending(i => i))
            {
                if (index < 0 || index >= ColumnCount || ColumnCount <= 1)
                {
                    continue;
                }

                foreach (var row in Rows)
                {
                    row.Cells.RemoveAt(index);
                }
                ColumnWidths.RemoveAt(index);
                ColumnCount--;

                var at = index;
                RewriteFormulas(f => NoteFormula.AdjustForDelete(f, false, at, 1));
            }

            Recalculate();
        }

        // ---------- SIRALAMA ----------

        private static int Group(NoteValue v)
        {
            switch (v.Kind)
            {
                case NoteValueKind.Empty: return 2;
                case NoteValueKind.Number: return 0;
                default: return 1;
            }
        }

        /// <summary>
        /// Sıralama karşılaştırması: önce sayılar (küçükten büyüğe), sonra metinler (A-Z, Türkçe kurallı),
        /// boşlar her zaman en sonda. Z-A'da sayı/metin sırası tersine döner, boşlar yine sonda kalır.
        /// </summary>
        public static int CompareForSort(NoteValue a, NoteValue b, bool ascending)
        {
            var ga = Group(a);
            var gb = Group(b);

            if (ga == 2 || gb == 2)
            {
                return ga == gb ? 0 : (ga == 2 ? 1 : -1);
            }

            int result;
            if (ga != gb)
            {
                result = ga.CompareTo(gb);
            }
            else if (ga == 0)
            {
                result = a.Number.CompareTo(b.Number);
            }
            else
            {
                result = string.Compare(a.Text, b.Text, NoteFormula.TurkishCulture, CompareOptions.IgnoreCase);
            }

            return ascending ? result : -result;
        }

        private sealed class RowComparer : IComparer<NoteRow>
        {
            private readonly int _column;
            private readonly bool _ascending;

            public RowComparer(int column, bool ascending)
            {
                _column = column;
                _ascending = ascending;
            }

            public int Compare(NoteRow x, NoteRow y)
            {
                var a = x[_column];
                var b = y[_column];
                return CompareForSort(
                    a == null ? NoteValue.Empty : a.Value,
                    b == null ? NoteValue.Empty : b.Value,
                    _ascending);
            }
        }

        /// <summary>
        /// 1. satır (başlık) yerinde kalır, altındaki satırlar seçilen sütuna göre sıralanır.
        /// Satırın kendi satırına başvuran formüller (=A5*B5) yeni satıra uyarlanır.
        /// </summary>
        public void SortByColumn(int column, bool ascending)
        {
            if (Rows.Count < 3)
            {
                return;
            }

            Recalculate();

            var body = new List<KeyValuePair<NoteRow, int>>();
            for (var i = 1; i < Rows.Count; i++)
            {
                body.Add(new KeyValuePair<NoteRow, int>(Rows[i], i));
            }

            var comparer = new RowComparer(column, ascending);
            var sorted = body.OrderBy(p => p.Key, comparer).ToList();

            var header = Rows[0];
            Rows.Clear();
            Rows.Add(header);
            foreach (var pair in sorted)
            {
                Rows.Add(pair.Key);
            }

            var wasQuiet = Quiet;
            Quiet = true;
            try
            {
                for (var newIndex = 1; newIndex < Rows.Count; newIndex++)
                {
                    var oldIndex = sorted[newIndex - 1].Value;
                    if (oldIndex == newIndex)
                    {
                        continue;
                    }

                    foreach (var cell in Rows[newIndex].Cells)
                    {
                        if (cell.IsFormula)
                        {
                            cell.Raw = NoteFormula.ShiftSameRow(cell.Raw, oldIndex, newIndex);
                        }
                    }
                }
            }
            finally
            {
                Quiet = wasQuiet;
            }

            Renumber();
            Recalculate();
        }
    }

    /// <summary>Panodaki (Excel'den kopyalanan) sekmeyle ayrılmış metni okur ve yazar.</summary>
    public static class NoteClipboard
    {
        /// <summary>Excel'in panoya koyduğu metni satır/hücre listesine çevirir (tırnaklı çok satırlı hücreler dahil).</summary>
        public static List<string[]> ParseTsv(string text)
        {
            var rows = new List<string[]>();
            if (string.IsNullOrEmpty(text))
            {
                return rows;
            }

            var row = new List<string>();
            var sb = new StringBuilder();
            var fieldStart = true;
            var pending = false;
            var i = 0;
            var n = text.Length;

            while (i < n)
            {
                var ch = text[i];

                if (fieldStart && ch == '"')
                {
                    i++;
                    while (i < n)
                    {
                        if (text[i] == '"')
                        {
                            if (i + 1 < n && text[i + 1] == '"')
                            {
                                sb.Append('"');
                                i += 2;
                                continue;
                            }
                            i++;
                            break;
                        }
                        sb.Append(text[i]);
                        i++;
                    }
                    fieldStart = false;
                    pending = true;
                    continue;
                }

                if (ch == '\t')
                {
                    row.Add(sb.ToString());
                    sb.Clear();
                    fieldStart = true;
                    pending = true;
                    i++;
                    continue;
                }

                if (ch == '\r' || ch == '\n')
                {
                    row.Add(sb.ToString());
                    sb.Clear();
                    rows.Add(row.ToArray());
                    row = new List<string>();
                    fieldStart = true;
                    pending = false;
                    if (ch == '\r' && i + 1 < n && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    i++;
                    continue;
                }

                sb.Append(ch);
                fieldStart = false;
                pending = true;
                i++;
            }

            if (pending || sb.Length > 0 || row.Count > 0)
            {
                row.Add(sb.ToString());
                rows.Add(row.ToArray());
            }

            return rows;
        }

        /// <summary>Hücre listesini Excel'in anlayacağı sekmeli metne çevirir.</summary>
        public static string FormatTsv(IList<string[]> rows)
        {
            var sb = new StringBuilder();
            for (var r = 0; r < rows.Count; r++)
            {
                if (r > 0)
                {
                    sb.Append("\r\n");
                }

                var cells = rows[r];
                for (var c = 0; c < cells.Length; c++)
                {
                    if (c > 0)
                    {
                        sb.Append('\t');
                    }

                    var text = cells[c] ?? "";
                    if (text.IndexOfAny(new[] { '\t', '\n', '\r', '"' }) >= 0)
                    {
                        sb.Append('"').Append(text.Replace("\"", "\"\"")).Append('"');
                    }
                    else
                    {
                        sb.Append(text);
                    }
                }
            }
            return sb.ToString();
        }
    }
}