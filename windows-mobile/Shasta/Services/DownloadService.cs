using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Shasta.Models;
using Windows.Storage;

namespace Shasta.Services
{
    // Downloads audio files for offline playback. Reuses the same
    // HttpClient-streaming-with-progress pattern UpdateCheckService already
    // uses for appxbundle downloads, rather than introducing
    // BackgroundDownloader as a second download subsystem.
    //
    // Scope note: this only covers downloading files and preferring them
    // over streaming during an otherwise-normal (server-reachable) play
    // session. Starting playback with zero connectivity at all — which
    // would need queuing local sessions via POST /api/session/local-all —
    // is deliberately NOT implemented here: that endpoint's exact request/
    // response contract was never confirmed against a live server, and
    // building it on a guess would be unverifiable speculation. Downloads
    // still deliver their main value (bandwidth savings, resilience to a
    // flaky connection mid-book) without it.
    public static class DownloadService
    {
        private const string DownloadsFolderName = "Downloads";

        public static Task<List<DownloadRecord>> GetDownloadsAsync() => LocalDataStore.GetDownloadsIndexAsync();

        public static async Task<bool> IsDownloadedAsync(string libraryItemId)
        {
            List<DownloadRecord> records = await LocalDataStore.GetDownloadsIndexAsync();
            return records.Any(r => r.LibraryItemId == libraryItemId && r.Status == DownloadRecord.Statuses.Completed);
        }

        // Maps a PlaySession audio track back to a downloaded file by
        // array-order correlation with the item's libraryFiles — the same
        // unconfirmed-but-reasonable assumption as AbsLibraryFile.Parse.
        public static async Task<StorageFile> GetLocalFileForTrackAsync(AbsLibraryItem item, int trackIndex)
        {
            if (item?.LibraryFiles == null || trackIndex < 0 || trackIndex >= item.LibraryFiles.Count)
            {
                return null;
            }
            string fileId = item.LibraryFiles[trackIndex].Id;
            return await GetLocalFileAsync(item.Id, fileId);
        }

        public static async Task<StorageFile> GetLocalFileAsync(string libraryItemId, string fileId)
        {
            List<DownloadRecord> records = await LocalDataStore.GetDownloadsIndexAsync();
            DownloadRecord record = records.FirstOrDefault(r =>
                r.LibraryItemId == libraryItemId && r.FileId == fileId && r.Status == DownloadRecord.Statuses.Completed);
            if (record == null)
            {
                return null;
            }
            try
            {
                StorageFolder folder = await GetDownloadsFolderAsync();
                return await folder.GetFileAsync(record.LocalFileName);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }

        // Downloads every file that makes up an item. Each file's
        // DownloadRecord is saved to the index as it completes, so a
        // partially-downloaded multi-file book shows accurate per-file
        // state rather than all-or-nothing.
        public static async Task DownloadItemAsync(AbsLibraryItem item, IProgress<double> overallProgress, CancellationToken cancellationToken)
        {
            if (item.LibraryFiles == null || item.LibraryFiles.Count == 0)
            {
                throw new InvalidOperationException("This item has no downloadable files.");
            }

            StorageFolder folder = await GetDownloadsFolderAsync();
            int total = item.LibraryFiles.Count;

            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int index = i;
                await DownloadOneFileAsync(item, item.LibraryFiles[i].Id, folder, cancellationToken,
                    fileProgress => overallProgress?.Report((index + fileProgress) / total * 100.0));
            }
        }

        private static async Task DownloadOneFileAsync(AbsLibraryItem item, string fileId, StorageFolder folder, CancellationToken cancellationToken, Action<double> fileProgress)
        {
            DownloadRecord record = new DownloadRecord
            {
                LibraryItemId = item.Id,
                FileId = fileId,
                Title = item.GetTitle(),
                Status = DownloadRecord.Statuses.Downloading,
            };
            await LocalDataStore.SaveDownloadRecordAsync(record);

            Uri uri = AbsApiClient.BuildDownloadFileUri(item.Id, fileId);
            using (HttpClient client = new HttpClient())
            using (HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                long? totalBytes = response.Content.Headers.ContentLength;
                string fileName = $"{item.Id}_{fileId}{ExtensionForContentType(response.Content.Headers.ContentType?.MediaType)}";

                StorageFile file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);

                using (Stream networkStream = await response.Content.ReadAsStreamAsync())
                using (Stream fileStream = await file.OpenStreamForWriteAsync())
                {
                    byte[] buffer = new byte[81920];
                    long totalRead = 0;
                    int read;
                    while ((read = await networkStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                        totalRead += read;
                        if (totalBytes.HasValue && totalBytes.Value > 0)
                        {
                            fileProgress?.Invoke((double)totalRead / totalBytes.Value);
                        }
                        record.DownloadedBytes = totalRead;
                    }
                    record.TotalBytes = totalBytes ?? totalRead;
                }

                record.LocalFileName = fileName;
                record.Status = DownloadRecord.Statuses.Completed;
                record.DownloadedAtUtc = DateTimeOffset.UtcNow;
                await LocalDataStore.SaveDownloadRecordAsync(record);
            }
        }

        public static async Task DeleteDownloadAsync(string libraryItemId, string fileId)
        {
            List<DownloadRecord> records = await LocalDataStore.GetDownloadsIndexAsync();
            DownloadRecord record = records.FirstOrDefault(r => r.LibraryItemId == libraryItemId && r.FileId == fileId);
            if (record == null)
            {
                return;
            }
            if (!string.IsNullOrEmpty(record.LocalFileName))
            {
                try
                {
                    StorageFolder folder = await GetDownloadsFolderAsync();
                    StorageFile file = await folder.GetFileAsync(record.LocalFileName);
                    await file.DeleteAsync();
                }
                catch (FileNotFoundException)
                {
                    // Already gone — still remove the index entry below.
                }
            }
            await LocalDataStore.RemoveDownloadRecordAsync(libraryItemId, fileId);
        }

        public static async Task DeleteAllDownloadsForItemAsync(string libraryItemId)
        {
            List<DownloadRecord> records = await LocalDataStore.GetDownloadsIndexAsync();
            foreach (DownloadRecord record in records.Where(r => r.LibraryItemId == libraryItemId).ToList())
            {
                await DeleteDownloadAsync(record.LibraryItemId, record.FileId);
            }
        }

        private static Task<StorageFolder> GetDownloadsFolderAsync() =>
            ApplicationData.Current.LocalFolder.CreateFolderAsync(DownloadsFolderName, CreationCollisionOption.OpenIfExists).AsTask();

        private static string ExtensionForContentType(string mediaType)
        {
            switch (mediaType)
            {
                case "audio/mpeg": return ".mp3";
                case "audio/mp4":
                case "audio/x-m4b":
                case "audio/m4b": return ".m4b";
                case "audio/aac": return ".aac";
                case "audio/ogg": return ".ogg";
                case "audio/flac": return ".flac";
                case "audio/wav":
                case "audio/x-wav": return ".wav";
                default: return ".audio";
            }
        }
    }
}
