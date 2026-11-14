using System.IO;
using System.ComponentModel;
using System.Net.Http;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RadioIN;
public sealed class UpcomingTrack : INotifyPropertyChanged
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Art { get; set; } = "";
    public DateTime? ExpectedUtc { get; set; }
    public bool IsRequest { get; set; }
    public string RequestLabel => IsRequest ? "DEMANDE AUDITEUR" : "À VENIR";
    public string DateLabel => ExpectedUtc.HasValue ? ExpectedUtc.Value.ToLocalTime().ToString("dd/MM/yyyy · HH:mm:ss") + " (estimé)" : "Horaire à confirmer";
    public string Countdown => DescribeCountdown(ExpectedUtc, DateTime.UtcNow);
    ImageSource? artwork;
    public ImageSource? Artwork { get => artwork; set { artwork = value; PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Artwork))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Tick() => PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Countdown)));
    internal static string DescribeCountdown(DateTime? when, DateTime now)
    {
        if (!when.HasValue) return "Heure de passage non communiquée";
        var seconds = (int)Math.Min(int.MaxValue,Math.Ceiling((when.Value - now).TotalSeconds));
        if (seconds <= 0) return "En attente de diffusion · horaire susceptible de changer";
        return "Dans " + (seconds >= 3600 ? (seconds / 3600) + " h " : "") + (seconds / 60 % 60) + " min " + (seconds % 60).ToString("00") + " s";
    }
}
sealed class UpcomingSnapshot
{
    internal List<UpcomingTrack> Tracks = new();
    internal DateTime UpdatedUtc;
    internal bool Stale;
    public sealed class Feed { public int schema { get; set; } public string station { get; set; } = ""; public bool available { get; set; } public long updated_at { get; set; } public bool stale { get; set; } public Row[] items { get; set; } = Array.Empty<Row>(); }
    public sealed class Row { public string title { get; set; } = ""; public string artist { get; set; } = ""; public string art { get; set; } = ""; public long? played_at { get; set; } public bool is_request { get; set; } }
    internal static UpcomingSnapshot Parse(string json, DateTime now)
    {
        if (json.Length > 256 * 1024) throw new InvalidDataException("Réponse de file trop volumineuse.");
        var data = new JavaScriptSerializer { MaxJsonLength = 256 * 1024 }.Deserialize<Feed>(json);
        if (data == null || data.schema != 1 || data.station != "radio_in" || !data.available || data.items == null || data.items.Length > 25) throw new InvalidDataException("File d’attente indisponible ou format incompatible.");
        DateTime updated = DateTimeOffset.FromUnixTimeSeconds(data.updated_at).UtcDateTime;
        if (now - updated > TimeSpan.FromSeconds(100) || updated - now > TimeSpan.FromMinutes(5)) throw new InvalidDataException("La file d’attente n’est plus à jour.");
        var result = new UpcomingSnapshot { UpdatedUtc = updated, Stale = data.stale };
        foreach (var row in data.items) {
            if (row == null || row.title == null || row.artist == null || row.title.Length > 1024 || row.artist.Length > 1024) throw new InvalidDataException("Titre de file invalide.");
            string art = Uri.TryCreate(row.art,UriKind.Absolute,out var uri) && uri.Scheme == "https" && uri.Host == "radio-in.duckdns.org" && uri.Port == 443 && uri.UserInfo.Length == 0 ? uri.AbsoluteUri : "";
            result.Tracks.Add(new UpcomingTrack { Title = row.title, Artist = row.artist, Art = art, IsRequest = row.is_request,
                ExpectedUtc = row.played_at.HasValue && row.played_at.Value > 0 ? DateTimeOffset.FromUnixTimeSeconds(row.played_at.Value).UtcDateTime : (DateTime?)null });
        }
        return result;
    }
}

public partial class MainWindow
{
    const string QueueUrl = "https://radio-in.duckdns.org:9443/queue.json";
    readonly CancellationTokenSource featureLifetime = new();
    readonly System.Windows.Threading.DispatcherTimer queueTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    UpcomingSnapshot? upcoming;
    bool queueLoading;
    int queueGeneration;
    string extraView = "", duplicateUrl = "";
    DateTime duplicateUntil;
    string queueError = "Chargement de la file…";
    readonly List<Track> sessionHistory = new();
    readonly Dictionary<string,ImageSource> queueArtCache = new();
    readonly HashSet<string> sessionEvents = new();
    readonly Queue<string> recentSessionEvents = new();
    long currentPlayedAt;
    void InitializeUpcoming()
    {
        UpcomingButton.Click += (_, _) => ShowUpcoming();
        CloseExtra.Click += (_, _) => CloseExtraPanel();
        RefreshQueue.Click += async (_, _) => await RefreshUpcoming();
        queueTimer.Tick += async (_, _) => await RefreshUpcoming();
        ExtraList.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent,new RoutedEventHandler((_, e) => {
            if (e.OriginalSource is Button b && b.Tag is Track track) { AddTrackFavorite(track); e.Handled = true; }
        }));
        Loaded += async (_, _) => { await WaitForStartup(); if (!testMode) { await RefreshUpcoming(); queueTimer.Start(); } };
        Closing += (_, _) => { queueTimer.Stop(); featureLifetime.Cancel(); };
    }
    void OpenExtraPanel(string name)
    {
        extraView = name; Panels.Visibility = Visibility.Collapsed; ExtraPanel.Visibility = Visibility.Visible;
        ExtraSearch.Visibility = Visibility.Collapsed;
        ExtraList.FontSize = 13 * preferences.TextScale;
        if (displayMode == 3) CoverStage.Visibility = Visibility.Collapsed;
        Dispatcher.BeginInvoke(new Action(FitExtraPanel));
    }
    void CloseExtraPanel()
    {
        extraView = ""; ExtraPanel.Visibility = Visibility.Collapsed; Panels.Visibility = Visibility.Visible;
        if (displayMode == 3) CoverStage.Visibility = Visibility.Visible;
    }
    void ShowUpcoming()
    {
        OpenExtraPanel("queue"); ExtraTitle.Text = "À venir";
        ExtraList.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse(@"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><DockPanel Margin='6,8'><Image Source='{Binding Artwork}' Width='48' Height='48' Stretch='UniformToFill' Margin='0,2,12,0' DockPanel.Dock='Left'/><StackPanel><TextBlock Text='{Binding RequestLabel}' Foreground='#9FD9CC' FontSize='10'/><TextBlock Text='{Binding Title}' FontWeight='SemiBold' TextWrapping='Wrap'/><TextBlock Text='{Binding Artist}' Foreground='#CBD5E1' TextWrapping='Wrap'/><TextBlock Text='{Binding DateLabel}' Foreground='#CBD5E1' FontSize='11' TextWrapping='Wrap' Margin='0,5,0,0'/><TextBlock Text='{Binding Countdown}' Foreground='#F5E1AA' FontSize='11' TextWrapping='Wrap'/></StackPanel></DockPanel></DataTemplate>");
        ExtraList.ItemsSource = upcoming?.Tracks;
        RefreshQueue.Visibility = Visibility.Visible; UpdateQueueStatus();
        if (!testMode && !queueLoading && (upcoming == null || DateTime.UtcNow - upcoming.UpdatedUtc > TimeSpan.FromSeconds(15))) _ = RefreshUpcoming();
    }
    void UpdateQueueStatus()
    {
        if (extraView != "queue") return;
        ExtraStatus.Text = upcoming == null ? queueError : (upcoming.Stale ? "Actualisation interrompue · dernière liste connue. " : upcoming.Tracks.Count == 0 ? "Aucun titre annoncé pour le moment. " : "Actualisé à " + upcoming.UpdatedUtc.ToLocalTime().ToString("HH:mm:ss") + ". ") + "Horaires estimatifs. Les demandes passent généralement sous 10 à 15 min, sans garantie.";
    }
    async Task RefreshUpcoming()
    {
        if (queueLoading || closed) return;
        queueLoading = true; RefreshQueue.IsEnabled = false;
        int generation = ++queueGeneration;
        try {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(featureLifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await client.GetAsync(QueueUrl,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            response.EnsureSuccessStatusCode();
            using var input = await response.Content.ReadAsStreamAsync(); using var buffer = new MemoryStream();
            var bytes = new byte[8192]; int read;
            while ((read = await input.ReadAsync(bytes,0,bytes.Length,timeout.Token)) > 0) { if (buffer.Length + read > 256 * 1024) throw new InvalidDataException("File trop volumineuse."); buffer.Write(bytes,0,read); }
            var parsed = UpcomingSnapshot.Parse(System.Text.Encoding.UTF8.GetString(buffer.ToArray()),DateTime.UtcNow);
            if (closed || generation != queueGeneration) return;
            upcoming = parsed; foreach (var row in parsed.Tracks) row.Artwork = queueArtCache.TryGetValue(row.Art,out var cached) ? cached : Icon;
            if (extraView == "queue") ExtraList.ItemsSource = parsed.Tracks;
            UpdateQueueStatus(); UpdateRequestTracking();
            _ = LoadQueueArtwork(parsed,generation);
        } catch(Exception ex) {
            if (closed) return;
            queueError = "File indisponible. Réessayez dans quelques instants.";
            if (upcoming != null && DateTime.UtcNow - upcoming.UpdatedUtc <= TimeSpan.FromSeconds(90)) upcoming.Stale = true;
            else { upcoming = null; if (extraView == "queue") ExtraList.ItemsSource = null; }
            LocalLog.Write("File des prochains titres",ex); UpdateQueueStatus(); UpdateRequestTracking();
        } finally { queueLoading = false; if (!closed) RefreshQueue.IsEnabled = true; }
    }
    async Task LoadQueueArtwork(UpcomingSnapshot snapshot, int generation)
    {
        foreach (var row in snapshot.Tracks) {
            if (closed || generation != queueGeneration || featureLifetime.IsCancellationRequested) return;
            if (row.Art.Length == 0) continue;
            if (queueArtCache.TryGetValue(row.Art,out var cached)) { row.Artwork = cached; continue; }
            try {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(featureLifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
                using var response = await client.GetAsync(row.Art,HttpCompletionOption.ResponseHeadersRead,timeout.Token); response.EnsureSuccessStatusCode();
                using var stream = await response.Content.ReadAsStreamAsync(); using var output = new MemoryStream();
                byte[] buffer = new byte[8192]; int n;
                while ((n = await stream.ReadAsync(buffer,0,buffer.Length,timeout.Token)) > 0) { if (output.Length + n > 2 * 1024 * 1024) throw new InvalidDataException("Pochette trop volumineuse."); output.Write(buffer,0,n); }
                if (closed || generation != queueGeneration) return;
                output.Position = 0; var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 96; image.StreamSource = output; image.EndInit(); image.Freeze();
                if (queueArtCache.Count >= 32) { foreach (var key in queueArtCache.Keys.Where(k => !snapshot.Tracks.Any(t => t.Art == k)).ToList()) queueArtCache.Remove(key); }
                queueArtCache[row.Art] = image; row.Artwork = image;
            } catch { /* Keep the radio logo on artwork failure. */ }
        }
    }
    UpcomingTrack? FindQueued(string title, string artist) => upcoming == null || upcoming.Stale || DateTime.UtcNow - upcoming.UpdatedUtc > TimeSpan.FromSeconds(90) ? null : upcoming.Tracks.FirstOrDefault(t => NormalizeSearch(t.Title).Trim() == NormalizeSearch(title).Trim() && NormalizeSearch(t.Artist).Trim() == NormalizeSearch(artist).Trim());
    bool ConfirmDuplicate(string url, Track? track = null)
    {
        if (upcoming == null) return false;
        track ??= Items.SelectedItem is Track selected && selected.Url == url ? selected : null;
        var found = track == null ? null : FindQueued(track.Title, track.ArtistName);
        if (found == null) return false;
        if (duplicateUrl == url && duplicateUntil > DateTime.UtcNow) { duplicateUrl = ""; return false; }
        duplicateUrl = url; duplicateUntil = DateTime.UtcNow.AddSeconds(20);
        ShowInlineNotice("« " + found.Title + " » est déjà annoncé dans la file. " + found.DateLabel + ". Pour envoyer malgré tout une nouvelle demande, cliquez de nouveau dans les 20 secondes."); return true;
    }
    void CaptureSession()
    {
        if (!playing || audio?.State != 3 || currentSongId.Length == 0) return;
        RecordSessionTrack(currentSongId,SongTitle.Text,Artist.Text,currentPlayedAt);
    }
    void RecordSessionTrack(string id,string title,string artist,long playedAt)
    {
        string key = id + "|" + playedAt;
        if (sessionEvents.Count == 0) recentSessionEvents.Clear();
        if (!sessionEvents.Add(key)) return;
        recentSessionEvents.Enqueue(key);
        if (recentSessionEvents.Count > 500) sessionEvents.Remove(recentSessionEvents.Dequeue());
        SaveListenedTrack(id, title, artist, playedAt);
        sessionHistory.Insert(0,new Track(title,DateTime.Now.ToString("HH:mm:ss") + " · " + artist,"") { Id = id, ArtistName = artist });
        if (sessionHistory.Count > 500) sessionHistory.RemoveAt(sessionHistory.Count - 1);
        if (extraView == "session") { ReplaceTracks(ExtraList,sessionHistory.ToList()); ExtraStatus.Text = sessionHistory.Count + " titres observés pendant l’écoute · cette session uniquement."; }
    }
    void ShowSessionHistory()
    {
        OpenExtraPanel("session"); ExtraTitle.Text = "Ma session d’écoute"; RefreshQueue.Visibility = Visibility.Collapsed;
        ExtraList.ItemTemplate = Items.ItemTemplate; ExtraList.ItemsSource = sessionHistory.ToList(); ExtraStatus.Text = sessionHistory.Count == 0 ? "Les titres apparaîtront pendant votre écoute." : sessionHistory.Count + " titres observés pendant l’écoute · cette session uniquement.";
    }
    void TickUpcoming()
    {
        CaptureSession();
        if (upcoming != null && DateTime.UtcNow - upcoming.UpdatedUtc > TimeSpan.FromSeconds(100)) { upcoming = null; queueError = "File indisponible : la dernière liste est trop ancienne."; if (extraView == "queue") ExtraList.ItemsSource = null; UpdateQueueStatus(); UpdateRequestTracking(); }
        if (extraView == "queue" && upcoming != null) foreach (var row in upcoming.Tracks) row.Tick();
    }
}
