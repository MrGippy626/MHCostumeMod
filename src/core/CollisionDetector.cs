using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Tables;
using UpkRename.Core;

namespace CostumeManager.Core
{

    public static class CollisionDetector
    {
        public sealed class Report
        {
            public string DonorClass    { get; set; }
            public string DonorPkgLeaf  { get; set; }
            public bool   DonorInDb     { get; set; }

            public List<string> Collisions { get; } = new();

            public List<string> MeshGroupCollisions { get; } = new();

            public List<string> CollisionsProtectedTfc    { get; } = new();
            public List<string> CollisionsProtectedImport { get; } = new();

            public List<string> AlreadyTokenRenamed { get; } = new();

            public int DonorObjectCount { get; set; }
            public int ModExportCount   { get; set; }

            public int SkippedNonRenderCollisions { get; set; }

            public bool AnySafeCollisions => Collisions.Count > 0;

            public string ToText()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Collision report — donor '{DonorClass}' (package '{DonorPkgLeaf}')");
                if (!DonorInDb)
                {
                    sb.AppendLine("  DONOR NOT IN REFERENCE DB — cannot detect collisions.");
                    sb.AppendLine("  Build the reference DB from CookedPCConsole first, then re-run.");
                    return sb.ToString();
                }
                sb.AppendLine($"  donor objects: {DonorObjectCount}   mod local exports: {ModExportCount}");
                sb.AppendLine();

                sb.AppendLine($"  COLLISIONS TO FIX ({Collisions.Count}) — local exports that will bind to the");
                sb.AppendLine("  resident donor at runtime (rename these to break the collision):");
                if (Collisions.Count == 0) sb.AppendLine("    (none)");
                foreach (var n in Collisions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine($"    • {n}"
                        + (MeshGroupCollisions.Contains(n) ? "   << MESH GROUP (whole-mesh collision — the main bug)" : ""));
                sb.AppendLine();

                if (CollisionsProtectedTfc.Count > 0)
                {
                    sb.AppendLine($"  COLLIDES BUT TFC-PROTECTED ({CollisionsProtectedTfc.Count}) — leave stock");
                    sb.AppendLine("  (name-based TFC lookup needs the stock name; do NOT rename):");
                    foreach (var n in CollisionsProtectedTfc.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                        sb.AppendLine($"    • {n}");
                    sb.AppendLine();
                }

                if (CollisionsProtectedImport.Count > 0)
                {
                    sb.AppendLine($"  COLLIDES BUT IMPORT-PROTECTED ({CollisionsProtectedImport.Count}) — leave stock");
                    sb.AppendLine("  (import resolves against a stock package by name; do NOT rename):");
                    foreach (var n in CollisionsProtectedImport.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                        sb.AppendLine($"    • {n}");
                    sb.AppendLine();
                }

                if (AlreadyTokenRenamed.Count > 0)
                {
                    sb.AppendLine($"  (already handled by token-rename: {AlreadyTokenRenamed.Count} name(s) contain the donor token)");
                }
                if (SkippedNonRenderCollisions > 0)
                {
                    sb.AppendLine($"  (ignored {SkippedNonRenderCollisions} harmless non-render collision(s) — " +
                        "animations, sockets, physics, particles: identical shared-skeleton sub-objects, not the render bug)");
                }
                return sb.ToString();
            }
        }

        public static async Task<Report> DetectAsync(
            string modUpkPath, string donorClass, string customName, string dbPath,
            Action<string> log = null)
        {
            var rep = new Report
            {
                DonorClass   = donorClass,
                DonorPkgLeaf = CostumeReferenceDb.DonorPkgLeafFromClass(donorClass),
            };

            rep.DonorInDb = CostumeReferenceDb.HasPackage(dbPath, rep.DonorPkgLeaf);
            if (!rep.DonorInDb)
            {
                log?.Invoke($"donor package '{rep.DonorPkgLeaf}' not in reference DB");
                return rep;
            }

            var donorNames = CostumeReferenceDb.GetDonorExportNames(dbPath, rep.DonorPkgLeaf);
            rep.DonorObjectCount = donorNames.Count;

            var header = await RenameEngine.LoadAsync(modUpkPath, null);

            HashSet<string> tfc;
            {
                var probe = await RenameEngine.LoadAsync(modUpkPath, null);
                var stubs = await RenameEngine.DetectStubTexturesAsync(probe);
                tfc = new HashSet<string>(stubs, StringComparer.OrdinalIgnoreCase);
            }
            var importDep = RenameEngine.GetImportDependentNames(header);

            string donorToken = DonorDetector.StripPrefixStatic(donorClass).ToLowerInvariant();

            var modExports = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (UnrealExportTableEntry e in header.ExportTable)
            {
                string leaf = e.ObjectNameIndex?.Name;
                if (string.IsNullOrEmpty(leaf)) continue;
                string cls = e.ClassReferenceNameIndex?.Name ?? "";

                if (!modExports.ContainsKey(leaf)) modExports[leaf] = cls;
            }
            rep.ModExportCount = modExports.Count;

            foreach (var kv in modExports)
            {
                string leaf = kv.Key;
                string cls  = kv.Value;

                if (!donorNames.Contains(leaf)) continue;

                if (!IsRenderRelevantClass(cls))
                {
                    rep.SkippedNonRenderCollisions++;
                    continue;
                }

                if (leaf.ToLowerInvariant().Contains(donorToken))
                {
                    rep.AlreadyTokenRenamed.Add(leaf);
                    continue;
                }

                if (tfc.Contains(leaf))       { rep.CollisionsProtectedTfc.Add(leaf);    continue; }
                if (importDep.Contains(leaf))  { rep.CollisionsProtectedImport.Add(leaf); continue; }

                rep.Collisions.Add(leaf);
            }

            var meshGroupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UnrealExportTableEntry e in header.ExportTable)
            {
                string cls = e.ClassReferenceNameIndex?.Name ?? "";
                if (!cls.Equals("SkeletalMesh", StringComparison.OrdinalIgnoreCase)
                 && !cls.Equals("StaticMesh", StringComparison.OrdinalIgnoreCase))
                    continue;
                string outer = e.OuterReferenceNameIndex?.Name;
                if (!string.IsNullOrEmpty(outer)) meshGroupNames.Add(outer);
            }

            foreach (var group in meshGroupNames)
            {
                if (!donorNames.Contains(group)) continue;
                if (rep.Collisions.Contains(group)) continue;
                if (group.ToLowerInvariant().Contains(donorToken))
                { if (!rep.AlreadyTokenRenamed.Contains(group)) rep.AlreadyTokenRenamed.Add(group); continue; }
                if (tfc.Contains(group))       { rep.CollisionsProtectedTfc.Add(group);    continue; }
                if (importDep.Contains(group)) { rep.CollisionsProtectedImport.Add(group); continue; }
                rep.Collisions.Add(group);
                rep.MeshGroupCollisions.Add(group);
            }

            log?.Invoke(rep.ToText());
            return rep;
        }

        private static readonly HashSet<string> RenderRelevantClasses =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "SkeletalMesh",
                "StaticMesh",
                "MaterialInstanceConstant",
                "MaterialInstanceTimeVarying",
                "Material",
                "Texture2D",
                "LightMapTexture2D",
                "ShadowMapTexture2D",
                "TextureCube",
                "TextureFlipBook",
            };

        public static bool IsRenderRelevantClass(string cls)
            => !string.IsNullOrEmpty(cls) && RenderRelevantClasses.Contains(cls);
    }
}
