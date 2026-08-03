using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Shasta.Models;
using Windows.Data.Json;
using Windows.Storage.Streams;

namespace Shasta.Services
{
    // Thin static wrapper over one shared HttpClient for talking to an
    // Audiobookshelf server. Holds the current server URL + bearer token
    // in memory only — AbsSessionStore (Phase 2) owns persisting them and
    // calls Configure()/SetBearerToken() on boot and after a refresh.
    public static class AbsApiClient
    {
        private static readonly HttpClient _http = new HttpClient();

        public static string ServerUrl { get; private set; } = "";
        private static string _bearerToken = "";

        public static void Configure(string serverUrl, string bearerToken)
        {
            ServerUrl = (serverUrl ?? "").TrimEnd('/');
            _bearerToken = bearerToken ?? "";
        }

        public static void SetBearerToken(string bearerToken)
        {
            _bearerToken = bearerToken ?? "";
        }

        public static async Task<JsonObject> GetJsonAsync(string path, IDictionary<string, string> query = null)
        {
            string text = await SendRawAsync(HttpMethod.Get, path, null, query, null);
            // A handful of endpoints (e.g. the progress PATCH route) return
            // an empty or non-object body on success — tolerate that
            // instead of throwing out of Parse().
            if (!string.IsNullOrWhiteSpace(text) && JsonObject.TryParse(text, out JsonObject parsed))
            {
                return parsed;
            }
            return new JsonObject();
        }

        // A few GET endpoints (personalized shelves) return a bare JSON
        // array at the top level instead of an object wrapper.
        public static async Task<JsonArray> GetJsonArrayAsync(string path, IDictionary<string, string> query = null)
        {
            string text = await SendRawAsync(HttpMethod.Get, path, null, query, null);
            if (!string.IsNullOrWhiteSpace(text) && JsonArray.TryParse(text, out JsonArray parsed))
            {
                return parsed;
            }
            return new JsonArray();
        }

        public static async Task<JsonObject> PostJsonAsync(string path, JsonObject body, IDictionary<string, string> extraHeaders = null, IDictionary<string, string> query = null)
        {
            string text = await SendRawAsync(HttpMethod.Post, path, body, query, extraHeaders);
            if (!string.IsNullOrWhiteSpace(text) && JsonObject.TryParse(text, out JsonObject parsed))
            {
                return parsed;
            }
            return new JsonObject();
        }

        public static async Task<JsonObject> PatchJsonAsync(string path, JsonObject body)
        {
            string text = await SendRawAsync(new HttpMethod("PATCH"), path, body, null, null);
            if (!string.IsNullOrWhiteSpace(text) && JsonObject.TryParse(text, out JsonObject parsed))
            {
                return parsed;
            }
            return new JsonObject();
        }

        // Raw bytes over the same authenticated pipeline as every JSON
        // call — covers and audio streams use the `Authorization` header,
        // not a `?token=` query param. The query-param approach was an
        // unverified assumption (see the removed BuildCoverUri/
        // BuildStreamUri comments in git history) that turned out not to
        // work against a real server: covers silently failed to load.
        public static async Task<byte[]> GetBytesAsync(string path, IDictionary<string, string> query = null)
        {
            Uri uri = BuildUri(path, query);
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                if (!string.IsNullOrEmpty(_bearerToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
                }
                using (HttpResponseMessage response = await _http.SendAsync(request))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new AbsApiException(response.StatusCode, "");
                    }
                    return await response.Content.ReadAsByteArrayAsync();
                }
            }
        }

        private static async Task<string> SendRawAsync(HttpMethod method, string path, JsonObject body, IDictionary<string, string> query, IDictionary<string, string> extraHeaders)
        {
            Uri uri = BuildUri(path, query);
            using (HttpRequestMessage request = new HttpRequestMessage(method, uri))
            {
                if (!string.IsNullOrEmpty(_bearerToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
                }
                if (extraHeaders != null)
                {
                    foreach (KeyValuePair<string, string> header in extraHeaders)
                    {
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }
                if (body != null)
                {
                    request.Content = new StringContent(body.Stringify(), Encoding.UTF8, "application/json");
                }

                using (HttpResponseMessage response = await _http.SendAsync(request))
                {
                    string text = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new AbsApiException(response.StatusCode, text);
                    }
                    return text;
                }
            }
        }

        // Real query-string builder — every key/value is percent-encoded,
        // never string-interpolated. The existing Rust ABS client in this
        // repo hit a real bug from skipping this: an un-encoded '+' inside
        // a base64 filter value decodes server-side as a space and
        // silently returns zero results instead of erroring.
        private static Uri BuildUri(string path, IDictionary<string, string> query)
        {
            StringBuilder sb = new StringBuilder(ServerUrl);
            sb.Append(path.StartsWith("/") ? path : "/" + path);
            if (query != null && query.Count > 0)
            {
                sb.Append('?');
                bool first = true;
                foreach (KeyValuePair<string, string> kv in query)
                {
                    if (!first)
                    {
                        sb.Append('&');
                    }
                    sb.Append(Uri.EscapeDataString(kv.Key));
                    sb.Append('=');
                    sb.Append(Uri.EscapeDataString(kv.Value ?? ""));
                    first = false;
                }
            }
            return new Uri(sb.ToString());
        }

        // Covers are fetched as bytes over the authenticated HttpClient
        // pipeline (GetCoverBytesAsync), not a bare Image.Source URI —
        // the `?token=` query-param approach was assumed to work the same
        // way for images as it does for streamed audio, but real-device
        // testing showed covers silently failing to load while streaming
        // worked fine. Reusing the exact request path already proven to
        // work for JSON calls sidesteps whatever the actual difference
        // is, rather than continuing to guess at it.
        public static async Task<byte[]> GetCoverBytesAsync(string itemId, int? width = null)
        {
            Dictionary<string, string> query = new Dictionary<string, string>();
            if (width.HasValue)
            {
                query["width"] = width.Value.ToString();
            }
            return await GetBytesAsync($"/api/items/{itemId}/cover", query);
        }

        // Same cover bytes, wrapped for SystemMediaTransportControls'
        // DisplayUpdater.Thumbnail (RandomAccessStreamReference, not an
        // Image control) — used by PlaybackService for lock-screen art.
        public static async Task<RandomAccessStreamReference> GetCoverStreamReferenceAsync(string itemId, int? width = null)
        {
            byte[] bytes = await GetCoverBytesAsync(itemId, width);
            InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
            using (DataWriter writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            return RandomAccessStreamReference.CreateFromStream(stream);
        }

        public static Uri BuildStreamUri(AudioTrack track)
        {
            string path = track.ContentUrl ?? "";
            if (path.StartsWith("http://") || path.StartsWith("https://"))
            {
                string separator = path.Contains("?") ? "&" : "?";
                return new Uri($"{path}{separator}token={Uri.EscapeDataString(_bearerToken)}");
            }
            return BuildUri(path, new Dictionary<string, string> { ["token"] = _bearerToken });
        }

        public static Uri BuildDownloadFileUri(string itemId, string fileId)
        {
            return BuildUri($"/api/items/{itemId}/file/{fileId}/download", new Dictionary<string, string> { ["token"] = _bearerToken });
        }
    }

    public sealed class AbsApiException : Exception
    {
        public HttpStatusCode StatusCode { get; }

        public AbsApiException(HttpStatusCode statusCode, string responseBody)
            : base($"Audiobookshelf API request failed ({(int)statusCode}): {responseBody}")
        {
            StatusCode = statusCode;
        }
    }
}
