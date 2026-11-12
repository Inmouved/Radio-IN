using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RadioIN;

sealed class UpdateTestHandler : HttpMessageHandler
{
    readonly Func<HttpRequestMessage, HttpResponseMessage> reply;
    internal UpdateTestHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) => this.reply = reply;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(reply(request)); }
}
public partial class MainWindow
{
    static string TestHash(byte[] bytes) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    async Task TestUpdates()
    {
        var results = new List<string>();
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); results.Add("PASS: " + name); }
        async Task Rejected(Func<Task> action, string name) { bool rejected = false; try { await action(); } catch { rejected = true; } Check(rejected, name); }
        string root = Path.Combine(dataDirectory, "update-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        byte[] installer = File.ReadAllBytes(typeof(MainWindow).Assembly.Location);
        string hash = TestHash(installer);
        object Release(string tag, bool draft = false, string? digest = null, string? url = null) => new { tag_name = tag, name = "Radio IN — " + tag, body = "Nouveautés de test\n• Recherche de mises à jour\n• Les favoris sont conservés", draft, prerelease = true, assets = new[] { new { name = "RadioIN-Beta-Test-Setup.exe", browser_download_url = url ?? UpdateClient.Repository + "/releases/download/" + tag + "/RadioIN-Beta-Test-Setup.exe", digest = digest ?? "sha256:" + hash, size = installer.LongLength, state = "uploaded" } } };
        string Json(params object[] releases) => new JavaScriptSerializer().Serialize(releases);
        var good = UpdateClient.Parse(Json(Release("windows-v" + AppInfo.WindowsVersion.ToString(4) + "-beta"))).Single();
        try {
            Check(AppInfo.WindowsVersion == Version.Parse(System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(MainWindow).Assembly.Location).FileVersion.Trim()), "Version interne conforme au lecteur de mises à jour");
            Check(preferences.CheckUpdatesOnStartup, "Vérification au démarrage activée par défaut");
            Check(UpdateClient.WindowsVersion("windows-v1.10.0-beta")! > UpdateClient.WindowsVersion("windows-v1.9.0-beta")!, "Comparaison numérique, pas alphabétique");
            Check(UpdateClient.WindowsVersion("windows-v" + new Version(AppInfo.WindowsVersion.Major, AppInfo.WindowsVersion.Minor, AppInfo.WindowsVersion.Build, AppInfo.WindowsVersion.Revision + 1).ToString(4) + "-beta")! > AppInfo.WindowsVersion, "Révisions à quatre nombres reconnues");
            Check(UpdateClient.WindowsVersion("android-v99.0.0-beta") == null, "Versions Android ignorées");
            Check(UpdateClient.Parse(Json(Release("windows-v9.0.0-beta", true))).Count == 0, "Brouillons ignorés");
            Check(UpdateClient.Parse(Json(Release("windows-v1.2.0-beta", digest:""))).Count == 0, "Empreinte absente refusée");
            Check(UpdateClient.Parse(Json(Release("windows-v1.2.0-beta", url:"https://example.org/RadioIN-Test-Setup.exe"))).Count == 0, "Téléchargements hors dépôt refusés");
            Check(!UpdateClient.ValidAssetUrl("http://github.com/Inmouved/Radio-IN/releases/download/windows-v1.2.0-beta/RadioIN-Test-Setup.exe", "windows-v1.2.0-beta"), "HTTP refusé");
            Check(!UpdateClient.ValidAssetUrl(UpdateClient.Repository + "/releases/download/windows-v1.2.0-beta/RadioIN-Android.apk", "windows-v1.2.0-beta"), "APK et fichiers non installateurs refusés");
            Check(!UpdateClient.ValidRedirect(new Uri("http://release-assets.githubusercontent.com/file")) && !UpdateClient.ValidRedirect(new Uri("https://example.org/file")), "Redirections HTTP et externes refusées");
            using (var client = new UpdateClient(new UpdateTestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json(Release("windows-v1.1.0-beta"), Release("windows-v1.9.0-beta"), Release("windows-v1.10.0-beta"), Release("android-v99.0.0-beta"))) }))) {
                var latest = await client.FindAsync(AppInfo.WindowsVersion, CancellationToken.None);
                Check(latest?.Version == new Version(1,10,0,0), "Plus haute version Windows bêta sélectionnée");
            }
            using (var client = new UpdateClient(new UpdateTestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json(Release("windows-v1.0.0-beta"), Release("windows-v1.1.0-beta"))) }))) Check(await client.FindAsync(AppInfo.WindowsVersion, CancellationToken.None) == null, "Aucune réinstallation de version identique ou antérieure");
            using (var client = new UpdateClient(new UpdateTestHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)))) await Rejected(async () => { await client.FindAsync(AppInfo.WindowsVersion, CancellationToken.None); }, "Limite GitHub gérée");
            using (var client = new UpdateClient(new UpdateTestHandler(_ => throw new HttpRequestException("Hors ligne simulé")))) await Rejected(async () => { await client.FindAsync(AppInfo.WindowsVersion, CancellationToken.None); }, "Panne réseau gérée sans lancer d’installation");
            using (var client = new UpdateClient(new UpdateTestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(installer) }))) {
                string file = await client.DownloadAsync(good, root, null, CancellationToken.None);
                UpdateClient.VerifyInstaller(file, good); Check(File.ReadAllBytes(file).SequenceEqual(installer), "Téléchargement exact, SHA-256 et version du programme vérifiés");
                var wrong = new UpdateRelease { Version = new Version(9,0,0,0), Sha256 = good.Sha256 };
                await Rejected(() => Task.Run(() => UpdateClient.VerifyInstaller(file, wrong)), "Installateur dont la version diffère du tag refusé");
            }
            int requests = 0;
            using (var client = new UpdateClient(new UpdateTestHandler(_ => ++requests == 1 ? new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://release-assets.githubusercontent.com/test") } } : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(installer) }))) {
                string file = await client.DownloadAsync(good, root, null, CancellationToken.None); Check(requests == 2 && File.Exists(file), "Redirection officielle GitHub suivie");
            }
            var corrupted = (byte[])installer.Clone(); corrupted[corrupted.Length - 1] ^= 1;
            using (var client = new UpdateClient(new UpdateTestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(corrupted) }))) await Rejected(async () => { await client.DownloadAsync(good, root, null, CancellationToken.None); }, "Fichier corrompu refusé avant installation");
            using (var client = new UpdateClient(new UpdateTestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[10]) }))) await Rejected(async () => { await client.DownloadAsync(good, root, null, CancellationToken.None); }, "Téléchargement tronqué refusé");
            using (var cancel = new CancellationTokenSource()) using (var client = new UpdateClient(new UpdateTestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(installer) }))) { cancel.Cancel(); await Rejected(async () => { await client.DownloadAsync(good, root, null, cancel.Token); }, "Téléchargement annulable"); }
            Check(Directory.GetFiles(root, "*.part", SearchOption.AllDirectories).Length == 0, "Fichiers partiels nettoyés après échec ou annulation");
            Check(UpdateClient.InstallArguments(@"C:\Radio Test\RadioIN.exe") == "/DIR=\"C:\\Radio Test\"", "Mise à jour dans le dossier existant avec espaces");
            string paddedSetup = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "RadioIN-Beta-1.1-Correctif-1-Setup.exe"));
            if (File.Exists(paddedSetup)) {
                var actual = System.Diagnostics.FileVersionInfo.GetVersionInfo(paddedSetup);
                Check(actual.ProductName != "Radio IN" && actual.ProductName.Trim() == "Radio IN", "Régression reproduite avec les métadonnées du véritable installateur Inno");
                UpdateClient.VerifyInstaller(paddedSetup, new UpdateRelease { Version = new Version(1,1,0,1), Sha256 = TestHash(File.ReadAllBytes(paddedSetup)) });
                Check(true, "Installateur publié avec nom complété par des espaces accepté par le contrôle corrigé");
            }
            string compatibleSetup = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "RadioIN-Beta-1.3-Setup.exe"));
            if (File.Exists(compatibleSetup)) {
                var actual = System.Diagnostics.FileVersionInfo.GetVersionInfo(compatibleSetup);
                Check(actual.ProductName == "Radio IN", "Nom du nouvel installateur compatible avec la comparaison stricte de Bêta 1.1");
                UpdateClient.VerifyInstaller(compatibleSetup, new UpdateRelease { Version = AppInfo.WindowsVersion, Sha256 = TestHash(File.ReadAllBytes(compatibleSetup)) });
                Check(true, "Véritable nouvel installateur vérifié : nom, version et SHA-256");
            }
            var menu = new ContextMenu(); AddUpdateOptions(menu); Check(menu.Items.OfType<MenuItem>().Count() == 2, "Commandes de recherche manuelle et automatique présentes");
            offeredUpdate = good; offeredUpdate.Name = "Radio IN — Bêta 1.2 (démonstration)"; ShowUpdateWindow(); updateWindow!.UpdateLayout();
            Check(updateDownload!.Visibility == Visibility.Visible && updateInstall!.Visibility == Visibility.Collapsed, "Aucune installation proposée avant téléchargement vérifié");
            var bitmap = new RenderTargetBitmap((int)updateWindow.ActualWidth, (int)updateWindow.ActualHeight, 96,96, PixelFormats.Pbgra32); bitmap.Render((Visual)updateWindow.Content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var output = File.Create(Path.Combine(AppContext.BaseDirectory, "update-preview.png"))) encoder.Save(output);
            updateWindow.Close(); preferences.CheckUpdatesOnStartup = false; SavePreferences();
            Check(!new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(preferencesPath)).CheckUpdatesOnStartup, "Désactivation de la vérification automatique mémorisée");
            results.Add("Aucun installateur exécuté ; aucune préférence personnelle modifiée.");
        } catch(Exception ex) { results.Add("ERROR: " + ex); }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "update-test.txt"), results); Close();
    }
    async Task TestLiveUpdates()
    {
        var results = new List<string>();
        try {
            using var client = new UpdateClient();
            var offered = await client.FindAsync(AppInfo.WindowsVersion, updateLifetime.Token);
            results.Add("Recherche publique GitHub réussie ; version plus récente=" + (offered?.Version.ToString() ?? "aucune"));
            var publicRelease = await client.FindAsync(new Version(0,0,0,0), updateLifetime.Token);
            if (publicRelease != null) {
                string file = await client.DownloadAsync(publicRelease, dataDirectory, null, updateLifetime.Token);
                UpdateClient.VerifyHash(file, publicRelease.Sha256);
                results.Add("Téléchargement réel et SHA-256 GitHub vérifiés : " + new FileInfo(file).Length + " octets");
                var actual = System.Diagnostics.FileVersionInfo.GetVersionInfo(file).FileVersion;
                results.Add("Tag=" + publicRelease.Tag + "; version interne du fichier=" + actual);
                try { UpdateClient.VerifyInstaller(file, publicRelease); results.Add("Version du fichier conforme au tag"); } catch(InvalidDataException) { results.Add("Protection confirmée : fichier dont la version diffère du tag refusé"); }
            }
            results.Add("Aucun installateur exécuté. Lecture seule du dépôt.");
        } catch(Exception ex) { results.Add("ERROR: " + ex); }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "update-live-test.txt"), results); Close();
    }
}
