using System.Windows;

namespace naLauncher2.Wpf
{
    internal static class WindowDimHelper
    {
        internal static void Dim(Window? owner)
        {
            if (owner?.FindName("DimOverlay") is UIElement overlay)
                overlay.Visibility = Visibility.Visible;
        }

        internal static void Undim(Window? owner)
        {
            if (owner?.FindName("DimOverlay") is UIElement overlay)
                overlay.Visibility = Visibility.Collapsed;
        }
    }
}
