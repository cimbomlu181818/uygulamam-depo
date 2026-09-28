using System.Windows;
using System.Windows.Controls;

namespace DEPO_DURUMU.Data
{
    /// <summary>
    /// Ürün ekleme/düzenleme formlarında, bir özelliğin veri tipine göre doğru
    /// giriş kontrolünü (metin kutusu veya Evet/Hayır kutucuğu) oluşturur/okur.
    /// Böylece bu mantık tek bir yerde durur, her pencerede tekrar yazılmaz.
    /// </summary>
    public static class DynamicFieldFactory
    {
        public const string YesNoDataType = "Evet/Hayır";

        /// <summary>Verilen veri tipine ve mevcut değere göre bir giriş kontrolü oluşturur.</summary>
        public static FrameworkElement CreateInput(string dataType, string value)
        {
            if (dataType == YesNoDataType)
            {
                return new CheckBox
                {
                    Content = "Evet",
                    IsChecked = value == "Evet",
                    Margin = new Thickness(0, 4, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            return new TextBox
            {
                Height = 26,
                VerticalContentAlignment = VerticalAlignment.Center,
                Text = value ?? ""
            };
        }

        /// <summary>Bir giriş kontrolünün o an tuttuğu metin değerini okur ("Evet"/"Hayır" ya da yazılan metin).</summary>
        public static string ReadValue(string dataType, FrameworkElement element)
        {
            if (dataType == YesNoDataType)
            {
                var checkBox = (CheckBox)element;
                return checkBox.IsChecked == true ? "Evet" : "Hayır";
            }

            return ((TextBox)element).Text.Trim();
        }
    }
}
