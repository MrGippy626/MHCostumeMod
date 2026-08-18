using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TfcAlias.Core
{

    public sealed class Mip
    {
        public int  Index;
        public uint Offset;
        public uint Size;
    }

    public sealed class Entry
    {
        public string FullName    = "";
        public string TfcFileName = "";
        public byte[] Guid        = new byte[16];
        public List<Mip> Mips     = new List<Mip>();

        public string PackageName
        {
            get { int d = FullName.LastIndexOf('.'); return d > 0 ? FullName.Substring(0, d) : ""; }
        }
        public string TextureName
        {
            get { int d = FullName.LastIndexOf('.'); return d >= 0 && d + 1 < FullName.Length ? FullName.Substring(d + 1) : FullName; }
        }
    }

    public sealed class AliasPair
    {
        public string From { get; set; }
        public string To   { get; set; }
        public AliasPair() { }
        public AliasPair(string from, string to) { From = from; To = to; }
    }

    public sealed class AliasRecord
    {
        public string DonorFull  { get; set; }
        public string CustomFull { get; set; }
        public string Texture    { get; set; }
        public string Tfc        { get; set; }
        public int    Mips       { get; set; }
        public uint   FirstOffset{ get; set; }
    }

    public sealed class AliasResult
    {
        public bool Ok      { get; set; }
        public int  Added   { get; set; }
        public int  Skipped { get; set; }
        public int  TotalEntries { get; set; }
        public long BytesWritten { get; set; }
        public List<string> NotFound { get; set; } = new List<string>();

        public List<AliasRecord> Records { get; set; } = new List<AliasRecord>();
    }

    public sealed class TfcVerifyResult
    {
        public bool   Identical      { get; set; }
        public int    EntryCount     { get; set; }
        public bool   TextureFirst   { get; set; }
        public long   OriginalLength { get; set; }
        public long   RebuiltLength  { get; set; }
        public string OriginalSha    { get; set; }
        public string RebuiltSha     { get; set; }
    }

    public sealed class TfcManifest
    {
        public List<Entry> Entries { get; }
        public bool TextureFirst   { get; }

        private TfcManifest(List<Entry> entries, bool textureFirst)
        {
            Entries = entries;
            TextureFirst = textureFirst;
        }

        public static TfcManifest Load(string path)
        {
            using (var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                uint count = br.ReadUInt32();
                var list = new List<Entry>((int)Math.Min(count, 100000));

                bool textureFirst = false;
                if (count > 0)
                {
                    long start = br.BaseStream.Position;
                    try { textureFirst = ReadUeString(br).IndexOf('.') >= 0; }
                    catch { }
                    br.BaseStream.Position = start;
                }

                for (uint i = 0; i < count; i++)
                {
                    if (br.BaseStream.Position >= br.BaseStream.Length) break;

                    var e = new Entry();
                    uint mipCount;
                    try
                    {
                        if (textureFirst)
                        {
                            e.FullName    = ReadUeString(br);
                            e.Guid        = br.ReadBytes(16);
                            e.TfcFileName = ReadUeString(br);
                            mipCount      = br.ReadUInt32();
                        }
                        else
                        {
                            e.TfcFileName = ReadUeString(br);
                            e.FullName    = ReadUeString(br);
                            e.Guid        = br.ReadBytes(16);
                            mipCount      = br.ReadUInt32();
                        }
                    }
                    catch (EndOfStreamException) { break; }

                    try
                    {
                        for (uint m = 0; m < mipCount; m++)
                            e.Mips.Add(new Mip
                            {
                                Index  = br.ReadInt32(),
                                Offset = br.ReadUInt32(),
                                Size   = br.ReadUInt32(),
                            });
                    }
                    catch (EndOfStreamException) { list.Add(e); break; }

                    list.Add(e);
                }
                return new TfcManifest(list, textureFirst);
            }
        }

        static string ReadUeString(BinaryReader br)
        {
            int len = br.ReadInt32();
            if (len == 0) return string.Empty;
            if (len > 0)
            {
                byte[] b = br.ReadBytes(len);
                int nul = Array.IndexOf(b, (byte)0);
                if (nul >= 0) b = b.Take(nul).ToArray();
                return Encoding.UTF8.GetString(b);
            }
            else
            {
                byte[] b = br.ReadBytes(-len * 2);
                string s = Encoding.Unicode.GetString(b);
                int nul = s.IndexOf('\0');
                if (nul >= 0) s = s.Substring(0, nul);
                return s;
            }
        }

        public byte[] Save()
        {
            using (var ms = new MemoryStream())
            {
                using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
                {
                    bw.Write((uint)Entries.Count);

                    foreach (var e in Entries)
                    {
                        if (TextureFirst)
                        {
                            WriteUeString(bw, e.FullName);
                            bw.Write(e.Guid, 0, 16);
                            WriteUeString(bw, e.TfcFileName);
                            bw.Write((uint)e.Mips.Count);
                        }
                        else
                        {
                            WriteUeString(bw, e.TfcFileName);
                            WriteUeString(bw, e.FullName);
                            bw.Write(e.Guid, 0, 16);
                            bw.Write((uint)e.Mips.Count);
                        }

                        foreach (var m in e.Mips)
                        {
                            bw.Write(m.Index);
                            bw.Write(m.Offset);
                            bw.Write(m.Size);
                        }
                    }
                }
                return ms.ToArray();
            }
        }

        static void WriteUeString(BinaryWriter bw, string s)
        {
            if (string.IsNullOrEmpty(s)) { bw.Write(0); return; }

            bool unicode = s.Any(c => c > 0xFF);
            if (unicode)
            {
                bw.Write(-(s.Length + 1));
                bw.Write(Encoding.Unicode.GetBytes(s));
                bw.Write((ushort)0);
            }
            else
            {
                bw.Write(s.Length + 1);
                bw.Write(Encoding.UTF8.GetBytes(s));
                bw.Write((byte)0);
            }
        }

        public AliasResult AddAliases(IEnumerable<AliasPair> pairs, Action<string> log = null)
        {
            var result = new AliasResult();

            foreach (var pair in pairs)
            {
                var source = Entries
                    .Where(e => string.Equals(e.PackageName, pair.From, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (source.Count == 0)
                {
                    result.NotFound.Add(pair.From);
                    log?.Invoke($"no entries for package \"{pair.From}\" - skipped");
                    continue;
                }

                foreach (var e in source)
                {
                    string newFull = pair.To + "." + e.TextureName;

                    if (Entries.Any(x => string.Equals(x.FullName, newFull, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Skipped++;
                        log?.Invoke($"= {newFull} (already present)");
                        continue;
                    }

                    var alias = new Entry
                    {
                        FullName    = newFull,
                        TfcFileName = e.TfcFileName,
                        Guid        = (byte[])e.Guid.Clone(),
                    };
                    foreach (var m in e.Mips)
                        alias.Mips.Add(new Mip { Index = m.Index, Offset = m.Offset, Size = m.Size });

                    Entries.Add(alias);
                    result.Added++;
                    result.Records.Add(new AliasRecord
                    {
                        DonorFull   = e.FullName,
                        CustomFull  = newFull,
                        Texture     = e.TextureName,
                        Tfc         = e.TfcFileName,
                        Mips        = e.Mips.Count,
                        FirstOffset = e.Mips.Count > 0 ? e.Mips[0].Offset : 0,
                    });
                    log?.Invoke($"+ {newFull} ({e.TfcFileName}, {e.Mips.Count} mips)");
                }
            }

            result.TotalEntries = Entries.Count;
            result.Ok = result.Added > 0 || result.Skipped > 0;
            return result;
        }

        public int RemoveByFullNames(IEnumerable<string> fullNames, Action<string> log = null)
        {
            var targets = new HashSet<string>(fullNames ?? Enumerable.Empty<string>(),
                                              StringComparer.OrdinalIgnoreCase);
            if (targets.Count == 0) return 0;

            int before = Entries.Count;
            var removed = Entries.Where(e => targets.Contains(e.FullName)).ToList();
            Entries.RemoveAll(e => targets.Contains(e.FullName));
            foreach (var e in removed) log?.Invoke($"- {e.FullName}");
            return before - Entries.Count;
        }

        public int RemoveByPackage(string customPkg, Action<string> log = null)
        {
            if (string.IsNullOrEmpty(customPkg)) return 0;
            int before = Entries.Count;
            var removed = Entries
                .Where(e => string.Equals(e.PackageName, customPkg, StringComparison.OrdinalIgnoreCase))
                .ToList();
            Entries.RemoveAll(e => string.Equals(e.PackageName, customPkg, StringComparison.OrdinalIgnoreCase));
            foreach (var e in removed) log?.Invoke($"- {e.FullName}");
            return before - Entries.Count;
        }
    }

    public static class TfcEngine
    {
        static string Sha(byte[] b)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(b)).Replace("-", "");
        }

        public static TfcVerifyResult Verify(string path)
        {
            var m    = TfcManifest.Load(path);
            byte[] orig = File.ReadAllBytes(path);
            byte[] rebd = m.Save();

            var r = new TfcVerifyResult
            {
                EntryCount     = m.Entries.Count,
                TextureFirst   = m.TextureFirst,
                OriginalLength = orig.Length,
                RebuiltLength  = rebd.Length,
                OriginalSha    = Sha(orig),
                RebuiltSha     = Sha(rebd),
            };
            r.Identical = orig.Length == rebd.Length && r.OriginalSha == r.RebuiltSha;
            return r;
        }

        public static AliasResult Alias(
            string inPath, string outPath, IEnumerable<AliasPair> pairs, Action<string> log = null)
        {
            var manifest = TfcManifest.Load(inPath);
            log?.Invoke($"loaded {manifest.Entries.Count:N0} entries " +
                        $"(layout = {(manifest.TextureFirst ? "textureFirst" : "tfcFirst")})");

            var result = manifest.AddAliases(pairs, log);

            if (result.Added > 0)
            {
                byte[] outBytes = manifest.Save();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
                File.WriteAllBytes(outPath, outBytes);
                result.BytesWritten = outBytes.Length;
                log?.Invoke($"{result.Added} added, {result.Skipped} skipped -> " +
                            $"{result.TotalEntries:N0} total; wrote {outPath} ({outBytes.Length:N0} bytes)");
            }
            else
            {
                log?.Invoke($"nothing to add ({result.Skipped} already present)");
            }

            return result;
        }

        public static AliasResult Alias(
            string inPath, string outPath, string fromPkg, string toPkg, Action<string> log = null)
            => Alias(inPath, outPath, new[] { new AliasPair(fromPkg, toPkg) }, log);

        public static int Unalias(
            string inPath, string outPath,
            IEnumerable<string> exactFullNames, string customPkgFallback,
            Action<string> log = null,
            IEnumerable<string> allowedPackages = null,
            IEnumerable<string> donorPackages = null)
        {
            var manifest = TfcManifest.Load(inPath);
            log?.Invoke($"loaded {manifest.Entries.Count:N0} entries " +
                        $"(layout = {(manifest.TextureFirst ? "textureFirst" : "tfcFirst")})");

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in allowedPackages ?? Enumerable.Empty<string>())
                if (!string.IsNullOrWhiteSpace(p)) allowed.Add(p);
            if (!string.IsNullOrWhiteSpace(customPkgFallback)) allowed.Add(customPkgFallback);

            int removed = 0;
            var exact = (exactFullNames?.ToList() ?? new List<string>());

            var refused = exact.Where(n => !allowed.Contains(PackageHalf(n))).ToList();
            if (refused.Count > 0)
            {
                log?.Invoke($"REFUSED to remove {refused.Count} row(s) outside this costume's own "
                          + "package(s) - they are not ours to delete:");
                foreach (string n in refused.Take(10)) log?.Invoke("    - " + n);
            }
            exact = exact.Where(n => allowed.Contains(PackageHalf(n))).ToList();

            if (exact.Count > 0)
                removed = manifest.RemoveByFullNames(exact, log);

            if (removed == 0 && !string.IsNullOrEmpty(customPkgFallback))
            {

                if (donorPackages != null &&
                    donorPackages.Any(d => string.Equals(d, customPkgFallback, StringComparison.OrdinalIgnoreCase)))
                {
                    log?.Invoke($"REFUSED package removal of \"{customPkgFallback}\" - that is a DONOR "
                              + "package, i.e. stock data. Nothing removed.");
                }
                else
                {
                    log?.Invoke($"no exact rows matched; removing by package \"{customPkgFallback}\"");
                    removed = manifest.RemoveByPackage(customPkgFallback, log);
                }
            }

            if (removed > 0)
            {
                byte[] outBytes = manifest.Save();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
                File.WriteAllBytes(outPath, outBytes);
                log?.Invoke($"{removed} removed -> {manifest.Entries.Count:N0} total; " +
                            $"wrote {outPath} ({outBytes.Length:N0} bytes)");
            }
            else
            {
                log?.Invoke("no matching rows to remove");
            }
            return removed;
        }

        static string PackageHalf(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return "";
            int dot = fullName.IndexOf('.');
            return dot < 0 ? fullName : fullName.Substring(0, dot);
        }

        public static List<string> Validate(
            string manifestPath, string donorPkg, string customPkg, AliasResult result)
        {
            var problems = new List<string>();
            var manifest = TfcManifest.Load(manifestPath);

            List<AliasRecord> records = result.Records
                .Where(r => string.Equals(PackageHalf(r.DonorFull), donorPkg,
                                          StringComparison.OrdinalIgnoreCase))
                .ToList();

            var donorTextures = manifest.Entries
                .Where(e => string.Equals(e.PackageName, donorPkg, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.TextureName)
                .ToList();

            var aliasedTextures = records
                .Select(r => r.Texture)
                .ToList();

            foreach (var tex in donorTextures)
                if (!aliasedTextures.Any(a => string.Equals(a, tex, StringComparison.OrdinalIgnoreCase)))
                    problems.Add($"donor texture \"{tex}\" was NOT aliased");

            foreach (var r in records)
            {

                string expectedFull = customPkg + "." + r.Texture;
                if (!string.Equals(r.CustomFull, expectedFull, StringComparison.OrdinalIgnoreCase))
                    problems.Add($"alias \"{r.CustomFull}\" doesn't preserve the texture name " +
                                 $"(expected \"{expectedFull}\")");

                var found = manifest.Entries.FirstOrDefault(e =>
                    string.Equals(e.FullName, r.CustomFull, StringComparison.OrdinalIgnoreCase));
                if (found == null)
                {
                    problems.Add($"alias \"{r.CustomFull}\" is missing from the written manifest");
                    continue;
                }

                var donor = manifest.Entries.FirstOrDefault(e =>
                    string.Equals(e.FullName, r.DonorFull, StringComparison.OrdinalIgnoreCase));
                if (donor != null)
                {
                    if (found.Mips.Count != donor.Mips.Count)
                        problems.Add($"alias \"{r.CustomFull}\" has {found.Mips.Count} mips, " +
                                     $"donor has {donor.Mips.Count}");
                    else if (found.Mips.Count > 0 && found.Mips[0].Offset != donor.Mips[0].Offset)
                        problems.Add($"alias \"{r.CustomFull}\" points at offset {found.Mips[0].Offset}, " +
                                     $"donor is at {donor.Mips[0].Offset} — NOT the same bytes");
                }
            }

            return problems;
        }
    }
}
