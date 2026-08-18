using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Models.UpkFile.Engine.Texture;
using UpkManager.Models.UpkFile.Objects;
using UpkRename.Core;

namespace CostumeManager.Core
{

    public static class FxNaming
    {
        public const string OuterPackage = "marvelgamecontent";

        public static string StockStemFromFile(string path)
        {
            string leaf = Path.GetFileNameWithoutExtension(path ?? "");
            if (leaf.StartsWith("UC__", StringComparison.OrdinalIgnoreCase)) leaf = leaf.Substring(4);
            if (leaf.EndsWith("_SF", StringComparison.OrdinalIgnoreCase)) leaf = leaf.Substring(0, leaf.Length - 3);
            return leaf;
        }

        public static string CustomStem(string stockStem, string customName)
        {
            return stockStem + "_" + customName;
        }

        public static string OutputUpkName(string stockStem, string customName)
        {
            return "UC__" + CustomStem(stockStem, customName) + "_SF.upk";
        }

        public static string PackageFName(string stockStem, string customName)
        {
            return ("uc__" + CustomStem(stockStem, customName) + "_sf").ToLowerInvariant();
        }

        public static string ClassPath(string classLeaf)
        {
            return OuterPackage + "." + (classLeaf ?? "").ToLowerInvariant();
        }

        public const int MaxTokenLength = 16;

        public static string SanitiseToken(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var sb = new System.Text.StringBuilder();
            foreach (char c in s)
            {
                if (!char.IsLetterOrDigit(c)) continue;
                sb.Append(c);
                if (sb.Length >= MaxTokenLength) break;
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        public static string TokenProblem(string token, IEnumerable<string> taken)
        {
            if (string.IsNullOrWhiteSpace(token))
                return "the token is empty - it names the files on disk, so it cannot be blank";

            foreach (char c in token)
                if (!char.IsLetterOrDigit(c))
                    return "the token may only contain letters and digits (found '" + c + "')";

            if (token.Length > MaxTokenLength)
                return "the token is longer than " + MaxTokenLength + " characters";

            if (taken != null)
                foreach (string t in taken)
                    if (string.Equals(t, token, StringComparison.OrdinalIgnoreCase))
                        return "\"" + token + "\" is already in use - tokens must be unique "
                             + "because they are what keeps effect class names from colliding";

            return null;
        }

        public static string SuggestToken(string hero, IEnumerable<string> taken)
        {
            string basis = SanitiseToken(hero) ?? "Pack";

            if (basis.Length > MaxTokenLength - 2) basis = basis.Substring(0, MaxTokenLength - 2);

            var used = new HashSet<string>(taken ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            for (int n = 1; n < 1000; n++)
            {
                string candidate = basis + n;
                if (!used.Contains(candidate)) return candidate;
            }
            return basis;
        }

        public static readonly string[] FilePrefixes =
        {
            "UC__Power", "UC__MarvelConditionEffect_", "UC__MarvelEntity_", "UC__MarvelProjectile_"
        };

        public static bool LooksLikeFxFile(string path)
        {
            string leaf = Path.GetFileName(path ?? "");
            if (!leaf.EndsWith(".upk", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (string p in FilePrefixes)
                if (leaf.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    public static class UpkClassExports
    {

        public static List<string> Find(UnrealHeader header, string prefix)
        {
            var outp = new List<string>();
            if (header == null || header.ExportTable == null) return outp;

            var names = new HashSet<string>(
                header.ExportTable.Select(e => e.ObjectNameIndex != null ? e.ObjectNameIndex.Name : ""),
                StringComparer.OrdinalIgnoreCase);

            return header.ExportTable
                .Select(e => e.ObjectNameIndex != null ? e.ObjectNameIndex.Name : null)
                .Where(n => !string.IsNullOrEmpty(n)
                         && !n.StartsWith("default__", StringComparison.OrdinalIgnoreCase)
                         && (prefix == null || n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                         && names.Contains("default__" + n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n.Length)
                .ToList();
        }

        public static string FindEffect(UnrealHeader header, string stockStem, Action<string> log = null)
        {
            List<string> all = Find(header, null);
            if (all.Count == 0) return null;

            if (!string.IsNullOrEmpty(stockStem))
            {
                string exact = all.FirstOrDefault(
                    n => string.Equals(n, stockStem, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;

                string starts = all.FirstOrDefault(
                    n => n.StartsWith(stockStem, StringComparison.OrdinalIgnoreCase));
                if (starts != null)
                {
                    if (log != null)
                        log("      note: no export named \"" + stockStem + "\"; using \"" + starts + "\"");
                    return starts;
                }
            }

            string[] fx = { "power", "marvelconditioneffect", "marvelentity_", "marvelprojectile" };
            string guess = all.FirstOrDefault(
                n => fx.Any(p => n.StartsWith(p, StringComparison.OrdinalIgnoreCase)));
            if (guess != null && log != null)
                log("      ⚠ guessed class export \"" + guess + "\" for stem \"" + stockStem +
                    "\" - verify this renders before trusting it");
            return guess;
        }

        public static bool IsSharedBaseClass(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string[] bases = { "marvelpower", "marvelprojectile", "marvelconditioneffect",
                               "marvelentity", "marvelagent", "object", "package" };
            foreach (string b in bases)
                if (string.Equals(name, b, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    public enum FxCompat { Match, Unknown, Mismatch }

    public static class FxCompatibility
    {

        public static string HeroOfCostume(string donorClassOrCostumeClass)
        {
            string s = (donorClassOrCostumeClass ?? "").Trim();
            if (s.Length == 0) return null;
            int dot = s.LastIndexOf('.');
            if (dot >= 0) s = s.Substring(dot + 1);
            const string p = "marvelplayer_";
            if (s.StartsWith(p, StringComparison.OrdinalIgnoreCase)) s = s.Substring(p.Length);
            int us = s.IndexOf('_');
            if (us > 0) s = s.Substring(0, us);
            return s.Length == 0 ? null : s.ToLowerInvariant();
        }

        public static string HeroOfProto(string proto)
        {
            return EffectTables.HeroFromProto(proto);
        }

        static bool NameCarriesHero(string effectName, string hero, HashSet<string> knownHeroes)
        {
            if (string.IsNullOrEmpty(effectName) || string.IsNullOrEmpty(hero)) return false;

            string low = effectName.ToLowerInvariant();
            int at = 0;
            while ((at = low.IndexOf(hero, at, StringComparison.Ordinal)) >= 0)
            {
                int end = at + hero.Length;
                char before = at == 0 ? '_' : effectName[at - 1];
                char after = end >= effectName.Length ? '_' : effectName[end];

                bool leftOk = !char.IsLetterOrDigit(before) || char.IsLower(before) || char.IsDigit(before);
                bool rightOk = !char.IsLetterOrDigit(after) || char.IsUpper(after) || char.IsDigit(after);

                if (leftOk && rightOk && !LongerHeroCovers(low, at, end, hero, knownHeroes))
                    return true;
                at = end;
            }
            return false;
        }

        static bool LongerHeroCovers(string lowerName, int at, int end, string hero,
                                     HashSet<string> knownHeroes)
        {
            if (knownHeroes == null) return false;
            foreach (string h2 in knownHeroes)
            {
                if (h2.Length <= hero.Length) continue;
                if (h2.IndexOf(hero, StringComparison.Ordinal) < 0) continue;
                int idx = 0;
                while ((idx = lowerName.IndexOf(h2, idx, StringComparison.Ordinal)) >= 0)
                {
                    if (idx <= at && idx + h2.Length >= end) return true;
                    idx += h2.Length;
                }
            }
            return false;
        }

        public static FxCompat Check(EffectRecord rec, string costumeHero, out string reason)
        {
            return Check(rec, costumeHero, null, out reason);
        }

        public static FxCompat Check(EffectRecord rec, string costumeHero,
                                     HashSet<string> knownHeroes, out string reason)
        {
            reason = null;
            if (rec == null || string.IsNullOrEmpty(costumeHero)) return FxCompat.Unknown;

            bool byName = NameCarriesHero(rec.Name, costumeHero, knownHeroes);
            string protoHero = HeroOfProto(rec.Proto);

            if (byName)
            {
                reason = "name carries \"" + costumeHero + "\"";
                return FxCompat.Match;
            }
            if (protoHero != null && protoHero == costumeHero)
            {
                reason = "prototype lives under Powers/Player/" + costumeHero;
                return FxCompat.Match;
            }
            if (protoHero != null)
            {
                reason = "belongs to \"" + protoHero + "\", not \"" + costumeHero + "\"";
                return FxCompat.Mismatch;
            }
            reason = "cannot tell which hero this belongs to";
            return FxCompat.Unknown;
        }
    }

    public sealed class FxBulkInfo
    {
        public int InlineTextures { get; set; }
        public int TfcTextures { get; set; }

        public bool Blocks { get { return false; } }

        public string Reason { get { return null; } }

        public string Note
        {
            get
            {
                var bits = new List<string>();
                if (InlineTextures > 0) bits.Add(InlineTextures + " inline (offsets relocated on write)");
                if (TfcTextures > 0) bits.Add(TfcTextures + " TFC-backed (key unchanged by our renames)");
                return bits.Count > 0 ? string.Join(", ", bits) : null;
            }
        }
    }

    public static class FxBulk
    {
        const uint STORE_IN_SEPARATE_FILE = 0x00000001;

        public const string Explanation =
            "renaming the package changes the manifest key its streamed textures resolve "
            + "under, so they would stop loading. Needs FX TFC aliasing. (Inline bulk is "
            + "already handled - the writer relocates those offsets.)";

        public static async Task<FxBulkInfo> InspectAsync(UnrealHeader h)
        {
            var info = new FxBulkInfo();
            if (h == null || h.ExportTable == null) return info;

            foreach (UnrealExportTableEntry e in h.ExportTable)
            {
                string cls = e.ClassReferenceNameIndex?.Name ?? "";
                if (!cls.Equals("Texture2D", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (e.UnrealObject == null) await e.ParseUnrealObject(false, false);
                    if (e.UnrealObject is not IUnrealObject uo || uo.UObject is not UTexture2D tex) continue;
                    if (tex.Mips == null) continue;

                    bool anyTfc = false, anyInline = false;
                    foreach (var m in tex.Mips)
                    {
                        if ((m.BulkDataFlags & STORE_IN_SEPARATE_FILE) != 0) anyTfc = true;
                        else anyInline = true;
                    }
                    if (anyTfc) info.TfcTextures++;

                    if (anyInline) info.InlineTextures++;
                }
                catch
                {

                    info.InlineTextures++;
                }
            }
            return info;
        }
    }

    public sealed class FxCandidate
    {
        public FxBulkInfo Bulk { get; set; }

        public string SourcePath { get; set; }
        public string FileName { get { return Path.GetFileName(SourcePath ?? ""); } }
        public string StockStem { get; set; }
        public EffectRecord Record { get; set; }

        public bool Known { get { return Record != null; } }
        public ulong FromAsset { get { return Record != null ? Record.AssetId : 0UL; } }
        public string Kind { get { return Record != null ? Record.Kind : null; } }

        public string StockPath { get; set; }
        public bool StockExists { get; set; }
        public bool IdenticalToStock { get; set; }
        public FxDiffReport Diff { get; set; }

        public bool Parsed { get; set; }
        public bool Truncated { get; set; }
        public string ClassLeaf { get; set; }
        public List<string> AllClassExports { get; set; } = new List<string>();

        public string SkipReason { get; set; }
        public bool Installable { get { return SkipReason == null; } }

        public List<string> CoveredStockFiles { get; set; }

        public bool SiblingRenameOptIn { get; set; }

        public FxCompat Compat { get; set; } = FxCompat.Unknown;
        public string CompatReason { get; set; }
        public bool CompatWarn { get { return Compat == FxCompat.Mismatch; } }

        public bool Selected { get; set; }

        public string Status
        {
            get
            {
                if (SkipReason != null) return SkipReason;
                if (!StockExists) return "no stock counterpart (will install)";
                return "differs from stock";
            }
        }

        public override string ToString() { return FileName; }
    }

    public sealed class FxEditedObject
    {
        public string Path { get; set; }
        public string Class { get; set; }

        public string ResolvedSystem { get; set; }

        public string ParticleSystem
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ResolvedSystem)) return ResolvedSystem;

                if (string.IsNullOrEmpty(Path)) return null;
                string[] parts = Path.Split('.');
                if (parts.Length < 4) return null;
                if (!string.Equals(parts[parts.Length - 3], "particles",
                                   StringComparison.OrdinalIgnoreCase)) return null;
                string sys = parts[parts.Length - 2];
                return string.IsNullOrWhiteSpace(sys) ? null : sys;
            }
        }

        public bool DeeperCandidate
        {
            get
            {
                if (ParticleSystem != null || string.IsNullOrEmpty(Path)) return false;
                return Path.IndexOf(".particles.", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public override string ToString() { return Path + "|" + Class; }
    }

    public sealed class FxDiffReport
    {
        public bool StockMissing { get; set; }
        public List<string> NamesAdded { get; } = new List<string>();
        public List<string> NamesRemoved { get; } = new List<string>();
        public List<string> ExportsAdded { get; } = new List<string>();
        public List<string> ExportsRemoved { get; } = new List<string>();
        public int CommonExports { get; set; }
        public int SizeDifferences { get; set; }

        public bool? PayloadIdentical { get; set; }
        public int PayloadDifferences { get; set; }
        public int PayloadCompared { get; set; }

        public List<FxEditedObject> EditedObjects { get; } = new List<FxEditedObject>();

        public bool ContentSameAsStock
        {
            get { return StructurallyIdentical && PayloadIdentical.HasValue && PayloadIdentical.Value; }
        }

        public bool StructurallyIdentical
        {
            get
            {
                return NamesAdded.Count == 0 && NamesRemoved.Count == 0 &&
                       ExportsAdded.Count == 0 && ExportsRemoved.Count == 0 &&
                       SizeDifferences == 0;
            }
        }

        public bool HasRemovedExports { get { return ExportsRemoved.Count > 0; } }

        public string Summary
        {
            get
            {
                if (StockMissing) return "no stock counterpart to compare against";
                if (ContentSameAsStock)
                    return "CONTENT IDENTICAL to stock (" + PayloadCompared +
                           " export payloads match) - repackaged only";
                if (StructurallyIdentical && PayloadIdentical.HasValue && !PayloadIdentical.Value)
                    return "same structure as stock but " + PayloadDifferences + " of " +
                           PayloadCompared + " export payload(s) differ - real edit";
                if (StructurallyIdentical)
                    return "same names, exports and sizes as stock - differs only in packaging";
                var bits = new List<string>();
                if (ExportsAdded.Count > 0) bits.Add("+" + ExportsAdded.Count + " export(s)");
                if (ExportsRemoved.Count > 0) bits.Add("-" + ExportsRemoved.Count + " export(s)");
                if (SizeDifferences > 0) bits.Add(SizeDifferences + " export(s) resized");
                if (NamesAdded.Count > 0) bits.Add("+" + NamesAdded.Count + " name(s)");
                if (NamesRemoved.Count > 0) bits.Add("-" + NamesRemoved.Count + " name(s)");
                return bits.Count > 0 ? string.Join(", ", bits) : "content changed";
            }
        }
    }

    public static class FxDiff
    {
        public static async Task<FxDiffReport> CompareAsync(string modPath, string stockPath)
        {
            var rep = new FxDiffReport();
            if (string.IsNullOrWhiteSpace(stockPath) || !File.Exists(stockPath))
            {
                rep.StockMissing = true;
                return rep;
            }

            UnrealHeader modH, stockH;
            try
            {
                modH = await RenameEngine.LoadAsync(modPath);
                stockH = await RenameEngine.LoadAsync(stockPath);
            }
            catch
            {
                rep.StockMissing = true;
                return rep;
            }

            var modNames = NameSet(modH);
            var stockNames = NameSet(stockH);
            rep.NamesAdded.AddRange(modNames.Except(stockNames, StringComparer.OrdinalIgnoreCase).OrderBy(s => s));
            rep.NamesRemoved.AddRange(stockNames.Except(modNames, StringComparer.OrdinalIgnoreCase).OrderBy(s => s));

            var modExp = ExportMap(modH);
            var stockExp = ExportMap(stockH);
            rep.ExportsAdded.AddRange(modExp.Keys.Except(stockExp.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(s => s));
            rep.ExportsRemoved.AddRange(stockExp.Keys.Except(modExp.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(s => s));

            foreach (var kv in modExp)
            {
                int stockSize;
                if (!stockExp.TryGetValue(kv.Key, out stockSize)) continue;
                rep.CommonExports++;
                if (stockSize != kv.Value) rep.SizeDifferences++;
            }

            try
            {
                var stockPayloads = PayloadMap(stockH);

                var classByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (UnrealExportTableEntry pe in modH.ExportTable)
                {
                    string pcls = pe.ClassReferenceNameIndex != null
                                ? pe.ClassReferenceNameIndex.Name : null;
                    if (string.IsNullOrEmpty(pcls)) continue;
                    string pfull;
                    try { pfull = pe.GetPathName(); } catch { pfull = null; }
                    if (!string.IsNullOrEmpty(pfull)) classByPath[pfull] = pcls;
                }

                int differ = 0, compared = 0;
                foreach (UnrealExportTableEntry e in modH.ExportTable)
                {
                    string nm = e.ObjectNameIndex != null ? e.ObjectNameIndex.Name : null;
                    if (string.IsNullOrEmpty(nm)) continue;
                    string cls = e.ClassReferenceNameIndex != null ? e.ClassReferenceNameIndex.Name : "";
                    string full;
                    try { full = e.GetPathName() ?? nm; } catch { full = nm; }
                    string key = full + "|" + cls;

                    byte[] sb;
                    if (!stockPayloads.TryGetValue(key, out sb)) continue;
                    byte[] mb = null;
                    try { mb = e.UnrealObjectReader != null ? e.UnrealObjectReader.GetBytes() : null; }
                    catch { }
                    if (mb == null || sb == null) continue;

                    compared++;
                    if (!SameBytes(mb, sb))
                    {
                        differ++;
                        rep.EditedObjects.Add(new FxEditedObject
                        {
                            Path = full,
                            Class = cls,
                            ResolvedSystem = EnclosingParticleSystem(full, classByPath)
                        });
                    }
                }
                rep.PayloadCompared = compared;
                rep.PayloadDifferences = differ;
                if (compared > 0) rep.PayloadIdentical = (differ == 0);
            }
            catch
            {

            }

            return rep;
        }

        internal static Dictionary<string, byte[]> PayloadMap(UnrealHeader h)
        {
            var d = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            if (h == null || h.ExportTable == null) return d;
            foreach (UnrealExportTableEntry e in h.ExportTable)
            {
                string nm = e.ObjectNameIndex != null ? e.ObjectNameIndex.Name : null;
                if (string.IsNullOrEmpty(nm)) continue;
                string cls = e.ClassReferenceNameIndex != null ? e.ClassReferenceNameIndex.Name : "";
                string full;
                try { full = e.GetPathName() ?? nm; } catch { full = nm; }
                string key = full + "|" + cls;
                if (d.ContainsKey(key)) continue;
                byte[] bytes = null;
                try { bytes = e.UnrealObjectReader != null ? e.UnrealObjectReader.GetBytes() : null; }
                catch { }
                d[key] = bytes;
            }
            return d;
        }

        internal static string EnclosingParticleSystem(string path, Dictionary<string, string> classByPath)
        {
            if (string.IsNullOrEmpty(path) || classByPath == null) return null;
            string[] parts = path.Split('.');

            for (int i = parts.Length - 1; i > 0; i--)
            {
                string cls;
                if (!classByPath.TryGetValue(string.Join(".", parts, 0, i), out cls)) continue;
                if (string.Equals(cls, "particlesystem", StringComparison.OrdinalIgnoreCase))
                {
                    string sys = parts[i - 1];
                    return string.IsNullOrWhiteSpace(sys) ? null : sys;
                }
            }
            return null;
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static HashSet<string> NameSet(UnrealHeader h)
        {
            var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (h == null || h.NameTable == null) return s;
            foreach (var n in h.NameTable)
            {
                string v = n != null && n.Name != null ? n.Name.String : null;
                if (!string.IsNullOrEmpty(v)) s.Add(v);
            }
            return s;
        }

        static Dictionary<string, int> ExportMap(UnrealHeader h)
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (h == null || h.ExportTable == null) return d;
            foreach (UnrealExportTableEntry e in h.ExportTable)
            {
                string nm = e.ObjectNameIndex != null ? e.ObjectNameIndex.Name : null;
                if (string.IsNullOrEmpty(nm)) continue;
                string cls = e.ClassReferenceNameIndex != null ? e.ClassReferenceNameIndex.Name : "";
                string key = nm + "|" + cls;
                if (!d.ContainsKey(key)) d[key] = e.SerialDataSize;
            }
            return d;
        }
    }

    public static class FxScanner
    {

        public static List<string> FindFxFiles(string folder)
        {
            var found = new List<string>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return found;
            try
            {
                foreach (string f in Directory.EnumerateFiles(folder, "*.upk", SearchOption.TopDirectoryOnly))
                    if (FxNaming.LooksLikeFxFile(f)) found.Add(f);
            }
            catch { }
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        public static async Task<List<FxCandidate>> ScanAsync(
            string folder, EffectTables tables, string cookedDir, Action<string> log = null,
            string costumeHero = null)
        {
            List<string> files = FindFxFiles(folder);
            if (files.Count == 0)
            {
                if (log != null) log("  no FX-shaped .upk files in " + folder);
                return new List<FxCandidate>();
            }
            if (log != null) log("  scanning " + files.Count + " FX-shaped file(s) in " + folder);
            return await ScanFilesAsync(files, tables, cookedDir, log, costumeHero);
        }

        public static async Task<List<FxCandidate>> ScanFilesAsync(
            List<string> files, EffectTables tables, string cookedDir, Action<string> log = null,
            string costumeHero = null)
        {
            var outp = new List<FxCandidate>();
            if (files == null || files.Count == 0) return outp;

            foreach (string f in files)
            {
                var c = new FxCandidate
                {
                    SourcePath = f,
                    StockStem = FxNaming.StockStemFromFile(f)
                };

                EffectRecord rec;
                if (tables != null && tables.ByUpkFileName.TryGetValue(c.FileName, out rec))
                    c.Record = rec;

                if (!c.Known)
                {

                    bool isStockFile = !string.IsNullOrWhiteSpace(cookedDir) &&
                                       File.Exists(Path.Combine(cookedDir, c.FileName));
                    c.SkipReason = isStockFile
                        ? "stock per-costume variant - never resolved for a custom costume"
                        : "not a known stock effect package";

                    outp.Add(c);
                    if (log != null)
                    {
                        log("    - " + c.FileName + ": " + c.SkipReason);
                        if (isStockFile)
                            log("      (the game ships this file; it is the variant worn with another "
                                + "costume of this hero, and a custom costume resolves the default instead)");
                    }
                    continue;
                }

                if (costumeHero != null)
                {
                    string why;
                    c.Compat = FxCompatibility.Check(c.Record, costumeHero,
                                                     tables != null ? tables.KnownHeroes : null, out why);
                    c.CompatReason = why;
                }

                if (!string.IsNullOrWhiteSpace(cookedDir))
                {
                    c.StockPath = Path.Combine(cookedDir, c.Record.Upk);
                    c.StockExists = File.Exists(c.StockPath);
                }

                if (c.StockExists && FxCrc32.FilesAreIdentical(f, c.StockPath))
                {
                    c.IdenticalToStock = true;
                    c.SkipReason = "identical to stock - nothing to install";
                    outp.Add(c);
                    if (log != null) log("    = " + c.FileName + ": " + c.SkipReason);
                    continue;
                }

                try
                {
                    UnrealHeader h = await RenameEngine.LoadAsync(f);
                    c.Parsed = true;

                    string why;
                    if (!CheckNotTruncatedHeader(h, f, out why))
                    {
                        c.Truncated = true;
                        c.SkipReason = "truncated or corrupt: " + why;
                        outp.Add(c);
                        if (log != null) log("    ! " + c.FileName + ": " + c.SkipReason);
                        continue;
                    }

                    c.Bulk = await FxBulk.InspectAsync(h);
                    if (c.Bulk.Blocks)
                    {
                        c.SkipReason = c.Bulk.Reason;
                        outp.Add(c);
                        if (log != null)
                        {
                            log("    ⛔ " + c.FileName + ": " + c.SkipReason);
                            log("       " + FxBulk.Explanation);
                        }
                        continue;
                    }

                    c.AllClassExports = UpkClassExports.Find(h, null);
                    c.ClassLeaf = UpkClassExports.FindEffect(h, c.StockStem, log);
                    if (string.IsNullOrEmpty(c.ClassLeaf))
                    {
                        c.SkipReason = "no class export found (nothing for the game to resolve)";
                        outp.Add(c);
                        if (log != null) log("    ! " + c.FileName + ": " + c.SkipReason);
                        continue;
                    }

                    c.Diff = await FxDiff.CompareAsync(f, c.StockPath);

                    if (c.Diff != null && c.Diff.ContentSameAsStock)
                    {
                        c.IdenticalToStock = true;
                        c.SkipReason = "content identical to stock (repackaged only) - nothing to install";
                        outp.Add(c);
                        if (log != null) log("    = " + c.FileName + ": " + c.SkipReason);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    c.SkipReason = "could not read: " + ex.Message;
                    outp.Add(c);
                    if (log != null) log("    ! " + c.FileName + ": " + c.SkipReason);
                    continue;
                }

                c.Selected = c.Compat != FxCompat.Mismatch;

                outp.Add(c);
                if (log != null)
                {
                    string note = c.Diff != null ? "  [" + c.Diff.Summary + "]" : "";
                    log("    + " + c.FileName + " -> " + c.Record.Name +
                        " (" + c.Record.Kind + ", from " + c.Record.AssetIdHex + ")" + note);
                    if (c.Compat == FxCompat.Mismatch)
                        log("      ⚠ WRONG HERO? " + c.CompatReason +
                            " - it will install but the costume's hero never resolves it");
                    if (c.Diff != null && c.Diff.HasRemovedExports)
                        log("      ⚠ removes " + c.Diff.ExportsRemoved.Count +
                            " export(s) present in stock - most likely shape to orphan a link");
                    if (c.AllClassExports.Count > 1)
                        log("      note: exports " + c.AllClassExports.Count +
                            " classes; all will be uniquified: " + string.Join(", ", c.AllClassExports));
                }
            }

            int ok = outp.Count(x => x.Installable);
            if (log != null) log("  " + ok + " of " + outp.Count + " file(s) installable");
            return outp;
        }

        public static bool CheckNotTruncatedHeader(UnrealHeader h, string path, out string why)
        {
            why = null;
            if (h != null && h.CompressionFlags != 0) return true;
            try
            {
                long fileLen = new FileInfo(path).Length;
                int bad = 0;
                string firstBad = null;
                foreach (UnrealExportTableEntry exp in h.ExportTable)
                {
                    long end = (long)exp.SerialDataOffset + exp.SerialDataSize;
                    if (exp.SerialDataSize > 0 && end > fileLen)
                    {
                        bad++;
                        if (firstBad == null)
                            firstBad = exp.ObjectNameIndex != null ? exp.ObjectNameIndex.Name : "?";
                    }
                }
                if (bad > 0)
                {
                    why = bad + " export(s) point past end of file (first: " + firstBad + ")";
                    return false;
                }
            }
            catch (Exception ex)
            {
                why = ex.Message;
                return false;
            }
            return true;
        }
    }

    public static class FxRenamer
    {
        public sealed class FxRenamePlan
        {
            public List<RenamePair> Pairs { get; } = new List<RenamePair>();
            public string ClassLeaf { get; set; }
            public string NewClassLeaf { get; set; }

            public List<string> SiblingClasses { get; } = new List<string>();

            public List<string> ParticleSystems { get; } = new List<string>();

            public List<string> RefusedClasses { get; } = new List<string>();

            public List<string> AlreadyUnique { get; } = new List<string>();
            public List<string> Skipped { get; } = new List<string>();
            public bool Ok { get { return Pairs.Count > 0 && ClassLeaf != null; } }
        }

        public static FxRenamePlan Build(UnrealHeader header, string stockStem, string customName,
                                         IEnumerable<string> stubTextureNames = null,
                                         Action<string> log = null,
                                         Func<string, bool> isLinkTimeRef = null,
                                         IEnumerable<FxEditedObject> edited = null,
                                         ICollection<string> coveredStockFiles = null)
        {
            var plan = new FxRenamePlan();
            if (header == null || string.IsNullOrWhiteSpace(customName)) return plan;

            var stubs = new HashSet<string>(stubTextureNames ?? new List<string>(),
                                            StringComparer.OrdinalIgnoreCase);

            string primary = UpkClassExports.FindEffect(header, stockStem, log);
            if (string.IsNullOrEmpty(primary))
            {
                plan.Skipped.Add("no class export found");
                return plan;
            }
            plan.ClassLeaf = primary;
            plan.NewClassLeaf = primary + "_" + customName.ToLowerInvariant();

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (header.NameTable != null)
                foreach (var nt in header.NameTable)
                {
                    string v = nt != null && nt.Name != null ? nt.Name.String : null;
                    if (!string.IsNullOrEmpty(v)) names.Add(v);
                }

            Action<string, string> add = (from, to) =>
            {
                if (string.IsNullOrEmpty(from)) return;
                if (stubs.Contains(from))
                {

                    plan.Skipped.Add(from + " (TFC-backed texture)");
                    return;
                }
                if (isLinkTimeRef != null && isLinkTimeRef(from.ToLowerInvariant()))
                {
                    plan.Skipped.Add(from + " (link-time reference)");
                    return;
                }
                plan.Pairs.Add(new RenamePair(from, to));
            };

            add(primary, plan.NewClassLeaf);
            string cdo = "default__" + primary;
            if (names.Contains(cdo)) add(cdo, "default__" + plan.NewClassLeaf);
            else plan.Skipped.Add(cdo + " (no CDO in the name table)");

            string selfOld = ("uc__" + stockStem + "_sf").ToLowerInvariant();
            string selfNew = FxNaming.PackageFName(stockStem, customName);
            if (names.Contains(selfOld)) plan.Pairs.Add(new RenamePair(selfOld, selfNew));
            else plan.Skipped.Add(selfOld + " (package self-name not in the name table)");

            foreach (string c in UpkClassExports.Find(header, null))
            {
                if (string.Equals(c, primary, StringComparison.OrdinalIgnoreCase)) continue;
                string low = c.ToLowerInvariant();

                if (low.EndsWith("_" + customName.ToLowerInvariant(), StringComparison.Ordinal))
                {

                    plan.AlreadyUnique.Add(c);
                    continue;
                }
                int exporters;
                string whyShared;
                if (FxRefDb.IsSharedClassName(low, coveredStockFiles, out exporters, out whyShared))
                {

                    plan.RefusedClasses.Add(c + " (" + whyShared + ")");
                    continue;
                }
                if (isLinkTimeRef != null && isLinkTimeRef(low))
                {
                    plan.RefusedClasses.Add(c + " (link-time reference)");
                    continue;
                }

                string newLeaf = c + "_" + customName.ToLowerInvariant();
                add(c, newLeaf);
                if (names.Contains("default__" + c)) add("default__" + c, "default__" + newLeaf);
                plan.SiblingClasses.Add(c);

                if (exporters > 1 && log != null)
                    log("      sibling class \"" + c + "\" is shared by " + exporters +
                        " official package(s), but this install ships a custom copy of ALL of " +
                        "them - renaming is coherent (" +
                        string.Join(", ", FxRefDb.SharedExporterFiles(low)) + ")");
            }

            var systems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int deeper = 0;
            foreach (FxEditedObject ed in edited ?? Enumerable.Empty<FxEditedObject>())
            {
                if (ed == null) continue;
                if (ed.DeeperCandidate) { deeper++; continue; }
                string sys = ed.ParticleSystem;
                if (!string.IsNullOrEmpty(sys)) systems.Add(sys);
            }

            foreach (string sys in systems.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            {
                string low = sys.ToLowerInvariant();
                if (low.EndsWith("_" + customName.ToLowerInvariant(), StringComparison.Ordinal))
                {
                    plan.AlreadyUnique.Add(sys);
                    continue;
                }
                if (!names.Contains(sys))
                {

                    plan.Skipped.Add(sys + " (particle system not in the name table)");
                    continue;
                }
                add(sys, sys + "_" + customName.ToLowerInvariant());
                plan.ParticleSystems.Add(sys);
            }

            if (log != null)
            {
                log("      rename: " + primary + " -> " + plan.NewClassLeaf +
                    " (+ CDO, + self-name)");
                if (plan.SiblingClasses.Count > 0)
                    log("      sibling class(es) uniquified: " + string.Join(", ", plan.SiblingClasses));
                if (plan.ParticleSystems.Count > 0)
                    log("      edited particle system(s) uniquified: " +
                        string.Join(", ", plan.ParticleSystems));
                foreach (string s in plan.RefusedClasses) log("      refused: " + s);
                if (plan.AlreadyUnique.Count > 0)
                    log("      already unique (source may be an installed file): " +
                        string.Join(", ", plan.AlreadyUnique));
                if (deeper > 0)

                    log("      ⚠ " + deeper + " edit(s) sit deeper than <group>.particles."
                        + "<system>.<module> and were NOT uniquified - that art stays shared");
                foreach (string s in plan.Skipped) log("      skipped: " + s);
                log("      = " + plan.Pairs.Count + " name(s) to rename");
            }
            return plan;
        }

    }

    public sealed class FxEffect
    {
        public ulong From { get; set; }
        public string Package { get; set; }
        public string ClassPath { get; set; }
        public string EffectName { get; set; }
        public string UpkPath { get; set; }
        public string SourceCrc { get; set; }

        public override string ToString() { return EffectName ?? Package; }
    }

    public sealed class FxBuildResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; }
        public string OutputPath { get; set; }
        public string Package { get; set; }
        public string ClassPath { get; set; }
        public string ClassLeaf { get; set; }
        public ulong From { get; set; }
        public long Bytes { get; set; }
        public List<string> Steps { get; } = new List<string>();

        public HashSet<string> RenamedTo { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public UnrealHeader WrittenHeader { get; set; }
    }

    public static class FxPackBuilder
    {

        sealed class FxWrite
        {
            public bool Ok;
            public string Error;
            public long Bytes;
            public int Applied;
            public int Fixups;
        }

        static FxWrite WriteWithBulkFixups(string srcPath, string outPath,
                                           List<RenamePair> pairs, Action<string> log)
        {
            var w = new FxWrite();
            try
            {
                MHTexLib.UpkFile upk = MHTexLib.UpkFile.Load(srcPath);

                var notFound = new List<string>();
                foreach (RenamePair p in pairs)
                {
                    if (p == null || string.IsNullOrEmpty(p.From)) continue;
                    bool hit = false;
                    foreach (MHTexLib.NameEntry ne in upk.NameTable)
                    {
                        if (!string.Equals(ne.String, p.From, StringComparison.OrdinalIgnoreCase)) continue;
                        ne.String = p.To;
                        hit = true;
                        w.Applied++;
                        if (log != null) log("      \"" + p.From + "\" -> \"" + p.To + "\"");
                    }
                    if (!hit) notFound.Add(p.From);
                }

                if (w.Applied == 0)
                {
                    w.Error = "rename matched nothing" +
                              (notFound.Count > 0 ? " (" + string.Join(", ", notFound) + ")" : "");
                    return w;
                }
                if (notFound.Count > 0 && log != null)
                    log("      ⚠ not in the name table: " + string.Join(", ", notFound));

                w.Fixups = upk.DiscoverBulkFixups();

                byte[] bytes = upk.Rebuild();
                File.WriteAllBytes(outPath, bytes);
                w.Bytes = bytes.LongLength;
                w.Ok = true;
                return w;
            }
            catch (Exception ex)
            {
                w.Error = "write failed: " + ex.Message;
                return w;
            }
        }

        static bool VerifyBulkOffsets(string path, int expected, out string why)
        {
            why = null;
            if (expected <= 0) return true;
            try
            {
                MHTexLib.UpkFile upk = MHTexLib.UpkFile.Load(path);
                int found = 0;
                foreach (MHTexLib.ExportEntry e in upk.ExportTable)
                    found += upk.FindInlineBulkOffsets(e).Count;

                if (found >= expected) return true;

                why = expected + " expected, " + found + " still valid";
                return false;
            }
            catch (Exception ex)
            {
                why = "could not re-read: " + ex.Message;
                return false;
            }
        }

        public static async Task<List<string>> DetectFxParentPackagesAsync(
            string upkPath, string cookedDir, Action<string> log = null)
        {
            var needed = new List<string>();
            try
            {
                UnrealHeader header = await RenameEngine.LoadAsync(upkPath);
                if (header == null || header.ImportTable == null) return needed;

                string ownPkg = Path.GetFileNameWithoutExtension(upkPath).ToLowerInvariant();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (UnrealImportTableEntry imp in header.ImportTable)
                {
                    string cls = imp.ClassNameIndex != null ? imp.ClassNameIndex.Name : "";
                    string nm = imp.ObjectNameIndex != null ? imp.ObjectNameIndex.Name : "";
                    if (!string.Equals(cls, "class", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.IsNullOrEmpty(nm)) continue;

                    string pkg = ("uc__" + nm + "_sf").ToLowerInvariant();
                    if (pkg == ownPkg || !seen.Add(pkg)) continue;

                    string file = Path.Combine(cookedDir, "UC__" + nm + "_SF.upk");
                    if (!File.Exists(file))
                    {

                        continue;
                    }

                    needed.Add(pkg);
                    if (log != null)
                        log("      chain: needs \"" + pkg + "\" resident first (subclasses " + nm + ")");
                }
            }
            catch (Exception ex)
            {
                if (log != null) log("      (parent scan skipped: " + ex.Message + ")");
            }
            return needed;
        }

        public static async Task<FxBuildResult> BuildAsync(FxCandidate cand, string customName,
                                                           string cookedDir, Action<string> log = null,
                                                           Func<string, bool> isLinkTimeRef = null)
        {
            var res = new FxBuildResult();
            if (cand == null || !cand.Known)
            {
                res.Error = "candidate is not a known stock effect";
                return res;
            }

            string outName = FxNaming.OutputUpkName(cand.StockStem, customName);
            res.OutputPath = Path.Combine(cookedDir, outName);
            res.Package = FxNaming.PackageFName(cand.StockStem, customName);
            res.From = cand.FromAsset;

            try
            {

                List<string> stubs;
                try
                {
                    UnrealHeader probe = await RenameEngine.LoadAsync(cand.SourcePath);
                    stubs = await RenameEngine.DetectStubTexturesAsync(probe);
                }
                catch { stubs = new List<string>(); }

                UnrealHeader header = await RenameEngine.LoadAsync(cand.SourcePath, log);

                string why;
                if (!FxScanner.CheckNotTruncatedHeader(header, cand.SourcePath, out why))
                {
                    res.Error = "source is truncated or corrupt: " + why;
                    return res;
                }

                string srcSelf = Path.GetFileNameWithoutExtension(cand.SourcePath);
                if (srcSelf.EndsWith("_SF", StringComparison.OrdinalIgnoreCase))
                    srcSelf = srcSelf.Substring(0, srcSelf.Length - 3);
                if (srcSelf.EndsWith("_" + customName, StringComparison.OrdinalIgnoreCase))
                {
                    res.Error = "source looks like an ALREADY-INSTALLED file (" +
                                Path.GetFileName(cand.SourcePath) + " already carries the \"" +
                                customName + "\" token). Install from the pack's original " +
                                "files - installing an output would double the token and " +
                                "nothing would resolve it.";
                    return res;
                }

                List<FxEditedObject> edited = null;
                if (cand.Diff != null && cand.Diff.EditedObjects.Count > 0)
                    edited = cand.Diff.EditedObjects;
                else if (!string.IsNullOrWhiteSpace(cand.StockPath) && File.Exists(cand.StockPath))
                {
                    FxDiffReport d = await FxDiff.CompareAsync(cand.SourcePath, cand.StockPath);
                    edited = d.EditedObjects;
                    if (log != null && d.EditedObjects.Count == 0)
                        log("      no edited objects vs stock - nothing to uniquify");
                }
                else if (log != null)
                {
                    log("      no stock counterpart - nothing to collide with, uniquify skipped");
                }

                FxRenamer.FxRenamePlan plan =
                    FxRenamer.Build(header, cand.StockStem, customName, stubs, log, isLinkTimeRef,
                                    edited, cand.CoveredStockFiles);
                if (!plan.Ok)
                {
                    res.Error = "nothing to rename (" +
                                (plan.Skipped.Count > 0 ? string.Join("; ", plan.Skipped) : "no class") + ")";
                    return res;
                }

                res.ClassLeaf = plan.NewClassLeaf;
                res.ClassPath = FxNaming.ClassPath(plan.NewClassLeaf);

                FxWrite w = WriteWithBulkFixups(cand.SourcePath, res.OutputPath, plan.Pairs, log);
                if (!w.Ok)
                {
                    res.Error = w.Error;
                    try { if (File.Exists(res.OutputPath)) File.Delete(res.OutputPath); } catch { }
                    return res;
                }
                res.Bytes = w.Bytes;
                res.Steps.Add(outName + " (" + w.Bytes.ToString("N0") + " bytes, " +
                              w.Applied + " names, " + w.Fixups + " bulk offset(s) relocated)");

                var written = await RenameEngine.LoadAsync(res.OutputPath);
                var got = new HashSet<string>(
                    (written.ExportTable ?? new List<UnrealExportTableEntry>())
                        .Select(x => x.ObjectNameIndex != null ? x.ObjectNameIndex.Name : ""),
                    StringComparer.OrdinalIgnoreCase);

                bool hasClass = got.Contains(plan.NewClassLeaf);
                bool hasCdo = got.Contains("default__" + plan.NewClassLeaf);
                if (!hasClass || !hasCdo)
                {
                    res.Error = "written package does not export " +
                        (!hasClass ? "the class \"" + plan.NewClassLeaf + "\""
                                   : "the CDO \"default__" + plan.NewClassLeaf + "\"") +
                        " - the game would fall back to stock FX";
                    try { File.Delete(res.OutputPath); } catch { }
                    return res;
                }
                res.Steps.Add("class " + plan.NewClassLeaf + " + CDO verified in the written file");

                string bulkWhy;
                if (!VerifyBulkOffsets(res.OutputPath, w.Fixups, out bulkWhy))
                {
                    res.Error = "written package has stale bulk-data offsets (" + bulkWhy +
                                ") - it would load and then read garbage";
                    try { File.Delete(res.OutputPath); } catch { }
                    return res;
                }
                res.Steps.Add(w.Fixups + " bulk-data offset(s) verified on disk");

                foreach (RenamePair p in plan.Pairs) res.RenamedTo.Add(p.To);
                res.WrittenHeader = written;

                res.Ok = true;
                return res;
            }
            catch (Exception ex)
            {
                res.Error = ex.Message;
                try { if (File.Exists(res.OutputPath)) File.Delete(res.OutputPath); } catch { }
                return res;
            }
        }
    }
}
