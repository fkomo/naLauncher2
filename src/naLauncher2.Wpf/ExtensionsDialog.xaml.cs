using System.Windows;
using System.Windows.Input;

namespace naLauncher2.Wpf
{
    /// <summary>
    /// Lists and edits a game's extensions on a copy; the properties dialog applies <see cref="Extensions"/> on its Save.
    /// </summary>
    public partial class ExtensionsDialog : Window
    {
        public Dictionary<string, string> Extensions { get; }

        public ExtensionsDialog(string title, IDictionary<string, string> extensions, bool readOnly = false)
        {
            Extensions = new Dictionary<string, string>(extensions);

            InitializeComponent();

            if (readOnly)
            {
                AddRow.Visibility = Visibility.Collapsed;
                OkButton.Visibility = Visibility.Collapsed;
                CancelButton.Text = "Close";
                // the delete buttons in the list template read this
                ExtensionsList.Tag = "readonly";
            }

            MaxHeight = SystemParameters.WorkArea.Height - 40;
            TitleText.Text = $"Extensions – {title}";
            RefreshList();

            ExtensionKeyBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { ExtensionValueBox.Focus(); e.Handled = true; }
            };
            ExtensionValueBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { AddExtension(); e.Handled = true; }
            };

            Loaded += (_, _) => { WindowDimHelper.Dim(Owner); ExtensionKeyBox.Focus(); };
            Closed += (_, _) => WindowDimHelper.Undim(Owner);
        }

        void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

        void RefreshList()
        {
            ExtensionsList.ItemsSource = null;
            ExtensionsList.ItemsSource = Extensions.OrderBy(kv => kv.Key);
        }

        void RemoveExtension_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string key })
            {
                Extensions.Remove(key);
                RefreshList();
            }
        }

        void AddExtension_Click(object sender, MouseButtonEventArgs e) => AddExtension();

        void AddExtension()
        {
            var key = ExtensionKeyBox.Text.Trim();
            var value = ExtensionValueBox.Text.Trim();
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                return;

            Extensions[key] = value;
            RefreshList();

            ExtensionKeyBox.Clear();
            ExtensionValueBox.Clear();
            ExtensionKeyBox.Focus();
        }

        void Ok_Click(object sender, MouseButtonEventArgs e) => DialogResult = true;

        void Cancel_Click(object sender, MouseButtonEventArgs e) => DialogResult = false;

        void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                DialogResult = false;
        }
    }
}
