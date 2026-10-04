using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DEPO_DURUMU.Data
{
    public enum NoteValueKind
    {
        Empty,
        Number,
        Text,
        Error
    }

    /// <summary>Bir hücrenin hesaplanmış değeri: boş, sayı, metin ya da hata (#DEĞER! gibi).</summary>
    public struct NoteValue
    {
        public NoteValueKind Kind;
        public double Number;
        public string Text;

        public static NoteValue Empty
        {
            get { return new NoteValue { Kind = NoteValueKind.Empty, Text = "" }; }
        }

        public static NoteValue FromNumber(double number)
        {
            return new NoteValue { Kind = NoteValueKind.Number, Number = number, Text = "" };
        }

        public static NoteValue FromText(string text)
        {
            return new NoteValue { Kind = NoteValueKind.Text, Text = text ?? "" };
        }

        public static NoteValue FromError(string code)
        {
            return new NoteValue { Kind = NoteValueKind.Error, Text = code };
        }
    }

    /// <summary>Formülde hata olduğunda fırlatılır; mesaj, hücrede görünen hata kodudur.</summary>
    public class NoteFormulaException : Exception
    {
        public NoteFormulaException(string code) : base(code)
        {
        }
    }

    /// <summary>
    /// Notlar sayfasındaki küçük hesaplayıcı. Tamamen bu dosyada yazılmıştır, dış paket kullanmaz.
    /// Desteklenenler: + - * / ^ % parantez, hücre başvurusu (A1, $A$1), aralık (A1:A5) ve
    /// TOPLA, ORTALAMA, MIN, MAKS, SAY işlevleri (İngilizce SUM, AVERAGE, MAX, COUNT da çalışır).
    /// İşlev bağımsız değişkenleri ; ile ayrılır (virgül ondalık ayracıdır).
    /// </summary>
    public static class NoteFormula
    {
        private static readonly CultureInfo Turkish = new CultureInfo("tr-TR");

        /// <summary>Büyük/küçük harfe duyarsız, Türkçe kurallı metin karşılaştırıcısı (filtre listeleri için).</summary>
        public static readonly StringComparer TextComparer = StringComparer.Create(new CultureInfo("tr-TR"), true);

        public static CultureInfo TurkishCulture
        {
            get { return Turkish; }
        }

        // ---------- SÜTUN HARFLERİ ----------

        /// <summary>0 -> A, 25 -> Z, 26 -> AA ...</summary>
        public static string ColumnName(int index)
        {
            var sb = new StringBuilder();
            var n = index + 1;
            while (n > 0)
            {
                var rem = (n - 1) % 26;
                sb.Insert(0, (char)('A' + rem));
                n = (n - 1) / 26;
            }
            return sb.ToString();
        }

        /// <summary>A -> 0, B -> 1, AA -> 26 ...</summary>
        public static int ColumnIndex(string letters)
        {
            var n = 0;
            foreach (var ch in letters.ToUpperInvariant())
            {
                n = n * 26 + (ch - 'A' + 1);
            }
            return n - 1;
        }

        // ---------- SAYI OKUMA / YAZMA ----------

        private static readonly Regex TurkishThousands = new Regex(@"^[+-]?\d{1,3}(\.\d{3})+(,\d+)?$", RegexOptions.Compiled);
        private static readonly Regex TurkishPlain = new Regex(@"^[+-]?\d+(,\d+)?$", RegexOptions.Compiled);
        private static readonly Regex EnglishThousands = new Regex(@"^[+-]?\d{1,3}(,\d{3})+(\.\d+)?$", RegexOptions.Compiled);
        private static readonly Regex DotDecimal = new Regex(@"^[+-]?\d*\.\d+$", RegexOptions.Compiled);
        private static readonly Regex Scientific = new Regex(@"^[+-]?\d+([.,]\d+)?[eE][+-]?\d+$", RegexOptions.Compiled);
        private static readonly Regex CurrencyStart = new Regex(@"^(?:[₺$€£]|TL|TRY)\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LeadingZeroInteger = new Regex(@"^[+-]?0\d+$", RegexOptions.Compiled);
        private static readonly Regex CurrencyEnd = new Regex(@"\s*(?:[₺$€£]|TL|TRY)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Hücre metnini sayıya çevirmeyi dener. Türkçe (1.234,56), düz (1234,5 / 1234.5),
        /// yüzde (%12 değil 12%) ve para birimi simgeli (₺, TL, $, €) yazımları kabul eder.
        /// </summary>
        public static bool TryParseNumber(string text, out double number)
        {
            number = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var s = text.Trim();
            s = CurrencyStart.Replace(s, "");
            s = CurrencyEnd.Replace(s, "");
            s = s.Trim();
            if (s.Length == 0)
            {
                return false;
            }

            // 15 haneden uzun sayılar (kimlik no, IBAN, seri no) ve başı sıfırla başlayan tam sayılar
            // (05321234567 gibi) sayı değil yazı sayılır: yuvarlanmaz, sıfırı silinmez.
            var digitCount = 0;
            foreach (var ch in s)
            {
                if (ch >= '0' && ch <= '9')
                {
                    digitCount++;
                }
            }
            if (digitCount > 15 || LeadingZeroInteger.IsMatch(s))
            {
                return false;
            }

            var percent = false;
            if (s.EndsWith("%", StringComparison.Ordinal))
            {
                percent = true;
                s = s.Substring(0, s.Length - 1).Trim();
            }

            string normalized;
            if (TurkishThousands.IsMatch(s))
            {
                normalized = s.Replace(".", "").Replace(',', '.');
            }
            else if (TurkishPlain.IsMatch(s))
            {
                normalized = s.Replace(',', '.');
            }
            else if (EnglishThousands.IsMatch(s))
            {
                normalized = s.Replace(",", "");
            }
            else if (DotDecimal.IsMatch(s))
            {
                normalized = s;
            }
            else if (Scientific.IsMatch(s))
            {
                normalized = s.Replace(',', '.');
            }
            else
            {
                return false;
            }

            double parsed;
            if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
                double.IsNaN(parsed) || double.IsInfinity(parsed))
            {
                return false;
            }

            number = percent ? parsed / 100.0 : parsed;
            return true;
        }

        /// <summary>Sonucu hücrede gösterilecek metne çevirir: 1234,5 gibi (binlik ayracı yok).</summary>
        public static string FormatNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return "#SAYI!";
            }

            var rounded = Math.Round(value, 10, MidpointRounding.AwayFromZero);
            if (rounded == 0)
            {
                return "0";
            }

            return rounded.ToString("0.##########", Turkish);
        }

        /// <summary>Alt çubuktaki özet için: 1.234,5 gibi (binlik ayraçlı).</summary>
        public static string FormatNumberGrouped(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return "#SAYI!";
            }

            var rounded = Math.Round(value, 10, MidpointRounding.AwayFromZero);
            if (rounded == 0)
            {
                return "0";
            }

            return rounded.ToString("#,##0.##########", Turkish);
        }

        // ---------- FORMÜL HESAPLAMA ----------

        /// <summary>
        /// "=" işaretinden sonraki formül metnini hesaplar. Hata olursa hata değeri döner (fırlatmaz).
        /// getCell: verilen (satır, sütun) hücresinin hesaplanmış değerini verir (0 tabanlı).
        /// </summary>
        public static NoteValue Evaluate(string formula, int rowCount, int columnCount, Func<int, int, NoteValue> getCell)
        {
            try
            {
                var parser = new Parser(formula, rowCount, columnCount, getCell);
                return parser.Run();
            }
            catch (NoteFormulaException ex)
            {
                return NoteValue.FromError(ex.Message);
            }
            catch (OverflowException)
            {
                return NoteValue.FromError("#SAYI!");
            }
        }

        private sealed class CellRange
        {
            public int Row1;
            public int Col1;
            public int Row2;
            public int Col2;

            public bool IsSingle
            {
                get { return Row1 == Row2 && Col1 == Col2; }
            }
        }

        private static readonly Regex CellToken = new Regex(@"^\$?([A-Za-z]{1,3})\$?(\d+)$", RegexOptions.Compiled);

        private sealed class Parser
        {
            private readonly string _s;
            private int _p;
            private readonly int _rows;
            private readonly int _cols;
            private readonly Func<int, int, NoteValue> _get;

            // true iken ifade yalnızca okunur, hesaplanmaz (EĞER'in seçilmeyen kolu için).
            private bool _skip;

            public Parser(string text, int rows, int cols, Func<int, int, NoteValue> get)
            {
                _s = text ?? "";
                _rows = rows;
                _cols = cols;
                _get = get;
            }

            public NoteValue Run()
            {
                SkipSpaces();
                if (_p >= _s.Length)
                {
                    throw new NoteFormulaException("#HATA!");
                }

                var result = ParseComparison();
                SkipSpaces();
                if (_p < _s.Length)
                {
                    throw new NoteFormulaException("#HATA!");
                }

                return ToFinal(result);
            }

            private void SkipSpaces()
            {
                while (_p < _s.Length && char.IsWhiteSpace(_s[_p]))
                {
                    _p++;
                }
            }

            private static bool IsDigit(char c)
            {
                return c >= '0' && c <= '9';
            }

            // karşılaştırma: =  <>  <  >  <=  >=
            private object ParseComparison()
            {
                var left = ParseConcat();
                while (true)
                {
                    SkipSpaces();
                    var op = ReadComparisonOperator();
                    if (op == null)
                    {
                        return left;
                    }

                    var right = ParseConcat();
                    left = Compare(left, right, op);
                }
            }

            private string ReadComparisonOperator()
            {
                if (_p >= _s.Length)
                {
                    return null;
                }

                var c = _s[_p];
                var next = _p + 1 < _s.Length ? _s[_p + 1] : '\0';

                if (c == '=')
                {
                    _p++;
                    return "=";
                }

                if (c == '<')
                {
                    if (next == '>') { _p += 2; return "<>"; }
                    if (next == '=') { _p += 2; return "<="; }
                    _p++;
                    return "<";
                }

                if (c == '>')
                {
                    if (next == '=') { _p += 2; return ">="; }
                    _p++;
                    return ">";
                }

                return null;
            }

            // metin birleştirme: &
            private object ParseConcat()
            {
                var left = ParseExpression();
                while (true)
                {
                    SkipSpaces();
                    if (_p < _s.Length && _s[_p] == '&')
                    {
                        _p++;
                        var right = ParseExpression();
                        left = ToText(left) + ToText(right);
                    }
                    else
                    {
                        return left;
                    }
                }
            }

            // toplama / çıkarma
            private object ParseExpression()
            {
                var left = ParseTerm();
                while (true)
                {
                    SkipSpaces();
                    if (_p < _s.Length && _s[_p] == '+')
                    {
                        _p++;
                        left = Check(ToNumber(left) + ToNumber(ParseTerm()));
                    }
                    else if (_p < _s.Length && _s[_p] == '-')
                    {
                        _p++;
                        left = Check(ToNumber(left) - ToNumber(ParseTerm()));
                    }
                    else
                    {
                        return left;
                    }
                }
            }

            // çarpma / bölme
            private object ParseTerm()
            {
                var left = ParsePower();
                while (true)
                {
                    SkipSpaces();
                    if (_p < _s.Length && _s[_p] == '*')
                    {
                        _p++;
                        left = Check(ToNumber(left) * ToNumber(ParsePower()));
                    }
                    else if (_p < _s.Length && _s[_p] == '/')
                    {
                        _p++;
                        var divisor = ToNumber(ParsePower());
                        if (!_skip && divisor == 0)
                        {
                            throw new NoteFormulaException("#BÖL/0!");
                        }
                        left = _skip ? 0.0 : Check(ToNumber(left) / divisor);
                    }
                    else
                    {
                        return left;
                    }
                }
            }

            // üs alma
            private object ParsePower()
            {
                var left = ParseUnary();
                while (true)
                {
                    SkipSpaces();
                    if (_p < _s.Length && _s[_p] == '^')
                    {
                        _p++;
                        left = Check(Math.Pow(ToNumber(left), ToNumber(ParseUnary())));
                    }
                    else
                    {
                        return left;
                    }
                }
            }

            private object ParseUnary()
            {
                SkipSpaces();
                if (_p < _s.Length && _s[_p] == '-')
                {
                    _p++;
                    return -ToNumber(ParseUnary());
                }
                if (_p < _s.Length && _s[_p] == '+')
                {
                    _p++;
                    return ParseUnary();
                }

                var value = ParsePrimary();
                SkipSpaces();
                while (_p < _s.Length && _s[_p] == '%')
                {
                    _p++;
                    value = ToNumber(value) / 100.0;
                    SkipSpaces();
                }
                return value;
            }

            private object ParsePrimary()
            {
                SkipSpaces();
                if (_p >= _s.Length)
                {
                    throw new NoteFormulaException("#HATA!");
                }

                var c = _s[_p];

                if (c == '(')
                {
                    _p++;
                    var inner = ParseComparison();
                    SkipSpaces();
                    if (_p >= _s.Length || _s[_p] != ')')
                    {
                        throw new NoteFormulaException("#HATA!");
                    }
                    _p++;
                    return inner;
                }

                if (c == '"')
                {
                    return ParseStringLiteral();
                }

                if (IsDigit(c) || ((c == ',' || c == '.') && _p + 1 < _s.Length && IsDigit(_s[_p + 1])))
                {
                    return ParseNumberToken();
                }

                if (c == '#')
                {
                    // Silinmiş başvurudan kalan #BAŞV! gibi hata kodu
                    var start = _p;
                    while (_p < _s.Length && "+-*/^(),;& <>=".IndexOf(_s[_p]) < 0)
                    {
                        _p++;
                    }
                    throw new NoteFormulaException(_s.Substring(start, _p - start));
                }

                if (c == '$' || char.IsLetter(c))
                {
                    return ParseNameOrReference();
                }

                throw new NoteFormulaException("#HATA!");
            }

            /// <summary>"metin" biçiminde yazı. İçinde tırnak için "" yazılır.</summary>
            private string ParseStringLiteral()
            {
                _p++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (_p >= _s.Length)
                    {
                        throw new NoteFormulaException("#HATA!");
                    }

                    var ch = _s[_p];
                    if (ch == '"')
                    {
                        if (_p + 1 < _s.Length && _s[_p + 1] == '"')
                        {
                            sb.Append('"');
                            _p += 2;
                            continue;
                        }

                        _p++;
                        break;
                    }

                    sb.Append(ch);
                    _p++;
                }

                return sb.ToString();
            }

            private double ParseNumberToken()
            {
                var start = _p;
                while (_p < _s.Length && IsDigit(_s[_p]))
                {
                    _p++;
                }
                if (_p + 1 < _s.Length && (_s[_p] == ',' || _s[_p] == '.') && IsDigit(_s[_p + 1]))
                {
                    _p++;
                    while (_p < _s.Length && IsDigit(_s[_p]))
                    {
                        _p++;
                    }
                }

                var token = _s.Substring(start, _p - start).Replace(',', '.');
                if (token.StartsWith(".", StringComparison.Ordinal))
                {
                    token = "0" + token;
                }
                return double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            private string ReadWord()
            {
                var start = _p;
                while (_p < _s.Length && (char.IsLetterOrDigit(_s[_p]) || _s[_p] == '$' || _s[_p] == '_'))
                {
                    _p++;
                }
                return _s.Substring(start, _p - start);
            }

            private object ParseNameOrReference()
            {
                var token = ReadWord();

                var save = _p;
                SkipSpaces();
                if (_p < _s.Length && _s[_p] == '(')
                {
                    _p++;
                    return CallFunction(token);
                }
                _p = save;

                var first = ToCellRange(token);

                if (_p < _s.Length && _s[_p] == ':')
                {
                    _p++;
                    var second = ToCellRange(ReadWord());
                    return new CellRange
                    {
                        Row1 = Math.Min(first.Row1, second.Row1),
                        Col1 = Math.Min(first.Col1, second.Col1),
                        Row2 = Math.Max(first.Row1, second.Row1),
                        Col2 = Math.Max(first.Col1, second.Col1)
                    };
                }

                return first;
            }

            private static CellRange ToCellRange(string token)
            {
                var match = CellToken.Match(token);
                if (!match.Success)
                {
                    throw new NoteFormulaException("#AD?");
                }

                var row = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) - 1;
                if (row < 0)
                {
                    throw new NoteFormulaException("#BAŞV!");
                }

                var col = ColumnIndex(match.Groups[1].Value);
                return new CellRange { Row1 = row, Col1 = col, Row2 = row, Col2 = col };
            }

            // ---------- İŞLEVLER ----------

            /// <summary>İşlev adını Türkçe harflerden bağımsız büyük harfe çevirir: eğer, EĞER, EGER hepsi EGER olur.</summary>
            private static string NormalizeName(string name)
            {
                return name
                    .Replace('ı', 'I').Replace('İ', 'I')
                    .Replace('ğ', 'G').Replace('Ğ', 'G')
                    .Replace('ş', 'S').Replace('Ş', 'S')
                    .Replace('ç', 'C').Replace('Ç', 'C')
                    .Replace('ö', 'O').Replace('Ö', 'O')
                    .Replace('ü', 'U').Replace('Ü', 'U')
                    .ToUpperInvariant();
            }

            private bool TryReadSeparator()
            {
                SkipSpaces();
                if (_p < _s.Length && (_s[_p] == ';' || _s[_p] == ','))
                {
                    _p++;
                    return true;
                }
                return false;
            }

            private void ExpectClose()
            {
                SkipSpaces();
                if (_p >= _s.Length || _s[_p] != ')')
                {
                    throw new NoteFormulaException("#HATA!");
                }
                _p++;
            }

            private object CallFunction(string name)
            {
                var fn = NormalizeName(name);

                if (fn == "EGER" || fn == "IF")
                {
                    return CallIf();
                }

                var args = new List<object>();

                SkipSpaces();
                if (_p < _s.Length && _s[_p] == ')')
                {
                    _p++;
                }
                else
                {
                    while (true)
                    {
                        args.Add(ParseComparison());
                        if (TryReadSeparator())
                        {
                            continue;
                        }
                        ExpectClose();
                        break;
                    }
                }

                if (_skip)
                {
                    return 0.0;
                }

                switch (fn)
                {
                    case "TOPLA":
                    case "SUM":
                        {
                            double sum = 0;
                            foreach (var n in Numbers(args)) { sum += n; }
                            return Check(sum);
                        }
                    case "ORTALAMA":
                    case "AVERAGE":
                        {
                            var list = Numbers(args);
                            if (list.Count == 0)
                            {
                                throw new NoteFormulaException("#BÖL/0!");
                            }
                            double sum = 0;
                            foreach (var n in list) { sum += n; }
                            return Check(sum / list.Count);
                        }
                    case "MIN":
                        {
                            var list = Numbers(args);
                            if (list.Count == 0) { return 0.0; }
                            var best = list[0];
                            foreach (var n in list) { if (n < best) { best = n; } }
                            return best;
                        }
                    case "MAKS":
                    case "MAX":
                        {
                            var list = Numbers(args);
                            if (list.Count == 0) { return 0.0; }
                            var best = list[0];
                            foreach (var n in list) { if (n > best) { best = n; } }
                            return best;
                        }
                    case "SAY":
                    case "COUNT":
                        return (double)Numbers(args).Count;
                    case "CARPIM":
                    case "PRODUCT":
                        {
                            var list = Numbers(args);
                            if (list.Count == 0) { return 0.0; }
                            double product = 1;
                            foreach (var n in list) { product *= n; }
                            return Check(product);
                        }
                    case "MUTLAK":
                    case "ABS":
                        RequireArgs(args, 1, 1);
                        return Math.Abs(ToNumber(args[0]));
                    case "KAREKOK":
                    case "SQRT":
                        {
                            RequireArgs(args, 1, 1);
                            var x = ToNumber(args[0]);
                            if (x < 0)
                            {
                                throw new NoteFormulaException("#SAYI!");
                            }
                            return Math.Sqrt(x);
                        }
                    case "YUVARLA":
                    case "ROUND":
                        {
                            RequireArgs(args, 1, 2);
                            var x = ToNumber(args[0]);
                            var digits = args.Count > 1 ? (int)Math.Truncate(ToNumber(args[1])) : 0;
                            digits = Math.Max(-15, Math.Min(15, digits));
                            if (digits >= 0)
                            {
                                return Check(Math.Round(x, digits, MidpointRounding.AwayFromZero));
                            }
                            var factor = Math.Pow(10, -digits);
                            return Check(Math.Round(x / factor, MidpointRounding.AwayFromZero) * factor);
                        }
                    case "BIRLESTIR":
                    case "CONCATENATE":
                    case "CONCAT":
                        {
                            var sb = new StringBuilder();
                            foreach (var arg in args)
                            {
                                var range = arg as CellRange;
                                if (range == null)
                                {
                                    sb.Append(ToText(arg));
                                    continue;
                                }

                                var lastRow = Math.Min(range.Row2, _rows - 1);
                                var lastCol = Math.Min(range.Col2, _cols - 1);
                                for (var row = range.Row1; row <= lastRow; row++)
                                {
                                    for (var col = range.Col1; col <= lastCol; col++)
                                    {
                                        sb.Append(CellText(_get(row, col)));
                                    }
                                }
                            }
                            return sb.ToString();
                        }
                    default:
                        throw new NoteFormulaException("#AD?");
                }
            }

            private static void RequireArgs(List<object> args, int min, int max)
            {
                if (args.Count < min || args.Count > max)
                {
                    throw new NoteFormulaException("#HATA!");
                }
            }

            /// <summary>EĞER(şart; doğruysa; yanlışsa): yalnızca seçilen kol hesaplanır (EĞER(B2=0;0;A2/B2) hata vermez).</summary>
            private object CallIf()
            {
                SkipSpaces();
                if (_p < _s.Length && _s[_p] == ')')
                {
                    throw new NoteFormulaException("#HATA!");
                }

                var truth = Truthy(ParseComparison());

                if (!TryReadSeparator())
                {
                    throw new NoteFormulaException("#HATA!");
                }

                var saved = _skip;

                _skip = saved || !truth;
                var whenTrue = ParseComparison();
                _skip = saved;

                object whenFalse = false;
                if (TryReadSeparator())
                {
                    _skip = saved || truth;
                    whenFalse = ParseComparison();
                    _skip = saved;
                }

                ExpectClose();

                if (_skip)
                {
                    return 0.0;
                }

                return truth ? whenTrue : whenFalse;
            }

            private bool Truthy(object value)
            {
                if (_skip)
                {
                    return true;
                }

                if (value is bool)
                {
                    return (bool)value;
                }

                var scalar = ToScalar(value);
                if (scalar == null)
                {
                    return false;
                }
                if (scalar is double)
                {
                    return (double)scalar != 0;
                }

                throw new NoteFormulaException("#DEĞER!");
            }

            // ---------- DEĞER DÖNÜŞÜMLERİ ----------

            /// <summary>İşlev bağımsız değişkenlerindeki sayıları toplar. Aralıklardaki metin ve boşlar atlanır.</summary>
            private List<double> Numbers(List<object> args)
            {
                var list = new List<double>();

                foreach (var arg in args)
                {
                    if (arg is double)
                    {
                        list.Add((double)arg);
                        continue;
                    }

                    if (arg is bool)
                    {
                        list.Add((bool)arg ? 1 : 0);
                        continue;
                    }

                    var text = arg as string;
                    if (text != null)
                    {
                        double parsed;
                        if (!TryParseNumber(text, out parsed))
                        {
                            throw new NoteFormulaException("#DEĞER!");
                        }
                        list.Add(parsed);
                        continue;
                    }

                    var range = (CellRange)arg;
                    var lastRow = Math.Min(range.Row2, _rows - 1);
                    var lastCol = Math.Min(range.Col2, _cols - 1);

                    for (var row = range.Row1; row <= lastRow; row++)
                    {
                        for (var col = range.Col1; col <= lastCol; col++)
                        {
                            var v = _get(row, col);
                            if (v.Kind == NoteValueKind.Error)
                            {
                                throw new NoteFormulaException(v.Text);
                            }
                            if (v.Kind == NoteValueKind.Number)
                            {
                                list.Add(v.Number);
                            }
                        }
                    }
                }

                return list;
            }

            /// <summary>Değeri sayıya çevirir. Hücre başvurusu tek hücre olmalı; metin hücresi #DEĞER! verir.</summary>
            private double ToNumber(object value)
            {
                if (_skip)
                {
                    return 0;
                }

                if (value is double)
                {
                    return (double)value;
                }

                if (value is bool)
                {
                    return (bool)value ? 1 : 0;
                }

                var text = value as string;
                if (text != null)
                {
                    double parsed;
                    if (TryParseNumber(text, out parsed))
                    {
                        return parsed;
                    }
                    throw new NoteFormulaException("#DEĞER!");
                }

                var range = (CellRange)value;
                if (!range.IsSingle)
                {
                    throw new NoteFormulaException("#DEĞER!");
                }

                var cell = _get(range.Row1, range.Col1);
                switch (cell.Kind)
                {
                    case NoteValueKind.Empty:
                        return 0;
                    case NoteValueKind.Number:
                        return cell.Number;
                    case NoteValueKind.Error:
                        throw new NoteFormulaException(cell.Text);
                    default:
                        throw new NoteFormulaException("#DEĞER!");
                }
            }

            /// <summary>Karşılaştırma için değeri sadeleştirir: null (boş), double ya da string.</summary>
            private object ToScalar(object value)
            {
                if (_skip)
                {
                    return 0.0;
                }

                if (value is double)
                {
                    return value;
                }

                if (value is bool)
                {
                    return (bool)value ? 1.0 : 0.0;
                }

                var text = value as string;
                if (text != null)
                {
                    return text;
                }

                var range = (CellRange)value;
                if (!range.IsSingle)
                {
                    throw new NoteFormulaException("#DEĞER!");
                }

                var cell = _get(range.Row1, range.Col1);
                switch (cell.Kind)
                {
                    case NoteValueKind.Empty:
                        return null;
                    case NoteValueKind.Number:
                        return cell.Number;
                    case NoteValueKind.Error:
                        throw new NoteFormulaException(cell.Text);
                    default:
                        return cell.Text;
                }
            }

            private static string CellText(NoteValue cell)
            {
                switch (cell.Kind)
                {
                    case NoteValueKind.Empty:
                        return "";
                    case NoteValueKind.Number:
                        return FormatNumber(cell.Number);
                    case NoteValueKind.Error:
                        throw new NoteFormulaException(cell.Text);
                    default:
                        return cell.Text;
                }
            }

            private string ToText(object value)
            {
                if (_skip)
                {
                    return "";
                }

                if (value is double)
                {
                    return FormatNumber((double)value);
                }

                if (value is bool)
                {
                    return (bool)value ? "DOĞRU" : "YANLIŞ";
                }

                var text = value as string;
                if (text != null)
                {
                    return text;
                }

                var range = (CellRange)value;
                if (!range.IsSingle)
                {
                    throw new NoteFormulaException("#DEĞER!");
                }

                return CellText(_get(range.Row1, range.Col1));
            }

            private object Compare(object left, object right, string op)
            {
                if (_skip)
                {
                    return false;
                }

                var cmp = CompareScalars(ToScalar(left), ToScalar(right));

                switch (op)
                {
                    case "=": return cmp == 0;
                    case "<>": return cmp != 0;
                    case "<": return cmp < 0;
                    case ">": return cmp > 0;
                    case "<=": return cmp <= 0;
                    default: return cmp >= 0;
                }
            }

            private static int CompareScalars(object a, object b)
            {
                if (a == null && b == null)
                {
                    return 0;
                }

                if (a == null)
                {
                    a = b is string ? (object)"" : (object)0.0;
                }
                else if (b == null)
                {
                    b = a is string ? (object)"" : (object)0.0;
                }

                if (a is double && b is double)
                {
                    var x = (double)a;
                    var y = (double)b;
                    if (Math.Abs(x - y) <= 1e-12 * Math.Max(1.0, Math.Max(Math.Abs(x), Math.Abs(y))))
                    {
                        return 0;
                    }
                    return x < y ? -1 : 1;
                }

                if (a is string && b is string)
                {
                    var result = string.Compare((string)a, (string)b, Turkish, CompareOptions.IgnoreCase);
                    return result < 0 ? -1 : (result > 0 ? 1 : 0);
                }

                // sayı her zaman metinden küçüktür (Excel gibi)
                return a is double ? -1 : 1;
            }

            private static double Check(double value)
            {
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    throw new NoteFormulaException("#SAYI!");
                }
                return value;
            }

            private NoteValue ToFinal(object value)
            {
                if (value is double)
                {
                    return NoteValue.FromNumber(Check((double)value));
                }

                if (value is bool)
                {
                    return NoteValue.FromText((bool)value ? "DOĞRU" : "YANLIŞ");
                }

                var text = value as string;
                if (text != null)
                {
                    return NoteValue.FromText(text);
                }

                var range = (CellRange)value;
                if (!range.IsSingle)
                {
                    throw new NoteFormulaException("#DEĞER!");
                }

                var cell = _get(range.Row1, range.Col1);
                if (cell.Kind == NoteValueKind.Empty)
                {
                    return NoteValue.FromNumber(0);
                }
                return cell;
            }
        }

        // ---------- SATIR / SÜTUN DEĞİŞİNCE FORMÜLLERİ DÜZELTME ----------

        private static readonly Regex ReferenceRegex = new Regex(
            @"(?<![A-Za-z0-9_$#])(?<c1d>\$?)(?<c1>[A-Za-z]{1,3})(?<r1d>\$?)(?<r1>\d+)(?![A-Za-z0-9_(])" +
            @"(?::(?<c2d>\$?)(?<c2>[A-Za-z]{1,3})(?<r2d>\$?)(?<r2>\d+)(?![A-Za-z0-9_(]))?",
            RegexOptions.Compiled);

        private sealed class RefBox
        {
            public int Col1;
            public int Row1;
            public int Col2;
            public int Row2;
            public bool IsRange;
        }

        private static string RewriteReferences(string formula, Func<RefBox, bool> mapper)
        {
            if (string.IsNullOrEmpty(formula) || formula[0] != '=')
            {
                return formula;
            }

            // "..." içindeki yazılar (ör. "A1") başvuru sayılmaz, dokunulmaz.
            var result = new StringBuilder(formula.Length + 8);
            var segment = new StringBuilder();
            var inQuote = false;

            foreach (var ch in formula)
            {
                if (ch == '"')
                {
                    if (!inQuote)
                    {
                        result.Append(RewriteSegment(segment.ToString(), mapper));
                        segment.Length = 0;
                    }

                    inQuote = !inQuote;
                    result.Append(ch);
                }
                else if (inQuote)
                {
                    result.Append(ch);
                }
                else
                {
                    segment.Append(ch);
                }
            }

            result.Append(RewriteSegment(segment.ToString(), mapper));
            return result.ToString();
        }

        private static string RewriteSegment(string text, Func<RefBox, bool> mapper)
        {
            if (text.Length == 0)
            {
                return text;
            }

            return ReferenceRegex.Replace(text, delegate (Match m)
            {
                var box = new RefBox
                {
                    Col1 = ColumnIndex(m.Groups["c1"].Value),
                    Row1 = int.Parse(m.Groups["r1"].Value, CultureInfo.InvariantCulture) - 1,
                    IsRange = m.Groups["c2"].Success
                };
                box.Col2 = box.Col1;
                box.Row2 = box.Row1;
                if (box.IsRange)
                {
                    box.Col2 = ColumnIndex(m.Groups["c2"].Value);
                    box.Row2 = int.Parse(m.Groups["r2"].Value, CultureInfo.InvariantCulture) - 1;
                }

                if (box.Row1 < 0 || box.Row2 < 0)
                {
                    return m.Value;
                }

                if (!mapper(box))
                {
                    return "#BAŞV!";
                }

                var text2 = m.Groups["c1d"].Value + ColumnName(box.Col1) + m.Groups["r1d"].Value + (box.Row1 + 1);
                if (box.IsRange)
                {
                    text2 += ":" + m.Groups["c2d"].Value + ColumnName(box.Col2) + m.Groups["r2d"].Value + (box.Row2 + 1);
                }
                return text2;
            });
        }

        /// <summary>Satır ya da sütun eklenince formüldeki başvuruları kaydırır.</summary>
        public static string AdjustForInsert(string formula, bool rows, int at, int count)
        {
            return RewriteReferences(formula, delegate (RefBox b)
            {
                if (rows)
                {
                    if (b.Row1 >= at) { b.Row1 += count; }
                    if (b.Row2 >= at) { b.Row2 += count; }
                }
                else
                {
                    if (b.Col1 >= at) { b.Col1 += count; }
                    if (b.Col2 >= at) { b.Col2 += count; }
                }
                return true;
            });
        }

        /// <summary>Satır ya da sütun silinince başvuruları düzeltir; silinen hücreye giden başvuru #BAŞV! olur.</summary>
        public static string AdjustForDelete(string formula, bool rows, int at, int count)
        {
            return RewriteReferences(formula, delegate (RefBox b)
            {
                var start = rows ? b.Row1 : b.Col1;
                var end = rows ? b.Row2 : b.Col2;

                if (end < at)
                {
                    return true;
                }

                if (start >= at + count)
                {
                    start -= count;
                    end -= count;
                }
                else
                {
                    var newStart = start < at ? start : at;
                    var newEnd = end >= at + count ? end - count : at - 1;
                    if (newEnd < newStart)
                    {
                        return false;
                    }
                    start = newStart;
                    end = newEnd;
                }

                if (rows) { b.Row1 = start; b.Row2 = end; }
                else { b.Col1 = start; b.Col2 = end; }
                return true;
            });
        }

        /// <summary>
        /// Sıralamada bir satır yer değiştirince, formülün kendi satırına verdiği başvuruları
        /// (örneğin =A5*B5) yeni satıra taşır. Diğer satırlara giden başvurulara dokunmaz.
        /// </summary>
        public static string ShiftSameRow(string formula, int oldRow, int newRow)
        {
            if (oldRow == newRow)
            {
                return formula;
            }

            return RewriteReferences(formula, delegate (RefBox b)
            {
                if (b.Row1 == oldRow && b.Row2 == oldRow)
                {
                    b.Row1 = newRow;
                    b.Row2 = newRow;
                }
                return true;
            });
        }
    }
}