using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shasta.Models;
using Windows.Data.Json;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

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
            // Explicit high limit, not omitted — confirmed ABS behavior on
            // the /series endpoint is that no limit (or limit=0) means
            // zero results, not "unlimited". Never actually verified this
            // is also true of /items specifically, but a real device
            // showed an empty library that should have had books, and
            // this is the same class of fix already proven necessary
            // elsewhere in this API.
            IDictionary<string, string> query = new Dictionary<string, string> { ["limit"] = "1000" };
            JsonObject response = await AbsApiClient.GetJsonAsync($"/api/libraries/{libraryId}/items", query);
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

        // Fetches cover bytes over the authenticated pipeline and decodes
        // them directly into the target Image's Source — see
        // AbsApiClient.GetCoverBytesAsync for why this replaced a plain
        // Image.Source = new BitmapImage(uri) approach. Swallows failures
        // (missing cover, network hiccup) rather than throwing, since a
        // blank cover is a fine degraded state and callers loop over many
        // items — one bad cover shouldn't abort a whole list render.
        public static async Task LoadCoverAsync(Image target, string itemId, int? width = null)
        {
            try
            {
                byte[] bytes = await AbsApiClient.GetCoverBytesAsync(itemId, width);
                BitmapImage bitmap = new BitmapImage();
                using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
                {
                    using (DataWriter writer = new DataWriter(stream.GetOutputStreamAt(0)))
                    {
                        writer.WriteBytes(bytes);
                        await writer.StoreAsync();
                        await writer.FlushAsync();
                        writer.DetachStream();
                    }
                    stream.Seek(0);
                    await bitmap.SetSourceAsync(stream);
                }
                target.Source = bitmap;
            }
            catch
            {
                // Leave target.Source as whatever it already was (usually
                // null/blank) — a missing cover is not fatal.
            }
        }
    }
}
