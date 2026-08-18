using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CostumeManager.Core
{

    public sealed class EffectRecord
    {
        public string Name { get; set; }
        public ulong AssetId { get; set; }
        public string Kind { get; set; }
        public string Proto { get; set; }
        public string Upk { get; set; }

        public List<string> Protos { get; } = new List<string>();

        public List<ulong> Summons { get; } = new List<ulong>();

        public string AssetIdHex { get { return "0x" + AssetId.ToString("X16"); } }

        public override string ToString() { return Name; }
    }

    public sealed class EffectTables
    {
        public Dictionary<string, EffectRecord> ByName { get; } =
            new Dictionary<string, EffectRecord>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, EffectRecord> ByUpkFileName { get; } =
            new Dictionary<string, EffectRecord>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<ulong, EffectRecord> ByAssetId { get; } =
            new Dictionary<ulong, EffectRecord>();

        public HashSet<string> KnownHeroes { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public int Count { get { return ByName.Count; } }
        public bool IsEmpty { get { return ByName.Count == 0; } }

        public bool IsStockUpkFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            return ByUpkFileName.ContainsKey(Path.GetFileName(fileName));
        }

        public static string DefaultPath()
        {
            return Path.Combine(AppContext.BaseDirectory, "Effects.json");
        }

        public static EffectTables Load(string effectsJson)
        {
            var t = new EffectTables();
            if (string.IsNullOrWhiteSpace(effectsJson) || !File.Exists(effectsJson)) return t;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(File.ReadAllText(effectsJson)); }
            catch { return t; }

            using (doc)
            {
                JsonElement obj;
                if (!doc.RootElement.TryGetProperty("effects", out obj)) return t;
                if (obj.ValueKind != JsonValueKind.Object) return t;

                foreach (JsonProperty kv in obj.EnumerateObject())
                {
                    if (kv.Value.ValueKind != JsonValueKind.Object) continue;

                    var rec = new EffectRecord { Name = kv.Name };

                    JsonElement a;
                    ulong av;
                    if (!kv.Value.TryGetProperty("assetId", out a)) continue;
                    if (!TryHex(a.GetString(), out av) || av == 0) continue;
                    rec.AssetId = av;

                    JsonElement k;
                    if (kv.Value.TryGetProperty("kind", out k) && k.ValueKind == JsonValueKind.String)
                        rec.Kind = k.GetString();

                    JsonElement p;
                    if (kv.Value.TryGetProperty("proto", out p) && p.ValueKind == JsonValueKind.String)
                        rec.Proto = p.GetString();

                    JsonElement ps;
                    if (kv.Value.TryGetProperty("protos", out ps) && ps.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement pe in ps.EnumerateArray())
                            if (pe.ValueKind == JsonValueKind.String)
                            {
                                string s = pe.GetString();
                                if (!string.IsNullOrWhiteSpace(s) && !rec.Protos.Contains(s))
                                    rec.Protos.Add(s);
                            }
                    }
                    if (rec.Protos.Count == 0 && !string.IsNullOrWhiteSpace(rec.Proto))
                        rec.Protos.Add(rec.Proto);

                    JsonElement sm;
                    if (kv.Value.TryGetProperty("summons", out sm) && sm.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement se in sm.EnumerateArray())
                        {
                            if (se.ValueKind != JsonValueKind.String) continue;
                            string s = se.GetString();
                            if (string.IsNullOrWhiteSpace(s)) continue;
                            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
                            ulong v;
                            if (ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber,
                                               System.Globalization.CultureInfo.InvariantCulture, out v)
                                && v != 0 && !rec.Summons.Contains(v))
                                rec.Summons.Add(v);
                        }
                    }

                    JsonElement u;
                    if (kv.Value.TryGetProperty("upk", out u) && u.ValueKind == JsonValueKind.String)
                        rec.Upk = u.GetString();

                    t.ByName[rec.Name] = rec;
                    if (!t.ByAssetId.ContainsKey(rec.AssetId)) t.ByAssetId[rec.AssetId] = rec;

                    if (!string.IsNullOrWhiteSpace(rec.Upk))
                    {
                        string leaf = Path.GetFileName(rec.Upk);
                        if (!t.ByUpkFileName.ContainsKey(leaf)) t.ByUpkFileName[leaf] = rec;
                    }

                    string hero = HeroFromProto(rec.Proto);
                    if (hero != null) t.KnownHeroes.Add(hero);
                }
            }
            return t;
        }

        public static string HeroFromProto(string proto)
        {
            if (string.IsNullOrWhiteSpace(proto)) return null;
            string[] parts = proto.Replace('/', '\\').Split('\\');
            for (int i = 0; i + 2 < parts.Length; i++)
                if (string.Equals(parts[i], "Powers", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(parts[i + 1], "Player", StringComparison.OrdinalIgnoreCase))
                    return parts[i + 2].ToLowerInvariant();
            return null;
        }

        static bool TryHex(string s, out ulong v)
        {
            v = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            return ulong.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
        }
    }

    public static class FxCrc32
    {

        internal static readonly uint[] Table = BuildTable();

        static uint[] BuildTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int i = 0; i < 8; i++)
                    c = ((c & 1) != 0) ? ((c >> 1) ^ 0xEDB88320u) : (c >> 1);
                t[n] = c;
            }
            return t;
        }

        public static uint ComputeFile(string path)
        {
            uint c = uint.MaxValue;
            using (FileStream fs = File.OpenRead(path))
            {
                var buf = new byte[81920];
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                    for (int i = 0; i < n; i++)
                        c = Table[(c ^ buf[i]) & 0xFF] ^ (c >> 8);
            }
            return c ^ 0xFFFFFFFFu;
        }

        public static string ComputeFileHex(string path)
        {
            return ComputeFile(path).ToString("X8");
        }

        public static bool FilesAreIdentical(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            if (!File.Exists(a) || !File.Exists(b)) return false;

            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            if (ComputeFile(a) != ComputeFile(b)) return false;

            using (FileStream sa = File.OpenRead(a))
            using (FileStream sb = File.OpenRead(b))
            {
                var ba = new byte[65536];
                var bb = new byte[65536];
                while (true)
                {
                    int na = ReadFull(sa, ba);
                    int nb = ReadFull(sb, bb);
                    if (na != nb) return false;
                    if (na == 0) return true;
                    for (int i = 0; i < na; i++)
                        if (ba[i] != bb[i]) return false;
                }
            }
        }

        static int ReadFull(Stream s, byte[] buf)
        {
            int total = 0;
            while (total < buf.Length)
            {
                int n = s.Read(buf, total, buf.Length - total);
                if (n <= 0) break;
                total += n;
            }
            return total;
        }
    }
}
