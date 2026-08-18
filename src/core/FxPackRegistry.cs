using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace CostumeManager.Core
{

    public sealed class FxPack
    {

        public string Token { get; set; }

        public string DisplayName { get; set; }

        public string Hero { get; set; }

        public string SourceFolder { get; set; }

        public string InstalledUtc { get; set; }

        public List<FxRecord> Effects { get; set; } = new List<FxRecord>();

        public List<string> Parents { get; set; } = new List<string>();

        public override string ToString() => DisplayName ?? Token;
    }

    public static class FxPackRegistry
    {
        public static string DefaultPath =>
            Path.Combine(AppContext.BaseDirectory, "fxpacks.json");

        public static List<FxPack> Read(string path = null)
        {
            path ??= DefaultPath;
            if (!File.Exists(path)) return new List<FxPack>();
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
                var arr = root?["packs"] as JsonArray;
                if (arr == null) return new List<FxPack>();

                var list = new List<FxPack>();
                foreach (var n in arr)
                {
                    if (n is not JsonObject o) continue;
                    list.Add(new FxPack
                    {
                        Token = (string)o["token"],
                        DisplayName = (string)o["displayName"],
                        Hero = (string)o["hero"],
                        SourceFolder = (string)o["sourceFolder"],
                        InstalledUtc = (string)o["installedUtc"],
                        Effects = (o["effects"] as JsonArray)?
                            .OfType<JsonObject>()
                            .Select(p => new FxRecord
                            {
                                UpkPath = (string)p["upkPath"],
                                Package = (string)p["package"],
                                ClassPath = (string)p["class"],
                                FromAsset = (string)p["from"],
                                EffectName = (string)p["effectName"],
                                SourceCrc = (string)p["sourceCrc"],
                            })
                            .Where(p => p.Package != null).ToList()
                            ?? new List<FxRecord>(),
                        Parents = (o["parents"] as JsonArray)?
                            .Select(x => (string)x).Where(s2 => s2 != null).ToList()
                            ?? new List<string>(),
                    });
                }
                return list.Where(p => !string.IsNullOrWhiteSpace(p.Token)).ToList();
            }
            catch
            {

                return new List<FxPack>();
            }
        }

        public static void Write(List<FxPack> packs, string path = null)
        {
            path ??= DefaultPath;
            var arr = new JsonArray();
            foreach (FxPack p in packs.OrderBy(p => p.Token, StringComparer.OrdinalIgnoreCase))
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Token)) continue;

                var fx = new JsonArray();
                foreach (FxRecord f in p.Effects ?? new List<FxRecord>())
                    if (f != null && f.Package != null)
                        fx.Add(new JsonObject
                        {
                            ["upkPath"] = f.UpkPath,
                            ["package"] = f.Package,
                            ["class"] = f.ClassPath,
                            ["from"] = f.FromAsset,
                            ["effectName"] = f.EffectName,
                            ["sourceCrc"] = f.SourceCrc,
                        });

                var parents = new JsonArray();
                foreach (string par in p.Parents ?? new List<string>())
                    if (!string.IsNullOrWhiteSpace(par)) parents.Add(par);

                arr.Add(new JsonObject
                {
                    ["token"] = p.Token,
                    ["displayName"] = p.DisplayName,
                    ["hero"] = p.Hero,
                    ["sourceFolder"] = p.SourceFolder,
                    ["installedUtc"] = p.InstalledUtc,
                    ["effects"] = fx,
                    ["parents"] = parents,
                });
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(path, new JsonObject { ["packs"] = arr }.ToJsonString(
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
                }));
        }

        public static FxPack Find(string token, string path = null)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;
            return Read(path).FirstOrDefault(
                p => string.Equals(p.Token, token, StringComparison.OrdinalIgnoreCase));
        }

        public static void Upsert(FxPack pack, string path = null)
        {
            if (pack == null || string.IsNullOrWhiteSpace(pack.Token)) return;
            List<FxPack> all = Read(path);
            all.RemoveAll(p => string.Equals(p.Token, pack.Token, StringComparison.OrdinalIgnoreCase));
            all.Add(pack);
            Write(all, path);
        }

        public static void Remove(string token, string path = null)
        {
            if (string.IsNullOrWhiteSpace(token)) return;
            List<FxPack> all = Read(path);
            if (all.RemoveAll(p => string.Equals(p.Token, token, StringComparison.OrdinalIgnoreCase)) == 0) return;
            Write(all, path);
        }

        public static HashSet<string> AllOwnedPackages(string path = null)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FxPack p in Read(path))
                foreach (FxRecord f in p.Effects ?? new List<FxRecord>())
                    if (!string.IsNullOrWhiteSpace(f.Package)) set.Add(f.Package);
            return set;
        }

        public sealed class PackUser
        {
            public uint Enum { get; set; }
            public string DisplayName { get; set; }

            public bool Enabled { get; set; }
            public override string ToString() => DisplayName ?? ("enum " + Enum);
        }

        public static List<PackUser> UsedBy(string gameRoot, string token)
        {
            var users = new List<PackUser>();
            if (string.IsNullOrWhiteSpace(token)) return users;

            try
            {
                string jsonPath = CostumeLibrary.CustomCostumesJson(gameRoot);
                if (!CostumeConfig.Exists(jsonPath)) return users;
                if (JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath)) is not JsonObject root) return users;

                foreach (string key in new[] { "costumes", "disabled" })
                {
                    if (root[key] is not JsonArray arr) continue;
                    foreach (var n in arr)
                    {
                        if (n is not JsonObject o) continue;
                        string assigned = (string)o["fxPack"];
                        if (!string.Equals(assigned, token, StringComparison.OrdinalIgnoreCase)) continue;
                        users.Add(new PackUser
                        {
                            Enum = o["enum"]?.GetValue<uint>() ?? 0,
                            DisplayName = (string)o["displayName"] ?? (string)o["name"],
                            Enabled = key == "costumes",
                        });
                    }
                }
            }
            catch {  }

            return users;
        }
    }
}
