using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CostumeManager.Core
{

    public sealed class ConditionFamily
    {

        public string Name { get; set; }

        public ulong AssetId { get; set; }

        public List<string> Heroes { get; set; } = new List<string>();

        public Dictionary<string, string> Variants { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public static class ConditionFamilies
    {
        public const string FileName = "ConditionFamilies.json";

        static readonly object _lock = new object();
        static Dictionary<string, ConditionFamily> _byName;
        static bool _loaded;

        public static bool Loaded { get { Ensure(null); return _loaded; } }

        static void Ensure(string path)
        {
            lock (_lock)
            {
                if (_byName != null) return;
                _byName = new Dictionary<string, ConditionFamily>(StringComparer.OrdinalIgnoreCase);

                string p = path;
                if (string.IsNullOrWhiteSpace(p))
                    p = Path.Combine(AppContext.BaseDirectory, FileName);
                if (!File.Exists(p)) return;

                try
                {
                    if (JsonNode.Parse(File.ReadAllText(p)) is not JsonObject root) return;
                    if (root["families"] is not JsonObject fams) return;

                    foreach (var kv in fams)
                    {
                        if (kv.Value is not JsonObject f) continue;
                        var fam = new ConditionFamily
                        {
                            Name = kv.Key,
                            AssetId = CostumeLibrary.ParseHex(f["assetId"]),
                        };
                        foreach (JsonNode h in (f["heroes"] as JsonArray) ?? new JsonArray())
                        {
                            string hs = (string)h;
                            if (!string.IsNullOrWhiteSpace(hs)) fam.Heroes.Add(hs);
                        }
                        if (f["variants"] is JsonObject vs)
                            foreach (var v in vs)
                            {
                                string file = (string)v.Value;
                                if (!string.IsNullOrWhiteSpace(file)) fam.Variants[v.Key] = file;
                            }
                        _byName[fam.Name] = fam;
                    }
                    _loaded = true;
                }
                catch {  }
            }
        }

        public static List<ConditionFamily> For(string hero, string donorToken)
        {
            Ensure(null);
            var hits = new List<ConditionFamily>();
            if (string.IsNullOrWhiteSpace(hero) || string.IsNullOrWhiteSpace(donorToken)) return hits;

            foreach (ConditionFamily f in _byName.Values)
                if (f.Variants.ContainsKey(donorToken) &&
                    f.Heroes.Any(h => string.Equals(h, hero, StringComparison.OrdinalIgnoreCase)))
                    hits.Add(f);

            return hits.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static void LoadFrom(string path)
        {
            lock (_lock) { _byName = null; _loaded = false; }
            Ensure(path);
        }
    }
}
