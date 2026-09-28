using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    public partial class ProductDetailWindow : Window
    {
        private const string SystemNameFieldName = "Sistem İsmi";

        private readonly ProductType _type;
        private readonly int _productId;
        private readonly List<PropertyDefinition> _properties;
        private readonly Dictionary<int, TextBox> _editInputs = new Dictionary<int, TextBox>();
        private TextBox _quantityInput;

        /// <summary>
        /// Pencerede bir şey değiştiyse (düzenlendi, silindi ya da zimmetlendi) true olur.
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
        /// Ürün bilgilerini sadece okunur olarak gösterir; altında zimmet durumu görünür.
        /// </summary>
        private void BuildViewMode()
        {
            FieldsPanel.Children.Clear();
            _editInputs.Clear();

            var values = ProductRepository.GetValues(_productId);

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

                var value = values.ContainsKey(property.Id) ? values[property.Id] : "";
                var valueText = new TextBlock
                {
                    Text = string.IsNullOrEmpty(value) ? "(boş)" : value
                };

                FieldsPanel.Children.Add(label);
                FieldsPanel.Children.Add(valueText);
            }

            AddAssignmentSection();

            EditButton.Visibility = Visibility.Visible;
            SaveButton.Visibility = Visibility.Collapsed;
            AssignButton.Visibility = Visibility.Visible;
            HandoverButton.Visibility = Visibility.Visible;
        }

        /// <summary>Ürünün şu an kimde olduğunu gösterir; her zimmetin yanında tutanak yazdırma düğmesi vardır.</summary>
        private void AddAssignmentSection()
        {
            FieldsPanel.Children.Add(new TextBlock
            {
                Text = "Zimmet",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 14, 0, 2)
            });

            var active = AssignmentRepository.GetActiveForProduct(_productId);

            if (active.Count == 0)
            {
                FieldsPanel.Children.Add(new TextBlock { Text = "Zimmette değil", Foreground = System.Windows.Media.Brushes.Gray });
                return;
            }

            foreach (var assignment in active)
            {
                var line = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };

                var printButton = new Button
                {
                    Content = "Tutanak Yazdır",
                    Width = 100,
                    Height = 24,
                    Tag = assignment
                };
                printButton.Click += PrintAssignmentButton_Click;
                DockPanel.SetDock(printButton, Dock.Right);
                line.Children.Add(printButton);

                var text = assignment.PersonName;
                if (!string.IsNullOrEmpty(assignment.Department))
                {
                    text += " (" + assignment.Department + ")";
                }
                text += " — " + assignment.Quantity + " adet, " + assignment.AssignedAtText;

                line.Children.Add(new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center
                });

                FieldsPanel.Children.Add(line);
            }
        }

        private void PrintAssignmentButton_Click(object sender, RoutedEventArgs e)
        {
            var assignment = (sender as Button)?.Tag as Assignment;
            if (assignment != null)
            {
                AssignmentReceiptPrinter.Print(this, assignment);
            }
        }

        /// <summary>
        /// Ürün bilgilerini değiştirilebilir kutular olarak gösterir.
        /// </summary>
        private void BuildEditMode()
        {
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
            AssignButton.Visibility = Visibility.Collapsed;
            HandoverButton.Visibility = Visibility.Collapsed;
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            BuildEditMode();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            int newQuantity;
            if (!int.TryParse(_quantityInput.Text.Trim(), out newQuantity) || newQuantity < 1)
            {
                MessageBox.Show("Adet, 1 veya daha büyük bir tam sayı olmalı.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Zimmetteki adetten az olamaz.
            var assignedQuantity = AssignmentRepository.GetActiveQuantity(_productId);
            if (newQuantity < assignedQuantity)
            {
                MessageBox.Show(
                    "Bu üründen " + assignedQuantity + " adet zimmette olduğu için adet " + assignedQuantity +
                    "'nin altına düşürülemez. Önce zimmeti iade alın.",
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

                if (ScrapRepository.IsSerialNumberUsed(value))
                {
                    MessageBox.Show(
                        "\"" + property.Name + "\" için girilen \"" + value + "\" değeri hurdadaki bir üründe kullanılıyor.",
                        "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var oldValues = ProductRepository.GetValues(_productId);
            var oldQuantity = ProductRepository.GetQuantity(_productId);

            foreach (var property in _properties)
            {
                var value = _editInputs[property.Id].Text.Trim();
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

        // ---------- ZİMMETLE / TESLİM-TESELLÜM ----------

        private void AssignButton_Click(object sender, RoutedEventArgs e)
        {
            var available = AssignmentRepository.GetAvailableQuantity(_productId);
            if (available < 1)
            {
                MessageBox.Show(
                    "Bu ürünün tamamı zaten zimmette. Yeniden zimmetlemek için önce iade alın.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var description = _type.Name;
            var summary = BuildDescription(ProductRepository.GetValues(_productId), 1);
            if (!string.IsNullOrEmpty(summary))
            {
                description += " — " + summary;
            }

            var window = new AssignWindow(_productId, description, available) { Owner = this };
            if (window.ShowDialog() == true)
            {
                Changed = true;
                BuildViewMode();
            }
        }

        /// <summary>Teslim-Tesellüm tutanağını, ilk satırı bu ürünün seri no ve sistem ismiyle dolu olarak açar.</summary>
        private void HandoverButton_Click(object sender, RoutedEventArgs e)
        {
            var values = ProductRepository.GetValues(_productId);

            var serialProperty = _properties.FirstOrDefault(p => p.IsSerialNumber);
            var serialNo = serialProperty != null && values.ContainsKey(serialProperty.Id)
                ? values[serialProperty.Id]
                : "";

            var systemProperty = _properties.FirstOrDefault(p => p.Name == SystemNameFieldName);
            var systemName = systemProperty != null && values.ContainsKey(systemProperty.Id)
                ? values[systemProperty.Id]
                : "";

            if (string.IsNullOrWhiteSpace(systemName))
            {
                systemName = _type.Name;
            }

            var window = new HandoverWindow(serialNo, systemName) { Owner = this };
            window.ShowDialog();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (AssignmentRepository.GetActiveQuantity(_productId) > 0)
            {
                MessageBox.Show(
                    "Bu ürün zimmette olduğu için silinemez. Önce Zimmetler ekranından iade alın.",
                    "Depo Durumu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                "Bu ürünü silmek istediğine emin misin?",
                "Depo Durumu", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            var descriptionBeforeDelete = BuildDescription(
                ProductRepository.GetValues(_productId), ProductRepository.GetQuantity(_productId));

            ProductRepository.Delete(_productId);
            LogRepository.Add(_type.Name, descriptionBeforeDelete, "Silindi");

            Changed = true;
            DialogResult = true;
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
