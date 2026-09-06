using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace naLauncher2.Wpf
{
    /// <summary>
    /// Row separator drawn above the first tile row of a group in the User Games grid: the label
    /// the group shares - a capital letter or a year, depending on the ordering - and the size of
    /// the group on the left, a thin horizontal line filling the rest.
    /// </summary>
    internal sealed class GroupDivider : Grid
    {
        public const double ControlHeight = 48;

        readonly TextBlock _labelText;
        readonly TextBlock _countText;

        /// <summary>
        /// The group this divider currently heads.
        /// </summary>
        public string Label { get; private set; } = string.Empty;

        /// <summary>
        /// Set while the divider is fading out, so grid updates no longer reuse it.
        /// </summary>
        public bool IsRemoving { get; set; }

        public TranslateTransform SlideTransform { get; } = new();

        public GroupDivider(string label, int count, double width)
        {
            Width = width;
            Height = ControlHeight;
            IsHitTestVisible = false;
            RenderTransform = SlideTransform;

            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _labelText = new TextBlock
            {
                Foreground = Brushes.White,
                Opacity = 0.7,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(_labelText, 0);
            Children.Add(_labelText);

            _countText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
            };
            Grid.SetColumn(_countText, 1);
            Children.Add(_countText);

            var line = new Rectangle
            {
                Height = 1,
                Fill = Brushes.White,
                Opacity = 0.06,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(line, 2);
            Children.Add(line);

            SetGroup(label, count);
        }

        /// <summary>
        /// Relabels the divider, so a divider already on the canvas can head a different group.
        /// </summary>
        public void SetGroup(string label, int count)
        {
            Label = label;
            _labelText.Text = label;
            _countText.Text = $"({count})";
        }
    }
}
