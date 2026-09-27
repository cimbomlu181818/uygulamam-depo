using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    public partial class AddProductWindow : Window
    {
        private readonly int _productTypeId;
        private readonly List<PropertyDefinition> _properties;
        private readonly Dictionary<int, TextBox> _inputs = new Dictionary<int, TextBox>();

        public AddProductWindow(int productTypeId, string typeName)
        {
            InitializeComponent();

            _productTypeId = productTypeId;
            TitleText.Text = "\"" + typeName + "\" - Yeni Ürün";

            _properties = TypePropertyRepository.GetForType(productTypeId);
            BuildForm();
        }

        /// <summary>
        /// Bu cinse bağlı her özellik için bir etiket + kutu oluşturur.
        /// </summary>
        private void BuildForm()
        {
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
            }

            var productId = ProductRepository.Add(_productTypeId);

            foreach (var property in _properties)
            {
                var value = _inputs[property.Id].Text.Trim();
                ProductRepository.SetValue(productId, property.Id, value);
            }

            DialogResult = true;
        }
    }
}