using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace CostumeManager.Core
{

    public sealed class InstallRecord
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public uint Enum { get; set; }
        public string InstalledUtc { get; set; }

        public string UpkPath { get; set; }
        public string CustomCostumesJson { get; set; }
        public string ManifestPath { get; set; }

        public List<string> TfcAliasRows { get; set; } = new List<string>();

        public string TfcPackage { get; set; }

        public string ManifestBackup { get; set; }
        public string JsonBackup { get; set; }

        public string IconUpkPath { get; set; }
        public string IconPackage { get; set; }

        public List<TfcAlias.Core.AliasPair> TfcAliasPairs { get; set; } = new List<TfcAlias.Core.AliasPair>();

        public List<FxRecord> FxPackages { get; set; } = new List<FxRecord>();
    }

    public sealed class FxRecord
    {
        public string UpkPath { get; set; }
        public string Package { get; set; }
        public string ClassPath { get; set; }
        public string FromAsset { get; set; }
        public string EffectName { get; set; }
        public string SourceCrc { get; set; }

        public override string ToString() { return EffectName ?? Package; }
    }

    public static class InstallLedger
    {

        public static string DefaultPath =>
            Path.Combine(AppContext.BaseDirectory, "installed.json");

        static JsonSerializerOptions Opts => new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };

        public static List<InstallRecord> Read(string path = null)
        {
            path ??= DefaultPath;
            if (!File.Exists(path)) return new List<InstallRecord>();
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
                var arr = root?["installs"] as JsonArray;
                if (arr == null) return new List<InstallRecord>();
                var list = new List<InstallRecord>();
                foreach (var n in arr)
                {
                    if (n is not JsonObject o) continue;
                    list.Add(new InstallRecord
                    {
                        Name = (string)o["name"],
                        DisplayName = (string)o["displayName"],
                        Enum = o["enum"]?.GetValue<uint>() ?? 0,
                        InstalledUtc = (string)o["installedUtc"],
                        UpkPath = (string)o["upkPath"],
                        CustomCostumesJson = (string)o["customCostumesJson"],
                        ManifestPath = (string)o["manifestPath"],
                        TfcPackage = (string)o["tfcPackage"],
                        ManifestBackup = (string)o["manifestBackup"],
                        JsonBackup = (string)o["jsonBackup"],
                        TfcAliasRows = (o["tfcAliasRows"] as JsonArray)?
                            .Select(x => (string)x).Where(s => s != null).ToList()
                            ?? new List<string>(),

                        IconUpkPath = (string)o["iconUpkPath"],
                        IconPackage = (string)o["iconPackage"],
                        TfcAliasPairs = (o["tfcAliasPairs"] as JsonArray)?
                            .OfType<JsonObject>()
                            .Select(p => new TfcAlias.Core.AliasPair((string)p["from"], (string)p["to"]))
                            .Where(p => p.From != null && p.To != null).ToList()
                            ?? new List<TfcAlias.Core.AliasPair>(),

                        FxPackages = (o["fxPackages"] as JsonArray)?
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
                    });
                }
                return list;
            }
            catch
            {

                return new List<InstallRecord>();
            }
        }

        static void Write(List<InstallRecord> records, string path)
        {
            var arr = new JsonArray();
            foreach (var r in records)
            {
                var rows = new JsonArray();
                foreach (var s in r.TfcAliasRows) rows.Add(s);
                var pairs = new JsonArray();
                foreach (var p in r.TfcAliasPairs ?? new List<TfcAlias.Core.AliasPair>())
                    if (p?.From != null && p.To != null)
                        pairs.Add(new JsonObject { ["from"] = p.From, ["to"] = p.To });
                var fx = new JsonArray();
                foreach (var f in r.FxPackages ?? new List<FxRecord>())
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

                arr.Add(new JsonObject
                {
                    ["iconUpkPath"] = r.IconUpkPath,
                    ["iconPackage"] = r.IconPackage,
                    ["tfcAliasPairs"] = pairs,
                    ["fxPackages"] = fx,
                    ["name"] = r.Name,
                    ["displayName"] = r.DisplayName,
                    ["enum"] = r.Enum,
                    ["installedUtc"] = r.InstalledUtc,
                    ["upkPath"] = r.UpkPath,
                    ["customCostumesJson"] = r.CustomCostumesJson,
                    ["manifestPath"] = r.ManifestPath,
                    ["tfcPackage"] = r.TfcPackage,
                    ["manifestBackup"] = r.ManifestBackup,
                    ["jsonBackup"] = r.JsonBackup,
                    ["tfcAliasRows"] = rows,
                });
            }
            var root = new JsonObject { ["installs"] = arr };
            File.WriteAllText(path, root.ToJsonString(Opts));
        }

        public static void Upsert(InstallRecord rec, string path = null)
        {
            path ??= DefaultPath;
            var list = Read(path);

            list.RemoveAll(r => string.Equals(r.Name, rec.Name, StringComparison.OrdinalIgnoreCase)
                             || r.Enum == rec.Enum);
            list.Add(rec);
            Write(list, path);
        }

        public static InstallRecord Remove(string name, string path = null)
        {
            path ??= DefaultPath;
            var list = Read(path);
            var rec = list.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            if (rec != null)
            {
                list.RemoveAll(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
                Write(list, path);
            }
            return rec;
        }
    }
}
