using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows;

namespace RadioIN;

static class AppInfo
{
    internal const string Version = "Bêta 1.4";
    internal static readonly Version WindowsVersion = new(1,4,0,0);
    internal static bool IsTest => Environment.GetCommandLineArgs().Any(a => a.EndsWith("-test"));
    internal static string DataDirectory => IsTest ? Path.Combine(AppContext.BaseDirectory, "test-data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RadioIN");
}

static class LocalLog
{
    static readonly object Sync = new();
    static string last = "";
    static DateTime lastAt;
    internal static string DirectoryPath => Path.Combine(AppInfo.DataDirectory, "logs");
    internal static void Write(string context, Exception? error = null)
    {
        try { lock (Sync) {
            string message = context + (error == null ? "" : " | " + error);
            if (message == last && DateTime.UtcNow - lastAt < TimeSpan.FromSeconds(30)) return;
            last = message; lastAt = DateTime.UtcNow;
            Directory.CreateDirectory(DirectoryPath);
            string path = Path.Combine(DirectoryPath, "RadioIN.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) {
                string old = path + ".previous"; if (File.Exists(old)) File.Delete(old); File.Move(path, old);
            }
            File.AppendAllText(path, DateTime.UtcNow.ToString("O") + " | " + AppInfo.Version + " | " + message + Environment.NewLine);
        } } catch { /* Diagnostics must never prevent listening. */ }
    }
}

// Only the current Windows user can send the fixed activation byte.
sealed class SingleInstanceGate : IDisposable
{
    readonly Mutex mutex;
    readonly string pipeName;
    readonly SecurityIdentifier user;
    bool owned, disposed, pending;
    NamedPipeServerStream? server;
    MainWindow? window;
    internal int Activations { get; private set; }
    internal SingleInstanceGate(string group)
    {
        user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Utilisateur Windows inconnu");
        pipeName = "RadioIN.Activation." + user.Value + group;
        mutex = new Mutex(false, "Local\\" + pipeName);
    }
    internal bool Acquire()
    {
        try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
        return owned;
    }
    internal bool ActivateExisting()
    {
        try { using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out); client.Connect(5000); if (GetNamedPipeServerProcessId(client.SafePipeHandle, out var pid)) AllowSetForegroundWindow(pid); client.WriteByte(1); client.Flush(); return true; }
        catch (Exception ex) { LocalLog.Write("Activation de l'instance existante", ex); return false; }
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(uint processId);
    internal void Listen()
    {
        Task.Run(async () => {
            while (!disposed) {
                try {
                    var acl = new PipeSecurity(); acl.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
                    using (var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 128, 128, acl)) {
                        server = pipe; await pipe.WaitForConnectionAsync();
                        var command = new byte[1]; if (await pipe.ReadAsync(command, 0, 1) == 1 && command[0] == 1) {
                            lock (this) { Activations++; if (window == null) pending = true; else QueueActivation(window); }
                        }
                    }
                } catch (Exception ex) { if (!disposed) { LocalLog.Write("Canal d'activation", ex); await Task.Delay(300); } }
            }
        });
    }
    static void QueueActivation(MainWindow target)
    {
        if (!target.Dispatcher.HasShutdownStarted) target.Dispatcher.BeginInvoke(new Action(target.ActivateExistingInstance));
    }
    internal void Attach(MainWindow target) { lock (this) { window = target; if (pending) { pending = false; QueueActivation(target); } } }
    public void Dispose()
    {
        disposed = true; try { server?.Dispose(); } catch { }
        if (owned) { mutex.ReleaseMutex(); owned = false; } mutex.Dispose();
    }
}

static class PublicRuntime
{
    internal static SingleInstanceGate? Instance;
    internal static void Run()
    {
        SingleInstanceGate? gate = null;
        try {
            AppDomain.CurrentDomain.UnhandledException += (_, e) => LocalLog.Write("Exception fatale", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, e) => { LocalLog.Write("Tâche asynchrone", e.Exception); e.SetObserved(); };
            var args = Environment.GetCommandLineArgs();
            int groupIndex = Array.FindIndex(args, a => a == "--instance-host-test" || a == "--instance-client-test");
            if (!AppInfo.IsTest || groupIndex >= 0) {
                string group = "";
                if (groupIndex >= 0) {
                    if (groupIndex + 1 >= args.Length || !System.Text.RegularExpressions.Regex.IsMatch(args[groupIndex + 1], "^[a-zA-Z0-9-]{1,64}$")) throw new ArgumentException("Invalid test group");
                    group = ".Test." + args[groupIndex + 1];
                }
                gate = new SingleInstanceGate(group);
                if (!gate.Acquire()) {
                    if (!gate.ActivateExisting()) MessageBox.Show("Radio IN est déjà ouvert mais ne répond pas encore. Réessayez dans quelques secondes.", "Radio IN");
                    return;
                }
                Instance = gate; gate.Listen();
            }
            LocalLog.Write("Démarrage");
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            using var resource = typeof(PublicRuntime).Assembly.GetManifestResourceStream("Skin.xaml");
            var app = (Application)System.Windows.Markup.XamlReader.Load(resource);
            app.DispatcherUnhandledException += (_, e) => {
                LocalLog.Write("Exception interface", e.Exception);
                MessageBox.Show("Radio IN doit se fermer. L’erreur a été enregistrée localement dans :\n" + LocalLog.DirectoryPath, "Radio IN — erreur");
                e.Handled = true; app.Shutdown(1);
            };
            var main = new MainWindow(); gate?.Attach(main);
            app.MainWindow = main; app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            Environment.ExitCode = app.Run(main);
        } catch (Exception ex) {
            LocalLog.Write("Échec du lancement", ex);
            MessageBox.Show("Impossible de lancer Radio IN. Le journal local se trouve dans :\n" + LocalLog.DirectoryPath, "Radio IN — erreur");
            Environment.ExitCode = 1;
        } finally { Instance = null; gate?.Dispose(); }
    }
}
