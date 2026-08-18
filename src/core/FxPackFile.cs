using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;

namespace CostumeManager.Core
{

    public static class FxPackFile
    {
        public const string Extension = ".mhfxpack";
        public const string ManifestName = "fxpack.json";
        public const int Format = 1;

        public const string PackagesDir = "packages/";

        public const string ParentsDir = "parents/";

        public sealed class Info
        {
            public int Format { get; set; }
            public string CreatedUtc { get; set; }

            public string Token { get; set; }

            public string DisplayName { get; set; }
            public string Hero { get; set; }

            public List<FxRecord> Effects { get; set; } = new List<FxRecord>();
            public List<string> Parents { get; set; } = new List<string>();

            public Dictionary<string, string> Files { get; set; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public override string ToString() => DisplayName ?? Token;
        }

        static System.Text.Json.JsonSerializerOptions JsonOpts =>
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
            };

        public static string WriteJson(Info i)
        {
            var effects = new JsonArray();
            foreach (FxRecord r in i.Effects)
            {
                effects.Add(new JsonObject
                {

                    ["package"]    = r.Package,
                    ["classPath"]  = r.ClassPath,
                    ["fromAsset"]  = r.FromAsset,
                    ["effectName"] = r.EffectName,
                    ["sourceCrc"]  = r.SourceCrc,
                });
            }

            var parents = new JsonArray();
            foreach (string p in i.Parents) parents.Add(p);

            var root = new JsonObject
            {
                ["format"]      = i.Format,
                ["createdUtc"]  = i.CreatedUtc,
                ["token"]       = i.Token,
                ["displayName"] = i.DisplayName,
                ["hero"]        = i.Hero,
                ["effects"]     = effects,
                ["parents"]     = parents,
            };

            return root.ToJsonString(JsonOpts);
        }

        public static Info ReadJson(string text, out string error)
        {
            error = null;
            try
            {
                if (JsonNode.Parse(text) is not JsonObject o)
                { error = ManifestName + " is not an object"; return null; }

                var info = new Info
                {
                    Format      = o["format"]?.GetValue<int>() ?? 0,
                    CreatedUtc  = (string)o["createdUtc"],
                    Token       = (string)o["token"],
                    DisplayName = (string)o["displayName"],
                    Hero        = (string)o["hero"],
                };

                if (string.IsNullOrWhiteSpace(info.Token))
                { error = ManifestName + " has no token"; return null; }

                if (o["effects"] is JsonArray fx)
                {
                    foreach (JsonNode n in fx)
                    {
                        if (n is not JsonObject e) continue;
                        info.Effects.Add(new FxRecord
                        {
                            Package    = (string)e["package"],
                            ClassPath  = (string)e["classPath"],
                            FromAsset  = (string)e["fromAsset"],
                            EffectName = (string)e["effectName"],
                            SourceCrc  = (string)e["sourceCrc"],
                        });
                    }
                }

                if (o["parents"] is JsonArray ps)
                    foreach (JsonNode n in ps)
                    {
                        string s = n?.ToString();
                        if (!string.IsNullOrWhiteSpace(s)) info.Parents.Add(s);
                    }

                if (info.Effects.Count == 0)
                { error = ManifestName + " lists no effects"; return null; }

                return info;
            }
            catch (Exception ex)
            {
                error = "unreadable " + ManifestName + ": " + ex.Message;
                return null;
            }
        }

        public static bool Write(FxPack pack, string cookedDir, string outPath,
                                 out string error, Action<string> log = null)
        {
            error = null;

            if (pack == null) { error = "no pack"; return false; }
            if (pack.Effects == null || pack.Effects.Count == 0)
            { error = "pack \"" + pack.Token + "\" owns no effect packages"; return false; }

            var info = new Info
            {
                Format      = Format,
                CreatedUtc  = DateTime.UtcNow.ToString("o"),
                Token       = pack.Token,
                DisplayName = pack.DisplayName,
                Hero        = pack.Hero,
                Effects     = pack.Effects,
                Parents     = pack.Parents ?? new List<string>(),
            };

            var sources = new List<(string zipEntry, string path)>();
            var missing = new List<string>();

            foreach (FxRecord r in pack.Effects)
            {
                string file = PackageFileName(r.Package);
                string path = ResolveSource(r.UpkPath, cookedDir, file);
                if (path == null) missing.Add(file);
                else sources.Add((PackagesDir + file, path));
            }

            foreach (string parent in info.Parents)
            {
                string file = PackageFileName(parent);
                string path = ResolveSource(null, cookedDir, file);
                if (path == null) missing.Add(file);
                else sources.Add((ParentsDir + file, path));
            }

            if (missing.Count > 0)
            {
                error = missing.Count + " package(s) missing from CookedPCConsole, so this pack "
                      + "cannot be exported: " + string.Join(", ", missing.Take(6))
                      + (missing.Count > 6 ? ", ..." : "")
                      + ". A pack that is short even one package makes the importing player's "
                      + "costume fail to arm entirely, so it is refused rather than written.";
                return false;
            }

            try
            {
                string dir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(outPath)) File.Delete(outPath);

                using (var zip = ZipFile.Open(outPath, ZipArchiveMode.Create))
                {
                    ZipArchiveEntry man = zip.CreateEntry(ManifestName, CompressionLevel.Optimal);
                    using (var w = new StreamWriter(man.Open())) w.Write(WriteJson(info));

                    foreach (var (entry, path) in sources)
                        zip.CreateEntryFromFile(path, entry, CompressionLevel.Optimal);
                }
            }
            catch (Exception ex)
            {
                error = "could not write " + outPath + ": " + ex.Message;
                return false;
            }

            long bytes = new FileInfo(outPath).Length;
            log?.Invoke($"wrote {Path.GetFileName(outPath)} - {pack.Effects.Count} effect package(s), "
                      + $"{info.Parents.Count} parent(s), {bytes / (1024.0 * 1024.0):F1} MB");
            return true;
        }

        public static Info Read(string packPath, out string error)
        {
            error = null;

            if (!File.Exists(packPath)) { error = "no such file: " + packPath; return null; }

            try
            {
                using var zip = ZipFile.OpenRead(packPath);

                ZipArchiveEntry man = zip.GetEntry(ManifestName);
                if (man == null) { error = "not a " + Extension + " - no " + ManifestName; return null; }

                string text;
                using (var r = new StreamReader(man.Open())) text = r.ReadToEnd();

                Info info = ReadJson(text, out error);
                if (info == null) return null;

                if (info.Format > Format)
                {
                    error = $"this pack was made by a newer tool (format {info.Format}, this "
                          + $"understands {Format}). Update the installer rather than importing it.";
                    return null;
                }

                var absent = new List<string>();

                foreach (FxRecord r in info.Effects)
                {
                    string file = PackageFileName(r.Package);
                    string entry = PackagesDir + file;
                    if (zip.GetEntry(entry) == null) absent.Add(file);
                    else info.Files[entry] = file;
                }

                foreach (string parent in info.Parents)
                {
                    string file = PackageFileName(parent);
                    string entry = ParentsDir + file;
                    if (zip.GetEntry(entry) == null) absent.Add(file);
                    else info.Files[entry] = file;
                }

                if (absent.Count > 0)
                {
                    error = absent.Count + " package(s) named in " + ManifestName + " are not in "
                          + "the pack: " + string.Join(", ", absent.Take(6))
                          + (absent.Count > 6 ? ", ..." : "")
                          + ". The file is incomplete - re-download it.";
                    return null;
                }

                return info;
            }
            catch (Exception ex)
            {
                error = "could not read " + packPath + ": " + ex.Message;
                return null;
            }
        }

        public static string PackageFileName(string package)
        {
            if (string.IsNullOrWhiteSpace(package)) return null;
            return package.EndsWith(".upk", StringComparison.OrdinalIgnoreCase)
                ? package
                : package + ".upk";
        }

        static string ResolveSource(string recordedPath, string cookedDir, string fileName)
        {
            if (!string.IsNullOrWhiteSpace(recordedPath)
                && IsInside(cookedDir, recordedPath)
                && File.Exists(recordedPath))
                return recordedPath;

            string local = Path.Combine(cookedDir, fileName);
            return File.Exists(local) ? local : null;
        }

        static bool IsInside(string dir, string path)
        {
            try
            {
                string d = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                string p = Path.GetFullPath(path);
                return p.StartsWith(d, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
