using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Threading;

namespace RadioIN;
public sealed class PortablePlan
{
    public string Archive { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Version { get; set; } = "";
    public string Target { get; set; } = "";
    public int Parent { get; set; }
    public bool TestOnly { get; set; }
}
// Shared by the player and its small, independent updater process.
static class PortableUpdate
{
    internal static string Hash(string path) { using var file = File.OpenRead(path); using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant(); }
    internal static void NoLinks(string path)
    {
        for (var dir = new DirectoryInfo(Path.GetFullPath(path)); dir != null; dir = dir.Parent)
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Les dossiers liés ne sont pas pris en charge pour une mise à jour ZIP.");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Fichier lié refusé.");
    }
    internal static bool Allowed(string name) => name == "RadioIN.exe" || name == "RadioIN.exe.config" || name == "libvlc.dll" || name == "libvlccore.dll" || name == "VLC-COPYING.txt" || name == "LIRE-MOI.txt" ||
        System.Text.RegularExpressions.Regex.IsMatch(name, @"^plugins/[a-z_]+/lib[a-zA-Z0-9_]+_plugin\.dll$");
    internal static List<string> Extract(string archive, string hash, Version version, string stage)
    {
        if (!string.Equals(Hash(archive),hash,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Empreinte SHA-256 du ZIP incorrecte.");
        NoLinks(stage); Directory.CreateDirectory(stage);
        var files = new List<string>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        using (var zip = ZipFile.OpenRead(archive)) {
            if (zip.Entries.Count > 256) throw new InvalidDataException("Le ZIP contient trop de fichiers.");
            foreach (var entry in zip.Entries) {
                string name = entry.FullName.Replace('\\','/');
                if (name.StartsWith("/") || name.Split('/').Any(p => p == ".." || p == "." || p.Contains(":")) || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000) throw new InvalidDataException("Chemin ou lien interdit dans le ZIP.");
                if (name.EndsWith("/")) { if (name != "plugins/" && !System.Text.RegularExpressions.Regex.IsMatch(name,@"^plugins/[a-z_]+/$")) throw new InvalidDataException("Dossier ZIP inattendu."); continue; }
                if (!Allowed(name) || !names.Add(name)) throw new InvalidDataException("Fichier ZIP inattendu ou dupliqué : " + name);
                total += entry.Length; if (entry.Length > 64 * 1024 * 1024 || total > 200 * 1024 * 1024) throw new InvalidDataException("ZIP décompressé trop volumineux.");
                string target = Path.GetFullPath(Path.Combine(stage,name.Replace('/',Path.DirectorySeparatorChar)));
                if (!target.StartsWith(Path.GetFullPath(stage).TrimEnd('\\') + "\\",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Extraction hors du dossier autorisé.");
                NoLinks(target); Directory.CreateDirectory(Path.GetDirectoryName(target));
                using var input = entry.Open(); using var output = new FileStream(target,FileMode.CreateNew,FileAccess.Write);
                byte[] buffer = new byte[81920]; long length = 0; int read;
                while ((read = input.Read(buffer,0,buffer.Length)) > 0) { length += read; if (length > entry.Length) throw new InvalidDataException("Fichier ZIP trop volumineux."); output.Write(buffer,0,read); }
                if (length != entry.Length) throw new InvalidDataException("Fichier ZIP incomplet.");
                files.Add(name);
            }
        }
        foreach (var required in new[] { "RadioIN.exe", "RadioIN.exe.config", "libvlc.dll", "libvlccore.dll", "plugins/access/libhttps_plugin.dll", "plugins/codec/libmpg123_plugin.dll" })
            if (!names.Contains(required)) throw new InvalidDataException("Fichier indispensable absent : " + required);
        var info = FileVersionInfo.GetVersionInfo(Path.Combine(stage,"RadioIN.exe"));
        if (!System.Version.TryParse(info.FileVersion?.Trim(),out var actual) || actual != version || info.ProductName?.Trim() != "Radio IN") throw new InvalidDataException("La version du ZIP ne correspond pas à la release Radio IN.");
        return files;
    }
    internal static void Apply(string stage, string target, string backup, List<string> files)
    {
        NoLinks(target); NoLinks(backup);
        if (!File.Exists(Path.Combine(target,"RadioIN.exe"))) throw new InvalidDataException("Lecteur portable introuvable dans le dossier cible.");
        Directory.CreateDirectory(backup);
        var changed = new List<string>(); var originals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Retain unrelated files and user data. Only files present in the validated archive are replaced.
        foreach (string name in files) { if (!Allowed(name)) throw new InvalidDataException("Fichier non autorisé."); NoLinks(Path.Combine(target,name)); }
        using var updateLock = new FileStream(Path.Combine(target,".radioin-update.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        try {
            foreach (string name in files.OrderBy(n => n == "RadioIN.exe" ? 1 : 0)) {
                string dest = Path.Combine(target,name), saved = Path.Combine(backup,name);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                if (File.Exists(dest)) { Directory.CreateDirectory(Path.GetDirectoryName(saved)); File.Copy(dest,saved,false); originals.Add(name); }
                // Probe the destination lock before overwriting. A failed probe has changed nothing.
                if (File.Exists(dest)) using (var probe = new FileStream(dest,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) { }
                changed.Add(name); File.Copy(Path.Combine(stage,name),dest,true);
            }
        } catch(Exception error) {
            var failures = new List<string>();
            foreach (var name in changed.AsEnumerable().Reverse()) try {
                string dest = Path.Combine(target,name);
                if (originals.Contains(name)) File.Copy(Path.Combine(backup,name),dest,true); else if (File.Exists(dest)) File.Delete(dest);
            } catch { failures.Add(name); }
            if (failures.Count > 0) throw new IOException("Restauration incomplète : " + string.Join(", ",failures) + ". Copie de secours : " + backup,error);
            throw new IOException("Mise à jour annulée ; les fichiers précédents ont été restaurés.",error);
        }
    }
}

#if PORTABLE_HELPER
static class PortableEntry
{
    [STAThread] static int Main(string[] args)
    {
        string root = AppDomain.CurrentDomain.BaseDirectory, message = ""; PortablePlan? plan = null; bool success = false;
        try {
            if (args.Length != 1) throw new ArgumentException("Plan de mise à jour manquant.");
            string planFile = Path.GetFullPath(args[0]); root = Path.GetDirectoryName(planFile)!;
            plan = new JavaScriptSerializer().Deserialize<PortablePlan>(File.ReadAllText(planFile));
            if (plan == null || !System.Version.TryParse(plan.Version,out var version)) throw new InvalidDataException("Plan invalide.");
            string stage = Path.Combine(root,"stage-" + Guid.NewGuid().ToString("N"));
            var files = PortableUpdate.Extract(plan.Archive,plan.Hash,version,stage);
            if (plan.Parent > 0) {
                try { using var parent = Process.GetProcessById(plan.Parent); if (!parent.WaitForExit(30000)) throw new IOException("Le lecteur est encore ouvert. Fermez-le avant de réessayer."); } catch(ArgumentException) { }
            }
            PortableUpdate.Apply(stage,plan.Target,Path.Combine(root,"backup"),files);
            success = true; message = "Mise à jour ZIP installée. Les favoris et réglages ont été conservés.";
        } catch(Exception ex) { message = ex.Message; }
        File.WriteAllText(Path.Combine(root,"result.txt"),(success ? "SUCCESS: " : "ERROR: ") + message);
        if (plan != null && !plan.TestOnly) {
            if (!success) System.Windows.Forms.MessageBox.Show(message,"Radio IN — mise à jour ZIP");
            try { Process.Start(new ProcessStartInfo(Path.Combine(plan.Target,"RadioIN.exe")) { UseShellExecute = true, WorkingDirectory = plan.Target }); } catch { }
        }
        return success ? 0 : 1;
    }
}
#endif
