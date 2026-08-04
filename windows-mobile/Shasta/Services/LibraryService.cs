using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shasta.Models;
using Windows.Data.Json;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
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

        // Whichever library was opened last, or the first one the server
        // returns — the fallback every "no specific library was handed to
        // me" screen uses (nav-drawer Library/Series/Collections, Home's
        // Continue Listening shelf). Centralized here so the resolution
        // rule can't drift between call sites the way it briefly did
        // before LibraryPage grew its own copy of this logic.
        public static async Task<AbsLibrary> ResolveDefaultLibraryAsync()
        {
            List<AbsLibrary> libraries = await GetLibrariesAsync();
            if (libraries.Count == 0)
            {
                return null;
            }
            AppSettings settings = await LocalDataStore.GetSettingsAsync();
            return libraries.FirstOrDefault(l => l.Id == settings.LastOpenedLibraryId) ?? libraries[0];
        }

        public static async Task<List<AbsSeries>> GetSeriesAsync(string libraryId)
        {
            // Same "no limit means zero results" trap as /items — see
            // GetLibraryItemsAsync's comment.
            IDictionary<string, string> query = new Dictionary<string, string> { ["limit"] = "1000" };
            JsonObject response = await AbsApiClient.GetJsonAsync($"/api/libraries/{libraryId}/series", query);
            List<AbsSeries> series = new List<AbsSeries>();
            foreach (IJsonValue entry in JsonHelpers.GetArrayOrEmpty(response, "results"))
            {
                if (entry.ValueType == JsonValueType.Object)
                {
                    series.Add(AbsSeries.Parse(entry.GetObject()));
                }
            }
            return series;
        }

        public static async Task<List<AbsCollection>> GetCollectionsAsync(string libraryId)
        {
            IDictionary<string, string> query = new Dictionary<string, string> { ["limit"] = "1000" };
            JsonObject response = await AbsApiClient.GetJsonAsync($"/api/libraries/{libraryId}/collections", query);
            List<AbsCollection> collections = new List<AbsCollection>();
            foreach (IJsonValue entry in JsonHelpers.GetArrayOrEmpty(response, "results"))
            {
                if (entry.ValueType == JsonValueType.Object)
                {
                    collections.Add(AbsCollection.Parse(entry.GetObject()));
                }
            }
            return collections;
        }

        // GET /libraries/:id/search?q=...&limit=... — verified against
        // server/controllers/LibraryController.js + libraryItemsBookFilters.js
        // on the audiobookshelf GitHub source. Response is
        // { book: [{ libraryItem: {...} }], narrators, tags, genres,
        // series, authors } — only the book matches are surfaced here;
        // matching by author/series/narrator/tag/genre name (rather than
        // title) is a fast-follow, not needed for a first search pass.
        public static async Task<List<AbsLibraryItem>> SearchBooksAsync(string libraryId, string query, int limit = 25)
        {
            IDictionary<string, string> queryParams = new Dictionary<string, string>
            {
                ["q"] = query,
                ["limit"] = limit.ToString(),
            };
            JsonObject response = await AbsApiClient.GetJsonAsync($"/api/libraries/{libraryId}/search", queryParams);
            List<AbsLibraryItem> items = new List<AbsLibraryItem>();
            foreach (IJsonValue entry in JsonHelpers.GetArrayOrEmpty(response, "book"))
            {
                if (entry.ValueType != JsonValueType.Object)
                {
                    continue;
                }
                JsonObject libraryItem = JsonHelpers.GetObjectOrNull(entry.GetObject(), "libraryItem");
                if (libraryItem != null)
                {
                    items.Add(AbsLibraryItem.Parse(libraryItem));
                }
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

        // Same fetch, but targeting a Border instead of an Image — every
        // cover in the app (item detail, player, cards, mini-player) uses
        // a Border with its Background set to an ImageBrush, not a plain
        // Image control. That's what lets LibraryPage's grid tiles etc.
        // share one code path regardless of a tile's exact size, and it's
        // also what would let a cover gain CornerRadius later without
        // switching element types — Image has no such property, but
        // Border does (and always has, unlike Control.CornerRadius — see
        // App.xaml's button styles for why that distinction matters here).
        public static async Task LoadCoverAsync(Border target, string itemId, int? width = null)
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
                target.Background = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill };
            }
            catch
            {
                // Leave whatever placeholder Background the Border already had.
            }
        }
    }
}
