using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using UpkRename.Core;
using TfcAlias.Core;

namespace CostumeManager.Core
{

    public static class HashPath
    {
        public static ulong Compute(string s)
        {
            byte[] b = Encoding.UTF8.GetBytes(s.ToLowerInvariant());
            uint adler = Adler32(b, 1);
            uint crc   = Crc32(b, 0);
            ulong combined = ((ulong)adler) | (((ulong)crc) << 32);
            return combined - 1UL;
        }

        public static ulong CustomId(string customName)
            => Compute("custom\\" + customName);

        public static ulong PrototypeId(string calligraphyPath)
        {
            string t = calligraphyPath.Replace('.', '?').Replace('\\', '.');
            return Compute(t);
        }

        static uint Adler32(byte[] data, uint seed)
        {
            const uint MOD = 65521;
            uint a = seed & 0xFFFF;
            uint b = (seed >> 16) & 0xFFFF;
            foreach (byte t in data)
            {
                a = (a + t) % MOD;
                b = (b + a) % MOD;
            }
            return (b << 16) | a;
        }

        static readonly uint[] CrcTable = BuildCrcTable();
        static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }
        static uint Crc32(byte[] data, uint seed)
        {
            uint c = seed ^ 0xFFFFFFFF;
            foreach (byte b in data)
                c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFF;
        }
    }

    public sealed class DonorTables
    {
        public Dictionary<string, ulong> AssetIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ulong> ProtoIds { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<string> AllDonorClasses => AssetIds.Keys.OrderBy(k => k);

        public static DonorTables Load(string costumesJson, string _unused = null)
        {
            var t = new DonorTables();
            if (!File.Exists(costumesJson)) return t;

            using var doc = JsonDocument.Parse(File.ReadAllText(costumesJson));
            if (!doc.RootElement.TryGetProperty("costumes", out var obj)) return t;

            foreach (var kv in obj.EnumerateObject())
            {
                string cls = kv.Name;
                var rec = kv.Value;

                if (rec.ValueKind == JsonValueKind.Object)
                {
                    if (rec.TryGetProperty("assetId", out var a) && TryHex(a.GetString(), out ulong av))
                        t.AssetIds[cls] = av;
                    if (rec.TryGetProperty("protoId", out var p) && TryHex(p.GetString(), out ulong pv))
                        t.ProtoIds[cls] = pv;
                }

                else if (rec.ValueKind == JsonValueKind.String && TryHex(rec.GetString(), out ulong av))
                {
                    t.AssetIds[cls] = av;
                }
            }
            return t;
        }

        static bool TryHex(string s, out ulong v)
        {
            v = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            return ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out v);
        }

        public bool TryResolve(string donorClass, out ulong assetId, out ulong protoId)
        {
            bool a = AssetIds.TryGetValue(donorClass, out assetId);
            bool p = TryResolveProto(donorClass, out protoId);
            return a && p;
        }

        public bool TryResolveAsset(string donorClass, out ulong assetId)
            => AssetIds.TryGetValue(donorClass, out assetId);

        public bool TryResolveProto(string donorClass, out ulong protoId)
        {
            if (ProtoIds.TryGetValue(donorClass, out protoId))
                return true;

            string n = Norm(donorClass);
            foreach (var kv in ProtoIds)
            {
                if (Norm(kv.Key) == n) { protoId = kv.Value; return true; }
            }
            protoId = 0;
            return false;
        }

        static string Norm(string s) => s.Replace("_", "").ToLowerInvariant();

        public IEnumerable<string> AllPrototypeClasses => ProtoIds.Keys.OrderBy(k => k);
    }

    public sealed class DonorGuess
    {
        public string DonorClass { get; set; }
        public ulong  AssetId    { get; set; }
        public ulong  ProtoId    { get; set; }
        public bool   Confident  { get; set; }
        public string MeshOuter  { get; set; }
    }

    public static class DonorDetector
    {

        public static async Task<DonorGuess> DetectAsync(
            string upkPath, DonorTables tables, Action<string> log = null)
        {

            var manifest = ManifestReader.FindAndRead(upkPath);
            if (manifest?.DonorClass != null)
            {
                log?.Invoke($"manifest ({manifest.Format}) names donor: {manifest.DonorClass}");

                tables.TryResolveAsset(manifest.DonorClass, out ulong mAsset);
                tables.TryResolveProto(manifest.DonorClass, out ulong mProto);

                if (mAsset == 0 || mProto == 0)
                {
                    log?.Invoke($"  ⚠ manifest names \"{manifest.DonorClass}\" but it does not resolve "
                              + $"(assetId={(mAsset == 0 ? "MISSING" : "ok")}, "
                              + $"protoId={(mProto == 0 ? "MISSING" : "ok")}) - ignoring the manifest "
                              + "and detecting from the UPK instead.");
                }
                else
                {
                    return new DonorGuess
                    {
                        DonorClass = manifest.DonorClass,
                        AssetId    = mAsset,
                        ProtoId    = mProto,
                        Confident  = true,
                        MeshOuter  = "(from manifest)",
                    };
                }
            }

            var info = await RenameEngine.GetInfoAsync(upkPath, log);
            var lowerNames = info.Names.Select(n => n.ToLowerInvariant()).ToList();

            AnalyzeVisualUpdate(lowerNames, log);

            string donorFromPkg = null;

            string classExport = info.ClassExport;
            if (!string.IsNullOrEmpty(classExport) &&
                classExport.StartsWith("marvelplayer_", StringComparison.OrdinalIgnoreCase))
            {
                string tok = classExport.Substring("marvelplayer_".Length);
                if (tok.IndexOf('_') > 0)
                {
                    donorFromPkg = tok;
                    log?.Invoke($"donor token from the CLASS EXPORT (authoritative): {classExport}");
                }
            }

            if (donorFromPkg == null)
            {
                foreach (var low in lowerNames)
                {
                    if (low.StartsWith("uc__marvelplayer_") && low.EndsWith("_sf"))
                    {
                        string mid = low.Substring("uc__marvelplayer_".Length);
                        donorFromPkg = mid.Substring(0, mid.Length - "_sf".Length);
                        break;
                    }
                }
            }
            if (donorFromPkg != null)
            {
                string want = donorFromPkg.Replace("_", "");
                foreach (var donorClass in tables.AllDonorClasses)
                {
                    string stripped = StripPrefix(donorClass).Replace("_", "");
                    if (stripped.Equals(want, StringComparison.OrdinalIgnoreCase))
                    {
                        log?.Invoke($"detected donor from UPK package: {donorClass}");
                        return MakeGuess(donorClass, tables, donorFromPkg);
                    }
                }
            }

            foreach (var group in info.MeshGroups)
            {
                string g = group.ToLowerInvariant();
                foreach (var donorClass in tables.AllDonorClasses)
                {
                    if (!StripPrefix(donorClass).ToLowerInvariant().Equals(g, StringComparison.Ordinal))
                        continue;

                    log?.Invoke($"detected donor from MESH GROUP \"{group}\": {donorClass}");
                    return MakeGuess(donorClass, tables, group);
                }
            }

            (string cls, int best, int second) ScoreDonors(string heroFilter)
            {
                string bc = null; int b = 0, s = 0;
                foreach (var donorClass in tables.AllDonorClasses)
                {
                    string token = StripPrefix(donorClass).ToLowerInvariant();
                    if (token.Length < 4) continue;

                    if (heroFilter != null &&
                        !token.Equals(heroFilter, StringComparison.Ordinal) &&
                        !token.StartsWith(heroFilter + "_", StringComparison.Ordinal))
                        continue;

                    int count = 0;
                    foreach (var low in lowerNames)
                        if (low.Contains(token)) count++;

                    if (count > b) { s = b; b = count; bc = donorClass; }
                    else if (count > s) { s = count; }
                }
                return (bc, b, s);
            }

            string heroScope = null;
            if (donorFromPkg != null)
            {
                int hu = donorFromPkg.IndexOf('_');
                heroScope = hu > 0 ? donorFromPkg.Substring(0, hu) : donorFromPkg;
            }

            var scored = (cls: (string)null, best: 0, second: 0);
            bool heroScoped = false;

            if (!string.IsNullOrEmpty(heroScope))
            {
                var s1 = ScoreDonors(heroScope);
                if (s1.cls != null && s1.best > 0)
                {
                    scored = s1;
                    heroScoped = true;
                    log?.Invoke($"donor scan scoped to hero \"{heroScope}\" " +
                                "(the UPK's package name names a costume that isn't a selectable donor)");
                }
            }

            if (scored.cls == null)
                scored = ScoreDonors(null);

            string bestClass = scored.cls;
            int bestCount = scored.best, secondCount = scored.second;

            if (bestClass != null && bestCount > 0)
            {
                string token = StripPrefix(bestClass).ToLowerInvariant();
                log?.Invoke($"UPK token scan{(heroScoped ? " (hero-scoped)" : "")}: \"{token}\" " +
                            $"appears in {bestCount} names (next best {secondCount})");

                bool confident = bestCount >= secondCount + 2;
                if (!confident)
                    log?.Invoke("  match is close — confirm the donor in the picker before installing");
                var g = MakeGuess(bestClass, tables, token);
                g.Confident = confident;
                return g;
            }

            log?.Invoke("couldn't identify the donor from the UPK — choose it manually");
            return null;
        }

        static void AnalyzeVisualUpdate(List<string> lowerNames, Action<string> log)
        {
            int vuTextures = lowerNames.Count(n => n.Contains("_vu_") || n.EndsWith("vu"));
            bool hasPhysics = lowerNames.Any(n => n.Contains("physics"));
            bool hasUnergo  = lowerNames.Any(n => n.StartsWith("unergo_"));

            bool isVu = vuTextures > 0 || hasUnergo;
            if (!isVu && !hasPhysics)
            {
                log?.Invoke("  costume type: standard (no visual-update markers)");
                return;
            }

            var parts = new List<string>();
            if (vuTextures > 0) parts.Add($"{vuTextures} _vu_ texture name(s)");
            if (hasPhysics)     parts.Add("a PhysicsAsset");
            if (hasUnergo)      parts.Add("an unergo_ reference");

            log?.Invoke($"  costume type: VISUAL UPDATE (_vu_) — carries {string.Join(", ", parts)}");
            log?.Invoke("    note: _vu_ costumes are structurally heavier; if this one fails to");
            log?.Invoke("    load in-game, that extra structure is the likely cause.");
        }

        static string StripPrefix(string donorClass)
        {
            const string prefix = "MarvelPlayer_";
            return donorClass.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? donorClass.Substring(prefix.Length) : donorClass;
        }

        static DonorGuess MakeGuess(string donorClass, DonorTables tables, string meshOuter)
        {
            tables.TryResolveAsset(donorClass, out ulong asset);
            tables.TryResolveProto(donorClass, out ulong proto);
            return new DonorGuess
            {
                DonorClass = donorClass, AssetId = asset, ProtoId = proto,
                Confident = true, MeshOuter = meshOuter,
            };
        }

        public static bool ExcludeLinkTimeRefs = true;

        public static bool ExcludeStubTextures = true;

        public static bool ExcludeVuTextures = false;

        public static bool ProtectImportedNames = false;

        static bool IsVuStubTextureName(string lowerName)
        {
            int idx = lowerName.IndexOf("_vu_", StringComparison.Ordinal);
            if (idx < 0) return false;
            string tail = lowerName.Substring(idx + 4);

            string[] suffixes = { "diff", "norm", "spec", "speccolor", "smspsk",
                                  "coat_diff", "coat_norm", "coat_spec", "coat_smspsk",
                                  "mask", "emask", "alpha" };
            foreach (var s in suffixes)
                if (tail == s) return true;
            return false;
        }

        static readonly List<string> _lastExcluded = new List<string>();
        public static IReadOnlyList<string> LastExcludedNames => _lastExcluded;

        static readonly List<RenamePair> _lastClassRepair = new List<RenamePair>();
        public static IReadOnlyList<RenamePair> LastClassRepair => _lastClassRepair;

        public static bool IsLinkTimeReference(string lowerName)
        {
            if (lowerName.StartsWith("unergo_", StringComparison.Ordinal))
                return true;

            if (lowerName.Contains("marvelplayeraudio"))
                return true;

            if (ExcludeVuTextures && IsVuStubTextureName(lowerName))
                return true;

            int u = lowerName.IndexOf('_');
            if (u > 0 && lowerName.Length > u + 4)
            {
                bool allDigits = true;
                for (int i = 0; i < u; i++)
                    if (lowerName[i] < '0' || lowerName[i] > '9') { allDigits = false; break; }
                if (allDigits &&
                    string.Compare(lowerName, u + 1, "uc__", 0, 4, StringComparison.Ordinal) == 0)
                    return true;
            }
            return false;
        }

        public static List<RenamePair> BuildRenamesFromNames(
            string donorClass, string customName, IEnumerable<string> upkNames,
            IEnumerable<string> stubTextureNames = null,
            IEnumerable<string> collisionNames = null,
            string actualClassExportName = null)
        {
            _lastExcluded.Clear();
            _lastClassRepair.Clear();

            var stubs = new HashSet<string>(
                stubTextureNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            string donorToken  = StripPrefixStatic(donorClass).ToLowerInvariant();
            int us = donorToken.IndexOf('_');
            string hero = us > 0 ? donorToken.Substring(0, us) : donorToken;
            string customToken = hero + "_" + customName.ToLowerInvariant();

            var pairs = new List<RenamePair>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var allNames = upkNames as IList<string> ?? upkNames?.ToList() ?? new List<string>();

            bool donorTokenIsBareHero = !donorToken.Contains('_');

            foreach (var name in donorTokenIsBareHero ? Enumerable.Empty<string>() : allNames)
            {
                string low = name.ToLowerInvariant();
                if (!low.Contains(donorToken)) continue;
                if (!seen.Add(name)) continue;

                if (ExcludeStubTextures && stubs.Contains(name))
                {
                    _lastExcluded.Add(name);
                    continue;
                }

                if (ExcludeLinkTimeRefs && IsLinkTimeReference(low))
                {
                    _lastExcluded.Add(name);
                    continue;
                }

                string renamed = ReplaceTokenPreservingCase(name, donorToken, customToken);
                if (!renamed.Equals(name, StringComparison.Ordinal))
                    pairs.Add(new RenamePair(name, renamed));
            }

            string expectedClassName = "marvelplayer_" + customToken;

            bool classRenamed;
            if (!string.IsNullOrEmpty(actualClassExportName))
                classRenamed = string.Equals(actualClassExportName, expectedClassName,
                                             StringComparison.OrdinalIgnoreCase)
                            || pairs.Any(p => string.Equals(p.From, actualClassExportName,
                                                            StringComparison.OrdinalIgnoreCase)
                                           && string.Equals(p.To, expectedClassName,
                                                            StringComparison.OrdinalIgnoreCase));
            else
                classRenamed = pairs.Any(
                    p => string.Equals(p.To, expectedClassName, StringComparison.OrdinalIgnoreCase));

            if (!classRenamed)
            {

                string actualToken = !string.IsNullOrEmpty(actualClassExportName)
                                     && actualClassExportName.StartsWith("marvelplayer_",
                                                                         StringComparison.OrdinalIgnoreCase)
                    ? actualClassExportName["marvelplayer_".Length..]
                    : DetectClassToken(allNames);

                if (!string.IsNullOrEmpty(actualToken) &&
                    !actualToken.Equals(customToken, StringComparison.OrdinalIgnoreCase))
                {

                    var repair = new[]
                    {
                        ($"marvelplayer_{actualToken}",          $"marvelplayer_{customToken}"),
                        ($"default__marvelplayer_{actualToken}", $"default__marvelplayer_{customToken}"),
                        ($"uc__marvelplayer_{actualToken}_sf",   $"uc__marvelplayer_{customToken}_sf"),
                    };

                    foreach (var (oldName, newName) in repair)
                    {

                        string actual = allNames.FirstOrDefault(
                            n => string.Equals(n, oldName, StringComparison.OrdinalIgnoreCase));

                        if (actual == null)
                        {

                            bool isKnownExport =
                                !string.IsNullOrEmpty(actualClassExportName) &&
                                (string.Equals(oldName, actualClassExportName,
                                               StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(oldName, "default__" + actualClassExportName,
                                               StringComparison.OrdinalIgnoreCase));
                            if (!isKnownExport) continue;
                            actual = oldName;
                        }
                        if (!seen.Add(actual)) continue;
                        if (ExcludeStubTextures && stubs.Contains(actual)) { _lastExcluded.Add(actual); continue; }
                        if (ExcludeLinkTimeRefs && IsLinkTimeReference(actual.ToLowerInvariant()))
                        { _lastExcluded.Add(actual); continue; }

                        var repairPair = new RenamePair(actual, newName);
                        pairs.Add(repairPair);
                        _lastClassRepair.Add(repairPair);
                    }
                }
            }

            if (collisionNames != null)
            {
                foreach (var name in collisionNames)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!seen.Add(name)) continue;

                    if (ExcludeStubTextures && stubs.Contains(name)) { _lastExcluded.Add(name); continue; }
                    if (ExcludeLinkTimeRefs && IsLinkTimeReference(name.ToLowerInvariant()))
                    { _lastExcluded.Add(name); continue; }

                    string renamed = UniquifyWithToken(name, customToken);
                    if (!renamed.Equals(name, StringComparison.Ordinal))
                        pairs.Add(new RenamePair(name, renamed));
                }
            }

            return pairs;
        }

        static string ReplaceTokenPreservingCase(string original, string token, string replacement)
        {
            var sb = new System.Text.StringBuilder(original.Length + 16);
            int i = 0;
            while (i < original.Length)
            {
                if (i + token.Length <= original.Length &&
                    string.Compare(original, i, token, 0, token.Length,
                                   StringComparison.OrdinalIgnoreCase) == 0)
                {
                    sb.Append(replacement);
                    i += token.Length;
                }
                else
                {
                    sb.Append(original[i]);
                    i++;
                }
            }
            return sb.ToString();
        }

        static string UniquifyWithToken(string original, string customToken)
        {
            if (string.IsNullOrEmpty(original)) return original;
            string prefix = customToken + "_";
            if (original.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return original;
            return prefix + original;
        }

        public static string DetectClassToken(IEnumerable<string> upkNames)
        {
            if (upkNames == null) return null;

            const string classPrefix = "marvelplayer_";
            const string cdoPrefix   = "default__marvelplayer_";

            var classTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cdoTokens   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in upkNames)
            {
                if (string.IsNullOrEmpty(name)) continue;
                string low = name.ToLowerInvariant();

                if (low.StartsWith(cdoPrefix, StringComparison.Ordinal))
                {
                    string t = low.Substring(cdoPrefix.Length);
                    if (t.Length > 0) cdoTokens.Add(t);
                }
                else if (low.StartsWith(classPrefix, StringComparison.Ordinal))
                {
                    string t = low.Substring(classPrefix.Length);

                    if (t.Length > 0 && !low.Contains("marvelplayeraudio")) classTokens.Add(t);
                }
            }

            classTokens.IntersectWith(cdoTokens);

            return classTokens.Count == 1 ? classTokens.First() : null;
        }

        public static string StripPrefixStatic(string donorClass)
        {
            const string prefix = "MarvelPlayer_";
            return donorClass.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? donorClass.Substring(prefix.Length) : donorClass;
        }

        public static List<RenamePair> BuildRenames(string donorClass, string customName)
        {
            const string prefix = "MarvelPlayer_";
            string stripped = donorClass.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? donorClass.Substring(prefix.Length) : donorClass;

            string donorInternal = stripped.ToLowerInvariant();

            int us = donorInternal.IndexOf('_');
            string hero = us > 0 ? donorInternal.Substring(0, us) : donorInternal;
            string customInternal = hero + "_" + customName.ToLowerInvariant();

            string donorComposite  = donorInternal + "." + donorInternal;
            string customComposite = customInternal + "." + customInternal;

            return new List<RenamePair>
            {
                new($"uc__marvelplayer_{donorInternal}_sf",   $"uc__marvelplayer_{customInternal}_sf"),
                new($"marvelplayer_{donorInternal}",          $"marvelplayer_{customInternal}"),
                new($"default__marvelplayer_{donorInternal}", $"default__marvelplayer_{customInternal}"),
                new(donorInternal,                            customInternal),
                new(donorComposite,                           customComposite),
                new($"{donorInternal}_as",                    $"{customInternal}_as"),
                new($"{donorInternal}_mat",                   $"{customInternal}_mat"),
                new($"{donorInternal}_mecharms",              $"{customInternal}_mecharms"),
            };
        }

        public static string CustomPackageName(string donorClass, string customName)
        {
            const string prefix = "MarvelPlayer_";
            string stripped = donorClass.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? donorClass.Substring(prefix.Length) : donorClass;
            string donorInternal = stripped.ToLowerInvariant();
            int us = donorInternal.IndexOf('_');
            string hero = us > 0 ? donorInternal.Substring(0, us) : donorInternal;
            string customInternal = hero + "_" + customName.ToLowerInvariant();
            return $"uc__marvelplayer_{customInternal}_sf";
        }

        public static List<AliasPair> BuildTfcAliases(
            IEnumerable<RenamePair> renamePairs, IEnumerable<string> manifestPackages)
        {
            var pairs = new List<AliasPair>();
            if (renamePairs == null || manifestPackages == null) return pairs;

            var renameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rp in renamePairs)
            {
                if (rp?.From == null || rp.To == null) continue;

                AddRenameKey(renameMap, rp.From, rp.To);
                int dotF = rp.From.IndexOf('.');
                int dotT = rp.To.IndexOf('.');
                if (dotF > 0 && dotT > 0)
                    AddRenameKey(renameMap, rp.From.Substring(0, dotF), rp.To.Substring(0, dotT));
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pkg in manifestPackages)
            {
                if (string.IsNullOrEmpty(pkg)) continue;

                if (renameMap.TryGetValue(pkg, out string newPkgLower))
                {

                    string customCased = ReCaseRenamed(pkg, pkg , newPkgLower);
                    if (seen.Add(pkg))
                        pairs.Add(new AliasPair(pkg, customCased));
                }
            }
            return pairs;
        }

        static void AddRenameKey(Dictionary<string, string> map, string from, string to)
        {
            if (!map.ContainsKey(from)) map[from] = to;
        }

        static string ReCaseRenamed(string casedOld, string lowerOld, string lowerNew)
        {

            int pre = 0;
            int maxPre = Math.Min(lowerOld.Length, lowerNew.Length);
            while (pre < maxPre &&
                   char.ToLowerInvariant(lowerOld[pre]) == char.ToLowerInvariant(lowerNew[pre]))
                pre++;

            int suf = 0;
            int maxSuf = Math.Min(lowerOld.Length, lowerNew.Length) - pre;
            while (suf < maxSuf &&
                   char.ToLowerInvariant(lowerOld[lowerOld.Length - 1 - suf]) ==
                   char.ToLowerInvariant(lowerNew[lowerNew.Length - 1 - suf]))
                suf++;

            int midLen = lowerNew.Length - pre - suf;
            string middle = midLen > 0 ? lowerNew.Substring(pre, midLen) : "";
            string middleTitle = middle.Length > 0
                ? char.ToUpperInvariant(middle[0]) + middle.Substring(1)
                : "";

            string casedPrefix = casedOld.Substring(0, Math.Min(pre, casedOld.Length));
            string casedSuffix = (suf > 0 && suf <= casedOld.Length)
                ? casedOld.Substring(casedOld.Length - suf)
                : "";

            return casedPrefix + middleTitle + casedSuffix;
        }

        public static AliasPair BuildTfcAlias(string donorClass, string customName)
        {
            const string prefix = "MarvelPlayer_";
            string donorHeroCostume = donorClass.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? donorClass.Substring(prefix.Length) : donorClass;
            int us = donorHeroCostume.IndexOf('_');
            string hero = us > 0 ? donorHeroCostume.Substring(0, us) : donorHeroCostume;
            string customHeroCostume = hero + "_" + customName;
            return new AliasPair(donorHeroCostume, customHeroCostume);
        }

        public static string CustomClassPath(string donorClass, string customName)
        {
            const string prefix = "MarvelPlayer_";
            string donorHeroCostume = donorClass.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? donorClass.Substring(prefix.Length) : donorClass;
            int us = donorHeroCostume.IndexOf('_');
            string hero = us > 0 ? donorHeroCostume.Substring(0, us) : donorHeroCostume;
            string customHeroCostume = hero + "_" + customName;
            return $"marvelgamecontent.MarvelPlayer_{customHeroCostume}";
        }
    }
}
