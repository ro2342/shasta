using System.Collections.Generic;
using Shasta.Services;
using Windows.Data.Json;

namespace Shasta.Models
{
    // Response of POST /api/items/{id}/play — the ABS-side playback
    // session used to report progress via /api/session/{id}/sync and
    // /api/session/{id}/close.
    public sealed class PlaySession
    {
        public string Id { get; set; } = "";
        public string UserId { get; set; } = "";
        public double CurrentTime { get; set; }
        public List<AudioTrack> AudioTracks { get; set; } = new List<AudioTrack>();

        public static PlaySession Parse(JsonObject obj)
        {
            PlaySession session = new PlaySession
            {
                Id = JsonHelpers.GetStringOrDefault(obj, "id"),
                UserId = JsonHelpers.GetStringOrDefault(obj, "userId"),
                CurrentTime = JsonHelpers.GetDoubleOrDefault(obj, "currentTime"),
            };
            foreach (IJsonValue entry in JsonHelpers.GetArrayOrEmpty(obj, "audioTracks"))
            {
                if (entry.ValueType == JsonValueType.Object)
                {
                    session.AudioTracks.Add(AudioTrack.Parse(entry.GetObject()));
                }
            }
            return session;
        }
    }
}
