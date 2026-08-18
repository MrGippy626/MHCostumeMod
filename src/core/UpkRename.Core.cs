using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

using UpkManager.Helpers;
using UpkManager.Models;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Models.UpkFile.Engine.Texture;
using UpkManager.Models.UpkFile.Objects;

namespace UpkRename.Core
{

    public sealed class RenamePair
    {
        public string From { get; set; }
        public string To   { get; set; }
        public RenamePair() { }
        public RenamePair(string from, string to) { From = from; To = to; }
    }

    public sealed class RenameResult
    {
        public bool     Ok           { get; set; }
        public int      Applied      { get; set; }
        public long     BytesWritten { get; set; }
        public string   OutputPath   { get; set; }
        public List<string> NotFound { get; set; } = new List<string>();

        public List<string> SplitNames { get; set; } = new List<string>();

        public List<string> BlockedNames { get; set; } = new List<string>();

        public List<string> NumericRenames { get; set; } = new List<string>();
    }

    public sealed class VerifyResult
    {
        public bool   Identical      { get; set; }
        public long   OriginalLength { get; set; }
        public long   RebuiltLength  { get; set; }
        public string OriginalSha    { get; set; }
        public string RebuiltSha     { get; set; }
        public int    FirstDiffAt    { get; set; } = -1;

        public bool   Truncated         { get; set; }
        public long   FileLength        { get; set; }
        public long   MaxSerialEnd      { get; set; }
        public int    TruncatedExports  { get; set; }
        public string FirstTruncatedName { get; set; }
    }

    public sealed class UpkInfo
    {
        public int  Version    { get; set; }
        public int  Licensee   { get; set; }
        public int  NameCount  { get; set; }
        public int  ImportCount{ get; set; }
        public int  ExportCount{ get; set; }
        public uint Flags       { get; set; }
        public uint Compression { get; set; }
        public List<string> Names { get; set; } = new List<string>();

        public List<string> StubTextureNames { get; set; } = new List<string>();

        public List<string> MeshGroups { get; set; } = new List<string>();

        public string ClassExport { get; set; }
    }

    public static class RenameEngine
    {

        public static async Task<UnrealHeader> LoadAsync(string path, Action<string> log = null)
        {
            byte[] bytes = File.ReadAllBytes(path);

            var reader = ByteArrayReader.CreateNew(bytes, 0);
            var header = new UnrealHeader(reader)
            {
                FullFilename = Path.GetFullPath(path),
                FileSize     = bytes.LongLength
            };

            await header.ReadHeaderAsync(p =>
            {
                if (log == null) return;

            });

            log?.Invoke($"loaded {Path.GetFileName(path)}");
            return header;
        }

        public static async Task<byte[]> RebuildAsync(UnrealHeader header, Action<string> log = null)
        {
            int tablesSize = header.GetBuilderSize();

            var offsets = new int[header.ExportTable.Count];
            var sizes   = new int[header.ExportTable.Count];

            int cursor = tablesSize;
            for (int i = 0; i < header.ExportTable.Count; i++)
            {
                offsets[i] = cursor;
                sizes[i]   = header.ExportTable[i].GetObjectSize(cursor);
                cursor    += sizes[i];
            }
            int totalSize = cursor;

            log?.Invoke($"tables {tablesSize:N0} bytes, total {totalSize:N0} bytes");

            var writer = ByteArrayWriter.CreateNew(totalSize);

            await header.WriteBuffer(writer, 0);

            int written = 0, empty = 0;
            for (int i = 0; i < header.ExportTable.Count; i++)
            {

                var objWriter = await header.ExportTable[i].WriteObjectBuffer();
                writer.Seek(offsets[i]);
                if (objWriter != null)
                {
                    await writer.WriteBytes(objWriter.GetBytes());
                    if (sizes[i] > 0) written++; else empty++;
                }
                else empty++;
            }

            if (empty > 0)
                log?.Invoke($"WARNING: {empty} export objects were EMPTY (size 0 - data missing)");
            else
                log?.Invoke($"wrote {written} export objects");

            return writer.GetBytes();
        }

        static string Sha(byte[] b)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(b)).Replace("-", "");
        }

        public static async Task<VerifyResult> VerifyAsync(string path, Action<string> log = null)
        {
            var header   = await LoadAsync(path, log);
            byte[] orig  = File.ReadAllBytes(path);
            byte[] rebd  = await RebuildAsync(header, log);

            var r = new VerifyResult
            {
                OriginalLength = orig.Length,
                RebuiltLength  = rebd.Length,
                OriginalSha    = Sha(orig),
                RebuiltSha     = Sha(rebd),
            };
            r.Identical = orig.Length == rebd.Length && r.OriginalSha == r.RebuiltSha;

            if (!r.Identical)
            {
                int n = Math.Min(orig.Length, rebd.Length);
                for (int i = 0; i < n; i++)
                    if (orig[i] != rebd[i]) { r.FirstDiffAt = i; break; }
            }

            r.FileLength = orig.Length;
            if ((uint)header.CompressionFlags == 0)
            {
                long maxEnd = 0;
                foreach (UnrealExportTableEntry e in header.ExportTable)
                {
                    long end = (long)e.SerialDataOffset + e.SerialDataSize;
                    if (end > maxEnd) maxEnd = end;
                    if (e.SerialDataSize > 0 && end > orig.Length)
                    {
                        r.TruncatedExports++;
                        r.FirstTruncatedName ??= (e.ObjectNameIndex?.Name ?? $"export#{r.TruncatedExports}");
                    }
                }
                r.MaxSerialEnd = maxEnd;
                r.Truncated = r.TruncatedExports > 0;
            }
            return r;
        }

        public static async Task<UpkInfo> GetInfoAsync(string path, Action<string> log = null)
        {
            var h = await LoadAsync(path, log);
            var info = new UpkInfo
            {
                Version     = h.Version,
                Licensee    = h.Licensee,
                NameCount   = h.NameTable.Count,
                ImportCount = h.ImportTable.Count,
                ExportCount = h.ExportTable.Count,
                Flags       = (uint)h.Flags,
                Compression = (uint)h.CompressionFlags,
            };
            for (int i = 0; i < h.NameTable.Count; i++)
                info.Names.Add(h.NameTable[i].Name.String);

            info.StubTextureNames = await DetectStubTexturesAsync(h);
            info.MeshGroups = CollectMeshGroups(h);
            try { info.ClassExport = CostumeManager.Core.UpkClassExports.Find(h, "marvelplayer_").FirstOrDefault(); }
            catch { }
            return info;
        }

        public static List<string> CollectMeshGroups(UnrealHeader h)
        {
            var byGroup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (UnrealExportTableEntry e in h.ExportTable)
            {
                string cls = e.ClassReferenceNameIndex?.Name ?? "";
                if (!cls.Equals("SkeletalMesh", StringComparison.OrdinalIgnoreCase) &&
                    !cls.Equals("StaticMesh", StringComparison.OrdinalIgnoreCase))
                    continue;

                string outer = e.OuterReferenceNameIndex?.Name;
                if (string.IsNullOrWhiteSpace(outer)) continue;

                int size = e.SerialDataSize;
                if (!byGroup.TryGetValue(outer, out int prev) || size > prev)
                    byGroup[outer] = size;
            }

            return byGroup.OrderByDescending(kv => kv.Value)
                          .Select(kv => kv.Key)
                          .ToList();
        }

        public static async Task<List<string>> DetectStubTexturesAsync(UnrealHeader h)
        {
            const uint STORE_IN_SEPARATE_FILE = 0x00000001;
            var stubs = new List<string>();
            foreach (UnrealExportTableEntry e in h.ExportTable)
            {
                string cls = e.ClassReferenceNameIndex?.Name ?? "";
                if (!cls.Equals("Texture2D", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (e.UnrealObject == null) await e.ParseUnrealObject(false, false);
                    if (e.UnrealObject is not IUnrealObject uo || uo.UObject is not UTexture2D tex)
                        continue;
                    bool anyTfc = false;
                    if (tex.Mips != null)
                        foreach (var m in tex.Mips)
                            if ((m.BulkDataFlags & STORE_IN_SEPARATE_FILE) != 0) { anyTfc = true; break; }
                    if (anyTfc)
                    {
                        string nm = e.ObjectNameIndex?.Name;
                        if (!string.IsNullOrEmpty(nm)) stubs.Add(nm);
                    }
                }
                catch {  }
            }
            return stubs;
        }

        public static HashSet<string> GetImportDependentNames(UnrealHeader header)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (header?.ImportTable == null) return names;

            foreach (UnrealImportTableEntry imp in header.ImportTable)
            {
                string nm = imp.ObjectNameIndex?.Name;
                if (!string.IsNullOrEmpty(nm)) names.Add(nm);

                int outerRef = imp.OuterReference;
                int guard = 0;
                while (outerRef != 0 && guard++ < 32)
                {
                    UnrealObjectTableEntryBase entry;
                    try { entry = header.GetObjectTableEntry(outerRef); }
                    catch { break; }
                    if (entry == null) break;
                    string on = entry.ObjectNameIndex?.Name;
                    if (!string.IsNullOrEmpty(on)) names.Add(on);
                    if (entry is UnrealImportTableEntry outerImp) outerRef = outerImp.OuterReference;
                    else break;
                }
            }
            return names;
        }

        public static async Task<RenameResult> RenameAsync(
            string inPath, string outPath, IEnumerable<RenamePair> pairs, Action<string> log = null)
        {
            var header = await LoadAsync(inPath, log);
            return await RenameLoadedAsync(header, outPath, pairs, log);
        }

        static int RepointExportsByDisplayName(UnrealHeader header, string fromDisplay, string to)
        {
            if (header?.ExportTable == null || string.IsNullOrEmpty(fromDisplay)) return 0;

            var targets = header.ExportTable
                                .Where(e => e.ObjectNameIndex != null
                                         && string.Equals(e.ObjectNameIndex.Name, fromDisplay,
                                                          StringComparison.OrdinalIgnoreCase))
                                .ToList();
            if (targets.Count == 0) return 0;

            if (targets.All(e => e.ObjectNameIndex.Numeric == 0)) return 0;

            int srcIdx = targets[0].ObjectNameIndex.Index;
            ulong flags = (srcIdx >= 0 && srcIdx < header.NameTable.Count)
                          ? header.NameTable[srcIdx].Flags : 0UL;

            var text = new UnrealString();
            text.SetString(to);

            var added = new UnrealNameTableEntry();
            added.SetNameTableEntry(text, flags, header.NameTable.Count);
            header.NameTable.Add(added);

            foreach (var e in targets) e.ObjectNameIndex.SetNameTableIndex(added, 0);
            return targets.Count;
        }

        static HashSet<UnrealExportTableEntry> FindImportAnchors(UnrealHeader header, int nameIdx)
        {
            var anchors = new HashSet<UnrealExportTableEntry>();

            foreach (UnrealImportTableEntry imp in header.ImportTable)
            {
                int r = imp.OuterReference;
                for (int guard = 0; r != 0 && guard < 32; guard++)
                {
                    UnrealObjectTableEntryBase e;
                    try { e = header.GetObjectTableEntry(r); }
                    catch { break; }
                    if (e == null) break;

                    if (e is UnrealExportTableEntry ex)
                    {
                        if (ex.ObjectNameIndex != null && ex.ObjectNameIndex.Index == nameIdx)
                            anchors.Add(ex);
                        r = ex.OuterReference;
                    }
                    else if (e is UnrealImportTableEntry im) r = im.OuterReference;
                    else break;
                }
            }
            return anchors;
        }

        static bool SplitRename(UnrealHeader header, int idx, string newName,
                                out int movedExports, out bool blocked)
        {
            movedExports = 0;
            blocked      = false;

            var exports = header.ExportTable
                                .Where(e => e.ObjectNameIndex != null && e.ObjectNameIndex.Index == idx)
                                .ToList();

            bool namedByImport = header.ImportTable
                                       .Any(i => i.ObjectNameIndex != null && i.ObjectNameIndex.Index == idx);
            if (exports.Count == 0)
            {
                if (!namedByImport) return false;
                blocked = true;
                return true;
            }

            HashSet<UnrealExportTableEntry> anchors = FindImportAnchors(header, idx);
            if (anchors.Count == 0 && !namedByImport) return false;

            if (anchors.Count == 0) { blocked = true; return true; }

            var movable = exports.Where(e => !anchors.Contains(e)).ToList();
            if (movable.Count == 0)
            {

                blocked = true;
                return true;
            }

            var text = new UnrealString();
            text.SetString(newName);

            var added = new UnrealNameTableEntry();
            added.SetNameTableEntry(text, header.NameTable[idx].Flags, header.NameTable.Count);
            header.NameTable.Add(added);

            foreach (var e in movable)
            {

                e.ObjectNameIndex.SetNameTableIndex(added, e.ObjectNameIndex.Numeric);
                movedExports++;
            }
            return true;
        }

        public static async Task<RenameResult> RenameLoadedAsync(
            UnrealHeader header, string outPath, IEnumerable<RenamePair> pairs, Action<string> log = null)
        {
            var list = pairs.ToList();
            var result = new RenameResult { OutputPath = outPath };

            string selfName = "";
            try { selfName = Path.GetFileNameWithoutExtension(outPath) ?? ""; } catch { }

            foreach (var pair in list)
            {
                bool hit = false;

                int nameCount = header.NameTable.Count;
                for (int i = 0; i < nameCount; i++)
                {
                    var entry = header.NameTable[i];
                    if (!string.Equals(entry.Name.String, pair.From, StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool isSelfRename = selfName.Length > 0
                                     && string.Equals(pair.To, selfName, StringComparison.OrdinalIgnoreCase);

                    if (!isSelfRename &&
                        SplitRename(header, i, pair.To, out int movedExports, out bool blocked))
                    {
                        if (blocked)
                        {
                            log?.Invoke($"[{i}] \"{pair.From}\" KEPT STOCK - an import resolves "
                                      + "through it and nothing else uses the name. Renaming it "
                                      + "would orphan that import (white mesh).");
                            result.BlockedNames.Add(pair.From);
                        }
                        else
                        {
                            log?.Invoke($"[{i}] \"{pair.From}\" -> \"{pair.To}\"  "
                                      + $"(SPLIT: {movedExports} export(s) moved; the group an "
                                      + "import resolves through keeps the stock name)");
                            result.SplitNames.Add(pair.From);
                            result.Applied++;
                        }
                        hit = true;
                        continue;
                    }

                    entry.Name.SetString(pair.To);
                    log?.Invoke($"[{i}] \"{pair.From}\" -> \"{pair.To}\"");
                    result.Applied++;
                    hit = true;
                }
                if (!hit)
                {

                    int moved = RepointExportsByDisplayName(header, pair.From, pair.To);
                    if (moved > 0)
                    {
                        log?.Invoke($"[numeric] \"{pair.From}\" -> \"{pair.To}\"  "
                                  + $"({moved} export(s) repointed; the name is an FName numeric "
                                  + "suffix, not a name-table string)");
                        result.NumericRenames.Add(pair.From);
                        result.Applied++;
                        hit = true;
                    }
                }
                if (!hit)
                {
                    result.NotFound.Add(pair.From);
                    log?.Invoke($"NOT FOUND: \"{pair.From}\"");
                }
            }

            if (result.Applied == 0)
            {
                result.Ok = false;
                return result;
            }

            byte[] rebuilt = await RebuildAsync(header, log);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            File.WriteAllBytes(outPath, rebuilt);

            result.Ok           = true;
            result.BytesWritten = rebuilt.Length;
            log?.Invoke($"wrote {outPath} ({rebuilt.Length:N0} bytes)");
            return result;
        }
    }
}
