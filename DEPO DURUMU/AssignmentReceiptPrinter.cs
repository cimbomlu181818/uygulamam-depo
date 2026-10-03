using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    /// <summary>
    /// Zimmet kayıtları için tutanak hazırlayıp önizler ve yazdırır: zimmet tutanağı ve iade tutanağı.
    /// Zimmetleme sonrası ve Zimmetler defterinden çağrılır.
    /// </summary>
    public static class AssignmentReceiptPrinter
    {
        private static readonly CultureInfo Turkish = new CultureInfo("tr-TR");

        // ---------- ÖNİZLEME ----------

        /// <summary>
        /// Zimmet tutanağını yazdırmadan önce ekranda gösterir. Pencerede "Yazdır" ve "Kapat" düğmeleri vardır;
        /// yazdırmak istenirse "Yazdır"a basılır, istenmezse kapatılır.
        /// </summary>
        public static void ShowPreview(Window owner, List<Assignment> assignments)
        {
            if (assignments == null || assignments.Count == 0)
            {
                return;
            }

            var window = new ReceiptPreviewWindow(owner, assignments, false, null);
            window.ShowDialog();
        }

        /// <summary>İade tutanağını (iade alınmış zimmetler için) önce ekranda gösterir; yazdırmak için pencerede "Yazdır"a basılır.</summary>
        public static void ShowReturnPreview(Window owner, List<Assignment> assignments)
        {
            if (assignments == null || assignments.Count == 0)
            {
                return;
            }

            var window = new ReceiptPreviewWindow(owner, assignments, true, null);
            window.ShowDialog();
        }

        /// <summary>
        /// KAYDETMEDEN önce tutanağı gösterir. Pencerede "Kaydet", "Kaydet ve Yazdır" ve "Vazgeç" düğmeleri vardır;
        /// kayıt ancak Kaydet'e basılınca yapılır (save geri çağrısı çalışır). Geri çağrı true dönerse pencere kapanır
        /// ve bu metot true verir; Vazgeç'e ya da pencereyi kapatmaya basılırsa hiçbir şey kaydedilmez ve false döner.
        /// Geri çağrı false dönerse (ör. hata oldu) pencere açık kalır.
        /// </summary>
        public static bool ShowSavePreview(Window owner, List<Assignment> assignments, bool isReturn, Func<Window, bool> save)
        {
            if (assignments == null || assignments.Count == 0)
            {
                return false;
            }

            var window = new ReceiptPreviewWindow(owner, assignments, isReturn, save);
            return window.ShowDialog() == true;
        }

        // ---------- YAZDIRMA ----------

        /// <summary>Tutanağı yazdırır. Yazdırma tamamlanırsa true, iptal edilir ya da hata verirse false döner.</summary>
        public static bool Print(Window owner, Assignment assignment)
        {
            var printed = PrintDocument(owner, width => BuildDocument(assignment, width), "Zimmet tutanağı");

            if (printed)
            {
                LogPrinted(new List<Assignment> { assignment }, "Zimmet tutanağı yazdırıldı");
            }

            return printed;
        }

        /// <summary>Bir kişiye toplu yapılan zimmetler için, malzemeleri liste hâlinde gösteren tek tutanak yazdırır.</summary>
        public static bool Print(Window owner, List<Assignment> assignments)
        {
            if (assignments == null || assignments.Count == 0)
            {
                return false;
            }

            if (assignments.Count == 1)
            {
                return Print(owner, assignments[0]);
            }

            var printed = PrintDocument(owner, width => BuildDocument(assignments, width), "Zimmet tutanağı");

            if (printed)
            {
                LogPrinted(assignments, "Zimmet tutanağı yazdırıldı");
            }

            return printed;
        }

        /// <summary>İade tutanağını yazdırır (tek ya da birden fazla malzeme).</summary>
        public static bool PrintReturn(Window owner, List<Assignment> assignments)
        {
            if (assignments == null || assignments.Count == 0)
            {
                return false;
            }

            var printed = PrintDocument(owner, width => BuildReturnDocument(assignments, width), "Zimmet iade tutanağı");

            if (printed)
            {
                LogPrinted(assignments, "İade tutanağı yazdırıldı");
            }

            return printed;
        }

        private static bool PrintDocument(Window owner, Func<double, FlowDocument> build, string jobName)
        {
            var printDialog = new PrintDialog();

            if (printDialog.ShowDialog() != true)
            {
                return false;
            }

            FlowDocument document = build(printDialog.PrintableAreaWidth);
            IDocumentPaginatorSource paginatorSource = document;

            try
            {
                printDialog.PrintDocument(paginatorSource.DocumentPaginator, jobName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "Yazdırılamadı:\n" + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            return true;
        }

        private static void LogPrinted(List<Assignment> assignments, string actionType)
        {
            foreach (var assignment in assignments)
            {
                try
                {
                    LogRepository.Add(assignment.TypeName,
                        assignment.ProductText + " | " + assignment.PersonName,
                        actionType);
                }
                catch (Exception)
                {
                    // Yazdırma zaten tamamlandı; log yazılamaması yazdırmayı engellemez.
                }
            }
        }

        // ---------- BELGELER ----------

        private static FlowDocument NewDocument(double pageWidth)
        {
            var effectivePageWidth = pageWidth > 0 ? pageWidth : 750;

            return new FlowDocument
            {
                PageWidth = effectivePageWidth,
                ColumnWidth = effectivePageWidth,
                FontSize = 12,
                PagePadding = new Thickness(30)
            };
        }

        /// <summary>Toplu zimmet tutanağı: bir kişiye verilen malzemeler liste hâlinde.</summary>
        private static FlowDocument BuildDocument(List<Assignment> assignments, double pageWidth)
        {
            return BuildListDocument(
                assignments,
                pageWidth,
                "ZİMMET TUTANAĞI",
                "Zimmet tarihi",
                "Teslim alan kişi",
                a => a.AssignedAt,
                a => a.AssignedAtText,
                a => a.AssignedNote,
                "Teslim edilen malzemeler:",
                "Yukarıda listelenen malzemeler, belirtilen tarih itibarıyla adıma zimmetlenmiştir.",
                "Teslim Eden",
                "Teslim Alan");
        }

        /// <summary>İade tutanağı: kişinin iade ettiği malzemeler liste hâlinde (tek malzeme de aynı biçimde).</summary>
        private static FlowDocument BuildReturnDocument(List<Assignment> assignments, double pageWidth)
        {
            return BuildListDocument(
                assignments,
                pageWidth,
                "ZİMMET İADE TUTANAĞI",
                "İade tarihi",
                "İade eden kişi",
                a => a.ReturnedAt,
                a => a.ReturnedAtText,
                a => a.ReturnedNote,
                "İade edilen malzemeler:",
                "Yukarıda listelenen malzemeler, belirtilen tarih itibarıyla adıma olan zimmetimden iade edilmiştir.",
                "İade Eden",
                "İade Alan");
        }

        private static FlowDocument BuildListDocument(
            List<Assignment> assignments,
            double pageWidth,
            string title,
            string dateLabel,
            string personLabel,
            Func<Assignment, string> dateRaw,
            Func<Assignment, string> dateText,
            Func<Assignment, string> noteOf,
            string itemsHeading,
            string statement,
            string leftSign,
            string rightSign)
        {
            var first = assignments[0];
            var document = NewDocument(pageWidth);

            document.Blocks.Add(new Paragraph(new Run(title))
            {
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20)
            });

            // Tüm kayıtların tarihi aynı gündeyse başlıkta tek tarih yazılır; farklı günlerdeyse tabloya "tarih" sütunu eklenir.
            var commonDate = CommonDateText(assignments.Select(dateRaw));
            var dateColumn = commonDate == null;

            if (!dateColumn)
            {
                AddField(document, dateLabel, commonDate);
            }

            AddField(document, personLabel, first.PersonName);
            AddField(document, "Sicil no", string.IsNullOrEmpty(first.RegistryNo) ? "-" : first.RegistryNo);
            AddField(document, "Birim", string.IsNullOrEmpty(first.Department) ? "-" : first.Department);

            var notes = assignments
                .Select(noteOf)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct()
                .ToList();

            if (notes.Count > 0)
            {
                AddField(document, "Not", string.Join("; ", notes));
            }

            document.Blocks.Add(new Paragraph(new Run(itemsHeading))
            {
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 10, 0, 4)
            });

            // Malzeme listesi: teslim-tesellüm tutanağıyla aynı yapı (tüm sütunlar oransal / Star).
            var itemsTable = new Table();
            var columnWidths = dateColumn
                ? new[] { 0.5, 3.0, 2.0, 1.0, 1.7 }   // No, Malzeme, Seri No, Miktar, Tarih
                : new[] { 0.5, 3.0, 2.0, 1.0 };       // No, Malzeme, Seri No, Miktar
            foreach (var w in columnWidths)
            {
                itemsTable.Columns.Add(new TableColumn { Width = new GridLength(w, GridUnitType.Star) });
            }

            var itemsGroup = new TableRowGroup();
            itemsTable.RowGroups.Add(itemsGroup);

            var headerRow = new TableRow { Background = Brushes.LightGray };
            headerRow.Cells.Add(MakeItemCell("No", true));
            headerRow.Cells.Add(MakeItemCell("Malzeme", true));
            headerRow.Cells.Add(MakeItemCell("Seri No", true));
            headerRow.Cells.Add(MakeItemCell("Miktar", true));
            if (dateColumn)
            {
                headerRow.Cells.Add(MakeItemCell(dateLabel, true));
            }
            itemsGroup.Rows.Add(headerRow);

            for (var i = 0; i < assignments.Count; i++)
            {
                var a = assignments[i];
                var itemRow = new TableRow();
                itemRow.Cells.Add(MakeItemCell((i + 1).ToString(Turkish), false));
                itemRow.Cells.Add(MakeItemCell(a.SystemNameText, false));
                itemRow.Cells.Add(MakeItemCell(string.IsNullOrEmpty(a.SerialNo) ? "-" : a.SerialNo, false));
                itemRow.Cells.Add(MakeItemCell(a.Quantity.ToString(Turkish), false));
                if (dateColumn)
                {
                    itemRow.Cells.Add(MakeItemCell(dateText(a), false));
                }
                itemsGroup.Rows.Add(itemRow);
            }

            document.Blocks.Add(itemsTable);

            document.Blocks.Add(new Paragraph(new Run(statement))
            {
                Margin = new Thickness(0, 30, 0, 40)
            });

            document.Blocks.Add(MakeSignatureTable(leftSign, rightSign));

            return document;
        }

        /// <summary>
        /// Kayıtların ortak tarih yazısı: hepsi aynı dakikadaysa "gg.aa.yyyy SS:dd", aynı günse "gg.aa.yyyy".
        /// Farklı günlere yayılıyorsa (ya da tarih okunamazsa) null döner; bu durumda tabloya tarih sütunu eklenir.
        /// </summary>
        private static string CommonDateText(IEnumerable<string> rawValues)
        {
            var dates = new List<DateTime>();

            foreach (var raw in rawValues)
            {
                DateTime parsed;
                if (!DateTime.TryParseExact(raw, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out parsed))
                {
                    return null;
                }

                dates.Add(parsed);
            }

            if (dates.Count == 0)
            {
                return "";
            }

            if (dates.Select(d => d.Date).Distinct().Count() > 1)
            {
                return null;
            }

            var sameMinute = dates.Select(d => d.ToString("HH:mm", CultureInfo.InvariantCulture)).Distinct().Count() == 1;

            return sameMinute
                ? dates[0].ToString("dd.MM.yyyy HH:mm", Turkish)
                : dates[0].ToString("dd.MM.yyyy", Turkish);
        }

        private static TableCell MakeItemCell(string text, bool bold)
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
                Padding = new Thickness(4)
            };
        }

        /// <summary>Tek malzemelik zimmet tutanağı.</summary>
        private static FlowDocument BuildDocument(Assignment assignment, double pageWidth)
        {
            var document = NewDocument(pageWidth);

            document.Blocks.Add(new Paragraph(new Run("ZİMMET TUTANAĞI"))
            {
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20)
            });

            AddField(document, "Ürün tipi", assignment.TypeName);
            AddField(document, "Sistem adı", assignment.SystemNameText);

            if (!string.IsNullOrEmpty(assignment.SerialNo))
            {
                AddField(document, "Seri No", assignment.SerialNo);
            }

            AddField(document, "Miktar", assignment.Quantity.ToString(Turkish));
            AddField(document, "Zimmet tarihi", assignment.AssignedAtText);

            document.Blocks.Add(new Paragraph { Margin = new Thickness(0, 6, 0, 6) });

            AddField(document, "Teslim alan kişi", assignment.PersonName);
            AddField(document, "Sicil no", string.IsNullOrEmpty(assignment.RegistryNo) ? "-" : assignment.RegistryNo);
            AddField(document, "Birim", string.IsNullOrEmpty(assignment.Department) ? "-" : assignment.Department);

            if (!string.IsNullOrEmpty(assignment.AssignedNote))
            {
                AddField(document, "Not", assignment.AssignedNote);
            }

            document.Blocks.Add(new Paragraph(new Run(
                "Yukarıda belirtilen malzeme, belirtilen tarih itibarıyla adıma zimmetlenmiştir."))
            {
                Margin = new Thickness(0, 30, 0, 40)
            });

            document.Blocks.Add(MakeSignatureTable("Teslim Eden", "Teslim Alan"));

            return document;
        }

        private static Table MakeSignatureTable(string leftTitle, string rightTitle)
        {
            var table = new Table();
            table.Columns.Add(new TableColumn());
            table.Columns.Add(new TableColumn());

            var rowGroup = new TableRowGroup();
            table.RowGroups.Add(rowGroup);

            var row = new TableRow();
            row.Cells.Add(MakeSignatureCell(leftTitle));
            row.Cells.Add(MakeSignatureCell(rightTitle));
            rowGroup.Rows.Add(row);

            return table;
        }

        private static void AddField(FlowDocument document, string label, string value)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 6) };
            paragraph.Inlines.Add(new Run(label + ": ") { FontWeight = FontWeights.SemiBold });
            paragraph.Inlines.Add(new Run(value ?? ""));
            document.Blocks.Add(paragraph);
        }

        private static TableCell MakeSignatureCell(string title)
        {
            var container = new Paragraph
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(20, 0, 20, 0)
            };
            container.Inlines.Add(new Run(title));

            return new TableCell(container)
            {
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 8, 0, 0)
            };
        }

        /// <summary>Tutanağı ekranda gösteren, Yazdır ve Kapat düğmeli önizleme penceresi (kodla kurulur, XAML gerekmez).</summary>
        private sealed class ReceiptPreviewWindow : Window
        {
            public ReceiptPreviewWindow(Window owner, List<Assignment> assignments, bool isReturn, Func<Window, bool> save)
            {
                Title = isReturn ? "Zimmet İade Tutanağı" : "Zimmet Tutanağı";
                Owner = owner;
                Width = 820;
                Height = 720;
                MinWidth = 500;
                MinHeight = 400;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;

                FlowDocument document;
                if (isReturn)
                {
                    document = BuildReturnDocument(assignments, 720);
                }
                else
                {
                    document = assignments.Count == 1
                        ? BuildDocument(assignments[0], 720)
                        : BuildDocument(assignments, 720);
                }
                document.Background = Brushes.White;

                var viewer = new FlowDocumentScrollViewer
                {
                    Document = document,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = Brushes.White
                };

                Action printNow = () =>
                {
                    if (isReturn)
                    {
                        AssignmentReceiptPrinter.PrintReturn(this, assignments);
                    }
                    else
                    {
                        AssignmentReceiptPrinter.Print(this, assignments);
                    }
                };

                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(10)
                };

                var root = new DockPanel();

                if (save == null)
                {
                    // Sadece görüntüleme (Zimmetler defterinden): Yazdır ve Kapat.
                    var printButton = new Button
                    {
                        Content = "Yazdır",
                        Width = 100,
                        Height = 30,
                        Margin = new Thickness(0, 0, 8, 0),
                        IsDefault = true
                    };
                    printButton.Click += (s, e) => printNow();

                    var closeButton = new Button
                    {
                        Content = "Kapat",
                        Width = 100,
                        Height = 30,
                        IsCancel = true
                    };
                    closeButton.Click += (s, e) => Close();

                    buttons.Children.Add(printButton);
                    buttons.Children.Add(closeButton);
                }
                else
                {
                    // Kaydetmeden önce önizleme: kayıt ancak Kaydet'e basılınca yapılır.
                    var banner = new TextBlock
                    {
                        Text = isReturn
                            ? "ÖNİZLEME: İade henüz KAYDEDİLMEDİ. Kaydet'e basınca iade alınır; Vazgeç'e basarsan hiçbir şey değişmez."
                            : "ÖNİZLEME: Zimmet henüz KAYDEDİLMEDİ. Kaydet'e basınca zimmet kaydedilir; Vazgeç'e basarsan hiçbir şey değişmez.",
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Brushes.DarkRed,
                        Background = Brushes.LemonChiffon,
                        Padding = new Thickness(10, 6, 10, 6),
                        TextWrapping = TextWrapping.Wrap
                    };
                    DockPanel.SetDock(banner, Dock.Top);
                    root.Children.Add(banner);

                    var saveButton = new Button
                    {
                        Content = "Kaydet",
                        Width = 100,
                        Height = 30,
                        Margin = new Thickness(0, 0, 8, 0),
                        IsDefault = true
                    };
                    saveButton.Click += (s, e) =>
                    {
                        if (save(this))
                        {
                            DialogResult = true;
                        }
                    };

                    var saveAndPrintButton = new Button
                    {
                        Content = "Kaydet ve Yazdır",
                        Width = 130,
                        Height = 30,
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    saveAndPrintButton.Click += (s, e) =>
                    {
                        if (!save(this))
                        {
                            return;
                        }

                        // Kayıt yapıldı; yazdırma iptal edilse bile kayıt geçerlidir (sonra Zimmetler defterinden yazdırılabilir).
                        printNow();
                        DialogResult = true;
                    };

                    var cancelButton = new Button
                    {
                        Content = "Vazgeç",
                        Width = 100,
                        Height = 30,
                        IsCancel = true
                    };
                    cancelButton.Click += (s, e) => DialogResult = false;

                    buttons.Children.Add(saveButton);
                    buttons.Children.Add(saveAndPrintButton);
                    buttons.Children.Add(cancelButton);
                }

                DockPanel.SetDock(buttons, Dock.Bottom);
                root.Children.Add(buttons);
                root.Children.Add(viewer);

                Content = root;
            }
        }
    }
}