using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RadioIN;
public partial class MainWindow
{
    TextBox? favoriteSearch;
    ComboBox? favoriteSort;
    double sleepGain = 1;
    bool RequestCoolingDown => requestAllowedAt.HasValue && requestAllowedAt.Value > DateTime.UtcNow;
    void InitializeBetaFeatures()
    {
        VersionLabel.Text = AppInfo.Version;
        if (Art.Source == null) TransitionArtwork(Icon);
        DismissNotice.Click += (_, _) => InlineNotice.Visibility = Visibility.Collapsed;
        RetryNow.Click += (_, _) => RetryImmediately();
        foreach (var list in new[] { Items, LiveHistory }) {
            list.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, e) => {
                if (e.OriginalSource is Button button && button.Tag is Track track) { AddTrackFavorite(track); e.Handled = true; }
            }));
            var menu = new ContextMenu(); var add = new MenuItem { Header = "♡ Ajouter la sélection aux favoris" };
            add.Click += (_, _) => { if (list.SelectedItem is Track t) AddTrackFavorite(t); };
            menu.Items.Add(add); list.ContextMenu = menu;
            list.PreviewMouseRightButtonDown += (_, e) => {
                DependencyObject? node = e.OriginalSource as DependencyObject;
                while (node != null && node is not ListBoxItem) node = node is Visual ? VisualTreeHelper.GetParent(node) : null;
                if (node is ListBoxItem item) item.IsSelected = true;
            };
        }
        SourceInitialized += (_, _) => { HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(MediaMessage); if (!testMode && preferences.MediaKeys) SetGlobalMediaKeys(true); };
        Closed += (_, _) => SetGlobalMediaKeys(false);
        if (Environment.GetCommandLineArgs().Contains("--beta12-test")) Loaded += async (_, _) => { await WaitForStartup(); await TestBetaFeatures(); };
    }
    void ShowInlineNotice(string text)
    {
        NoticeText.Text = text.Length > 2000 ? text.Substring(0,2000) + "…" : text;
        InlineNotice.Background = Play.Background;
        InlineNotice.Visibility = Visibility.Visible;
        if (favoritesWindow?.IsActive == true) { favoritesWindow.Hide(); RestoreWindow(); }
    }
    void AddTrackFavorite(Track track)
    {
        if (string.IsNullOrWhiteSpace(track.Id)) { ShowInlineNotice("Ce titre ne possède pas d’identifiant de favoris."); return; }
        bool existing = preferences.Favorites.Any(f => f.Id == track.Id);
        if (!existing) preferences.Favorites.Add(new Favorite { Id = track.Id, Title = track.Title, Artist = track.ArtistName });
        SavePreferences(); UpdateFavoritesList();
        SyncFavoriteButtons();
        ShowInlineNotice(existing ? "Ce titre est déjà dans vos favoris." : "♥ « " + track.Title + " » ajouté aux favoris.");
    }
    static string NormalizeSearch(string value)
    {
        var result = new StringBuilder();
        foreach (char c in (value ?? "").Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) result.Append(char.ToLowerInvariant(c));
        return result.ToString().Normalize(NormalizationForm.FormC);
    }
    static List<Favorite> FilterFavorites(IEnumerable<Favorite> source, string query, int sort)
    {
        string term = NormalizeSearch(query);
        var rows = source.Where(f => NormalizeSearch(f.Title + " " + f.Artist).Contains(term));
        return sort switch {
            1 => rows.OrderBy(f => f.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(f => f.Artist).ToList(),
            2 => rows.OrderBy(f => f.Artist, StringComparer.CurrentCultureIgnoreCase).ThenBy(f => f.Title).ToList(),
            3 => rows.ToList(),
            _ => rows.Reverse().ToList()
        };
    }
    void AddFavoriteFilters(DockPanel panel)
    {
        var filters = new DockPanel { Margin = new Thickness(12,0,12,0) };
        favoriteSort = new ComboBox { Width = 145, Margin = new Thickness(8,0,0,0), ToolTip = "Trier les favoris", ItemsSource = new[] { "Ajouts récents", "Titre A–Z", "Artiste A–Z", "Ajouts anciens" }, SelectedIndex = 0 };
        favoriteSearch = new TextBox { Padding = new Thickness(6), ToolTip = "Rechercher un titre ou un artiste" };
        DockPanel.SetDock(favoriteSort, Dock.Right); filters.Children.Add(favoriteSort); filters.Children.Add(favoriteSearch);
        DockPanel.SetDock(filters, Dock.Top); panel.Children.Insert(1, filters);
        favoriteSearch.TextChanged += (_, _) => UpdateFavoritesList(); favoriteSort.SelectionChanged += (_, _) => UpdateFavoritesList();
    }
    static ScrollViewer? FindScroll(DependencyObject parent)
    {
        if (parent is ScrollViewer scroll) return scroll;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var found = FindScroll(VisualTreeHelper.GetChild(parent, i)); if (found != null) return found; }
        return null;
    }
    static void ReplaceTracks(ListBox list, List<Track> rows)
    {
        var selected = list.SelectedItem as Track;
        var scroll = FindScroll(list); double offset = scroll?.VerticalOffset ?? 0;
        list.ItemsSource = rows;
        if (selected != null) list.SelectedItem = rows.FirstOrDefault(t => selected.Id.Length > 0 ? t.Id == selected.Id : t.Url == selected.Url && t.Title == selected.Title);
        list.UpdateLayout(); FindScroll(list)?.ScrollToVerticalOffset(offset);
    }
    void RetryImmediately()
    {
        if (audio == null) { ShowInlineNotice("Moteur audio indisponible. Relancez Radio IN."); return; }
        if (!playing) TogglePlay(this, new RoutedEventArgs());
        else { retryAt = DateTime.UtcNow; CheckAudio(); UpdateConnection("Nouvelle connexion au direct…"); }
    }
    void ApplyEffectiveVolume() => audio?.SetVolume(muted ? 0 : Volume.Value * sleepGain);
    void TickBetaFeatures()
    {
        RetryNow.Visibility = retryAt.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (retryAt.HasValue) UpdateConnection("Reconnexion dans " + Math.Max(0, Math.Ceiling((retryAt.Value - DateTime.UtcNow).TotalSeconds)) + " s · tentative " + reconnectAttempts);
        if (!loading && !sending) Send.IsEnabled = !RequestCoolingDown;
        Send.Content = RequestCoolingDown ? "Patienter " + Math.Ceiling((requestAllowedAt!.Value - DateTime.UtcNow).TotalSeconds) + " s" : "Demander la sélection";
        sleepGain = sleepUntil.HasValue && preferences.SleepFadeSeconds > 0 ? Math.Max(0, Math.Min(1, (sleepUntil.Value - DateTime.UtcNow).TotalSeconds / preferences.SleepFadeSeconds)) : 1;
        ApplyEffectiveVolume();
    }
    void ShowCustomSleep()
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "Arrêter dans combien de minutes ? (1 à 1 440)", Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
        var minutes = new TextBox { Text = "45", Margin = new Thickness(0,8,0,12), Padding = new Thickness(6) }; body.Children.Add(minutes);
        body.Children.Add(new TextBlock { Text = "Baisse progressive du volume avant l’arrêt", Foreground = Brushes.White });
        var fade = new ComboBox { ItemsSource = new[] { "Désactivée", "15 secondes", "30 secondes", "60 secondes" }, SelectedIndex = preferences.SleepFadeSeconds == 0 ? 0 : preferences.SleepFadeSeconds == 15 ? 1 : preferences.SleepFadeSeconds == 60 ? 3 : 2, Margin = new Thickness(0,8,0,12) }; body.Children.Add(fade);
        var error = new TextBlock { Foreground = Brushes.LightSalmon, TextWrapping = TextWrapping.Wrap }; body.Children.Add(error);
        var apply = new Button { Content = "Activer la minuterie", Margin = new Thickness(0,8,0,0) }; body.Children.Add(apply);
        var dialog = CreateInfoWindow("RADIO IN / MINUTERIE", body, 420);
        apply.Click += (_, _) => {
            if (!int.TryParse(minutes.Text, out var n) || n < 1 || n > 1440) { error.Text = "Saisissez un nombre entier de 1 à 1 440."; return; }
            preferences.SleepFadeSeconds = new[] { 0,15,30,60 }[fade.SelectedIndex]; SavePreferences(); SetSleep(n); dialog.Close();
        }; dialog.Show();
    }
    void ConfirmResetSettings()
    {
        var body = new StackPanel(); body.Children.Add(new TextBlock { Text = "Remettre les réglages par défaut ? Vos favoris seront conservés.", Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
        var reset = new Button { Content = "Réinitialiser les réglages", Margin = new Thickness(0,12,0,0) }; body.Children.Add(reset);
        var dialog = CreateInfoWindow("RADIO IN / RÉGLAGES", body, 420);
        reset.Click += (_, _) => { ResetSettingsKeepingFavorites(); dialog.Close(); }; dialog.Show();
    }
    void ResetSettingsKeepingFavorites()
    {
        var favorites = preferences.Favorites; preferences = new Preferences { Favorites = favorites, LastSeenVersion = AppInfo.Version };
        muted = false; MuteButton.Content = CoverMute.Content = "Son"; ApplyReadingSettings();
        if (!testMode) SetGlobalMediaKeys(false);
        SetSleep(0); Volume.Value = preferences.Volume; Pin.IsChecked = false; ApplyDesign(0); ChangeMode(0);
        Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2;
        Top = SystemParameters.WorkArea.Top + Math.Max(0, (SystemParameters.WorkArea.Height - ActualHeight) / 2);
        SavePreferences(); UpdateFavoritesList(); SyncFavoriteButtons(); ShowInlineNotice("Réglages réinitialisés. Vos favoris sont conservés.");
    }
    bool ExecuteMediaCommand(int command)
    {
        switch (command) {
            case 14: TogglePlay(this, new RoutedEventArgs()); return true;
            case 13: case 47: if (playing) TogglePlay(this, new RoutedEventArgs()); return true;
            case 46: if (!playing) TogglePlay(this, new RoutedEventArgs()); return true;
            default: return false;
        }
    }
    IntPtr MediaMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x312 && (wParam.ToInt32() == 5401 || wParam.ToInt32() == 5402)) handled = ExecuteMediaCommand(wParam.ToInt32() == 5401 ? 14 : 13);
        if (msg == 0x319) handled = ExecuteMediaCommand((int)((lParam.ToInt64() >> 16) & 0xfff));
        return IntPtr.Zero;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window, int id);
    bool SetGlobalMediaKeys(bool enabled)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(hwnd,5401); UnregisterHotKey(hwnd,5402);
        if (!enabled) return true;
        if (RegisterHotKey(hwnd,5401,0x4000,0xB3) && RegisterHotKey(hwnd,5402,0x4000,0xB2)) return true;
        UnregisterHotKey(hwnd,5401); UnregisterHotKey(hwnd,5402); preferences.MediaKeys = false;
        ShowInlineNotice("Les touches multimédias sont déjà utilisées par une autre application. Les raccourcis Ctrl+E restent disponibles."); return false;
    }
    void AddMediaOption(ContextMenu menu)
    {
        AddMediaOption(menu.Items);
    }
    void AddMediaOption(MenuItem menu)
    {
        AddMediaOption(menu.Items);
    }
    void AddMediaOption(ItemCollection items)
    {
        var option = new MenuItem { Header = "Touches multimédias en arrière-plan", IsCheckable = true, IsChecked = preferences.MediaKeys };
        option.Click += (_, _) => { preferences.MediaKeys = option.IsChecked && SetGlobalMediaKeys(true); if (!option.IsChecked) SetGlobalMediaKeys(false); option.IsChecked = preferences.MediaKeys; SavePreferences(); }; items.Add(option);
    }
    static string PlainReleaseNotes(string notes)
    {
        var text = Regex.Replace(notes ?? "", @"(?m)^\s{0,3}#{1,6}\s+", "");
        text = Regex.Replace(text, @"!?\[([^\]]+)\]\([^\r\n)]*\)", "$1");
        text = Regex.Replace(text, @"(?m)^\s*[-*+]\s+", "• ");
        return text.Replace("**", "").Replace("__", "").Replace("`", "");
    }
}
