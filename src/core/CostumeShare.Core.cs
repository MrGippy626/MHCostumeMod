
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using IconPack.Core;
using TfcAlias.Core;

namespace CostumeManager.Core
{

    public static class GamePaths
    {

        public static (string cooked, string manifest, string bin) Resolve(string gameRoot)
        {

            gameRoot = (gameRoot ?? "").Trim().Trim('"').TrimEnd('\\', '/');

            string[] cookedCandidates =
            {
                Path.Combine(gameRoot, "UnrealEngine3", "MarvelGame", "CookedPCConsole"),
                Path.Combine(gameRoot, "MarvelGame", "CookedPCConsole"),
                Path.Combine(gameRoot, "CookedPCConsole"),
            };

            string cooked = cookedCandidates.FirstOrDefault(Directory.Exists) ?? cookedCandidates[0];

            string[] binCandidates =
            {
                Path.Combine(gameRoot, "UnrealEngine3", "Binaries", "Win64"),
                Path.Combine(gameRoot, "UnrealEngine3", "Binaries", "Win32"),
                Path.Combine(gameRoot, "Binaries", "Win64"),
                Path.Combine(gameRoot, "Binaries", "Win32"),
            };

            string bin = binCandidates.FirstOrDefault(Directory.Exists) ?? binCandidates[0];

            string manifest = Path.Combine(cooked, "TextureFileCacheManifest.bin");
            return (cooked, manifest, bin);
        }

        public static bool LooksLikeGameFolder(string gameRoot)
        {
            if (string.IsNullOrWhiteSpace(gameRoot)) return false;
            var (cooked, _, _) = Resolve(gameRoot);
            return Directory.Exists(cooked);
        }

        public static string AutoDetect()
        {
            var roots = new List<string>();
            foreach (DriveInfo d in SafeDrives())
            {
                roots.Add(Path.Combine(d.RootDirectory.FullName, "MarvelHeroes"));
                roots.Add(Path.Combine(d.RootDirectory.FullName, "Marvel Heroes"));
                roots.Add(Path.Combine(d.RootDirectory.FullName, "Games", "MarvelHeroes"));
                roots.Add(Path.Combine(d.RootDirectory.FullName, "Program Files (x86)",
                                       "Steam", "steamapps", "common", "Marvel Heroes 2016"));
                roots.Add(Path.Combine(d.RootDirectory.FullName, "SteamLibrary",
                                       "steamapps", "common", "Marvel Heroes 2016"));
            }
            return roots.FirstOrDefault(LooksLikeGameFolder);
        }

        static IEnumerable<DriveInfo> SafeDrives()
        {
            DriveInfo[] all;
            try { all = DriveInfo.GetDrives(); } catch { yield break; }
            foreach (DriveInfo d in all)
            {
                bool ok;
                try { ok = d.IsReady && d.DriveType != DriveType.CDRom; } catch { ok = false; }
                if (ok) yield return d;
            }
        }
    }

    public static class Backup
    {

        public const int MaxStamped = 2;

        public static string Timestamped(string path)
        {

            string plainBak = path + ".bak";
            if (!File.Exists(plainBak))
            {
                File.Copy(path, plainBak);
                return plainBak;
            }

            try
            {
                string newestExisting = EnumerateStamped(path).Select(f => f.path).FirstOrDefault()
                                        ?? plainBak;
                if (File.Exists(newestExisting) && FilesEqual(path, newestExisting))
                    return newestExisting;
            }
            catch {  }

            string stamped = $"{path}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
            File.Copy(path, stamped, overwrite: true);

            Prune(path);
            return stamped;
        }

        public static IEnumerable<(string path, DateTime when)> EnumerateStamped(string path)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            string name = Path.GetFileName(path);
            string plainBak = name + ".bak";
            if (!Directory.Exists(dir)) yield break;

            var results = new List<(string, DateTime)>();
            foreach (var f in Directory.EnumerateFiles(dir, name + ".*.bak"))
            {
                string fn = Path.GetFileName(f);
                if (string.Equals(fn, plainBak, StringComparison.OrdinalIgnoreCase)) continue;

                string mid = fn.Length > name.Length + 5
                    ? fn.Substring(name.Length + 1, fn.Length - (name.Length + 1) - 4)
                    : "";
                DateTime when = DateTime.TryParseExact(mid, "yyyyMMdd-HHmmss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dt)
                    ? dt : File.GetLastWriteTime(f);
                results.Add((f, when));
            }
            foreach (var r in results.OrderByDescending(r => r.Item2))
                yield return r;
        }

        static void Prune(string path)
        {
            try
            {
                var stamped = EnumerateStamped(path).ToList();
                for (int i = MaxStamped; i < stamped.Count; i++)
                {
                    try { File.Delete(stamped[i].path); } catch { }
                }
            }
            catch { }
        }

        static bool FilesEqual(string a, string b)
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (!fa.Exists || !fb.Exists) return false;
            if (fa.Length != fb.Length) return false;
            using var sa = fa.OpenRead();
            using var sb = fb.OpenRead();
            byte[] ba = new byte[65536], bb = new byte[65536];
            int ra;
            while ((ra = sa.Read(ba, 0, ba.Length)) > 0)
            {
                int off = 0, rb;
                while (off < ra && (rb = sb.Read(bb, off, ra - off)) > 0) off += rb;
                if (off != ra) return false;
                for (int i = 0; i < ra; i++) if (ba[i] != bb[i]) return false;
            }
            return sb.ReadByte() == -1;
        }
    }

    public static class CostumeConfig
    {
        public const string PackedName = "CustomCostumes.mhc";
        public const string PlainName  = "CustomCostumes.json";

        static readonly byte[] Magic = { (byte)'M', (byte)'H', (byte)'C', (byte)'C' };
        const byte Version = 1;
        const string Key = "MarvelHeroesCostumeConfig";

        public static string PackedPath(string anyConfigPathOrDir)
            => Path.Combine(DirOf(anyConfigPathOrDir), PackedName);

        public static string PlainPath(string anyConfigPathOrDir)
            => Path.Combine(DirOf(anyConfigPathOrDir), PlainName);

        public static string ExistingPath(string anyConfigPathOrDir)
        {
            string packed = PackedPath(anyConfigPathOrDir);
            string plain  = PlainPath(anyConfigPathOrDir);
            bool hasPacked = File.Exists(packed), hasPlain = File.Exists(plain);

            if (hasPacked && hasPlain)
            {
                try
                {
                    return File.GetLastWriteTimeUtc(plain) > File.GetLastWriteTimeUtc(packed)
                        ? plain : packed;
                }
                catch { return packed; }
            }
            if (hasPacked) return packed;
            return hasPlain ? plain : packed;
        }

        public static bool Exists(string anyConfigPathOrDir)
            => File.Exists(PackedPath(anyConfigPathOrDir)) || File.Exists(PlainPath(anyConfigPathOrDir));

        public static string InUseName(string anyConfigPathOrDir)
            => Path.GetFileName(ExistingPath(anyConfigPathOrDir));

        public static string ReadAllText(string anyConfigPathOrDir)
        {

            string chosen = ExistingPath(anyConfigPathOrDir);

            if (string.Equals(Path.GetFileName(chosen), PackedName, StringComparison.OrdinalIgnoreCase)
                && File.Exists(chosen))
            {
                byte[] raw = File.ReadAllBytes(chosen);
                if (IsPacked(raw))
                    return System.Text.Encoding.UTF8.GetString(Transform(raw, 5, raw.Length - 5));

                string fallback = PlainPath(anyConfigPathOrDir);
                return File.Exists(fallback) ? File.ReadAllText(fallback) : null;
            }

            return File.Exists(chosen) ? File.ReadAllText(chosen) : null;
        }

        public static void WriteAllText(string anyConfigPathOrDir, string text)
        {
            string packed = PackedPath(anyConfigPathOrDir);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(packed)));

            byte[] body = System.Text.Encoding.UTF8.GetBytes(text);
            byte[] scrambled = Transform(body, 0, body.Length);

            var outBytes = new byte[5 + scrambled.Length];
            Array.Copy(Magic, outBytes, 4);
            outBytes[4] = Version;
            Array.Copy(scrambled, 0, outBytes, 5, scrambled.Length);
            File.WriteAllBytes(packed, outBytes);
            WriteSnapshot(text);

            string plain = PlainPath(anyConfigPathOrDir);
            if (File.Exists(plain))
            {
                try
                {
                    string retired = plain + ".replaced";
                    if (File.Exists(retired)) File.Delete(retired);
                    File.Move(plain, retired);
                }
                catch {  }
            }
        }

        public static string SnapshotDirectory { get; set; }

        public const string SnapshotName = "CustomCostumes.snapshot.json";

        static void WriteSnapshot(string text)
        {
            if (string.IsNullOrWhiteSpace(SnapshotDirectory)) return;
            try
            {
                Directory.CreateDirectory(SnapshotDirectory);
                string note =
                    "// READ-ONLY SNAPSHOT of the live CustomCostumes.mhc, written by the Costume\n"
                  + "// Manager for reference. EDITING THIS FILE DOES NOTHING - the game reads the\n"
                  + "// .mhc in the game folder. Change costumes with the Manager, or repack with:\n"
                  + "//     python mhcpack.py pack <edited.json> -o CustomCostumes.mhc\n";
                File.WriteAllText(Path.Combine(SnapshotDirectory, SnapshotName), note + text);
            }
            catch {  }
        }

        public sealed class RestoreSource
        {
            public string Path { get; set; }
            public string Label { get; set; }
            public int Costumes { get; set; }
            public DateTime When { get; set; }

            public override string ToString() => Label;
        }

        public static List<RestoreSource> FindRestoreSources(string anyConfigPathOrDir,
                                                             string snapshotDir = null)
        {
            var found = new List<RestoreSource>();
            string dir = DirOf(anyConfigPathOrDir);

            var candidates = new List<string>();
            try
            {
                candidates.AddRange(Directory.GetFiles(dir, PackedName + "*"));
                candidates.AddRange(Directory.GetFiles(dir, PlainName + "*"));
            }
            catch { }
            if (!string.IsNullOrWhiteSpace(snapshotDir))
            {
                try { candidates.AddRange(Directory.GetFiles(snapshotDir, "CustomCostumes*.json")); }
                catch { }
            }

            foreach (string f in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {

                if (string.Equals(f, PackedPath(dir), StringComparison.OrdinalIgnoreCase)) continue;

                string text = TryReadAny(f);
                if (text == null) continue;

                int count;
                try
                {
                    var arr = JsonNode.Parse(StripComments(text))?["costumes"] as JsonArray;
                    if (arr == null) continue;
                    count = arr.Count;
                }
                catch { continue; }

                found.Add(new RestoreSource
                {
                    Path = f,
                    Costumes = count,
                    When = File.GetLastWriteTime(f),
                    Label = $"{System.IO.Path.GetFileName(f)} — {count} costume(s), "
                          + File.GetLastWriteTime(f).ToString("yyyy-MM-dd HH:mm"),
                });
            }
            return found.OrderByDescending(r => r.When).ToList();
        }

        public static string TryReadAny(string path)
        {
            try
            {
                byte[] raw = File.ReadAllBytes(path);
                if (IsPacked(raw))
                    return System.Text.Encoding.UTF8.GetString(Transform(raw, 5, raw.Length - 5));
                return System.Text.Encoding.UTF8.GetString(raw);
            }
            catch { return null; }
        }

        public static string StripComments(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var lines = text.Split('\n').SkipWhile(l => l.TrimStart().StartsWith("//"));
            return string.Join("\n", lines);
        }

        public static int RestoreFrom(string anyConfigPathOrDir, string sourcePath)
        {
            string text = StripComments(TryReadAny(sourcePath))
                ?? throw new IOException("could not read " + sourcePath);

            var arr = JsonNode.Parse(text)?["costumes"] as JsonArray
                ?? throw new InvalidDataException("that file has no \"costumes\" array");

            string packed = PackedPath(anyConfigPathOrDir);
            if (File.Exists(packed)) Backup.Timestamped(packed);

            WriteAllText(anyConfigPathOrDir, text);
            return arr.Count;
        }

        static bool IsPacked(byte[] raw)
        {
            if (raw == null || raw.Length < 5) return false;
            for (int i = 0; i < 4; i++) if (raw[i] != Magic[i]) return false;
            return true;
        }

        static byte[] Transform(byte[] src, int offset, int count)
        {
            var outBytes = new byte[count];
            for (int i = 0; i < count; i++)
            {
                byte c = src[offset + i];
                c = (byte)(c ^ (byte)Key[i % Key.Length]);
                c = (byte)(c ^ unchecked((byte)(i * 167 + 13)));
                outBytes[i] = c;
            }
            return outBytes;
        }

        static string DirOf(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return ".";

            if (Directory.Exists(p)) return p;
            string dir = Path.GetDirectoryName(p);
            return string.IsNullOrEmpty(dir) ? "." : dir;
        }
    }

    public sealed class InstalledCostume
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public uint Enum { get; set; }
        public ulong CustomId { get; set; }
        public string DonorClass { get; set; }
        public string Upk { get; set; }
        public string IconPackage { get; set; }
        public string InstalledUtc { get; set; }
        public bool InLedger { get; set; }

        public bool Enabled { get; set; } = true;
    }

    public static class CostumeLibrary
    {

        public static string CustomCostumesJson(string gameRoot)
        {
            var (_, _, bin) = GamePaths.Resolve(gameRoot);
            return CostumeConfig.ExistingPath(bin);
        }

        public static List<InstalledCostume> ListInstalled(string gameRoot)
        {
            var list = new List<InstalledCostume>();
            string jsonPath = CustomCostumesJson(gameRoot);
            var ledger = InstallLedger.Read();

            Add(ReadCostumes(jsonPath), true);
            Add(ReadDisabled(jsonPath), false);
            return list;

            void Add(JsonArray arr, bool enabled)
            {
                if (arr == null) return;
                foreach (var n in arr)
                {
                    if (n is not JsonObject o) continue;
                    string display = (string)o["name"];
                    uint en = o["enum"]?.GetValue<uint>() ?? 0;
                    var led = ledger.FirstOrDefault(r => r.Enum == en)
                              ?? ledger.FirstOrDefault(r => string.Equals(r.DisplayName, display,
                                                                         StringComparison.OrdinalIgnoreCase));
                    list.Add(new InstalledCostume
                    {
                        DisplayName  = display,
                        Enum         = en,
                        CustomId     = ParseHex(o["customId"] ?? o["prototypeId"]),
                        DonorClass   = (string)o["donorClass"],
                        Upk          = (string)o["upk"],
                        IconPackage  = (string)o["iconPackage"],
                        Name         = led?.Name,
                        InstalledUtc = led?.InstalledUtc,
                        InLedger     = led != null,
                        Enabled      = enabled,
                    });
                }
            }
        }

        public static JsonArray ReadCostumes(string jsonPath)
        {
            if (!CostumeConfig.Exists(jsonPath)) return null;
            try { return JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath))?["costumes"] as JsonArray; }
            catch { return null; }
        }

        public static JsonArray ReadDisabled(string jsonPath)
        {
            if (!CostumeConfig.Exists(jsonPath)) return null;
            try { return JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath))?["disabled"] as JsonArray; }
            catch { return null; }
        }

        public static int SetEnabled(string jsonPath, IDictionary<ulong, bool> wanted)
        {
            if (wanted == null || wanted.Count == 0) return 0;

            JsonObject root;
            try { root = JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath) ?? "") as JsonObject; }
            catch { return 0; }
            if (root == null) return 0;

            if (root["costumes"] is not JsonArray live) { live = new JsonArray(); root["costumes"] = live; }
            if (root["disabled"] is not JsonArray off) { off = new JsonArray(); root["disabled"] = off; }

            int moved = 0;
            moved += Move(off, live, id => wanted.TryGetValue(id, out bool on) && on);
            moved += Move(live, off, id => wanted.TryGetValue(id, out bool on) && !on);

            if (moved > 0)
            {
                root["disabled"] = off;
                CostumeConfig.WriteAllText(jsonPath, root.ToJsonString(JsonOpts));
            }
            return moved;

            int Move(JsonArray from, JsonArray to, Func<ulong, bool> shouldMove)
            {
                int n = 0;
                for (int i = from.Count - 1; i >= 0; i--)
                {
                    if (from[i] is not JsonObject o) continue;
                    ulong id = ParseHex(o["customId"] ?? o["prototypeId"]);
                    if (!shouldMove(id)) continue;
                    JsonNode clone = o.DeepClone();
                    from.RemoveAt(i);
                    to.Add(clone);
                    n++;
                }
                return n;
            }
        }

        public static JsonObject FindByEnum(string jsonPath, uint enumId)
        {
            if (ReadCostumes(jsonPath) is not JsonArray arr) return null;
            foreach (var n in arr)
                if (n is JsonObject o && o["enum"]?.GetValue<uint>() == enumId) return o;
            return null;
        }

        public static JsonObject FindByCustomId(string jsonPath, ulong customId)
        {
            if (customId == 0 || ReadCostumes(jsonPath) is not JsonArray arr) return null;
            foreach (var n in arr)
                if (n is JsonObject o && ParseHex(o["customId"] ?? o["prototypeId"]) == customId) return o;
            return null;
        }

        public static ulong ParseHex(JsonNode n)
        {
            string s = (string)n;
            if (string.IsNullOrWhiteSpace(s)) return 0;
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            return ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber,
                                  System.Globalization.CultureInfo.InvariantCulture, out ulong v) ? v : 0;
        }

        internal static JsonSerializerOptions JsonOpts => new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        };

        public static void UpsertEntry(string jsonPath, JsonObject entry, ulong customId, uint enumId)
        {
            JsonObject root = null;
            if (CostumeConfig.Exists(jsonPath))
            {
                try { root = JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath)) as JsonObject; } catch { }
            }
            root ??= new JsonObject();

            if (root["fxDryRun"] == null) root["fxDryRun"] = false;
            if (root["perAvatarMesh"] == null) root["perAvatarMesh"] = true;

            if (root["costumes"] is not JsonArray arr)
            {
                arr = new JsonArray();
                root["costumes"] = arr;
            }

            for (int i = arr.Count - 1; i >= 0; i--)
            {
                if (arr[i] is not JsonObject o) continue;
                bool sameId   = ParseHex(o["customId"] ?? o["prototypeId"]) == customId;
                bool sameEnum = o["enum"] != null && o["enum"].GetValue<uint>() == enumId;
                if (sameId || sameEnum) arr.RemoveAt(i);
            }

            arr.Add(entry);
            CostumeConfig.WriteAllText(jsonPath, root.ToJsonString(JsonOpts));
        }

        public static JsonObject RemoveEntry(string jsonPath, uint enumId)
        {
            if (!CostumeConfig.Exists(jsonPath)) return null;
            JsonObject root;
            try { root = JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath)) as JsonObject; }
            catch { return null; }
            if (root?["costumes"] is not JsonArray arr) return null;

            JsonObject removed = null;
            for (int i = arr.Count - 1; i >= 0; i--)
            {
                if (arr[i] is not JsonObject o) continue;
                if (o["enum"]?.GetValue<uint>() != enumId) continue;
                removed = o.DeepClone() as JsonObject;
                arr.RemoveAt(i);
            }

            if (removed != null) CostumeConfig.WriteAllText(jsonPath, root.ToJsonString(JsonOpts));
            return removed;
        }
    }

    public sealed class CostumePackInfo
    {
        public int    Format      { get; set; }
        public string Name        { get; set; }
        public string DisplayName { get; set; }
        public ulong  CustomId    { get; set; }
        public string DonorClass  { get; set; }
        public uint   Enum        { get; set; }
        public string UpkFileName { get; set; }
        public string CreatedUtc  { get; set; }

        public List<AliasPair> TfcAliasPairs { get; set; } = new List<AliasPair>();

        public Dictionary<IconRole, string> IconArt { get; set; } = new Dictionary<IconRole, string>();

        public string IconUpk { get; set; }

        public string FxPackToken { get; set; }

        public JsonObject Entry { get; set; }
    }

    public static class CostumePackFile
    {
        public const string Extension = ".mhcostume";
        public const string ManifestName = "pack.json";
        public const int Format = 1;

        public const string IconUpkEntry = "icons/package.upk";

        public static string WriteJson(CostumePackInfo i)
        {
            var pairs = new JsonArray();
            foreach (AliasPair p in i.TfcAliasPairs)
                pairs.Add(new JsonObject { ["from"] = p.From, ["to"] = p.To });

            var icons = new JsonArray();
            foreach (var kv in i.IconArt)
                icons.Add(new JsonObject { ["role"] = kv.Key.ToString(), ["file"] = kv.Value });

            var root = new JsonObject
            {
                ["format"]        = i.Format,
                ["createdUtc"]    = i.CreatedUtc,
                ["name"]          = i.Name,
                ["displayName"]   = i.DisplayName,
                ["customId"]      = $"0x{i.CustomId:X16}",
                ["donorClass"]    = i.DonorClass,
                ["enum"]          = i.Enum,
                ["upkFileName"]   = i.UpkFileName,
                ["iconUpk"]       = i.IconUpk,
                ["tfcAliasPairs"] = pairs,
                ["iconArt"]       = icons,
                ["fxPackToken"]   = i.FxPackToken,
                ["entry"]         = i.Entry?.DeepClone(),
            };
            return root.ToJsonString(CostumeLibrary.JsonOpts);
        }

        public static CostumePackInfo ParseJson(string text, out string error)
        {
            error = null;
            try
            {
                if (JsonNode.Parse(text) is not JsonObject o) { error = "pack.json is not an object"; return null; }

                var info = new CostumePackInfo
                {
                    Format      = o["format"]?.GetValue<int>() ?? 0,
                    CreatedUtc  = (string)o["createdUtc"],
                    Name        = (string)o["name"],
                    DisplayName = (string)o["displayName"],
                    CustomId    = CostumeLibrary.ParseHex(o["customId"]),
                    DonorClass  = (string)o["donorClass"],
                    Enum        = o["enum"]?.GetValue<uint>() ?? 0,
                    UpkFileName = (string)o["upkFileName"],
                    IconUpk     = (string)o["iconUpk"],
                    FxPackToken = (string)o["fxPackToken"],
                    Entry       = o["entry"]?.DeepClone() as JsonObject,
                };

                if (string.IsNullOrWhiteSpace(info.UpkFileName))
                { error = "pack.json is missing upkFileName"; return null; }

                if (info.Entry == null)
                { error = "pack.json has no costume entry"; return null; }

                foreach (var n in (o["tfcAliasPairs"] as JsonArray) ?? new JsonArray())
                    if (n is JsonObject p && (string)p["from"] != null && (string)p["to"] != null)
                        info.TfcAliasPairs.Add(new AliasPair((string)p["from"], (string)p["to"]));

                foreach (var n in (o["iconArt"] as JsonArray) ?? new JsonArray())
                    if (n is JsonObject a && System.Enum.TryParse((string)a["role"], out IconRole role)
                        && (string)a["file"] != null)
                        info.IconArt[role] = (string)a["file"];

                return info;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        public static CostumePackInfo Read(string packPath, out string error)
        {
            error = null;
            try
            {
                using var zip = ZipFile.OpenRead(packPath);
                ZipArchiveEntry man = zip.GetEntry(ManifestName);
                if (man == null) { error = "not a costume pack (no " + ManifestName + ")"; return null; }

                string text;
                using (var r = new StreamReader(man.Open())) text = r.ReadToEnd();

                CostumePackInfo info = ParseJson(text, out error);
                if (info == null) return null;

                if (info.Format > Format)
                {
                    error = $"pack format {info.Format} is newer than this program understands "
                          + $"(supports {Format}) - update it";
                    return null;
                }
                if (zip.GetEntry(info.UpkFileName) == null)
                {
                    error = $"pack does not contain its costume UPK ({info.UpkFileName})";
                    return null;
                }
                return info;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }
    }

    public sealed class ManifestReport
    {
        public bool Ok => Problems.Count == 0;
        public List<string> Problems { get; } = new List<string>();
        public List<string> LostRows { get; } = new List<string>();
        public int Entries { get; set; }
        public int PristineEntries { get; set; }
        public int Added { get; set; }
        public long Bytes { get; set; }
        public bool RoundTrips { get; set; }

        public bool NoBaseline { get; set; }

        public List<string> MissingCustomRows { get; } = new List<string>();
        public int CustomRowsChecked { get; set; }
    }

    public static class ManifestDoctor
    {

        public static string PristinePath(string manifestPath) => manifestPath + ".bak";

        public static bool EnsureBaseline(string manifestPath, bool alreadyHasCostumes,
                                          Action<string> log = null)
        {
            string bak = PristinePath(manifestPath);
            if (File.Exists(bak)) return true;
            if (!File.Exists(manifestPath)) return false;

            try
            {
                File.Copy(manifestPath, bak);
                if (alreadyHasCostumes)
                    log?.Invoke("NOTE: saved the first manifest baseline, but costumes are already "
                              + "installed - so this baseline is NOT stock. Lost-row detection will "
                              + "only catch damage from here on.");
                else
                    log?.Invoke("saved a pristine copy of the texture manifest ("
                              + Path.GetFileName(bak) + ")");
                return true;
            }
            catch (Exception ex) { log?.Invoke("could not save a manifest baseline: " + ex.Message); return false; }
        }

        public static void GuardAfterRemoval(string manifestPath, Action<string> log = null)
        {
            try
            {
                if (!File.Exists(PristinePath(manifestPath))) return;

                ManifestReport rep = Check(manifestPath);
                if (rep.LostRows.Count == 0) return;

                log?.Invoke($"⚠ {rep.LostRows.Count} STOCK manifest row(s) went missing during that "
                          + "operation - restoring them now:");
                foreach (string r in rep.LostRows.Take(10)) log?.Invoke("     " + r);
                if (rep.LostRows.Count > 10) log?.Invoke($"     ... and {rep.LostRows.Count - 10} more");

                if (Repair(manifestPath, out int restored, log))
                    log?.Invoke($"  restored {restored} stock row(s) - the client is safe. "
                              + "Please report this, it means an uninstall removed data it should not have.");
                else
                    log?.Invoke("  COULD NOT repair automatically - use the Repair tab.");
            }
            catch (Exception ex) { log?.Invoke("manifest guard failed: " + ex.Message); }
        }

        public static ManifestReport Check(string manifestPath, string pristinePath = null)
        {
            var rep = new ManifestReport();
            pristinePath ??= PristinePath(manifestPath);

            if (!File.Exists(manifestPath))
            {
                rep.Problems.Add("TextureFileCacheManifest.bin not found: " + manifestPath);
                return rep;
            }

            TfcManifest live;
            try { live = TfcManifest.Load(manifestPath); }
            catch (Exception ex) { rep.Problems.Add("could not read the manifest: " + ex.Message); return rep; }

            rep.Entries = live.Entries.Count;
            rep.Bytes = new FileInfo(manifestPath).Length;

            try { rep.RoundTrips = TfcEngine.Verify(manifestPath).Identical; }
            catch { rep.RoundTrips = false; }
            if (!rep.RoundTrips)
                rep.Problems.Add("this manifest does not round-trip - it may be damaged, and repair "
                               + "is disabled because rewriting it could lose more.");

            var dupes = live.Entries.GroupBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                                    .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dupes.Count > 0)
                rep.Problems.Add($"{dupes.Count} duplicate row name(s), e.g. {string.Join(", ", dupes.Take(3))}");

            int blank = live.Entries.Count(e => string.IsNullOrEmpty(e.FullName) || !e.FullName.Contains('.'));
            if (blank > 0) rep.Problems.Add($"{blank} row(s) with a blank or malformed name");

            if (!File.Exists(pristinePath))
            {

                rep.NoBaseline = true;
                return rep;
            }

            TfcManifest stock;
            try { stock = TfcManifest.Load(pristinePath); }
            catch (Exception ex) { rep.Problems.Add("could not read the pristine backup: " + ex.Message); return rep; }

            rep.PristineEntries = stock.Entries.Count;
            var haveNow = new HashSet<string>(live.Entries.Select(e => e.FullName), StringComparer.OrdinalIgnoreCase);
            var stockNames = new HashSet<string>(stock.Entries.Select(e => e.FullName), StringComparer.OrdinalIgnoreCase);

            rep.LostRows.AddRange(stock.Entries.Select(e => e.FullName).Where(n => !haveNow.Contains(n)));
            rep.Added = live.Entries.Count(e => !stockNames.Contains(e.FullName));

            if (rep.LostRows.Count > 0)
                rep.Problems.Add($"{rep.LostRows.Count} STOCK row(s) are missing - installs may only add, "
                               + "so these were deleted. The client can crash while streaming those textures.");
            return rep;
        }

        public static void CheckCustomRows(ManifestReport rep, string manifestPath,
                                           IEnumerable<(string Name, IEnumerable<string> Rows)> installed)
        {
            if (rep == null || installed == null) return;

            HashSet<string> have;
            try
            {
                have = new HashSet<string>(TfcManifest.Load(manifestPath).Entries.Select(e => e.FullName),
                                           StringComparer.OrdinalIgnoreCase);
            }
            catch { return; }

            foreach (var (name, rows) in installed)
            {
                if (rows == null) continue;
                foreach (string row in rows)
                {
                    if (string.IsNullOrWhiteSpace(row)) continue;
                    rep.CustomRowsChecked++;
                    if (!have.Contains(row))
                        rep.MissingCustomRows.Add($"{name}: {row}");
                }
            }

            if (rep.MissingCustomRows.Count > 0)
                rep.Problems.Add($"{rep.MissingCustomRows.Count} alias row(s) for installed costumes are "
                    + "MISSING - those costumes will freeze the client when equipped, because their "
                    + "textures cannot be resolved. Restore a manifest backup that still has them, or "
                    + "reinstall the affected costumes.");
        }

        public static bool Repair(string manifestPath, out int restored, Action<string> log = null,
                                  string pristinePath = null)
        {
            restored = 0;
            pristinePath ??= PristinePath(manifestPath);

            ManifestReport rep = Check(manifestPath, pristinePath);
            if (!rep.RoundTrips)
            {
                log?.Invoke("REFUSING to repair: the manifest does not round-trip byte-for-byte.");
                return false;
            }
            if (rep.LostRows.Count == 0) { log?.Invoke("nothing missing - no repair needed"); return true; }

            TfcManifest live = TfcManifest.Load(manifestPath);
            TfcManifest stock = TfcManifest.Load(pristinePath);

            var haveNow = new HashSet<string>(live.Entries.Select(e => e.FullName), StringComparer.OrdinalIgnoreCase);
            List<Entry> missing = stock.Entries.Where(e => !haveNow.Contains(e.FullName)).ToList();

            Backup.Timestamped(manifestPath);
            foreach (Entry e in missing) live.Entries.Add(e);
            File.WriteAllBytes(manifestPath, live.Save());
            restored = missing.Count;

            foreach (Entry e in missing.Take(20)) log?.Invoke("   + " + e.FullName);
            if (missing.Count > 20) log?.Invoke($"   ... and {missing.Count - 20} more");
            log?.Invoke($"restored {restored} row(s) -> {live.Entries.Count:N0} total");

            ManifestReport after = Check(manifestPath, pristinePath);
            if (after.LostRows.Count > 0)
            {
                log?.Invoke("WARNING: rows are still missing after the repair.");
                return false;
            }
            return true;
        }
    }

    public sealed class PlayerResult
    {
        public bool Ok { get; set; }
        public string FailedStep { get; set; }
        public List<string> Steps { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();

        public uint Enum { get; set; }

        public string Replaced { get; set; }

        public bool WasReinstall { get; set; }
    }

    public static class PlayerInstall
    {

        public static PlayerResult Import(string gameRoot, string packPath, Action<string> log = null)
        {
            var res = new PlayerResult();

            log?.Invoke("  reading the pack...");

            CostumePackInfo info = CostumePackFile.Read(packPath, out string err);
            if (info == null)
            {
                res.FailedStep = "read";
                log?.Invoke("Cannot read pack: " + err);
                return res;
            }

            log?.Invoke($"  pack read: \"{info.DisplayName}\" enum {info.Enum}, "
                      + $"{info.UpkFileName ?? "(no upk)"}");

            var (cooked, manifestPath, bin) = GamePaths.Resolve(gameRoot);
            if (!Directory.Exists(cooked))
            {
                res.FailedStep = "gamefolder";
                log?.Invoke($"CookedPCConsole not found under \"{gameRoot}\". Pick your Marvel Heroes folder.");
                return res;
            }
            Directory.CreateDirectory(bin);

            string jsonPath = Path.Combine(bin, "CustomCostumes.json");

            ulong customId = info.CustomId;
            if (!string.IsNullOrWhiteSpace(info.Name))
            {
                ulong fromName = HashName.CustomId(info.Name);
                if (customId == 0) customId = fromName;
                else if (customId != fromName)
                {
                    res.FailedStep = "identity";
                    log?.Invoke($"Pack is inconsistent: customId 0x{customId:X16} does not match the hash "
                              + $"of name \"{info.Name}\" (0x{fromName:X16}). Refusing to import.");
                    return res;
                }
            }
            if (customId == 0)
            {
                res.FailedStep = "identity";
                log?.Invoke("Pack has no customId and no name to derive one from. Refusing to import.");
                return res;
            }

            res.Enum = info.Enum;

            JsonObject occupant = CostumeLibrary.FindByEnum(jsonPath, info.Enum);
            if (occupant != null)
            {
                ulong occupantId = CostumeLibrary.ParseHex(occupant["customId"] ?? occupant["prototypeId"]);
                string occupantName = (string)occupant["name"] ?? "(unnamed)";

                if (occupantId == customId)
                {
                    res.WasReinstall = true;
                    log?.Invoke($"  \"{occupantName}\" is already installed on enum {info.Enum} - reinstalling");
                }
                else
                {
                    res.Replaced = occupantName;
                    log?.Invoke($"  enum {info.Enum} is held by \"{occupantName}\", which the server has "
                              + "since reassigned - removing it");
                }

                PlayerResult drop = Uninstall(gameRoot, info.Enum, log, quiet: true);
                foreach (string s in drop.Steps) res.Steps.Add("replaced: " + s);
                if (!drop.Ok)
                {
                    res.FailedStep = "replace";
                    log?.Invoke("  ABORT: could not remove the costume already on this enum.");
                    return res;
                }
            }

            string upkOut = Path.Combine(cooked, info.UpkFileName);
            string tempUpk = upkOut + ".importing";

            try
            {
                using var zip = ZipFile.OpenRead(packPath);

                log?.Invoke($"[1/4] writing {info.UpkFileName}");
                if (File.Exists(tempUpk)) File.Delete(tempUpk);
                zip.GetEntry(info.UpkFileName).ExtractToFile(tempUpk, true);
                if (File.Exists(upkOut)) File.Delete(upkOut);
                File.Move(tempUpk, upkOut);
                res.Steps.Add($"wrote {info.UpkFileName}");

                string iconUpkPath = null;
                if (!string.IsNullOrWhiteSpace(info.IconUpk) && zip.GetEntry(info.IconUpk) != null)
                {
                    iconUpkPath = Path.Combine(cooked, IconPackBuilder.UpkFileNameForEnum(info.Enum));
                    zip.GetEntry(info.IconUpk).ExtractToFile(iconUpkPath, true);
                    res.Steps.Add("wrote " + Path.GetFileName(iconUpkPath));
                    log?.Invoke($"[2/4] writing {Path.GetFileName(iconUpkPath)}");
                }
                else if (info.Entry?["iconPackage"] != null)
                {
                    res.Warnings.Add("this costume declares custom icons but the pack has no icon "
                                   + "package - it will fall back to the donor's icons. Ask for a "
                                   + "pack exported by a newer Costume Manager.");
                    log?.Invoke("[2/4] no icon package in this pack");
                }
                else
                {
                    log?.Invoke("[2/4] no custom icons - the donor's are used");
                }

                var pairs = new List<AliasPair>(info.TfcAliasPairs);
                if (iconUpkPath != null)
                    pairs.Add(new AliasPair(IconPackBuilder.DonorPackageName,
                                            IconPackBuilder.PackageNameForEnum(info.Enum)));

                if (pairs.Count > 0 && File.Exists(manifestPath))
                {
                    log?.Invoke("[3/4] aliasing the TFC manifest");

                    ManifestDoctor.EnsureBaseline(manifestPath,
                        (CostumeLibrary.ReadCostumes(jsonPath)?.Count ?? 0) > 0, log);
                    Backup.Timestamped(manifestPath);
                    AliasResult ar = TfcEngine.Alias(manifestPath, manifestPath, pairs, log);
                    res.Steps.Add($"manifest: {ar.Added} row(s) added, {ar.Skipped} already present");

                    if (ar.Added == 0 && ar.Skipped == 0)
                        res.Warnings.Add("no manifest rows matched this pack's texture packages - "
                                       + "the costume may render untextured");
                }
                else if (pairs.Count > 0)
                {
                    res.Warnings.Add("TextureFileCacheManifest.bin not found, so texture aliases were "
                                   + "not applied - the costume may render untextured");
                    log?.Invoke("[3/4] manifest not found");
                }
                else
                {
                    log?.Invoke("[3/4] no texture aliases needed");
                }

                log?.Invoke($"[4/4] writing {CostumeConfig.InUseName(jsonPath)}");
                if (CostumeConfig.Exists(jsonPath)) Backup.Timestamped(CostumeConfig.ExistingPath(jsonPath));

                JsonObject fresh = info.Entry.DeepClone() as JsonObject;
                fresh["enum"] = info.Enum;
                fresh["customId"] = $"0x{customId:X16}";

                ParkFxIfPackMissing(cooked, fresh, info, res, log);

                CostumeLibrary.UpsertEntry(jsonPath, fresh, customId, info.Enum);
                res.Steps.Add("json: costume entry written");

                try
                {
                    InstallLedger.Upsert(new InstallRecord
                    {
                        Name = !string.IsNullOrWhiteSpace(info.Name) ? info.Name : info.DisplayName,
                        DisplayName = info.DisplayName,
                        Enum = info.Enum,
                        InstalledUtc = DateTime.UtcNow.ToString("o"),
                        UpkPath = upkOut,
                        CustomCostumesJson = jsonPath,
                        ManifestPath = manifestPath,
                        TfcPackage = info.TfcAliasPairs.FirstOrDefault()?.To,
                        TfcAliasPairs = new List<AliasPair>(info.TfcAliasPairs),
                        IconUpkPath = iconUpkPath,
                        IconPackage = (string)info.Entry["iconPackage"],
                    });
                    res.Steps.Add("ledger: recorded");
                }
                catch (Exception ex) { log?.Invoke("  (ledger write skipped: " + ex.Message + ")"); }
            }
            catch (Exception ex)
            {
                res.FailedStep = res.FailedStep ?? "import";
                log?.Invoke("Import failed: " + ex.Message);
                try { if (File.Exists(tempUpk)) File.Delete(tempUpk); } catch { }
                return res;
            }

            foreach (string w in res.Warnings) log?.Invoke("  ⚠ " + w);
            res.Ok = true;
            log?.Invoke("");
            log?.Invoke($"DONE. \"{info.DisplayName}\" installed on enum {info.Enum}. "
                      + "Restart the game client to see it.");
            return res;
        }

        public static PlayerResult Uninstall(string gameRoot, uint enumId, Action<string> log = null,
                                             bool quiet = false)
        {
            var res = new PlayerResult { Enum = enumId };
            var (cooked, manifestPath, bin) = GamePaths.Resolve(gameRoot);
            string jsonPath = Path.Combine(bin, "CustomCostumes.json");

            void Say(string s) { if (!quiet) log?.Invoke(s); }

            try
            {
                InstallRecord rec = InstallLedger.Read().FirstOrDefault(r => r.Enum == enumId);
                JsonObject entry = CostumeLibrary.FindByEnum(jsonPath, enumId);

                string upk = rec?.UpkPath;
                if (!string.IsNullOrWhiteSpace(upk) && !IsInside(upk, cooked)) upk = null;

                if (string.IsNullOrWhiteSpace(upk) || !File.Exists(upk))
                {

                    string named = (string)entry?["upk"] ?? (string)entry?["package"];
                    if (string.IsNullOrWhiteSpace(named) && !string.IsNullOrWhiteSpace(rec?.UpkPath))
                        named = Path.GetFileName(rec.UpkPath);

                    if (!string.IsNullOrWhiteSpace(named))
                    {
                        if (!named.EndsWith(".upk", StringComparison.OrdinalIgnoreCase)) named += ".upk";
                        upk = Path.Combine(cooked, named);
                    }
                }
                if (!string.IsNullOrWhiteSpace(upk) && File.Exists(upk))
                {
                    File.Delete(upk);
                    Say($"[1/4] deleted {Path.GetFileName(upk)}");
                    res.Steps.Add("deleted costume UPK");
                }
                else Say("[1/4] costume UPK already absent");

                string iconUpk = rec?.IconUpkPath;
                if (!string.IsNullOrWhiteSpace(iconUpk) && !IsInside(iconUpk, cooked)) iconUpk = null;
                if (string.IsNullOrWhiteSpace(iconUpk))
                    iconUpk = Path.Combine(cooked, IconPackBuilder.UpkFileNameForEnum(enumId));
                if (File.Exists(iconUpk))
                {
                    File.Delete(iconUpk);
                    Say($"[2/4] deleted {Path.GetFileName(iconUpk)}");
                    res.Steps.Add("deleted icon UPK");
                }
                else Say("[2/4] no icon UPK");

                if (File.Exists(manifestPath))
                {
                    var fallbacks = new List<string>();
                    if (!string.IsNullOrWhiteSpace(rec?.TfcPackage)) fallbacks.Add(rec.TfcPackage);
                    foreach (AliasPair p in rec?.TfcAliasPairs ?? new List<AliasPair>())
                        if (!string.IsNullOrWhiteSpace(p.To)) fallbacks.Add(p.To);
                    fallbacks.Add(IconPackBuilder.PackageNameForEnum(enumId));

                    var donorPkgs = (rec?.TfcAliasPairs ?? new List<AliasPair>())
                        .Where(p => !string.IsNullOrWhiteSpace(p.From)).Select(p => p.From).ToList();
                    donorPkgs.Add(IconPackBuilder.DonorPackageName);

                    Backup.Timestamped(manifestPath);
                    int removed = 0;
                    foreach (string pkg in fallbacks.Distinct(StringComparer.OrdinalIgnoreCase))
                        removed += TfcEngine.Unalias(manifestPath, manifestPath,
                                                     rec?.TfcAliasRows, pkg, quiet ? null : log,
                                                     fallbacks, donorPkgs);
                    Say($"[3/4] removed {removed} manifest row(s)");
                    res.Steps.Add($"manifest: -{removed} rows");

                    ManifestDoctor.GuardAfterRemoval(manifestPath, quiet ? null : log);
                }
                else Say("[3/4] manifest not found");

                string cfgName = CostumeConfig.InUseName(jsonPath);
                if (CostumeLibrary.RemoveEntry(jsonPath, enumId) != null)
                {
                    Say($"[4/4] removed the {cfgName} entry");
                    res.Steps.Add("removed config entry");
                }
                else Say($"[4/4] no {cfgName} entry");

                try
                {
                    if (rec?.Name != null) { InstallLedger.Remove(rec.Name); res.Steps.Add("removed ledger record"); }
                }
                catch { }

                res.Ok = true;
                return res;
            }
            catch (Exception ex)
            {
                res.FailedStep = "uninstall";
                log?.Invoke("Uninstall failed: " + ex.Message);
                return res;
            }
        }

        static bool AnyEntryNeedsDonorTable(string jsonPath)
        {
            JsonArray arr = CostumeLibrary.ReadCostumes(jsonPath);
            if (arr == null) return false;
            foreach (var n in arr)
                if (n is JsonObject o && o["donorAsset"] == null) return true;
            return false;
        }

        static bool IsInside(string file, string folder)
        {
            try
            {
                string f = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                return Path.GetFullPath(file).StartsWith(f, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static List<string> Verify(string gameRoot)
        {
            var problems = new List<string>();
            var (cooked, manifestPath, bin) = GamePaths.Resolve(gameRoot);

            if (!Directory.Exists(cooked))
            {
                problems.Add($"CookedPCConsole not found under \"{gameRoot}\" - is this the game folder?");
                return problems;
            }

            string jsonPath = Path.Combine(bin, "CustomCostumes.json");
            string donorJson = Path.Combine(bin, "Costumes.json");

            int recorded = 0;
            try
            {
                var (_, _, thisBin) = GamePaths.Resolve(gameRoot);
                recorded = InstallLedger.Read().Count(r => IsInside(r.UpkPath, thisBin)
                                                        || IsInside(r.UpkPath, cooked));
            }
            catch { }

            if (!CostumeConfig.Exists(jsonPath) && recorded > 0)
                problems.Add($"no costume list in {bin} (CustomCostumes.mhc), but {recorded} "
                           + "costume(s) are recorded as installed here - the list has been lost.");

            if (!File.Exists(donorJson) && AnyEntryNeedsDonorTable(jsonPath))
                problems.Add($"Costumes.json not found in {bin}, and some costumes rely on it to "
                           + "resolve their donor. Those costumes will fail silently. Re-import them "
                           + "from an up-to-date pack, or ask your admin for that file.");

            foreach (string f in new[] { jsonPath, donorJson })
            {
                if (!File.Exists(f)) continue;
                try { JsonNode.Parse(File.ReadAllText(f)); }
                catch (Exception ex) { problems.Add($"{Path.GetFileName(f)} does not parse: {ex.Message}"); }
            }

            ManifestReport mrep = ManifestDoctor.Check(manifestPath);
            foreach (string p in mrep.Problems)
                problems.Add("texture manifest: " + p);

            if (mrep.NoBaseline && recorded > 0)
                problems.Add("texture manifest: no pristine backup beside it, so rows lost from "
                           + "STOCK cannot be detected. It is taken automatically the first time "
                           + "a costume is installed.");

            if (!CostumeConfig.Exists(jsonPath)) return problems;

            List<InstalledCostume> installed = CostumeLibrary.ListInstalled(gameRoot);

            foreach (var group in installed.GroupBy(c => c.Enum).Where(g => g.Count() > 1))
                problems.Add($"enum {group.Key} is used by {group.Count()} costumes "
                           + $"({string.Join(", ", group.Select(c => c.DisplayName))}) - "
                           + "only one can work; the others are unreachable.");

            TfcManifest manifest = null;
            if (File.Exists(manifestPath))
            {
                try { manifest = TfcManifest.Load(manifestPath); } catch { }
            }

            foreach (InstalledCostume c in installed)
            {
                if (!string.IsNullOrWhiteSpace(c.Upk) && !File.Exists(Path.Combine(cooked, c.Upk)))
                    problems.Add($"{c.DisplayName}: {c.Upk} is missing from CookedPCConsole.");

                if (!string.IsNullOrWhiteSpace(c.IconPackage))
                {
                    string iconUpk = Path.Combine(cooked, IconPackBuilder.UpkFileNameForEnum(c.Enum));
                    if (!File.Exists(iconUpk))
                        problems.Add($"{c.DisplayName}: declares custom icons but "
                                   + $"{Path.GetFileName(iconUpk)} is missing.");
                    else if (manifest != null &&
                             !manifest.Entries.Any(e => string.Equals(e.PackageName,
                                 IconPackBuilder.PackageNameForEnum(c.Enum), StringComparison.OrdinalIgnoreCase)))
                        problems.Add($"{c.DisplayName}: no TFC rows for its icon package "
                                   + $"({IconPackBuilder.PackageNameForEnum(c.Enum)}) - icons may not render.");
                }

                if (!c.InLedger)
                    problems.Add($"{c.DisplayName}: no install record, so it cannot be cleanly uninstalled "
                               + "by this program (it was installed by something else).");
            }

            VerifyFx(gameRoot, cooked, jsonPath, problems);
            return problems;
        }

        static void VerifyFx(string gameRoot, string cooked, string jsonPath, List<string> problems)
        {

            const int NameAtMost = 3;

            JsonObject root;
            try
            {
                string text = CostumeConfig.ReadAllText(jsonPath);
                if (text == null) return;
                root = JsonNode.Parse(text) as JsonObject;
            }
            catch { return; }
            if (root == null) return;

            List<FxPack> packs;
            try { packs = FxPackRegistry.Read(FxPackInstall.RegistryPathFor(gameRoot)); }
            catch { packs = new List<FxPack>(); }

            bool Missing(string pkg)
            {
                if (string.IsNullOrWhiteSpace(pkg)) return false;
                string file = pkg.EndsWith(".upk", StringComparison.OrdinalIgnoreCase) ? pkg : pkg + ".upk";
                return !File.Exists(Path.Combine(cooked, file));
            }

            string Summarise(List<string> missing)
            {
                string named = string.Join(", ", missing.Take(NameAtMost));
                return missing.Count > NameAtMost
                    ? $"{named}, +{missing.Count - NameAtMost} more"
                    : named;
            }

            foreach (string key in new[] { "costumes", "disabled" })
            {
                if (root[key] is not JsonArray arr) continue;

                foreach (JsonNode n in arr)
                {
                    if (n is not JsonObject o) continue;
                    string name = (string)o["displayName"] ?? (string)o["name"] ?? "(unnamed)";

                    if (o[FxPackInstall.PendingKey] != null) continue;

                    var gone = new List<string>();
                    if (o["chain"] is JsonArray chain)
                        foreach (JsonNode c in chain)
                        {
                            string pkg = c?.ToString();
                            if (Missing(pkg)) gone.Add(pkg);
                        }

                    if (o["effects"] is JsonArray fx)
                        foreach (JsonNode f in fx)
                        {
                            string pkg = (f as JsonObject)?["package"]?.ToString();
                            if (Missing(pkg) && !gone.Contains(pkg)) gone.Add(pkg);
                        }

                    if (gone.Count > 0)
                        problems.Add($"{name}: {gone.Count} effect package(s) named in its load chain "
                                   + $"are missing from CookedPCConsole ({Summarise(gone)}). "
                                   + "This costume will NOT appear at all - the donor renders "
                                   + "instead. Re-import its effects pack.");

                    string token = (string)o["fxPack"];
                    if (!string.IsNullOrWhiteSpace(token)
                        && !packs.Any(p => string.Equals(p.Token, token, StringComparison.OrdinalIgnoreCase)))
                        problems.Add($"{name}: uses effects pack \"{token}\", which is not installed "
                                   + "here. Install that pack, or remove and re-import the costume.");
                }
            }

            foreach (FxPack p in packs)
            {
                var gone = new List<string>();

                foreach (FxRecord r in p.Effects ?? new List<FxRecord>())
                    if (Missing(r?.Package)) gone.Add(r.Package);

                var goneParents = new List<string>();
                foreach (string par in p.Parents ?? new List<string>())
                    if (Missing(par)) goneParents.Add(par);

                if (gone.Count > 0)
                    problems.Add($"effects pack \"{p.Token}\": {gone.Count} of "
                               + $"{p.Effects?.Count ?? 0} package(s) are missing from "
                               + $"CookedPCConsole ({Summarise(gone)}). Every costume using this "
                               + "pack will fail to appear. Re-import the pack.");

                if (goneParents.Count > 0)
                    problems.Add($"effects pack \"{p.Token}\": {goneParents.Count} PARENT package(s) "
                               + $"missing ({Summarise(goneParents)}). These ship with the game, so "
                               + "this usually means the game folder is incomplete rather than the "
                               + "pack being at fault.");
            }
        }

        static void ParkFxIfPackMissing(string cooked, JsonObject fresh, CostumePackInfo info,
                                        PlayerResult res, Action<string> log)
        {
            if (fresh?["effects"] is not JsonArray effects || effects.Count == 0) return;

            var missing = new List<string>();
            foreach (JsonNode n in effects)
                if (n is JsonObject o && o["package"] != null)
                {
                    string file = o["package"].ToString();
                    if (!file.EndsWith(".upk", StringComparison.OrdinalIgnoreCase)) file += ".upk";
                    if (!File.Exists(Path.Combine(cooked, file))) missing.Add(file);
                }

            if (missing.Count == 0) return;

            var owned = new List<string>();
            foreach (JsonNode n in effects)
                if (n is JsonObject o && o["package"] != null) owned.Add(o["package"].ToString());

            if (fresh["chain"] is JsonArray chain)
                for (int i = 1; i < chain.Count; i++)
                {
                    string p = chain[i]?.ToString();
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    string file = p.EndsWith(".upk", StringComparison.OrdinalIgnoreCase) ? p : p + ".upk";
                    if (!File.Exists(Path.Combine(cooked, file))) owned.Add(p);
                }

            string token = info.FxPackToken;
            if (FxPackInstall.Park(fresh, owned, token))
            {
                string named = string.IsNullOrWhiteSpace(token) ? "its FX pack" : $"FX pack \"{token}\"";
                res.Warnings.Add($"{missing.Count} effect package(s) are not installed, so this "
                               + $"costume was installed with STOCK effects. Import {named} and "
                               + "its custom effects turn on automatically - nothing was lost.");
                log?.Invoke($"      effects parked - {named} is not installed here");
                res.Steps.Add("json: effects parked pending " + named);
            }
        }
    }

    public static class HashName
    {
        public static ulong CustomId(string customName) => Compute("custom\\" + customName);

        public static ulong Compute(string s)
        {
            byte[] b = System.Text.Encoding.UTF8.GetBytes(s.ToLowerInvariant());
            uint adler = Adler32(b, 1);
            uint crc = Crc32(b, 0);
            ulong combined = ((ulong)adler) | (((ulong)crc) << 32);
            return combined - 1UL;
        }

        static uint Adler32(byte[] data, uint seed)
        {
            const uint MOD = 65521;
            uint a = seed & 0xFFFF;
            uint b = (seed >> 16) & 0xFFFF;
            foreach (byte t in data)
            {
                a = (a + t) % MOD;
                b = (b + a) % MOD;
            }
            return (b << 16) | a;
        }

        static readonly uint[] CrcTable = BuildCrcTable();
        static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        static uint Crc32(byte[] data, uint seed)
        {
            uint c = seed ^ 0xFFFFFFFFu;
            foreach (byte b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }
    }
}
