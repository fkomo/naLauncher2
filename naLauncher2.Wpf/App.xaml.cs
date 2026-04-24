using naLauncher2.Wpf.Api;
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

        public static TwitchDevAuthz? TwitchDevAuthz { get; set; }

        protected override async void OnStartup(StartupEventArgs e)
        {
            await AppSettings.Instance.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "settings.json"));

            Log.WriteLine("App starting ...");

            SettingsChanged();

            base.OnStartup(e);
        }

        public static void SettingsChanged()
        {
            TwitchDevAuthz = null;

            if (AppSettings.Instance.TwitchDev?.ClientId != null && AppSettings.Instance.TwitchDev.ClientSecret != null)
                TwitchDevAuthz = new TwitchDevAuthz(AppSettings.Instance.TwitchDev.ClientId, AppSettings.Instance.TwitchDev.ClientSecret);
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            await AppSettings.Instance.Save();

            Log.WriteLine("App exiting ...");
            base.OnExit(e);
        }
    }
}
