using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace naLauncher2.Wpf
{
    /// <summary>
    /// Heading drawn above the first tile row of a group in the User Games grid: the label the
    /// group shares - a capital letter, a year or a playtime range, depending on the ordering -
    /// and the size of the group beside it. Clicking it collapses or expands the group.
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

        public bool Collapsed { get; private set; }

        /// <summary>
        /// Set while the divider is fading out, so grid updates no longer reuse it.
        /// </summary>
        public bool IsRemoving { get; set; }

        public TranslateTransform SlideTransform { get; } = new();

        /// <summary>
        /// Raised when the divider is clicked; the owner flips the group's collapsed state.
        /// </summary>
        public event Action<GroupDivider>? Toggled;

        public GroupDivider(string label, int count, bool collapsed, double width)
        {
            Width = width;
            Height = ControlHeight;
            RenderTransform = SlideTransform;

            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // only the heading itself is clickable, not the empty width of the row
            var heading = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
            };
            heading.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Toggled?.Invoke(this);
            };
            heading.MouseEnter += (_, _) => _labelText!.Opacity = 1;
            heading.MouseLeave += (_, _) => _labelText!.Opacity = 0.7;
            Children.Add(heading);

            _labelText = new TextBlock
            {
                Foreground = Brushes.White,
                Opacity = 0.7,
                FontSize = 32,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 8, 8),
            };
            heading.Children.Add(_labelText);

            _countText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 12, 14),
            };
            heading.Children.Add(_countText);

            SetGroup(label, count, collapsed);
        }

        /// <summary>
        /// Relabels the divider, so a divider already on the canvas can head a different group.
        /// </summary>
        public void SetGroup(string label, int count, bool collapsed)
        {
            Label = label;
            Collapsed = collapsed;
            _labelText.Text = label;
            _countText.Text = $"({count})";
        }
    }
}
