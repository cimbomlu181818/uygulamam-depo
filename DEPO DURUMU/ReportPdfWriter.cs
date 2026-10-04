using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace DEPO_DURUMU
{
    /// <summary>
    /// Raporu doğrudan PDF dosyası olarak yazar (PDFsharp-WPF paketiyle). A4 yatay sayfa, her sayfada
    /// tablo başlığı tekrarlanır, altta sayfa numarası ve son sayfada Teslim Eden / Teslim Alan imza satırları olur.
    /// </summary>
    public static class ReportPdfWriter
    {
        private const string FontName = "Arial";
        private const double Margin = 30;
        private const double FooterHeight = 22;
        private const double CellPadding = 3;

        // No, Zaman, İşlem, Cins, Ürün / Kalem, Kişi / Birim, Detay (yazdırılan belgeyle aynı oranlar)
        private static readonly double[] ColumnWeights = { 0.4, 1.3, 1.5, 1.1, 2.6, 2.2, 2.4 };
        private static readonly string[] Headers =
            { "No", "Zaman", "İşlem", "Cins", "Ürün / Kalem", "Kişi / Birim", "Detay" };

        // ---------- KAYDET (pencereli) ----------

        /// <summary>Dosya adını sorar ve raporu PDF olarak kaydeder. Kaydedildiyse true döner.</summary>
        public static bool SaveWithDialog(Window owner, ReportData data)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Raporu PDF Olarak Kaydet",
                FileName = SafeFileName(data) + ".pdf",
                Filter = "PDF dosyası (*.pdf)|*.pdf"
            };

            if (dialog.ShowDialog(owner) != true)
            {
                return false;
            }

            try
            {
                Save(dialog.FileName, data);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner,
                    "PDF yazılamadı: " + ex.Message + "\n\nDosya başka bir programda açıksa kapatıp tekrar dene.",
                    "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            MessageBox.Show(owner, "Rapor PDF olarak kaydedildi:\n" + dialog.FileName, "Depo Takip",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }

        private static string SafeFileName(ReportData data)
        {
            var name = string.IsNullOrWhiteSpace(data.Title)
                ? "Rapor_" + data.RangeFrom.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                : data.Title.Trim();

            foreach (var bad in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(bad, '_');
            }

            return name;
        }

        // ---------- YAZ ----------

        /// <summary>Raporu verilen yola PDF olarak yazar. Var olan dosyanın üzerine yazar.</summary>
        public static void Save(string path, ReportData data)
        {
            var document = new PdfDocument();
            document.Info.Title = string.IsNullOrWhiteSpace(data.Title) ? "İşlem Raporu" : data.Title;
            document.Info.Creator = "Depo Takip";

            var titleFont = new XFont(FontName, 16, XFontStyleEx.Bold);
            var boldFont = new XFont(FontName, 9, XFontStyleEx.Bold);
            var font = new XFont(FontName, 9, XFontStyleEx.Regular);
            var lineHeight = 9 * 1.3;

            var pages = new List<PdfPage>();
            PdfPage page = null;
            XGraphics gfx = null;
            double width = 0;
            double height = 0;
            double y = 0;

            Action newPage = () =>
            {
                if (gfx != null)
                {
                    gfx.Dispose();
                }

                page = document.AddPage();
                page.Size = PageSize.A4;
                page.Orientation = PageOrientation.Landscape;
                pages.Add(page);

                gfx = XGraphics.FromPdfPage(page);
                width = page.Width.Point;
                height = page.Height.Point;
                y = Margin;
            };

            var contentWidth = 0.0;
            var columnWidths = new double[ColumnWeights.Length];

            newPage();
            contentWidth = width - 2 * Margin;
            var weightSum = ColumnWeights.Sum();
            for (var c = 0; c < columnWidths.Length; c++)
            {
                columnWidths[c] = contentWidth * ColumnWeights[c] / weightSum;
            }

            // Başlık ve bilgi satırları
            gfx.DrawString("İŞLEM RAPORU", titleFont, XBrushes.Black,
                new XRect(Margin, y, contentWidth, 22), XStringFormats.TopCenter);
            y += 32;

            var infoLines = new List<KeyValuePair<string, string>>();
            if (!string.IsNullOrWhiteSpace(data.Title))
            {
                infoLines.Add(new KeyValuePair<string, string>("Rapor adı", data.Title));
            }
            infoLines.Add(new KeyValuePair<string, string>("Tarih aralığı", data.RangeText));
            infoLines.Add(new KeyValuePair<string, string>("Hazırlanma zamanı",
                data.CreatedAt.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) +
                (string.IsNullOrWhiteSpace(data.CreatedBy) ? "" : "  |  Hazırlayan: " + data.CreatedBy)));
            infoLines.Add(new KeyValuePair<string, string>("İşlemler", data.Rows.Count == 0 ? "-" : data.SummaryText));

            foreach (var info in infoLines)
            {
                var label = info.Key + ": ";
                var labelWidth = gfx.MeasureString(label, boldFont).Width;
                var lines = Wrap(gfx, info.Value, font, contentWidth - labelWidth);

                gfx.DrawString(label, boldFont, XBrushes.Black,
                    new XRect(Margin, y, labelWidth, lineHeight), XStringFormats.TopLeft);
                for (var i = 0; i < lines.Count; i++)
                {
                    gfx.DrawString(lines[i], font, XBrushes.Black,
                        new XRect(Margin + labelWidth, y, contentWidth - labelWidth, lineHeight), XStringFormats.TopLeft);
                    y += lineHeight;
                }

                y += 3;
            }

            y += 8;
            gfx.DrawString("İşlem listesi:", boldFont, XBrushes.Black,
                new XRect(Margin, y, contentWidth, lineHeight), XStringFormats.TopLeft);
            y += lineHeight + 4;

            // Tablo
            var limit = new Func<double>(() => height - Margin - FooterHeight);

            Action drawHeader = () =>
            {
                var rowHeight = lineHeight + 2 * CellPadding;
                DrawRow(gfx, Headers.Select(h => new List<string> { h }).ToList(), columnWidths, y, rowHeight,
                    boldFont, lineHeight, true);
                y += rowHeight;
            };

            drawHeader();

            for (var i = 0; i < data.Rows.Count; i++)
            {
                var r = data.Rows[i];
                var values = new[]
                {
                    (i + 1).ToString(CultureInfo.InvariantCulture),
                    r.EventAtText, r.Operation, r.TypeName, r.Item, r.Person, r.Detail
                };

                var cells = new List<List<string>>();
                var maxLines = 1;
                for (var c = 0; c < values.Length; c++)
                {
                    var lines = Wrap(gfx, values[c], font, columnWidths[c] - 2 * CellPadding);
                    cells.Add(lines);
                    maxLines = Math.Max(maxLines, lines.Count);
                }

                var rowHeight = maxLines * lineHeight + 2 * CellPadding;

                if (y + rowHeight > limit())
                {
                    newPage();
                    drawHeader();
                }

                DrawRow(gfx, cells, columnWidths, y, rowHeight, font, lineHeight, false);
                y += rowHeight;
            }

            // Toplam ve imza satırları (sığmazsa yeni sayfaya)
            if (y + 90 > limit())
            {
                newPage();
            }

            y += 10;
            gfx.DrawString("Toplam " + data.Rows.Count.ToString(CultureInfo.InvariantCulture) + " kayıt.",
                font, XBrushes.Black, new XRect(Margin, y, contentWidth, lineHeight), XStringFormats.TopLeft);
            y += lineHeight + 40;

            var pen = new XPen(XColors.Black, 0.8);
            var signWidth = contentWidth / 2 - 60;
            DrawSignature(gfx, "Teslim Eden", Margin + 30, y, signWidth, boldFont, pen);
            DrawSignature(gfx, "Teslim Alan", Margin + contentWidth / 2 + 30, y, signWidth, boldFont, pen);

            gfx.Dispose();
            gfx = null;

            // Sayfa numaraları: toplam sayfa sayısı belli olduktan sonra her sayfanın altına yazılır.
            for (var p = 0; p < pages.Count; p++)
            {
                using (var footer = XGraphics.FromPdfPage(pages[p], XGraphicsPdfPageOptions.Append))
                {
                    footer.DrawString("Sayfa " + (p + 1) + " / " + pages.Count, font, XBrushes.Gray,
                        new XRect(Margin, pages[p].Height.Point - Margin - 12, contentWidth, 12),
                        XStringFormats.TopCenter);
                }
            }

            document.Save(path);
        }

        private static void DrawSignature(XGraphics gfx, string title, double x, double y, double width,
            XFont font, XPen pen)
        {
            gfx.DrawLine(pen, x, y, x + width, y);
            gfx.DrawString(title, font, XBrushes.Black,
                new XRect(x, y + 4, width, 14), XStringFormats.TopCenter);
        }

        /// <summary>Bir tablo satırını çizer: hücre çerçeveleri ve her hücrenin satırları.</summary>
        private static void DrawRow(XGraphics gfx, List<List<string>> cells, double[] columnWidths, double y,
            double rowHeight, XFont font, double lineHeight, bool isHeader)
        {
            var pen = new XPen(XColors.Black, 0.5);
            var x = Margin;

            for (var c = 0; c < cells.Count; c++)
            {
                var rect = new XRect(x, y, columnWidths[c], rowHeight);

                if (isHeader)
                {
                    gfx.DrawRectangle(pen, XBrushes.LightGray, rect);
                }
                else
                {
                    gfx.DrawRectangle(pen, rect);
                }

                for (var i = 0; i < cells[c].Count; i++)
                {
                    gfx.DrawString(cells[c][i], font, XBrushes.Black,
                        new XRect(x + CellPadding, y + CellPadding + i * lineHeight,
                            columnWidths[c] - 2 * CellPadding, lineHeight),
                        isHeader ? XStringFormats.TopCenter : XStringFormats.TopLeft);
                }

                x += columnWidths[c];
            }
        }

        /// <summary>
        /// Metni verilen genişliğe sığacak satırlara böler. Sığmayan uzun sözcükler (seri no vb.) harf harf bölünür.
        /// Boş metin için tek boş satır verir.
        /// </summary>
        private static List<string> Wrap(XGraphics gfx, string text, XFont font, double maxWidth)
        {
            var lines = new List<string>();
            text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();

            if (text.Length == 0)
            {
                lines.Add("");
                return lines;
            }

            var current = "";

            foreach (var word in text.Split(' '))
            {
                if (word.Length == 0)
                {
                    continue;
                }

                var candidate = current.Length == 0 ? word : current + " " + word;
                if (gfx.MeasureString(candidate, font).Width <= maxWidth)
                {
                    current = candidate;
                    continue;
                }

                if (current.Length > 0)
                {
                    lines.Add(current);
                    current = "";
                }

                // Sözcüğün kendisi satıra sığmıyorsa harf harf böl.
                var piece = word;
                while (gfx.MeasureString(piece, font).Width > maxWidth && piece.Length > 1)
                {
                    var take = piece.Length - 1;
                    while (take > 1 && gfx.MeasureString(piece.Substring(0, take), font).Width > maxWidth)
                    {
                        take--;
                    }

                    lines.Add(piece.Substring(0, take));
                    piece = piece.Substring(take);
                }

                current = piece;
            }

            if (current.Length > 0)
            {
                lines.Add(current);
            }

            return lines;
        }
    }
}