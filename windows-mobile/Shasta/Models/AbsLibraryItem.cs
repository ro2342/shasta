using System.Collections.Generic;
using Shasta.Services;
using Windows.Data.Json;

namespace Shasta.Models
{
    // `media` is deliberately kept as a raw JsonObject instead of a rigid
    // POCO — its shape (especially `metadata.author*`) varies across ABS
    // versions and media types (book vs. podcast), the same reason the
    // existing Rust client in this repo treats it as untyped JSON rather
    // than a strict struct. The narrow Get*() accessors below cover only
    // what the UI actually renders; field paths are a best-effort read of
    // a real ABS server response and should be re-checked against a live
    // server in Phase 3 rather than trusted blindly.
    public sealed class AbsLibraryItem
    {
        public string Id { get; set; } = "";
        public string Ino { get; set; } = "";
        public string LibraryId { get; set; } = "";
        public long? AddedAt { get; set; }
        public string MediaType { get; set; } = "";
        public bool IsFile { get; set; }
        public JsonObject Media { get; set; } = new JsonObject();
        public List<AbsLibraryFile> LibraryFiles { get; set; } = new List<AbsLibraryFile>();

        public static AbsLibraryItem Parse(JsonObject obj)
        {
            AbsLibraryItem item = new AbsLibraryItem
            {
                Id = JsonHelpers.GetStringOrDefault(obj, "id"),
                Ino = JsonHelpers.GetStringOrDefault(obj, "ino"),
                LibraryId = JsonHelpers.GetStringOrDefault(obj, "libraryId"),
                MediaType = JsonHelpers.GetStringOrDefault(obj, "mediaType", "book"),
                IsFile = JsonHelpers.GetBoolOrDefault(obj, "isFile"),
                Media = JsonHelpers.GetObjectOrNull(obj, "media") ?? new JsonObject(),
            };
            if (obj.ContainsKey("addedAt") && obj["addedAt"].ValueType == JsonValueType.Number)
            {
                item.AddedAt = (long)obj["addedAt"].GetNumber();
            }
            foreach (IJsonValue entry in JsonHelpers.GetArrayOrEmpty(obj, "libraryFiles"))
            {
                if (entry.ValueType == JsonValueType.Object)
                {
                    item.LibraryFiles.Add(AbsLibraryFile.Parse(entry.GetObject()));
                }
            }
            return item;
        }

        private JsonObject GetMetadata() => JsonHelpers.GetObjectOrNull(Media, "metadata") ?? new JsonObject();

        public string GetTitle()
        {
            string title = JsonHelpers.GetStringOrDefault(GetMetadata(), "title");
            return string.IsNullOrEmpty(title) ? "Untitled" : title;
        }

        public string GetDescription() => JsonHelpers.GetStringOrDefault(GetMetadata(), "description");

        // Handles the string/object/array variance: a joined `authorName`
        // string, an `authors` array of {name}, or a single `author`
        // field as either a string or a {name} object.
        public string GetAuthorDisplay()
        {
            JsonObject metadata = GetMetadata();

            string authorName = JsonHelpers.GetStringOrDefault(metadata, "authorName");
            if (!string.IsNullOrEmpty(authorName))
            {
                return authorName;
            }

            JsonArray authors = JsonHelpers.GetArrayOrEmpty(metadata, "authors");
            if (authors.Count > 0)
            {
                List<string> names = new List<string>();
                foreach (IJsonValue entry in authors)
                {
                    if (entry.ValueType == JsonValueType.Object)
                    {
                        string name = JsonHelpers.GetStringOrDefault(entry.GetObject(), "name");
                        if (!string.IsNullOrEmpty(name))
                        {
                            names.Add(name);
                        }
                    }
                    else if (entry.ValueType == JsonValueType.String)
                    {
                        names.Add(entry.GetString());
                    }
                }
                if (names.Count > 0)
                {
                    return string.Join(", ", names);
                }
            }

            if (metadata.ContainsKey("author"))
            {
                IJsonValue authorValue = metadata["author"];
                if (authorValue.ValueType == JsonValueType.String)
                {
                    return authorValue.GetString();
                }
                if (authorValue.ValueType == JsonValueType.Object)
                {
                    string name = JsonHelpers.GetStringOrDefault(authorValue.GetObject(), "name");
                    if (!string.IsNullOrEmpty(name))
                    {
                        return name;
                    }
                }
            }

            return "Unknown Author";
        }

        public string GetSeriesName()
        {
            JsonObject metadata = GetMetadata();

            string seriesName = JsonHelpers.GetStringOrDefault(metadata, "seriesName");
            if (!string.IsNullOrEmpty(seriesName))
            {
                return seriesName;
            }

            JsonArray series = JsonHelpers.GetArrayOrEmpty(metadata, "series");
            if (series.Count > 0)
            {
                List<string> names = new List<string>();
                foreach (IJsonValue entry in series)
                {
                    if (entry.ValueType == JsonValueType.Object)
                    {
                        string name = JsonHelpers.GetStringOrDefault(entry.GetObject(), "name");
                        if (!string.IsNullOrEmpty(name))
                        {
                            names.Add(name);
                        }
                    }
                }
                if (names.Count > 0)
                {
                    return string.Join(", ", names);
                }
            }

            return "";
        }

        public double GetDurationSeconds() => JsonHelpers.GetDoubleOrDefault(Media, "duration");

        public string GetCoverPath() => JsonHelpers.GetStringOrDefault(Media, "coverPath");

        // Only present on an ?expanded=1 item response. Best-guess field
        // shape (see AbsChapter) — an unexpected/missing field here just
        // yields an empty list, never a crash.
        public List<AbsChapter> GetChapters()
        {
            List<AbsChapter> chapters = new List<AbsChapter>();
            foreach (IJsonValue entry in JsonHelpers.GetArrayOrEmpty(Media, "chapters"))
            {
                if (entry.ValueType == JsonValueType.Object)
                {
                    chapters.Add(AbsChapter.Parse(entry.GetObject()));
                }
            }
            return chapters;
        }
    }
}
