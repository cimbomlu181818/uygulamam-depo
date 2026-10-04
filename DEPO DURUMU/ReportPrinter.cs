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
    /// Bir raporun tamamı: başlığı, tarih aralığı, seçilen işlemler ve satırlar. Rapor ekranı, yazdırma,
    /// PDF ve Excel aynı veriden çalışır; böylece hepsi birbirinin aynısını gösterir.
    /// </summary>
    public class ReportData
    {
        private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>Kaydedilmişse raporun adı; yeni (henüz kaydedilmemiş) raporda boş.</summary>
        public string Title { get; set; }

        public DateTime RangeFrom { get; set; }
        public DateTime RangeTo { get; set; }

        /// <summary>Rapora dahil edilen işlem adları.</summary>
        public List<string> Operations { get; set; }

        public List<ReportRow> Rows { get; set; }

        /// <summary>Raporun hazırlandığı an. Kaydedilmiş raporda kaydedildiği an.</summary>
        public DateTime CreatedAt { get; set; }

        public string CreatedBy { get; set; }

        /// <summary>Kaydedilmiş raporun veritabanı numarası; yeni raporda null.</summary>
        public int? SavedId { get; set; }

        public ReportData()
        {
            Operations = new List<string>();
            Rows = new List<ReportRow>();
        }

        /// <summary>Kaydedilmiş bir raporu satırlarıyla birlikte okur; yoksa null.</summary>
        public static ReportData FromSaved(int reportId)
        {
            var saved = ReportRepository.Get(reportId);
            if (saved == null)
            {
                return null;
            }

            var data = new ReportData
            {
                Title = saved.Title,
                RangeFrom = ParseRaw(saved.RangeFrom),
                RangeTo = ParseRaw(saved.RangeTo),
                CreatedAt = ParseRaw(saved.CreatedAt),
                CreatedBy = saved.CreatedBy,
                SavedId = saved.Id,
                Rows = ReportRepository.GetRows(reportId)
            };

            if (!string.IsNullOrWhiteSpace(saved.Operations))
            {
                data.Operations = saved.Operations
                    .Split(new[] { "; " }, StringSplitOptions.RemoveEmptyEntries)
                    .ToList();
            }

            return data;
        }

        private static DateTime ParseRaw(string raw)
        {
            DateTime parsed;
            return DateTime.TryParseExact(raw, TimeFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out parsed)
                ? parsed
                : DateTime.MinValue;
        }

        /// <summary>"04.10.2026 00:00 - 04.10.2026 23:59" gibi aralık yazısı.</summary>
        public string RangeText
        {
            get
            {
                return RangeFrom.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) + " - " +
                       RangeTo.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Raporun önerilen adı: tek günlükse "Gün sonu raporu 04.10.2026", değilse aralık.</summary>
        public string SuggestedTitle
        {
            get
            {
                if (RangeFrom.Date == RangeTo.Date)
                {
                    return "Gün sonu raporu " + RangeFrom.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
                }

                return "Rapor " + RangeFrom.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) + " - " +
                       RangeTo.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>İşlem adlarına göre satır sayıları. Örnek: "Zimmetlendi (5), Hurdaya taşındı (2)".</summary>
        public string SummaryText
        {
            get
            {
                return string.Join(", ", Rows
                    .GroupBy(r => r.Operation)
                    .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                    .Select(g => g.Key + " (" + g.Count() + ")"));
            }
        }
    }

    /// <summary>
    /// Raporu yazdırılabilir belge olarak hazırlar ve yazdırır. Rapor ekranındaki görüntü de aynı belgedir;
    /// ekranda ne görünüyorsa kâğıda öyle çıkar.
    /// </summary>
    public static class ReportPrinter
    {
        private static readonly CultureInfo Turkish = new CultureInfo("tr-TR");

        // ---------- YAZDIR ----------

        /// <summary>Yazdırma penceresini açar ve raporu yazdırır. Yazdırıldıysa true döner.</summary>
        public static bool Print(Window owner, ReportData data)
        {
            var printDialog = new PrintDialog();

            if (printDialog.ShowDialog() != true)
            {
                return false;
            }

            try
            {
                FlowDocument document = BuildDocument(data, printDialog.PrintableAreaWidth);
                IDocumentPaginatorSource paginatorSource = document;
                printDialog.PrintDocument(paginatorSource.DocumentPaginator,
                    string.IsNullOrWhiteSpace(data.Title) ? "İşlem Raporu" : data.Title);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "Yazdırılamadı:\n" + ex.Message, "Depo Takip",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            return true;
        }

        // ---------- BELGE ----------

        /// <summary>
        /// Raporun belgesini kurar: başlık, bilgiler, işlem özeti, satır tablosu ve imza satırları.
        /// pageWidth, yazıcının sayfa genişliğidir (0 ya da küçükse varsayılan kullanılır).
        /// </summary>
        public static FlowDocument BuildDocument(ReportData data, double pageWidth)
        {
            var effectivePageWidth = pageWidth > 0 ? pageWidth : 900;

            var document = new FlowDocument
            {
                PageWidth = effectivePageWidth,
                ColumnWidth = effectivePageWidth,
                FontSize = 11,
                PagePadding = new Thickness(30),
                Background = Brushes.White
            };

            document.Blocks.Add(new Paragraph(new Run("İŞLEM RAPORU"))
            {
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            });

            if (!string.IsNullOrWhiteSpace(data.Title))
            {
                AddField(document, "Rapor adı", data.Title);
            }

            AddField(document, "Tarih aralığı", data.RangeText);
            AddField(document, "Hazırlanma zamanı",
                data.CreatedAt.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) +
                (string.IsNullOrWhiteSpace(data.CreatedBy) ? "" : "  |  Hazırlayan: " + data.CreatedBy));
            AddField(document, "İşlemler", data.Rows.Count == 0 ? "-" : data.SummaryText);

            document.Blocks.Add(new Paragraph(new Run("İşlem listesi:"))
            {
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 10, 0, 4)
            });

            document.Blocks.Add(BuildRowsTable(data.Rows));

            document.Blocks.Add(new Paragraph(new Run("Toplam " + data.Rows.Count.ToString(Turkish) + " kayıt."))
            {
                Margin = new Thickness(0, 8, 0, 36)
            });

            document.Blocks.Add(MakeSignatureTable("Teslim Eden", "Teslim Alan"));

            return document;
        }

        private static Table BuildRowsTable(List<ReportRow> rows)
        {
            var table = new Table();

            // No, Zaman, İşlem, Cins, Ürün / Kalem, Kişi / Birim, Detay
            foreach (var width in new[] { 0.4, 1.3, 1.5, 1.1, 2.6, 2.2, 2.4 })
            {
                table.Columns.Add(new TableColumn { Width = new GridLength(width, GridUnitType.Star) });
            }

            var group = new TableRowGroup();
            table.RowGroups.Add(group);

            var header = new TableRow { Background = Brushes.LightGray };
            foreach (var title in new[] { "No", "Zaman", "İşlem", "Cins", "Ürün / Kalem", "Kişi / Birim", "Detay" })
            {
                header.Cells.Add(MakeCell(title, true));
            }
            group.Rows.Add(header);

            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var row = new TableRow();
                row.Cells.Add(MakeCell((i + 1).ToString(Turkish), false));
                row.Cells.Add(MakeCell(r.EventAtText, false));
                row.Cells.Add(MakeCell(r.Operation, false));
                row.Cells.Add(MakeCell(r.TypeName, false));
                row.Cells.Add(MakeCell(r.Item, false));
                row.Cells.Add(MakeCell(r.Person, false));
                row.Cells.Add(MakeCell(r.Detail, false));
                group.Rows.Add(row);
            }

            return table;
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
                Padding = new Thickness(3)
            };
        }

        private static void AddField(FlowDocument document, string label, string value)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 6) };
            paragraph.Inlines.Add(new Run(label + ": ") { FontWeight = FontWeights.SemiBold });
            paragraph.Inlines.Add(new Run(value ?? ""));
            document.Blocks.Add(paragraph);
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
    }
}