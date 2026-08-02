using Windows.UI.Xaml;

namespace Shasta.Services
{
    // Applies light/dark/auto across the whole app. "auto" uses
    // ElementTheme.Default, which already means "follow the system theme"
    // natively in UWP. See wp-apps/03-design-visual-e-navegacao.md.
    public static class ThemeModeService
    {
        public static void Apply(string themeMode)
        {
            if (!(Window.Current?.Content is FrameworkElement root))
            {
                return;
            }

            switch (themeMode)
            {
                case "light":
                    root.RequestedTheme = ElementTheme.Light;
                    break;
                case "dark":
                    root.RequestedTheme = ElementTheme.Dark;
                    break;
                default:
                    root.RequestedTheme = ElementTheme.Default;
                    break;
            }
        }
    }
}
