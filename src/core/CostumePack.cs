using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using IconPack.Core;
using TfcAlias.Core;
using UpkRename.Core;

namespace CostumeManager.Core
{

    public static partial class Installer
    {
        public const string PackExtension = CostumePackFile.Extension;

        public static async Task<List<string>> DetectRequiredPackagesAsync(
            string upkPath, string cookedDir, Action<string> log = null)
        {
            var needed = new List<string>();
            try
            {
                var header = await RenameEngine.LoadAsync(upkPath).ConfigureAwait(false);

                string ownPkg = Path.GetFileNameWithoutExtension(upkPath).ToLowerInvariant();

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var imp in header.ImportTable)
                {
                    string cls = imp.ClassNameIndex?.Name ?? "";
                    string nm  = imp.ObjectNameIndex?.Name ?? "";

                    if (!string.Equals(cls, "class", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!nm.StartsWith("marvelplayer_", StringComparison.OrdinalIgnoreCase)) continue;

                    string tail = nm.Substring("marvelplayer_".Length);
                    if (!tail.Contains('_')) continue;

                    string pkg = ("uc__" + nm + "_sf").ToLowerInvariant();
                    if (pkg == ownPkg || !seen.Add(pkg)) continue;

                    string file = Path.Combine(cookedDir, "UC__" + nm + "_SF.upk");
                    if (!File.Exists(file))
                    {
                        log?.Invoke($"  ⚠ needs \"{nm}\" but UC__{nm}_SF.upk is not in CookedPCConsole "
                                  + "- NOT adding it to the chain (driving the loader at a missing "
                                  + "package corrupts loader state)");
                        continue;
                    }
                    needed.Add(pkg);
                    log?.Invoke($"  chain: needs \"{pkg}\" loaded first (subclasses {nm})");
                }
            }
            catch (Exception ex) { log?.Invoke("  (dependency scan skipped: " + ex.Message + ")"); }
            return needed;
        }

        public static string FindCostumeClassExport(UpkManager.Models.UpkFile.UnrealHeader header)
        {
            return CostumeManager.Core.UpkClassExports.Find(header, "marvelplayer_").FirstOrDefault();
        }

        public sealed class OrphanImport
        {
            public string FullPath   { get; set; }
            public string ClassName  { get; set; }
            public string BrokenName { get; set; }
            public bool   IsTexture => string.Equals(ClassName, "texture2d", StringComparison.OrdinalIgnoreCase);
            public override string ToString() => $"{ClassName} {FullPath}   (renamed: {BrokenName})";
        }

        public static List<OrphanImport> FindOrphanedImports(
            UpkManager.Models.UpkFile.UnrealHeader header, Func<string, bool> wasRenamedByUs)
        {
            var found = new List<OrphanImport>();
            if (header?.ImportTable == null || wasRenamedByUs == null) return found;

            foreach (UpkManager.Models.UpkFile.Tables.UnrealImportTableEntry imp in header.ImportTable)
            {

                var parts = new List<string>();
                string own = imp.ObjectNameIndex?.Name;
                if (!string.IsNullOrEmpty(own)) parts.Add(own);

                int outerRef = imp.OuterReference;
                for (int guard = 0; outerRef != 0 && guard < 32; guard++)
                {
                    UpkManager.Models.UpkFile.Tables.UnrealObjectTableEntryBase entry;
                    try { entry = header.GetObjectTableEntry(outerRef); }
                    catch { break; }
                    if (entry == null) break;
                    string on = entry.ObjectNameIndex?.Name;
                    if (!string.IsNullOrEmpty(on)) parts.Add(on);
                    if (entry is UpkManager.Models.UpkFile.Tables.UnrealImportTableEntry oi)
                        outerRef = oi.OuterReference;
                    else break;
                }

                string broken = parts.FirstOrDefault(wasRenamedByUs);
                if (broken == null) continue;

                parts.Reverse();
                found.Add(new OrphanImport
                {
                    FullPath   = string.Join(".", parts),
                    ClassName  = imp.ClassNameIndex?.Name ?? "?",
                    BrokenName = broken,
                });
            }
            return found;
        }

        public static bool ReportOrphanedImports(UpkManager.Models.UpkFile.UnrealHeader header,
                                                 Func<string, bool> wasRenamedByUs,
                                                 Action<string> log,
                                                 List<string> steps = null)
        {
            List<OrphanImport> orphans = FindOrphanedImports(header, wasRenamedByUs);
            if (orphans.Count == 0) return true;

            int tex = orphans.Count(o => o.IsTexture);
            log?.Invoke("");
            log?.Invoke($"  *** {orphans.Count} ORPHANED IMPORT(S) - the rename broke a name this");
            log?.Invoke("      package imports from ANOTHER package, so it resolves to NULL:");
            foreach (OrphanImport o in orphans) log?.Invoke("        " + o);
            if (tex > 0)
            {
                log?.Invoke($"      {tex} of them is a TEXTURE. If the mesh's material samples it the");
                log?.Invoke("      costume renders WHITE. (A mod carrying its own textures is fine -");
                log?.Invoke("      check the model in game before assuming it is broken.)");
            }
            log?.Invoke("      This mod's package name is used BOTH as its own mesh group and as the");
            log?.Invoke("      outer of an import, so renaming it cannot satisfy both.");
            steps?.Add($"⚠ {orphans.Count} orphaned import(s){(tex > 0 ? $", {tex} texture" : "")} - see log");
            return false;
        }

        public static async Task<int> AuditImportsAsync(string gameRoot, Action<string> log = null)
        {
            var (cooked, _, bin) = ResolvePaths(gameRoot);
            string cfgPath = CostumeConfig.ExistingPath(bin);
            if (!CostumeConfig.Exists(cfgPath)) { log?.Invoke("no costume config found"); return 0; }

            JsonObject root;
            try { root = JsonNode.Parse(CostumeConfig.ReadAllText(cfgPath)) as JsonObject; }
            catch (Exception ex) { log?.Invoke("config does not parse: " + ex.Message); return 0; }
            if (root == null) return 0;

            int flagged = 0, scanned = 0;
            foreach (string key in new[] { "costumes", "disabled" })
            {
                if (root[key] is not JsonArray arr) continue;
                foreach (var n in arr)
                {
                    if (n is not JsonObject o) continue;
                    string name = (string)o["name"] ?? "(unnamed)";
                    string upk  = (string)o["upk"] ?? (string)o["package"];
                    string cls  = (string)o["class"] ?? "";
                    if (string.IsNullOrWhiteSpace(upk)) continue;

                    string bare = cls.Contains('.') ? cls[(cls.LastIndexOf('.') + 1)..] : cls;
                    int us = bare.LastIndexOf('_');
                    if (us < 0) continue;
                    string token = bare[(us + 1)..];
                    string hero  = bare[..us];
                    if (token.Length < 2) continue;

                    if (hero.StartsWith("MarvelPlayer_", StringComparison.OrdinalIgnoreCase))
                        hero = hero["MarvelPlayer_".Length..];
                    string mark = hero + "_" + token;

                    string path = Path.Combine(cooked, upk.EndsWith(".upk", StringComparison.OrdinalIgnoreCase)
                                                       ? upk : upk + ".upk");
                    if (!File.Exists(path)) continue;

                    scanned++;

                    string ownPkg = Path.GetFileNameWithoutExtension(path);

                    List<OrphanImport> orphans;
                    try
                    {
                        var header = await RenameEngine.LoadAsync(path).ConfigureAwait(false);
                        orphans = FindOrphanedImports(header,
                            s => s.IndexOf(mark, StringComparison.OrdinalIgnoreCase) >= 0
                              && !s.Equals(ownPkg, StringComparison.OrdinalIgnoreCase));
                    }
                    catch (Exception ex) { log?.Invoke($"  {name}: could not read ({ex.Message})"); continue; }

                    if (orphans.Count == 0) continue;
                    flagged++;

                    int tex = orphans.Count(x => x.IsTexture);
                    log?.Invoke($"  {name}  ({mark}): {orphans.Count} orphaned import(s)"
                              + (tex > 0 ? $", {tex} TEXTURE" : ", none are textures"));
                    foreach (OrphanImport x in orphans) log?.Invoke("      " + x);
                    if (tex == 0)
                        log?.Invoke("      no texture involved - will not cause a white mesh");
                }
            }

            log?.Invoke(flagged == 0
                ? $"scanned {scanned} costume(s) - no orphaned imports"
                : $"scanned {scanned} costume(s) - {flagged} with orphaned imports (see above)");
            return flagged;
        }

        public static async Task<int> RebuildChainsAsync(string gameRoot, Action<string> log = null)
        {
            var (cooked, _, bin) = ResolvePaths(gameRoot);
            string jsonPath = CostumeConfig.ExistingPath(bin);
            if (!CostumeConfig.Exists(jsonPath)) { log?.Invoke("no costume config found"); return 0; }

            JsonObject root;
            try { root = JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath)) as JsonObject; }
            catch (Exception ex) { log?.Invoke("config does not parse: " + ex.Message); return 0; }
            if (root == null) return 0;

            int total = 0, done = 0;
            foreach (string k in new[] { "costumes", "disabled" })
                if (root[k] is JsonArray a0) total += a0.Count;
            log?.Invoke($"scanning {total} costume(s) for load-chain dependencies...");

            int changed = 0;
            foreach (string key in new[] { "costumes", "disabled" })
            {
                if (root[key] is not JsonArray arr) continue;
                foreach (var n in arr)
                {
                    if (n is not JsonObject o) continue;
                    string name = (string)o["name"] ?? "(unnamed)";
                    string upk = (string)o["upk"] ?? (string)o["package"];
                    if (string.IsNullOrWhiteSpace(upk)) continue;

                    string path = Path.Combine(cooked, upk.EndsWith(".upk", StringComparison.OrdinalIgnoreCase)
                                                       ? upk : upk + ".upk");
                    done++;
                    if (!File.Exists(path)) { log?.Invoke($"  [{done}/{total}] {name}: UPK missing, skipped"); continue; }

                    log?.Invoke($"  [{done}/{total}] {name}");
                    string own = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                    List<string> deps = await DetectRequiredPackagesAsync(path, cooked, null)
                                              .ConfigureAwait(false);

                    var existing = new List<string>();
                    if (o["chain"] is JsonArray had)
                        foreach (var c in had)
                        {
                            string s = ((string)c ?? "").Trim();
                            if (s.Length > 0) existing.Add(s);
                        }
                    if (existing.Count == 0) existing.Add(own);

                    var missing = deps.Where(d => !existing.Any(
                                        x => string.Equals(x, d, StringComparison.OrdinalIgnoreCase)))
                                      .ToList();
                    if (missing.Count == 0) continue;

                    var wanted = new JsonArray();
                    foreach (string d in missing)  wanted.Add(d);
                    foreach (string s in existing) wanted.Add(s);

                    o["chain"] = wanted;
                    changed++;
                    log?.Invoke($"  {name}: chain += [{string.Join(", ", missing)}] "
                              + $"-> [{string.Join(", ", missing.Concat(existing))}]");
                }
            }

            if (changed > 0)
            {
                Backup.Timestamped(CostumeConfig.ExistingPath(bin));
                CostumeConfig.WriteAllText(bin, root.ToJsonString(CostumeLibrary.JsonOpts));
                log?.Invoke($"rewrote {changed} chain(s) - restart the client");
            }
            else log?.Invoke("every chain is already correct");
            return changed;
        }

        public static string IconArtCacheDir =>
            Path.Combine(Path.GetDirectoryName(InstallLedger.DefaultPath) ?? AppContext.BaseDirectory,
                         "IconArt");

        public sealed class PackResult
        {
            public bool Ok { get; set; }
            public string FailedStep { get; set; }
            public List<string> Steps { get; } = new List<string>();
            public List<string> Warnings { get; } = new List<string>();

            public uint Enum { get; set; }

            public bool EnumChanged { get; set; }
        }

        public static PackResult ExportPack(string gameRoot, uint enumId, string outPath,
                                            Action<string> log = null)
        {
            var res = new PackResult();
            var (cooked, manifestPath, bin) = ResolvePaths(gameRoot);
            string jsonPath = Path.Combine(bin, "CustomCostumes.json");

            JsonObject entry = CostumeLibrary.FindByEnum(jsonPath, enumId);
            if (entry == null)
            {
                res.FailedStep = "lookup";
                log?.Invoke($"No installed costume with enum {enumId} in CustomCostumes.json.");
                return res;
            }

            InstallRecord rec = InstallLedger.Read().FirstOrDefault(r => r.Enum == enumId);

            string name = rec?.Name;
            if (string.IsNullOrWhiteSpace(name))
                res.Warnings.Add("no ledger record for this costume, so its internal name is unknown - "
                               + "the pack will be identified by customId alone (still correct, but the "
                               + "name check on import is skipped). Reinstalling it restores the record.");

            string upkPath = rec?.UpkPath;
            if (string.IsNullOrWhiteSpace(upkPath) || !File.Exists(upkPath))
            {
                string pkg = (string)entry["package"];
                if (!string.IsNullOrWhiteSpace(pkg))
                    upkPath = Path.Combine(cooked, pkg.EndsWith(".upk", StringComparison.OrdinalIgnoreCase) ? pkg : pkg + ".upk");
            }
            if (string.IsNullOrWhiteSpace(upkPath) || !File.Exists(upkPath))
            {
                res.FailedStep = "upk";
                log?.Invoke($"Costume UPK not found on disk ({upkPath ?? "unknown path"}). Nothing to export.");
                return res;
            }

            List<AliasPair> pairs = rec?.TfcAliasPairs != null && rec.TfcAliasPairs.Count > 0
                ? rec.TfcAliasPairs
                : RecoverAliasPairs(manifestPath, rec?.TfcAliasRows, rec?.TfcPackage, log);

            if (pairs.Count == 0)
                res.Warnings.Add("no TFC alias pairs recorded or recovered - if this costume's textures "
                               + "live in CharTextures.tfc it will import without them and render untextured");

            var info = new CostumePackInfo
            {
                Format      = CostumePackFile.Format,
                Name        = name,

                DisplayName = (string)entry["displayName"] ?? (string)entry["name"]
                              ?? rec?.DisplayName ?? name,
                CustomId    = CostumeLibrary.ParseHex(entry["customId"] ?? entry["prototypeId"]),

                DonorClass  = (string)entry["donorClass"] ?? (string)entry["class"],
                Enum        = enumId,
                UpkFileName = Path.GetFileName(upkPath),
                CreatedUtc  = DateTime.UtcNow.ToString("o"),
                TfcAliasPairs = pairs,
                Entry       = entry.DeepClone() as JsonObject,

                FxPackToken = (string)entry["fxPack"],
            };

            if (info.Entry?["effects"] is JsonArray fxArr && fxArr.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(info.FxPackToken))
                    res.Warnings.Add($"this costume has {fxArr.Count} custom effect(s) but is not "
                                   + "assigned to an FX pack, so there is nothing to export them "
                                   + "with. Players will get the costume with STOCK effects. "
                                   + "Assign it to a pack on the Effects tab first.");
                else
                    res.Warnings.Add($"this costume's effects live in FX pack \"{info.FxPackToken}\" "
                                   + "- export that separately and ship both files, or players "
                                   + "get the costume with stock effects.");
            }

            EnsureDonorAsset(info.Entry, info.DonorClass, res, log);

            string builtIcon = rec?.IconUpkPath;
            if (string.IsNullOrWhiteSpace(builtIcon) || !File.Exists(builtIcon))
                builtIcon = Path.Combine(cooked, IconPackBuilder.UpkFileNameForEnum(enumId));

            bool hasIcons = entry["iconPackage"] != null;
            if (hasIcons && !File.Exists(builtIcon))
            {
                res.Warnings.Add($"this costume declares custom icons but {Path.GetFileName(builtIcon)} "
                               + "is missing from CookedPCConsole - players importing this pack will get "
                               + "the donor's icons");
                builtIcon = null;
            }
            else if (!hasIcons) builtIcon = null;

            var art = new Dictionary<IconRole, string>();
            foreach (IconRoleInfo r in IconPackBuilder.Roles)
            {
                string src = FindCachedArt(enumId, r.Role);
                if (src != null) art[r.Role] = src;
            }

            if (art.Count == 0 && entry["protoIcons"] != null)
                res.Warnings.Add("this costume has custom icons installed, but their source images are no "
                               + "longer in the art cache - the pack will import with donor icons");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
                if (File.Exists(outPath)) File.Delete(outPath);

                using var zip = ZipFile.Open(outPath, ZipArchiveMode.Create);

                zip.CreateEntryFromFile(upkPath, info.UpkFileName, CompressionLevel.Optimal);
                res.Steps.Add($"costume UPK: {info.UpkFileName} ({new FileInfo(upkPath).Length:N0} bytes)");

                if (builtIcon != null)
                {
                    zip.CreateEntryFromFile(builtIcon, CostumePackFile.IconUpkEntry, CompressionLevel.Optimal);
                    info.IconUpk = CostumePackFile.IconUpkEntry;
                    res.Steps.Add($"icon package: {Path.GetFileName(builtIcon)} "
                                + $"({new FileInfo(builtIcon).Length:N0} bytes)");
                }

                foreach (var kv in art)
                {
                    string inZip = "icons/" + kv.Key + Path.GetExtension(kv.Value);
                    zip.CreateEntryFromFile(kv.Value, inZip, CompressionLevel.Optimal);
                    info.IconArt[kv.Key] = inZip;
                    res.Steps.Add($"icon art: {kv.Key} <- {Path.GetFileName(kv.Value)}");
                }

                ZipArchiveEntry man = zip.CreateEntry(CostumePackFile.ManifestName, CompressionLevel.Optimal);
                using (var w = new StreamWriter(man.Open()))
                    w.Write(CostumePackFile.WriteJson(info));

                res.Steps.Add($"manifest: enum {info.Enum}, donor {info.DonorClass}, "
                            + $"{info.TfcAliasPairs.Count} alias pair(s)");
            }
            catch (Exception ex)
            {
                res.FailedStep = "zip";
                log?.Invoke("Export failed: " + ex.Message);
                return res;
            }

            foreach (string s in res.Steps) log?.Invoke("  " + s);
            foreach (string w in res.Warnings) log?.Invoke("  ⚠ " + w);
            res.Ok = true;
            res.Enum = enumId;
            log?.Invoke($"exported -> {outPath}");
            return res;
        }

        public static PackResult ImportPack(string gameRoot, string packPath, string serverDir,
                                            Action<string> log = null)
        {
            var res = new PackResult();

            CostumePackInfo info = CostumePackFile.Read(packPath, out string err);
            if (info == null)
            {
                res.FailedStep = "read";
                log?.Invoke("Cannot read pack: " + err);
                return res;
            }

            var (cooked, manifestPath, bin) = ResolvePaths(gameRoot);
            string jsonPath = Path.Combine(bin, "CustomCostumes.json");

            ulong expected = info.CustomId;
            if (!string.IsNullOrWhiteSpace(info.Name))
            {
                ulong fromName = HashName.CustomId(info.Name);
                if (info.CustomId == 0) expected = fromName;
                else if (info.CustomId != fromName)
                {
                    res.FailedStep = "identity";
                    log?.Invoke($"Pack is inconsistent: customId 0x{info.CustomId:X16} does not match "
                              + $"hash of name \"{info.Name}\" (0x{fromName:X16}). Refusing to import.");
                    return res;
                }
            }
            if (expected == 0)
            {
                res.FailedStep = "identity";
                log?.Invoke("Pack has no customId and no name to derive one from. Refusing to import.");
                return res;
            }

            if (CostumeLibrary.FindByCustomId(jsonPath, expected) != null)
            {
                res.FailedStep = "duplicate";
                log?.Invoke($"\"{info.DisplayName}\" is already installed (customId 0x{expected:X16}). "
                          + "Uninstall it first if you want to replace it with this pack.");
                return res;
            }

            uint chosen = info.Enum;
            if (EnumInUse(jsonPath, chosen))
            {
                uint next = EnumAllocator.NextFree(jsonPath, PendingPurgePathsFor(serverDir));
                res.EnumChanged = true;
                res.Warnings.Add($"enum {info.Enum} is already taken here, so this costume was installed "
                               + $"on {next}. That is fine if you run your own server (ServerCostumes.json "
                               + $"is rewritten to match). If you connect to someone else's server, it must "
                               + $"use {next} for this costume too or the costume will not resolve.");
                chosen = next;
                log?.Invoke($"  enum {info.Enum} taken -> allocated {next}");
            }
            else
            {
                log?.Invoke($"  enum {chosen} is free - keeping the pack's enum");
            }
            res.Enum = chosen;

            string upkOut = Path.Combine(cooked, info.UpkFileName);
            if (File.Exists(upkOut))
            {
                res.FailedStep = "upk";
                log?.Invoke($"{info.UpkFileName} already exists in CookedPCConsole. Remove it first - "
                          + "overwriting a package that another costume may be using is never safe.");
                return res;
            }

            string tempArt = null;
            try
            {
                using var zip = ZipFile.OpenRead(packPath);

                zip.GetEntry(info.UpkFileName).ExtractToFile(upkOut, false);
                res.Steps.Add($"wrote {info.UpkFileName}");
                log?.Invoke($"[1/5] wrote {info.UpkFileName}");

                if (info.TfcAliasPairs.Count > 0 && File.Exists(manifestPath))
                {
                    log?.Invoke("[2/5] aliasing the TFC manifest");
                    Backup.Timestamped(manifestPath);
                    AliasResult ar = TfcEngine.Alias(manifestPath, manifestPath, info.TfcAliasPairs, log);
                    res.Steps.Add($"manifest: {ar.Added} row(s) added, {ar.Skipped} already present");

                    if (ar.Added == 0 && ar.Skipped == 0)
                        res.Warnings.Add("no manifest rows matched this pack's alias pairs - the costume's "
                                       + "textures may not resolve");
                }
                else
                {
                    log?.Invoke("[2/5] no TFC aliases in this pack");
                }

                var sources = new List<IconSource>();
                if (info.IconArt.Count > 0)
                {
                    tempArt = Path.Combine(Path.GetTempPath(), "mhpack_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempArt);
                    foreach (var kv in info.IconArt)
                    {
                        ZipArchiveEntry e = zip.GetEntry(kv.Value);
                        if (e == null) continue;
                        string outFile = Path.Combine(tempArt, Path.GetFileName(kv.Value));
                        e.ExtractToFile(outFile, true);
                        sources.Add(new IconSource { Role = kv.Key, ImagePath = outFile });
                    }
                }

                var patches = new List<IconPatch>();
                string iconPackage = null;
                string iconUpkPath = null;

                if (sources.Count > 0)
                {
                    log?.Invoke($"[3/5] building the icon package for enum {chosen}");
                    string donorIcon = Path.Combine(cooked, IconPackBuilder.DefaultDonorUpk);
                    iconUpkPath = Path.Combine(cooked, IconPackBuilder.UpkFileNameForEnum(chosen));

                    IconPackResult icons = IconPackBuilder.Build(chosen, donorIcon, iconUpkPath, sources, log);
                    foreach (string s in icons.Steps) res.Steps.Add("  icons: " + s);

                    if (!icons.Ok)
                    {

                        res.Warnings.Add("icon package build failed (" + icons.FailedStep
                                       + ") - importing with the donor's icons instead");
                        iconUpkPath = null;
                    }
                    else
                    {
                        patches = icons.Patches;
                        iconPackage = icons.PackageFName;

                        if (icons.RemainingTfcTextures.Count > 0 && File.Exists(manifestPath))
                        {
                            TfcEngine.Alias(manifestPath, manifestPath, new[]
                            {
                                new AliasPair(IconPackBuilder.DonorPackageName,
                                              IconPackBuilder.PackageNameForEnum(chosen)),
                            }, log);
                            res.Steps.Add("manifest: icon alias added");
                        }

                        CacheImportedArt(chosen, sources);
                    }
                }
                else
                {
                    log?.Invoke("[3/5] no icon art in this pack - the donor's icons will be used");
                }

                log?.Invoke($"[4/5] writing {CostumeConfig.InUseName(jsonPath)}");
                if (CostumeConfig.Exists(jsonPath)) Backup.Timestamped(CostumeConfig.ExistingPath(jsonPath));

                if (!DonorKnownLocally(info.DonorClass))
                    res.Warnings.Add($"donor \"{info.DonorClass}\" is not in this machine's Costumes.json. "
                                   + "The pack carries its ids so the costume should still work, but a "
                                   + "different Costumes.json between the two machines is worth checking.");

                JsonObject fresh = info.Entry.DeepClone() as JsonObject;
                fresh["enum"] = chosen;
                fresh["customId"] = $"0x{expected:X16}";

                fresh.Remove("iconPackage");
                fresh.Remove("protoIcons");
                if (iconPackage != null)
                {
                    fresh["iconPackage"] = iconPackage;
                    var iconArr = new JsonArray();
                    foreach (IconPatch p in patches)
                        iconArr.Add(new JsonObject { ["off"] = p.OffsetHex, ["asset"] = p.AssetHex, ["path"] = p.Path });
                    fresh["protoIcons"] = iconArr;
                }

                CostumeLibrary.UpsertEntry(jsonPath, fresh, expected, chosen);
                res.Steps.Add("json: costume entry written");

                string serverJsonPath = !string.IsNullOrWhiteSpace(serverDir)
                    ? Path.Combine(serverDir, "ServerCostumes.json")
                    : Path.Combine(AppContext.BaseDirectory, "ServerCostumes.json");
                RegenerateServerFromClient(jsonPath, serverJsonPath, log);
                res.Steps.Add("regenerated ServerCostumes.json");

                log?.Invoke("[5/5] recording in the ledger");
                try
                {
                    InstallLedger.Upsert(new InstallRecord
                    {

                        Name = !string.IsNullOrWhiteSpace(info.Name) ? info.Name : info.DisplayName,
                        DisplayName = info.DisplayName,
                        Enum = chosen,
                        InstalledUtc = DateTime.UtcNow.ToString("o"),
                        UpkPath = upkOut,
                        CustomCostumesJson = jsonPath,
                        ManifestPath = manifestPath,
                        TfcPackage = info.TfcAliasPairs.FirstOrDefault()?.To,
                        TfcAliasPairs = info.TfcAliasPairs,
                        IconUpkPath = iconUpkPath,
                        IconPackage = iconPackage,
                    });
                    res.Steps.Add("ledger: recorded");
                }
                catch (Exception ex) { log?.Invoke("  (ledger write skipped: " + ex.Message + ")"); }
            }
            catch (Exception ex)
            {
                res.FailedStep = res.FailedStep ?? "import";
                log?.Invoke("Import failed: " + ex.Message);
                return res;
            }
            finally
            {
                if (tempArt != null) TryDeleteDir(tempArt);
            }

            foreach (string w in res.Warnings) log?.Invoke("  ⚠ " + w);
            res.Ok = true;
            log?.Invoke("");
            log?.Invoke($"DONE. \"{info.DisplayName}\" imported on enum {chosen}. "
                      + "Restart the client, and restart the server so it picks up ServerCostumes.json.");
            return res;
        }

        static bool EnumInUse(string jsonPath, uint enumId) => CostumeLibrary.FindByEnum(jsonPath, enumId) != null;

        static string[] PendingPurgePathsFor(string serverDir)
        {
            var paths = new List<string> { Path.Combine(AppContext.BaseDirectory, PendingPurgeFileName) };
            if (!string.IsNullOrWhiteSpace(serverDir))
                paths.Add(Path.Combine(serverDir, PendingPurgeFileName));
            return paths.ToArray();
        }

        static void EnsureDonorAsset(JsonObject entry, string donorClass, PackResult res,
                                     Action<string> log)
        {
            if (entry == null || entry["donorAsset"] != null) return;

            if (string.IsNullOrWhiteSpace(donorClass))
            {
                res.Warnings.Add("this costume has neither donorAsset nor donorClass - it cannot "
                               + "resolve on the importing machine");
                return;
            }

            try
            {
                DonorTables tables = DonorTables.Load(
                    Path.Combine(AppContext.BaseDirectory, "Costumes.json"));

                string match = tables.AllDonorClasses.FirstOrDefault(
                    c => string.Equals(c, donorClass, StringComparison.OrdinalIgnoreCase));

                if (match != null && tables.AssetIds.TryGetValue(match, out ulong asset) && asset != 0)
                {
                    entry["donorAsset"] = $"0x{asset:X16}";
                    log?.Invoke($"  filled in donorAsset for {donorClass} (pack no longer needs Costumes.json)");
                    return;
                }

                res.Warnings.Add($"could not resolve donorAsset for \"{donorClass}\" from this machine's "
                               + "Costumes.json - the recipient will need their own copy of that file");
            }
            catch (Exception ex)
            {
                res.Warnings.Add("could not resolve donorAsset: " + ex.Message);
            }
        }

        static bool DonorKnownLocally(string donorClass)
        {
            if (string.IsNullOrWhiteSpace(donorClass)) return false;
            try
            {
                DonorTables tables = DonorTables.Load(
                    Path.Combine(AppContext.BaseDirectory, "Costumes.json"));
                return tables.AllDonorClasses.Any(
                    c => string.Equals(c, donorClass, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        static List<AliasPair> RecoverAliasPairs(string manifestPath, List<string> customRows,
                                                 string customPkg, Action<string> log)
        {
            var pairs = new List<AliasPair>();
            if (!File.Exists(manifestPath)) return pairs;

            try
            {
                TfcManifest man = TfcManifest.Load(manifestPath);

                IEnumerable<Entry> rows = customRows != null && customRows.Count > 0
                    ? man.Entries.Where(e => customRows.Contains(e.FullName))
                    : man.Entries.Where(e => customPkg != null
                        && string.Equals(e.PackageName, customPkg, StringComparison.OrdinalIgnoreCase));

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Entry row in rows)
                {
                    uint off = row.Mips.Count > 0 ? row.Mips[0].Offset : 0;
                    Entry donor = man.Entries.FirstOrDefault(e =>
                        !ReferenceEquals(e, row) &&
                        string.Equals(e.TextureName, row.TextureName, StringComparison.OrdinalIgnoreCase) &&
                        e.Mips.Count > 0 && e.Mips[0].Offset == off &&
                        !string.Equals(e.PackageName, row.PackageName, StringComparison.OrdinalIgnoreCase));

                    if (donor == null) continue;

                    if (string.Equals(donor.PackageName, IconPackBuilder.DonorPackageName,
                                      StringComparison.OrdinalIgnoreCase))
                        continue;

                    string key = donor.PackageName + "->" + row.PackageName;
                    if (seen.Add(key)) pairs.Add(new AliasPair(donor.PackageName, row.PackageName));
                }

                if (pairs.Count > 0)
                    log?.Invoke($"  recovered {pairs.Count} alias pair(s) from the manifest "
                              + "(installed before pairs were recorded)");
            }
            catch (Exception ex) { log?.Invoke("  alias recovery failed: " + ex.Message); }

            return pairs;
        }

        static string FindCachedArt(uint enumId, IconRole role)
        {
            try
            {
                if (!Directory.Exists(IconArtCacheDir)) return null;
                return Directory.GetFiles(IconArtCacheDir, $"{enumId}_{role}.*").FirstOrDefault();
            }
            catch { return null; }
        }

        static void CacheImportedArt(uint enumId, IEnumerable<IconSource> sources)
        {
            try
            {
                Directory.CreateDirectory(IconArtCacheDir);
                foreach (IconSource s in sources)
                {
                    if (string.IsNullOrWhiteSpace(s.ImagePath) || !File.Exists(s.ImagePath)) continue;
                    foreach (string stale in Directory.GetFiles(IconArtCacheDir, $"{enumId}_{s.Role}.*"))
                        File.Delete(stale);
                    File.Copy(s.ImagePath,
                        Path.Combine(IconArtCacheDir, $"{enumId}_{s.Role}{Path.GetExtension(s.ImagePath)}"), true);
                }
            }
            catch {  }
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        static void TryDeleteDir(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }
    }
}
