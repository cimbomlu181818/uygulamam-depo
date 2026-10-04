using System;
using System.Windows;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    /// <summary>
    /// Raporu ekranda gösterir. Yeni hazırlanan raporda Kaydet düğmesi vardır; kaydedilmiş bir rapor
    /// açıldığında Kaydet gizlenir. Yazdır her ikisinde de çalışır.
    /// </summary>
    public partial class ReportViewWindow : Window
    {
        private readonly ReportData _data;

        public ReportViewWindow(ReportData data)
        {
            InitializeComponent();

            _data = data;
            Title = data.SavedId.HasValue ? "Rapor - " + data.Title : "Yeni Rapor";

            // Kaydedilmiş rapor zaten donmuş kopya olarak durur; tekrar kaydedilmez.
            SaveButton.Visibility = data.SavedId.HasValue ? Visibility.Collapsed : Visibility.Visible;

            Viewer.Document = ReportPrinter.BuildDocument(data, 900);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var title = SimpleInputWindow.Ask(this, "Raporu Kaydet", "Rapor adı:", _data.SuggestedTitle);
            if (title == null)
            {
                return;
            }

            try
            {
                _data.SavedId = ReportRepository.Save(
                    title, _data.RangeFrom, _data.RangeTo, _data.Operations, _data.Rows);
                _data.Title = title;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Rapor kaydedilemedi:\n" + ex.Message, "Depo Takip",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Kaydedilince rapor listeye eklenmiştir; çağıran pencere listeyi tazeler.
            DialogResult = true;
        }

        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            ReportPrinter.Print(this, _data);
        }

        private void PdfButton_Click(object sender, RoutedEventArgs e)
        {
            ReportPdfWriter.SaveWithDialog(this, _data);
        }

        private void ExcelButton_Click(object sender, RoutedEventArgs e)
        {
            ReportExcelWriter.SaveWithDialog(this, _data);
        }

        /// <summary>
        /// Raporu gösterir. Yeni rapor kaydedilirse true döner (liste tazelensin diye); aksi halde false.
        /// </summary>
        public static bool Open(Window owner, ReportData data)
        {
            var window = new ReportViewWindow(data) { Owner = owner };
            return window.ShowDialog() == true;
        }
    }
}