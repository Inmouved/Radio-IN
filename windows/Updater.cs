using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RadioIN;

sealed class UpdateRelease
{
    internal Version Version = new(0,0,0,0);
    internal string Name = "", Notes = "", Download = "", Sha256 = "", Tag = "";
    internal long Size;
    internal bool IsZip;
}

// No account token is embedded. Only the public release API of this repository is used.
sealed class UpdateClient : IDisposable
{
    internal const string Repository = "https://github.com/Inmouved/Radio-IN";
    internal const string Api = "https://api.github.com/repos/Inmouved/Radio-IN/releases?per_page=100";
    internal const long MaxInstallerSize = 100 * 1024 * 1024;
    readonly HttpClient http;
    internal UpdateClient(HttpMessageHandler? handler = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RadioIN-Windows/1.2");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }
    internal static Version? WindowsVersion(string tag)
    {
        var match = Regex.Match(tag ?? "", @"^windows-v(\d+\.\d+\.\d+(?:\.\d+)?)(?:-beta(?:[.\-]\d+)?)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var version)) return null;
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    }
    internal static bool ValidAssetUrl(string url, string tag)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Query.Length != 0) return false;
        string prefix = "/Inmouved/Radio-IN/releases/download/" + Uri.EscapeDataString(tag) + "/";
        return uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal) && Regex.IsMatch(Uri.UnescapeDataString(uri.AbsolutePath.Substring(prefix.Length)), @"^RadioIN-[A-Za-z0-9._-]+(?:-Setup\.exe|-Windows\.zip)$", RegexOptions.IgnoreCase);
    }
    internal static bool ValidRedirect(Uri uri) => uri.Scheme == "https" && uri.Port == 443 && uri.UserInfo.Length == 0 &&
        (uri.Host == "release-assets.githubusercontent.com" || uri.Host == "objects.githubusercontent.com" || uri.Host == "github-releases.githubusercontent.com");
    public sealed class Asset { public string name { get; set; } = ""; public string browser_download_url { get; set; } = ""; public string digest { get; set; } = ""; public string state { get; set; } = ""; public long size { get; set; } }
    public sealed class Release { public string tag_name { get; set; } = ""; public string name { get; set; } = ""; public string body { get; set; } = ""; public bool draft { get; set; } public bool prerelease { get; set; } public Asset[] assets { get; set; } = Array.Empty<Asset>(); }
    internal static List<UpdateRelease> Parse(string json)
    {
        var serializer = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        var releases = serializer.Deserialize<Release[]>(json) ?? throw new FormatException("Réponse GitHub invalide.");
        var result = new List<UpdateRelease>();
        foreach (var release in releases) {
            var version = WindowsVersion(release.tag_name);
            if (release.draft || version == null) continue;
            // Include Windows beta installers and portable ZIPs; ignore Android releases.
            foreach (var asset in release.assets ?? Array.Empty<Asset>()) {
                if (asset.state != "uploaded" || asset.size <= 0 || asset.size > MaxInstallerSize || !ValidAssetUrl(asset.browser_download_url, release.tag_name) ||
                    !Regex.IsMatch(asset.digest ?? "", @"^sha256:[0-9a-fA-F]{64}$")) continue;
                result.Add(new UpdateRelease { Version = version, Tag = release.tag_name, Name = release.name ?? release.tag_name, Notes = (release.body ?? "").Substring(0, Math.Min(16000, (release.body ?? "").Length)), Download = asset.browser_download_url, IsZip = asset.browser_download_url.EndsWith(".zip",StringComparison.OrdinalIgnoreCase), Sha256 = asset.digest.Substring(7).ToLowerInvariant(), Size = asset.size });
            }
        }
        return result;
    }
    internal async Task<UpdateRelease?> FindAsync(Version current, CancellationToken token, bool preferZip = false)
    {
        var releases = new List<UpdateRelease>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
        // Recent releases are listed by creation date, not semantic version or publication state.
        for (int page = 1; page <= 5; page++) {
            using var response = await http.GetAsync(Api + "&page=" + page, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429) throw new InvalidOperationException("La limite de vérification GitHub est atteinte. Réessayez plus tard.");
            response.EnsureSuccessStatusCode();
            byte[] bytes = await ReadBoundedAsync(response.Content, 4 * 1024 * 1024, timeout.Token).ConfigureAwait(false);
            string json = Encoding.UTF8.GetString(bytes);
            releases.AddRange(Parse(json));
            var count = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.Deserialize<Release[]>(json)?.Length ?? 0;
            if (count < 100) break;
        }
        return releases.Where(r => r.Version > current).OrderByDescending(r => r.Version).ThenByDescending(r => r.IsZip == preferZip).FirstOrDefault();
    }
    static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken token)
    {
        using var input = await content.ReadAsStreamAsync().ConfigureAwait(false);
        using var output = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0) {
            if (output.Length + read > limit) throw new InvalidDataException("Réponse trop volumineuse.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
    internal async Task<string> DownloadAsync(UpdateRelease release, string dataRoot, IProgress<int>? progress, CancellationToken token)
    {
        if (!ValidAssetUrl(release.Download, release.Tag) || release.Size <= 0 || release.Size > MaxInstallerSize || !Regex.IsMatch(release.Sha256, @"^[0-9a-f]{64}$")) throw new InvalidDataException("Informations de téléchargement invalides.");
        string directory = Path.Combine(dataRoot, "updates", release.Version.ToString(4) + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string partial = Path.Combine(directory, "RadioIN-Update.part"), final = Path.Combine(directory, release.IsZip ? "RadioIN-Windows.zip" : "RadioIN-Setup.exe");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try {
            Uri address = new(release.Download);
            for (int redirects = 0; ; redirects++) {
                using var response = await http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if ((int)response.StatusCode >= 300 && (int)response.StatusCode <= 399) {
                    if (redirects >= 5 || response.Headers.Location == null) throw new InvalidDataException("Redirection de téléchargement invalide.");
                    address = new Uri(address, response.Headers.Location);
                    if (!ValidRedirect(address)) throw new InvalidDataException("La redirection sort des serveurs de téléchargement GitHub autorisés.");
                    continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != release.Size) throw new InvalidDataException("La taille du fichier ne correspond pas à la release.");
                using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true)) {
                    var buffer = new byte[81920]; long length = 0; int read; int lastPercent = -1;
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0) {
                        length += read; if (length > release.Size) throw new InvalidDataException("Le téléchargement dépasse la taille annoncée.");
                        await output.WriteAsync(buffer, 0, read, timeout.Token).ConfigureAwait(false);
                        int percent = (int)(length * 100 / release.Size); if (percent != lastPercent) { progress?.Report(percent); lastPercent = percent; }
                    }
                    if (length != release.Size) throw new InvalidDataException("Téléchargement incomplet.");
                }
                VerifyHash(partial, release.Sha256); timeout.Token.ThrowIfCancellationRequested();
                File.Move(partial, final); return final;
            }
        } catch { if (File.Exists(partial)) File.Delete(partial); throw; }
    }
    internal static void VerifyHash(string file, string expected)
    {
        using var input = File.OpenRead(file); using var sha = SHA256.Create();
        string actual = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("L’empreinte SHA-256 est différente. Le fichier ne sera pas lancé.");
    }
    internal static void VerifyInstaller(string file, UpdateRelease release)
    {
        VerifyHash(file, release.Sha256);
        using (var input = File.OpenRead(file)) if (input.ReadByte() != 'M' || input.ReadByte() != 'Z') throw new InvalidDataException("Le fichier n’est pas un programme Windows.");
        var info = FileVersionInfo.GetVersionInfo(file);
        if (!Version.TryParse(info.FileVersion?.Trim(), out var version) || version != release.Version) throw new InvalidDataException("L’installateur ne correspond pas au numéro de version annoncé sur GitHub.");
        if (!string.Equals(info.ProductName?.Trim(), "Radio IN", StringComparison.Ordinal)) throw new InvalidDataException("Le fichier téléchargé n’est pas identifié comme un installateur Radio IN.");
    }
    internal static string InstallArguments(string executable)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(executable)) ?? throw new InvalidDataException("Dossier de l’application introuvable.");
        // Windows paths cannot contain quotes; quoting preserves spaces in portable install paths.
        return "/DIR=\"" + directory.TrimEnd(Path.DirectorySeparatorChar) + "\"";
    }
    public void Dispose() => http.Dispose();
}

public partial class MainWindow
{
    readonly CancellationTokenSource updateLifetime = new();
    CancellationTokenSource? updateOperation;
    Window? updateWindow;
    TextBlock? updateMessage, updateNotes;
    ProgressBar? updateProgress;
    Button? updateDownload, updateInstall, updateCancel;
    UpdateRelease? offeredUpdate;
    string? verifiedInstaller;
    bool updateChecking, updateDownloading;
    void InitializeUpdater()
    {
        Closing += (_, _) => { updateLifetime.Cancel(); updateOperation?.Cancel(); updateWindow?.Close(); };
        if (tray != null) tray.BalloonTipClicked += (_, _) => Dispatcher.Invoke(() => { if (offeredUpdate != null) { RestoreWindow(); ShowUpdateWindow(); } });
        Loaded += async (_, _) => {
            await WaitForStartup();
            if (testMode || !preferences.CheckUpdatesOnStartup) return;
            try { await Task.Delay(2500, updateLifetime.Token); } catch(OperationCanceledException) { return; }
            if (!closed) await CheckUpdatesAsync(false);
        };
        if (Environment.GetCommandLineArgs().Contains("--update-test")) Loaded += async (_, _) => { await WaitForStartup(); await TestUpdates(); };
        if (Environment.GetCommandLineArgs().Contains("--update-live-test")) Loaded += async (_, _) => { await WaitForStartup(); await TestLiveUpdates(); };
        if (Environment.GetCommandLineArgs().Contains("--update-ui-test")) Loaded += async (_, _) => { await WaitForStartup(); await CheckUpdatesAsync(true); };
    }
    void AddUpdateOptions(ContextMenu menu)
    {
        AddUpdateOptions(menu.Items);
    }
    void AddUpdateOptions(MenuItem menu)
    {
        AddUpdateOptions(menu.Items);
    }
    void AddUpdateOptions(ItemCollection items)
    {
        var check = new MenuItem { Header = "Rechercher une mise à jour…" };
        check.Click += async (_, _) => await CheckUpdatesAsync(true); items.Add(check);
        var auto = new MenuItem { Header = "Vérifier les mises à jour au démarrage", IsCheckable = true, IsChecked = preferences.CheckUpdatesOnStartup };
        auto.Click += (_, _) => { preferences.CheckUpdatesOnStartup = auto.IsChecked; SavePreferences(); }; items.Add(auto);
    }
    void ShowUpdateWindow()
    {
        if (updateWindow != null) { updateWindow.Activate(); return; }
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "Version installée : " + AppInfo.Version, Foreground = Brushes.LightSteelBlue, Margin = new Thickness(0,0,0,12) });
        updateMessage = new TextBlock { Text = "Recherche des mises à jour…", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, FontSize = 16, Margin = new Thickness(0,0,0,12) }; body.Children.Add(updateMessage);
        updateNotes = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSteelBlue };
        var scroll = new ScrollViewer { Content = updateNotes, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0,0,0,12) }; body.Children.Add(scroll);
        updateProgress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8, Foreground = Brushes.LightGreen, Margin = new Thickness(0,0,0,12), Visibility = Visibility.Collapsed }; body.Children.Add(updateProgress);
        updateDownload = new Button { Content = "Télécharger la mise à jour", Visibility = Visibility.Collapsed }; updateDownload.Click += async (_, _) => await DownloadUpdateAsync(); body.Children.Add(updateDownload);
        updateInstall = new Button { Content = "Installer et fermer le lecteur", Visibility = Visibility.Collapsed }; updateInstall.Click += async (_, _) => await InstallUpdateAsync(); body.Children.Add(updateInstall);
        updateCancel = new Button { Content = "Annuler le téléchargement", Visibility = Visibility.Collapsed }; updateCancel.Click += (_, _) => updateOperation?.Cancel(); body.Children.Add(updateCancel);
        body.Children.Add(new TextBlock { Text = "Source : Inmouved/Radio-IN sur GitHub.\nLe téléchargement ne coupe pas la radio. L’installation ferme le lecteur et conserve vos favoris et réglages.", Foreground = Brushes.LightSteelBlue, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0,12,0,0) });
        updateWindow = CreateInfoWindow("RADIO IN / MISE À JOUR", body, 550);
        updateWindow.Closed += (_, _) => { updateOperation?.Cancel(); updateWindow = null; updateMessage = null; updateNotes = null; updateProgress = null; updateDownload = null; updateInstall = null; updateCancel = null; };
        UpdateOffer(); updateWindow.Show();
    }
    void UpdateOffer()
    {
        if (updateMessage == null) return;
        if (updateChecking) { updateMessage.Text = "Recherche des mises à jour…"; return; }
        if (offeredUpdate == null) return;
        updateMessage.Text = "Nouvelle version disponible : " + offeredUpdate.Name + (offeredUpdate.IsZip ? "\nMise à jour portable (ZIP)." : "\nInstallateur Windows.");
        updateNotes!.Text = PlainReleaseNotes(offeredUpdate.Notes);
        updateDownload!.Visibility = verifiedInstaller == null ? Visibility.Visible : Visibility.Collapsed;
        updateDownload.IsEnabled = !updateDownloading;
        updateInstall!.Visibility = verifiedInstaller != null ? Visibility.Visible : Visibility.Collapsed;
        updateInstall.Content = offeredUpdate.IsZip ? "Mettre à jour et redémarrer le lecteur" : "Installer et fermer le lecteur";
    }
    async Task CheckUpdatesAsync(bool manual)
    {
        if (closed) return;
        if (manual) ShowUpdateWindow();
        if (updateChecking || updateDownloading) return;
        var elapsed = preferences.LastUpdateCheckUtc.HasValue ? DateTime.UtcNow - preferences.LastUpdateCheckUtc.Value : TimeSpan.MaxValue;
        if (!manual && elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromHours(6)) return;
        updateChecking = true; if (updateMessage != null) updateMessage.Text = "Recherche des mises à jour…";
        offeredUpdate = null; verifiedInstaller = null;
        if (updateDownload != null) updateDownload.Visibility = updateInstall!.Visibility = Visibility.Collapsed;
        updateOperation?.Dispose(); updateOperation = CancellationTokenSource.CreateLinkedTokenSource(updateLifetime.Token);
        var operation = updateOperation;
        try {
            using var client = new UpdateClient();
            bool portable = !File.Exists(Path.Combine(AppContext.BaseDirectory,"unins000.exe"));
            var release = await client.FindAsync(AppInfo.WindowsVersion, operation.Token, portable);
            operation.Token.ThrowIfCancellationRequested();
            if (closed) return;
            preferences.LastUpdateCheckUtc = DateTime.UtcNow; SavePreferences();
            offeredUpdate = release; verifiedInstaller = null; updateChecking = false;
            if (release == null) {
                if (manual && updateMessage != null) { updateMessage.Text = "Aucune version Windows plus récente n’est disponible."; updateNotes!.Text = "Vous utilisez " + AppInfo.Version + "."; updateDownload!.Visibility = updateInstall!.Visibility = Visibility.Collapsed; }
            } else if (!manual && !IsVisible && tray != null) {
                tray.ShowBalloonTip(8000, "Radio IN — mise à jour disponible", release.Name + " · cliquez pour consulter les nouveautés.", System.Windows.Forms.ToolTipIcon.Info);
            } else { ShowUpdateWindow(); UpdateOffer(); }
        } catch(OperationCanceledException) {
            if (!closed && manual && updateMessage != null) updateMessage.Text = "La recherche a expiré. Vérifiez votre connexion et réessayez.";
        } catch(Exception ex) {
            LocalLog.Write("Recherche de mise à jour", ex);
            if (!closed && manual && updateMessage != null) updateMessage.Text = "Vérification impossible. " + (ex is InvalidOperationException ? ex.Message : "Vérifiez votre connexion ou réessayez plus tard.");
        } finally { updateChecking = false; }
    }
    async Task DownloadUpdateAsync()
    {
        if (offeredUpdate == null || updateDownloading) return;
        var release = offeredUpdate; var dialog = updateWindow;
        updateDownloading = true; updateDownload!.IsEnabled = false; updateCancel!.Visibility = Visibility.Visible; updateProgress!.Visibility = Visibility.Visible;
        updateOperation?.Dispose(); updateOperation = CancellationTokenSource.CreateLinkedTokenSource(updateLifetime.Token);
        try {
            using var client = new UpdateClient();
            var progress = new Progress<int>(p => { if (updateWindow == dialog && updateProgress != null) { updateProgress.Value = p; updateMessage!.Text = "Téléchargement : " + p + " %"; } });
            string file = await client.DownloadAsync(release, dataDirectory, progress, updateOperation.Token);
            try { await Task.Run(() => VerifyUpdatePackage(file, release)); }
            catch { try { File.Delete(file); } catch { } throw; }
            if (closed || updateWindow != dialog) return;
            verifiedInstaller = file; UpdateOffer(); updateMessage!.Text = "Téléchargement vérifié. La mise à jour est prête à installer.";
        } catch(OperationCanceledException) { if (!closed && updateWindow == dialog) updateMessage!.Text = "Téléchargement annulé ou délai dépassé. Le lecteur reste ouvert."; }
        catch(Exception ex) { LocalLog.Write("Téléchargement de mise à jour", ex); if (!closed && updateWindow == dialog) updateMessage!.Text = "Mise à jour non installée. " + (ex is InvalidDataException ? ex.Message : "Le téléchargement a échoué. Réessayez plus tard."); }
        finally { updateDownloading = false; if (updateWindow == dialog && updateDownload != null) { updateDownload.IsEnabled = true; updateCancel!.Visibility = Visibility.Collapsed; updateProgress!.Visibility = Visibility.Collapsed; } else if (updateWindow != null) UpdateOffer(); }
    }
    async Task InstallUpdateAsync()
    {
        if (verifiedInstaller == null || offeredUpdate == null || updateInstall == null) return;
        updateInstall.IsEnabled = false;
        try {
            var file = verifiedInstaller; var release = offeredUpdate;
            await Task.Run(() => VerifyUpdatePackage(file, release));
            if (closed || updateWindow == null) return;
            if (testMode) throw new InvalidOperationException("Les tests ne lancent jamais d’installateur.");
            if (release.IsZip) StartPortableUpdate(file,release);
            else Process.Start(new ProcessStartInfo(file, UpdateClient.InstallArguments(typeof(MainWindow).Assembly.Location)) { UseShellExecute = true });
            Close();
        } catch(Exception ex) { LocalLog.Write("Lancement de mise à jour", ex); if (!closed && updateMessage != null) { updateMessage.Text = "Installation non lancée. " + ex.Message; if (updateInstall != null) updateInstall.IsEnabled = true; } }
    }
    static void VerifyUpdatePackage(string file, UpdateRelease release)
    {
        if (!release.IsZip) { UpdateClient.VerifyInstaller(file,release); return; }
        string stage = Path.Combine(Path.GetDirectoryName(file)!,"validation-" + Guid.NewGuid().ToString("N"));
        try { PortableUpdate.Extract(file,release.Sha256,release.Version,stage); }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage,true); }
    }
    void StartPortableUpdate(string file, UpdateRelease release)
    {
        string target = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        PortableUpdate.NoLinks(target);
        // Check write access before closing the player.
        string probe = Path.Combine(target,".radioin-write-" + Guid.NewGuid().ToString("N"));
        using (File.Create(probe)) { } File.Delete(probe);
        string root = Path.GetDirectoryName(file)!, helper = Path.Combine(root,"RadioIN-Updater.exe");
        using (var input = typeof(MainWindow).Assembly.GetManifestResourceStream("RadioIN.updater.exe")) using (var output = File.Create(helper)) input.CopyTo(output);
        File.WriteAllText(helper + ".config","<configuration><startup><supportedRuntime version=\"v4.0\" sku=\".NETFramework,Version=v4.8\"/></startup></configuration>");
        string planFile = Path.Combine(root,"plan.json");
        File.WriteAllText(planFile,new JavaScriptSerializer().Serialize(new PortablePlan { Archive = file, Hash = release.Sha256, Version = release.Version.ToString(4), Target = target, Parent = Process.GetCurrentProcess().Id }));
        Process.Start(new ProcessStartInfo(helper,"\"" + planFile + "\"") { UseShellExecute = false, CreateNoWindow = true });
    }
}
