using Shasta.Services;
using Windows.Data.Json;

namespace Shasta.Models
{
    // One entry of a LibraryItem's `libraryFiles` array — the id here is
    // what GET /api/items/{itemId}/file/{fileId}/download expects.
    // Best-guess field name (see the plan's open risks): ABS is known to
    // key files by inode number ("ino"), so that's tried first, falling
    // back to a plain "id" if a server ever returns one instead. Verify
    // against a real expanded item response before trusting downloads.
    public sealed class AbsLibraryFile
    {
        public string Id { get; set; } = "";

        public static AbsLibraryFile Parse(JsonObject obj)
        {
            string id = JsonHelpers.GetStringOrDefault(obj, "ino");
            if (string.IsNullOrEmpty(id))
            {
                id = JsonHelpers.GetStringOrDefault(obj, "id");
            }
            return new AbsLibraryFile { Id = id };
        }
    }
}
