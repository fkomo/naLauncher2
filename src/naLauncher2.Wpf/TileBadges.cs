using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace naLauncher2.Wpf
{
    /// <summary>
    /// What one badge shows for a game.
    /// </summary>
    record TileBadgeContent(string Text, Brush Background, Brush Foreground, bool Bold = true);

    /// <summary>
    /// A badge in the top-left strip of a game tile, tied to the sort mode it belongs to.
    /// Returning null from <see cref="GetContent"/> means the game has no badge of this kind.
    /// </summary>
    record TileBadgeDefinition(GamesSortMode SortMode, Func<GameInfo, TileBadgeContent?> GetContent);

    /// <summary>
    /// The tile badges. A tile shows the badge of the active sort first and always; the others follow
    /// to its right, in this order, while the mouse is over the tile's name.
    /// </summary>
    static class TileBadges
    {
        public const double Height = 40;
        const double Spacing = 6;

        static readonly Brush PlayTimeBackground = Frozen(new SolidColorBrush(Color.FromArgb(0xDD, 0x1A, 0x1A, 0x1A)));

        public static readonly TileBadgeDefinition[] All =
        [
            new(GamesSortMode.Rating, game => game.Rating is int rating
                ? new TileBadgeContent(rating.ToString(), Frozen(new SolidColorBrush(GetMetacriticColor(rating))), Brushes.Black)
                : null),

            new(GamesSortMode.PlayTime, game => game.TotalPlayTime is TimeSpan playTime
                ? new TileBadgeContent(PlayTimeFormat.Format(playTime), PlayTimeBackground, Brushes.White, Bold: false)
                : null),
        ];

        /// <summary>
        /// Badge definitions in display order for the given sort: the active sort's badge first.
        /// </summary>
        public static IEnumerable<TileBadgeDefinition> Ordered(GamesSortMode? activeSort) =>
            All.Where(b => b.SortMode == activeSort).Concat(All.Where(b => b.SortMode != activeSort));

        public static Border Create(TileBadgeContent content) => new()
        {
            Height = Height,
            MinWidth = Height,
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, Spacing, 0),
            Padding = new Thickness(8, 0, 8, 0),
            Background = content.Background,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 4, Color = Colors.Black, ShadowDepth = 1, Opacity = 0.5 },
            Child = new TextBlock
            {
                Text = content.Text,
                Foreground = content.Foreground,
                FontSize = 18,
                FontWeight = content.Bold ? FontWeights.Bold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };

        static Color GetMetacriticColor(int score)
        {
            if (score >= 75)
                return Color.FromArgb(0xff, 0x00, 0xce, 0x7a);

            else if (score < 50)
                return Color.FromArgb(0xff, 0xff, 0x6b, 0x73);

            else
                return Color.FromArgb(0xff, 0xff, 0xbd, 0x3f);
        }

        static Brush Frozen(Brush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}
