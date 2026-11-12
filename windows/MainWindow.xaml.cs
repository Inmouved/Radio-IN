using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Media;

namespace RadioIN;
public partial class MainWindow : Window
{
    const string Origin = "https://radio-in.duckdns.org";
    readonly HttpClient client = new() { BaseAddress = new Uri(Origin), Timeout = TimeSpan.FromSeconds(15) };
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(15) };
    readonly DispatcherTimer audioTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    NativeAudio? audio;
    DateTime connecting;
    List<Track> history = new();
    bool playing, refreshing, requesting, loading, closed, sending;
    int page = 1, pages = 1, generation;
    string query = "", artUrl = "";
    int displayMode, design;
    string[] designs = { "Actuel", "Sombre", "Flamme", "Vert", "Bleu", "Auto" };
    string[] modes = { "Compact", "Réduit", "Large", "Pochette" };
    public MainWindow()
    {
        InitializeComponent();
        InitializeChrome();
        LiveHistory.ItemTemplate = Items.ItemTemplate;
        DesignButton.Click += (_, _) => OpenChoices(DesignButton, designs, design, ApplyDesign);
        ModeButton.Click += (_, _) => OpenChoices(ModeButton, modes, displayMode, i => ChangeMode(i));
        Search.TextChanged += (_, _) => { searchTimer.Stop(); searchTimer.Start(); };
        searchTimer.Tick += async (_, _) => { searchTimer.Stop(); if (Drawer.Visibility == Visibility.Visible && requesting) { query = Search.Text.Trim(); page = 1; await LoadRequests(); } };
        try { audio = new NativeAudio(); audio.SetVolume(Volume.Value); }
        catch (Exception ex) { LocalLog.Write("Initialisation audio", ex); Status.Text = "Moteur audio : " + ex.Message; Play.IsEnabled = false; }
        Loaded += async (_, _) => { await Refresh(); timer.Start(); };
        timer.Tick += async (_, _) => { await Refresh(); if (displayMode == 2 && !loading && !sending) await LoadRequests(true); };
        audioTimer.Tick += (_, _) => CheckAudio();
        audioTimer.Start();
        Closed += (_, _) => { closed = true; timer.Stop(); audioTimer.Stop(); searchTimer.Stop(); audio?.Dispose(); client.Dispose(); };
        InitializeFeatures();
        InitializeVisuals();
        InitializePublicFeatures();
        InitializeUpdater();
        InitializeBetaFeatures();
        InitializeRelease13();
        if (Environment.GetCommandLineArgs().Contains("--layout-test")) Loaded += async (_, _) => {
            var results = new List<string>();
            try {
                await WaitForStartup();
                for (int theme = 0; theme < designs.Length; theme++) {
                    ApplyDesign(theme); UpdateLayout();
                    results.Add("Design " + designs[theme] + ": " + TintLayer.Background + "; cover=" + Art.Visibility);
                }
                for (int mode = 0; mode < modes.Length; mode++) {
                    ChangeMode(mode); await Task.Delay(1000); UpdateLayout();
                    results.Add($"Mode {modes[mode]}: {ActualWidth}x{ActualHeight}; navigation={Navigation.Visibility}; history={HistoryPane.Visibility}; requests={Drawer.Visibility}");
                    if (mode == 2) { while (loading) await Task.Delay(100); results.Add($"Live history={LiveHistory.Items.Count}; catalogue={Items.Items.Count}"); }
                }
                Search.Text = "Dredg"; await Task.Delay(1000); while (loading) await Task.Delay(100);
                results.Add($"Live search Dredg={Items.Items.Count}");
                ChangeMode(1); TogglePlay(this, new RoutedEventArgs()); await Task.Delay(10000);
                results.Add($"Reduced audio: state={audio?.State}, time={audio?.Time}ms");
                ChangeMode(0); UpdateLayout(); results.Add($"Back to compact: {ActualWidth}x{ActualHeight}; title={SongTitle.Text}");
                ApplyDesign(0);
            } catch(Exception ex) { results.Add("ERROR: " + ex); }
            System.IO.File.WriteAllLines(System.IO.Path.Combine(AppContext.BaseDirectory, "layouts-test.txt"), results);
            Close();
        };
        if (Environment.GetCommandLineArgs().Contains("--audio-test")) Loaded += async (_, _) => {
            TogglePlay(this, new RoutedEventArgs());
            await Task.Delay(15000);
            System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "audio-test.txt"), $"State={audio?.State}; TimeMs={audio?.Time}; Status={Status.Text}");
            Close();
        };
        if (Environment.GetCommandLineArgs().Contains("--self-test")) Loaded += async (_, _) => {
            var results = new List<string>();
            try {
                await WaitForStartup();
                TogglePlay(this, new RoutedEventArgs()); await Task.Delay(12000);
                results.Add($"Audio: state={audio?.State}, time={audio?.Time}ms");
                ShowHistory(this, new RoutedEventArgs());
                results.Add($"History: {Items.Items.Count} tracks; title={SongTitle.Text}; artwork={Art.Source != null}; background={Backdrop.ImageSource != null}");
                requesting = true; SearchPanel.Visibility = RequestActions.Visibility = Visibility.Visible;
                query = "Dredg"; page = 1; await LoadRequests();
                results.Add($"Search Dredg: {Items.Items.Count} results; page={PageLabel.Text}; first={(Items.Items.Count > 0 ? ((Track)Items.Items[0]).Detail : "NONE")}");
                if (Items.Items.Count > 0) { Items.SelectedIndex = 0; results.Add("Request selection: " + ((Track)Items.SelectedItem).Url); }
                Volume.Value = 0.25; results.Add("Volume changed to 25%");
                TogglePlay(this, new RoutedEventArgs()); results.Add($"Stopped: {!playing}, state={audio?.State}");
                TogglePlay(this, new RoutedEventArgs()); await Task.Delay(8000);
                results.Add($"Restart: state={audio?.State}, time={audio?.Time}ms");
                CloseDrawer(this, new RoutedEventArgs());
                UpdateLayout();
                results.Add($"Compact: {ActualWidth}x{ActualHeight}; drawer={Drawer.Visibility}");
            } catch (Exception ex) { results.Add("ERROR: " + ex); }
            System.IO.File.WriteAllLines(System.IO.Path.Combine(AppContext.BaseDirectory, "verification.txt"), results);
            Close();
        };
    }
    static string S(JsonElement e, string key) => e.TryGetProperty(key, out var v) ? v.ToString() : "";
    async Task Refresh()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/nowplaying/radio_in"));
            if (closed) return;
            var root = doc.RootElement;
            metadataOnline = root.TryGetProperty("is_online", out var online) && online.GetBoolean();
            if (!metadataOnline && !playing) UpdateConnection("Radio hors ligne · nouvel essai automatique");
            var song = root.GetProperty("now_playing").GetProperty("song");
            SongTitle.Text = S(song, "title"); Artist.Text = S(song, "artist");
            CoverTitle.Text = SongTitle.Text; CoverArtist.Text = Artist.Text;
            SongTitle.ToolTip = CoverTitle.ToolTip = SongTitle.Text + " — " + Artist.Text;
            currentSongId = S(song, "id");
            currentPlayedAt = root.GetProperty("now_playing").TryGetProperty("played_at", out var began) ? began.GetInt64() : 0;
            FavoriteButton.Content = preferences.Favorites.Any(f => f.Id == currentSongId) ? "♥" : "♡";
            ReducedSong.Text = SongTitle.Text;
            ReducedSong.ToolTip = Artist.Text + " — " + SongTitle.Text;
            var url = S(song, "art");
            if (url != artUrl) { artUrl = url; await LoadArtwork(url); }
            if (closed) return;
            NotifyTrack(currentSongId); CaptureSession();
            history = root.GetProperty("song_history").EnumerateArray().Select(e => {
                var s = e.GetProperty("song");
                var time = DateTimeOffset.FromUnixTimeSeconds(e.GetProperty("played_at").GetInt64()).ToLocalTime().ToString("HH:mm");
                return new Track(S(s, "title"), time + "  ·  " + S(s, "artist"), "") { Id = S(s, "id"), ArtistName = S(s, "artist") };
            }).ToList();
            ReplaceTracks(LiveHistory, history);
            RequestButton.IsEnabled = root.GetProperty("station").GetProperty("requests_enabled").GetBoolean();
            if (Drawer.Visibility == Visibility.Visible && !requesting) ReplaceTracks(Items, history);
        }
        catch (Exception ex) { metadataOnline = false; LocalLog.Write("Lecture des titres", ex); if (!closed) { Status.Text = ExplainNetwork(ex, "Titres"); if (!playing) UpdateConnection(Status.Text); } }
        finally { refreshing = false; }
    }
    void TogglePlay(object sender, RoutedEventArgs e)
    {
        if (audio == null) return;
        if (playing) { playing = false; retryAt = null; reconnectAttempts = 0; audio.Stop(); UpdateListeningAnimation(false); Play.Content = "▶  Écouter"; UpdateConnection("Lecture arrêtée"); }
        else try { playing = true; retryAt = null; audio.Play(Origin + "/listen/radio_in/radio.mp3"); ApplyEffectiveVolume(); connecting = progressAt = DateTime.UtcNow; lastAudioTime = 0; Play.Content = "■  Arrêter"; UpdateConnection("Connexion au direct…"); }
        catch (Exception ex) { LocalLog.Write("Lancement du flux audio", ex); ScheduleReconnect(); }
    }
    void CheckAudio()
    {
        TickBetaFeatures(); TickUpcoming(); UpdateConnectionBadge();
        UpdateListeningAnimation();
        if (TickSleep() || !playing || audio == null) return;
        if (retryAt.HasValue) {
            if (DateTime.UtcNow < retryAt.Value) return;
            retryAt = null;
            try { audio.Play(Origin + "/listen/radio_in/radio.mp3"); ApplyEffectiveVolume(); connecting = progressAt = DateTime.UtcNow; lastAudioTime = 0; } catch { ScheduleReconnect(); }
            return;
        }
        if (audio.State == 3) {
            connecting = DateTime.UtcNow;
            if (audio.Time > lastAudioTime) { lastAudioTime = audio.Time; progressAt = DateTime.UtcNow; reconnectAttempts = 0; }
            if (DateTime.UtcNow - progressAt > TimeSpan.FromSeconds(25)) { ScheduleReconnect(); return; }
            UpdateConnection("Vous écoutez Radio IN en direct" + (sleepUntil.HasValue ? " · arrêt dans " + Math.Ceiling((sleepUntil.Value - DateTime.UtcNow).TotalMinutes) + " min" : ""), Drawer.Visibility != Visibility.Visible);
        }
        else if (audio.State is 5 or 6 or 7 || DateTime.UtcNow - connecting > TimeSpan.FromSeconds(30)) ScheduleReconnect();
    }
    void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { ApplyEffectiveVolume(); UpdateListeningAnimation(); if (IsLoaded) SavePreferences(); }
    void PinChanged(object sender, RoutedEventArgs e) { Topmost = Pin.IsChecked == true; if (IsLoaded) SavePreferences(); }
    void ShowHistory(object sender, RoutedEventArgs e)
    {
        CloseExtraPanel();
        generation++; requesting = false; Drawer.Visibility = Visibility.Visible; PanelTitle.Text = "Derniers titres diffusés";
        SearchPanel.Visibility = RequestActions.Visibility = Visibility.Collapsed; Items.ItemsSource = history;
    }
    async void ShowRequests(object sender, RoutedEventArgs e)
    {
        CloseExtraPanel();
        requesting = true; Drawer.Visibility = Visibility.Visible; PanelTitle.Text = "Demander un titre";
        SearchPanel.Visibility = RequestActions.Visibility = Visibility.Visible; query = Search.Text.Trim(); await LoadRequests();
    }
    void CloseDrawer(object sender, RoutedEventArgs e) { generation++; Drawer.Visibility = Visibility.Collapsed; }
    void OpenChoices(Button button, string[] labels, int selected, Action<int> apply)
    {
        var menu = new ContextMenu();
        for (int i = 0; i < labels.Length; i++) {
            int choice = i; var item = new MenuItem { Header = labels[i], IsCheckable = true, IsChecked = i == selected };
            item.Click += (_, _) => apply(choice); menu.Items.Add(item);
        }
        button.ContextMenu = menu; menu.PlacementTarget = button; menu.IsOpen = true;
    }
    void ApplyDesign(int index)
    {
        design = index; DesignButton.Content = designs[index] + " ▾";
        string[] bases = { "#101927", "#08090D", "#471707", "#082D21", "#08274F" };
        string[] accents = { "#2F4B6E", "#424956", "#BE4517", "#167451", "#235FB4" };
        var color = index == 5 ? Color.FromRgb((byte)(autoColor.R * .35), (byte)(autoColor.G * .35), (byte)(autoColor.B * .35)) : (Color)ColorConverter.ConvertFromString(bases[index]);
        var start = Color.FromArgb(index == 0 ? (byte)175 : (byte)145, color.R, color.G, color.B);
        var end = Color.FromArgb(index == 0 ? (byte)236 : (byte)224, color.R, color.G, color.B);
        TransitionColors(start, end, index == 5 ? Color.FromRgb((byte)(autoColor.R * .6), (byte)(autoColor.G * .6), (byte)(autoColor.B * .6)) : (Color)ColorConverter.ConvertFromString(accents[index]));
        BackdropLayer.Opacity = index == 0 ? 0.8 : 1;
        BackdropLayer.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = index == 0 ? 22 : 9 };
        if (IsLoaded) SavePreferences();
    }
    async void ChangeMode(int mode)
    {
        CloseExtraPanel();
        displayMode = mode; generation++; loading = false;
        ModeButton.Content = modes[mode] + " ▾";
        Width = mode == 2 ? 860 : 440;
        ArtRow.Visibility = AudioControls.Visibility = Status.Visibility = mode == 1 || mode == 3 ? Visibility.Collapsed : Visibility.Visible;
        CoverStage.Visibility = mode == 3 ? Visibility.Visible : Visibility.Collapsed;
        SetCoverControls(mode == 3 && IsMouseOver);
        ReducedSong.Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
        Navigation.Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryPane.Visibility = mode == 2 ? Visibility.Visible : Visibility.Collapsed;
        ClosePanelButton.Visibility = mode == 2 ? Visibility.Collapsed : Visibility.Visible;
        Drawer.Visibility = mode == 2 ? Visibility.Visible : Visibility.Collapsed;
        Items.Height = mode == 2 ? 280 : 245;
        UpdateListeningAnimation();
        if (IsLoaded) SavePreferences();
        if (mode == 2) {
            requesting = true; PanelTitle.Text = "Demander un titre";
            SearchPanel.Visibility = RequestActions.Visibility = Visibility.Visible;
            query = Search.Text.Trim(); await LoadRequests();
        }
    }
    async Task LoadRequests(bool silent = false)
    {
        var selectedUrl = (Items.SelectedItem as Track)?.Url;
        var id = ++generation; loading = true; Send.IsEnabled = Previous.IsEnabled = Next.IsEnabled = false;
        if (!silent) PageLabel.Text = "…";
        try
        {
            if (Environment.GetCommandLineArgs().Contains("--catalogue-failure-test")) { await Task.Delay(100); throw new HttpRequestException("Simulated catalogue failure"); }
            using var doc = JsonDocument.Parse(await client.GetStringAsync($"/api/station/1/requests?rowCount=10&page={page}&searchPhrase={Uri.EscapeDataString(query)}"));
            if (closed || id != generation) return;
            var root = doc.RootElement; pages = Math.Max(1, root.GetProperty("total_pages").GetInt32());
            if (page > pages) { page = pages; loading = false; await LoadRequests(silent); return; }
            var tracks = root.GetProperty("rows").EnumerateArray().Select(e => new Track(S(e.GetProperty("song"), "title"), S(e.GetProperty("song"), "artist"), S(e, "request_url")) { Id = S(e.GetProperty("song"), "id"), ArtistName = S(e.GetProperty("song"), "artist") }).ToList();
            ReplaceTracks(Items, tracks);
            PageLabel.Text = $"{page}/{pages}"; if (!silent) Status.Text = root.GetProperty("total").GetInt32() == 0 ? "Aucun titre trouvé" : "Sélectionnez un titre, puis cliquez sur Demander.";
        }
        catch (Exception ex) { LocalLog.Write("Catalogue des demandes", ex); if (!closed && id == generation) { PageLabel.Text = "—"; Status.Text = ExplainNetwork(ex, "Catalogue"); } }
        finally { if (!closed && id == generation) { loading = false; Send.IsEnabled = !sending && !RequestCoolingDown; Previous.IsEnabled = page > 1; Next.IsEnabled = page < pages; } }
    }
    async void SearchClick(object sender, RoutedEventArgs e) { searchTimer.Stop(); query = Search.Text.Trim(); page = 1; await LoadRequests(); }
    void SearchKey(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) SearchClick(sender, e); }
    async void PrevPage(object sender, RoutedEventArgs e) { if (!loading && page > 1) { page--; await LoadRequests(); } }
    async void NextPage(object sender, RoutedEventArgs e) { if (!loading && page < pages) { page++; await LoadRequests(); } }
    async void SendRequest(object sender, RoutedEventArgs e)
    {
        if (sending) return;
        if (Items.SelectedItem is not Track track) { Status.Text = "Choisissez un titre dans la liste."; return; }
        await SubmitRequest(track.Url);
    }
    public sealed class Track
    {
        public string Id { get; set; } = "";
        public string ArtistName { get; set; } = "";
        public string Title { get; set; }
        public string Detail { get; set; }
        public string Url { get; set; }
        public Track(string title, string detail, string url) { Title = title; Detail = detail; Url = url; }
    }
}
