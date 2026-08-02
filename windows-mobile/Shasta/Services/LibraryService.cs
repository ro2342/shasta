using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shasta.Models;
using Windows.Data.Json;

namespace Shasta.Services
{
    // The single surface Views talk to for library/item data — wraps
    // AbsApiClient's raw JSON calls into typed models so Views never parse
    // JsonObject directly.
    public static class LibraryService
    {
        public static async Task<List<AbsLibrary>> GetLibrariesAsync()
        {
            JsonObject response = await AbsApiClient.GetJsonAsync("/api/libraries");
            List<AbsLibrary> libraries = new List<AbsLibrary>();
            foreach (IJsonValue entry in JsonHelpers.GetArrayOrEmpty(response, "libraries"))
            {
                if (entry.ValueType == JsonValueType.Object)
                {
                    libraries.Add(AbsLibrary.Parse(entry.GetObject()));
                }
            }
            return libraries;
        }

        public static async Task<List<AbsLibraryItem>> GetLibraryItemsAsync(string libraryId)
        {
            JsonObject response = await AbsApiClient.GetJsonAsync($"/api/libraries/{libraryId}/items");
            List<AbsLibraryItem> items = new List<AbsLibraryItem>();
            foreach (IJsonValue entry in JsonHelpers.GetArrayOrEmpty(response, "results"))
            {
                if (entry.ValueType == JsonValueType.Object)
                {
                    items.Add(AbsLibraryItem.Parse(entry.GetObject()));
                }
            }
            return items;
        }

        // /personalized returns an array of named shelves; this picks out
        // just the "continue-listening" one and flattens its entities.
        // Other shelf types (recently added, discover, etc.) are ignored
        // for v1.
        public static async Task<List<AbsLibraryItem>> GetContinueListeningAsync(string libraryId)
        {
            JsonArray shelves = await AbsApiClient.GetJsonArrayAsync($"/api/libraries/{libraryId}/personalized");
            List<AbsLibraryItem> items = new List<AbsLibraryItem>();
            foreach (IJsonValue shelfValue in shelves)
            {
                if (shelfValue.ValueType != JsonValueType.Object)
                {
                    continue;
                }
                JsonObject shelf = shelfValue.GetObject();
                if (JsonHelpers.GetStringOrDefault(shelf, "id") != "continue-listening")
                {
                    continue;
                }
                foreach (IJsonValue entry in JsonHelpers.GetArrayOrEmpty(shelf, "entities"))
                {
                    if (entry.ValueType == JsonValueType.Object)
                    {
                        items.Add(AbsLibraryItem.Parse(entry.GetObject()));
                    }
                }
                break;
            }
            return items;
        }

        public static async Task<AbsLibraryItem> GetItemDetailAsync(string itemId, bool expanded = true)
        {
            IDictionary<string, string> query = expanded
                ? new Dictionary<string, string> { ["expanded"] = "1" }
                : null;
            JsonObject response = await AbsApiClient.GetJsonAsync($"/api/items/{itemId}", query);
            return AbsLibraryItem.Parse(response);
        }

        public static Uri GetCoverUri(string itemId, int? width = null) => AbsApiClient.BuildCoverUri(itemId, width);
    }
}
