using System.Collections.Generic;
using Shasta.Services;
using Windows.Data.Json;

namespace Shasta.Models
{
    // One entry of GET /api/libraries/{id}/series's `results` array. Books
    // arrive as "minified" library items (server's toOldJSONMinified) —
    // AbsLibraryItem.Parse doesn't require any field minified items lack,
    // so the same parser covers both shapes.
    public sealed class AbsSeries
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<AbsLibraryItem> Books { get; set; } = new List<AbsLibraryItem>();

        public static AbsSeries Parse(JsonObject obj)
        {
            AbsSeries series = new AbsSeries
            {
                Id = JsonHelpers.GetStringOrDefault(obj, "id"),
                Name = JsonHelpers.GetStringOrDefault(obj, "name"),
            };
            foreach (IJsonValue entry in JsonHelpers.GetArrayOrEmpty(obj, "books"))
            {
                if (entry.ValueType == JsonValueType.Object)
                {
                    series.Books.Add(AbsLibraryItem.Parse(entry.GetObject()));
                }
            }
            return series;
        }
    }
}
