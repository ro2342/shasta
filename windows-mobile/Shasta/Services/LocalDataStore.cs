using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Shasta.Models;
using Windows.Data.Json;
using Windows.Storage;

namespace Shasta.Services
{
    // One JSON file per "store" in ApplicationData.Current.LocalFolder —
    // see wp-apps/05-dados-locais-e-conteudo.md. Session tokens do NOT go
    // through here — those are credentials, persisted separately via
    // PasswordVault by AbsSessionStore (Phase 2).
    public static class LocalDataStore
    {
        private const string SettingsFile = "settings.json";
        private const string DownloadsFile = "downloads.json";

        private static async Task<JsonObject> ReadStoreAsync(string fileName)
        {
            try
            {
                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(fileName);
                string text = await FileIO.ReadTextAsync(file);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return new JsonObject();
                }
                return JsonObject.Parse(text);
            }
            catch (FileNotFoundException)
            {
                return new JsonObject();
            }
        }

        private static async Task WriteStoreAsync(string fileName, JsonObject obj)
        {
            StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                fileName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, obj.Stringify());
        }

        public static async Task<AppSettings> GetSettingsAsync()
        {
            JsonObject store = await ReadStoreAsync(SettingsFile);
            return AppSettings.Parse(store);
        }

        public static async Task SetSettingsAsync(AppSettings settings)
        {
            await WriteStoreAsync(SettingsFile, settings.ToJson());
        }

        // Keyed by "<libraryItemId>/<fileId>" — the same composite-key
        // pattern as the wp-apps template's lists.json.
        public static async Task<List<DownloadRecord>> GetDownloadsIndexAsync()
        {
            JsonObject store = await ReadStoreAsync(DownloadsFile);
            List<DownloadRecord> records = new List<DownloadRecord>();
            foreach (KeyValuePair<string, IJsonValue> kv in store)
            {
                if (kv.Value.ValueType == JsonValueType.Object)
                {
                    records.Add(DownloadRecord.Parse(kv.Value.GetObject()));
                }
            }
            return records;
        }

        public static async Task SaveDownloadRecordAsync(DownloadRecord record)
        {
            JsonObject store = await ReadStoreAsync(DownloadsFile);
            store[DownloadKey(record.LibraryItemId, record.FileId)] = record.ToJson();
            await WriteStoreAsync(DownloadsFile, store);
        }

        public static async Task RemoveDownloadRecordAsync(string libraryItemId, string fileId)
        {
            JsonObject store = await ReadStoreAsync(DownloadsFile);
            string key = DownloadKey(libraryItemId, fileId);
            if (store.ContainsKey(key))
            {
                store.Remove(key);
                await WriteStoreAsync(DownloadsFile, store);
            }
        }

        private static string DownloadKey(string libraryItemId, string fileId) => $"{libraryItemId}/{fileId}";

        // Only clears local caches/preferences — the actual downloaded
        // audio files are DownloadService's responsibility (callers should
        // clear those separately first, see SettingsPage's danger zone).
        // Session tokens live in PasswordVault and are untouched here too;
        // AbsAuthService.LogoutAsync() owns clearing those.
        public static async Task ResetAllAsync()
        {
            string[] files = { SettingsFile, DownloadsFile };
            foreach (string fileName in files)
            {
                try
                {
                    StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(fileName);
                    await file.DeleteAsync();
                }
                catch (FileNotFoundException)
                {
                    // Already didn't exist — nothing to delete.
                }
            }
        }
    }
}
