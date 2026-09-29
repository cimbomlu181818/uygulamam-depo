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

            var window = new ReceiptPreviewWindow(owner, assignments);
            window.ShowDialog();
        }

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

            var effectivePageWidth = pageWidth > 0 ? pageWidth : 750;

            var document = new FlowDocument
            {
                PageWidth = effectivePageWidth,
                ColumnWidth = effectivePageWidth,
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

            // Malzeme listesi: teslim-tesellüm tutanağıyla aynı yapı (tüm sütunlar oransal / Star).
            var itemsTable = new Table();
            var columnWidths = new[] { 0.5, 3.0, 2.0, 1.0 }; // No, Malzeme, Seri No, Miktar
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
            itemsGroup.Rows.Add(headerRow);

            for (var i = 0; i < assignments.Count; i++)
            {
                var a = assignments[i];
                var itemRow = new TableRow();
                itemRow.Cells.Add(MakeItemCell((i + 1).ToString(Turkish), false));
                itemRow.Cells.Add(MakeItemCell(a.SystemNameText, false));
                itemRow.Cells.Add(MakeItemCell(string.IsNullOrEmpty(a.SerialNo) ? "-" : a.SerialNo, false));
                itemRow.Cells.Add(MakeItemCell(a.Quantity.ToString(Turkish), false));
                itemsGroup.Rows.Add(itemRow);
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

        private static FlowDocument BuildDocument(Assignment assignment, double pageWidth)
        {
            var effectivePageWidth = pageWidth > 0 ? pageWidth : 750;

            var document = new FlowDocument
            {
                PageWidth = effectivePageWidth,
                ColumnWidth = effectivePageWidth,
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

        /// <summary>Tutanağı ekranda gösteren, Yazdır ve Kapat düğmeli önizleme penceresi (kodla kurulur, XAML gerekmez).</summary>
        private sealed class ReceiptPreviewWindow : Window
        {
            public ReceiptPreviewWindow(Window owner, List<Assignment> assignments)
            {
                Title = "Zimmet Tutanağı";
                Owner = owner;
                Width = 820;
                Height = 720;
                MinWidth = 500;
                MinHeight = 400;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;

                var document = assignments.Count == 1
                    ? BuildDocument(assignments[0], 720)
                    : BuildDocument(assignments, 720);
                document.Background = Brushes.White;

                var viewer = new FlowDocumentScrollViewer
                {
                    Document = document,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = Brushes.White
                };

                var printButton = new Button
                {
                    Content = "Yazdır",
                    Width = 100,
                    Height = 30,
                    Margin = new Thickness(0, 0, 8, 0),
                    IsDefault = true
                };
                printButton.Click += (s, e) => AssignmentReceiptPrinter.Print(this, assignments);

                var closeButton = new Button
                {
                    Content = "Kapat",
                    Width = 100,
                    Height = 30,
                    IsCancel = true
                };
                closeButton.Click += (s, e) => Close();

                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(10)
                };
                buttons.Children.Add(printButton);
                buttons.Children.Add(closeButton);

                var root = new DockPanel();
                DockPanel.SetDock(buttons, Dock.Bottom);
                root.Children.Add(buttons);
                root.Children.Add(viewer);

                Content = root;
            }
        }
    }
}