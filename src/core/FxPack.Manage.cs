using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.Json.Nodes;

namespace CostumeManager.Core
{

    public static partial class Installer
    {

        public sealed class InstalledEffect
        {
            public uint CostumeEnum { get; set; }
            public string FromHex { get; set; }
            public ulong From { get; set; }
            public string Package { get; set; }
            public string ClassPath { get; set; }
            public string UpkPath { get; set; }
            public bool UpkExists { get; set; }
            public string EffectName { get; set; }
            public string Kind { get; set; }

            public string State
            {
                get
                {
                    if (!UpkExists) return "MISSING UPK";
                    if (From == 0) return "BAD 'from'";
                    if (EffectName == null) return "UNKNOWN SOURCE";
                    return "OK";
                }
            }

            public bool IsBroken { get { return !UpkExists || From == 0; } }

            public override string ToString()
            {
                return (EffectName ?? Package) + " (" + State + ")";
            }
        }

        public sealed class CostumeEffects
        {
            public uint Enum { get; set; }
            public string DisplayName { get; set; }
            public string DonorClass { get; set; }
            public bool Enabled { get; set; } = true;

            public List<InstalledEffect> Effects { get; set; }

            public bool OptedIn { get { return Effects != null; } }
            public int Count { get { return Effects == null ? 0 : Effects.Count; } }
            public int BrokenCount { get { return Effects == null ? 0 : Effects.Count(e => e.IsBroken); } }

            public override string ToString() { return DisplayName; }
        }

        public static List<CostumeEffects> ListEffects(string gameRoot, EffectTables tables = null)
        {
            var outp = new List<CostumeEffects>();
            if (string.IsNullOrWhiteSpace(gameRoot)) return outp;

            string cooked, manifest, bin;
            try
            {
                var p = GamePaths.Resolve(gameRoot);
                cooked = p.cooked; manifest = p.manifest; bin = p.bin;
            }
            catch { return outp; }

            string jsonPath = CostumeLibrary.CustomCostumesJson(gameRoot);
            if (string.IsNullOrWhiteSpace(jsonPath)) return outp;

            JsonNode root;
            try { root = JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath)); }
            catch { return outp; }
            if (root == null) return outp;

            foreach (string arrName in new[] { "costumes", "disabled" })
            {
                JsonArray arr = root[arrName] as JsonArray;
                if (arr == null) continue;

                foreach (JsonNode n in arr)
                {
                    JsonObject o = n as JsonObject;
                    if (o == null) continue;

                    var ce = new CostumeEffects { Enabled = (arrName == "costumes") };
                    try { ce.Enum = o["enum"] != null ? (uint)o["enum"] : 0u; } catch { }
                    ce.DisplayName = o["name"] != null ? o["name"].ToString() : "(unnamed)";
                    ce.DonorClass = o["donorClass"] != null ? o["donorClass"].ToString() : null;

                    JsonArray fx = o["effects"] as JsonArray;
                    if (o.ContainsKey("effects") && fx != null)
                    {
                        ce.Effects = new List<InstalledEffect>();
                        foreach (JsonNode fn in fx)
                        {
                            JsonObject fo = fn as JsonObject;
                            if (fo == null) continue;

                            var ie = new InstalledEffect { CostumeEnum = ce.Enum };
                            ie.FromHex = fo["from"] != null ? fo["from"].ToString() : null;
                            ie.From = ParseHexOrZero(ie.FromHex);
                            ie.Package = fo["package"] != null ? fo["package"].ToString() : null;
                            ie.ClassPath = fo["class"] != null ? fo["class"].ToString() : null;

                            if (!string.IsNullOrWhiteSpace(ie.Package) && !string.IsNullOrWhiteSpace(cooked))
                            {
                                ie.UpkPath = Path.Combine(cooked, ie.Package + ".upk");
                                ie.UpkExists = File.Exists(ie.UpkPath);
                            }

                            if (tables != null && ie.From != 0)
                            {
                                EffectRecord rec;
                                if (tables.ByAssetId.TryGetValue(ie.From, out rec))
                                {
                                    ie.EffectName = rec.Name;
                                    ie.Kind = rec.Kind;
                                }
                            }
                            ce.Effects.Add(ie);
                        }
                    }
                    outp.Add(ce);
                }
            }

            return outp.OrderBy(c => c.Enum).ToList();
        }

        public static UninstallResult SetIsolation(string gameRoot, uint enumId, bool on,
                                                   Action<string> log = null)
        {
            var res = new UninstallResult();
            try
            {
                string jsonPath = CostumeLibrary.CustomCostumesJson(gameRoot);
                if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(CostumeConfig.ExistingPath(jsonPath)))
                {
                    res.Error = "no CustomCostumes config found in that game folder";
                    return res;
                }

                JsonNode root = JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath));
                JsonObject target = null;
                string whichArray = null;

                foreach (string arrName in new[] { "costumes", "disabled" })
                {
                    JsonArray arr = root[arrName] as JsonArray;
                    if (arr == null) continue;
                    foreach (JsonNode n in arr)
                    {
                        JsonObject o = n as JsonObject;
                        if (o == null || o["enum"] == null) continue;
                        if ((uint)o["enum"] == enumId) { target = o; whichArray = arrName; break; }
                    }
                    if (target != null) break;
                }

                if (target == null)
                {
                    res.Error = "no costume with enum " + enumId + " in the config";
                    return res;
                }

                JsonArray existing = target["effects"] as JsonArray;
                if (on && existing != null && existing.Count > 0)
                {
                    res.Error = "this costume already has " + existing.Count + " effect(s). "
                              + "Remove them before running the isolation test, or the test "
                              + "would delete them.";
                    return res;
                }

                Backup.Timestamped(CostumeConfig.ExistingPath(jsonPath));

                if (on) target["effects"] = new JsonArray();
                else target.Remove("effects");

                target.Remove("hotspots");

                var opts = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
                };
                CostumeConfig.WriteAllText(jsonPath, root.ToJsonString(opts));

                string name = target["name"] != null ? target["name"].ToString() : ("enum " + enumId);
                res.Steps.Add(on
                    ? $"\"{name}\": isolation ON - wrote \"effects\": [] ({whichArray})"
                    : $"\"{name}\": isolation OFF - removed the \"effects\" key ({whichArray})");
                if (log != null) foreach (string s in res.Steps) log("  " + s);

                if (on && log != null)
                {
                    log("  Now launch the game and equip this costume. In CostumeMod.log expect:");
                    log("    \"<name>\": custom FX opt-in -> forged CostumeUnrealClass 0x... at +0x3D0");
                    log("    [H3] \"<name>\": matched via FORGED CostumeUnrealClass (0x...)");
                    log("  PASS = the costume still renders correctly.");
                    log("  FAIL = it renders as the DONOR, or the client crashes - and since no");
                    log("         effect package is involved, the forged id is the cause.");
                }

                res.Ok = true;
                return res;
            }
            catch (Exception ex)
            {
                res.Error = ex.Message;
                return res;
            }
        }

        static void PushHotspotsToServer(string jsonPath, string serverDir,
                                         UninstallResult res, Action<string> log)
        {
            string serverJson = !string.IsNullOrWhiteSpace(serverDir)
                ? Path.Combine(serverDir, "ServerCostumes.json")
                : Path.Combine(AppContext.BaseDirectory, "ServerCostumes.json");

            RegenerateServerFromClient(jsonPath, serverJson, log);
            res.Steps.Add("ServerCostumes.json regenerated -> " + serverJson);
            if (string.IsNullOrWhiteSpace(serverDir))
                res.Steps.Add("⚠ no server folder is configured (Settings tab), so that file is "
                              + "beside CostumeManager.exe and must be copied to the server "
                              + "yourself.");
            res.Steps.Add("⚑ RESTART THE SERVER - forged hotspot ids are aliased at load time.");
        }

        public static async Task<UninstallResult> AddEffectsAsync(
            string gameRoot, uint enumId, List<FxCandidate> selected,
            EffectTables tables, Action<string> log = null, string serverDir = null)
        {
            var res = new UninstallResult();
            try
            {
                string cooked = GamePaths.Resolve(gameRoot).cooked;
                string jsonPath = CostumeLibrary.CustomCostumesJson(gameRoot);
                if (string.IsNullOrWhiteSpace(cooked) || !Directory.Exists(cooked))
                { res.Error = "could not resolve CookedPCConsole"; return res; }

                JsonNode root = JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath));
                JsonObject target = FindEntry(root, enumId);
                if (target == null) { res.Error = "no costume with enum " + enumId; return res; }

                InstallRecord rec = InstallLedger.Read().FirstOrDefault(r => r.Enum == enumId);
                string customName = rec != null && !string.IsNullOrWhiteSpace(rec.Name)
                    ? rec.Name
                    : SanitiseToken(target["name"] != null ? target["name"].ToString() : null);
                if (string.IsNullOrWhiteSpace(customName))
                { res.Error = "could not determine this costume's custom-name token"; return res; }

                if (log != null) log("[fx] installing " + selected.Count + " effect(s) as \"" + customName + "\"");

                List<string> coveredStock = selected
                    .Where(c => c != null && c.Installable && !string.IsNullOrEmpty(c.StockStem))
                    .Select(c => "UC__" + c.StockStem + "_SF.upk")
                    .ToList();

                var built = new List<FxEffect>();
                var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (FxCandidate c in selected)
                {
                    if (c == null || !c.Installable) continue;
                    c.CoveredStockFiles = c.SiblingRenameOptIn ? coveredStock : null;
                    if (c.SiblingRenameOptIn && log != null)
                        log("  · " + c.FileName + ": \"fix shared name\" is ON for this package "
                            + "- sibling classes it shares with stock may be renamed");
                    FxBuildResult b = await FxPackBuilder.BuildAsync(
                        c, customName, cooked, log, DonorDetector.IsLinkTimeReference);

                    if (!b.Ok)
                    {

                        res.Steps.Add("⚠ " + c.FileName + ": " + b.Error);
                        if (log != null) log("  ⚠ " + c.FileName + ": " + b.Error);
                        continue;
                    }

                    if (b.WrittenHeader != null)
                        ReportOrphanedImports(b.WrittenHeader, n => b.RenamedTo.Contains(n), log, res.Steps);

                    foreach (string parent in await FxPackBuilder.DetectFxParentPackagesAsync(
                                                   b.OutputPath, cooked, log))
                        parents.Add(parent);

                    string crc = null;
                    try { crc = FxCrc32.ComputeFileHex(c.SourcePath); } catch { }

                    built.Add(new FxEffect
                    {
                        From = b.From,
                        Package = b.Package,
                        ClassPath = b.ClassPath,
                        EffectName = c.Record != null ? c.Record.Name : c.StockStem,
                        UpkPath = b.OutputPath,
                        SourceCrc = crc,
                    });
                    foreach (string s in b.Steps) res.Steps.Add("  " + s);
                }

                if (built.Count == 0)
                {
                    res.Error = "no effect package could be built - nothing was changed";
                    return res;
                }

                DonorTables donorTables = null;
                try { donorTables = DonorTables.Load(
                          Path.Combine(AppContext.BaseDirectory, "Costumes.json")); }
                catch { }

                List<FxEffect> stubRows = AddSubclassStubRows(built, tables, donorTables, log);
                foreach (FxEffect s in stubRows) built.Add(s);

                Backup.Timestamped(CostumeConfig.ExistingPath(jsonPath));

                JsonArray arr = target["effects"] as JsonArray;
                if (arr == null) { arr = new JsonArray(); target["effects"] = arr; }

                foreach (FxEffect e in built)
                {
                    string fromHex = "0x" + e.From.ToString("X16");
                    for (int i = arr.Count - 1; i >= 0; i--)
                    {
                        JsonObject o = arr[i] as JsonObject;
                        if (o != null && o["from"] != null &&
                            string.Equals(o["from"].ToString(), fromHex, StringComparison.OrdinalIgnoreCase))
                            arr.RemoveAt(i);
                    }
                    arr.Add(new JsonObject
                    {
                        ["from"] = fromHex,
                        ["package"] = e.Package,
                        ["class"] = e.ClassPath,
                    });
                }

                FxHotspots.Sync(target, tables, log);

                if (parents.Count > 0)
                {
                    JsonArray chain = target["chain"] as JsonArray;
                    if (chain == null) { chain = new JsonArray(); target["chain"] = chain; }

                    var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (JsonNode n in chain) if (n != null) have.Add(n.ToString());

                    int inserted = 0;
                    foreach (string parent in parents)
                    {
                        if (have.Contains(parent)) continue;

                        chain.Insert(Math.Min(1, chain.Count), parent);
                        have.Add(parent);
                        inserted++;
                        res.Steps.Add("chain: added parent \"" + parent + "\" (a base class this effect subclasses)");
                    }
                    if (inserted == 0)
                        res.Steps.Add("chain: " + parents.Count + " parent(s) already present");
                }

                CostumeConfig.WriteAllText(jsonPath, root.ToJsonString(JsonOpts()));
                res.Steps.Add("config: " + built.Count + " effect(s) attached, "
                              + arr.Count + " total on this costume");

                PushHotspotsToServer(jsonPath, serverDir, res, log);

                if (rec != null)
                {
                    if (rec.FxPackages == null) rec.FxPackages = new List<FxRecord>();
                    foreach (FxEffect e in built)
                    {

                        if (string.IsNullOrEmpty(e.UpkPath)) continue;

                        rec.FxPackages.RemoveAll(f => string.Equals(f.Package, e.Package,
                                                                    StringComparison.OrdinalIgnoreCase));
                        rec.FxPackages.Add(new FxRecord
                        {
                            UpkPath = e.UpkPath,
                            Package = e.Package,
                            ClassPath = e.ClassPath,
                            FromAsset = "0x" + e.From.ToString("X16"),
                            EffectName = e.EffectName,
                            SourceCrc = e.SourceCrc,
                        });
                    }
                    InstallLedger.Upsert(rec);
                    res.Steps.Add("ledger: " + rec.FxPackages.Count + " effect package(s) recorded");
                }
                else
                {
                    res.Steps.Add("⚠ no ledger record for this costume - uninstall will fall back "
                                  + "to the config's effects list to find these files");
                }

                res.Steps.Add(VerifyEffectFiles(root, enumId, cooked, log));

                res.Ok = true;
                return res;
            }
            catch (Exception ex)
            {
                res.Error = ex.Message;
                return res;
            }
        }

        static string CostumeVariantToken(string stub, string parent, DonorTables donors)
        {
            if (donors == null || string.IsNullOrEmpty(stub) || string.IsNullOrEmpty(parent))
                return null;
            if (stub.Length <= parent.Length) return null;
            if (!stub.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) return null;

            string tail = stub.Substring(parent.Length).TrimStart('_');
            if (tail.Length == 0) return null;

            foreach (string cls in donors.AssetIds.Keys)
            {
                int us = cls.IndexOf('_');
                if (us < 0) continue;
                int us2 = cls.IndexOf('_', us + 1);
                if (us2 < 0) continue;
                string token = cls.Substring(us2 + 1);
                if (token.Length > 0 && string.Equals(token, tail, StringComparison.OrdinalIgnoreCase))
                    return token;
            }
            return null;
        }

        static List<FxEffect> AddSubclassStubRows(List<FxEffect> built, EffectTables tables,
                                                  DonorTables donors, Action<string> log)
        {
            var extra = new List<FxEffect>();
            if (built == null || built.Count == 0 || tables == null) return extra;

            if (!FxRefDb.SubclassIndexAvailable)
            {
                log?.Invoke(FxRefDb.Available
                    ? "⚠ subclass stubs NOT checked - effect_reference.db predates the subclass "
                      + "index. Rebuild it with fxrefbuilder, or a power whose later stages are "
                      + "inherited stubs (Gambit's charge-up card) will render stock for those "
                      + "stages only."
                    : "⚠ subclass stubs NOT checked - effect_reference.db not found.");
                return extra;
            }

            var covered = new HashSet<ulong>();
            foreach (FxEffect e in built) covered.Add(e.From);

            foreach (FxEffect parent in new List<FxEffect>(built))
            {

                EffectRecord prec = null;
                if (parent.EffectName != null) tables.ByName.TryGetValue(parent.EffectName, out prec);
                if (prec == null) continue;
                string stockClass = "marvelgamecontent." + prec.Name.ToLowerInvariant();

                foreach (string stubFile in FxRefDb.SubclassStubsOf(stockClass))
                {
                    if (!tables.ByUpkFileName.TryGetValue(stubFile, out EffectRecord srec)) continue;
                    if (srec.AssetId == 0) continue;

                    if (!covered.Add(srec.AssetId)) continue;

                    string variantOf = CostumeVariantToken(srec.Name, prec.Name, donors);
                    string tag = variantOf != null
                        ? $"  (the \"{variantOf}\" costume's variant - usually inert for a custom "
                          + "costume, kept because a dropped row would be a silent gap)"
                        : "";

                    extra.Add(new FxEffect
                    {
                        From = srec.AssetId,
                        Package = parent.Package,
                        ClassPath = parent.ClassPath,
                        EffectName = srec.Name,
                        UpkPath = null,
                        SourceCrc = null,
                    });
                    log?.Invoke($"  subclass stub: {srec.Name} inherits {prec.Name} "
                                + $"-> 0x{srec.AssetId:X16} routed to {parent.Package}{tag}");
                }
            }

            if (extra.Count > 0)
                log?.Invoke($"{extra.Count} subclass stub row(s) added - these are stock packages "
                            + "that inherit a customised class and would otherwise render stock");
            return extra;
        }

        public static UninstallResult RemoveEffects(string gameRoot, uint enumId, string package,
                                                    EffectTables tables, Action<string> log = null,
                                                    string serverDir = null)
        {
            var res = new UninstallResult();
            try
            {
                string cooked = GamePaths.Resolve(gameRoot).cooked;
                string jsonPath = CostumeLibrary.CustomCostumesJson(gameRoot);
                JsonNode root = JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath));
                JsonObject target = FindEntry(root, enumId);
                if (target == null) { res.Error = "no costume with enum " + enumId; return res; }

                JsonArray arr = target["effects"] as JsonArray;
                if (arr == null) { res.Error = "this costume has no effects"; return res; }

                Backup.Timestamped(CostumeConfig.ExistingPath(jsonPath));

                var removed = new List<string>();
                for (int i = arr.Count - 1; i >= 0; i--)
                {
                    JsonObject o = arr[i] as JsonObject;
                    if (o == null) { arr.RemoveAt(i); continue; }
                    string pkg = o["package"] != null ? o["package"].ToString() : null;
                    if (package != null && !string.Equals(pkg, package, StringComparison.OrdinalIgnoreCase))
                        continue;
                    arr.RemoveAt(i);
                    if (pkg != null) removed.Add(pkg);
                }

                HashSet<string> packOwned = FxPackRegistry.AllOwnedPackages();

                foreach (string pkg in removed)
                {
                    string file = Path.Combine(cooked, pkg + ".upk");
                    if (tables != null && tables.IsStockUpkFileName(Path.GetFileName(file)))
                    {
                        res.Steps.Add("⛔ REFUSED to delete \"" + Path.GetFileName(file)
                                      + "\" - that is a STOCK game package, not one we created");
                        continue;
                    }
                    if (packOwned.Contains(pkg))
                    {
                        res.Steps.Add("kept \"" + Path.GetFileName(file)
                                      + "\" - it belongs to an FX pack, which other costumes may use");
                        continue;
                    }
                    try
                    {
                        if (File.Exists(file)) { File.Delete(file); res.Steps.Add("deleted " + Path.GetFileName(file)); }
                    }
                    catch (Exception ex) { res.Steps.Add("⚠ could not delete " + Path.GetFileName(file) + ": " + ex.Message); }
                }

                if (arr.Count == 0)
                {
                    target.Remove("effects");
                    res.Steps.Add("no effects left - removed the \"effects\" key "
                                  + "(back to the untouched legacy path, not isolation mode)");
                }

                FxHotspots.Sync(target, tables);

                CostumeConfig.WriteAllText(jsonPath, root.ToJsonString(JsonOpts()));
                PushHotspotsToServer(jsonPath, serverDir, res, log);

                InstallRecord rec = InstallLedger.Read().FirstOrDefault(r => r.Enum == enumId);
                if (rec != null && rec.FxPackages != null)
                {
                    rec.FxPackages.RemoveAll(f => removed.Any(
                        p => string.Equals(p, f.Package, StringComparison.OrdinalIgnoreCase)));
                    InstallLedger.Upsert(rec);
                }

                res.Steps.Add(VerifyEffectFiles(root, enumId, cooked, log));

                if (package == null)
                {
                    foreach (string f in FindOrphanedFxPackages(gameRoot, enumId, tables, log))
                    {
                        try
                        {
                            File.Delete(f);
                            res.Steps.Add("swept orphaned " + Path.GetFileName(f));
                        }
                        catch (Exception ex)
                        { res.Steps.Add("⚠ could not delete " + Path.GetFileName(f) + ": " + ex.Message); }
                    }
                }

                if (log != null) foreach (string s in res.Steps) log("  " + s);
                res.Ok = true;
                return res;
            }
            catch (Exception ex) { res.Error = ex.Message; return res; }
        }

        public static UninstallResult PruneMissingEffects(string gameRoot, EffectTables tables,
                                                          Action<string> log = null,
                                                          string serverDir = null)
        {
            var res = new UninstallResult();
            try
            {
                string cooked = GamePaths.Resolve(gameRoot).cooked;
                string jsonPath = CostumeLibrary.CustomCostumesJson(gameRoot);
                JsonNode root = JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath));

                int pruned = 0, costumes = 0;
                foreach (string arrName in new[] { "costumes", "disabled" })
                {
                    JsonArray outer = root[arrName] as JsonArray;
                    if (outer == null) continue;
                    foreach (JsonNode n in outer)
                    {
                        JsonObject o = n as JsonObject;
                        JsonArray fx = o != null ? o["effects"] as JsonArray : null;
                        if (fx == null) continue;

                        int before = fx.Count;
                        for (int i = fx.Count - 1; i >= 0; i--)
                        {
                            JsonObject e = fx[i] as JsonObject;
                            string pkg = e != null && e["package"] != null ? e["package"].ToString() : null;
                            if (pkg == null || File.Exists(Path.Combine(cooked, pkg + ".upk"))) continue;
                            fx.RemoveAt(i);
                            pruned++;
                            res.Steps.Add("\"" + o["name"] + "\": dropped dead entry " + pkg);
                        }
                        if (fx.Count != before)
                        {
                            costumes++;
                            if (fx.Count == 0) o.Remove("effects");
                            FxHotspots.Sync(o, tables);
                        }
                    }
                }

                if (pruned == 0)
                {
                    res.Steps.Add("no missing effect packages - nothing to prune");
                    res.Ok = true;
                    return res;
                }

                Backup.Timestamped(CostumeConfig.ExistingPath(jsonPath));
                CostumeConfig.WriteAllText(jsonPath, root.ToJsonString(JsonOpts()));
                PushHotspotsToServer(jsonPath, serverDir, res, log);
                res.Steps.Add("pruned " + pruned + " dead entry/entries across " + costumes + " costume(s)");
                if (log != null) foreach (string s in res.Steps) log("  " + s);
                res.Ok = true;
                return res;
            }
            catch (Exception ex) { res.Error = ex.Message; return res; }
        }

        public static UninstallResult SyncHotspots(string gameRoot, EffectTables tables,
                                                   Action<string> log = null, string serverDir = null)
        {
            var res = new UninstallResult();
            try
            {
                string jsonPath = CostumeLibrary.CustomCostumesJson(gameRoot);
                if (!CostumeConfig.Exists(jsonPath))
                { res.Error = "no CustomCostumes config found"; return res; }

                string detail;
                if (!FxHotspots.SelfTest(out detail))
                {

                    res.Error = detail;
                    return res;
                }
                res.Steps.Add(detail);

                if (tables == null || tables.ByName.Count == 0)
                {
                    res.Error = "Effects.json is missing or empty next to CostumeManager.exe - "
                              + "refusing, because with no effect table every costume would "
                              + "look like it has no hotspots and the existing ids would be "
                              + "deleted.";
                    return res;
                }

                JsonNode root = JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath));

                int costumes = 0, total = 0;
                foreach (string arrName in new[] { "costumes", "disabled" })
                {
                    JsonArray outer = root[arrName] as JsonArray;
                    if (outer == null) continue;
                    foreach (JsonNode n in outer)
                    {
                        JsonObject o = n as JsonObject;
                        if (o == null) continue;

                        int before = (o["hotspots"] as JsonArray)?.Count ?? 0;
                        int now = FxHotspots.Sync(o, tables);
                        if (now > 0)
                        {
                            costumes++;
                            total += now;
                            res.Steps.Add("\"" + o["name"] + "\": " + now + " hotspot id(s)"
                                          + (before == now ? " (unchanged)" : ""));
                        }
                        else if (before > 0)
                        {
                            res.Steps.Add("\"" + o["name"] + "\": no world-entity effects any "
                                          + "more - removed " + before + " stale id(s)");
                        }
                    }
                }

                Backup.Timestamped(CostumeConfig.ExistingPath(jsonPath));
                CostumeConfig.WriteAllText(jsonPath, root.ToJsonString(JsonOpts()));

                if (total == 0)
                    res.Steps.Add("no costume has a world-entity (hotspot/pet) effect installed "
                                  + "- nothing to forge");

                string serverJson = !string.IsNullOrWhiteSpace(serverDir)
                    ? Path.Combine(serverDir, "ServerCostumes.json")
                    : Path.Combine(AppContext.BaseDirectory, "ServerCostumes.json");

                RegenerateServerFromClient(jsonPath, serverJson, log);
                res.Steps.Add("ServerCostumes.json regenerated -> " + serverJson);
                if (string.IsNullOrWhiteSpace(serverDir))
                    res.Steps.Add("⚠ no server folder is configured (Settings tab), so that file "
                                  + "is beside CostumeManager.exe and must be copied to the "
                                  + "server yourself.");
                res.Steps.Add("⚑ RESTART THE SERVER - the forged ids are aliased at load time.");

                if (log != null) foreach (string s in res.Steps) log("  " + s);
                res.Ok = true;
                return res;
            }
            catch (Exception ex) { res.Error = ex.Message; return res; }
        }

        public static List<string> FindOrphanedFxPackages(string gameRoot, uint enumId,
                                                          EffectTables tables, Action<string> log = null)
        {
            var orphans = new List<string>();
            try
            {
                string cooked = GamePaths.Resolve(gameRoot).cooked;
                if (string.IsNullOrWhiteSpace(cooked) || !Directory.Exists(cooked)) return orphans;

                InstallRecord rec = InstallLedger.Read().FirstOrDefault(r => r.Enum == enumId);
                string token = rec != null ? rec.Name : null;
                if (string.IsNullOrWhiteSpace(token)) return orphans;

                JsonNode root = JsonNode.Parse(CostumeConfig.ReadAllText(
                                                   CostumeLibrary.CustomCostumesJson(gameRoot)));
                JsonObject target = FindEntry(root, enumId);
                if (target == null) return orphans;

                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string key in new[] { "upk", "package", "iconPackage" })
                    if (target[key] != null) keep.Add(StripUpk(target[key].ToString()));
                if (target["chain"] is JsonArray ch)
                    foreach (JsonNode n in ch) if (n != null) keep.Add(StripUpk(n.ToString()));
                if (target["effects"] is JsonArray fx)
                    foreach (JsonNode n in fx)
                        if (n is JsonObject o && o["package"] != null) keep.Add(StripUpk(o["package"].ToString()));

                foreach (string owned in FxPackRegistry.AllOwnedPackages()) keep.Add(StripUpk(owned));

                string suffix = "_" + token + "_SF.upk";
                foreach (string f in Directory.EnumerateFiles(cooked, "UC__*" + suffix))
                {
                    string leaf = Path.GetFileName(f);
                    string stem = StripUpk(leaf);
                    if (keep.Contains(stem)) continue;
                    if (tables != null && tables.IsStockUpkFileName(leaf))
                    {
                        if (log != null) log("  ⛔ refusing to treat stock package as an orphan: " + leaf);
                        continue;
                    }

                    if (leaf.StartsWith("UC__MarvelPlayer_", StringComparison.OrdinalIgnoreCase)) continue;
                    orphans.Add(f);
                }
            }
            catch (Exception ex) { if (log != null) log("  (orphan scan failed: " + ex.Message + ")"); }
            return orphans;
        }

        static string StripUpk(string s)
        {
            string v = (s ?? "").Trim();
            if (v.EndsWith(".upk", StringComparison.OrdinalIgnoreCase)) v = v.Substring(0, v.Length - 4);
            return v;
        }

        static JsonObject FindEntry(JsonNode root, uint enumId)
        {
            foreach (string arrName in new[] { "costumes", "disabled" })
            {
                JsonArray arr = root[arrName] as JsonArray;
                if (arr == null) continue;
                foreach (JsonNode n in arr)
                {
                    JsonObject o = n as JsonObject;
                    if (o == null || o["enum"] == null) continue;
                    try { if ((uint)o["enum"] == enumId) return o; } catch { }
                }
            }
            return null;
        }

        static JsonSerializerOptions JsonOpts()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true,
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
            };
        }

        static string VerifyEffectFiles(JsonNode root, uint enumId, string cooked, Action<string> log)
        {
            JsonObject target = FindEntry(root, enumId);
            JsonArray arr = target != null ? target["effects"] as JsonArray : null;
            if (arr == null) return "no effects declared";

            var missing = new List<string>();
            foreach (JsonNode n in arr)
            {
                JsonObject o = n as JsonObject;
                string pkg = o != null && o["package"] != null ? o["package"].ToString() : null;
                if (pkg != null && !File.Exists(Path.Combine(cooked, pkg + ".upk"))) missing.Add(pkg);
            }

            if (missing.Count == 0) return "verified: all " + arr.Count + " effect package(s) present on disk";

            string msg = "⛔ " + missing.Count + " declared package(s) are NOT on disk ("
                       + string.Join(", ", missing) + ") - this costume will NOT arm and the donor "
                       + "will render. Use \"Prune missing FX\" to recover.";
            if (log != null) log("  " + msg);
            return msg;
        }

        static string SanitiseToken(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var sb = new System.Text.StringBuilder();
            foreach (char c in s) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.Length > 0 ? sb.ToString() : null;
        }

        static ulong ParseHexOrZero(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            ulong v;
            return ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber,
                                  System.Globalization.CultureInfo.InvariantCulture, out v) ? v : 0UL;
        }
    }
}
