using System.IO;
using System.Diagnostics;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RadioIN;
public partial class MainWindow
{
    async Task TestRelease13()
    {
        var results = new List<string>();
        void Check(bool yes,string name) { if (!yes) throw new InvalidOperationException(name); results.Add("PASS: " + name); }
        void Reject(Action action,string name) { bool bad = false; try { action(); } catch { bad = true; } Check(bad,name); }
        string root = Path.Combine(dataDirectory,"beta13-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try {
            while (!release13Ready) await Task.Delay(20);
            timer.Stop(); audioTimer.Stop(); queueTimer.Stop();
            var deadline = DateTime.UtcNow.AddSeconds(20); while ((refreshing || loading) && DateTime.UtcNow < deadline) await Task.Delay(50);
            DateTime now = DateTime.UtcNow;
            string Feed(string title = "Écho",bool stale = false,long? updated = null,bool available = true) => new JavaScriptSerializer().Serialize(new {
                schema = 1, station = "radio_in", available, updated_at = updated ?? new DateTimeOffset(now).ToUnixTimeSeconds(), stale,
                items = new[] { new { title, artist = "Artiste", art = "https://radio-in.duckdns.org/api/station/1/art/test.jpg", played_at = (long?)new DateTimeOffset(now.AddMinutes(12)).ToUnixTimeSeconds(), is_request = true },
                    new { title = "Sans horaire", artist = "Test", art = "https://example.org/secret", played_at = (long?)null, is_request = false } }
            });
            upcoming = UpcomingSnapshot.Parse(Feed(),now);
            Check(upcoming.Tracks.Count == 2 && upcoming.Tracks[0].IsRequest,"File : titres et demandes auditeur lus");
            Check(upcoming.Tracks[0].ExpectedUtc.HasValue && upcoming.Tracks[0].DateLabel.Contains("estimé"),"File : date et heure estimées");
            Check(!upcoming.Tracks[1].ExpectedUtc.HasValue && upcoming.Tracks[1].DateLabel.Contains("confirmer"),"Heure absente non inventée");
            Check(upcoming.Tracks[1].Art == "","Pochettes hors radio refusées");
            Check(UpcomingTrack.DescribeCountdown(now.AddMinutes(12),now).Contains("12 min"),"Compte à rebours calculé");
            Check(UpcomingTrack.DescribeCountdown(now.AddSeconds(-1),now).StartsWith("En attente"),"Horaire dépassé sans compte à rebours négatif");
            Reject(() => UpcomingSnapshot.Parse(Feed(updated:new DateTimeOffset(now.AddMinutes(-10)).ToUnixTimeSeconds()),now),"File périmée refusée");
            Reject(() => UpcomingSnapshot.Parse(Feed(available:false),now),"File indisponible reconnue");
            Reject(() => UpcomingSnapshot.Parse("not JSON",now),"File malformée refusée");
            Check(FindQueued("echo","ARTISTE") != null,"Doublon reconnu sans distinction d’accents ou de casse");
            Items.ItemsSource = new[] { new Track("Écho","Artiste","/api/station/1/request/test") { Id = "echo", ArtistName = "Artiste" } }; Items.SelectedIndex = 0;
            Check(ConfirmDuplicate("/api/station/1/request/test") && NoticeText.Text.Contains("déjà annoncé"),"Avertissement avant demande déjà annoncée");
            Check(!ConfirmDuplicate("/api/station/1/request/test"),"Deuxième clic permet la confirmation explicite sans envoi de test");
            upcoming.Stale = true; Check(FindQueued("Écho","Artiste") == null,"Ancienne liste non utilisée pour avertir à tort"); upcoming.Stale = false;
            for (int mode = 0; mode < 4; mode++) {
                ChangeMode(mode); await Task.Delay(200); ShowUpcoming(); UpdateLayout();
                Check(ExtraPanel.IsVisible && ExtraList.Items.Count == 2 && Panels.Visibility == Visibility.Collapsed,modes[mode] + " : panneau À venir accessible");
                Check(displayMode != 3 || CoverStage.Visibility == Visibility.Collapsed,modes[mode] + " : contenu tient dans le lecteur");
                SaveBetaPreview("beta13-queue-" + mode + ".png");
                CloseExtraPanel(); Check(ExtraPanel.Visibility == Visibility.Collapsed && (mode != 3 || CoverStage.IsVisible),modes[mode] + " : retour au lecteur");
            }
            ShowUpcoming(); upcoming.UpdatedUtc = DateTime.UtcNow.AddMinutes(-3); TickUpcoming();
            Check(upcoming == null && ExtraList.Items.Count == 0 && ExtraStatus.Text.Contains("indisponible"),"Liste trop ancienne masquée avec message");
            upcoming = UpcomingSnapshot.Parse(Feed(stale:true),now); ShowUpcoming(); Check(ExtraStatus.Text.Contains("dernière liste"),"Panne temporaire signalée sur cache récent");
            upcoming = UpcomingSnapshot.Parse(Feed(),now);
            Volume.Value = .43; ToggleMute(); Check(muted && Volume.Value == .43 && MuteButton.ToolTip.ToString().Contains("Rétablir"),"Muet conserve le volume");
            Volume.Value = .55; ToggleMute(); Check(!muted && Volume.Value == .55,"Rétablissement au volume choisi pendant le muet");
            ToggleMute(); Check(!listeningAnimated,"Animation arrêtée en muet"); ToggleMute();
            currentSongId = "share"; SongTitle.Text = "Titre"; Artist.Text = "Artiste";
            Check(CurrentShareText().Contains("Artiste — Titre") && CurrentShareText().Contains("https://radio-in.duckdns.org/public/radio_in"),"Texte de partage contient titre, artiste et lien");
            sessionHistory.Clear(); sessionEvents.Clear();
            RecordSessionTrack("a","Titre A","Artiste A",100); RecordSessionTrack("a","Titre A","Artiste A",100);
            Check(sessionHistory.Count == 1,"Session : rafraîchissement sans doublon");
            RecordSessionTrack("a","Titre A","Artiste A",200); Check(sessionHistory.Count == 2,"Session : nouvelle diffusion du même titre conservée");
            ShowSessionHistory(); Check(ExtraList.Items.Count == 2 && ExtraList.ItemTemplate == Items.ItemTemplate,"Session affichée avec actions favoris");
            for (int i = 0; i < 510; i++) RecordSessionTrack("t" + i,"Titre " + i,"Test",i);
            Check(sessionHistory.Count == 500 && sessionHistory[0].Title == "Titre 509","Session bornée à 500 titres, plus récent en premier");
            var snapshots = new FavoritesSnapshots(Path.Combine(root,"backups"));
            var a = new Favorite { Id = "a", Title = "A", Artist = "Test" };
            snapshots.Save(new List<Favorite> { a }); snapshots.Save(new List<Favorite> { a });
            Check(snapshots.Files.Length == 1,"Sauvegardes : aucun doublon sur simple réglage du volume");
            string savedA = snapshots.Files.Single();
            Check(FavoritesSnapshots.Read(savedA).Single().Id == "a","Sauvegarde automatiquement relisible");
            preferences.Favorites = new List<Favorite> { new Favorite { Id = "b", Title = "B", Artist = "Test" } };
            RestoreFavorites(savedA); RestoreFavorites(savedA);
            Check(preferences.Favorites.Count == 2 && preferences.Favorites.Any(f => f.Id == "b"),"Restauration fusionne sans doublon ni perte des favoris actuels");
            for (int i = 0; i < 25; i++) snapshots.Save(new List<Favorite> { new Favorite { Id = "f" + i, Title = "Favori " + i, Artist = "Test" } });
            Check(snapshots.Files.Length == 20 && FavoritesSnapshots.Read(snapshots.Files[0])[0].Id == "f24","Les 20 dernières copies conservées");
            string badFile = Path.Combine(root,"bad.json"); File.WriteAllText(badFile,"{\"Application\":\"Other\",\"Schema\":1,\"Favorites\":[]}");
            Reject(() => RestoreFavorites(badFile),"Sauvegarde invalide refusée avant toute mutation");
            Check(preferences.Favorites.Count == 2,"Échec de restauration sans perte de favoris");
            preferences.PlayerOpacity = .8; preferences.TextScale = 1.25; preferences.AnimationMode = 2; ApplyReadingSettings();
            Check(AllowsTransparency && Math.Abs(Opacity - .8) < .001 && (GetExtendedStyle(new WindowInteropHelper(this).Handle,-20).ToInt64() & 0x80000) != 0,"Opacité appliquée par WPF à une fenêtre Windows transparente");
            Check(SongTitle.FontSize == 21.25 && ExtraList.FontSize == 16.25,"Taille des titres et listes augmentée");
            SongTitle.Text = new string('W',100); ChangeMode(0); UpdateLayout();
            Check(!SongTitle.IsScrolling && !listeningAnimated,"Animations et titre défilant désactivés");
            TransitionArtwork(Icon); Check(PreviousArt.Source == null,"Aucune transition de pochette si animations désactivées");
            for (int theme = 0; theme < 6; theme++) { ApplyDesign(theme); Check(!((LinearGradientBrush)TintLayer.Background).GradientStops[0].HasAnimatedProperties,designs[theme] + " : couleur sans animation"); }
            for (int mode = 0; mode < 4; mode++) {
                ChangeMode(mode); await Task.Delay(150); ShowUpcoming(); UpdateLayout();
                Check(ExtraPanel.ActualWidth > 300 && ActualWidth == (mode == 2 ? 860 : 440),modes[mode] + " : grandes polices sans changement de largeur");
                SaveBetaPreview("beta13-large-text-" + mode + ".png");
            }
            preferences.AnimationMode = 1; ApplyReadingSettings(); Check(SongTitle.MotionMode == 1 && dissolve.TimeSpan.TotalMilliseconds == 400,"Animations discrètes configurées");
            preferences.AnimationMode = 0; preferences.TextScale = 1; preferences.PlayerOpacity = 1; ApplyReadingSettings();
            SavePreferences();
            var prefs = new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(preferencesPath));
            Check(prefs.PlayerOpacity == 1 && prefs.TextScale == 1 && prefs.AnimationMode == 0,"Nouveaux réglages persistés");
            ShowReleaseNews(); Check(newsWindow != null && preferences.LastSeenVersion == AppInfo.Version,"Nouveautés affichées et version mémorisée"); newsWindow!.Close();
            Check(newsWindow == null,"Présentation des nouveautés fermable");
            var menu = new ContextMenu(); AddRelease13Options(menu);
            var favoritesMenu = menu.Items.OfType<MenuItem>().Single(x => (string)x.Header == "Favoris");
            Check(favoritesMenu.Items.OfType<MenuItem>().Any(x => (string)x.Header == "♡ Ajouter le titre aux favoris") && favoritesMenu.Items.OfType<MenuItem>().Any(x => (string)x.Header == "Mes favoris…"),"Favoris regroupés et commandes conservées");
            Check(menu.Items.OfType<MenuItem>().Any(x => (string)x.Header == "À venir…") && menu.Items.OfType<MenuItem>().Any(x => (string)x.Header == "Aide"),"Commandes de lecture et aide regroupées");
            Check(HistoryButton.Visibility == Visibility.Collapsed,"Historique retiré de la barre et accessible via le menu options");
            ShowOptions();
            var options = OptionsButton.ContextMenu!;
            Check(options.Items.OfType<MenuItem>().Any(x => (string)x.Header == "Historique…"),"Historique présent dans le menu ⋯");
            Check(options.Items.OfType<MenuItem>().Any(x => (string)x.Header == "Écoute et notifications") && options.Items.OfType<MenuItem>().Any(x => (string)x.Header == "Mises à jour"),"Réglages d’écoute et mises à jour accessibles dans les sous-menus");
            options.IsOpen = false;
            playing = false; retryAt = null; UpdateConnectionBadge(); Check(ConnectionBadge.Text.Contains("Arrêté"),"Indicateur connexion : arrêté");
            playing = true; retryAt = DateTime.UtcNow.AddSeconds(10); UpdateConnectionBadge(); Check(ConnectionBadge.Text.Contains("Reconnexion"),"Indicateur connexion : reconnexion");
            playing = false; retryAt = null;
            results.Add("Aucune demande musicale réelle envoyée ; profil de test isolé.");
        } catch(Exception ex) { results.Add("ERROR: " + ex); Environment.ExitCode = 1; }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"beta13-test.txt"),results); Close();
    }
    async Task TestLiveQueue()
    {
        var results = new List<string>();
        try {
            await Refresh(); await RefreshUpcoming();
            if (upcoming == null) throw new InvalidOperationException(queueError);
            ShowUpcoming(); await Task.Delay(1500); UpdateLayout(); SaveBetaPreview("beta13-queue-live.png");
            results.Add("File HTTPS réelle : " + upcoming.Tracks.Count + " titres ; cache périmé=" + upcoming.Stale);
            results.Add("Premier titre : " + upcoming.Tracks.FirstOrDefault()?.Title + " ; " + upcoming.Tracks.FirstOrDefault()?.DateLabel);
            if (!playing) TogglePlay(this,new RoutedEventArgs()); await Task.Delay(12000); CheckAudio();
            if (audio?.State != 3 || audio.Time <= 0 || sessionHistory.Count == 0) throw new InvalidOperationException("Lecture ou historique de session non démarré.");
            results.Add("Audio réel : état=" + audio.State + " ; temps=" + audio.Time + " ms ; session=" + sessionHistory.Count);
            Volume.Value = .35; ToggleMute(); CheckAudio();
            if (!muted || Volume.Value != .35 || listeningAnimated || audio.VolumePercent != 0) throw new InvalidOperationException("Muet/animation");
            ToggleMute(); await Task.Delay(200); results.Add("Volume VLC après rétablissement=" + audio.VolumePercent + "; curseur=" + Volume.Value + "; gain=" + sleepGain);
            if (Math.Abs(audio.VolumePercent - 35) > 1) throw new InvalidOperationException("Volume rétabli incorrect"); results.Add("Muet réel VLC : 0 %, puis rétablissement à 35 %.");
            ShowSessionHistory(); UpdateLayout(); SaveBetaPreview("beta13-session-live.png");
            TogglePlay(this,new RoutedEventArgs());
            results.Add("Aucune demande envoyée ; API publiques consultées en lecture seule.");
        } catch(Exception ex) { results.Add("ERROR: " + ex); Environment.ExitCode = 1; }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"queue-live-test.txt"),results); Close();
    }
}
