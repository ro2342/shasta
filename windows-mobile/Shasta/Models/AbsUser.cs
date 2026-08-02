using Shasta.Services;
using Windows.Data.Json;

namespace Shasta.Models
{
    // Mirrors the "user" object embedded in Audiobookshelf's /login and
    // /auth/refresh responses.
    public sealed class AbsUser
    {
        public string Id { get; set; } = "";
        public string Username { get; set; } = "";
        public string Email { get; set; } = "";
        public bool IsActive { get; set; }
        public string Type { get; set; } = "";

        public static AbsUser Parse(JsonObject obj)
        {
            return new AbsUser
            {
                Id = JsonHelpers.GetStringOrDefault(obj, "id"),
                Username = JsonHelpers.GetStringOrDefault(obj, "username"),
                Email = JsonHelpers.GetStringOrDefault(obj, "email"),
                IsActive = JsonHelpers.GetBoolOrDefault(obj, "isActive"),
                Type = JsonHelpers.GetStringOrDefault(obj, "type"),
            };
        }
    }
}
