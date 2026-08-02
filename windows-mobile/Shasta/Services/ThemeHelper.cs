using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace Shasta.Services
{
    // The one place in the app that reads the system accent color, so the
    // same resource lookup isn't spread across every page. Used where the
    // accent color needs to be copied into a SolidColorBrush (e.g. menu
    // button, active nav item) — doesn't follow a live theme change on its
    // own, reapply on UISettings.ColorValuesChanged (see MainPage.xaml.cs).
    public static class ThemeHelper
    {
        public static SolidColorBrush AccentBrush()
        {
            return new SolidColorBrush((Windows.UI.Color)Application.Current.Resources["SystemAccentColor"]);
        }
    }
}
