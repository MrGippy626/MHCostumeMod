using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace CostumeManager.Core
{
    public static partial class Installer
    {

        static List<string> ParentsFromChain(JsonObject entry)
        {
            var parents = new List<string>();
            if (entry == null) return parents;

            var fx = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (entry["effects"] is JsonArray fxArr)
                foreach (JsonNode n in fxArr)
                    if (n is JsonObject o && o["package"] != null) fx.Add(o["package"].ToString());

            if (entry["chain"] is JsonArray chain)
                for (int i = 1; i < chain.Count; i++)
                {
                    string p = chain[i]?.ToString();
                    if (!string.IsNullOrWhiteSpace(p) && !fx.Contains(p)) parents.Add(p);
                }
            return parents;
        }

        public static UninstallResult MigrateFxPacks(string gameRoot, Action<string> log = null,
                                                     string ledgerPath = null)
        {
            var res = new UninstallResult();
            try
            {
                string jsonPath = CostumeLibrary.CustomCostumesJson(gameRoot);
                if (!CostumeConfig.Exists(jsonPath))
                { res.Error = "no costume config found under " + gameRoot; return res; }

                JsonNode root = JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath));

                List<InstallRecord> ledger = InstallLedger.Read(ledgerPath);
                List<FxPack> packs = FxPackRegistry.Read();

                var existing = new HashSet<string>(packs.Select(p => p.Token),
                                                   StringComparer.OrdinalIgnoreCase);
                int adopted = 0, assigned = 0, skipped = 0;

                foreach (InstallRecord rec in ledger)
                {
                    if (rec.FxPackages == null || rec.FxPackages.Count == 0) continue;
                    if (string.IsNullOrWhiteSpace(rec.Name))
                    {
                        res.Steps.Add("⚠ ledger record for enum " + rec.Enum
                                    + " has FX but no token - cannot adopt it, skipped");
                        skipped++;
                        continue;
                    }

                    JsonObject entry = FindEntry(root, rec.Enum);
                    string hero = entry != null
                        ? FxCompatibility.HeroOfCostume((string)entry["donorClass"])
                        : null;

                    List<string> parents = ParentsFromChain(entry);

                    if (!existing.Contains(rec.Name))
                    {
                        packs.Add(new FxPack
                        {
                            Token = rec.Name,

                            DisplayName = (rec.DisplayName ?? rec.Name) + " effects",
                            Hero = hero,
                            SourceFolder = null,
                            InstalledUtc = rec.InstalledUtc,
                            Parents = parents,
                            Effects = rec.FxPackages.Select(f => new FxRecord
                            {
                                UpkPath = f.UpkPath,
                                Package = f.Package,
                                ClassPath = f.ClassPath,
                                FromAsset = f.FromAsset,
                                EffectName = f.EffectName,
                                SourceCrc = f.SourceCrc,
                            }).ToList(),
                        });
                        existing.Add(rec.Name);
                        adopted++;
                        res.Steps.Add("adopted pack \"" + rec.Name + "\" ("
                                    + rec.FxPackages.Count + " package(s), hero "
                                    + (hero ?? "unknown") + ")");
                    }

                    else
                    {

                        FxPack had = packs.FirstOrDefault(x =>
                            string.Equals(x.Token, rec.Name, StringComparison.OrdinalIgnoreCase));
                        if (had != null && (had.Parents == null || had.Parents.Count == 0)
                            && parents.Count > 0)
                        {
                            had.Parents = parents;
                            adopted++;
                            res.Steps.Add("pack \"" + rec.Name + "\": recovered "
                                        + parents.Count + " parent package(s) - "
                                        + string.Join(", ", parents));
                        }
                    }

                    if (entry == null)
                    {
                        res.Steps.Add("⚠ pack \"" + rec.Name + "\" adopted, but no costume with "
                                    + "enum " + rec.Enum + " is in the config to assign it to");
                        continue;
                    }
                    if ((string)entry["fxPack"] == null)
                    {
                        entry["fxPack"] = rec.Name;
                        assigned++;
                        res.Steps.Add("\"" + entry["name"] + "\" -> pack \"" + rec.Name + "\"");
                    }
                }

                if (adopted == 0 && assigned == 0)
                {
                    res.Ok = true;
                    res.Steps.Add("nothing to migrate - every installed FX pack is already in the registry");
                    if (skipped > 0) res.Steps.Add("(" + skipped + " skipped, see above)");
                    return res;
                }

                if (adopted > 0) FxPackRegistry.Write(packs);

                if (assigned > 0)
                {
                    Backup.Timestamped(CostumeConfig.ExistingPath(jsonPath));

                    CostumeConfig.WriteAllText(jsonPath, root.ToJsonString(JsonOpts()));
                }

                res.Ok = true;
                res.Steps.Add("migrated: " + adopted + " pack(s) adopted, "
                            + assigned + " costume assignment(s) recorded");
                if (log != null) foreach (string s in res.Steps) log("  " + s);
            }
            catch (Exception ex)
            {
                res.Error = ex.Message;
            }
            return res;
        }
    }
}
