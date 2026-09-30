using System.Windows;
using System.Windows.Controls;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    public partial class AddStatisticWindow : Window
    {
        /// <summary>Yeni bir istatistik kutusu eklemek için.</summary>
        public AddStatisticWindow()
        {
            InitializeComponent();
            TypeCombo.ItemsSource = ProductTypeRepository.GetAll();
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
                MessageBox.Show("Önce bir ürün grubu ve bilgi seç.", "Depo Durumu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            HomeStatisticsRepository.Add(type.Id, property.Id);
            DialogResult = true;
        }
    }
}