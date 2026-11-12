using System.IO;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace RadioIN;
public partial class MainWindow
{
    void InitializeChrome()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        using (var png = typeof(MainWindow).Assembly.GetManifestResourceStream("RadioIN.icon.png")) {
            Icon = BitmapFrame.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        }
        using (var ico = typeof(MainWindow).Assembly.GetManifestResourceStream("RadioIN.icon.ico"))
        using (var loadedIcon = new System.Drawing.Icon(ico, new System.Drawing.Size(32, 32))) {
            applicationIcon = (System.Drawing.Icon)loadedIcon.Clone();
        }
        Closed += (_, _) => { applicationIcon?.Dispose(); applicationIcon = null; };
        // Listen on the whole window so the outer margins and free spaces work too.
        MouseLeftButtonDown += (_, e) => DragWindow(this, e);
        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        CloseWindowButton.Click += (_, _) => Close();
        CloseWindowButton.MouseEnter += (_, _) => CloseWindowButton.Background = new SolidColorBrush(Color.FromRgb(164, 48, 61));
        CloseWindowButton.MouseLeave += (_, _) => CloseWindowButton.ClearValue(Control.BackgroundProperty);
        AddRoundedCorners(this);
        if (Environment.GetCommandLineArgs().Contains("--drag-test")) Loaded += async (_, _) => {
            var results = new List<string>();
            try {
                await WaitForStartup();
                void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); results.Add("PASS: " + name); }
                void Free(double x, double y, string name) => Check(CanStartWindowDrag(this, InputHitTest(new Point(x, y)) as DependencyObject), name);
                void Interactive(FrameworkElement element, string name) {
                    var center = element.TranslatePoint(new Point(element.ActualWidth / 2, element.ActualHeight / 2), this);
                    Check(!CanStartWindowDrag(this, InputHitTest(center) as DependencyObject), name);
                }
                for (int mode = 0; mode < modes.Length; mode++) {
                    ChangeMode(mode); await Task.Delay(300); UpdateLayout();
                    Free(ActualWidth / 2, 8, modes[mode] + " : marge supérieure");
                    Free(8, ActualHeight / 2, modes[mode] + " : marge gauche");
                    Free(ActualWidth - 8, ActualHeight / 2, modes[mode] + " : marge droite");
                    Free(ActualWidth / 2, ActualHeight - 7, modes[mode] + " : marge inférieure");
                    Interactive(DesignButton, modes[mode] + " : choix du design protégé");
                    Interactive(ModeButton, modes[mode] + " : choix du mode protégé");
                    Interactive(CloseWindowButton, modes[mode] + " : fermeture protégée");
                    if (mode == 0 || mode == 2) {
                        Interactive(Play, modes[mode] + " : lecture protégée");
                        Interactive(FavoriteButton, modes[mode] + " : favoris protégés");
                        Interactive(Volume, modes[mode] + " : volume protégé");
                    }
                    if (mode == 0) Interactive(Pin, "Compact : épingle protégée");
                    if (mode == 2) { Interactive(Search, "Large : recherche protégée"); Interactive(Items, "Large : catalogue protégé"); Interactive(LiveHistory, "Large : historique protégé"); }
                }
                Check(!CanStartWindowDrag(this, new System.Windows.Documents.Hyperlink()), "Liens protégés");
            } catch(Exception ex) { results.Add("ERROR: " + ex); Environment.ExitCode = 1; }
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "drag-test.txt"), results); Close();
        };
    }
    static bool CanStartWindowDrag(Window window, DependencyObject? node)
    {
        while (node != null) {
            if (node == window) return true;
            if (node is System.Windows.Controls.Primitives.ButtonBase ||
                node is System.Windows.Controls.Primitives.TextBoxBase || node is PasswordBox ||
                node is System.Windows.Controls.Primitives.Selector || node is ScrollViewer ||
                node is System.Windows.Controls.Primitives.RangeBase || node is System.Windows.Controls.Primitives.Thumb ||
                node is System.Windows.Controls.Primitives.MenuBase || node is System.Windows.Documents.Hyperlink) return false;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return false;
    }
    static void DragWindow(Window window, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !CanStartWindowDrag(window, e.OriginalSource as DependencyObject)) return;
        try { window.DragMove(); e.Handled = true; } catch (InvalidOperationException) { }
    }
    static void AddRoundedCorners(Window window)
    {
        window.SourceInitialized += (_, _) => {
            try { int preference = 2; DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(window).Handle, 33, ref preference, sizeof(int)); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        };
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    public sealed class Favorite
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public override string ToString() => Title + " — " + Artist;
    }
    public sealed class Preferences
    {
        public int Design { get; set; }
        public int Mode { get; set; }
        public double Volume { get; set; } = 0.75;
        public double? Left { get; set; }
        public double? Top { get; set; }
        public bool Pin { get; set; }
        public bool Notifications { get; set; }
        public bool LaunchHidden { get; set; }
        public bool AutoPlay { get; set; }
        public bool CheckUpdatesOnStartup { get; set; } = true;
        public int SleepFadeSeconds { get; set; } = 30;
        public bool MediaKeys { get; set; }
        public double PlayerOpacity { get; set; } = 1;
        public double TextScale { get; set; } = 1;
        public int AnimationMode { get; set; }
        public string LastSeenVersion { get; set; } = "";
        public DateTime? LastUpdateCheckUtc { get; set; }
        public List<Favorite> Favorites { get; set; } = new();
    }
    Preferences preferences = new();
    Forms.NotifyIcon? tray;
    System.Drawing.Icon? applicationIcon;
    Window? toast, favoritesWindow;
    ListBox? favoritesList;
    readonly DispatcherTimer hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    string currentSongId = "", notifiedSongId = "";
    bool restoring, hoverControls, keyboardHover, preferencesReady;
    Color autoColor = Color.FromRgb(60, 83, 110);
    DateTime? sleepUntil, retryAt, requestAllowedAt;
    int reconnectAttempts;
    long lastAudioTime;
    DateTime progressAt = DateTime.UtcNow;
    bool testMode => AppInfo.IsTest;
    string dataDirectory => AppInfo.DataDirectory;
    string preferencesPath => Path.Combine(dataDirectory, "preferences.json");

    void InitializeFeatures()
    {
        if (!testMode || Environment.GetCommandLineArgs().Contains("--saved-startup-test")) try {
            if (File.Exists(preferencesPath)) preferences = new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(preferencesPath)) ?? new Preferences();
        } catch(Exception ex) { LocalLog.Write("Lecture préférences", ex); Status.Text = "Préférences illisibles : valeurs par défaut utilisées."; }
        preferences.Favorites = preferences.Favorites ?? new List<Favorite>();
        restoring = true;
        Volume.Value = double.IsNaN(preferences.Volume) || double.IsInfinity(preferences.Volume) ? .75 : Math.Max(0, Math.Min(1, preferences.Volume));
        Pin.IsChecked = preferences.Pin;
        ApplyDesign(Math.Max(0, Math.Min(designs.Length - 1, preferences.Design)));
        int restoredMode = Math.Max(0, Math.Min(modes.Length - 1, preferences.Mode));
        var arguments = Environment.GetCommandLineArgs();
        int startupTestIndex = Array.IndexOf(arguments, "--startup-test");
        if (startupTestIndex >= 0 && startupTestIndex + 1 < arguments.Length && int.TryParse(arguments[startupTestIndex + 1], out var testModeIndex) && testModeIndex >= 0)
            restoredMode = Math.Max(0, Math.Min(modes.Length - 1, testModeIndex));
        if ((!testMode || arguments.Contains("--saved-startup-test")) && preferences.Left.HasValue && preferences.Top.HasValue) {
            var savedScreen = Forms.Screen.AllScreens.FirstOrDefault(s => preferences.Left.Value >= s.WorkingArea.Left && preferences.Left.Value < s.WorkingArea.Right && preferences.Top.Value >= s.WorkingArea.Top && preferences.Top.Value < s.WorkingArea.Bottom);
            if (savedScreen != null) { WindowStartupLocation = WindowStartupLocation.Manual; Left = preferences.Left.Value; Top = preferences.Top.Value; }
        }
        restoring = false;
        OptionsButton.Click += (_, _) => ShowOptions();
        FavoriteButton.Click += (_, _) => AddFavorite();
        PreviewKeyDown += Shortcuts;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Tab) keyboardHover = true; };
        PreviewMouseDown += (_, _) => keyboardHover = false;
        MouseEnter += (_, _) => { hoverTimer.Stop(); SetHoverControls(true); };
        MouseLeave += (_, _) => { hoverTimer.Stop(); hoverTimer.Start(); };
        hoverTimer.Tick += (_, _) => { hoverTimer.Stop(); if (!IsMouseOver && (!keyboardHover || !AudioControls.IsKeyboardFocusWithin && !CoverHud.IsKeyboardFocusWithin)) SetHoverControls(false); };
        GotKeyboardFocus += (_, _) => { if (displayMode == 1 || displayMode == 3) SetHoverControls(true); };
        LostKeyboardFocus += (_, _) => { hoverTimer.Stop(); hoverTimer.Start(); };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && tray != null) { Hide(); tray.Visible = true; } };
        bool startupLayoutApplied = false;
        ContentRendered += (_, _) => {
            if (startupLayoutApplied) return;
            startupLayoutApplied = true;
            // In the lightweight launcher the constructor runs before Application.Run.
            // Wait for the first rendered frame: startup WM_SIZE must not overwrite the restored width.
            if (preferences.Left.HasValue && preferences.Top.HasValue && (!testMode || arguments.Contains("--saved-startup-test"))) {
                var screen = Forms.Screen.AllScreens.FirstOrDefault(s => preferences.Left.Value >= s.WorkingArea.Left && preferences.Left.Value < s.WorkingArea.Right && preferences.Top.Value >= s.WorkingArea.Top && preferences.Top.Value < s.WorkingArea.Bottom);
                if (screen != null) { Left = preferences.Left.Value; Top = preferences.Top.Value; }
            }
            ChangeMode(restoredMode);
            preferencesReady = true;
        };
        Closing += (_, _) => { SavePreferences(); hoverTimer.Stop(); toast?.Close(); favoritesWindow?.Close(); tray?.Dispose(); tray = null; };
        if (!testMode) SetupTray();
        if (Environment.GetCommandLineArgs().Contains("--features-test")) Loaded += async (_, _) => { await WaitForStartup(); await TestFeatures(); };
        if (startupTestIndex >= 0) Loaded += async (_, _) => {
            var results = new List<string>();
            try {
                await WaitForStartup();
                await Task.Delay(1500);
                var deadline = DateTime.UtcNow.AddSeconds(20);
                while (loading && DateTime.UtcNow < deadline) await Task.Delay(100);
                UpdateLayout();
                results.Add("Expected mode=" + restoredMode + "; restored mode=" + displayMode);
                results.Add("UI access=" + Dispatcher.CheckAccess() + "; context=" + System.Threading.SynchronizationContext.Current?.GetType().Name);
                results.Add("Catalogue loading=" + loading + "; rows=" + Items.Items.Count + "; status=" + Status.Text);
                results.Add("History count=" + history.Count + "; window visible=" + IsVisible);
                SavePreferences();
                var persisted = new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(preferencesPath));
                results.Add("Persisted mode=" + persisted.Mode + "; position=" + persisted.Left + "," + persisted.Top);
                if (displayMode != restoredMode || loading || !Dispatcher.CheckAccess()) throw new InvalidOperationException("Startup regression");
                if (restoredMode == 2 && !arguments.Contains("--catalogue-failure-test")) {
                    TogglePlay(this, new RoutedEventArgs()); await Task.Delay(7000);
                    results.Add("Audio state=" + audio?.State + "; time=" + audio?.Time);
                    TogglePlay(this, new RoutedEventArgs());
                }
            } catch(Exception ex) { results.Add("ERROR: " + ex); }
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "startup-test-" + restoredMode + (arguments.Contains("--catalogue-failure-test") ? "-offline" : "") + ".txt"), results); Close();
        };
        bool iconTestStarted = false;
        if (Environment.GetCommandLineArgs().Contains("--icon-test")) Loaded += async (_, _) => {
            if (iconTestStarted) return; iconTestStarted = true;
            var results = new List<string>();
            try {
                await WaitForStartup();
                results.Add("Title=" + Title);
                results.Add("Window icon=" + ((BitmapSource)Icon).PixelWidth + "px");
                using (var resource = typeof(MainWindow).Assembly.GetManifestResourceStream("RadioIN.icon.ico"))
                using (var reader = new BinaryReader(resource)) { reader.ReadInt16(); reader.ReadInt16(); results.Add("ICO frames=" + reader.ReadInt16()); }
                using (var extracted = System.Drawing.Icon.ExtractAssociatedIcon(typeof(MainWindow).Assembly.Location))
                using (var pixels = extracted.ToBitmap()) { results.Add("EXE icon=" + extracted.Width + "px; corner alpha=" + pixels.GetPixel(0,0).A + "; tile=" + pixels.GetPixel(pixels.Width/2,pixels.Height/4)); }
                SetupTray(); results.Add("Tray uses logo=" + ReferenceEquals(tray?.Icon, applicationIcon));
                MinimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(300); results.Add("Minimized to tray=" + !IsVisible);
                RestoreWindow(); await Task.Delay(300); results.Add("Restored=" + IsVisible);
                ShowFavorites(); results.Add("Favorites icon=" + (favoritesWindow?.Icon == Icon)); favoritesWindow?.Close();
            } catch(Exception ex) { results.Add("ERROR: " + ex); }
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "icon-test.txt"), results); Close();
        };
    }
    async Task WaitForStartup()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!preferencesReady && !closed && DateTime.UtcNow < deadline) await Task.Delay(20);
        if (!preferencesReady) throw new InvalidOperationException("Window startup did not complete");
    }
    void SavePreferences()
    {
        if (restoring || !preferencesReady) return;
        preferences.Design = design; preferences.Mode = displayMode; preferences.Volume = Volume.Value; preferences.Pin = Pin.IsChecked == true;
        if (WindowState == WindowState.Normal) { preferences.Left = Left; preferences.Top = Top; }
        else if (!RestoreBounds.IsEmpty) { preferences.Left = RestoreBounds.Left; preferences.Top = RestoreBounds.Top; }
        try {
            BackupFavorites();
            Directory.CreateDirectory(dataDirectory);
            string temp = preferencesPath + ".tmp";
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(preferences));
            if (File.Exists(preferencesPath)) File.Replace(temp, preferencesPath, preferencesPath + ".bak"); else File.Move(temp, preferencesPath);
        } catch(Exception ex) { LocalLog.Write("Sauvegarde préférences", ex); Status.Text = "Impossible d’enregistrer les préférences."; }
    }
    void SetupTray()
    {
        tray = new Forms.NotifyIcon { Icon = applicationIcon ?? System.Drawing.SystemIcons.Application, Text = "Radio IN — " + AppInfo.Version, Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Afficher Radio IN", null, (_, _) => Dispatcher.Invoke(RestoreWindow));
        menu.Items.Add("Lecture / Arrêt", null, (_, _) => Dispatcher.Invoke(() => TogglePlay(this, new RoutedEventArgs())));
        menu.Items.Add("Favoris", null, (_, _) => Dispatcher.Invoke(() => { RestoreWindow(); ShowFavorites(); }));
        menu.Items.Add("Quitter", null, (_, _) => Dispatcher.Invoke(Close));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreWindow);
    }
    void RestoreWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
    void ShowOptions()
    {
        var menu = new ContextMenu();
        void Add(string text, Action action) { var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("♡ Ajouter le titre aux favoris", AddFavorite);
        Add("Mes favoris…", ShowFavorites);
        AddRelease13Options(menu);
        var notify = new MenuItem { Header = "Notifications de changement de titre", IsCheckable = true, IsChecked = preferences.Notifications };
        notify.Click += (_, _) => { preferences.Notifications = notify.IsChecked; SavePreferences(); }; menu.Items.Add(notify);
        var pin = new MenuItem { Header = "Toujours visible", IsCheckable = true, IsChecked = Topmost };
        pin.Click += (_, _) => { Pin.IsChecked = pin.IsChecked; SavePreferences(); }; menu.Items.Add(pin);
        var sleep = new MenuItem { Header = sleepUntil.HasValue ? "Minuterie : " + Math.Ceiling((sleepUntil.Value - DateTime.UtcNow).TotalMinutes) + " min restantes" : "Minuterie de sommeil" };
        foreach (int minutes in new[] { 15, 30, 60, 0 }) {
            var item = new MenuItem { Header = minutes == 0 ? "Désactiver" : minutes + " minutes" };
            item.Click += (_, _) => SetSleep(minutes); sleep.Items.Add(item);
        }
        var customSleep = new MenuItem { Header = "Durée et fondu personnalisés…" }; customSleep.Click += (_, _) => ShowCustomSleep(); sleep.Items.Add(customSleep);
        menu.Items.Add(sleep);
        Add("Réduire près de l’horloge", () => WindowState = WindowState.Minimized);
        Add("Raccourcis clavier…", () => MessageBox.Show(this, "Ctrl+E : lecture / arrêt\nCtrl+↑ / ↓ : volume\nCtrl+M : mode suivant\nCtrl+D : design suivant\nCtrl+F : ajouter le titre aux favoris\nCtrl+L : liste des favoris\nCtrl+H : historique\nCtrl+R : demandes de titres\nCtrl+N : notifications\nÉchap : fermer le panneau (Compact)", "Radio IN — raccourcis"));
        AddPublicOptions(menu);
        Add("Quitter", Close);
        OptionsButton.ContextMenu = menu; menu.PlacementTarget = OptionsButton; menu.IsOpen = true;
    }
    void SetHoverControls(bool show)
    {
        hoverControls = show;
        if (displayMode == 1) AudioControls.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (displayMode == 3) SetCoverControls(show);
    }
    void Shortcuts(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) { if (e.Key == Key.Escape && displayMode == 0) CloseDrawer(this, e); return; }
        e.Handled = ExecuteShortcut(e.Key);
    }
    bool ExecuteShortcut(Key key)
    {
        switch (key) {
            case Key.E: TogglePlay(this, new RoutedEventArgs()); break;
            case Key.Up: Volume.Value = Math.Min(1, Volume.Value + .05); break;
            case Key.Down: Volume.Value = Math.Max(0, Volume.Value - .05); break;
            case Key.M: ChangeMode((displayMode + 1) % modes.Length); break;
            case Key.D: ApplyDesign((design + 1) % designs.Length); break;
            case Key.F: AddFavorite(); break;
            case Key.L: ShowFavorites(); break;
            case Key.N: preferences.Notifications = !preferences.Notifications; SavePreferences(); break;
            case Key.H: if (displayMode != 0) ChangeMode(0); ShowHistory(this, new RoutedEventArgs()); break;
            case Key.R: if (displayMode != 0) ChangeMode(0); ShowRequests(this, new RoutedEventArgs()); break;
            default: return false;
        }
        return true;
    }
    void AddFavorite()
    {
        if (currentSongId.Length == 0) { Status.Text = "Attendez le chargement du titre en cours."; return; }
        if (!preferences.Favorites.Any(f => f.Id == currentSongId)) preferences.Favorites.Add(new Favorite { Id = currentSongId, Title = SongTitle.Text, Artist = Artist.Text });
        FavoriteButton.Content = "♥"; Status.Text = "Titre enregistré dans vos favoris."; SavePreferences(); UpdateFavoritesList();
    }
    void UpdateFavoritesList() { if (favoritesList != null) { string? id = (favoritesList.SelectedItem as Favorite)?.Id; var rows = FilterFavorites(preferences.Favorites, favoriteSearch?.Text ?? "", favoriteSort?.SelectedIndex ?? 0); favoritesList.ItemsSource = rows; favoritesList.SelectedItem = rows.FirstOrDefault(f => f.Id == id); } }
    void ShowFavorites()
    {
        if (favoritesWindow != null) { favoritesWindow.Show(); favoritesWindow.Activate(); return; }
        favoritesList = new ListBox { Margin = new Thickness(12), Height = 270, Background = new SolidColorBrush(Color.FromRgb(17, 27, 40)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        var panel = new DockPanel(); var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12) };
        var request = new Button { Content = "Demander ce titre" }; var remove = new Button { Content = "Retirer" };
        actions.Children.Add(request); actions.Children.Add(remove); DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions); panel.Children.Add(favoritesList);
        var header = new DockPanel { Margin = new Thickness(12), Background = Brushes.Transparent };
        var closeFavorites = new Button { Content = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 1,1 L 11,11 M 11,1 L 1,11"), Stroke = Brushes.White, StrokeThickness = 2, Width = 12, Height = 12, Stretch = Stretch.Uniform }, Width = 34, Height = 32, Padding = new Thickness(0), Margin = new Thickness(0), Background = new SolidColorBrush(Color.FromRgb(45, 64, 87)), BorderBrush = new SolidColorBrush(Color.FromRgb(116, 145, 181)), ToolTip = "Fermer les favoris" };
        System.Windows.Automation.AutomationProperties.SetName(closeFavorites, "Fermer les favoris");
        DockPanel.SetDock(closeFavorites, Dock.Right); header.Children.Add(closeFavorites); header.Children.Add(new TextBlock { Text = "RADIO IN / MES FAVORIS", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
        DockPanel.SetDock(header, Dock.Top); panel.Children.Insert(0, header);
        AddFavoriteFilters(panel);
        favoritesWindow = new Window { Owner = this, Icon = Icon, Title = "Radio IN — Mes favoris", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Background = new SolidColorBrush(Color.FromRgb(20, 29, 42)), Width = 470, SizeToContent = SizeToContent.Height, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var favoriteDialog = favoritesWindow; closeFavorites.Click += (_, _) => favoriteDialog.Close(); header.MouseLeftButtonDown += (_, e) => DragWindow(favoriteDialog, e); AddRoundedCorners(favoriteDialog);
        favoritesWindow.Closed += (_, _) => { favoritesWindow = null; favoritesList = null; favoriteSearch = null; favoriteSort = null; };
        remove.Click += (_, _) => { if (favoritesList?.SelectedItem is Favorite f) { preferences.Favorites.Remove(f); SavePreferences(); UpdateFavoritesList(); FavoriteButton.Content = preferences.Favorites.Any(x => x.Id == currentSongId) ? "♥" : "♡"; } };
        request.Click += async (_, _) => {
            if (!(favoritesList?.SelectedItem is Favorite f)) return;
            request.IsEnabled = false;
            try {
                string found = ""; int totalPages = 1;
                for (int p = 1; p <= totalPages && found.Length == 0; p++) {
                    using var doc = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/api/station/1/requests?rowCount=100&page=" + p + "&searchPhrase=" + Uri.EscapeDataString(f.Title)));
                    totalPages = doc.RootElement.GetProperty("total_pages").GetInt32();
                    var row = doc.RootElement.GetProperty("rows").EnumerateArray().FirstOrDefault(r => S(r.GetProperty("song"), "id") == f.Id);
                    if (row.TryGetProperty("request_url", out var url)) found = url.ToString();
                }
                if (found.Length == 0) { ShowInlineNotice("Ce titre n’est pas disponible dans le catalogue des demandes."); return; }
                pendingFavoriteTitle = f.Title; pendingFavoriteArtist = f.Artist;
                await SubmitRequest(found);
            } catch { if (!closed) ShowInlineNotice("Catalogue indisponible, réessayez plus tard."); }
            finally { request.IsEnabled = true; }
        };
        favoritesList.FontSize = 13 * preferences.TextScale;
        UpdateFavoritesList(); favoritesWindow.Show();
    }
    void SetSleep(int minutes)
    {
        sleepUntil = minutes > 0 ? DateTime.UtcNow.AddMinutes(minutes) : (DateTime?)null;
        sleepGain = 1; ApplyEffectiveVolume();
        Status.Text = minutes > 0 ? "Arrêt dans " + minutes + " minutes." : "Minuterie désactivée.";
    }
    bool TickSleep()
    {
        if (!sleepUntil.HasValue || DateTime.UtcNow < sleepUntil.Value) return false;
        sleepUntil = null; sleepGain = 1; retryAt = null; playing = false; audio?.Stop(); UpdateListeningAnimation(false); Play.Content = "▶  Écouter"; Status.Text = "Lecture arrêtée par la minuterie."; return true;
    }
    void NotifyTrack(string id)
    {
        if (id == notifiedSongId) return;
        bool first = notifiedSongId.Length == 0; notifiedSongId = id;
        if (!preferences.Notifications || first || testMode) return;
        ShowTrackNotification();
    }
    void ShowTrackNotification()
    {
        toast?.Close();
        var box = new DockPanel { Margin = new Thickness(14) };
        box.Children.Add(new Image { Source = Art.Source, Width = 62, Height = 62, Margin = new Thickness(0, 0, 12, 0) });
        var text = new StackPanel(); text.Children.Add(new TextBlock { Text = "RADIO IN", Foreground = Brushes.LightSteelBlue, FontSize = 10 });
        text.Children.Add(new TextBlock { Text = SongTitle.Text, Foreground = Brushes.White, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = Artist.Text, Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap }); box.Children.Add(text);
        toast = new Window { Width = 330, SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Topmost = true, Background = new SolidColorBrush(Color.FromRgb(20, 29, 42)), Content = box };
        toast.Left = SystemParameters.WorkArea.Right - 345; toast.Top = SystemParameters.WorkArea.Bottom - 135;
        var popup = toast; var dismiss = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        dismiss.Tick += (_, _) => { dismiss.Stop(); popup.Close(); }; popup.Closed += (_, _) => dismiss.Stop();
        popup.MouseLeftButtonUp += (_, _) => { popup.Close(); RestoreWindow(); }; popup.Show(); dismiss.Start();
    }
    async Task LoadArtwork(string url)
    {
        try {
            var bytes = await client.GetByteArrayAsync(url);
            if (closed || url != artUrl) return;
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 600; image.StreamSource = new MemoryStream(bytes); image.EndInit(); image.Freeze();
            TransitionArtwork(image);
            var small = new TransformedBitmap(image, new ScaleTransform(24.0 / image.PixelWidth, 24.0 / image.PixelHeight));
            var pixels = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0); byte[] data = new byte[pixels.PixelWidth * pixels.PixelHeight * 4]; pixels.CopyPixels(data, pixels.PixelWidth * 4, 0);
            long r = 0, g = 0, b = 0, count = 0;
            for (int i = 0; i < data.Length; i += 4) { int brightness = data[i] + data[i + 1] + data[i + 2]; if (brightness < 60 || brightness > 700) continue; b += data[i]; g += data[i + 1]; r += data[i + 2]; count++; }
            autoColor = count > 0 ? Color.FromRgb((byte)(r / count), (byte)(g / count), (byte)(b / count)) : Color.FromRgb(60, 83, 110);
            if (design == 5) ApplyDesign(5);
        } catch { if (!closed && url == artUrl) { TransitionArtwork(Icon); autoColor = Color.FromRgb(60, 83, 110); if (design == 5) ApplyDesign(5); } }
    }
    void ScheduleReconnect()
    {
        audio?.Stop(); UpdateListeningAnimation(false); reconnectAttempts++; retryAt = DateTime.UtcNow.AddSeconds(Math.Min(30, 3 * Math.Pow(2, Math.Min(4, reconnectAttempts - 1))));
        UpdateConnection(!NetworkInterfaceAvailable() ? "Aucune connexion réseau · reconnexion automatique…" : !metadataOnline ? "Radio inaccessible · reconnexion automatique…" : "Flux interrompu · reconnexion automatique…"); RetryNow.Visibility = Visibility.Visible; Play.Content = "■  Arrêter";
        LocalLog.Write("Reconnexion audio, tentative " + reconnectAttempts);
    }
    async Task SubmitRequest(string url)
    {
        if (sending) return;
        if (RequestCoolingDown) { ShowInlineNotice("Nouvelle demande possible dans " + Math.Ceiling((requestAllowedAt!.Value - DateTime.UtcNow).TotalSeconds) + " s."); return; }
        if (!Uri.TryCreate(new Uri(Origin), url, out var uri) || uri.Scheme != "https" || uri.Host != new Uri(Origin).Host || !uri.AbsolutePath.StartsWith("/api/station/1/request/")) return;
        if (ConfirmDuplicate(url)) return;
        sending = true; Send.IsEnabled = false; RequestFeedback.Text = "Envoi de la demande…";
        ShowInlineNotice(RequestFeedback.Text);
        try {
            using var response = await client.PostAsync(uri, null);
            await HandleRequestReply(response);
        } catch { if (!closed) { RequestFeedback.Text = "Confirmation non reçue. La demande a peut-être été acceptée : vérifiez avant de réessayer."; ShowInlineNotice(RequestFeedback.Text); } }
        finally { sending = false; if (!closed) Send.IsEnabled = !loading && !RequestCoolingDown; }
    }
    async Task HandleRequestReply(System.Net.Http.HttpResponseMessage response)
    {
            bool accepted = false, recognized = false; string message = "";
            try { using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync()); message = S(doc.RootElement, "message"); if (doc.RootElement.TryGetProperty("success", out var ok)) { recognized = true; accepted = response.IsSuccessStatusCode && ok.GetBoolean(); } } catch { message = "Réponse du serveur non reconnue."; }
            requestAllowedAt = response.Headers.RetryAfter == null ? (DateTime?)null : response.Headers.RetryAfter.Delta.HasValue ? DateTime.UtcNow.Add(response.Headers.RetryAfter.Delta.Value) : response.Headers.RetryAfter.Date?.UtcDateTime;
            if (!closed) RequestFeedback.Text = (accepted ? "✓ Demande acceptée. " : response.IsSuccessStatusCode && !recognized ? "Confirmation non reconnue. " : "Demande refusée. ") + message + (requestAllowedAt.HasValue ? " Prochaine demande : " + requestAllowedAt.Value.ToLocalTime().ToString("HH:mm:ss") : "");
            if (!closed) { ShowInlineNotice(RequestFeedback.Text); TickBetaFeatures(); }
    }
    async Task TestFeatures()
    {
        var results = new List<string>();
        try {
            await Refresh(); ApplyDesign(5); await Task.Delay(2000);
            results.Add($"Automatic colour={autoColor}; artwork={Art.Source != null}");
            AddFavorite(); AddFavorite(); results.Add("Favorite count=" + preferences.Favorites.Count);
            Volume.Value = .35; SavePreferences(); var loaded = new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(preferencesPath)); results.Add($"Saved volume={loaded.Volume}; design={loaded.Design}; favorites={loaded.Favorites.Count}");
            ChangeMode(1); SetHoverControls(true); UpdateLayout(); results.Add("Reduced hover controls=" + AudioControls.Visibility); SetHoverControls(false); UpdateLayout(); results.Add("Reduced idle controls=" + AudioControls.Visibility);
            TogglePlay(this, new RoutedEventArgs()); await Task.Delay(10000); results.Add($"Playback state={audio?.State}; time={audio?.Time}");
            audio?.Stop(); CheckAudio(); results.Add("Interrupted stream detected=" + retryAt.HasValue); retryAt = DateTime.UtcNow; CheckAudio(); await Task.Delay(10000); results.Add($"Reconnected state={audio?.State}; time={audio?.Time}");
            SetSleep(15); sleepUntil = DateTime.UtcNow.AddSeconds(-1); TickSleep(); results.Add($"Sleep stopped={!playing}; retry cancelled={!retryAt.HasValue}");
            ChangeMode(0); ShowFavorites(); results.Add("Favorites window=" + (favoritesWindow != null)); favoritesWindow?.Close();
            requestAllowedAt = DateTime.UtcNow.AddSeconds(20); await SubmitRequest("/api/station/1/request/test"); results.Add("Cooldown=" + RequestFeedback.Text);
            SetupTray(); results.Add("Tray menu=" + tray?.ContextMenuStrip.Items.Count + "; visible=" + tray?.Visible); tray?.Dispose(); tray = null;
            ShowTrackNotification(); results.Add("Notification with cover=" + (toast != null && toast.IsVisible)); toast?.Close();
            ExecuteShortcut(Key.M); ExecuteShortcut(Key.Up); results.Add($"Shortcut mode={displayMode}; volume={Volume.Value}");
            using(var reply = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent("{\"success\":true,\"message\":\"Demande enregistrée\"}") }) { await HandleRequestReply(reply); results.Add("Mock accepted=" + RequestFeedback.Text); }
            using(var reply = new System.Net.Http.HttpResponseMessage((System.Net.HttpStatusCode)429) { Content = new System.Net.Http.StringContent("{\"success\":false,\"message\":\"Veuillez patienter\"}") }) { reply.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60)); await HandleRequestReply(reply); results.Add("Mock refused=" + RequestFeedback.Text); }
            results.Add("No real song requests were submitted. Server responses above are simulated.");
        } catch(Exception ex) { results.Add("ERROR: " + ex); }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "features-test.txt"), results); Close();
    }
}
