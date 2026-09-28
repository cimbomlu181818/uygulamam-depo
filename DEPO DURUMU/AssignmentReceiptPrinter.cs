using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    /// <summary>
    /// Bir zimmet kaydı için tutanak hazırlayıp yazdırır.
    /// Hem ürün detayı ekranından hem de Zimmetler defterinden çağrılır.
    /// </summary>
    public static class AssignmentReceiptPrinter
    {
        private static readonly CultureInfo Turkish = new CultureInfo("tr-TR");

        /// <summary>Tutanağı yazdırır. Yazdırma tamamlanırsa true, iptal edilir ya da hata verirse false döner.</summary>
        public static bool Print(Window owner, Assignment assignment)
        {
            var printDialog = new PrintDialog();

            if (printDialog.ShowDialog() != true)
            {
                return false;
            }

            FlowDocument document = BuildDocument(assignment, printDialog.PrintableAreaWidth);
            IDocumentPaginatorSource paginatorSource = document;

            try
            {
                printDialog.PrintDocument(paginatorSource.DocumentPaginator, "Zimmet tutanağı");
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "Yazdırılamadı:\n" + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            try
            {
                LogRepository.Add(assignment.TypeName,
                    assignment.ProductText + " | " + assignment.PersonName,
                    "Zimmet tutanağı yazdırıldı");
            }
            catch (Exception)
            {
                // Yazdırma zaten tamamlandı; log yazılamaması yazdırmayı engellemez.
            }

            return true;
        }

        private static FlowDocument BuildDocument(Assignment assignment, double pageWidth)
        {
            var document = new FlowDocument
            {
                PageWidth = pageWidth > 0 ? pageWidth : 750,
                FontSize = 12,
                PagePadding = new Thickness(30)
            };

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

            var table = new Table();
            table.Columns.Add(new TableColumn());
            table.Columns.Add(new TableColumn());

            var rowGroup = new TableRowGroup();
            table.RowGroups.Add(rowGroup);

            var row = new TableRow();
            row.Cells.Add(MakeSignatureCell("Teslim Eden"));
            row.Cells.Add(MakeSignatureCell("Teslim Alan"));
            rowGroup.Rows.Add(row);

            document.Blocks.Add(table);

            return document;
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
    }
}
