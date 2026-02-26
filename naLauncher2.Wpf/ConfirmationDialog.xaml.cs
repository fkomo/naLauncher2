using System.Windows;
using System.Windows.Input;

namespace naLauncher2.Wpf
{
    public partial class ConfirmationDialog : Window
    {
        public ConfirmationDialog(string message)
        {
            InitializeComponent();
            MessageText.Text = message;
        }

        void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

        void Confirm_Click(object sender, MouseButtonEventArgs e) => DialogResult = true;

        void Cancel_Click(object sender, MouseButtonEventArgs e) => DialogResult = false;

        void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                DialogResult = false;
        }
    }
}
