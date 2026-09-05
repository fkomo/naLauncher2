using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace naLauncher2.Wpf
{
    /// <summary>
    /// Tile that heads a title group in the User Games grid, sized like a
    /// <see cref="GameInfoControl"/> but on a slightly lighter background: the capital letter
    /// shared by the group, its size underneath, and a +/- marker showing whether the group
    /// is collapsed. Clicking it collapses or expands the games that belong to the group.
    /// </summary>
    internal sealed class TitleGroupTile : TitleGroupElement
    {
        // one step lighter than the #FF1A1A1A of a game tile, so the header reads as part of
        // the grid without competing with the covers
        static readonly Brush TileBackground = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        static readonly Brush TileBorder = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));

        readonly Border _border;
        readonly TextBlock _letterLabel;
        readonly TextBlock _countLabel;
        readonly TextBlock _marker;
        readonly Rectangle _glassOverlay;

        public TitleGroupTile(string letter, int count, bool collapsed)
        {
            Width = GameInfoControl.ControlWidth;
            Height = GameInfoControl.ControlHeight;
            Cursor = Cursors.Hand;

            var shadow = new Rectangle
            {
                Fill = TileBackground,
                IsHitTestVisible = false,
                Effect = new DropShadowEffect
                {
                    BlurRadius = GameInfoControl.ShadowBlurRadius,
                    Color = Colors.Black,
                    ShadowDepth = 0,
                    Opacity = 1,
                },
            };
            Children.Add(shadow);

            var content = new Grid();

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            _letterLabel = new TextBlock
            {
                Foreground = Brushes.White,
                Opacity = 0.85,
                FontSize = 110,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                LineHeight = 118,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };
            stack.Children.Add(_letterLabel);

            _countLabel = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 20,
                TextAlignment = TextAlignment.Center,
            };
            stack.Children.Add(_countLabel);

            content.Children.Add(stack);

            _marker = new TextBlock
            {
                Foreground = Brushes.White,
                Opacity = 0.35,
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 12, 8),
            };
            content.Children.Add(_marker);

            _glassOverlay = new Rectangle
            {
                IsHitTestVisible = false,
                Opacity = 0,
                Fill = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), 0),
                        new GradientStop(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF), 0.45),
                        new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.75),
                    },
                    new Point(0, 0), new Point(0, 1)),
            };
            content.Children.Add(_glassOverlay);

            _border = new Border
            {
                BorderBrush = TileBorder,
                BorderThickness = new Thickness(1),
                Background = TileBackground,
                Child = content,
            };
            Children.Add(_border);

            SetGroup(letter, count, collapsed);
        }

        /// <summary>
        /// Relabels the tile, so a tile already on the canvas can head a different group.
        /// </summary>
        public void SetGroup(string letter, int count, bool collapsed)
        {
            Letter = letter;
            _letterLabel.Text = letter;
            _countLabel.Text = $"{count}";
            _marker.Text = collapsed ? "+" : "\u2212";
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            _border.BorderBrush = new SolidColorBrush(Colors.LightSkyBlue);
            _glassOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(_glassOverlay.Opacity, 1,
                new Duration(TimeSpan.FromMilliseconds(GameInfoControl.GlassOverlayDuration))));
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            _border.BorderBrush = TileBorder;
            _glassOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(_glassOverlay.Opacity, 0,
                new Duration(TimeSpan.FromMilliseconds(GameInfoControl.GlassOverlayDuration))));
        }
    }
}
