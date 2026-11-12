using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Diagnostics;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading;

namespace RadioIN;
public partial class MainWindow
{
    async Task TestBetaFeatures()
    {
        var results = new List<string>();
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); results.Add("PASS: " + label); }
        void Reject(Action action, string label) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected,label); }
        string root = Path.Combine(dataDirectory,"beta-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try {
            timer.Stop(); audioTimer.Stop();
            while (refreshing || loading) await Task.Delay(100);
            preferences.Favorites.Clear();
            var track = new Track("Écho", "12:00 · Artiste A", "") { Id = "history-id", ArtistName = "Artiste A" };
            AddTrackFavorite(track); AddTrackFavorite(track);
            Check(preferences.Favorites.Count == 1 && preferences.Favorites[0].Artist == "Artiste A", "Favori historique avec identifiant, artiste sans heure, sans doublon");
            AddTrackFavorite(new Track("Autre", "Artiste B", "/api/station/1/request/1") { Id = "request-id", ArtistName = "Artiste B" });
            Check(preferences.Favorites.Count == 2,"Favori depuis le catalogue");
            Check(FilterFavorites(preferences.Favorites,"echo",0).Count == 1,"Recherche insensible aux accents et à la casse");
            Check(FilterFavorites(preferences.Favorites,"ARTISTE B",0).Single().Id == "request-id","Recherche par artiste");
            Check(FilterFavorites(preferences.Favorites,"",0)[0].Id == "request-id","Tri par ajouts récents");
            Check(FilterFavorites(preferences.Favorites,"",1)[0].Title == "Autre","Tri par titre");
            Check(FilterFavorites(preferences.Favorites,"",2)[0].Artist == "Artiste A","Tri par artiste");
            ShowFavorites(); favoriteSearch!.Text = "echo";
            Check(favoritesList!.Items.Count == 1,"Filtre des favoris connecté à l’interface");
            favoritesList.SelectedIndex = 0; UpdateFavoritesList();
            Check((favoritesList.SelectedItem as Favorite)?.Id == "history-id","Sélection des favoris conservée");
            favoritesWindow!.Close();
            for (int mode = 0; mode < 4; mode++) {
                ChangeMode(mode); await Task.Delay(120); UpdateLayout();
                Check(displayMode == mode,modes[mode] + " : mode attendu actif");
                using var reply = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"success\":true,\"message\":\"Votre demande a été soumise et sera bientôt jouée.\"}") };
                await HandleRequestReply(reply); UpdateLayout();
                Check(InlineNotice.IsVisible && NoticeText.Text.StartsWith("✓"),modes[mode] + " : confirmation intégrée");
                var bounds = InlineNotice.TransformToAncestor(this).TransformBounds(new Rect(0,0,InlineNotice.ActualWidth,InlineNotice.ActualHeight));
                Check(bounds.Left >= 0 && bounds.Right <= ActualWidth && bounds.Bottom <= ActualHeight,modes[mode] + " : message à l’intérieur du lecteur");
                Check(VersionLabel.IsVisible && VersionLabel.Text == AppInfo.Version,modes[mode] + " : version visible");
                SaveBetaPreview("beta-mode-" + mode + ".png");
            }
            for (int theme = 0; theme < 6; theme++) { ApplyDesign(theme); Check(ReferenceEquals(InlineNotice.Background,Play.Background),designs[theme] + " : notification suit le thème"); }
            using (var reply = new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent("{\"success\":false,\"message\":\"Veuillez patienter\"}") }) {
                reply.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
                await HandleRequestReply(reply);
                Check(NoticeText.Text.Contains("refusée") && !Send.IsEnabled && Send.Content.ToString().Contains("Patienter"),"Refus et compte à rebours du serveur");
            }
            requestAllowedAt = DateTime.UtcNow.AddSeconds(-1); TickBetaFeatures(); Check(Send.IsEnabled,"Demandes réactivées après le délai");
            using (var reply = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("invalid") }) { await HandleRequestReply(reply); Check(NoticeText.Text.Contains("non reconnue"),"Réponse inattendue intégrée"); }
            Check(!OwnedWindows.Cast<Window>().Any(w => w.Title == "Radio IN — demande"),"Aucune boîte Windows de demande");
            ChangeMode(0); ShowHistory(this,new RoutedEventArgs());
            var rows = Enumerable.Range(0,40).Select(i => new Track("Titre " + i,"Artiste","") { Id = i.ToString() }).ToList();
            ReplaceTracks(Items,rows); Items.SelectedIndex = 7; UpdateLayout(); FindScroll(Items)!.ScrollToVerticalOffset(4); UpdateLayout();
            double before = FindScroll(Items)!.VerticalOffset;
            ReplaceTracks(Items,rows.Select(t => new Track(t.Title,t.Detail,t.Url) { Id = t.Id }).ToList()); UpdateLayout();
            Check((Items.SelectedItem as Track)?.Id == "7","Sélection conservée lors du rafraîchissement");
            Check(Math.Abs(FindScroll(Items)!.VerticalOffset - before) < .1,"Position de défilement conservée");
            Items.ScrollIntoView(Items.Items[0]); UpdateLayout();
            var favoriteAction = FindVisual<Button>(Items);
            Check(favoriteAction?.Tag is Track,"Bouton favori présent sur une ligne d’historique");
            int beforeClick = preferences.Favorites.Count;
            favoriteAction!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,favoriteAction));
            Check(preferences.Favorites.Count == beforeClick + 1,"Clic sur le coeur ajoute effectivement le titre");
            Search.Text = "Feeder"; searchTimer.Stop(); page = 3; query = "Feeder"; ReplaceTracks(Items,rows);
            Check(Search.Text == "Feeder" && page == 3 && query == "Feeder","Recherche et page conservées");
            TransitionArtwork(null); Check(ReferenceEquals(Art.Source,Icon) && ReferenceEquals(CoverArt.Source,Icon),"Logo de secours sans pochette");
            preferences.SleepFadeSeconds = 30; Volume.Value = .6; SetSleep(1); sleepUntil = DateTime.UtcNow.AddSeconds(15); TickBetaFeatures();
            Check(sleepGain > .45 && sleepGain <= .5 && Volume.Value == .6,"Fondu progressif sans modifier le volume enregistré");
            SetSleep(0); Check(sleepGain == 1 && Volume.Value == .6,"Annulation du fondu restaure le volume");
            SetSleep(1); sleepUntil = DateTime.UtcNow.AddSeconds(-1); TickSleep(); Check(!playing && !retryAt.HasValue,"Minuterie arrête le flux et les reconnexions");
            retryAt = DateTime.UtcNow.AddSeconds(10); TickBetaFeatures(); Check(RetryNow.Visibility == Visibility.Visible,"Bouton de reconnexion disponible");
            retryAt = null; TickBetaFeatures(); Check(RetryNow.Visibility == Visibility.Collapsed,"Bouton de reconnexion masqué hors panne");
            Check(ExecuteMediaCommand(13) && !playing,"Touche multimédia Arrêt");
            Check(!ExecuteMediaCommand(11),"Commande piste suivante non interceptée pour une radio en direct");
            int count = preferences.Favorites.Count; ResetSettingsKeepingFavorites();
            Check(preferences.Favorites.Count == count && Volume.Value == .75 && design == 0 && displayMode == 0,"Réinitialisation conserve les favoris");
            Check(!preferences.MediaKeys,"Contrôle global multimédia désactivé par défaut");
            Check(PlainReleaseNotes("## Nouveautés\n- **Volume**\n[Radio](https://example.org)") == "Nouveautés\n• Volume\nRadio","Notes sans balisage Markdown brut");
            await TestPortableUpdates(root,Check,Reject);
            Check(new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(preferencesPath)).Favorites.Count == count,"Favoris persistés après réinitialisation");
            results.Add("Aucune demande réelle envoyée ; données de test isolées du profil utilisateur.");
        } catch(Exception ex) { results.Add("ERROR: " + ex); Environment.ExitCode = 1; }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"beta12-test.txt"),results); Close();
    }
    void SaveBetaPreview(string name)
    {
        UpdateLayout(); var preview = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth),(int)Math.Ceiling(ActualHeight),96,96,PixelFormats.Pbgra32); preview.Render((Visual)Content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(preview)); using var file = File.Create(Path.Combine(AppContext.BaseDirectory,name)); encoder.Save(file);
    }
    async Task TestPortableUpdates(string root, Action<bool,string> check, Action<Action,string> reject)
    {
        string archive = Path.Combine(root,"good.zip");
        string[] required = { "RadioIN.exe", "RadioIN.exe.config", "libvlc.dll", "libvlccore.dll", "plugins/access/libhttps_plugin.dll", "plugins/codec/libmpg123_plugin.dll" };
        using (var zip = ZipFile.Open(archive,ZipArchiveMode.Create)) foreach (string name in required) zip.CreateEntryFromFile(Path.Combine(AppContext.BaseDirectory,name),name);
        string hash = PortableUpdate.Hash(archive), stage = Path.Combine(root,"valid");
        string distribution = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","RadioIN-Beta-1.3.2-Windows.zip"));
        if (File.Exists(distribution)) {
            var packaged = PortableUpdate.Extract(distribution,PortableUpdate.Hash(distribution),AppInfo.WindowsVersion,Path.Combine(root,"distribution"));
            check(packaged.Count == 26,"ZIP de distribution réel validé : programme, configuration, licences et 20 modules audio");
        }
        var files = PortableUpdate.Extract(archive,hash,AppInfo.WindowsVersion,stage);
        check(files.Count == required.Length,"ZIP : extraction et version du vrai programme vérifiées");
        reject(() => PortableUpdate.Extract(archive,new string('0',64),AppInfo.WindowsVersion,Path.Combine(root,"bad-hash")),"ZIP corrompu refusé");
        reject(() => PortableUpdate.Extract(archive,hash,new Version(9,0,0,0),Path.Combine(root,"wrong-version")),"ZIP de mauvaise version refusé");
        foreach (var name in new[] { "../escape.exe", "plugins/access/../../escape.exe", "unwanted.exe", "C:/escape.exe" }) {
            string bad = Path.Combine(root,"bad-" + Guid.NewGuid().ToString("N") + ".zip"); using (var zip = ZipFile.Open(bad,ZipArchiveMode.Create)) using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write("bad");
            reject(() => PortableUpdate.Extract(bad,PortableUpdate.Hash(bad),AppInfo.WindowsVersion,Path.Combine(root,Guid.NewGuid().ToString("N"))),"ZIP : chemin interdit refusé " + name);
        }
        string missing = Path.Combine(root,"missing.zip"); using (var zip = ZipFile.Open(missing,ZipArchiveMode.Create)) zip.CreateEntryFromFile(typeof(MainWindow).Assembly.Location,"RadioIN.exe");
        reject(() => PortableUpdate.Extract(missing,PortableUpdate.Hash(missing),AppInfo.WindowsVersion,Path.Combine(root,"missing")),"ZIP incomplet refusé");
        string duplicate = Path.Combine(root,"duplicate.zip"); using (var zip = ZipFile.Open(duplicate,ZipArchiveMode.Create)) { zip.CreateEntry("RadioIN.exe"); zip.CreateEntry("RadioIN.exe"); }
        reject(() => PortableUpdate.Extract(duplicate,PortableUpdate.Hash(duplicate),AppInfo.WindowsVersion,Path.Combine(root,"duplicate")),"Entrées ZIP dupliquées refusées");
        string target = Path.Combine(root,"Radio IN portable"); Directory.CreateDirectory(target);
        foreach (var name in required) { string dest = Path.Combine(target,name); Directory.CreateDirectory(Path.GetDirectoryName(dest)); File.WriteAllText(dest,"OLD " + name); }
        File.WriteAllText(Path.Combine(target,"user-file.txt"),"Keep me");
        using (var locked = new FileStream(Path.Combine(target,"libvlccore.dll"),FileMode.Open,FileAccess.Read,FileShare.None)) {
            reject(() => PortableUpdate.Apply(stage,target,Path.Combine(root,"rollback"),files),"Fichier verrouillé : mise à jour interrompue");
        }
        check(required.All(n => File.ReadAllText(Path.Combine(target,n)) == "OLD " + n),"Restauration complète après échec en cours de remplacement");
        PortableUpdate.Apply(stage,target,Path.Combine(root,"backup-success"),files);
        check(required.All(n => PortableUpdate.Hash(Path.Combine(target,n)) == PortableUpdate.Hash(Path.Combine(stage,n))),"Tous les fichiers du ZIP remplacés correctement");
        check(File.ReadAllText(Path.Combine(target,"user-file.txt")) == "Keep me","Fichiers personnels préservés");
        check(File.ReadAllText(Path.Combine(root,"backup-success","RadioIN.exe")) == "OLD RadioIN.exe","Ancienne version conservée en copie de secours");
        string tag = "windows-v9.0.0-beta";
        object Asset(string suffix) => new { name = "RadioIN-Test-" + suffix, browser_download_url = UpdateClient.Repository + "/releases/download/" + tag + "/RadioIN-Test-" + suffix, digest = "sha256:" + hash, size = new FileInfo(archive).Length, state = "uploaded" };
        string json = new JavaScriptSerializer().Serialize(new[] { new { tag_name = tag, assets = new[] { Asset("Setup.exe"), Asset("Windows.zip") } } });
        using (var client = new UpdateClient(new UpdateTestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }))) {
            check((await client.FindAsync(AppInfo.WindowsVersion,CancellationToken.None,true))!.IsZip,"Version portable : ZIP prioritaire");
            check(!(await client.FindAsync(AppInfo.WindowsVersion,CancellationToken.None,false))!.IsZip,"Version installée : installateur prioritaire");
        }
        string helperRoot = Path.Combine(root,"helper"); Directory.CreateDirectory(helperRoot); string helper = Path.Combine(helperRoot,"RadioIN-Updater.exe");
        using (var input = typeof(MainWindow).Assembly.GetManifestResourceStream("RadioIN.updater.exe")) using (var output = File.Create(helper)) input.CopyTo(output);
        string planFile = Path.Combine(helperRoot,"plan.json");
        File.WriteAllText(planFile,new JavaScriptSerializer().Serialize(new PortablePlan { Archive = archive, Hash = hash, Version = AppInfo.WindowsVersion.ToString(4), Target = target, TestOnly = true }));
        using (var process = Process.Start(new ProcessStartInfo(helper,"\"" + planFile + "\"") { UseShellExecute = false, CreateNoWindow = true })) {
            bool done = await Task.Run(() => process.WaitForExit(20000)); check(done && process.ExitCode == 0,"Processus indépendant : mise à jour ZIP de bout en bout");
        }
        check(File.ReadAllText(Path.Combine(helperRoot,"result.txt")).StartsWith("SUCCESS"),"Résultat du processus de mise à jour enregistré");
    }
}
