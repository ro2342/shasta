using System.Collections.Generic;
using Shasta.Services;
using Windows.Data.Json;

namespace Shasta.Models
{
    // One entry of GET /api/libraries/{id}/collections's `results` array.
    // Books arrive as expanded library items here (server's
    // toOldJSONExpanded), a superset of what AbsLibraryItem.Parse reads.
    public sealed class AbsCollection
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<AbsLibraryItem> Books { get; set; } = new List<AbsLibraryItem>();

        public static AbsCollection Parse(JsonObject obj)
        {
            AbsCollection collection = new AbsCollection
            {
                Id = JsonHelpers.GetStringOrDefault(obj, "id"),
                Name = JsonHelpers.GetStringOrDefault(obj, "name"),
            };
            foreach (IJsonValue entry in JsonHelpers.GetArrayOrEmpty(obj, "books"))
            {
                if (entry.ValueType == JsonValueType.Object)
                {
                    collection.Books.Add(AbsLibraryItem.Parse(entry.GetObject()));
                }
            }
            return collection;
        }
    }
}
