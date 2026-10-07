using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace naLauncher2.Wpf
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        Mutex? _singleInstance;
        EventWaitHandle? _showRequest;

        protected override async void OnStartup(StartupEventArgs e)
        {
            if (!AcquireSingleInstance())
            {
                Shutdown();
                return;
            }

            await AppSettings.Instance.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "settings.json"));

            Log.WriteLine("App starting ...");

            SettingsChanged();

            base.OnStartup(e);
        }

        /// <summary>
        /// Only one instance may run, or two trackers would write the same library. A second instance
        /// asks the first one to show its window, then exits. Instances are told apart by install
        /// folder, so a dev build and the published app (which use different settings) can run side by side.
        /// </summary>
        bool AcquireSingleInstance()
        {
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppContext.BaseDirectory.ToLowerInvariant())))[..16];

            _singleInstance = new Mutex(true, $@"Local\naLauncher2-{id}", out bool createdNew);
            _showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\naLauncher2-{id}-show");

            if (!createdNew)
            {
                _showRequest.Set();
                _singleInstance.Dispose();
                _singleInstance = null;
                return false;
            }

            var listener = new Thread(() =>
            {
                while (_showRequest.WaitOne())
                    Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ShowFromTray());
            })
            { IsBackground = true, Name = "naLauncher2 show request listener" };
            listener.Start();

            return true;
        }

        public static void SettingsChanged()
        {
            GameLibrary.TwitchDevAuthz = null;

            if (AppSettings.Instance.TwitchDev?.ClientId != null && AppSettings.Instance.TwitchDev.ClientSecret != null)
                GameLibrary.TwitchDevAuthz = new TwitchDevAuthz(AppSettings.Instance.TwitchDev.ClientId, AppSettings.Instance.TwitchDev.ClientSecret);
        }

        /// <summary>
        /// Windows is logging off or shutting down: end tracked sessions now and save, synchronously,
        /// because the process may be gone before any await resumes.
        /// </summary>
        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            Log.WriteLine($"Windows session ending ({e.ReasonSessionEnding}) ...");

            // run off the UI thread: blocking the UI thread on code that resumes on it would deadlock
            Task.Run(async () =>
            {
                await SessionTracker.Instance.StopAll();
                await AppSettings.Instance.Save();
            }).GetAwaiter().GetResult();

            base.OnSessionEnding(e);
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            // a second instance that only signalled the first one has nothing to save
            if (_singleInstance is not null)
            {
                await AppSettings.Instance.Save();

                Log.WriteLine("App exiting ...");
            }

            // the single-instance mutex is released by Windows when the process ends
            base.OnExit(e);
        }
    }
}
