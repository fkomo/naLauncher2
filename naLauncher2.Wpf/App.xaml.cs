using naLauncher2.Wpf.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace naLauncher2.Wpf
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static readonly JsonSerializerOptions JsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            IgnoreReadOnlyFields = true,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IgnoreReadOnlyProperties = true,
        };

        protected override async void OnStartup(StartupEventArgs e)
        {
            await AppSettings.Instance.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "settings.json"));

            Log.WriteLine("App starting ...");

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.WriteLine("App exiting ...");
            base.OnExit(e);
        }
    }
}
