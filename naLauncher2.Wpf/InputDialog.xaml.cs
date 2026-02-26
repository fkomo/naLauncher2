using System.Windows;
using System.Windows.Input;

namespace naLauncher2.Wpf
{
    public partial class InputDialog : Window
    {
        public string InputText => InputBox.Text;

        public InputDialog(string initialValue)
        {
            InitializeComponent();
            InputBox.Text = initialValue;
            Loaded += (_, _) => { InputBox.Focus(); InputBox.SelectAll(); };
        }

        void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

        void Save_Click(object sender, MouseButtonEventArgs e) => DialogResult = true;

        void Cancel_Click(object sender, MouseButtonEventArgs e) => DialogResult = false;

        void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) DialogResult = true;
            else if (e.Key == Key.Escape) DialogResult = false;
        }
    }
}
