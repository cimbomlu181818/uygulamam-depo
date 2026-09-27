using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    public partial class ReorderStatisticsWindow : Window
    {
        private readonly ObservableCollection<HomeStatisticCard> _cards;

        public ReorderStatisticsWindow(List<HomeStatisticCard> cards)
        {
            InitializeComponent();

            _cards = new ObservableCollection<HomeStatisticCard>(cards);
            CardsList.ItemsSource = _cards;

            if (_cards.Count > 0)
            {
                CardsList.SelectedIndex = 0;
            }
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs e)
        {
            var index = CardsList.SelectedIndex;
            if (index <= 0)
            {
                return;
            }

            _cards.Move(index, index - 1);
            CardsList.SelectedIndex = index - 1;
        }

        private void MoveDownButton_Click(object sender, RoutedEventArgs e)
        {
            var index = CardsList.SelectedIndex;
            if (index < 0 || index >= _cards.Count - 1)
            {
                return;
            }

            _cards.Move(index, index + 1);
            CardsList.SelectedIndex = index + 1;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            HomeStatisticsRepository.Reorder(_cards.Select(c => c.Id).ToList());
            DialogResult = true;
        }
    }
}