using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    public partial class ScrapWindow : Window
    {
        private List<ScrapProduct> _items = new List<ScrapProduct>();

        public ScrapWindow()
        {
            InitializeComponent();
            LoadList();
        }

        private void LoadList()
        {
            _items = ScrapRepository.GetAll();

            ItemsList.ItemsSource = null;
            ItemsList.ItemsSource = _items.Select(BuildSummary).ToList();

            DetailsPanel.Children.Clear();
            RestoreButton.IsEnabled = false;
            DeleteButton.IsEnabled = false;

            EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Listede görünecek kısa satırı oluşturur: cins adı, varsa Seri No, taşınma tarihi.
        /// </summary>
        private string BuildSummary(ScrapProduct item)
        {
            var values = ScrapRepository.GetValues(item.Id);
            var serial = values.FirstOrDefault(v => v.IsSerialNumber && !string.IsNullOrEmpty(v.TextValue));

            var text = item.TypeName;
            if (serial != null)
            {
                text += " - " + serial.PropertyName + ": " + serial.TextValue;
            }
            text += "  (" + item.ScrappedAt + ")";

            return text;
        }

        private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var index = ItemsList.SelectedIndex;
            DetailsPanel.Children.Clear();

            if (index < 0 || index >= _items.Count)
            {
                RestoreButton.IsEnabled = false;
                DeleteButton.IsEnabled = false;
                return;
            }

            var item = _items[index];
            var values = ScrapRepository.GetValues(item.Id);

            var typeLabel = new TextBlock
            {
                Text = "Ürün Cinsi",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 2)
            };
            var typeValue = new TextBlock { Text = item.TypeName, Margin = new Thickness(0, 0, 0, 8) };

            DetailsPanel.Children.Add(typeLabel);
            DetailsPanel.Children.Add(typeValue);

            foreach (var value in values)
            {
                var label = new TextBlock
                {
                    Text = value.PropertyName,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 8, 0, 2)
                };

                var text = string.IsNullOrEmpty(value.TextValue) ? "(boş)" : value.TextValue;
                var valueText = new TextBlock { Text = text };

                DetailsPanel.Children.Add(label);
                DetailsPanel.Children.Add(valueText);
            }

            RestoreButton.IsEnabled = true;
            DeleteButton.IsEnabled = true;
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            var index = ItemsList.SelectedIndex;
            if (index < 0 || index >= _items.Count)
            {
                return;
            }

            var item = _items[index];

            var error = ScrapRepository.Restore(item.Id);
            if (error != null)
            {
                MessageBox.Show(error, "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show("Ürün depoya geri getirildi.", "Depo Durumu",
                MessageBoxButton.OK, MessageBoxImage.Information);

            LoadList();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var index = ItemsList.SelectedIndex;
            if (index < 0 || index >= _items.Count)
            {
                return;
            }

            var item = _items[index];

            var result = MessageBox.Show(
                "Bu kaydı hurdadan kalıcı olarak silmek istediğine emin misin?\n\nBu işlem geri alınamaz.",
                "Depo Durumu", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            ScrapRepository.DeletePermanently(item.Id);
            LoadList();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}