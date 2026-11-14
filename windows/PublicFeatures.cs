using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace RadioIN;

static class StartupRegistration
{
    const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static string Command(string executable) => "\"" + executable + "\" --startup";
    internal static bool Enabled { get { using var key = Registry.CurrentUser.OpenSubKey(KeyPath); return key?.GetValue("RadioIN") is string; } }
    internal static void Set(bool enabled)
    {
        if (AppInfo.IsTest) throw new InvalidOperationException("Les tests ne modifient pas le démarrage Windows.");
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        if (enabled) key.SetValue("RadioIN", Command(typeof(MainWindow).Assembly.Location), RegistryValueKind.String);
        else key.DeleteValue("RadioIN", false);
    }
}

public partial class MainWindow
{
    public sealed class BackupFile
    {
        public string Application { get; set; } = "RadioIN";
        public int Schema { get; set; } = 1;
        public string Version { get; set; } = AppInfo.Version;
        public Preferences Settings { get; set; } = new();
    }
    Window? aboutWindow;
    bool metadataOnline = true;
    string connectionText = "Prêt à écouter";
    static bool NetworkInterfaceAvailable() { try { return NetworkInterface.GetIsNetworkAvailable(); } catch { return true; } }
    internal void ActivateExistingInstance() { if (closed || Dispatcher.HasShutdownStarted) return; RestoreWindow(); LocalLog.Write("Fenêtre existante réaffichée"); }
    void InitializePublicFeatures()
    {
        Title = "Radio IN — " + AppInfo.Version;
        UpdateConnection(audio == null ? "Moteur audio indisponible · consultez le journal local" : "Prêt à écouter");
        Closed += (_, _) => aboutWindow?.Close();
        Loaded += async (_, _) => {
            await WaitForStartup();
            if (!testMode) {
                if (preferences.AutoPlay) TogglePlay(this, new RoutedEventArgs());
                if (Environment.GetCommandLineArgs().Contains("--startup") && preferences.LaunchHidden) WindowState = WindowState.Minimized;
            }
        };
        if (Environment.GetCommandLineArgs().Contains("--public-test")) Loaded += async (_, _) => { await WaitForStartup(); await TestPublicFeatures(); };
        if (Environment.GetCommandLineArgs().Contains("--instance-host-test")) Loaded += async (_, _) => {
            await WaitForStartup();
            SetupTray(); WindowState = WindowState.Minimized; await Task.Delay(300);
            var args = Environment.GetCommandLineArgs(); string token = args[Array.IndexOf(args, "--instance-host-test") + 1];
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "instance-ready-" + token + ".txt"), "ready");
            await Task.Delay(6500);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "instance-test-" + token + ".txt"), "Activations=" + PublicRuntime.Instance?.Activations + "; visible=" + IsVisible + "; normal=" + (WindowState == WindowState.Normal));
            Close();
        };
    }
    void UpdateConnection(string value, bool show = true)
    {
        connectionText = value; if (show) Status.Text = value;
        ReducedSong.ToolTip = Artist.Text + " — " + SongTitle.Text + "\n" + value;
        CoverPlay.ToolTip = value;
        UpdateConnectionBadge();
    }
    static string ExplainNetwork(Exception error, string service)
    {
        if (!NetworkInterfaceAvailable()) return "Aucune connexion réseau · attente du retour d’Internet";
        Exception? current = error;
        while (current != null) {
            if (current is SocketException socket && socket.SocketErrorCode == SocketError.HostNotFound) return "Serveur de la radio introuvable · nouvel essai automatique";
            if (current is TaskCanceledException) return service + " : délai de connexion dépassé";
            current = current.InnerException;
        }
        return service + " indisponible · nouvel essai automatique";
    }
    void AddPublicOptions(ContextMenu menu)
    {
        var backup = new MenuItem { Header = "Sauvegarde et réglages" };
        void Add(MenuItem parent,string text,Action action) { var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); parent.Items.Add(item); }
        Add(backup,"Exporter favoris et réglages…",ExportSettings);
        Add(backup,"Importer une sauvegarde…",ImportSettings);
        Add(backup,"Réinitialiser les réglages…",ConfirmResetSettings);
        menu.Items.Add(backup);
        var startupMenu = new MenuItem { Header = "Démarrage" };
        var startup = new MenuItem { Header = "Lancer avec Windows", IsCheckable = true };
        try { startup.IsChecked = StartupRegistration.Enabled; } catch(Exception ex) { LocalLog.Write("Lecture démarrage Windows", ex); }
        startup.Click += (_, _) => {
            try { StartupRegistration.Set(startup.IsChecked); }
            catch(Exception ex) { startup.IsChecked = !startup.IsChecked; LocalLog.Write("Configuration démarrage Windows", ex); MessageBox.Show(this, "Impossible de modifier le démarrage Windows.", "Radio IN"); }
        };
        startupMenu.Items.Add(startup);
        var hidden = new MenuItem { Header = "Avec Windows : démarrer près de l’horloge", IsCheckable = true, IsChecked = preferences.LaunchHidden };
        hidden.Click += (_, _) => { preferences.LaunchHidden = hidden.IsChecked; SavePreferences(); }; startupMenu.Items.Add(hidden);
        menu.Items.Add(startupMenu);
        var updates = new MenuItem { Header = "Mises à jour" }; AddUpdateOptions(updates); menu.Items.Add(updates);
        var about = new MenuItem { Header = "À propos — " + AppInfo.Version }; about.Click += (_, _) => ShowAbout(); menu.Items.Add(about);
    }
    Window CreateInfoWindow(string title, UIElement content, double width = 510)
    {
        var body = new DockPanel { Margin = new Thickness(16) };
        var header = new DockPanel { Background = Brushes.Transparent, Margin = new Thickness(0,0,0,16) };
        var close = new Button { Content = "×", Padding = new Thickness(0), Width = 32, Height = 30, FontSize = 20 };
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        header.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(header, Dock.Top); body.Children.Add(header); body.Children.Add(content);
        var dialog = new Window { Title = title, Icon = Icon, Owner = this, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Background = new SolidColorBrush(Color.FromRgb(20,29,42)), Width = width, SizeToContent = SizeToContent.Height, Content = body, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        close.Click += (_, _) => dialog.Close(); header.MouseLeftButtonDown += (_, e) => DragWindow(dialog, e); AddRoundedCorners(dialog); return dialog;
    }
    void OpenLocalFolder(string path)
    {
        try { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch(Exception ex) { LocalLog.Write("Ouverture dossier local", ex); MessageBox.Show(this, "Le dossier n’a pas pu être ouvert.", "Radio IN"); }
    }
    void ShowAbout()
    {
        if (aboutWindow != null) { aboutWindow.Activate(); return; }
        var body = new StackPanel();
        body.Children.Add(new Image { Source = Icon, Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,0,0,14) });
        body.Children.Add(new TextBlock { Text = AppInfo.Version, Foreground = Brushes.White, FontSize = 22, FontWeight = FontWeights.Bold });
        body.Children.Add(new TextBlock { Text = "Lecteur Radio IN · Windows 64 bits\n\nRecherche de mises à jour sur GitHub, instance unique, sauvegardes et options de démarrage.\n\nLes journaux restent sur ce PC : aucun envoi automatique.\n\nÉtat : " + connectionText, Foreground = Brushes.LightSteelBlue, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,16) });
        var data = new Button { Content = "Ouvrir le dossier des réglages", Margin = new Thickness(0,0,0,8) }; data.Click += (_, _) => OpenLocalFolder(dataDirectory); body.Children.Add(data);
        var logs = new Button { Content = "Ouvrir le journal d’erreurs", Margin = new Thickness(0) }; logs.Click += (_, _) => OpenLocalFolder(LocalLog.DirectoryPath); body.Children.Add(logs);
        aboutWindow = CreateInfoWindow("RADIO IN / À PROPOS", body); aboutWindow.Closed += (_, _) => aboutWindow = null; aboutWindow.Show();
    }
    BackupFile CreateBackup()
    {
        SavePreferences();
        var serializer = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        return new BackupFile { Settings = serializer.Deserialize<Preferences>(serializer.Serialize(preferences)) };
    }
    static Preferences ValidateBackup(string json)
    {
        if (json.Length > 4 * 1024 * 1024) throw new FormatException("Sauvegarde trop volumineuse.");
        var serializer = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        var fields = serializer.DeserializeObject(json) as Dictionary<string, object>;
        if (fields == null || !fields.ContainsKey("Application") || !fields.ContainsKey("Schema") || !fields.ContainsKey("Settings")) throw new FormatException("Sauvegarde Radio IN incomplète.");
        var backup = serializer.Deserialize<BackupFile>(json);
        if (backup == null || backup.Application != "RadioIN" || backup.Schema != 1 || backup.Settings == null) throw new FormatException("Ce fichier n’est pas une sauvegarde Radio IN compatible.");
        var settings = backup.Settings;
        if (double.IsNaN(settings.Volume) || double.IsInfinity(settings.Volume) || settings.Volume < 0 || settings.Volume > 1) throw new FormatException("Volume invalide dans la sauvegarde.");
        settings.Favorites = settings.Favorites ?? new List<Favorite>();
        settings.ModePositions ??= new Dictionary<string, ModePosition>();
        if (settings.Favorites.Count > 10000 || settings.Favorites.Any(f => f == null || string.IsNullOrWhiteSpace(f.Id) || f.Id.Length > 128 || f.Title == null || f.Title.Length > 1024 || f.Artist == null || f.Artist.Length > 1024)) throw new FormatException("Liste de favoris invalide.");
        settings.Favorites = settings.Favorites.GroupBy(f => f.Id).Select(g => g.First()).ToList();
        settings.Design = Math.Max(0, Math.Min(5, settings.Design)); settings.Mode = Math.Max(0, Math.Min(3, settings.Mode));
        settings.PlayerOpacity = FiniteRange(settings.PlayerOpacity,.7,1,1); settings.TextScale = FiniteRange(settings.TextScale,1,1.25,1); settings.AnimationMode = Math.Max(0,Math.Min(2,settings.AnimationMode));
        settings.SleepFadeSeconds = Math.Max(0, Math.Min(60, settings.SleepFadeSeconds));
        if (settings.Left.HasValue && (double.IsNaN(settings.Left.Value) || double.IsInfinity(settings.Left.Value))) settings.Left = null;
        if (settings.Top.HasValue && (double.IsNaN(settings.Top.Value) || double.IsInfinity(settings.Top.Value))) settings.Top = null;
        return settings;
    }
    void ApplyBackup(Preferences settings)
    {
        settings.Favorites = preferences.Favorites.Concat(settings.Favorites).GroupBy(f => f.Id).Select(g => g.First()).ToList();
        var before = preferences; restoring = true;
        try {
            preferences = settings; Volume.Value = settings.Volume; Pin.IsChecked = settings.Pin;
            ApplyDesign(settings.Design); ChangeMode(settings.Mode); ApplyReadingSettings(); UpdateFavoritesList();
            if (!testMode) SetGlobalMediaKeys(settings.MediaKeys);
            if (settings.ModePositions.TryGetValue(settings.Mode.ToString(System.Globalization.CultureInfo.InvariantCulture), out var position)) PlacePlayer(position);
            else if (settings.Left.HasValue && settings.Top.HasValue) {
                var monitor = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => settings.Left.Value >= s.WorkingArea.Left && settings.Left.Value < s.WorkingArea.Right && settings.Top.Value >= s.WorkingArea.Top && settings.Top.Value < s.WorkingArea.Bottom);
                if (monitor != null) { Left = settings.Left.Value; Top = settings.Top.Value; }
            }
            SyncFavoriteButtons();
        } catch { preferences = before; throw; }
        finally { restoring = false; }
        SavePreferences(); Status.Text = "Sauvegarde importée · favoris fusionnés.";
    }
    void ExportSettings()
    {
        var dialog = new SaveFileDialog { Title = "Exporter favoris et réglages", Filter = "Sauvegarde Radio IN (*.json)|*.json", FileName = "RadioIN-sauvegarde.json", DefaultExt = ".json", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.Serialize(CreateBackup())); Status.Text = "Sauvegarde exportée."; }
        catch(Exception ex) { LocalLog.Write("Export sauvegarde", ex); MessageBox.Show(this, "Impossible d’écrire cette sauvegarde.", "Radio IN"); }
    }
    void ImportSettings()
    {
        var dialog = new OpenFileDialog { Title = "Importer une sauvegarde Radio IN", Filter = "Sauvegarde Radio IN (*.json)|*.json", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        try {
            if (new FileInfo(dialog.FileName).Length > 4 * 1024 * 1024) throw new FormatException("Sauvegarde trop volumineuse.");
            var imported = ValidateBackup(File.ReadAllText(dialog.FileName));
            if (MessageBox.Show(this, "Importer ces réglages et fusionner les favoris ?\nVos favoris actuels seront conservés. Le démarrage avec Windows ne sera pas modifié.", "Radio IN — import", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            ApplyBackup(imported);
        } catch(Exception ex) { LocalLog.Write("Import sauvegarde", ex); MessageBox.Show(this, "Import impossible : " + ex.Message, "Radio IN"); }
    }
    async Task TestPublicFeatures()
    {
        var results = new List<string>();
        void Require(bool value, string name) { results.Add(name + "=" + value); if (!value) throw new InvalidOperationException(name); }
        try {
            preferences.Favorites.Clear(); preferences.Favorites.Add(new Favorite { Id="existing", Title="Favori existant", Artist="Test" });
            var json = new JavaScriptSerializer().Serialize(CreateBackup());
            var imported = ValidateBackup(json); imported.Favorites.Add(new Favorite { Id="new", Title="Nouveau favori", Artist="Test" }); imported.Mode = 2;
            ApplyBackup(imported); Require(preferences.Favorites.Count == 2, "Import merges without loss");
            ApplyBackup(ValidateBackup(json)); Require(preferences.Favorites.Count == 2, "Import does not duplicate");
            var originalSettings = ValidateBackup(json); Require(!originalSettings.Left.HasValue || Math.Abs(Left - originalSettings.Left.Value) < 1, "Import restores position");
            bool invalid = false; try { ValidateBackup("{\"Application\":\"Other\",\"Schema\":1}"); } catch(FormatException) { invalid = true; } Require(invalid, "Rejects wrong backup");
            invalid = false; try { ValidateBackup("not JSON"); } catch { invalid = true; } Require(invalid, "Rejects malformed JSON");
            LocalLog.Write("Public verification", new InvalidOperationException("Simulated local diagnostic"));
            Require(File.ReadAllText(Path.Combine(LocalLog.DirectoryPath, "RadioIN.log")).Contains("Simulated local diagnostic"), "Local error log");
            Require(StartupRegistration.Command(@"C:\Radio Test\RadioIN.exe") == "\"C:\\Radio Test\\RadioIN.exe\" --startup", "Quoted startup command");
            Require(!preferences.AutoPlay && !preferences.LaunchHidden, "Startup sound and hidden launch default off");
            ShowAbout(); Require(aboutWindow != null && aboutWindow.Title.Contains("À PROPOS"), "About window");
            aboutWindow.UpdateLayout();
            var preview = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(aboutWindow.ActualWidth), (int)Math.Ceiling(aboutWindow.ActualHeight), 96, 96, PixelFormats.Pbgra32); preview.Render((Visual)aboutWindow.Content);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(preview));
            using (var file = File.Create(Path.Combine(AppContext.BaseDirectory, "about-preview.png"))) encoder.Save(file);
            aboutWindow?.Close();
            UpdateConnection("Reconnexion au direct…"); Require(Status.Text.Contains("Reconnexion"), "Visible connection state");
            Require(ExplainNetwork(new TaskCanceledException(), "Radio").Length > 0, "Timeout message");
            await Task.Delay(100);
            results.Add("Registry unchanged: automatic startup registration is not exercised in tests.");
        } catch(Exception ex) { results.Add("ERROR: " + ex); }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "public-test.txt"), results); Close();
    }
}
