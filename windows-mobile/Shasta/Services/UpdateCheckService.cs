using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;

namespace Shasta.Services
{
    public sealed class UpdateCheckResult
    {
        public bool Success { get; set; }
        public string Latest { get; set; }
        public bool UpdateAvailable { get; set; }
        public string Error { get; set; }
    }

    // Checks/downloads/installs an update without depending on the
    // Microsoft Store — see wp-apps/07-build-e-ci-cd.md for the full
    // explanation of each step and why a sideloaded app can't install
    // itself (a Windows security boundary, not an implementation gap).
    public static class UpdateCheckService
    {
        // Points at the version.json published by the CI workflow (see
        // Phase 7 / .github/workflows/windows-mobile-build.yml).
        private const string VersionUrl = "https://ro2342.github.io/shasta/app/version.json";
        public const string DownloadPageUrl = "https://ro2342.github.io/shasta/app/";
        public const string DownloadFileUrl = "https://ro2342.github.io/shasta/app/app.appxbundle";

        private const string DownloadFolderTokenKey = "UpdateDownloadFolder";
        private const string DownloadFileName = "app.appxbundle";

        public static bool HasDownloadFolder()
        {
            return StorageApplicationPermissions.FutureAccessList.ContainsItem(DownloadFolderTokenKey);
        }

        public static async Task<StorageFolder> PickDownloadFolderAsync()
        {
            FolderPicker picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.Downloads,
            };
            picker.FileTypeFilter.Add("*");

            StorageFolder folder = await picker.PickSingleFolderAsync();
            if (folder == null)
            {
                return null;
            }

            StorageApplicationPermissions.FutureAccessList.AddOrReplace(DownloadFolderTokenKey, folder);
            return folder;
        }

        // Only asks for the folder the first time — after that, the token
        // persisted in FutureAccessList already grants direct access, no
        // new picker.
        public static async Task<StorageFolder> GetOrPickDownloadFolderAsync()
        {
            if (HasDownloadFolder())
            {
                return await StorageApplicationPermissions.FutureAccessList.GetFolderAsync(DownloadFolderTokenKey);
            }
            return await PickDownloadFolderAsync();
        }

        public static async Task<StorageFile> DownloadUpdateAsync(IProgress<double> progress)
        {
            StorageFolder folder = await GetOrPickDownloadFolderAsync();
            if (folder == null)
            {
                return null;
            }

            StorageFile file = await folder.CreateFileAsync(DownloadFileName, CreationCollisionOption.ReplaceExisting);

            using (HttpClient client = new HttpClient())
            // ResponseHeadersRead allows reading the body as a stream
            // (block by block) instead of loading the whole download into
            // memory before writing to disk — matters on a low-RAM device.
            using (HttpResponseMessage response = await client.GetAsync(new Uri(DownloadFileUrl), HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long? totalBytes = response.Content.Headers.ContentLength;

                using (Stream networkStream = await response.Content.ReadAsStreamAsync())
                using (Stream fileStream = await file.OpenStreamForWriteAsync())
                {
                    byte[] buffer = new byte[81920];
                    long totalRead = 0;
                    int read;
                    while ((read = await networkStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read);
                        totalRead += read;
                        if (totalBytes.HasValue && totalBytes.Value > 0)
                        {
                            progress?.Report((double)totalRead / totalBytes.Value * 100.0);
                        }
                    }
                }
            }

            return file;
        }

        // Trusted version source: always the installed package's own
        // Identity/@Version from the manifest, never stored anywhere else.
        public static string GetInstalledVersion()
        {
            PackageVersion v = Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
        }

        // Compares Major.Minor.Build.Revision piece by piece, numerically
        // — never as a string ("10" must be greater than "9").
        public static int CompareVersions(string a, string b)
        {
            string[] pa = a.Split('.');
            string[] pb = b.Split('.');
            int len = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < len; i++)
            {
                int na = i < pa.Length && int.TryParse(pa[i], out int va) ? va : 0;
                int nb = i < pb.Length && int.TryParse(pb[i], out int vb) ? vb : 0;
                if (na != nb)
                {
                    return na - nb;
                }
            }
            return 0;
        }

        public static async Task<UpdateCheckResult> CheckAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    string json = await client.GetStringAsync(new Uri(VersionUrl));
                    JsonObject obj = JsonObject.Parse(json);
                    string latest = obj.GetNamedString("version");
                    string installed = GetInstalledVersion();
                    return new UpdateCheckResult
                    {
                        Success = true,
                        Latest = latest,
                        UpdateAvailable = CompareVersions(latest, installed) > 0,
                    };
                }
            }
            catch (Exception ex)
            {
                return new UpdateCheckResult { Success = false, Error = ex.Message };
            }
        }
    }
}
