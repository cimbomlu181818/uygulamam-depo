using System.Windows;

namespace DEPO_DURUMU
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DEPO_DURUMU.Data.Database.Initialize();
        }
    }
}