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
        private readonly List<PropertyDefinition> _editable;
        private readonly Dictionary<int, FrameworkElement> _editInputs = new Dictionary<int, FrameworkElement>();
        private TextBox _quantityInput;

        /// <summary>
        /// Pencerede bir şey değiştiyse (düzenlendi) true olur.
        /// Pencereyi açan ekran buna bakıp tabloyu yeniler.
        /// </summary>
        public bool Changed { get; private set; }

        /// <summary>
        /// Çift tıklayınca bilgi penceresi olarak açılır. Sağ tık menüsündeki "Düzenle" ise
        /// startInEditMode: true ile açar ve pencere doğrudan düzenlenebilir kutularla gelir.
        /// </summary>
        public ProductDetailWindow(ProductType type, int productId, bool startInEditMode = false)
        {
            InitializeComponent();

            _type = type;
            _productId = productId;
            TitleText.Text = type.Name + " - Ürün Detayı";

            _properties = TypePropertyRepository.GetForType(type.Id);

            // "Zimmet" özelliği zimmet kayıtlarından hesaplanır; elle düzenlenmez.
            _editable = _properties.Where(p => !PropertyDefinitionRepository.IsZimmet(p)).ToList();

            if (startInEditMode)
            {
                BuildEditMode();
            }
            else
            {
                BuildViewMode();
            }
        }

        /// <summary>
        /// Ürün bilgilerini sadece okunur olarak gösterir.
        /// </summary>
        private void BuildViewMode()
        {
            FieldsPanel.Children.Clear();
            _editInputs.Clear();

            var values = ProductRepository.GetValues(_productId);
            var zimmetSummary = AssignmentRepository.GetActiveSummaryByProduct();

            FieldsPanel.Children.Add(new TextBlock
            {
                Text = "Adet",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 2)
            });
            FieldsPanel.Children.Add(new TextBlock
            {
                Text = ProductRepository.GetQuantity(_productId).ToString()
            });

            foreach (var property in _properties)
            {
                var label = new TextBlock
                {
                    Text = property.Name,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 8, 0, 2)
                };

                string value;
                string emptyText = "(boş)";

                if (PropertyDefinitionRepository.IsZimmet(property))
                {
                    if (!zimmetSummary.TryGetValue(_productId, out value))
                    {
                        value = "";
                    }

                    emptyText = "Zimmette değil";
                }
                else
                {
                    value = values.ContainsKey(property.Id) ? values[property.Id] : "";
                }

                var valueText = new TextBlock
                {
                    Text = string.IsNullOrEmpty(value) ? emptyText : value
                };

                FieldsPanel.Children.Add(label);
                FieldsPanel.Children.Add(valueText);
            }

            SaveButton.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Ürün bilgilerini değiştirilebilir kutular olarak gösterir.
        /// </summary>
        private void BuildEditMode()
        {
            TitleText.Text = _type.Name + " - Ürünü Düzenle";
            FieldsPanel.Children.Clear();
            _editInputs.Clear();

            var values = ProductRepository.GetValues(_productId);

            FieldsPanel.Children.Add(new TextBlock
            {
                Text = "Adet",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 2)
            });

            _quantityInput = new TextBox
            {
                Height = 26,
                VerticalContentAlignment = VerticalAlignment.Center,
                Text = ProductRepository.GetQuantity(_productId).ToString()
            };
            FieldsPanel.Children.Add(_quantityInput);

            foreach (var property in _editable)
            {
                var label = new TextBlock
                {
                    Text = property.Name,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 8, 0, 2)
                };

                var currentValue = values.ContainsKey(property.Id) ? values[property.Id] : "";
                var input = DynamicFieldFactory.CreateInput(property.DataType, currentValue);

                _editInputs[property.Id] = input;

                FieldsPanel.Children.Add(label);
                FieldsPanel.Children.Add(input);
            }

            SaveButton.Visibility = Visibility.Visible;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            int newQuantity;
            if (!int.TryParse(_quantityInput.Text.Trim(), out newQuantity) || newQuantity < 1)
            {
                MessageBox.Show("Adet, 1 veya daha büyük bir tam sayı olmalı.",
                    "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Zimmetteki adetten az olamaz.
            var assignedQuantity = AssignmentRepository.GetActiveQuantity(_productId);
            if (newQuantity < assignedQuantity)
            {
                MessageBox.Show(
                    "Bu üründen " + assignedQuantity + " adet zimmette olduğu için adet " + assignedQuantity +
                    "'nin altına düşürülemez. Önce zimmeti iade alın.",
                    "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Seri No gibi tekil olması gereken özellikleri kaydetmeden önce kontrol et.
            // Değeri değişmemişse sorulmaz (içe aktarmayla aynı seri no'lu gelmiş ürünler de düzenlenebilsin).
            var currentValues = ProductRepository.GetValues(_productId);
            foreach (var property in _editable)
            {
                if (!property.IsSerialNumber)
                {
                    continue;
                }

                var value = DynamicFieldFactory.ReadValue(property.DataType, _editInputs[property.Id]);
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                string currentValue;
                if (currentValues.TryGetValue(property.Id, out currentValue) && currentValue == value)
                {
                    continue;
                }

                var isUsed = ProductRepository.IsValueUsedByAnotherProduct(property.Id, value, _productId);
                if (isUsed)
                {
                    MessageBox.Show(
                        "\"" + property.Name + "\" için girilen \"" + value + "\" değeri zaten başka bir üründe kullanılıyor.",
                        "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (ScrapRepository.IsSerialNumberUsed(value))
                {
                    MessageBox.Show(
                        "\"" + property.Name + "\" için girilen \"" + value + "\" değeri hurdadaki bir üründe kullanılıyor.",
                        "Depo Takip", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var oldValues = ProductRepository.GetValues(_productId);
            var oldQuantity = ProductRepository.GetQuantity(_productId);

            foreach (var property in _editable)
            {
                var value = DynamicFieldFactory.ReadValue(property.DataType, _editInputs[property.Id]);
                ProductRepository.SetValue(_productId, property.Id, value);
            }

            ProductRepository.SetQuantity(_productId, newQuantity);

            var newValues = ProductRepository.GetValues(_productId);
            var changeDescription = BuildChangeDescription(oldValues, newValues);

            if (oldQuantity != newQuantity)
            {
                var quantityChange = "Adet: " + oldQuantity + " -> " + newQuantity;
                changeDescription = string.IsNullOrEmpty(changeDescription)
                    ? quantityChange
                    : changeDescription + " | " + quantityChange;
            }

            if (!string.IsNullOrEmpty(changeDescription))
            {
                LogRepository.Add(_type.Name, changeDescription, "Güncellendi");
            }

            Changed = true;
            DialogResult = true;
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

        /// <summary>
        /// Log defterine yazılacak, o anki değerleri anlatan sabit metni oluşturur.
        /// Seri No doluysa öne alınır, ardından dolu ilk birkaç özellik eklenir.
        /// </summary>
        private string BuildDescription(Dictionary<int, string> values, int quantity)
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

            if (quantity > 1)
            {
                parts.Add("Adet: " + quantity);
            }

            return string.Join(" | ", parts);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = Changed;
        }
    }
}