using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    /// <summary>
    /// Yeni rapor akışının ikinci adımı: seçilen aralıkta yapılmış işlemleri listeler,
    /// kullanıcı rapora girmesini istediklerinin yanına tik koyar.
    /// </summary>
    public partial class ReportPickWindow : Window
    {
        /// <summary>Listedeki bir satır. Tik değişince pencerenin özeti güncellensin diye değişikliği bildirir.</summary>
        private class PickItem : INotifyPropertyChanged
        {
            private bool _isChecked;

            public string Name { get; set; }
            public string Group { get; set; }
            public int Count { get; set; }

            public string CountText
            {
                get { return "(" + Count + ")"; }
            }

            public bool IsChecked
            {
                get { return _isChecked; }
                set
                {
                    if (_isChecked == value)
                    {
                        return;
                    }

                    _isChecked = value;
                    var handler = PropertyChanged;
                    if (handler != null)
                    {
                        handler(this, new PropertyChangedEventArgs("IsChecked"));
                    }
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        private readonly List<PickItem> _items = new List<PickItem>();

        /// <summary>Kullanıcının tik koyduğu işlemlerin adları.</summary>
        public List<string> SelectedOperations { get; private set; }

        public ReportPickWindow(DateTime from, DateTime to)
        {
            InitializeComponent();

            RangeText.Text = "Aralık: " + from.ToString("dd.MM.yyyy HH:mm") + "  -  " + to.ToString("dd.MM.yyyy HH:mm");
            SelectedOperations = new List<string>();

            try
            {
                foreach (var operation in ReportCollector.GetOperations(from, to))
                {
                    var item = new PickItem
                    {
                        Name = operation.Name,
                        Group = operation.Group,
                        Count = operation.Count
                    };
                    item.PropertyChanged += (s, e) => UpdateSummary();
                    _items.Add(item);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "İşlemler okunamadı:\n" + ex.Message, "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

            var view = new ListCollectionView(_items);
            view.GroupDescriptions.Add(new PropertyGroupDescription("Group"));
            OperationsList.ItemsSource = view;

            EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            OperationsList.Visibility = _items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

            UpdateSummary();
        }

        private void UpdateSummary()
        {
            var checkedItems = _items.Where(i => i.IsChecked).ToList();
            var rowCount = checkedItems.Sum(i => i.Count);

            SummaryText.Text = checkedItems.Count == 0
                ? "Henüz işlem seçilmedi."
                : checkedItems.Count + " işlem seçildi, rapora " + rowCount + " satır girecek.";

            ContinueButton.IsEnabled = checkedItems.Count > 0;
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items)
            {
                item.IsChecked = true;
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items)
            {
                item.IsChecked = false;
            }
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedOperations = _items.Where(i => i.IsChecked).Select(i => i.Name).ToList();
            DialogResult = true;
        }

        /// <summary>
        /// Pencereyi açar. Kullanıcı Devam derse true döner ve seçilen işlem adlarını verir; iptal ederse false döner.
        /// </summary>
        public static bool TryPick(Window owner, DateTime from, DateTime to, out List<string> operations)
        {
            var window = new ReportPickWindow(from, to) { Owner = owner };

            if (window.ShowDialog() == true)
            {
                operations = window.SelectedOperations;
                return true;
            }

            operations = new List<string>();
            return false;
        }
    }
}