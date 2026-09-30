using System.Windows;
using System.Windows.Controls;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    public partial class ScrapDetailWindow : Window
    {
        private readonly int _scrapProductId;
        private readonly int _quantity;

        /// <summary>
        /// Pencerede bir şey değiştiyse (geri getirildi ya da kalıcı silindi) true olur.
        /// Pencereyi açan ekran buna bakıp listeyi yeniler.
        /// </summary>
        public bool Changed { get; private set; }

        public ScrapDetailWindow(ScrapProduct item)
        {
            InitializeComponent();

            _scrapProductId = item.Id;
            _quantity = item.Quantity;
            TitleText.Text = item.TypeName + " - Hurda Ürün Detayı";
            SubtitleText.Text = string.IsNullOrEmpty(item.ScrappedAt)
                ? "Hurdaya içe aktarıldı"
                : "Hurdaya taşındı: " + item.ScrappedAt;

            BuildView();
        }

        /// <summary>
        /// Hurdada donmuş duran bilgileri, sadece okunur olarak gösterir.
        /// </summary>
        private void BuildView()
        {
            FieldsPanel.Children.Clear();

            FieldsPanel.Children.Add(new TextBlock
            {
                Text = "Adet",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 2)
            });
            FieldsPanel.Children.Add(new TextBlock { Text = _quantity.ToString() });

            foreach (var value in ScrapRepository.GetValues(_scrapProductId))
            {
                var label = new TextBlock
                {
                    Text = value.PropertyName,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 8, 0, 2)
                };

                var valueText = new TextBlock
                {
                    Text = string.IsNullOrEmpty(value.TextValue) ? "(boş)" : value.TextValue
                };

                FieldsPanel.Children.Add(label);
                FieldsPanel.Children.Add(valueText);
            }
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            var error = ScrapRepository.Restore(
                _scrapProductId, text => ScrapWindow.AskRestoreConflict(this, text));

            if (error == ScrapRepository.RestoreCancelled)
            {
                return;
            }

            if (error != null)
            {
                MessageBox.Show(error, "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show("Ürün depoya geri getirildi.", "Depo Durumu",
                MessageBoxButton.OK, MessageBoxImage.Information);

            Changed = true;
            DialogResult = true;
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Bu ürün hurdadan KALICI olarak silinecek.\n\nBu işlem geri alınamaz, onaylıyor musun?",
                "Depo Durumu", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            ScrapRepository.DeletePermanently(_scrapProductId);

            Changed = true;
            DialogResult = true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = Changed;
        }
    }
}