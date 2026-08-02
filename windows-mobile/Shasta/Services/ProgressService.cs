using System.Net;
using System.Threading.Tasks;
using Shasta.Models;
using Windows.Data.Json;

namespace Shasta.Services
{
    // Owns every HTTP call related to an ABS playback session's lifecycle
    // and progress reporting. Doesn't touch MediaPlayer itself —
    // PlaybackService drives *when* these get called.
    public static class ProgressService
    {
        public static async Task<PlaySession> StartSessionAsync(string itemId)
        {
            JsonObject response = await AbsApiClient.PostJsonAsync($"/api/items/{itemId}/play", new JsonObject());
            return PlaySession.Parse(response);
        }

        // Called on a 30-second interval by PlaybackService while actively
        // playing — the confirmed cadence the existing Rust ABS client
        // uses. A sync failure (brief network drop) isn't fatal, just
        // skip this tick and try again next interval.
        public static async Task<bool> SyncSessionAsync(string sessionId, double currentTime, double timeListenedDelta)
        {
            try
            {
                JsonObject body = new JsonObject
                {
                    ["currentTime"] = JsonValue.CreateNumberValue(currentTime),
                    ["timeListened"] = JsonValue.CreateNumberValue(timeListenedDelta),
                };
                await AbsApiClient.PostJsonAsync($"/api/session/{sessionId}/sync", body);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Idempotent — a 404 (session already closed/expired server-side)
        // is tolerated rather than surfaced as an error.
        public static async Task CloseSessionAsync(string sessionId, double currentTime, double timeListenedDelta)
        {
            try
            {
                JsonObject body = new JsonObject
                {
                    ["currentTime"] = JsonValue.CreateNumberValue(currentTime),
                    ["timeListened"] = JsonValue.CreateNumberValue(timeListenedDelta),
                };
                await AbsApiClient.PostJsonAsync($"/api/session/{sessionId}/close", body);
            }
            catch (AbsApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
            }
            catch
            {
                // Best-effort — losing the final close call just means the
                // server catches up on the next sync/progress call.
            }
        }

        public static async Task SetProgressAsync(string itemId, double currentTime, double duration, bool isFinished)
        {
            JsonObject body = new JsonObject
            {
                ["currentTime"] = JsonValue.CreateNumberValue(currentTime),
                ["duration"] = JsonValue.CreateNumberValue(duration),
                ["isFinished"] = JsonValue.CreateBooleanValue(isFinished),
            };
            // Returns an empty body on success — AbsApiClient tolerates
            // that, caller should follow up with GetProgressAsync if it
            // needs the server's normalized record back.
            await AbsApiClient.PatchJsonAsync($"/api/me/progress/{itemId}", body);
        }

        public static async Task<MediaProgress> GetProgressAsync(string itemId)
        {
            JsonObject response = await AbsApiClient.GetJsonAsync($"/api/me/progress/{itemId}");
            return MediaProgress.Parse(response);
        }
    }
}
