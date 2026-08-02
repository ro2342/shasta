using System;
using Shasta.Services;
using Windows.Data.Json;

namespace Shasta.Models
{
    // One row in the downloads index DownloadService (Phase 5) persists via
    // LocalDataStore — one record per downloaded audio file.
    public sealed class DownloadRecord
    {
        public static class Statuses
        {
            public const string Queued = "queued";
            public const string Downloading = "downloading";
            public const string Completed = "completed";
            public const string Failed = "failed";
        }

        public string LibraryItemId { get; set; } = "";
        public string FileId { get; set; } = "";
        public string Title { get; set; } = "";
        public string LocalFileName { get; set; } = "";
        public long TotalBytes { get; set; }
        public long DownloadedBytes { get; set; }
        public string Status { get; set; } = Statuses.Queued;
        public DateTimeOffset? DownloadedAtUtc { get; set; }

        public JsonObject ToJson()
        {
            return new JsonObject
            {
                ["libraryItemId"] = JsonValue.CreateStringValue(LibraryItemId ?? ""),
                ["fileId"] = JsonValue.CreateStringValue(FileId ?? ""),
                ["title"] = JsonValue.CreateStringValue(Title ?? ""),
                ["localFileName"] = JsonValue.CreateStringValue(LocalFileName ?? ""),
                ["totalBytes"] = JsonValue.CreateNumberValue(TotalBytes),
                ["downloadedBytes"] = JsonValue.CreateNumberValue(DownloadedBytes),
                ["status"] = JsonValue.CreateStringValue(Status ?? Statuses.Queued),
                ["downloadedAtUtc"] = JsonValue.CreateStringValue(
                    DownloadedAtUtc.HasValue ? DownloadedAtUtc.Value.ToString("o") : ""),
            };
        }

        public static DownloadRecord Parse(JsonObject obj)
        {
            DownloadRecord record = new DownloadRecord
            {
                LibraryItemId = JsonHelpers.GetStringOrDefault(obj, "libraryItemId"),
                FileId = JsonHelpers.GetStringOrDefault(obj, "fileId"),
                Title = JsonHelpers.GetStringOrDefault(obj, "title"),
                LocalFileName = JsonHelpers.GetStringOrDefault(obj, "localFileName"),
                TotalBytes = JsonHelpers.GetInt64OrDefault(obj, "totalBytes"),
                DownloadedBytes = JsonHelpers.GetInt64OrDefault(obj, "downloadedBytes"),
                Status = JsonHelpers.GetStringOrDefault(obj, "status", Statuses.Queued),
            };
            string downloadedAtStr = JsonHelpers.GetStringOrNull(obj, "downloadedAtUtc");
            if (!string.IsNullOrEmpty(downloadedAtStr) && DateTimeOffset.TryParse(downloadedAtStr, out DateTimeOffset downloadedAt))
            {
                record.DownloadedAtUtc = downloadedAt;
            }
            return record;
        }
    }
}
