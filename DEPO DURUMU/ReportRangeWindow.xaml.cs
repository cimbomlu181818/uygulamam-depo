using System;
using System.Globalization;
using System.Windows;

namespace DEPO_DURUMU
{
    /// <summary>
    /// Yeni rapor akışının birinci adımı: rapora girecek işlemlerin tarih ve saat aralığını seçtirir.
    /// </summary>
    public partial class ReportRangeWindow : Window
    {
        private static readonly string[] TimeFormats = { "HH:mm", "H:mm" };

        /// <summary>Seçilen aralığın başı.</summary>
        public DateTime RangeFrom { get; private set; }

        /// <summary>Seçilen aralığın sonu. Yazılan dakikanın sonuna kadar (saniye 59) dahildir.</summary>
        public DateTime RangeTo { get; private set; }

        public ReportRangeWindow()
        {
            InitializeComponent();
            SetRange(DateTime.Today, DateTime.Today, "00:00", "23:59");
        }

        private void SetRange(DateTime fromDate, DateTime toDate, string fromTime, string toTime)
        {
            FromDatePicker.SelectedDate = fromDate;
            ToDatePicker.SelectedDate = toDate;
            FromTimeBox.Text = fromTime;
            ToTimeBox.Text = toTime;
        }

        private void TodayButton_Click(object sender, RoutedEventArgs e)
        {
            SetRange(DateTime.Today, DateTime.Today, "00:00", "23:59");
        }

        private void YesterdayButton_Click(object sender, RoutedEventArgs e)
        {
            var yesterday = DateTime.Today.AddDays(-1);
            SetRange(yesterday, yesterday, "00:00", "23:59");
        }

        private void ThisWeekButton_Click(object sender, RoutedEventArgs e)
        {
            // Hafta pazartesi başlar.
            var daysSinceMonday = ((int)DateTime.Today.DayOfWeek + 6) % 7;
            SetRange(DateTime.Today.AddDays(-daysSinceMonday), DateTime.Today, "00:00", "23:59");
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            DateTime from;
            DateTime to;

            if (!TryRead(FromDatePicker, FromTimeBox, "Başlangıç", out from) ||
                !TryRead(ToDatePicker, ToTimeBox, "Bitiş", out to))
            {
                return;
            }

            // Yazılan dakika dahil olsun: 17:00 yazılınca 17:00:59'a kadar.
            to = to.AddSeconds(59);

            if (to < from)
            {
                MessageBox.Show(this, "Bitiş, başlangıçtan önce olamaz.", "Depo Takip",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            RangeFrom = from;
            RangeTo = to;
            DialogResult = true;
        }

        private bool TryRead(System.Windows.Controls.DatePicker picker, System.Windows.Controls.TextBox timeBox,
            string label, out DateTime result)
        {
            result = DateTime.MinValue;

            if (picker.SelectedDate == null)
            {
                MessageBox.Show(this, label + " tarihini seç.", "Depo Takip",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                picker.Focus();
                return false;
            }

            DateTime time;
            if (!DateTime.TryParseExact((timeBox.Text ?? "").Trim(), TimeFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out time))
            {
                MessageBox.Show(this, label + " saatini 24 saatlik yaz (örnek 08:30).", "Depo Takip",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                timeBox.Focus();
                timeBox.SelectAll();
                return false;
            }

            result = picker.SelectedDate.Value.Date + time.TimeOfDay;
            return true;
        }

        /// <summary>
        /// Pencereyi açar. Kullanıcı Devam derse true döner ve aralığı verir; iptal ederse false döner.
        /// </summary>
        public static bool TryAsk(Window owner, out DateTime from, out DateTime to)
        {
            var window = new ReportRangeWindow { Owner = owner };

            if (window.ShowDialog() == true)
            {
                from = window.RangeFrom;
                to = window.RangeTo;
                return true;
            }

            from = DateTime.MinValue;
            to = DateTime.MinValue;
            return false;
        }
    }
}