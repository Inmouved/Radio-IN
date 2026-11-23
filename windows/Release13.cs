using System.IO;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Threading;

namespace RadioIN;
sealed class FavoritesSnapshots
{
    static long lastTimestampTicks;
    internal sealed class Snapshot
    {
        public string Application { get; set; } = "RadioIN-Favorites";
        public int Schema { get; set; } = 1;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public List<MainWindow.Favorite> Favorites { get; set; } = new();
    }
    readonly string root;
    string last = "";
    internal FavoritesSnapshots(string root) { this.root = Path.GetFullPath(root); }
    internal string[] Files => Directory.Exists(root) ? Directory.GetFiles(root,"favoris-*.json").OrderByDescending(Path.GetFileName).ToArray() : Array.Empty<string>();
    internal void Save(List<MainWindow.Favorite> favorites)
    {
        var serializer = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        string json = serializer.Serialize(favorites);
        if (json == last) return;
        Directory.CreateDirectory(root);
        long ticks;
        while (true) {
            long previous = Interlocked.Read(ref lastTimestampTicks);
            ticks = Math.Max(DateTime.UtcNow.Ticks, previous + 1);
            if (Interlocked.CompareExchange(ref lastTimestampTicks,ticks,previous) == previous) break;
        }
        string path = Path.Combine(root,"favoris-" + new DateTime(ticks,DateTimeKind.Utc).ToString("yyyyMMdd-HHmmss-fffffff") + "-" + Guid.NewGuid().ToString("N") + ".json");
        string temp = path + ".tmp";
        File.WriteAllText(temp,serializer.Serialize(new Snapshot { Favorites = favorites })); File.Move(temp,path);
        last = json;
        foreach (var file in Files.Skip(20)) {
            if (!Path.GetFullPath(file).StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Chemin de sauvegarde invalide.");
            File.Delete(file);
        }
    }
    internal static List<MainWindow.Favorite> Read(string file)
    {
        if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new InvalidDataException("Sauvegarde trop volumineuse.");
        var data = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.Deserialize<Snapshot>(File.ReadAllText(file));
        if (data == null || data.Application != "RadioIN-Favorites" || data.Schema != 1 || data.Favorites == null || data.Favorites.Count > 10000 || data.Favorites.Any(f => f == null || string.IsNullOrWhiteSpace(f.Id) || f.Id.Length > 128 || f.Title == null || f.Title.Length > 1024 || f.Artist == null || f.Artist.Length > 1024)) throw new InvalidDataException("Sauvegarde de favoris invalide.");
        return data.Favorites.GroupBy(f => f.Id).Select(g => g.First()).ToList();
    }
}

public partial class MainWindow
{
    FavoritesSnapshots? favoriteSnapshots;
    bool muted;
    bool release13Ready;
    Window? newsWindow;
    void InitializeRelease13()
    {
        InitializeUpcoming();
        MuteButton.Click += (_, _) => ToggleMute(); CoverMute.Click += (_, _) => ToggleMute();
        SourceInitialized += (_, _) => ApplyNativeOpacity();
        SizeChanged += (_, _) => { if (ExtraPanel.IsVisible) Dispatcher.BeginInvoke(new Action(FitExtraPanel)); };
        Activated += (_, _) => { if (release13Ready && !testMode && IsVisible && WindowState == WindowState.Normal && preferences.LastSeenVersion != AppInfo.Version) ShowReleaseNews(); };
        Loaded += async (_, _) => {
            await WaitForStartup();
            favoriteSnapshots = new FavoritesSnapshots(Path.Combine(dataDirectory,"favorites-backups"));
            BackupFavorites(); ApplyReadingSettings(); release13Ready = true;
            if (!testMode) { await Task.Delay(1200); if (!closed && IsVisible && WindowState == WindowState.Normal && preferences.LastSeenVersion != AppInfo.Version) ShowReleaseNews(); }
        };
        if (Environment.GetCommandLineArgs().Contains("--beta13-test")) Loaded += async (_, _) => { await WaitForStartup(); await TestRelease13(); };
        if (Environment.GetCommandLineArgs().Contains("--queue-live-test")) Loaded += async (_, _) => { await WaitForStartup(); await TestLiveQueue(); };
    }
    void BackupFavorites()
    {
        try { favoriteSnapshots?.Save(preferences.Favorites); } catch(Exception ex) { LocalLog.Write("Sauvegarde automatique des favoris",ex); }
    }
    void ShowRestoreFavorites()
    {
        BackupFavorites();
        var files = favoriteSnapshots?.Files ?? Array.Empty<string>();
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "Les 20 dernières copies sont conservées. Restaurer fusionne la copie avec vos favoris actuels.", Foreground = Brushes.LightSteelBlue, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) });
        var list = new ListBox { Height = 200, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        foreach (string file in files) {
            try { var favorites = FavoritesSnapshots.Read(file); list.Items.Add(new ListBoxItem { Tag = file, Content = File.GetLastWriteTime(file).ToString("dd/MM/yyyy HH:mm:ss") + " · " + favorites.Count + " favoris" }); }
            catch { /* Corrupt snapshots remain available on disk but are not offered. */ }
        }
        body.Children.Add(list); var restore = new Button { Content = "Restaurer la sélection", Margin = new Thickness(0,12,0,0), IsEnabled = list.Items.Count > 0 }; body.Children.Add(restore);
        var status = new TextBlock { Foreground = Brushes.LightSteelBlue, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,0), Text = list.Items.Count == 0 ? "Aucune copie valide disponible." : "" }; body.Children.Add(status);
        var dialog = CreateInfoWindow("RADIO IN / RESTAURER LES FAVORIS",body,480);
        restore.Click += (_, _) => {
            if (list.SelectedItem is not ListBoxItem item) { status.Text = "Sélectionnez une sauvegarde."; return; }
            try { RestoreFavorites((string)item.Tag); dialog.Close(); ShowInlineNotice("Favoris restaurés et fusionnés avec votre liste actuelle."); }
            catch(Exception ex) { LocalLog.Write("Restauration favoris",ex); status.Text = "Cette copie ne peut pas être restaurée."; }
        }; dialog.Show();
    }
    void RestoreFavorites(string file)
    {
        var restored = FavoritesSnapshots.Read(file); BackupFavorites();
        preferences.Favorites = preferences.Favorites.Concat(restored).GroupBy(f => f.Id).Select(g => g.First()).ToList();
        SavePreferences(); UpdateFavoritesList(); SyncFavoriteButtons();
    }
    void ToggleMute()
    {
        muted = !muted; ApplyEffectiveVolume(); UpdateListeningAnimation();
        MuteButton.Content = CoverMute.Content = muted ? "Muet" : "Son";
        MuteButton.ToolTip = CoverMute.ToolTip = muted ? "Rétablir le son" : "Couper le son";
        UpdateConnectionBadge();
    }
    void UpdateConnectionBadge()
    {
        if (ConnectionBadge == null) return;
        string label = !playing ? "Arrêté" : retryAt.HasValue ? "Reconnexion" : audio?.State != 3 ? "Chargement" : muted || Volume.Value == 0 ? "Muet" : "Connexion stable";
        ConnectionBadge.Text = "● " + label;
        ConnectionBadge.Foreground = new SolidColorBrush(retryAt.HasValue ? Color.FromRgb(245,202,131) : playing && audio?.State == 3 ? Color.FromRgb(159,217,204) : Color.FromRgb(180,195,214));
    }
    void CopyCurrentTrack()
    {
        if (currentSongId.Length == 0) { ShowInlineNotice("Attendez le chargement du titre en cours."); return; }
        try { Clipboard.SetText(CurrentShareText()); ShowInlineNotice("Titre et lien de la radio copiés."); }
        catch(Exception ex) { LocalLog.Write("Copie du titre",ex); ShowInlineNotice("Le presse-papiers est occupé. Réessayez."); }
    }
    string CurrentShareText() => Artist.Text + " — " + SongTitle.Text + "\nÀ l’écoute sur Radio IN\nhttps://radio-in.duckdns.org/public/radio_in";
    [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetWindowLongPtrW",SetLastError=true)] static extern IntPtr GetExtendedStyle(IntPtr hwnd,int index);
    void ApplyNativeOpacity()
    {
        Opacity = FiniteRange(preferences.PlayerOpacity,.7,1,1);
    }
    static double FiniteRange(double value,double min,double max,double fallback) => double.IsNaN(value) || double.IsInfinity(value) ? fallback : Math.Max(min,Math.Min(max,value));
    void FitExtraPanel()
    {
        if (!ExtraPanel.IsVisible || closed) return;
        double available = SystemParameters.WorkArea.Height;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null) {
            var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
            available = source.CompositionTarget.TransformFromDevice.Transform(new Point(0,screen.WorkingArea.Height)).Y;
        }
        double height = Math.Max(100,Math.Min(285,available - (ActualHeight - ExtraList.ActualHeight) - 24));
        if (Math.Abs(ExtraList.Height - height) > 1) ExtraList.Height = height;
    }
    void ApplyReadingSettings()
    {
        preferences.PlayerOpacity = FiniteRange(preferences.PlayerOpacity,.7,1,1);
        preferences.TextScale = FiniteRange(preferences.TextScale,1,1.25,1);
        preferences.AnimationMode = Math.Max(0,Math.Min(2,preferences.AnimationMode));
        double scale = preferences.TextScale;
        SongTitle.FontSize = 17 * scale; ReducedSong.FontSize = 15 * scale; CoverTitle.FontSize = 20 * scale;
        Artist.FontSize = CoverArtist.FontSize = Items.FontSize = LiveHistory.FontSize = ExtraList.FontSize = Search.FontSize = NoticeText.FontSize = 13 * scale;
        if (favoritesList != null) favoritesList.FontSize = 13 * scale;
        SongTitle.MotionMode = ReducedSong.MotionMode = CoverTitle.MotionMode = preferences.AnimationMode;
        ApplyNativeOpacity(); UpdateListeningAnimation(false); UpdateListeningAnimation();
        if (preferences.AnimationMode == 2) { TransitionArtwork(Art.Source); ApplyDesign(design); SetCoverControls(CoverHud.IsHitTestVisible); }
        if (ExtraPanel.IsVisible) Dispatcher.BeginInvoke(new Action(FitExtraPanel));
    }
    Window? settingsWindow;
    void ShowSettings()
    {
        if (settingsWindow != null) { settingsWindow.Activate(); return; }
        var body = new StackPanel();
        var groups = new ComboBox {
            ItemsSource = new[] { "Apparence", "Écoute", "Notifications", "Mises à jour", "Sauvegardes" },
            SelectedIndex = 0, Margin = new Thickness(0,0,0,16)
        };
        System.Windows.Automation.AutomationProperties.SetName(groups, "Rubrique des paramètres");
        body.Children.Add(groups);
        var scroll = new ScrollViewer { MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(scroll);
        var feedback = new TextBlock { Foreground = Brushes.LightSteelBlue, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,0) };
        body.Children.Add(feedback);
        var sections = Enumerable.Range(0, 5).Select(_ => new StackPanel()).ToArray();
        void Label(int group, string text) => sections[group].Children.Add(new TextBlock { Text = text, Foreground = Brushes.White });
        void Choose(string label, string[] choices, int selected, Action<int> change)
        {
            Label(0, label);
            var control = new ComboBox { ItemsSource = choices, SelectedIndex = selected, Margin = new Thickness(0,5,0,12) };
            System.Windows.Automation.AutomationProperties.SetName(control, label);
            control.SelectionChanged += (_, _) => { if (control.SelectedIndex >= 0) change(control.SelectedIndex); };
            sections[0].Children.Add(control);
        }
        void Option(int group, string label, bool value, Action<CheckBox> change)
        {
            var control = new CheckBox { Content = label, IsChecked = value, Foreground = Brushes.White, Margin = new Thickness(0,0,0,12) };
            control.Click += (_, _) => change(control);
            sections[group].Children.Add(control);
        }
        void Command(int group, string label, Action action)
        {
            var control = new Button { Content = label, Margin = new Thickness(0,0,0,6) };
            control.Click += (_, _) => action();
            sections[group].Children.Add(control);
        }
        Choose("Design", designs, design, ApplyDesign);
        Choose("Affichage", modes, displayMode, ChangeMode);
        Choose("Taille du texte", new[] { "100 %", "110 %", "125 %" }, preferences.TextScale >= 1.2 ? 2 : preferences.TextScale > 1 ? 1 : 0,
            value => { preferences.TextScale = new[] { 1d,1.1,1.25 }[value]; ApplyReadingSettings(); SavePreferences(); });
        Choose("Animations", new[] { "Normales", "Discrètes", "Désactivées" }, preferences.AnimationMode,
            value => { preferences.AnimationMode = value; ApplyReadingSettings(); SavePreferences(); });
        Label(0, "Opacité du lecteur · 70 à 100 %");
        var opacity = new Slider { Minimum = .7, Maximum = 1, Value = preferences.PlayerOpacity, ToolTip = "Opacité du lecteur" };
        System.Windows.Automation.AutomationProperties.SetName(opacity, "Opacité du lecteur");
        opacity.ValueChanged += (_, _) => { preferences.PlayerOpacity = opacity.Value; ApplyNativeOpacity(); SavePreferences(); };
        sections[0].Children.Add(opacity);
        Option(0, "Toujours visible", preferences.Pin, box => Pin.IsChecked = box.IsChecked);
        bool launchWithWindows = false;
        try { launchWithWindows = !testMode && StartupRegistration.Enabled; }
        catch (Exception ex) { LocalLog.Write("Lecture démarrage Windows", ex); }
        Option(1, "Lancer avec Windows", launchWithWindows, box => {
            try { StartupRegistration.Set(box.IsChecked == true); feedback.Text = ""; }
            catch (Exception ex) {
                box.IsChecked = box.IsChecked != true;
                LocalLog.Write("Configuration démarrage Windows", ex);
                feedback.Text = "Impossible de modifier le démarrage Windows.";
            }
        });
        Option(1, "Au démarrage de Windows, réduire près de l’horloge", preferences.LaunchHidden, box => { preferences.LaunchHidden = box.IsChecked == true; SavePreferences(); });
        Option(1, "Écouter automatiquement au lancement", preferences.AutoPlay, box => { preferences.AutoPlay = box.IsChecked == true; SavePreferences(); });
        Option(1, "Touches multimédias en arrière-plan", preferences.MediaKeys, box => {
            ChangeMediaKeys(box.IsChecked == true); box.IsChecked = preferences.MediaKeys;
            feedback.Text = preferences.MediaKeys ? "" : "Les raccourcis du lecteur restent disponibles.";
        });
        Command(1, "Raccourcis clavier…", ShowShortcuts);
        Option(2, "M’avertir au changement de titre", preferences.Notifications, box => { preferences.Notifications = box.IsChecked == true; SavePreferences(); });
        Option(2, "M’avertir quand une demande est annoncée", preferences.RequestAlerts, box => { preferences.RequestAlerts = box.IsChecked == true; SavePreferences(); UpdateRequestTracking(); });
        Option(3, "Vérifier au démarrage et toutes les 30 minutes", preferences.CheckUpdatesOnStartup, async box => await SetAutomaticUpdates(box.IsChecked == true));
        Command(3, "Rechercher une mise à jour…", () => _ = CheckUpdatesAsync(true));
        Command(4, "Exporter les favoris et réglages…", ExportSettings);
        Command(4, "Importer une sauvegarde…", ImportSettings);
        Command(4, "Restaurer des favoris…", ShowRestoreFavorites);
        Command(4, "Réinitialiser les réglages…", ConfirmResetSettings);
        scroll.Content = sections[0];
        groups.SelectionChanged += (_, _) => {
            if (groups.SelectedIndex < 0) return;
            feedback.Text = ""; scroll.Content = sections[groups.SelectedIndex]; scroll.ScrollToTop();
        };
        settingsWindow = CreateInfoWindow("RADIO IN / PARAMÈTRES", body, 460);
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show();
    }
    void ShowShortcuts() => ShowStyledMessage("Raccourcis clavier",
        "Ctrl+E  Lecture / arrêt\nCtrl+↑ / ↓  Volume\nCtrl+M  Mode suivant\nCtrl+D  Design suivant\nCtrl+F  Ajouter ou retirer des favoris\nCtrl+L  Mes favoris\nCtrl+H  Derniers titres diffusés\nCtrl+R  Demander un titre\nCtrl+N  Notifications\nTab  Afficher et parcourir les commandes masquées\nÉchap  Fermer le panneau ouvert");
    void ShowReleaseNews()
    {
        if (newsWindow != null) { newsWindow.Activate(); return; }
        preferences.LastSeenVersion = AppInfo.Version; SavePreferences();
        var body = new StackPanel(); body.Children.Add(new TextBlock { Text = AppInfo.Version, Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,12) });
        body.Children.Add(new TextBlock { Text = "Les réglages sont regroupés dans Paramètres. Le cœur permet d’ajouter ou de retirer le titre en cours. Mon historique d’écoute regroupe toutes vos écoutes et la session en cours.", Foreground = Brushes.LightSteelBlue, TextWrapping = TextWrapping.Wrap });
        var close = new Button { Content = "C’est parti", Margin = new Thickness(0,16,0,0) }; body.Children.Add(close);
        newsWindow = CreateInfoWindow("RADIO IN / NOUVEAUTÉS",body,490); close.Click += (_, _) => newsWindow?.Close(); newsWindow.Closed += (_, _) => newsWindow = null; newsWindow.Show();
    }
}
