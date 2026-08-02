using Shasta.Services;
using Windows.Data.Json;

namespace Shasta.Models
{
    public sealed class Bookmark
    {
        public string LibraryItemId { get; set; } = "";
        public string Title { get; set; } = "";
        public double Time { get; set; }

        public static Bookmark Parse(JsonObject obj)
        {
            return new Bookmark
            {
                LibraryItemId = JsonHelpers.GetStringOrDefault(obj, "libraryItemId"),
                Title = JsonHelpers.GetStringOrDefault(obj, "title"),
                Time = JsonHelpers.GetDoubleOrDefault(obj, "time"),
            };
        }
    }
}
