using System;
using System.Collections.Generic;
using System.Linq;

namespace CostumeManager.Core
{

    public enum FxSiblingVerdict
    {

        NotApplicable,

        Refused,

        Offered,

        OfferedUnlikely,
    }

    public sealed class FxSiblingDecision
    {
        public FxSiblingVerdict Verdict { get; set; }
        public string ClassLeaf { get; set; }
        public int OfficialExporters { get; set; }
        public bool CoverageComplete { get; set; }
        public bool ClientPredicted { get; set; }

        public string Headline { get; set; }

        public string Tooltip { get; set; }

        public bool Offerable
        {
            get
            {
                return Verdict == FxSiblingVerdict.Offered
                    || Verdict == FxSiblingVerdict.OfferedUnlikely;
            }
        }
    }

    public static class FxSiblingPolicy
    {

        public const string VisualTest =
            "How to tell whether you need this — cast the power twice, once wearing this custom " +
            "costume and once wearing a STOCK costume of the same hero:\n\n" +
            "  • custom looks right, stock looks stock   →  leave this OFF, it already works\n" +
            "  • custom looks STOCK                      →  turn it ON, this is what fixes it\n" +
            "  • BOTH look custom                        →  the effect is leaking onto stock. " +
            "Turning it ON stops the leak, but it may also take the custom art away — that " +
            "effect can only be one or the other.\n\n" +
            "Nothing is destroyed either way: the original file is kept and you can flip this " +
            "back at any time.";

        public static FxSiblingDecision Evaluate(FxCandidate cand,
                                                 EffectRecord stockEffect,
                                                 ICollection<string> coveredStockFiles)
        {
            var d = new FxSiblingDecision { Verdict = FxSiblingVerdict.NotApplicable };
            if (cand == null || cand.AllClassExports == null) return d;

            List<string> siblings = cand.AllClassExports
                .Where(n => !string.IsNullOrEmpty(n)
                            && !string.Equals(n, cand.ClassLeaf, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (siblings.Count == 0) return d;

            foreach (string leaf in siblings)
            {
                int exporters;
                string why;
                if (!FxRefDb.IsSharedClassName(leaf, null, out exporters, out why)) continue;

                d.ClassLeaf = leaf;
                d.OfficialExporters = exporters;
                d.CoverageComplete = FxRefDb.CoveredBy(leaf, coveredStockFiles);

                if (!d.CoverageComplete)
                {
                    d.Verdict = FxSiblingVerdict.Refused;
                    d.Headline = "shared with stock — cannot be changed";
                    d.Tooltip =
                        "\"" + leaf + "\" is exported by " + exporters + " official game " +
                        "packages, and this install does not replace all of them.\n\n" +
                        "Renaming only some of them cannot change what you see — the copies " +
                        "left under the stock name still win. There is nothing to turn on here.";
                    return d;
                }

                d.ClientPredicted = stockEffect == null
                                 || stockEffect.Summons == null
                                 || stockEffect.Summons.Count == 0;

                if (d.ClientPredicted)
                {
                    d.Verdict = FxSiblingVerdict.Offered;
                    d.Headline = "may need this — see tooltip";
                    d.Tooltip =
                        "This power builds its projectile on your own machine, so it finds its " +
                        "art BY NAME. This install replaces all " + exporters + " game packages " +
                        "that define \"" + leaf + "\", so renaming it is possible and safe.\n\n" +
                        "Whether it HELPS depends on which copy the game happens to load first, " +
                        "and that cannot be known without looking.\n\n" + VisualTest;
                }
                else
                {
                    d.Verdict = FxSiblingVerdict.OfferedUnlikely;
                    d.Headline = "probably not needed";
                    d.Tooltip =
                        "The server creates this power's projectile, so the game looks its art " +
                        "up by ID rather than by name — the shared name \"" + leaf + "\" usually " +
                        "does not matter, and this effect most likely already works.\n\n" +
                        "Turning it on is allowed (all " + exporters + " game packages that " +
                        "define it are replaced by this install) but it has not been needed in " +
                        "any confirmed case.\n\n" + VisualTest;
                }
                return d;
            }

            return d;
        }
    }
}
