using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using UpkManager.Models.UpkFile.Tables;
using UpkRename.Core;
using TfcAlias.Core;
using IconPack.Core;

namespace CostumeManager.Core
{

    public sealed class CostumeInput
    {
        public string UpkPath      { get; set; }
        public string DonorClass   { get; set; }
        public string CustomName   { get; set; }
        public string DisplayName  { get; set; }
        public uint   Enum         { get; set; }
        public string GameDir      { get; set; }
        public string ServerDir    { get; set; }

        public ulong  CustomIdOverride { get; set; }

        public ulong  DonorIdOverride  { get; set; }

        public List<IconPack.Core.IconSource> Icons { get; set; } = new();

        public long StorePrice { get; set; } = InstallPlan.DefaultStorePrice;
    }

    public sealed class InstallPlan
    {
        public string CustomName    { get; set; }
        public string DisplayName   { get; set; }
        public string DonorClass    { get; set; }
        public ulong  DonorAsset    { get; set; }
        public ulong  DonorId       { get; set; }
        public ulong  CustomId      { get; set; }
        public uint   Enum          { get; set; }
        public string ClassPath     { get; set; }
        public string PackageName   { get; set; }

        public long StorePrice { get; set; } = DefaultStorePrice;

        public const long DefaultStorePrice = 750;
        public string OutputUpkName { get; set; }
        public string OutputUpkPath { get; set; }
        public List<RenamePair> Renames { get; set; }
        public AliasPair TfcAlias    { get; set; }

        public List<AliasPair> TfcAliases { get; set; } = new();

        public List<string> RequiredPackages { get; set; } = new();

        public List<IconPack.Core.IconSource> IconSources { get; set; } = new();
        public string IconUpkPath   { get; set; }
        public string IconPackage   { get; set; }
        public List<IconPack.Core.IconPatch> IconPatches { get; set; } = new();
        public AliasPair IconTfcAlias { get; set; }

        public bool FxOptIn { get; set; }
        public List<FxEffect> Effects { get; set; } = new List<FxEffect>();
        public string FxSourceFolder { get; set; }

        public string CookedPcConsole { get; set; }
        public string ManifestPath    { get; set; }
        public string BinariesWin64   { get; set; }
        public string CustomCostumesJson { get; set; }
        public string ServerCostumesJson { get; set; }

        public List<string> Warnings { get; } = new();
    }

    public sealed class InstallResult
    {
        public bool Ok { get; set; }
        public string FailedStep { get; set; }
        public string ManifestBackup { get; set; }
        public string JsonBackup { get; set; }

        public List<string> TfcAliasRows { get; } = new();
        public List<string> Steps { get; } = new();
    }

    public static partial class Installer
    {

        public const string PendingPurgeFileName = "PendingCostumePurges.json";

        public static (string cooked, string manifest, string bin) ResolvePaths(string gameRoot)
            => GamePaths.Resolve(gameRoot);

        public static InstallPlan BuildPlan(CostumeInput input, DonorTables tables)
        {
            var (cooked, manifest, bin) = ResolvePaths(input.GameDir);

            tables.TryResolveAsset(input.DonorClass, out ulong asset);
            ulong proto = input.DonorIdOverride;
            if (proto == 0) tables.TryResolveProto(input.DonorClass, out proto);

            string pkg = DonorDetector.CustomPackageName(input.DonorClass, input.CustomName);
            string cls = DonorDetector.CustomClassPath(input.DonorClass, input.CustomName);

            const string cp = "MarvelPlayer_";
            string heroCostume = input.DonorClass.StartsWith(cp, StringComparison.OrdinalIgnoreCase)
                ? input.DonorClass.Substring(cp.Length) : input.DonorClass;
            int us = heroCostume.IndexOf('_');
            string hero = us > 0 ? heroCostume.Substring(0, us) : heroCostume;
            string customHeroCostume = hero + "_" + input.CustomName;
            string upkNm = $"UC__MarvelPlayer_{customHeroCostume}_SF.upk";

            ulong customId = input.CustomIdOverride != 0
                ? input.CustomIdOverride
                : HashPath.CustomId(input.CustomName);

            var plan = new InstallPlan
            {
                CustomName    = input.CustomName,

                DisplayName   = string.IsNullOrWhiteSpace(input.DisplayName)
                                ? input.CustomName : input.DisplayName.Trim(),
                DonorClass    = input.DonorClass,
                DonorAsset    = asset,
                DonorId       = proto,
                CustomId      = customId,
                Enum          = input.Enum,
                ClassPath     = cls,
                PackageName   = pkg,
                StorePrice    = input.StorePrice,
                OutputUpkName = upkNm,
                OutputUpkPath = Path.Combine(cooked, upkNm),
                Renames       = DonorDetector.BuildRenames(input.DonorClass, input.CustomName),
                TfcAlias      = DonorDetector.BuildTfcAlias(input.DonorClass, input.CustomName),
                CookedPcConsole = cooked,
                ManifestPath    = manifest,
                BinariesWin64   = bin,
                CustomCostumesJson = Path.Combine(bin, "CustomCostumes.json"),

                ServerCostumesJson = !string.IsNullOrWhiteSpace(input.ServerDir)
                    ? Path.Combine(input.ServerDir, "ServerCostumes.json")
                    : Path.Combine(AppContext.BaseDirectory, "ServerCostumes.json"),

                IconSources = input.Icons ?? new List<IconPack.Core.IconSource>(),
            };

            if (asset == 0) plan.Warnings.Add($"donorAsset not found for \"{input.DonorClass}\" in Costumes.json");
            if (proto == 0) plan.Warnings.Add($"donorId unresolved for \"{input.DonorClass}\" - pick the matching prototype in the GUI");
            if (!Directory.Exists(cooked)) plan.Warnings.Add($"CookedPCConsole not found at {cooked}");
            if (!File.Exists(manifest))    plan.Warnings.Add($"manifest not found at {manifest}");

            return plan;
        }

        static string TimestampedBackup(string path) => Backup.Timestamped(path);

        public static async Task<InstallResult> InstallAsync(
            InstallPlan plan, string sourceUpkPath, Action<string> log = null)
        {
            var result = new InstallResult();

            try
            {
                log?.Invoke($"[1/3] renaming UPK -> {plan.OutputUpkName}");

                List<string> stubTextures;
                {
                    var probeHeader = await RenameEngine.LoadAsync(sourceUpkPath, null);
                    stubTextures = await RenameEngine.DetectStubTexturesAsync(probeHeader);
                }
                if (stubTextures.Count > 0)
                    log?.Invoke($"  {stubTextures.Count} TFC-backed texture(s) will be left un-renamed: " +
                                string.Join(", ", stubTextures));

                var header = await RenameEngine.LoadAsync(sourceUpkPath, log);

                if ((uint)header.CompressionFlags == 0)
                {
                    long fileLen = new FileInfo(sourceUpkPath).Length;
                    int badExports = 0; string firstBad = null; long maxEnd = 0;
                    foreach (UnrealExportTableEntry exp in header.ExportTable)
                    {
                        long end = (long)exp.SerialDataOffset + exp.SerialDataSize;
                        if (end > maxEnd) maxEnd = end;
                        if (exp.SerialDataSize > 0 && end > fileLen)
                        { badExports++; firstBad ??= (exp.ObjectNameIndex?.Name ?? "?"); }
                    }
                    if (badExports > 0)
                    {
                        result.FailedStep = "verify";
                        string msg = $"source UPK is truncated/corrupt: {badExports} export(s) point past EOF " +
                            $"(file {fileLen:N0} bytes, data runs to {maxEnd:N0}; first bad: \"{firstBad}\"). " +
                            "It would crash the game on load. Re-download a complete copy.";
                        result.Steps.Add("ABORT: " + msg);
                        log?.Invoke("  ✗ ABORT: " + msg);
                        return result;
                    }
                }

                var names = new List<string>();
                for (int i = 0; i < header.NameTable.Count; i++)
                    names.Add(header.NameTable[i].Name.String);

                var excluded = new HashSet<string>(stubTextures, StringComparer.OrdinalIgnoreCase);

                List<string> collisionNames = null;
                try
                {
                    string dbPath = System.IO.Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, CostumeReferenceDb.DefaultFileName);
                    if (CostumeReferenceDb.Exists(dbPath))
                    {
                        var rep = await CollisionDetector.DetectAsync(
                            sourceUpkPath, plan.DonorClass, plan.CustomName, dbPath, null);
                        if (rep.AnySafeCollisions)
                        {
                            collisionNames = rep.Collisions;
                            log?.Invoke($"  [collision] uniquifying {collisionNames.Count} colliding " +
                                        "material/texture name(s) so they don't bind to the resident donor:");
                            foreach (var n in collisionNames) log?.Invoke($"      uniquify: {n}");
                        }
                        else
                        {
                            log?.Invoke("  [collision] no unprotected donor-name collisions detected.");
                        }
                    }
                    else
                    {
                        log?.Invoke($"  [collision] reference DB not found ({CostumeReferenceDb.DefaultFileName}); " +
                                    "token-only rename. Run CostumeRefBuilder to enable collision fixing.");
                    }
                }
                catch (Exception cex) { log?.Invoke("  [collision] detection failed: " + cex.Message); }

                string actualClass = FindCostumeClassExport(header);
                if (actualClass != null) log?.Invoke($"  costume class export: {actualClass}");
                else log?.Invoke("  ⚠ no costume class export found (marvelplayer_* with a default__ CDO)");

                {
                    string HeroOf(string s)
                    {
                        if (string.IsNullOrEmpty(s)) return null;
                        int m = s.IndexOf("marvelplayer_", StringComparison.OrdinalIgnoreCase);
                        if (m < 0) return null;
                        string tail = s[(m + "marvelplayer_".Length)..];
                        int u = tail.IndexOf('_');
                        return (u > 0 ? tail[..u] : tail).ToLowerInvariant();
                    }

                    if (!string.IsNullOrEmpty(plan.DonorClass) &&
                        !plan.DonorClass.StartsWith("MarvelPlayer_", StringComparison.OrdinalIgnoreCase))
                    {
                        result.FailedStep = "donor";
                        log?.Invoke("");
                        log?.Invoke($"  *** ABORT: \"{plan.DonorClass}\" is not a player costume "
                                  + "(donors must be MarvelPlayer_*). A TeamUp/NPC class has no "
                                  + "CostumePrototype, so the costume cannot be aliased and the "
                                  + "account crashes on logout.");
                        return result;
                    }

                    string modHero   = HeroOf(actualClass);
                    string donorHero = HeroOf(plan.DonorClass);
                    if (modHero != null && donorHero != null && modHero != donorHero)
                    {
                        result.FailedStep = "donor";
                        log?.Invoke("");
                        log?.Invoke($"  *** ABORT: this mod is a {modHero.ToUpperInvariant()} costume but the "
                                  + $"donor is {donorHero.ToUpperInvariant()} ({plan.DonorClass}).");
                        log?.Invoke("      A costume belongs to ONE hero. Installing across heroes renders the");
                        log?.Invoke("      wrong hero and can crash on equip (Array.h:575); the server rejects");
                        log?.Invoke("      it as well (UsableBy).");
                        log?.Invoke($"      Usually this means \"{modHero}\" has no costumes in Costumes.json, so");
                        log?.Invoke("      donor detection fell back to a frequency guess. Pick a donor manually.");
                        return result;
                    }
                }

                var renames = DonorDetector.BuildRenamesFromNames(
                    plan.DonorClass, plan.CustomName, names, excluded, collisionNames, actualClass);
                plan.Renames = renames;

                int repairedCount = DonorDetector.LastClassRepair.Count;
                if (repairedCount > 0)
                {
                    log?.Invoke($"  {renames.Count - repairedCount} name(s) contain the donor token");
                    log?.Invoke($"  + {repairedCount} class name(s) REPAIRED — this mod is authored on the hero's " +
                                "base class, so the donor token never matched them:");
                    foreach (var rp in DonorDetector.LastClassRepair)
                        log?.Invoke($"      {rp.From} -> {rp.To}");
                }
                else
                {
                    log?.Invoke($"  {renames.Count} name(s) contain the donor token — renaming all");
                }

                var rr = await RenameEngine.RenameLoadedAsync(
                    header, plan.OutputUpkPath, renames, log);

                if (!rr.Ok)
                {
                    result.FailedStep = "rename";
                    if (rr.NotFound.Any())
                        log?.Invoke("  renames that matched nothing: " + string.Join(", ", rr.NotFound));
                    log?.Invoke("  ABORT: no names matched the donor token. " +
                                "Wrong donor? Run Verify and check.");
                    return result;
                }
                result.Steps.Add($"UPK: {plan.OutputUpkName} ({rr.BytesWritten:N0} bytes, {rr.Applied} names)");

                var renamedTo = new HashSet<string>(renames.Select(p => p.To),
                                                    StringComparer.OrdinalIgnoreCase);
                ReportOrphanedImports(header, n => renamedTo.Contains(n), log, result.Steps);

                {
                    string want = plan.ClassPath.Contains('.')
                        ? plan.ClassPath[(plan.ClassPath.LastIndexOf('.') + 1)..]
                        : plan.ClassPath;

                    var written = await RenameEngine.LoadAsync(plan.OutputUpkPath).ConfigureAwait(false);
                    bool hasClass = written.ExportTable.Any(
                        e => string.Equals(e.ObjectNameIndex?.Name, want, StringComparison.OrdinalIgnoreCase));
                    bool hasCdo = written.ExportTable.Any(
                        e => string.Equals(e.ObjectNameIndex?.Name, "default__" + want, StringComparison.OrdinalIgnoreCase));

                    if (!hasClass)
                    {
                        log?.Invoke("");
                        log?.Invoke($"  *** CLASS \"{want}\" IS NOT EXPORTED BY THE WRITTEN UPK.");
                        log?.Invoke("      Hook 3 will resolve nothing and the costume will NOT swap -");
                        log?.Invoke("      the install otherwise looks completely healthy.");
                        log?.Invoke($"      This UPK exports \"{actualClass ?? "(none found)"}\".");
                        result.Steps.Add($"⚠ class \"{want}\" not exported - the costume will not swap");
                    }
                    else if (!hasCdo)
                    {
                        log?.Invoke($"  *** class \"{want}\" exported but its CDO \"default__{want}\" is "
                                  + "missing - the class cannot instantiate.");
                        result.Steps.Add($"⚠ CDO for \"{want}\" missing");
                    }
                }
            }
            catch (Exception ex)
            {
                result.FailedStep = "rename";
                log?.Invoke("  ABORT: " + ex.Message);
                return result;
            }

            try
            {

                plan.RequiredPackages = await DetectRequiredPackagesAsync(plan.OutputUpkPath,
                                                                          plan.CookedPcConsole, log)
                                              .ConfigureAwait(false);

                log?.Invoke("[2/3] backing up + aliasing TextureFileCacheManifest.bin");
                ManifestDoctor.EnsureBaseline(plan.ManifestPath,
                    (CostumeLibrary.ReadCostumes(plan.CustomCostumesJson)?.Count ?? 0) > 0, log);
                result.ManifestBackup = TimestampedBackup(plan.ManifestPath);
                log?.Invoke($"  backup: {Path.GetFileName(result.ManifestBackup)}");

                var donorManifest = TfcManifest.Load(plan.ManifestPath);
                var pkgs = donorManifest.Entries
                    .Select(e => e.PackageName)
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                plan.TfcAliases = DonorDetector.BuildTfcAliases(plan.Renames, pkgs);

                if (plan.TfcAliases.Count == 0)
                {
                    log?.Invoke("  no renamed texture packages — textures use stock keys, " +
                                "no aliasing needed (skipping TFC step)");
                    result.Steps.Add("manifest: no aliasing needed");

                    goto AfterTfc;
                }

                log?.Invoke($"  texture packages to alias: " +
                    string.Join(", ", plan.TfcAliases.Select(p => $"{p.From}->{p.To}")));

                var ar = TfcEngine.Alias(
                    plan.ManifestPath, plan.ManifestPath,
                    plan.TfcAliases, log);

                if (ar.Added == 0 && ar.Skipped == 0)
                {
                    result.FailedStep = "tfcalias";
                    log?.Invoke($"  ABORT: no manifest rows for any of " +
                                string.Join(", ", plan.TfcAliases.Select(p => p.From)) +
                                ". The renamed texture packages aren't in the manifest — unexpected.");
                    return result;
                }

                if (ar.Added > 0)
                {
                    int problemCount = 0;
                    foreach (var pair in plan.TfcAliases)
                    {
                        var problems = TfcEngine.Validate(plan.ManifestPath, pair.From, pair.To, ar);
                        foreach (var p in problems) { log?.Invoke($"     - {p}"); problemCount++; }
                    }
                    if (problemCount == 0)
                        log?.Invoke($"  ✓ validated {ar.Records.Count} alias(es) across " +
                                    $"{plan.TfcAliases.Count} package(s): names preserved, offsets match");
                    else
                        log?.Invoke($"  ⚠ {problemCount} alias validation problem(s) - manifest written but may not resolve");
                }

                result.Steps.Add($"manifest: +{ar.Added} rows across {plan.TfcAliases.Count} package(s) (skipped {ar.Skipped})");

                foreach (var rec in ar.Records)
                    if (!string.IsNullOrEmpty(rec.CustomFull))
                        result.TfcAliasRows.Add(rec.CustomFull);
            }
            catch (Exception ex)
            {
                result.FailedStep = "tfcalias";
                log?.Invoke("  ABORT: " + ex.Message);
                return result;
            }

            AfterTfc:

            if (plan.IconSources is { Count: > 0 })
            {
                try
                {
                    log?.Invoke("[2b] building custom icon package");

                    string donorIcon = Path.Combine(plan.CookedPcConsole, IconPackBuilder.DefaultDonorUpk);
                    plan.IconUpkPath = Path.Combine(plan.CookedPcConsole,
                                                    IconPackBuilder.UpkFileNameForEnum(plan.Enum));

                    IconPackResult icons = IconPackBuilder.Build(
                        plan.Enum, donorIcon, plan.IconUpkPath, plan.IconSources, log);

                    foreach (string s in icons.Steps) result.Steps.Add("  icons: " + s);

                    if (!icons.Ok)
                    {
                        result.FailedStep = "icons";
                        log?.Invoke("  ABORT: icon package build failed (" + icons.FailedStep + ")");
                        return result;
                    }

                    plan.IconPackage = icons.PackageFName;
                    plan.IconPatches = icons.Patches;

                    if (icons.RemainingTfcTextures.Count > 0 && File.Exists(plan.ManifestPath))
                    {
                        plan.IconTfcAlias = new AliasPair
                        {
                            From = IconPackBuilder.DonorPackageName,
                            To   = IconPackBuilder.PackageNameForEnum(plan.Enum),
                        };
                        TfcAlias.Core.AliasResult ar = TfcEngine.Alias(
                            plan.ManifestPath, plan.ManifestPath,
                            new[] { plan.IconTfcAlias }, log);
                        foreach (var rec in ar.Records)
                            result.TfcAliasRows.Add(rec.CustomFull);
                        result.Steps.Add($"  icons: {icons.RemainingTfcTextures.Count} TFC texture(s) " +
                                         $"aliased {plan.IconTfcAlias.From} -> {plan.IconTfcAlias.To}");
                    }
                }
                catch (Exception ex)
                {
                    result.FailedStep = "icons";
                    log?.Invoke("  ABORT: " + ex.Message);
                    return result;
                }
            }

            try
            {

                log?.Invoke($"[3/3] writing the {CostumeConfig.InUseName(plan.CustomCostumesJson)} entry");
                if (CostumeConfig.Exists(plan.CustomCostumesJson))
                    result.JsonBackup = TimestampedBackup(CostumeConfig.ExistingPath(plan.CustomCostumesJson));

                AppendCostumeEntry(plan);
                AppendServerEntry(plan);
                result.Steps.Add($"JSON: entry \"{plan.CustomName}\" (enum {plan.Enum})");
                result.Steps.Add($"server: ServerCostumes.json updated (copy to the server)");

                CopyCostumesTableIfNeeded(plan, log, result);
            }
            catch (Exception ex)
            {
                result.FailedStep = "json";
                log?.Invoke("  ABORT: " + ex.Message);
                return result;
            }

            result.Ok = true;

            try
            {
                InstallLedger.Upsert(new InstallRecord
                {
                    Name = plan.CustomName,
                    DisplayName = plan.DisplayName,
                    Enum = plan.Enum,
                    InstalledUtc = DateTime.UtcNow.ToString("o"),
                    UpkPath = plan.OutputUpkPath,
                    CustomCostumesJson = plan.CustomCostumesJson,
                    ManifestPath = plan.ManifestPath,
                    TfcPackage = plan.TfcAlias?.To,
                    ManifestBackup = result.ManifestBackup,
                    JsonBackup = result.JsonBackup,
                    TfcAliasRows = new List<string>(result.TfcAliasRows),
                    IconUpkPath = plan.IconUpkPath,
                    IconPackage = plan.IconPackage,

                    TfcAliasPairs = (plan.TfcAliases ?? new List<AliasPair>())
                        .Select(p => new AliasPair(p.From, p.To)).ToList(),
                });
                log?.Invoke("  ledger: recorded install (installed.json)");
            }
            catch (Exception ex)
            {
                log?.Invoke("  (ledger write skipped: " + ex.Message + ")");
            }

            log?.Invoke("");
            log?.Invoke("DONE. Restart the client (with the launcher) and equip via the server command.");
            return result;
        }

        public static List<InstalledCostume> ListInstalled(string gameRoot)
            => CostumeLibrary.ListInstalled(gameRoot);

        public sealed class IconUpdateResult
        {
            public bool Ok { get; set; }
            public string FailedStep { get; set; }
            public List<string> Steps { get; } = new List<string>();
        }

        public static List<string> VerifyInstalled(string gameRoot, DonorTables tables)
        {
            var problems = new List<string>();
            var (cooked, manifestPath, bin) = ResolvePaths(gameRoot);
            string jsonPath = Path.Combine(bin, "CustomCostumes.json");

            if (!CostumeConfig.Exists(jsonPath))
            {
                problems.Add($"CustomCostumes.json not found at {jsonPath}");
                return problems;
            }

            JsonObject root;
            try { root = JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath)) as JsonObject; }
            catch (Exception ex) { problems.Add("CustomCostumes.json does not parse: " + ex.Message); return problems; }
            if (root?["costumes"] is not JsonArray arr) { problems.Add("CustomCostumes.json has no \"costumes\" array"); return problems; }

            var ledger = InstallLedger.Read();
            var seenEnums = new Dictionary<uint, string>();
            string manifestText = File.Exists(manifestPath) ? SafeReadManifest(manifestPath) : null;

            foreach (var n in arr)
            {
                if (n is not JsonObject o) continue;
                string name = (string)o["name"] ?? "(unnamed)";
                uint en = o["enum"]?.GetValue<uint>() ?? 0;

                if (seenEnums.TryGetValue(en, out string other))
                    problems.Add($"{name}: enum {en} is ALSO used by {other} - one of them will not work");
                else
                    seenEnums[en] = name;

                string upk = (string)o["upk"];
                if (!string.IsNullOrWhiteSpace(upk))
                {
                    string upkPath = Path.Combine(cooked, upk);
                    if (!File.Exists(upkPath))
                        problems.Add($"{name}: UPK missing - {upk}");
                    else if (new FileInfo(upkPath).Length < 512)
                        problems.Add($"{name}: UPK is suspiciously small ({new FileInfo(upkPath).Length} bytes) - {upk}");
                }

                string donorClass = (string)o["donorClass"];
                if (!string.IsNullOrWhiteSpace(donorClass) && tables != null &&
                    !tables.AssetIds.ContainsKey(donorClass))
                    problems.Add($"{name}: donor '{donorClass}' is not in Costumes.json");

                string iconPkg = (string)o["iconPackage"];
                if (!string.IsNullOrWhiteSpace(iconPkg))
                {
                    string iconUpk = Path.Combine(cooked, IconPackBuilder.UpkFileNameForEnum(en));
                    if (!File.Exists(iconUpk))
                        problems.Add($"{name}: iconPackage '{iconPkg}' declared but {Path.GetFileName(iconUpk)} is missing");
                }

                string tfcTo = (string)o["tfcAlias"]?["to"];
                if (manifestText != null && !string.IsNullOrWhiteSpace(tfcTo) &&
                    manifestText.IndexOf(tfcTo, StringComparison.OrdinalIgnoreCase) < 0)
                    problems.Add($"{name}: no TFC manifest rows for '{tfcTo}' - textures will fall back or orphan");

                if (!ledger.Any(r => r.Enum == en))
                    problems.Add($"{name}: no ledger record - uninstall cannot clean this one up automatically");
            }

            return problems;
        }

        static string SafeReadManifest(string path)
        {
            try { return System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path)); }
            catch { return null; }
        }

        public static IconUpdateResult UpdateIcons(string gameRoot, uint enumId,
                                                   List<IconSource> sources, string displayName = null,
                                                   Action<string> log = null, string serverDir = null)
        {
            var res = new IconUpdateResult();
            var (cooked, manifestPath, bin) = ResolvePaths(gameRoot);
            string jsonPath = Path.Combine(bin, "CustomCostumes.json");

            if (!CostumeConfig.Exists(jsonPath))
            {
                res.FailedStep = "json";
                log?.Invoke("CustomCostumes.json not found.");
                return res;
            }

            JsonNode root;
            try { root = JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath)); }
            catch (Exception ex) { res.FailedStep = "json"; log?.Invoke("JSON parse failed: " + ex.Message); return res; }

            JsonObject entry = null;
            if (root?["costumes"] is JsonArray arr)
                foreach (var n in arr)
                    if (n is JsonObject o && o["enum"]?.GetValue<uint>() == enumId) { entry = o; break; }

            if (entry == null)
            {
                res.FailedStep = "json";
                log?.Invoke($"No installed costume with enum {enumId}.");
                return res;
            }

            bool hadIcons = entry["iconPackage"] != null;

            string donorIcon = Path.Combine(cooked, IconPackBuilder.DefaultDonorUpk);
            string iconUpk = Path.Combine(cooked, IconPackBuilder.UpkFileNameForEnum(enumId));

            log?.Invoke("[1/3] rebuilding icon package");
            IconPackResult icons = IconPackBuilder.Build(enumId, donorIcon, iconUpk, sources, log);
            foreach (string s in icons.Steps) res.Steps.Add("  icons: " + s);

            if (!icons.Ok)
            {
                res.FailedStep = "icons";
                log?.Invoke("  ABORT: icon package build failed (" + icons.FailedStep + ")");
                return res;
            }

            if (!hadIcons && icons.RemainingTfcTextures.Count > 0)
            {
                if (File.Exists(manifestPath))
                {
                    log?.Invoke("[2/3] first icons for this costume - aliasing the TFC manifest");
                    TimestampedBackup(manifestPath);
                    TfcEngine.Alias(manifestPath, manifestPath, new[]
                    {
                        new AliasPair
                        {
                            From = IconPackBuilder.DonorPackageName,
                            To   = IconPackBuilder.PackageNameForEnum(enumId),
                        }
                    }, log);
                    res.Steps.Add("manifest: icon alias added");
                }
            }
            else
            {
                log?.Invoke("[2/3] manifest unchanged (icon package name is enum-derived)");
            }

            log?.Invoke("[3/3] rewriting icon entries");
            TimestampedBackup(CostumeConfig.ExistingPath(jsonPath));

            entry["iconPackage"] = icons.PackageFName;
            var iconArr = new JsonArray();
            foreach (IconPack.Core.IconPatch p in icons.Patches)
                iconArr.Add(new JsonObject { ["off"] = p.OffsetHex, ["asset"] = p.AssetHex, ["path"] = p.Path });
            entry["protoIcons"] = iconArr;

            if (!string.IsNullOrWhiteSpace(displayName))
                entry["displayName"] = displayName;

            var opts = new JsonSerializerOptions
            {
                WriteIndented = true,
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
            };
            CostumeConfig.WriteAllText(jsonPath, root.ToJsonString(opts));
            res.Steps.Add($"json: {icons.Patches.Count} icon patch(es)");

            string serverJsonPath = !string.IsNullOrWhiteSpace(serverDir)
                ? Path.Combine(serverDir, "ServerCostumes.json")
                : Path.Combine(AppContext.BaseDirectory, "ServerCostumes.json");

            RegenerateServerFromClient(jsonPath, serverJsonPath, log);
            res.Steps.Add("regenerated ServerCostumes.json");

            try
            {
                var rec = InstallLedger.Read().FirstOrDefault(r => r.Enum == enumId);
                if (rec != null)
                {
                    rec.IconUpkPath = iconUpk;
                    rec.IconPackage = icons.PackageFName;
                    InstallLedger.Upsert(rec);
                    res.Steps.Add("ledger updated");
                }
            }
            catch (Exception ex) { log?.Invoke("  (ledger update skipped: " + ex.Message + ")"); }

            res.Ok = true;
            log?.Invoke("");
            log?.Invoke("DONE. Restart the client to see the new icons - no reinstall needed.");
            return res;
        }

        public sealed class UninstallResult
        {
            public bool Ok { get; set; }
            public List<string> Steps { get; } = new();
            public string Error { get; set; }
        }

        public static UninstallResult Uninstall(string gameRoot, uint enumId, Action<string> log = null, string serverDir = null)
        {
            var res = new UninstallResult();
            try
            {
                var (cooked, manifest, bin) = ResolvePaths(gameRoot);
                string jsonPath = Path.Combine(bin, "CustomCostumes.json");

                string serverJsonPath = !string.IsNullOrWhiteSpace(serverDir)
                    ? Path.Combine(serverDir, "ServerCostumes.json")
                    : Path.Combine(AppContext.BaseDirectory, "ServerCostumes.json");

                var ledgerRec = InstallLedger.Read().FirstOrDefault(r => r.Enum == enumId);

                JsonObject root = null;
                JsonObject entry = null;
                if (CostumeConfig.Exists(jsonPath))
                {
                    root = JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath)) as JsonObject;
                    if (root?["costumes"] is JsonArray a)
                        entry = a.FirstOrDefault(n => n is JsonObject o &&
                                    o["enum"]?.GetValue<uint>() == enumId) as JsonObject;
                }

                if (ledgerRec == null && entry == null)
                {
                    res.Error = $"no costume with enum {enumId} found in the ledger or CustomCostumes.json";
                    log?.Invoke("  " + res.Error);
                    return res;
                }

                string display = ledgerRec?.DisplayName ?? (string)entry?["name"] ?? $"enum {enumId}";
                log?.Invoke($"── UNINSTALL: {display} (enum {enumId}) ──");

                string upkPath = ledgerRec?.UpkPath;
                if (string.IsNullOrEmpty(upkPath) && entry?["upk"] != null)
                    upkPath = Path.Combine(cooked, (string)entry["upk"]);
                if (!string.IsNullOrEmpty(upkPath) && File.Exists(upkPath))
                {
                    File.Delete(upkPath);
                    log?.Invoke($"[1/4] deleted UPK: {Path.GetFileName(upkPath)}");
                    res.Steps.Add("deleted UPK");
                }
                else { log?.Invoke("[1/4] UPK not found (already gone)"); res.Steps.Add("UPK already absent"); }

                string iconUpk = ledgerRec?.IconUpkPath;
                if (string.IsNullOrEmpty(iconUpk))
                {
                    string derived = Path.Combine(cooked, IconPackBuilder.UpkFileNameForEnum(enumId));
                    if (File.Exists(derived)) iconUpk = derived;
                }
                if (!string.IsNullOrEmpty(iconUpk) && File.Exists(iconUpk))
                {
                    File.Delete(iconUpk);
                    log?.Invoke($"[1b/4] deleted icon UPK: {Path.GetFileName(iconUpk)}");
                    res.Steps.Add("deleted icon UPK");
                }

                string manifestPath = ledgerRec?.ManifestPath ?? manifest;
                var exactRows = ledgerRec?.TfcAliasRows;
                string customPkg = ledgerRec?.TfcPackage;
                if (string.IsNullOrEmpty(customPkg) && entry?["tfcAlias"]?["to"] != null)
                    customPkg = (string)entry["tfcAlias"]["to"];

                if (File.Exists(manifestPath))
                {
                    log?.Invoke("[2/4] removing TFC alias rows");
                    string bak = TimestampedBackup(manifestPath);
                    log?.Invoke($"  backup: {Path.GetFileName(bak)}");

                    var ourPkgs = new List<string> { customPkg,
                                                     IconPackBuilder.PackageNameForEnum(enumId) };
                    var donorPkgs = new List<string>();
                    foreach (AliasPair p in ledgerRec?.TfcAliasPairs ?? new List<AliasPair>())
                    {
                        if (!string.IsNullOrWhiteSpace(p.To)) ourPkgs.Add(p.To);
                        if (!string.IsNullOrWhiteSpace(p.From)) donorPkgs.Add(p.From);
                    }
                    donorPkgs.Add(IconPackBuilder.DonorPackageName);

                    int removed = TfcEngine.Unalias(manifestPath, manifestPath, exactRows, customPkg,
                                                    log, ourPkgs, donorPkgs);
                    res.Steps.Add($"manifest: -{removed} rows");

                    ManifestDoctor.GuardAfterRemoval(manifestPath, log);
                }
                else { log?.Invoke("[2/4] manifest not found"); res.Steps.Add("manifest absent"); }

                if (root != null && root["costumes"] is JsonArray arr2)
                {
                    if (CostumeConfig.Exists(jsonPath)) TimestampedBackup(CostumeConfig.ExistingPath(jsonPath));

                    JsonObject retired = null;
                    for (int i = arr2.Count - 1; i >= 0; i--)
                    {
                        if (arr2[i] is JsonObject o && o["enum"]?.GetValue<uint>() == enumId)
                        {
                            retired = new JsonObject
                            {
                                ["name"]        = (string)o["name"],
                                ["displayName"] = (string)(o["displayName"] ?? o["name"]),
                                ["prototypeId"] = (string)o["customId"],
                                ["donorId"]     = (string)o["donorId"],
                                ["enum"]        = enumId,
                            };
                            arr2.RemoveAt(i);
                        }
                    }

                    var opts = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
                    };
                    CostumeConfig.WriteAllText(jsonPath, root.ToJsonString(opts));
                    log?.Invoke($"[3/4] removed the {CostumeConfig.InUseName(jsonPath)} entry");
                    res.Steps.Add("removed config entry");
                    RegenerateServerFromClient(jsonPath, serverJsonPath, log);
                    res.Steps.Add("regenerated ServerCostumes.json");

                    if (retired != null && QueuePendingPurge(serverJsonPath, retired, log))
                        res.Steps.Add("queued token purge (runs on next server start)");
                }
                else { log?.Invoke("[3/4] no costume config present"); }

                InstallLedger.Remove(ledgerRec?.Name ?? display);
                log?.Invoke("[4/4] removed ledger record");
                res.Steps.Add("removed ledger record");

                res.Ok = true;
                log?.Invoke("");
                log?.Invoke("UNINSTALL COMPLETE. Restart the client to see the change.");
                return res;
            }
            catch (Exception ex)
            {
                res.Error = ex.Message;
                log?.Invoke("  UNINSTALL ERROR: " + ex.Message);
                return res;
            }
        }

        static bool QueuePendingPurge(string serverJsonPath, JsonObject costume, Action<string> log = null,
                                      string mode = "delete")
        {
            if (string.IsNullOrWhiteSpace(serverJsonPath) || costume?["enum"] == null) return false;

            string dir = Path.GetDirectoryName(serverJsonPath);
            if (string.IsNullOrEmpty(dir)) return false;
            string path = Path.Combine(dir, PendingPurgeFileName);

            var arr = new JsonArray();
            var seen = new HashSet<uint>();

            if (File.Exists(path))
            {
                try
                {
                    if (JsonNode.Parse(File.ReadAllText(path)) is JsonArray prev)
                    {
                        foreach (var p in prev)
                        {
                            if (p is not JsonObject po || po["enum"] == null) continue;
                            if (seen.Add(po["enum"].GetValue<uint>()))
                                arr.Add(po.DeepClone());
                        }
                    }
                }
                catch {  }
            }

            if (seen.Add(costume["enum"].GetValue<uint>()))
            {
                var entry = costume.DeepClone().AsObject();
                entry["mode"] = mode;
                arr.Add(entry);
            }

            var o = new JsonSerializerOptions
            {
                WriteIndented = true,
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
            };
            File.WriteAllText(path, arr.ToJsonString(o));
            log?.Invoke($"  queued token purge in {PendingPurgeFileName} (runs on next server start)");
            return true;
        }

        static void RegenerateServerFromClient(string clientJsonPath, string serverJsonPath,
                                               Action<string> log = null)
        {
            if (!CostumeConfig.Exists(clientJsonPath)) return;
            JsonNode clientRoot;
            try { clientRoot = JsonNode.Parse(CostumeConfig.ReadAllText(clientJsonPath)); }
            catch { return; }
            if (clientRoot?["costumes"] is not JsonArray clientArr) return;

            var outArr = new JsonArray();
            foreach (var node in clientArr)
            {
                if (node is not JsonObject c) continue;
                var en = c["enum"];
                if (en == null) continue;
                string searchName = InternalNameFromClass((string)c["class"]);
                string display = (string)c["name"] ?? searchName;
                if (string.IsNullOrEmpty(searchName)) searchName = display;
                var entry = new JsonObject
                {
                    ["name"]        = searchName,
                    ["displayName"] = display,
                    ["prototypeId"] = c["customId"] != null ? (string)c["customId"] : null,
                    ["donorId"]     = c["donorId"] != null ? (string)c["donorId"] : null,
                    ["enum"]        = en.GetValue<uint>(),
                };

                if (c["storePrice"] != null)
                {
                    try { entry["storePrice"] = c["storePrice"].GetValue<long>(); }
                    catch {  }
                }

                if (c["hotspots"] is JsonArray hs && hs.Count > 0)
                {
                    var copy = new JsonArray();
                    foreach (JsonNode h in hs)
                        if (h is JsonObject ho && ho["stock"] != null && ho["forged"] != null
                                               && ho["enum"] != null)
                            copy.Add(new JsonObject
                            {
                                ["stock"]  = (string)ho["stock"],
                                ["forged"] = (string)ho["forged"],
                                ["enum"]   = ho["enum"].GetValue<uint>(),
                            });
                    if (copy.Count > 0) entry["hotspots"] = copy;
                }

                outArr.Add(entry);
            }
            var root = new JsonObject { ["costumes"] = outArr };

            var opts = new JsonSerializerOptions
            {
                WriteIndented = true,
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
            };
            File.WriteAllText(serverJsonPath, root.ToJsonString(opts));
            log?.Invoke($"  ServerCostumes.json regenerated ({outArr.Count} costume(s))");

            PublishFxPackList(serverJsonPath, log);
            MirrorFxRegistryToGameFolder(clientJsonPath, log);
        }

        static void MirrorFxRegistryToGameFolder(string clientJsonPath, Action<string> log = null)
        {
            try
            {
                string bin = Path.GetDirectoryName(clientJsonPath);
                if (string.IsNullOrWhiteSpace(bin) || !Directory.Exists(bin)) return;

                string dest = Path.Combine(bin, "fxpacks.json");
                List<FxPack> packs = FxPackRegistry.Read();

                if (packs.Count == 0 && File.Exists(dest)) return;

                FxPackRegistry.Write(packs, dest);
                log?.Invoke($"  fxpacks.json mirrored to the game folder ({packs.Count} pack(s))");
            }
            catch (Exception ex)
            {

                log?.Invoke("  could not mirror fxpacks.json: " + ex.Message);
            }
        }

        static void PublishFxPackList(string serverJsonPath, Action<string> log = null)
        {
            try
            {
                string dir = Path.GetDirectoryName(serverJsonPath);
                if (string.IsNullOrWhiteSpace(dir)) return;

                var packs = new JsonArray();
                foreach (FxPack p in FxPackRegistry.Read())
                {
                    if (string.IsNullOrWhiteSpace(p?.Token)) continue;
                    packs.Add(new JsonObject
                    {
                        ["token"]       = p.Token,
                        ["displayName"] = p.DisplayName ?? p.Token,
                        ["hero"]        = p.Hero,
                        ["effects"]     = p.Effects?.Count ?? 0,
                    });
                }

                var opts = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
                };

                File.WriteAllText(Path.Combine(dir, "ServerFxPacks.json"),
                    new JsonObject { ["packs"] = packs }.ToJsonString(opts));

                log?.Invoke($"  ServerFxPacks.json published ({packs.Count} pack(s))");
            }
            catch (Exception ex)
            {

                log?.Invoke("  could not publish ServerFxPacks.json: " + ex.Message);
            }
        }

        static JsonArray BuildChain(InstallPlan plan)
        {
            var chain = new JsonArray();
            foreach (string dep in plan.RequiredPackages ?? new List<string>())
                chain.Add(dep);
            chain.Add(plan.PackageName);
            return chain;
        }

        static void AppendCostumeEntry(InstallPlan plan)
        {
            JsonObject root;
            if (CostumeConfig.Exists(plan.CustomCostumesJson))
            {
                var text = CostumeConfig.ReadAllText(plan.CustomCostumesJson);
                root = JsonNode.Parse(text) as JsonObject ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            if (root["costumes"] is not JsonArray arr)
            {
                arr = new JsonArray();
                root["costumes"] = arr;
            }

            string name = plan.CustomName;
            for (int i = arr.Count - 1; i >= 0; i--)
            {
                if (arr[i] is JsonObject o &&
                    (string.Equals((string)o["name"], plan.DisplayName, StringComparison.OrdinalIgnoreCase)
                     || (o["enum"] != null && o["enum"].GetValue<uint>() == plan.Enum)))
                {
                    arr.RemoveAt(i);
                }
            }

            var entry = new JsonObject
            {
                ["name"]       = plan.DisplayName,
                ["enum"]       = plan.Enum,
                ["customId"]   = $"0x{plan.CustomId:X16}",
                ["donorId"]    = $"0x{plan.DonorId:X16}",
                ["donorClass"] = plan.DonorClass,

                ["donorAsset"] = $"0x{plan.DonorAsset:X16}",
                ["class"]      = plan.ClassPath,

                ["chain"]      = BuildChain(plan),
                ["upk"]        = plan.OutputUpkName,
                ["tfcAlias"]   = new JsonObject
                {
                    ["from"] = plan.TfcAlias.From,
                    ["to"]   = plan.TfcAlias.To,
                },
            };

            if (plan.StorePrice > 0) entry["storePrice"] = plan.StorePrice;

            if (!string.IsNullOrEmpty(plan.IconPackage) && plan.IconPatches is { Count: > 0 })
            {
                entry["iconPackage"] = plan.IconPackage;

                var icons = new JsonArray();
                foreach (IconPack.Core.IconPatch p in plan.IconPatches)
                {
                    icons.Add(new JsonObject
                    {
                        ["off"]   = p.OffsetHex,
                        ["asset"] = p.AssetHex,
                        ["path"]  = p.Path,
                    });
                }
                entry["protoIcons"] = icons;
            }

            if (!string.IsNullOrWhiteSpace(plan.DisplayName))
                entry["displayName"] = plan.DisplayName;

            if (plan.FxOptIn)
                entry["effects"] = BuildEffects(plan);

            arr.Add(entry);

            var opts = new JsonSerializerOptions
            {
                WriteIndented = true,

                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
            };
            CostumeConfig.WriteAllText(plan.CustomCostumesJson, root.ToJsonString(opts));
        }

        static JsonArray BuildEffects(InstallPlan plan)
        {
            var arr = new JsonArray();
            foreach (var e in (plan.Effects ?? new List<FxEffect>())
                              .OrderBy(x => x.EffectName ?? x.Package, StringComparer.OrdinalIgnoreCase))
            {
                if (e == null || e.From == 0 || string.IsNullOrWhiteSpace(e.Package)) continue;
                arr.Add(new JsonObject
                {
                    ["from"] = "0x" + e.From.ToString("X16"),
                    ["package"] = e.Package,
                    ["class"] = e.ClassPath,
                });
            }
            return arr;
        }

        static void AppendServerEntry(InstallPlan plan)
        {

            if (!CostumeConfig.Exists(plan.CustomCostumesJson)) return;

            JsonNode clientRoot;
            try { clientRoot = JsonNode.Parse(CostumeConfig.ReadAllText(plan.CustomCostumesJson)); }
            catch { return; }

            if (clientRoot?["costumes"] is not JsonArray clientArr) return;

            var outArr = new JsonArray();
            foreach (var node in clientArr)
            {
                if (node is not JsonObject c) continue;

                var    en    = c["enum"];
                var    cid   = c["customId"];

                if (en == null) continue;

                string searchName = InternalNameFromClass((string)c["class"]);

                string display    = (string)c["displayName"] ?? (string)c["name"] ?? searchName;
                if (string.IsNullOrEmpty(searchName)) searchName = display;

                outArr.Add(new JsonObject
                {
                    ["name"]        = searchName,
                    ["displayName"] = display,
                    ["prototypeId"] = cid != null ? (string)cid : null,

                    ["donorId"]     = c["donorId"] != null ? (string)c["donorId"] : null,
                    ["enum"]        = en.GetValue<uint>(),
                });
            }

            var root = new JsonObject { ["costumes"] = outArr };
            var opts = new JsonSerializerOptions
            {
                WriteIndented = true,
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
            };
            File.WriteAllText(plan.ServerCostumesJson, root.ToJsonString(opts));
        }

        static void CopyCostumesTableIfNeeded(InstallPlan plan, Action<string> log, InstallResult result)
        {
            string dest = Path.Combine(plan.BinariesWin64, "Costumes.json");
            if (File.Exists(dest))
            {
                log?.Invoke("  Costumes.json already in the game folder — leaving it");
                return;
            }

            string src = Path.Combine(AppContext.BaseDirectory, "Costumes.json");
            if (!File.Exists(src))
            {
                log?.Invoke("  ⚠ Costumes.json not found next to the manager — the DLL " +
                            "needs it in Binaries\\Win64 to resolve donors. Copy it manually.");
                return;
            }

            try
            {
                File.Copy(src, dest);
                log?.Invoke($"  copied Costumes.json -> {plan.BinariesWin64}");
                result.Steps.Add("Costumes.json: copied to the game folder (DLL donor table)");
            }
            catch (Exception ex)
            {
                log?.Invoke("  ⚠ couldn't copy Costumes.json: " + ex.Message);
            }
        }

        static string InternalNameFromClass(string classPath)
        {
            if (string.IsNullOrEmpty(classPath)) return "";
            int dot = classPath.LastIndexOf('.');
            string cls = dot >= 0 ? classPath.Substring(dot + 1) : classPath;
            const string prefix = "MarvelPlayer_";
            if (cls.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                cls = cls.Substring(prefix.Length);
            int us = cls.IndexOf('_');
            return us >= 0 && us + 1 < cls.Length ? cls.Substring(us + 1) : cls;
        }
    }
}
