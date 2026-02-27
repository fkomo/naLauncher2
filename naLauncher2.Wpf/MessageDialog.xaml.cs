using System.Windows;
using System.Windows.Input;

namespace naLauncher2.Wpf
{
    public partial class MessageDialog : Window
    {
        public MessageDialog(string title, string message)
        {
            InitializeComponent();
            TitleText.Text = title;
            MessageText.Text = message;
        }

        void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

        void OK_Click(object sender, MouseButtonEventArgs e) => DialogResult = true;

        void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                DialogResult = true;
        }
    }
}
