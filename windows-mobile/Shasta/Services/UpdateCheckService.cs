using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Storage;
using Windows.System;

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
        //
        // NO "/app/" path segment — confirmed by directly fetching the
        // live site (not just the GitHub Contents API, which reflects
        // repo structure and was misleading): actions/upload-pages-
        // artifact with path: app publishes that folder's CONTENTS as
        // the site ROOT, not as an /app/ subdirectory. Every URL here
        // originally pointed at a path that 404s — the download page's
        // own links worked anyway because they're relative, resolving
        // correctly against wherever the page itself is served from.
        private const string VersionUrl = "https://ro2342.github.io/shasta/version.json";
        public const string DownloadPageUrl = "https://ro2342.github.io/shasta/";
        public const string DownloadFileUrl = "https://ro2342.github.io/shasta/app.appxbundle";

        private const string DownloadFileName = "app.appxbundle";

        // Downloads straight into the app's own LocalFolder — no
        // FolderPicker. A picker was here originally (matching the
        // wp-apps template's desktop-oriented pattern), but on phone form
        // factor there's no guarantee a meaningful "Downloads" location
        // even exists the way FolderPicker expects, and it's an extra UI
        // step the user has to notice and complete. LocalFolder is always
        // writable without asking, and Launcher.LaunchFileAsync can open
        // a file from the calling app's own storage without needing it
        // to live somewhere externally picked.
        public static async Task<StorageFile> DownloadUpdateAsync(IProgress<double> progress)
        {
            StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                DownloadFileName, CreationCollisionOption.ReplaceExisting);

            using (HttpClient client = new HttpClient())
            // ResponseHeadersRead allows reading the body as a stream
            // (block by block) instead of loading the whole download into
            // memory before writing to disk — matters on a low-RAM device.
            // Cache-busted with the current time: the app itself has no
            // notion of "which build" to ask for by version number before
            // downloading, and app.appxbundle is always the same filename
            // across releases, the same trap version.json/index.html
            // already hit on the download page.
            using (HttpResponseMessage response = await client.GetAsync(
                new Uri($"{DownloadFileUrl}?t={DateTimeOffset.UtcNow.Ticks}"), HttpCompletionOption.ResponseHeadersRead))
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

        public static async Task<bool> InstallUpdateAsync(StorageFile file)
        {
            LauncherOptions options = new LauncherOptions { TreatAsUntrusted = false };
            return await Launcher.LaunchFileAsync(file, options);
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
                    // Cache-busted for the same reason as the download
                    // above — version.json needs to be read fresh every
                    // check, not whatever the OS/CDN cached last time.
                    string json = await client.GetStringAsync(
                        new Uri($"{VersionUrl}?t={DateTimeOffset.UtcNow.Ticks}"));
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
