using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;

namespace CostumeManager.Core
{

    public static class FxPackInstall
    {

        public const string PendingKey = "pendingFx";

        public sealed class Result
        {
            public bool Ok => FailedStep == null;
            public string FailedStep { get; set; }
            public List<string> Steps { get; } = new List<string>();
            public List<string> Warnings { get; } = new List<string>();
            public string Token { get; set; }
            public int FilesWritten { get; set; }
            public int CostumesRestored { get; set; }
        }

        public static Result Import(string gameRoot, string packPath, Action<string> log = null)
        {
            var res = new Result();
            var (cooked, _, bin) = GamePaths.Resolve(gameRoot);

            FxPackFile.Info info = FxPackFile.Read(packPath, out string error);
            if (info == null)
            {
                res.FailedStep = "read";
                log?.Invoke("cannot import: " + error);
                return res;
            }

            res.Token = info.Token;
            log?.Invoke($"FX pack \"{info.DisplayName ?? info.Token}\" - token {info.Token}, "
                      + $"{info.Effects.Count} effect package(s), {info.Parents.Count} parent(s)");

            if (!Directory.Exists(cooked))
            {
                res.FailedStep = "gamedir";
                log?.Invoke("CookedPCConsole not found under " + gameRoot);
                return res;
            }

            FxPack existing = FxPackRegistry.Find(info.Token, RegistryPath(bin));
            if (existing != null)
            {
                var had = new HashSet<string>(existing.Effects.Select(e => e.Package),
                                              StringComparer.OrdinalIgnoreCase);
                var now = new HashSet<string>(info.Effects.Select(e => e.Package),
                                              StringComparer.OrdinalIgnoreCase);

                if (had.SetEquals(now))
                    log?.Invoke($"token \"{info.Token}\" is already installed with the same "
                              + "packages - reinstalling over it");
                else
                    res.Warnings.Add($"token \"{info.Token}\" is already installed with a "
                                   + $"DIFFERENT set ({existing.Effects.Count} packages vs "
                                   + $"{info.Effects.Count}). Replacing it; any costume using the "
                                   + "old set may lose effects it had. If these are two different "
                                   + "packs, the author must rebuild one under another token - "
                                   + "this tool cannot rename them.");
            }

            int written = 0;
            int skippedParents = 0;
            try
            {
                using var zip = ZipFile.OpenRead(packPath);

                foreach (var kv in info.Files)
                {
                    ZipArchiveEntry e = zip.GetEntry(kv.Key);
                    if (e == null) continue;

                    string dest = Path.Combine(cooked, kv.Value);

                    bool isParent = kv.Key.StartsWith(FxPackFile.ParentsDir,
                                                      StringComparison.OrdinalIgnoreCase);

                    if (isParent && File.Exists(dest))
                    {
                        skippedParents++;
                        continue;
                    }

                    if (!isParent && File.Exists(dest) && !LooksLikeOurs(kv.Value, info.Token))
                    {
                        res.FailedStep = "collision";
                        log?.Invoke($"REFUSED: \"{kv.Value}\" already exists and does not carry "
                                  + $"the pack token \"{info.Token}\", so it is not ours to "
                                  + "replace. Nothing further was written.");
                        return res;
                    }

                    e.ExtractToFile(dest, true);
                    written++;
                }
            }
            catch (Exception ex)
            {
                res.FailedStep = "copy";
                log?.Invoke("failed while copying packages: " + ex.Message);
                return res;
            }

            res.FilesWritten = written;

            if (skippedParents > 0)
                log?.Invoke($"  {skippedParents} parent package(s) already in the game - kept "
                          + "the stock file, which is what the chain links against");
            res.Steps.Add($"copied {written} package(s) into CookedPCConsole");
            log?.Invoke($"copied {written} package(s)");

            var pack = new FxPack
            {
                Token         = info.Token,
                DisplayName   = info.DisplayName,
                Hero          = info.Hero,
                SourceFolder  = packPath,
                InstalledUtc  = DateTime.UtcNow.ToString("o"),
                Effects       = info.Effects,
                Parents       = info.Parents,
            };

            foreach (FxRecord r in pack.Effects)
                r.UpkPath = Path.Combine(cooked, FxPackFile.PackageFileName(r.Package));

            FxPackRegistry.Upsert(pack, RegistryPath(bin));
            res.Steps.Add("recorded in " + Path.GetFileName(RegistryPath(bin)));

            res.CostumesRestored = RestorePending(bin, info.Token, log);
            if (res.CostumesRestored > 0)
                res.Steps.Add($"restored effects on {res.CostumesRestored} costume(s)");

            log?.Invoke(res.CostumesRestored > 0
                ? $"done - {res.CostumesRestored} costume(s) now have their effects. "
                + "Restart the game client."
                : "done - no installed costume uses this pack yet. Import one, or it will pick "
                + "this up automatically.");

            return res;
        }

        public static Result Remove(string gameRoot, string token, Action<string> log = null)
        {
            var res = new Result { Token = token };
            var (cooked, _, bin) = GamePaths.Resolve(gameRoot);

            FxPack pack = FxPackRegistry.Find(token, RegistryPath(bin));
            if (pack == null)
            {
                res.FailedStep = "lookup";
                log?.Invoke($"no FX pack \"{token}\" is installed");
                return res;
            }

            int parked = ParkAll(bin, pack, log);
            if (parked > 0) res.Steps.Add($"parked effects on {parked} costume(s)");

            int deleted = 0;
            foreach (FxRecord r in pack.Effects)
            {
                string file = FxPackFile.PackageFileName(r.Package);
                string path = Path.Combine(cooked, file);
                try { if (File.Exists(path)) { File.Delete(path); deleted++; } }
                catch (Exception ex) { res.Warnings.Add($"could not delete {file}: {ex.Message}"); }
            }

            res.Steps.Add($"deleted {deleted} package(s); parents left in place (stock or shared)");

            FxPackRegistry.Remove(token, RegistryPath(bin));
            res.Steps.Add("removed from the registry");

            log?.Invoke($"removed FX pack \"{token}\" - {deleted} package(s) deleted, "
                      + $"{parked} costume(s) fell back to stock effects. Restart the client.");
            return res;
        }

        public static bool Park(JsonObject entry, IEnumerable<string> fxPackages, string token)
        {
            if (entry == null) return false;
            if (entry[PendingKey] != null) return false;

            JsonArray effects  = entry["effects"]  as JsonArray;
            JsonArray hotspots = entry["hotspots"] as JsonArray;
            if (effects == null && hotspots == null) return false;

            var owned = new HashSet<string>(
                (fxPackages ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)),
                StringComparer.OrdinalIgnoreCase);

            if (effects != null)
                foreach (JsonNode n in effects)
                    if (n is JsonObject o && o["package"] != null)
                        owned.Add(o["package"].ToString());

            var parked = new JsonObject { ["token"] = token };
            if (effects  != null) parked["effects"]  = effects.DeepClone();
            if (hotspots != null) parked["hotspots"] = hotspots.DeepClone();

            if (entry["chain"] is JsonArray chain)
            {
                var keep = new JsonArray();
                var moved = new JsonArray();

                for (int i = 0; i < chain.Count; i++)
                {
                    string p = chain[i]?.ToString();
                    if (i == 0 || string.IsNullOrWhiteSpace(p) || !owned.Contains(p))
                        keep.Add(p);
                    else
                        moved.Add(p);
                }

                if (moved.Count > 0) parked["chain"] = moved;
                entry["chain"] = keep;
            }

            entry.Remove("effects");
            entry.Remove("hotspots");
            entry[PendingKey] = parked;
            return true;
        }

        public static bool Unpark(JsonObject entry)
        {
            if (entry?[PendingKey] is not JsonObject parked) return false;

            if (parked["effects"]  is JsonArray fx) entry["effects"]  = fx.DeepClone();
            if (parked["hotspots"] is JsonArray hs) entry["hotspots"] = hs.DeepClone();

            if (parked["chain"] is JsonArray moved && moved.Count > 0)
            {
                JsonArray chain = entry["chain"] as JsonArray ?? new JsonArray();
                var rebuilt = new JsonArray();

                if (chain.Count > 0) rebuilt.Add(chain[0]?.ToString());
                foreach (JsonNode n in moved) rebuilt.Add(n?.ToString());
                for (int i = 1; i < chain.Count; i++) rebuilt.Add(chain[i]?.ToString());

                entry["chain"] = rebuilt;
            }

            entry.Remove(PendingKey);
            return true;
        }

        public static string PendingToken(JsonObject entry)
        {
            return (entry?[PendingKey] as JsonObject)?["token"]?.ToString();
        }

        static int RestorePending(string bin, string token, Action<string> log)
        {
            return Rewrite(bin, (entry, name) =>
            {
                if (!string.Equals(PendingToken(entry), token, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (!Unpark(entry)) return false;
                log?.Invoke($"  {name}: effects restored");
                return true;
            });
        }

        static int ParkAll(string bin, FxPack pack, Action<string> log)
        {
            var owned = pack.Effects.Select(e => e.Package).ToList();
            if (pack.Parents != null) owned.AddRange(pack.Parents);

            return Rewrite(bin, (entry, name) =>
            {
                string token = (string)entry["fxPack"];
                bool mine = string.Equals(token, pack.Token, StringComparison.OrdinalIgnoreCase);

                if (!mine && entry["effects"] is JsonArray fx)
                {
                    var set = new HashSet<string>(owned, StringComparer.OrdinalIgnoreCase);
                    foreach (JsonNode n in fx)
                        if (n is JsonObject o && o["package"] != null && set.Contains(o["package"].ToString()))
                        { mine = true; break; }
                }

                if (!mine) return false;
                if (!Park(entry, owned, pack.Token)) return false;

                log?.Invoke($"  {name}: effects parked - it will render stock effects until this "
                          + "pack is installed again");
                return true;
            });
        }

        static int Rewrite(string bin, Func<JsonObject, string, bool> edit)
        {
            string jsonPath = CostumeConfig.ExistingPath(bin);
            if (jsonPath == null) return 0;

            JsonObject root;
            try { root = JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath)) as JsonObject; }
            catch { return 0; }
            if (root == null) return 0;

            int changed = 0;

            foreach (string key in new[] { "costumes", "disabled" })
            {
                if (root[key] is not JsonArray arr) continue;

                foreach (JsonNode n in arr)
                {
                    if (n is not JsonObject entry) continue;
                    string name = (string)entry["name"] ?? "?";
                    if (edit(entry, name)) changed++;
                }
            }

            bool flags = false;
            if (root["fxDryRun"] == null) { root["fxDryRun"] = false; flags = true; }
            if (root["perAvatarMesh"] == null) { root["perAvatarMesh"] = true; flags = true; }

            if (changed > 0 || flags)
                CostumeConfig.WriteAllText(jsonPath, root.ToJsonString(JsonOpts));

            return changed;
        }

        static System.Text.Json.JsonSerializerOptions JsonOpts =>
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
            };

        public static string RegistryPathFor(string gameRoot)
        {
            var (_, _, bin) = GamePaths.Resolve(gameRoot);
            return RegistryPath(bin);
        }

        static string RegistryPath(string bin)
        {

            return Path.Combine(bin, "fxpacks.json");
        }

        static bool LooksLikeOurs(string fileName, string token)
        {
            return fileName != null && token != null
                && fileName.IndexOf("_" + token + "_", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
