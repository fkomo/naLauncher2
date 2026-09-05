using System.Windows.Controls;
using System.Windows.Media;

namespace naLauncher2.Wpf
{
    /// <summary>
    /// Base for the elements that head a title group in the User Games grid
    /// (<see cref="TitleDivider"/> and <see cref="TitleGroupTile"/>). They are laid out on the
    /// same canvas as the game tiles and are reconciled by position when the grid is updated.
    /// </summary>
    internal abstract class TitleGroupElement : Grid
    {
        /// <summary>
        /// The group this element currently heads.
        /// </summary>
        public string Letter { get; protected set; } = string.Empty;

        /// <summary>
        /// Set while the element is fading out, so grid updates no longer reuse it.
        /// </summary>
        public bool IsRemoving { get; set; }

        public TranslateTransform SlideTransform { get; } = new();

        protected TitleGroupElement()
        {
            RenderTransform = SlideTransform;
        }
    }
}
