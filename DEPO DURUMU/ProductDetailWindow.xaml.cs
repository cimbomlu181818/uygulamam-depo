using System.Collections.Generic;
using System.Linq;
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

            var oldValues = ProductRepository.GetValues(_productId);

            foreach (var property in _properties)
            {
                var value = _editInputs[property.Id].Text.Trim();
                ProductRepository.SetValue(_productId, property.Id, value);
            }

            var newValues = ProductRepository.GetValues(_productId);
            var changeDescription = BuildChangeDescription(oldValues, newValues);

            if (!string.IsNullOrEmpty(changeDescription))
            {
                LogRepository.Add(_type.Name, changeDescription, "Güncellendi");
            }

            Changed = true;
            BuildViewMode();
        }

        /// <summary>
        /// Kaydetmeden önceki ve sonraki değerleri karşılaştırıp sadece değişen
        /// alanları "Alan: eski -> yeni" biçiminde listeler. Hiçbir şey
        /// değişmediyse boş metin döner (bu durumda log'a hiç yazılmaz).
        /// </summary>
        private string BuildChangeDescription(Dictionary<int, string> oldValues, Dictionary<int, string> newValues)
        {
            var parts = new List<string>();

            foreach (var property in _properties)
            {
                var oldValue = oldValues.ContainsKey(property.Id) ? oldValues[property.Id] : "";
                var newValue = newValues.ContainsKey(property.Id) ? newValues[property.Id] : "";

                if (oldValue == newValue)
                {
                    continue;
                }

                var oldText = string.IsNullOrEmpty(oldValue) ? "(boş)" : oldValue;
                var newText = string.IsNullOrEmpty(newValue) ? "(boş)" : newValue;

                parts.Add(property.Name + ": " + oldText + " -> " + newText);
            }

            return string.Join(" | ", parts);
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

            var descriptionBeforeDelete = BuildDescription(ProductRepository.GetValues(_productId));

            ProductRepository.Delete(_productId);
            LogRepository.Add(_type.Name, descriptionBeforeDelete, "Silindi");

            Changed = true;
            DialogResult = true;
        }

        /// <summary>
        /// Log defterine yazılacak, o anki değerleri anlatan sabit metni oluşturur.
        /// Seri No doluysa öne alınır, ardından dolu ilk birkaç özellik eklenir.
        /// </summary>
        private string BuildDescription(Dictionary<int, string> values)
        {
            var parts = new List<string>();

            var serial = _properties.FirstOrDefault(p => p.IsSerialNumber);
            if (serial != null && values.ContainsKey(serial.Id) && !string.IsNullOrEmpty(values[serial.Id]))
            {
                parts.Add(serial.Name + ": " + values[serial.Id]);
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

                if (values.ContainsKey(property.Id) && !string.IsNullOrEmpty(values[property.Id]))
                {
                    parts.Add(property.Name + ": " + values[property.Id]);
                }
            }

            return string.Join(" | ", parts);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = Changed;
        }
    }
}