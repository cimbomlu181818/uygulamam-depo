using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    public partial class AddStatisticWindow : Window
    {
        // Düzenleme modunda dolu, yeni ekleme modunda null olur.
        private readonly HomeStatisticCard _editingCard;

        /// <summary>Yeni bir istatistik kutusu eklemek için.</summary>
        public AddStatisticWindow() : this(null)
        {
        }

        /// <summary>
        /// editingCard verilirse pencere "düzenleme" modunda açılır: cins/özellik
        /// önceden seçili gelir ve Kaydet'e basınca yeni kutu değil, güncelleme yapılır.
        /// </summary>
        public AddStatisticWindow(HomeStatisticCard editingCard)
        {
            InitializeComponent();
            _editingCard = editingCard;

            var types = ProductTypeRepository.GetAll();
            TypeCombo.ItemsSource = types;

            if (_editingCard == null)
            {
                return;
            }

            Title = "İstatistik Kutusunu Düzenle";
            AddButton.Content = "Kaydet";

            var selectedType = types.FirstOrDefault(t => t.Id == _editingCard.ProductTypeId);
            TypeCombo.SelectedItem = selectedType;

            if (selectedType == null)
            {
                return;
            }

            var properties = TypePropertyRepository.GetForType(selectedType.Id);
            PropertyCombo.ItemsSource = properties;
            PropertyCombo.SelectedItem = properties.FirstOrDefault(p => p.Id == _editingCard.PropertyId);
        }

        private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var type = TypeCombo.SelectedItem as ProductType;

            PropertyCombo.ItemsSource = type == null
                ? null
                : TypePropertyRepository.GetForType(type.Id);
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var type = TypeCombo.SelectedItem as ProductType;
            var property = PropertyCombo.SelectedItem as PropertyDefinition;

            if (type == null || property == null)
            {
                MessageBox.Show("Önce bir ürün cinsi ve özellik seç.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_editingCard != null)
            {
                HomeStatisticsRepository.Update(_editingCard.Id, type.Id, property.Id);
            }
            else
            {
                HomeStatisticsRepository.Add(type.Id, property.Id);
            }

            DialogResult = true;
        }
    }
}