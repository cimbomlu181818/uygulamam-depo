using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DEPO_DURUMU.Data;

namespace DEPO_DURUMU
{
    /// <summary>
    /// Renklerin ne anlama geldiğini gösteren ve değiştirmeye yarayan liste penceresi.
    /// </summary>
    public partial class ColorSettingsWindow : Window
    {
        private readonly Dictionary<string, TextBox> _boxes = new Dictionary<string, TextBox>();
        private readonly Dictionary<string, string> _original;

        public ColorSettingsWindow()
        {
            InitializeComponent();

            _original = ColorRepository.GetAllMeanings();

            foreach (var choice in ColorRepository.Palette)
            {
                string meaning;
                _original.TryGetValue(choice.Key, out meaning);

                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var swatch = new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(choice.Hex)),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                Grid.SetColumn(swatch, 0);
                row.Children.Add(swatch);

                var name = new TextBlock
                {
                    Text = choice.Name,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(name, 1);
                row.Children.Add(name);

                var box = new TextBox
                {
                    Height = 26,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Text = meaning ?? ""
                };
                Grid.SetColumn(box, 2);
                row.Children.Add(box);

                _boxes[choice.Key] = box;
                ColorList.Children.Add(row);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var pair in _boxes)
            {
                var text = pair.Value.Text.Trim();

                string before;
                _original.TryGetValue(pair.Key, out before);
                before = before ?? "";

                // Sadece değişenler yazılır; hiç anlamı olmayan renge boş kayıt açılmaz.
                if (text != before)
                {
                    ColorRepository.SetMeaning(pair.Key, text);
                }
            }

            DialogResult = true;
        }
    }
}