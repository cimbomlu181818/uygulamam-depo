using System;
using System.Collections.Generic;
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

            var printDialog = new PrintDialog();

            if (printDialog.ShowDialog() != true)
            {
                return false;
            }

            FlowDocument document = BuildDocument(assignments, printDialog.PrintableAreaWidth);
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

            foreach (var assignment in assignments)
            {
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
            }

            return true;
        }

        private static FlowDocument BuildDocument(List<Assignment> assignments, double pageWidth)
        {
            var first = assignments[0];

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

            AddField(document, "Zimmet tarihi", first.AssignedAtText);
            AddField(document, "Teslim alan kişi", first.PersonName);
            AddField(document, "Sicil no", string.IsNullOrEmpty(first.RegistryNo) ? "-" : first.RegistryNo);
            AddField(document, "Birim", string.IsNullOrEmpty(first.Department) ? "-" : first.Department);

            if (!string.IsNullOrEmpty(first.AssignedNote))
            {
                AddField(document, "Not", first.AssignedNote);
            }

            document.Blocks.Add(new Paragraph(new Run("Teslim edilen malzemeler:"))
            {
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 10, 0, 4)
            });

            var itemsTable = new Table { CellSpacing = 0 };
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(40) });
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(170) });
            itemsTable.Columns.Add(new TableColumn { Width = new GridLength(60) });

            var itemsGroup = new TableRowGroup();
            itemsTable.RowGroups.Add(itemsGroup);
            itemsGroup.Rows.Add(MakeItemRow(new[] { "No", "Malzeme", "Seri No", "Miktar" }, true));

            for (var i = 0; i < assignments.Count; i++)
            {
                var a = assignments[i];
                itemsGroup.Rows.Add(MakeItemRow(new[]
                {
                    (i + 1).ToString(Turkish),
                    a.SystemNameText,
                    string.IsNullOrEmpty(a.SerialNo) ? "-" : a.SerialNo,
                    a.Quantity.ToString(Turkish)
                }, false));
            }

            document.Blocks.Add(itemsTable);

            document.Blocks.Add(new Paragraph(new Run(
                "Yukarıda listelenen malzemeler, belirtilen tarih itibarıyla adıma zimmetlenmiştir."))
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

        private static TableRow MakeItemRow(string[] cells, bool bold)
        {
            var row = new TableRow();

            foreach (var text in cells)
            {
                var paragraph = new Paragraph(new Run(text ?? "")) { Margin = new Thickness(0) };

                if (bold)
                {
                    paragraph.FontWeight = FontWeights.SemiBold;
                }

                row.Cells.Add(new TableCell(paragraph)
                {
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(4, 2, 4, 2)
                });
            }

            return row;
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
