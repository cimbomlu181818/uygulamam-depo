using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    public partial class ProductDetailWindow : Window
    {
        private readonly ProductType _type;
        private readonly int _productId;
        private readonly List<PropertyDefinition> _properties;
        private readonly Dictionary<int, TextBox> _editInputs = new Dictionary<int, TextBox>();

        /// <summary>
        /// Pencerede bir şey değiştiyse (düzenlendi ya da silindi) true olur.
        /// Pencereyi açan ekran buna bakıp tabloyu yeniler.
        /// </summary>
        public bool Changed { get; private set; }

        public ProductDetailWindow(ProductType type, int productId)
        {
            InitializeComponent();

            _type = type;
            _productId = productId;
            TitleText.Text = type.Name + " - Ürün Detayı";

            _properties = TypePropertyRepository.GetForType(type.Id);
            BuildViewMode();
        }

        /// <summary>
        /// Ürün bilgilerini sadece okunur olarak gösterir.
        /// </summary>
        private void BuildViewMode()
        {
            FieldsPanel.Children.Clear();
            _editInputs.Clear();

            var values = ProductRepository.GetValues(_productId);

            foreach (var property in _properties)
            {
                var label = new TextBlock
                {
                    Text = property.Name,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 8, 0, 2)
                };

                var value = values.ContainsKey(property.Id) ? values[property.Id] : "";
                var valueText = new TextBlock
                {
                    Text = string.IsNullOrEmpty(value) ? "(boş)" : value
                };

                FieldsPanel.Children.Add(label);
                FieldsPanel.Children.Add(valueText);
            }

            EditButton.Visibility = Visibility.Visible;
            SaveButton.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Ürün bilgilerini değiştirilebilir kutular olarak gösterir.
        /// </summary>
        private void BuildEditMode()
        {
            FieldsPanel.Children.Clear();
            _editInputs.Clear();

            var values = ProductRepository.GetValues(_productId);

            foreach (var property in _properties)
            {
                var label = new TextBlock
                {
                    Text = property.Name,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 8, 0, 2)
                };

                var textBox = new TextBox
                {
                    Height = 26,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Text = values.ContainsKey(property.Id) ? values[property.Id] : ""
                };

                _editInputs[property.Id] = textBox;

                FieldsPanel.Children.Add(label);
                FieldsPanel.Children.Add(textBox);
            }

            EditButton.Visibility = Visibility.Collapsed;
            SaveButton.Visibility = Visibility.Visible;
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            BuildEditMode();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Seri No gibi tekil olması gereken özellikleri kaydetmeden önce kontrol et.
            foreach (var property in _properties)
            {
                if (!property.IsSerialNumber)
                {
                    continue;
                }

                var value = _editInputs[property.Id].Text.Trim();
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                var isUsed = ProductRepository.IsValueUsedByAnotherProduct(property.Id, value, _productId);
                if (isUsed)
                {
                    MessageBox.Show(
                        "\"" + property.Name + "\" için girilen \"" + value + "\" değeri zaten başka bir üründe kullanılıyor.",
                        "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            foreach (var property in _properties)
            {
                var value = _editInputs[property.Id].Text.Trim();
                ProductRepository.SetValue(_productId, property.Id, value);
            }

            Changed = true;
            BuildViewMode();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Bu ürünü silmek istediğine emin misin?",
                "Depo Durumu", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            ProductRepository.Delete(_productId);
            Changed = true;
            DialogResult = true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = Changed;
        }
    }
}