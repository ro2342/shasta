using Shasta.Services;
using Windows.Data.Json;

namespace Shasta.Models
{
    // Best-guess shape — chapter field names inside an expanded item's
    // `media` blob weren't confirmed from source (see the plan's open
    // risks). Parsed defensively: a missing/renamed field here just means
    // an empty chapter list, never a crash. Verify against a real expanded
    // item response in Phase 3/4 and adjust the field names below.
    public sealed class AbsChapter
    {
        public long Id { get; set; }
        public double Start { get; set; }
        public double End { get; set; }
        public string Title { get; set; } = "";

        public static AbsChapter Parse(JsonObject obj)
        {
            return new AbsChapter
            {
                Id = JsonHelpers.GetInt64OrDefault(obj, "id"),
                Start = JsonHelpers.GetDoubleOrDefault(obj, "start"),
                End = JsonHelpers.GetDoubleOrDefault(obj, "end"),
                Title = JsonHelpers.GetStringOrDefault(obj, "title"),
            };
        }
    }
}
