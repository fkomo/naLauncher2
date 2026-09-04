using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace naLauncher2.Wpf
{
    /// <summary>
    /// Row separator drawn above the first tile row of a title group in the User Games grid:
    /// the capital letter shared by that group and the size of the group on the left,
    /// a thin horizontal line filling the rest.
    /// </summary>
    internal sealed class TitleDivider : Grid
    {
        public const double ControlHeight = 48;

        readonly TextBlock _letterLabel;
        readonly TextBlock _countLabel;

        /// <summary>
        /// Set while the divider is fading out, so grid updates no longer reuse it.
        /// </summary>
        public bool IsRemoving { get; set; }

        public TranslateTransform SlideTransform { get; } = new();

        public string Letter
        {
            get => _letterLabel.Text;
            set => _letterLabel.Text = value;
        }

        public int Count
        {
            set => _countLabel.Text = $"({value})";
        }

        public TitleDivider(string letter, int count, double width)
        {
            Width = width;
            Height = ControlHeight;
            IsHitTestVisible = false;
            RenderTransform = SlideTransform;

            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _letterLabel = new TextBlock
            {
                Text = letter,
                Foreground = Brushes.White,
                Opacity = 0.7,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(_letterLabel, 0);
            Children.Add(_letterLabel);

            _countLabel = new TextBlock
            {
                Text = $"({count})",
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
            };
            Grid.SetColumn(_countLabel, 1);
            Children.Add(_countLabel);

            var line = new Rectangle
            {
                Height = 1,
                Fill = Brushes.White,
                Opacity = 0.06,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(line, 2);
            Children.Add(line);
        }
    }
}
