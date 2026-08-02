using Shasta.Services;
using Windows.Data.Json;

namespace Shasta.Models
{
    // One entry from a PlaySession's `audioTracks` array — one per audio
    // file in a multi-file audiobook.
    public sealed class AudioTrack
    {
        public long Index { get; set; }
        public double StartOffset { get; set; }
        public double Duration { get; set; }
        public string Title { get; set; } = "";
        public string ContentUrl { get; set; } = "";

        public static AudioTrack Parse(JsonObject obj)
        {
            return new AudioTrack
            {
                Index = JsonHelpers.GetInt64OrDefault(obj, "index"),
                StartOffset = JsonHelpers.GetDoubleOrDefault(obj, "startOffset"),
                Duration = JsonHelpers.GetDoubleOrDefault(obj, "duration"),
                Title = JsonHelpers.GetStringOrDefault(obj, "title"),
                ContentUrl = JsonHelpers.GetStringOrDefault(obj, "contentUrl"),
            };
        }
    }
}
