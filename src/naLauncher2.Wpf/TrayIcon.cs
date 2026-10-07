using Forms = System.Windows.Forms;

namespace naLauncher2.Wpf
{
    /// <summary>
    /// Notification-area icon shown while the main window is hidden (a game is being tracked when
    /// the window was closed). Double-click or "Show" brings the window back; "Exit" quits for real.
    /// </summary>
    sealed class TrayIcon : IDisposable
    {
        readonly Forms.NotifyIcon _icon;

        public TrayIcon(Action show, Action exit)
        {
            using var iconStream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("naLauncher2.Wpf.Assets.na-launcher.ico")!;

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Show", null, (_, _) => show());
            menu.Items.Add("Exit", null, (_, _) => exit());

            _icon = new Forms.NotifyIcon
            {
                Icon = new System.Drawing.Icon(iconStream),
                Text = "naLauncher2",
                ContextMenuStrip = menu,
                Visible = false,
            };
            _icon.DoubleClick += (_, _) => show();
        }

        public bool Visible
        {
            get => _icon.Visible;
            set => _icon.Visible = value;
        }

        /// <summary>
        /// Tooltip text; Windows limits it to 127 characters.
        /// </summary>
        public string Text
        {
            set => _icon.Text = value.Length > 127 ? value[..124] + "..." : value;
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.ContextMenuStrip?.Dispose();
            _icon.Dispose();
        }
    }
}
