using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;

namespace CostumeManager.Core
{

    public static class FxHotspots
    {

        const ulong Salt = 0x0757_C057_0757_C057;

        public static ulong ProtoIdFromPath(string calligraphyRelativePath)
        {
            if (string.IsNullOrWhiteSpace(calligraphyRelativePath)) return 0;

            string p = calligraphyRelativePath.Replace('\\', '/')
                                              .Replace('.', '?')
                                              .Replace('/', '.')
                                              .ToLowerInvariant();
            byte[] b = Encoding.UTF8.GetBytes(p);
            ulong v = ((ulong)Adler32(b)) | ((ulong)Crc32(b) << 32);
            return unchecked(v - 1);
        }

        static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            for (int i = 0; i < data.Length; i++)
            {
                a = (a + data[i]) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        static uint Crc32(byte[] data)
        {
            uint c = uint.MaxValue;
            for (int i = 0; i < data.Length; i++)
                c = FxCrc32.Table[(c ^ data[i]) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }

        public static ulong Forge(ulong stockProtoId, uint costumeEnum)
        {
            return unchecked(stockProtoId ^ Salt ^ ((ulong)costumeEnum << 40));
        }

        public const uint EnumBase = 200000;
        public const uint CustomEnumBase = 100000;
        public const int MaxHotspotsPerCostume = 64;

        public static uint EnumFor(uint costumeEnum, int index)
        {
            if (costumeEnum < CustomEnumBase) return 0;
            if (index < 0 || index >= MaxHotspotsPerCostume) return 0;
            return EnumBase + (costumeEnum - CustomEnumBase) * (uint)MaxHotspotsPerCostume + (uint)index;
        }

        static readonly (string Path, ulong Id)[] Controls =
        {
            (@"Entity\PowerEntities\PrototypesHotspot\GambitStreetSweepTrailArea.prototype",
             0x6F5A69B9A31A1F4CUL),
            (@"Powers\Player\Gambit\RainInPainArea.prototype",
             0xD75B881F9CD81222UL),
        };

        public static bool SelfTest(out string detail)
        {
            var bad = new List<string>();
            foreach ((string path, ulong want) in Controls)
            {
                ulong got = ProtoIdFromPath(path);
                if (got != want)
                    bad.Add($"{path}: got 0x{got:X16}, expected 0x{want:X16}");
            }
            detail = bad.Count == 0
                ? $"prototype hash OK ({Controls.Length}/{Controls.Length} controls)"
                : "prototype hash FAILED: " + string.Join("; ", bad);
            return bad.Count == 0;
        }

        public sealed class Pair
        {
            public string EffectName;
            public string ProtoPath;
            public ulong Stock;
            public ulong Forged;
            public uint Enum;
        }

        public static bool IsHotspot(EffectRecord r)
        {
            if (r == null || string.IsNullOrWhiteSpace(r.Name)) return false;
            if (!string.Equals(r.Kind, "entity", StringComparison.OrdinalIgnoreCase)) return false;
            return r.Name.StartsWith("MarvelEntity_Hotspot", StringComparison.OrdinalIgnoreCase);
        }

        public static List<Pair> BuildPairs(IEnumerable<ulong> fromAssetIds, EffectTables tables,
                                            uint costumeEnum, Action<string> log = null)
        {
            var pairs = new List<Pair>();
            if (fromAssetIds == null || tables == null) return pairs;

            string detail;
            if (!SelfTest(out detail))
            {
                if (log != null) log("  ⚠ " + detail + " - no hotspot ids will be written");
                return pairs;
            }

            var seen = new HashSet<ulong>();
            bool full = false;

            for (int pass = 0; pass < 2 && !full; pass++)
            {
            foreach (ulong from in fromAssetIds)
            {
                if (full) break;

                EffectRecord r;
                if (!tables.ByAssetId.TryGetValue(from, out r) || r == null) continue;
                if (!IsHotspot(r)) continue;

                int which = -1;
                foreach (string protoPath in ProtosOf(r))
                {
                    which++;

                    if ((pass == 0) != (which == 0)) continue;
                    ulong stock = ProtoIdFromPath(protoPath);
                    if (stock == 0 || !seen.Add(stock)) continue;

                    ulong forged = Forge(stock, costumeEnum);
                    if (forged == stock) continue;

                    uint hsEnum = EnumFor(costumeEnum, pairs.Count);
                    if (hsEnum == 0)
                    {

                        if (log != null)
                            log($"  ⚠ more than {MaxHotspotsPerCostume} hotspot prototypes on one " +
                                $"costume - \"{r.Name}\" and any after it were skipped");
                        full = true;
                        break;
                    }

                    pairs.Add(new Pair
                    {
                        EffectName = r.Name,
                        ProtoPath = protoPath,
                        Stock = stock,
                        Forged = forged,
                        Enum = hsEnum,
                    });
                }
            }
            }

            foreach (ulong from in fromAssetIds)
            {
                if (full) break;

                EffectRecord r;
                if (!tables.ByAssetId.TryGetValue(from, out r) || r == null) continue;
                if (r.Summons == null || r.Summons.Count == 0) continue;

                foreach (ulong stock in r.Summons)
                {
                    if (stock == 0 || !seen.Add(stock)) continue;

                    ulong forged = Forge(stock, costumeEnum);
                    if (forged == stock) continue;

                    uint hsEnum = EnumFor(costumeEnum, pairs.Count);
                    if (hsEnum == 0)
                    {
                        if (log != null)
                            log($"  ⚠ more than {MaxHotspotsPerCostume} forgeable prototypes on one " +
                                $"costume - \"{r.Name}\" summon targets after this were skipped");
                        full = true;
                        break;
                    }

                    pairs.Add(new Pair
                    {
                        EffectName = r.Name,

                        ProtoPath = "(summoned by " + r.Name + ")",
                        Stock = stock,
                        Forged = forged,
                        Enum = hsEnum,
                    });
                }
            }

            return pairs;
        }

        static IEnumerable<string> ProtosOf(EffectRecord r)
        {
            if (r.Protos != null && r.Protos.Count > 0)
            {
                foreach (string p in r.Protos)
                    if (!string.IsNullOrWhiteSpace(p)) yield return p;
            }
            else if (!string.IsNullOrWhiteSpace(r.Proto))
            {
                yield return r.Proto;
            }
        }

        public static JsonArray ToJson(List<Pair> pairs)
        {
            var arr = new JsonArray();
            if (pairs == null) return arr;
            foreach (Pair p in pairs)
            {
                arr.Add(new JsonObject
                {
                    ["effect"] = p.EffectName,
                    ["proto"] = p.ProtoPath,
                    ["stock"] = "0x" + p.Stock.ToString("X16"),
                    ["forged"] = "0x" + p.Forged.ToString("X16"),
                    ["enum"] = p.Enum,
                });
            }
            return arr;
        }

        public static int Sync(JsonObject target, EffectTables tables, Action<string> log = null)
        {
            if (target == null) return 0;

            uint costumeEnum = 0;
            JsonNode en = target["enum"];
            if (en != null) { try { costumeEnum = en.GetValue<uint>(); } catch { } }

            var froms = new List<ulong>();
            if (costumeEnum != 0 && target["effects"] is JsonArray fx)
            {
                foreach (JsonNode n in fx)
                {
                    JsonObject e = n as JsonObject;
                    if (e == null || e["from"] == null) continue;
                    string hex = e["from"].ToString().Trim();
                    if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex.Substring(2);
                    ulong v;
                    if (ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out v) && v != 0)
                        froms.Add(v);
                }
            }

            List<Pair> pairs = BuildPairs(froms, tables, costumeEnum, log);
            if (pairs.Count == 0)
            {
                target.Remove("hotspots");
                return 0;
            }

            target["hotspots"] = ToJson(pairs);
            if (log != null)
                foreach (Pair p in pairs)
                    log($"  [hotspot] {p.EffectName}: 0x{p.Stock:X16} -> 0x{p.Forged:X16}  (enum {p.Enum})");
            return pairs.Count;
        }
    }
}
