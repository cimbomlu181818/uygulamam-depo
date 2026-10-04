using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    /// <summary>
    /// Kayıtlı raporların listesi. Buradan yeni rapor hazırlanır (tarih aralığı, işlem seçme, rapor ekranı);
    /// kayıtlı raporlar açılır, yazdırılır, PDF/Excel olarak kaydedilir ya da silinir.
    /// Raporlar kaydedildikleri andaki donmuş kopyadır; ürün ya da zimmet sonradan değişse de rapor aynı kalır.
    /// </summary>
    public partial class ReportsWindow : Window
    {
        public ReportsWindow()
        {
            InitializeComponent();
            LoadReports();
        }

        private void LoadReports()
        {
            List<SavedReport> reports;

            try
            {
                reports = ReportRepository.GetAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Raporlar okunamadı:\n" + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                reports = new List<SavedReport>();
            }

            ReportsGrid.ItemsSource = reports;
            ReportsGrid.Visibility = reports.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            EmptyText.Visibility = reports.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            CountText.Text = reports.Count + " rapor";
        }

        // ---------- YENİ RAPOR ----------

        /// <summary>Yeni ekle: tarih aralığı, işlem seçme, rapor ekranı. Rapor ekranında Kaydet denirse liste tazelenir.</summary>
        private void NewButton_Click(object sender, RoutedEventArgs e)
        {
            DateTime from;
            DateTime to;
            if (!ReportRangeWindow.TryAsk(this, out from, out to))
            {
                return;
            }

            List<string> operations;
            if (!ReportPickWindow.TryPick(this, from, to, out operations))
            {
                return;
            }

            List<ReportRow> rows;
            try
            {
                rows = ReportCollector.Collect(from, to, operations);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "İşlemler okunamadı:\n" + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var data = new ReportData
            {
                RangeFrom = from,
                RangeTo = to,
                Operations = operations,
                Rows = rows,
                CreatedAt = DateTime.Now,
                CreatedBy = Environment.UserName
            };

            if (ReportViewWindow.Open(this, data))
            {
                LoadReports();
            }
        }

        // ---------- SATIRDAKİ DÜĞMELER ----------

        /// <summary>Tıklanan düğmenin bulunduğu satırdaki raporu verir.</summary>
        private static SavedReport ReportOf(object sender)
        {
            var element = sender as FrameworkElement;
            return element == null ? null : element.DataContext as SavedReport;
        }

        /// <summary>Kaydedilmiş raporu satırlarıyla birlikte okur; okunamazsa kullanıcıya söyler ve null verir.</summary>
        private ReportData Load(SavedReport report)
        {
            if (report == null)
            {
                return null;
            }

            try
            {
                var data = ReportData.FromSaved(report.Id);
                if (data == null)
                {
                    MessageBox.Show(this, "Bu rapor bulunamadı. Liste tazelenecek.", "Depo Durumu",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    LoadReports();
                }

                return data;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Rapor okunamadı:\n" + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private void OpenReport(SavedReport report)
        {
            var data = Load(report);
            if (data != null)
            {
                ReportViewWindow.Open(this, data);
            }
        }

        private void OpenRow_Click(object sender, RoutedEventArgs e)
        {
            OpenReport(ReportOf(sender));
        }

        private void PrintRow_Click(object sender, RoutedEventArgs e)
        {
            var data = Load(ReportOf(sender));
            if (data != null)
            {
                ReportPrinter.Print(this, data);
            }
        }

        private void PdfRow_Click(object sender, RoutedEventArgs e)
        {
            var data = Load(ReportOf(sender));
            if (data != null)
            {
                ReportPdfWriter.SaveWithDialog(this, data);
            }
        }

        private void ExcelRow_Click(object sender, RoutedEventArgs e)
        {
            var data = Load(ReportOf(sender));
            if (data != null)
            {
                ReportExcelWriter.SaveWithDialog(this, data);
            }
        }

        private void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            var report = ReportOf(sender);
            if (report == null)
            {
                return;
            }

            var answer = MessageBox.Show(this,
                report.Title + "\n" + report.RangeText + " | " + report.RowCount + " kayıt" +
                "\n\nBu rapor KALICI olarak silinecek (stok, zimmet ve tutanaklar etkilenmez). Onaylıyor musun?",
                "Depo Durumu", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                ReportRepository.Delete(report.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Silinemedi:\n" + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LoadReports();
        }

        /// <summary>Satıra çift tıklayınca rapor açılır (başlığa ya da boş yere çift tıklamak bir şey yapmaz).</summary>
        private void ReportsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;

            while (source != null && !(source is DataGridRow) && !(source is Button))
            {
                source = VisualTreeHelper.GetParent(source);
            }

            // Düğmenin üstüne çift tıklandıysa düğmenin kendi işi yapılır, ayrıca açılmaz.
            var row = source as DataGridRow;
            if (row == null)
            {
                return;
            }

            OpenReport(row.Item as SavedReport);
        }
    }
}
