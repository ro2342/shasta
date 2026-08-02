using Shasta.Services;
using Windows.Data.Json;

namespace Shasta.Models
{
    // One entry from GET /api/libraries's `libraries` array.
    public sealed class AbsLibrary
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string MediaType { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Provider { get; set; } = "";
        public long DisplayOrder { get; set; }

        public static AbsLibrary Parse(JsonObject obj)
        {
            return new AbsLibrary
            {
                Id = JsonHelpers.GetStringOrDefault(obj, "id"),
                Name = JsonHelpers.GetStringOrDefault(obj, "name"),
                MediaType = JsonHelpers.GetStringOrDefault(obj, "mediaType"),
                Icon = JsonHelpers.GetStringOrDefault(obj, "icon"),
                Provider = JsonHelpers.GetStringOrDefault(obj, "provider"),
                DisplayOrder = JsonHelpers.GetInt64OrDefault(obj, "displayOrder"),
            };
        }
    }
}
