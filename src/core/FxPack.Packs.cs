using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace CostumeManager.Core
{
    public static partial class Installer
    {

        public static async Task<UninstallResult> InstallFxPackAsync(
            string gameRoot, string token, string displayName, string hero,
            List<FxCandidate> selected, Action<string> log = null, string sourceFolder = null,
            string registryPath = null)
        {
            var res = new UninstallResult();
            try
            {
                string cooked = GamePaths.Resolve(gameRoot).cooked;
                if (string.IsNullOrWhiteSpace(cooked) || !Directory.Exists(cooked))
                { res.Error = "could not resolve CookedPCConsole"; return res; }

                var taken = FxPackRegistry.Read(registryPath).Select(p => p.Token)
                    .Concat(InstallLedger.Read().Select(r => r.Name))
                    .Where(t => !string.IsNullOrWhiteSpace(t));
                string problem = FxNaming.TokenProblem(token, taken);
                if (problem != null) { res.Error = problem; return res; }

                if (selected == null || selected.Count(c => c != null && c.Installable) == 0)
                { res.Error = "nothing installable was selected"; return res; }

                int installable = selected.Count(c => c != null && c.Installable);
                if (installable > 255)
                {
                    res.Error = "a pack cannot hold more than 255 effects (" + installable
                              + " selected) - the DLL forges effect ids with an 8-bit index";
                    return res;
                }

                if (log != null)
                    log("[fx] installing pack \"" + token + "\" (" + installable + " effect(s))");

                var built = new List<FxRecord>();
                var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                List<string> coveredStock = selected
                    .Where(c => c != null && c.Installable && !string.IsNullOrEmpty(c.StockStem))
                    .Select(c => "UC__" + c.StockStem + "_SF.upk")
                    .ToList();

                foreach (FxCandidate c in selected)
                {
                    if (c == null || !c.Installable) continue;
                    c.CoveredStockFiles = c.SiblingRenameOptIn ? coveredStock : null;

                    FxBuildResult b = await FxPackBuilder.BuildAsync(
                        c, token, cooked, log, DonorDetector.IsLinkTimeReference);

                    if (!b.Ok)
                    {

                        res.Steps.Add("⚠ " + c.FileName + ": " + b.Error);
                        continue;
                    }

                    if (b.WrittenHeader != null)
                        ReportOrphanedImports(b.WrittenHeader, n => b.RenamedTo.Contains(n), log, res.Steps);

                    foreach (string parent in await FxPackBuilder.DetectFxParentPackagesAsync(
                                                   b.OutputPath, cooked, log))
                        parents.Add(parent);

                    string crc = null;
                    try { crc = FxCrc32.ComputeFileHex(c.SourcePath); } catch { }

                    built.Add(new FxRecord
                    {
                        UpkPath = b.OutputPath,
                        Package = b.Package,
                        ClassPath = b.ClassPath,
                        FromAsset = "0x" + b.From.ToString("X16"),
                        EffectName = c.Record != null ? c.Record.Name : c.StockStem,
                        SourceCrc = crc,
                    });
                    foreach (string s in b.Steps) res.Steps.Add("  " + s);
                }

                if (built.Count == 0)
                { res.Error = "no effect package could be built - nothing was changed"; return res; }

                FxPackRegistry.Upsert(new FxPack
                {
                    Token = token,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? token : displayName,
                    Hero = hero,
                    SourceFolder = sourceFolder,
                    InstalledUtc = DateTime.UtcNow.ToString("o"),
                    Effects = built,
                    Parents = parents.ToList(),
                }, registryPath);

                res.Steps.Add("pack \"" + token + "\": " + built.Count + " package(s) written"
                            + (parents.Count > 0 ? ", " + parents.Count + " parent package(s) needed" : ""));
                res.Ok = true;
            }
            catch (Exception ex) { res.Error = ex.Message; }
            return res;
        }

        public static UninstallResult AssignFxPack(string gameRoot, uint enumId, string token,
                                                   EffectTables tables, Action<string> log = null,
                                                   string serverDir = null, string registryPath = null)
        {
            var res = new UninstallResult();
            try
            {
                string cooked = GamePaths.Resolve(gameRoot).cooked;
                string jsonPath = CostumeLibrary.CustomCostumesJson(gameRoot);
                JsonNode root = JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath));
                JsonObject target = FindEntry(root, enumId);
                if (target == null) { res.Error = "no costume with enum " + enumId; return res; }

                FxPack pack = FxPackRegistry.Find(token, registryPath);
                if (pack == null) { res.Error = "no FX pack called \"" + token + "\""; return res; }

                string costumeHero = FxCompatibility.HeroOfCostume((string)target["donorClass"]);
                if (!string.IsNullOrWhiteSpace(pack.Hero) && !string.IsNullOrWhiteSpace(costumeHero)
                    && !string.Equals(pack.Hero, costumeHero, StringComparison.OrdinalIgnoreCase))
                {
                    res.Error = "\"" + token + "\" is a " + pack.Hero + " pack and this costume is "
                              + costumeHero + " - its effects could never resolve";
                    return res;
                }

                string already = (string)target["fxPack"];
                JsonArray existing = target["effects"] as JsonArray;
                if (existing != null && existing.Count > 0
                    && !string.Equals(already, token, StringComparison.OrdinalIgnoreCase))
                {
                    res.Error = "\"" + target["name"] + "\" already has "
                              + existing.Count + " effect(s)"
                              + (already != null ? " from pack \"" + already + "\"" : "")
                              + " - unassign or remove them first";
                    return res;
                }

                var effects = new List<FxEffect>();
                foreach (FxRecord f in pack.Effects)
                {
                    ulong from = 0;
                    try { from = Convert.ToUInt64((f.FromAsset ?? "0").Replace("0x", ""), 16); } catch { }
                    if (from == 0 || string.IsNullOrWhiteSpace(f.Package)) continue;
                    effects.Add(new FxEffect
                    {
                        From = from,
                        Package = f.Package,
                        ClassPath = f.ClassPath,
                        EffectName = f.EffectName,
                        UpkPath = f.UpkPath,
                        SourceCrc = f.SourceCrc,
                    });
                }
                if (effects.Count == 0) { res.Error = "pack \"" + token + "\" has no usable rows"; return res; }

                List<string> parents = pack.Parents ?? new List<string>();
                if (parents.Count == 0)
                {
                    foreach (FxPackRegistry.PackUser u in FxPackRegistry.UsedBy(gameRoot, token))
                    {
                        if (u.Enum == enumId) continue;
                        List<string> fromPeer = ParentsFromChain(FindEntry(root, u.Enum));
                        if (fromPeer.Count > 0)
                        {
                            parents = fromPeer;
                            res.Steps.Add("recovered " + fromPeer.Count + " parent package(s) from \""
                                        + u.DisplayName + "\", which already uses this pack");
                            break;
                        }
                    }
                }

                DonorTables donorTables = null;
                try { donorTables = DonorTables.Load(
                          Path.Combine(AppContext.BaseDirectory, "Costumes.json")); }
                catch { }
                foreach (FxEffect s in AddSubclassStubRows(effects, tables, donorTables, log))
                    effects.Add(s);

                var arr = new JsonArray();
                foreach (FxEffect e in effects)
                    arr.Add(new JsonObject
                    {
                        ["from"] = "0x" + e.From.ToString("X16"),
                        ["package"] = e.Package,
                        ["class"] = e.ClassPath,
                    });

                var haveChain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (target["chain"] is JsonArray existingChain)
                    foreach (JsonNode n in existingChain) if (n != null) haveChain.Add(n.ToString());
                bool chainComplete = parents.All(p2 => string.IsNullOrWhiteSpace(p2) || haveChain.Contains(p2));

                string before = existing?.ToJsonString();
                if (before != null && before == arr.ToJsonString() && chainComplete
                    && string.Equals(already, token, StringComparison.OrdinalIgnoreCase))
                {
                    res.Ok = true;
                    res.Steps.Add("\"" + target["name"] + "\" is already using \"" + token
                                + "\" - nothing changed");
                    return res;
                }

                Backup.Timestamped(CostumeConfig.ExistingPath(jsonPath));
                target["effects"] = arr;
                target["fxPack"] = token;

                FxHotspots.Sync(target, tables, log);

                if (parents.Count > 0)
                {
                    JsonArray chain = target["chain"] as JsonArray;
                    if (chain == null) { chain = new JsonArray(); target["chain"] = chain; }
                    var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (JsonNode n in chain) if (n != null) have.Add(n.ToString());
                    foreach (string parent in parents)
                    {
                        if (string.IsNullOrWhiteSpace(parent) || have.Contains(parent)) continue;
                        chain.Insert(Math.Min(1, chain.Count), parent);
                        have.Add(parent);
                        res.Steps.Add("chain: added parent \"" + parent + "\"");
                    }
                }

                CostumeConfig.WriteAllText(jsonPath, root.ToJsonString(JsonOpts()));
                res.Steps.Add("\"" + target["name"] + "\" -> pack \"" + token + "\" ("
                            + arr.Count + " effect row(s))");

                PushHotspotsToServer(jsonPath, serverDir, res, log);

                res.Steps.Add(VerifyEffectFiles(root, enumId, cooked, log));
                res.Ok = true;
            }
            catch (Exception ex) { res.Error = ex.Message; }
            return res;
        }

        public static UninstallResult UnassignFxPack(string gameRoot, uint enumId,
                                                     EffectTables tables, Action<string> log = null,
                                                     string serverDir = null)
        {
            var res = new UninstallResult();
            try
            {
                string jsonPath = CostumeLibrary.CustomCostumesJson(gameRoot);
                JsonNode root = JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath));
                JsonObject target = FindEntry(root, enumId);
                if (target == null) { res.Error = "no costume with enum " + enumId; return res; }

                if (target["effects"] == null && target["fxPack"] == null)
                { res.Ok = true; res.Steps.Add("that costume has no FX pack assigned"); return res; }

                string had = (string)target["fxPack"];
                Backup.Timestamped(CostumeConfig.ExistingPath(jsonPath));

                target.Remove("effects");
                target.Remove("hotspots");
                target.Remove("fxPack");

                CostumeConfig.WriteAllText(jsonPath, root.ToJsonString(JsonOpts()));
                res.Steps.Add("\"" + target["name"] + "\" no longer uses "
                            + (had != null ? "pack \"" + had + "\"" : "any FX pack")
                            + " - its packages were left on disk for whoever else uses them");
                PushHotspotsToServer(jsonPath, serverDir, res, log);
                res.Ok = true;
            }
            catch (Exception ex) { res.Error = ex.Message; }
            return res;
        }

        public static UninstallResult DeleteFxPack(string gameRoot, string token,
                                                   EffectTables tables, Action<string> log = null,
                                                   string registryPath = null)
        {
            var res = new UninstallResult();
            try
            {
                FxPack pack = FxPackRegistry.Find(token, registryPath);
                if (pack == null) { res.Error = "no FX pack called \"" + token + "\""; return res; }

                List<FxPackRegistry.PackUser> users = FxPackRegistry.UsedBy(gameRoot, token);
                if (users.Count > 0)
                {
                    res.Error = "\"" + token + "\" is still used by "
                              + string.Join(", ", users.Select(u => "\"" + u.DisplayName + "\""))
                              + " - unassign it there first";
                    return res;
                }

                string cooked = GamePaths.Resolve(gameRoot).cooked;
                int deleted = 0, refused = 0;
                foreach (FxRecord f in pack.Effects ?? new List<FxRecord>())
                {
                    if (string.IsNullOrWhiteSpace(f.Package)) continue;
                    string file = Path.Combine(cooked, f.Package + ".upk");

                    if (tables != null && tables.IsStockUpkFileName(Path.GetFileName(file)))
                    { res.Steps.Add("⛔ REFUSED to delete \"" + Path.GetFileName(file)
                                  + "\" - that is a STOCK game package"); refused++; continue; }

                    try { if (File.Exists(file)) { File.Delete(file); deleted++; } }
                    catch (Exception ex) { res.Steps.Add("⚠ could not delete " + f.Package + ": " + ex.Message); }
                }

                FxPackRegistry.Remove(token, registryPath);
                res.Steps.Add("pack \"" + token + "\" deleted: " + deleted + " package(s) removed"
                            + (refused > 0 ? ", " + refused + " refused" : ""));
                res.Ok = true;
            }
            catch (Exception ex) { res.Error = ex.Message; }
            return res;
        }

        public static UninstallResult ExportFxPack(string gameRoot, string token, string outPath,
                                                   Action<string> log = null,
                                                   string registryPath = null)
        {
            var res = new UninstallResult();
            var (cooked, _, _) = ResolvePaths(gameRoot);

            FxPack pack = FxPackRegistry.Find(token, registryPath);
            if (pack == null)
            {
                res.Error = $"no FX pack \"{token}\" in the registry";
                log?.Invoke(res.Error);
                return res;
            }

            if (!FxPackFile.Write(pack, cooked, outPath, out string error, log))
            {
                res.Error = "export failed: " + error;
                log?.Invoke(res.Error);
                return res;
            }

            res.Ok = true;

            res.Steps.Add($"exported {pack.Effects.Count} effect package(s) to "
                        + Path.GetFileName(outPath));

            log?.Invoke($"players need this AND a .mhcostume whose pack token is \"{pack.Token}\". "
                      + "Either order works: importing the costume first parks its effects, and "
                      + "importing this pack restores them.");
            return res;
        }

        public static List<FxPack> PacksForCostume(string donorClass, string registryPath = null)
        {
            string hero = FxCompatibility.HeroOfCostume(donorClass);
            return FxPackRegistry.Read(registryPath)
                .Where(p => string.IsNullOrWhiteSpace(p.Hero) || string.IsNullOrWhiteSpace(hero)
                         || string.Equals(p.Hero, hero, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.DisplayName ?? p.Token, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
