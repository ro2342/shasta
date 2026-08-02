using Shasta.Services;
using Windows.Data.Json;

namespace Shasta.Models
{
    // GET/PATCH /api/me/progress/{itemId}/{episodeId?}.
    public sealed class MediaProgress
    {
        public string Id { get; set; } = "";
        public string LibraryItemId { get; set; } = "";
        public string EpisodeId { get; set; }
        public double Duration { get; set; }
        public double Progress { get; set; }
        public double CurrentTime { get; set; }
        public bool IsFinished { get; set; }
        public long LastUpdate { get; set; }

        public static MediaProgress Parse(JsonObject obj)
        {
            return new MediaProgress
            {
                Id = JsonHelpers.GetStringOrDefault(obj, "id"),
                LibraryItemId = JsonHelpers.GetStringOrDefault(obj, "libraryItemId"),
                EpisodeId = JsonHelpers.GetStringOrNull(obj, "episodeId"),
                Duration = JsonHelpers.GetDoubleOrDefault(obj, "duration"),
                Progress = JsonHelpers.GetDoubleOrDefault(obj, "progress"),
                CurrentTime = JsonHelpers.GetDoubleOrDefault(obj, "currentTime"),
                IsFinished = JsonHelpers.GetBoolOrDefault(obj, "isFinished"),
                LastUpdate = JsonHelpers.GetInt64OrDefault(obj, "lastUpdate"),
            };
        }
    }
}
