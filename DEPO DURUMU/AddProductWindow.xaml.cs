using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    public partial class AddProductWindow : Window
    {
        private readonly int _productTypeId;
        private readonly string _typeName;
        private readonly List<PropertyDefinition> _properties;
        private readonly Dictionary<int, TextBox> _inputs = new Dictionary<int, TextBox>();
        private TextBox _quantityBox;

        public AddProductWindow(int productTypeId, string typeName)
        {
            InitializeComponent();

            _productTypeId = productTypeId;
            _typeName = typeName;
            TitleText.Text = "\"" + typeName + "\" - Yeni Ürün";

            _properties = TypePropertyRepository.GetForType(productTypeId);
            BuildForm();
        }

        /// <summary>
        /// Bu cinse bağlı her özellik için bir etiket + kutu oluşturur.
        /// </summary>
        private void BuildForm()
        {
            // Her ürünün bir adedi vardır; en üstte, varsayılan 1 olarak sorulur.
            FieldsPanel.Children.Add(new TextBlock
            {
                Text = "Adet",
                Margin = new Thickness(0, 8, 0, 2)
            });

            _quantityBox = new TextBox
            {
                Height = 26,
                VerticalContentAlignment = VerticalAlignment.Center,
                Text = "1"
            };
            FieldsPanel.Children.Add(_quantityBox);

            foreach (var property in _properties)
            {
                var label = new TextBlock
                {
                    Text = property.Name,
                    Margin = new Thickness(0, 8, 0, 2)
                };

                var textBox = new TextBox
                {
                    Height = 26,
                    VerticalContentAlignment = VerticalAlignment.Center
                };

                _inputs[property.Id] = textBox;

                FieldsPanel.Children.Add(label);
                FieldsPanel.Children.Add(textBox);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            int quantity;
            if (!int.TryParse(_quantityBox.Text.Trim(), out quantity) || quantity < 1)
            {
                MessageBox.Show("Adet, 1 veya daha büyük bir tam sayı olmalı.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Seri No gibi tekil olması gereken özellikleri kaydetmeden önce kontrol et.
            foreach (var property in _properties)
            {
                if (!property.IsSerialNumber)
                {
                    continue;
                }

                var value = _inputs[property.Id].Text.Trim();
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                var isUsed = ProductRepository.IsValueUsedByAnotherProduct(property.Id, value, -1);
                if (isUsed)
                {
                    MessageBox.Show(
                        "\"" + property.Name + "\" için girilen \"" + value + "\" değeri zaten başka bir üründe kullanılıyor.",
                        "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (ScrapRepository.IsSerialNumberUsed(value))
                {
                    MessageBox.Show(
                        "\"" + property.Name + "\" için girilen \"" + value + "\" değeri hurdadaki bir üründe kullanılıyor.",
                        "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var productId = ProductRepository.Add(_productTypeId, quantity);

            foreach (var property in _properties)
            {
                var value = _inputs[property.Id].Text.Trim();
                ProductRepository.SetValue(productId, property.Id, value);
            }

            LogRepository.Add(_typeName, BuildDescription(quantity), "Eklendi");

            DialogResult = true;
        }

        /// <summary>
        /// Log defterine yazılacak, o anki değerleri anlatan sabit metni oluşturur.
        /// Seri No doluysa öne alınır, ardından dolu ilk birkaç özellik eklenir.
        /// </summary>
        private string BuildDescription(int quantity)
        {
            var parts = new List<string>();

            var serial = _properties.FirstOrDefault(p => p.IsSerialNumber);
            if (serial != null)
            {
                var value = _inputs[serial.Id].Text.Trim();
                if (!string.IsNullOrEmpty(value))
                {
                    parts.Add(serial.Name + ": " + value);
                }
            }

            foreach (var property in _properties)
            {
                if (parts.Count >= 3)
                {
                    break;
                }

                if (property.IsSerialNumber)
                {
                    continue;
                }

                var value = _inputs[property.Id].Text.Trim();
                if (!string.IsNullOrEmpty(value))
                {
                    parts.Add(property.Name + ": " + value);
                }
            }

            if (quantity > 1)
            {
                parts.Add("Adet: " + quantity);
            }

            return string.Join(" | ", parts);
        }
    }
}