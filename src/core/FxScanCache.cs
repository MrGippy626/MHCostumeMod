using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CostumeManager.Core
{

    public sealed class FxScanCache
    {
        public sealed class Item
        {
            public string File { get; set; }
            public string SourcePath { get; set; }
            public string StockStem { get; set; }
            public string FromHex { get; set; }
            public string Kind { get; set; }
            public string EffectName { get; set; }
            public bool Installable { get; set; }
            public string SkipReason { get; set; }
            public string Compat { get; set; }
            public string CompatReason { get; set; }
            public string Note { get; set; }
            public long Bytes { get; set; }

            public List<string> ClassExports { get; set; }
            public string PrimaryClass { get; set; }

            public ulong From
            {
                get
                {
                    if (string.IsNullOrWhiteSpace(FromHex)) return 0UL;
                    string s = FromHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                             ? FromHex.Substring(2) : FromHex;
                    return ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber,
                                          System.Globalization.CultureInfo.InvariantCulture,
                                          out ulong v) ? v : 0UL;
                }
            }
        }

        public sealed class Scan
        {
            public uint Enum { get; set; }
            public string Folder { get; set; }
            public string ScannedUtc { get; set; }
            public List<Item> Items { get; set; } = new List<Item>();

            public bool SourcesPresent
            {
                get { return Items.All(i => string.IsNullOrEmpty(i.SourcePath) || File.Exists(i.SourcePath)); }
            }

            public int MissingSources
            {
                get { return Items.Count(i => !string.IsNullOrEmpty(i.SourcePath) && !File.Exists(i.SourcePath)); }
            }
        }

        public static string DefaultPath(string managerDir = null)
        {
            string dir = managerDir;
            if (string.IsNullOrWhiteSpace(dir))
                dir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(dir, "fxscans.json");
        }

        public static Dictionary<uint, Scan> Read(string path = null)
        {
            var outp = new Dictionary<uint, Scan>();
            string p = path ?? DefaultPath();
            if (!File.Exists(p)) return outp;
            try
            {
                JsonNode root = JsonNode.Parse(File.ReadAllText(p));
                JsonArray arr = root?["scans"] as JsonArray;
                if (arr == null) return outp;
                foreach (JsonNode n in arr)
                {
                    var s = n.Deserialize<Scan>();
                    if (s != null && s.Enum != 0) outp[s.Enum] = s;
                }
            }
            catch {  }
            return outp;
        }

        public static void Write(Dictionary<uint, Scan> scans, string path = null)
        {
            string p = path ?? DefaultPath();
            var root = new JsonObject
            {
                ["scans"] = new JsonArray(
                    scans.Values.OrderBy(s => s.Enum)
                         .Select(s => JsonSerializer.SerializeToNode(s))
                         .ToArray())
            };
            File.WriteAllText(p, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        public static Scan Get(uint enumId, string path = null)
        {
            Read(path).TryGetValue(enumId, out Scan s);
            return s;
        }

        public static void Save(Scan scan, string path = null)
        {
            if (scan == null || scan.Enum == 0) return;
            Dictionary<uint, Scan> all = Read(path);
            all[scan.Enum] = scan;
            Write(all, path);
        }

        public static void Forget(uint enumId, string path = null)
        {
            Dictionary<uint, Scan> all = Read(path);
            if (all.Remove(enumId)) Write(all, path);
        }

        public static Scan FromCandidates(uint enumId, string folder, List<FxCandidate> cands)
        {
            var s = new Scan
            {
                Enum = enumId,
                Folder = folder,
                ScannedUtc = DateTime.UtcNow.ToString("o"),
            };
            foreach (FxCandidate c in cands ?? new List<FxCandidate>())
            {
                long bytes = 0;
                try { bytes = new FileInfo(c.SourcePath).Length; } catch { }
                s.Items.Add(new Item
                {
                    File = c.FileName,
                    SourcePath = c.SourcePath,
                    StockStem = c.StockStem,
                    FromHex = c.FromAsset != 0 ? "0x" + c.FromAsset.ToString("X16") : null,
                    Kind = c.Kind,
                    EffectName = c.Record != null ? c.Record.Name : null,
                    Installable = c.Installable,
                    SkipReason = c.SkipReason,
                    Compat = c.Compat.ToString(),
                    CompatReason = c.CompatReason,
                    Note = c.Bulk != null ? c.Bulk.Note : null,
                    Bytes = bytes,
                    ClassExports = c.AllClassExports != null && c.AllClassExports.Count > 0
                                 ? new List<string>(c.AllClassExports) : null,
                    PrimaryClass = c.ClassLeaf,
                });
            }
            return s;
        }

        public static async System.Threading.Tasks.Task<List<FxCandidate>> RehydrateAsync(
            Scan scan, IEnumerable<string> fileNames, EffectTables tables,
            string cookedDir, Action<string> log = null, string hero = null)
        {
            var want = new HashSet<string>(fileNames ?? Enumerable.Empty<string>(),
                                           StringComparer.OrdinalIgnoreCase);
            var paths = new List<string>();
            foreach (Item i in scan.Items)
            {
                if (!want.Contains(i.File)) continue;
                if (string.IsNullOrEmpty(i.SourcePath) || !File.Exists(i.SourcePath))
                {
                    if (log != null) log("  ⚠ source file is gone, skipping: " + i.File);
                    continue;
                }
                paths.Add(i.SourcePath);
            }
            return await FxScanner.ScanFilesAsync(paths, tables, cookedDir, log, hero);
        }
    }
}
