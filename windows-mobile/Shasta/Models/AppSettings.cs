using Shasta.Services;
using Windows.Data.Json;

namespace Shasta.Models
{
    // Non-sensitive app settings persisted via LocalDataStore's
    // settings.json. Session tokens live in PasswordVault instead (see
    // AbsSession) — they're credentials, not app data.
    public sealed class AppSettings
    {
        public string ServerUrl { get; set; } = "";
        public string ThemeMode { get; set; } = "auto";
        public string LastOpenedLibraryId { get; set; } = "";

        public JsonObject ToJson()
        {
            return new JsonObject
            {
                ["serverUrl"] = JsonValue.CreateStringValue(ServerUrl ?? ""),
                ["themeMode"] = JsonValue.CreateStringValue(ThemeMode ?? "auto"),
                ["lastOpenedLibraryId"] = JsonValue.CreateStringValue(LastOpenedLibraryId ?? ""),
            };
        }

        public static AppSettings Parse(JsonObject obj)
        {
            return new AppSettings
            {
                ServerUrl = JsonHelpers.GetStringOrDefault(obj, "serverUrl"),
                ThemeMode = JsonHelpers.GetStringOrDefault(obj, "themeMode", "auto"),
                LastOpenedLibraryId = JsonHelpers.GetStringOrDefault(obj, "lastOpenedLibraryId"),
            };
        }
    }
}
