using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CostumeManager.Core
{

    public sealed class ConditionVariant
    {

        public string SourcePath { get; set; }
        public string FileName { get; set; }

        public string VariantStem { get; set; }

        public string DefaultStem { get; set; }

        public string DonorToken { get; set; }

        public ulong FromAsset { get; set; }

        public string FamilyHero { get; set; }

        public string SkipReason { get; set; }

        public bool Installable => SkipReason == null && FromAsset != 0;
    }

    public static class ConditionVariants
    {
        public const string Prefix = "UC__MarvelConditionEffect_";
        public const string Suffix = "_SF.upk";

        public static string DonorToken(string donorClass)
        {
            if (string.IsNullOrWhiteSpace(donorClass)) return null;
            const string p = "MarvelPlayer_";
            int i = donorClass.IndexOf(p, StringComparison.OrdinalIgnoreCase);
            string rest = i >= 0 ? donorClass.Substring(i + p.Length) : donorClass;

            int u = rest.IndexOf('_');
            if (u < 0 || u + 1 >= rest.Length) return null;
            return rest.Substring(u + 1);
        }

        public static List<ConditionVariant> Find(string sourceFolder, string donorClass,
                                                  EffectTables tables, string cookedDir,
                                                  Action<string> log = null)
        {
            var found = new List<ConditionVariant>();
            if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
                return found;

            string token = DonorToken(donorClass);
            if (string.IsNullOrWhiteSpace(token))
            {
                log?.Invoke("  condition effects: no donor token - nothing to match against");
                return found;
            }

            string[] files;
            try { files = Directory.GetFiles(sourceFolder, Prefix + "*" + Suffix); }
            catch { return found; }

            var shipped = new HashSet<string>(
                files.Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);

            foreach (string f in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(f);
                string stem = name.Substring(Prefix.Length, name.Length - Prefix.Length - Suffix.Length);

                var v = new ConditionVariant
                {
                    SourcePath = f,
                    FileName = name,
                    VariantStem = "MarvelConditionEffect_" + stem,
                    DonorToken = token,
                };

                if (!stem.EndsWith("_" + token, StringComparison.OrdinalIgnoreCase))
                {

                    v.SkipReason = HasAnySuffix(stem)
                        ? "keyed on another costume - it would never resolve for this one"
                        : "not costume-keyed - this belongs in an FX pack, not the costume";
                    found.Add(v);
                    continue;
                }

                string baseStem = stem.Substring(0, stem.Length - token.Length - 1);
                v.DefaultStem = "MarvelConditionEffect_" + baseStem;
                string defaultFile = Prefix + baseStem + Suffix;

                if (shipped.Contains(defaultFile))
                {
                    v.SkipReason = "the mod also ships the default for this family";
                    found.Add(v);
                    continue;
                }

                bool stockDefaultExists = !string.IsNullOrWhiteSpace(cookedDir) &&
                                          File.Exists(Path.Combine(cookedDir, defaultFile));
                if (!stockDefaultExists)
                {
                    v.SkipReason = "no stock default for this family - not a costume-keyed variant";
                    found.Add(v);
                    continue;
                }

                EffectRecord rec = null;
                if (tables != null) tables.ByUpkFileName.TryGetValue(defaultFile, out rec);
                if (rec == null || rec.AssetId == 0)
                {

                    v.SkipReason = "the default has no Effects.json record, so there is no id to redirect";
                    found.Add(v);
                    continue;
                }

                var familyHeroes = new List<string>();
                foreach (string pr in (rec.Protos != null && rec.Protos.Count > 0)
                                          ? (IEnumerable<string>)rec.Protos
                                          : new[] { rec.Proto })
                {
                    string ph = HeroFromProtoPath(pr);
                    if (ph != null && !familyHeroes.Contains(ph, StringComparer.OrdinalIgnoreCase))
                        familyHeroes.Add(ph);
                }
                string familyHero = familyHeroes.FirstOrDefault();
                string donorHero = DonorHero(donorClass);
                if (familyHeroes.Count > 0 && donorHero != null &&
                    !familyHeroes.Contains(donorHero, StringComparer.OrdinalIgnoreCase))
                {
                    v.SkipReason = $"belongs to {string.Join("/", familyHeroes)}, not {donorHero} "
                                 + "- the token is shared between those heroes and this one could "
                                 + "never resolve";
                    found.Add(v);
                    continue;
                }
                v.FamilyHero = donorHero != null &&
                               familyHeroes.Contains(donorHero, StringComparer.OrdinalIgnoreCase)
                                   ? donorHero : familyHero;

                v.FromAsset = rec.AssetId;
                found.Add(v);
            }

            if (log != null)
                foreach (ConditionVariant v in found)
                    log(v.Installable
                        ? $"  condition effect: {v.FileName} -> redirects 0x{v.FromAsset:X16} "
                          + $"({v.DefaultStem}, the DEFAULT - a forged costume key resolves that, "
                          + "not the donor-suffixed variant)"
                        : $"  condition effect: {v.FileName}: {v.SkipReason}");

            return found;
        }

        public static string DonorHero(string donorClass)
        {
            if (string.IsNullOrWhiteSpace(donorClass)) return null;
            const string p = "MarvelPlayer_";
            int i = donorClass.IndexOf(p, StringComparison.OrdinalIgnoreCase);
            string rest = i >= 0 ? donorClass.Substring(i + p.Length) : donorClass;
            int u = rest.IndexOf('_');
            return u > 0 ? rest.Substring(0, u) : (rest.Length > 0 ? rest : null);
        }

        public static string HeroFromProtoPath(string proto)
        {
            if (string.IsNullOrWhiteSpace(proto)) return null;
            string[] parts = proto.Replace('/', '\\').Split('\\');
            for (int i = 0; i + 1 < parts.Length; i++)
                if (string.Equals(parts[i], "Player", StringComparison.OrdinalIgnoreCase) &&
                    i > 0 && string.Equals(parts[i - 1], "Powers", StringComparison.OrdinalIgnoreCase))
                    return parts[i + 1];
            return null;
        }

        static bool HasAnySuffix(string stem)
        {
            int u = stem.LastIndexOf('_');
            return u > 0 && u + 1 < stem.Length;
        }

    }
}
