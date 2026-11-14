using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace RadioIN;
public partial class MainWindow
{
    public sealed class ListeningEntry
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public DateTime PlayedUtc { get; set; }
    }
    public sealed class TrackedRequest
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public DateTime AcceptedUtc { get; set; }
        public DateTime? EstimatedUtc { get; set; }
        public DateTime? PlayedUtc { get; set; }
        public bool Alerted { get; set; }
    }
    public sealed class ListeningData
    {
        public List<ListeningEntry> History { get; set; } = new();
        public List<TrackedRequest> Requests { get; set; } = new();
    }
    ListeningData listeningData = new();
    readonly JavaScriptSerializer listeningSerializer = new() { MaxJsonLength = 8 * 1024 * 1024 };
    string listeningPath => Path.Combine(dataDirectory, "listening.json");
    TextBox? historyFilter;
    bool nativePositionReady, positioningMode, placementQueued;

    void InitializeRelease14()
    {
        CoverFavorite.Click += (_, _) => AddFavorite();
        LargeUpcomingButton.Click += (_, _) => ShowUpcoming();
        historyFilter = ExtraSearch;
        historyFilter.TextChanged += (_, _) => { if (extraView == "archive") RenderListeningArchive(); };
        if (!testMode) {
            foreach (string path in new[] { listeningPath, listeningPath + ".bak" }) {
                if (!File.Exists(path)) continue;
                try {
                    if (new FileInfo(path).Length > listeningSerializer.MaxJsonLength) continue;
                    var data = listeningSerializer.Deserialize<ListeningData>(File.ReadAllText(path));
                    if (data?.History == null || data.Requests == null) continue;
                    listeningData = data;
                    listeningData.History = data.History.Where(x => x != null).OrderByDescending(x => x.PlayedUtc).Take(1000).ToList();
                    listeningData.Requests = data.Requests.Where(x => x != null).OrderByDescending(x => x.AcceptedUtc).Take(200).ToList();
                    break;
                } catch (Exception ex) { LocalLog.Write("Lecture de l’historique local", ex); }
            }
        }
        SizeChanged += (_, _) => QueueConstrainPlayer();
        DpiChanged += (_, _) => QueueConstrainPlayer();
        if (Environment.GetCommandLineArgs().Contains("--beta14-test"))
            Loaded += async (_, _) => { await WaitForStartup(); await TestRelease14(); };
    }
    void SaveListeningData()
    {
        try {
            Directory.CreateDirectory(dataDirectory);
            string temporary = listeningPath + ".tmp";
            File.WriteAllText(temporary, listeningSerializer.Serialize(listeningData));
            if (File.Exists(listeningPath)) File.Replace(temporary, listeningPath, listeningPath + ".bak");
            else File.Move(temporary, listeningPath);
        } catch (Exception ex) { LocalLog.Write("Sauvegarde de l’historique local", ex); }
    }
    void SaveListenedTrack(string id, string title, string artist, long playedAt)
    {
        if (playedAt <= 0 || playedAt > 253402300799) return;
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(playedAt).UtcDateTime;
        if (listeningData.History.Any(x => x.Id == id && x.PlayedUtc == timestamp)) return;
        listeningData.History.Insert(0, new ListeningEntry { Id = id, Title = title, Artist = artist, PlayedUtc = timestamp });
        if (listeningData.History.Count > 1000) listeningData.History.RemoveAt(1000);
        SaveListeningData();
        if (extraView == "archive") RenderListeningArchive();
    }
    void ShowListeningArchive()
    {
        OpenExtraPanel("archive");
        ExtraTitle.Text = "Historique d’écoute";
        ExtraSearch.Visibility = Visibility.Visible;
        RefreshQueue.Visibility = Visibility.Collapsed;
        ExtraList.ItemTemplate = Items.ItemTemplate;
        RenderListeningArchive();
    }
    static bool MatchesArchive(ListeningEntry entry, string query)
    {
        var local = entry.PlayedUtc.ToLocalTime();
        string searchable = NormalizeSearch(entry.Title + " " + entry.Artist + " " +
            local.ToString("dd/MM/yyyy yyyy-MM-dd d MMMM yyyy", CultureInfo.GetCultureInfo("fr-FR")));
        return NormalizeSearch(query).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).All(searchable.Contains);
    }
    void RenderListeningArchive()
    {
        var rows = listeningData.History.Where(x => MatchesArchive(x, historyFilter?.Text ?? "")).Select(x =>
            new Track(x.Title, x.PlayedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm") + " · " + x.Artist, "")
            { Id = x.Id, ArtistName = x.Artist }).ToList();
        ReplaceTracks(ExtraList, rows);
        ExtraStatus.Text = rows.Count == 0 ? "Aucun titre trouvé dans l’historique d’écoute." : rows.Count + " titres · jusqu’à 1 000 écoutes conservées sur ce PC.";
    }
    void RecordAcceptedRequest(Track track)
    {
        listeningData.Requests.Insert(0, new TrackedRequest {
            Id = track.Id, Title = track.Title, Artist = track.ArtistName, AcceptedUtc = DateTime.UtcNow
        });
        if (listeningData.Requests.Count > 200) listeningData.Requests.RemoveAt(200);
        SaveListeningData();
        if (extraView == "requests") RenderMyRequests();
    }
    static bool SameTrack(string title, string artist, TrackedRequest request) =>
        NormalizeSearch(title).Trim() == NormalizeSearch(request.Title).Trim() &&
        NormalizeSearch(artist).Trim() == NormalizeSearch(request.Artist).Trim();
    void UpdateRequestTracking()
    {
        var now = DateTime.UtcNow;
        bool changed = false;
        var active = listeningData.Requests.Where(x => !x.PlayedUtc.HasValue &&
            x.AcceptedUtc <= now && now - x.AcceptedUtc < TimeSpan.FromDays(1)).ToList();
        foreach (var played in history.Concat(currentPlayedAt > 0 ? new[] {
            new Track(SongTitle.Text, "", "") { Id = currentSongId, ArtistName = Artist.Text, PlayedAt = currentPlayedAt }
        } : Array.Empty<Track>()).Where(x => x.PlayedAt > 0 && x.PlayedAt <= 253402300799).OrderBy(x => x.PlayedAt)) {
            var when = DateTimeOffset.FromUnixTimeSeconds(played.PlayedAt).UtcDateTime;
            if (when > now || listeningData.Requests.Any(x => x.PlayedUtc == when && SameTrack(played.Title, played.ArtistName, x))) continue;
            var request = active.LastOrDefault(x => !x.PlayedUtc.HasValue && when >= x.AcceptedUtc &&
                (x.Id.Length > 0 && played.Id.Length > 0 ? x.Id == played.Id : SameTrack(played.Title, played.ArtistName, x)));
            if (request == null) continue;
            request.PlayedUtc = when;
            request.EstimatedUtc = null;
            changed = true;
        }
        foreach (var request in active.Where(x => !x.PlayedUtc.HasValue)) {
            var matches = upcoming != null && !upcoming.Stale && now - upcoming.UpdatedUtc <= TimeSpan.FromSeconds(90)
                ? upcoming.Tracks.Where(x => x.IsRequest && x.ExpectedUtc > now && SameTrack(x.Title, x.Artist, request)).ToList()
                : new List<UpcomingTrack>();
            // The public queue has no request ID. Omit ambiguous matches.
            DateTime? estimate = matches.Count == 1 && active.Count(x => !x.PlayedUtc.HasValue && SameTrack(request.Title, request.Artist, x)) == 1
                ? matches[0].ExpectedUtc : null;
            if (request.EstimatedUtc != estimate) { request.EstimatedUtc = estimate; changed = true; }
            if (!estimate.HasValue || request.Alerted || !preferences.RequestAlerts) continue;
            request.Alerted = true;
            changed = true;
            string message = request.Artist + " — " + request.Title + " · vers " + estimate.Value.ToLocalTime().ToString("HH:mm");
            if (IsVisible) ShowInlineNotice(message);
            else if (!testMode) tray?.ShowBalloonTip(7000, "Radio IN — Demande annoncée", message, System.Windows.Forms.ToolTipIcon.Info);
        }
        if (changed) SaveListeningData();
        if (extraView == "requests") RenderMyRequests();
    }
    void ShowMyRequests()
    {
        OpenExtraPanel("requests");
        ExtraTitle.Text = "Mes demandes";
        RefreshQueue.Visibility = Visibility.Collapsed;
        ExtraList.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse(
            @"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><TextBlock Text='{Binding Detail}' TextWrapping='Wrap' Margin='6'/></DataTemplate>");
        RenderMyRequests();
    }
    void RenderMyRequests()
    {
        ReplaceTracks(ExtraList, listeningData.Requests.Select(x => new Track(x.Title,
            x.Title + " — " + x.Artist + "\n" + (x.PlayedUtc.HasValue
                ? "Titre diffusé à " + x.PlayedUtc.Value.ToLocalTime().ToString("dd/MM HH:mm")
                : x.EstimatedUtc > DateTime.UtcNow && upcoming != null && !upcoming.Stale
                    ? "Annoncé vers " + x.EstimatedUtc.Value.ToLocalTime().ToString("dd/MM HH:mm") + " (estimé)"
                    : DateTime.UtcNow - x.AcceptedUtc >= TimeSpan.FromDays(1) ? "Diffusion non confirmée" : "Acceptée · en attente")
            + " · demandée le " + x.AcceptedUtc.ToLocalTime().ToString("dd/MM HH:mm"), "")).ToList());
        ExtraStatus.Text = listeningData.Requests.Count == 0 ? "Les demandes acceptées apparaîtront ici." :
            "Les passages sont repérés par titre à partir de la file publique et de l’historique de la radio.";
    }
    void SyncFavoriteButtons()
    {
        FavoriteButton.Content = CoverFavorite.Content = preferences.Favorites.Any(x => x.Id == currentSongId) ? "♥" : "♡";
    }

    public sealed class ModePosition
    {
        public int X { get; set; }
        public int Y { get; set; }
    }
    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    void RememberModePosition()
    {
        if (!nativePositionReady || positioningMode || restoring || WindowState != WindowState.Normal || !IsVisible) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero && GetWindowRect(handle, out var rect))
            preferences.ModePositions[displayMode.ToString(CultureInfo.InvariantCulture)] = new ModePosition { X = rect.Left, Y = rect.Top };
    }
    void ConstrainPlayer() => PlacePlayer(null);
    void QueueConstrainPlayer()
    {
        if (!nativePositionReady || placementQueued || positioningMode) return;
        placementQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => {
            placementQueued = false;
            ConstrainPlayer();
        }));
    }
    void PlacePlayer(ModePosition? position)
    {
        if (!nativePositionReady || positioningMode || closed || WindowState != WindowState.Normal) return;
        var source = PresentationSource.FromVisual(this);
        var handle = new WindowInteropHelper(this).Handle;
        if (source?.CompositionTarget == null || !GetWindowRect(handle, out var rect)) return;
        int x = position?.X ?? rect.Left, y = position?.Y ?? rect.Top;
        var screens = System.Windows.Forms.Screen.AllScreens;
        var screen = screens.FirstOrDefault(s => s.Bounds.Contains(x, y)) ??
            (position != null ? System.Windows.Forms.Screen.PrimaryScreen : System.Windows.Forms.Screen.FromHandle(handle));
        if (screen == null) return;
        var area = screen.WorkingArea;
        positioningMode = true;
        try {
            if (position != null && !screen.Bounds.Contains(x, y)) { x = area.Left; y = area.Top; }
            // Move first so WPF updates its DPI transform on the target monitor.
            if (position != null && (x != rect.Left || y != rect.Top))
                SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x15);
            var scale = source.CompositionTarget.TransformFromDevice;
            MaxHeight = Math.Max(1, area.Height * scale.M22);
            Width = Math.Min(displayMode == 2 ? 860 : 440, area.Width * scale.M11);
            CoverStage.Height = Math.Max(1, Math.Min(402, Width - 38));
            if (position != null) UpdateLayout();
            if (!GetWindowRect(handle, out rect)) return;
            var clamped = ClampPlacement(x, y, rect.Right - rect.Left, rect.Bottom - rect.Top, area);
            if (clamped.X != rect.Left || clamped.Y != rect.Top)
                SetWindowPos(handle, IntPtr.Zero, clamped.X, clamped.Y, 0, 0, 0x15);
        } finally { positioningMode = false; }
    }
    static System.Drawing.Point ClampPlacement(int x, int y, int width, int height, System.Drawing.Rectangle area) =>
        new(Math.Max(area.Left, Math.Min(x, area.Right - width)), Math.Max(area.Top, Math.Min(y, area.Bottom - height)));
}
