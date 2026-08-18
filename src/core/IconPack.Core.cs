using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using DDSLib;
using DDSLib.Constants;

using MHTexLib;

namespace IconPack.Core
{

    public static partial class IconPackBuilder
    {
        public static IconPackResult Build(
            uint costumeEnum,
            string donorUpkPath,
            string outputUpkPath,
            IEnumerable<IconSource> sources,
            Action<string> log)
        {
            var result = new IconPackResult
            {
                PackageFName = PackageFNameForEnum(costumeEnum),
                OutputUpkPath = outputUpkPath,
            };

            string pkg = PackageNameForEnum(costumeEnum).ToLowerInvariant();
            var art = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (IconSource src in sources ?? Enumerable.Empty<IconSource>())
                {
                    if (src == null || string.IsNullOrWhiteSpace(src.ImagePath)) continue;
                    if (!File.Exists(src.ImagePath))
                    {
                        result.FailedStep = "images";
                        result.Steps.Add($"ABORT: image not found for {src.Role}: {src.ImagePath}");
                        return result;
                    }
                    art[RoleInfo(src.Role).ShortName] = src.ImagePath;
                }

                if (art.Count == 0)
                {
                    result.FailedStep = "images";
                    result.Steps.Add("ABORT: no icon images supplied - nothing to build.");
                    return result;
                }
                result.Steps.Add($"[1/4] {art.Count} icon image(s) resolved.");
            }
            catch (Exception ex)
            {
                result.FailedStep = "images";
                result.Steps.Add("ABORT resolving images: " + ex.Message);
                return result;
            }

            UpkFile upk;
            try
            {
                if (!File.Exists(donorUpkPath))
                {
                    result.FailedStep = "load";
                    result.Steps.Add("ABORT: donor icon package not found: " + donorUpkPath);
                    return result;
                }

                upk = UpkFile.Load(donorUpkPath);
                log?.Invoke($"  donor loaded: names {upk.NameTable.Count}, exports {upk.ExportTable.Count}");

                var renames = new List<(string From, string To)>
                {
                    (DonorSelfName, PackageFNameForEnum(costumeEnum)),
                    (DonorPackageName, pkg),
                };
                foreach (IconRoleInfo r in Roles)
                    renames.Add((r.DonorTexture, r.ShortName));

                foreach ((string from, string to) in renames)
                {
                    int hits = 0;
                    for (int i = 0; i < upk.NameTable.Count; i++)
                    {
                        if (!string.Equals(upk.NameTable[i].String, from, StringComparison.OrdinalIgnoreCase))
                            continue;
                        upk.NameTable[i].String = to;
                        hits++;
                    }
                    if (hits == 0) log?.Invoke($"  WARNING: rename \"{from}\" matched nothing");
                }
                result.Steps.Add("[2/4] donor cloned and renamed to " + pkg + ".");
            }
            catch (Exception ex)
            {
                result.FailedStep = "load";
                result.Steps.Add("ABORT loading/renaming donor: " + ex.Message);
                return result;
            }

            int replaced = 0, reinjected = 0, tfcLeft = 0;
            try
            {
                foreach ((int index, ExportEntry export) in upk.TextureExports().ToList())
                {
                    string name = NameOf(upk, export);
                    Texture2DProperties props = upk.ParseTex2DProperties(index);
                    (byte[] PixelData, int Width, int Height, string FormatName) px = upk.ExtractPixelData(index);

                    if (art.TryGetValue(name, out string imagePath))
                    {
                        if (props == null)
                        {
                            result.FailedStep = "textures";
                            result.Steps.Add($"ABORT: no Texture2D properties on \"{name}\" to base the replacement on.");
                            return result;
                        }

                        bool wasTfc = px.PixelData == null || px.PixelData.Length == 0;
                        DdsInfo dds = LoadOrEncode(imagePath, props);

                        props.SizeX = dds.Width;
                        props.SizeY = dds.Height;
                        props.OriginalSizeX = dds.Width;
                        props.OriginalSizeY = dds.Height;
                        props.Format = dds.PixelFormat;

                        props.TextureFileCacheName = null;
                        props.TextureFileCacheGuid = Guid.Empty;
                        props.NeverStream = true;

                        Builders.ReplaceTexture2D(upk, index, dds, props);
                        replaced++;
                        log?.Invoke($"  [art] {name} <- {Path.GetFileName(imagePath)} " +
                                    $"{dds.Width}x{dds.Height}{(wasTfc ? "  (was TFC-backed, now inline)" : "")}");
                        continue;
                    }

                    if (props == null || px.PixelData == null || px.PixelData.Length == 0)
                    {

                        result.RemainingTfcTextures.Add(name);
                        tfcLeft++;
                        continue;
                    }

                    Builders.ReplaceTexture2D(upk, index, new DdsInfo
                    {
                        Width = px.Width,
                        Height = px.Height,
                        PixelFormat = props.Format,
                        PixelData = px.PixelData,
                    }, props);
                    reinjected++;
                }

                if (replaced == 0)
                {
                    result.FailedStep = "textures";
                    result.Steps.Add("ABORT: no custom art was applied - refusing to write a pointless package.");
                    return result;
                }
                result.Steps.Add($"[3/4] {replaced} custom, {reinjected} re-injected for fixups, {tfcLeft} left on TFC.");
            }
            catch (Exception ex)
            {
                result.FailedStep = "textures";
                result.Steps.Add("ABORT building textures: " + ex.Message);
                return result;
            }

            try
            {
                string dir = Path.GetDirectoryName(outputUpkPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                upk.Save(outputUpkPath);

                UpkFile check = UpkFile.Load(outputUpkPath);
                long bytes = new FileInfo(outputUpkPath).Length;
                result.Steps.Add($"[4/4] wrote {Path.GetFileName(outputUpkPath)} ({bytes:N0} bytes), " +
                                 $"re-read OK ({check.ExportTable.Count} exports).");
            }
            catch (Exception ex)
            {
                result.FailedStep = "write";
                result.Steps.Add("ABORT writing package: " + ex.Message);
                TryDelete(outputUpkPath, log);
                return result;
            }

            foreach (IconSource src in sources.Where(s => s != null && !string.IsNullOrWhiteSpace(s.ImagePath)))
            {
                IconRoleInfo info = RoleInfo(src.Role);
                foreach (uint off in info.ProtoOffsets)
                {
                    result.Patches.Add(new IconPatch
                    {
                        Offset = off,
                        AssetId = AssetIdFor(costumeEnum, src.Role),
                        Path = IconPathFor(costumeEnum, src.Role),
                    });
                }
            }

            result.Ok = true;
            return result;
        }

        private static DdsInfo LoadOrEncode(string imagePath, Texture2DProperties donorProps)
        {
            if (string.Equals(Path.GetExtension(imagePath), ".dds", StringComparison.OrdinalIgnoreCase))
                return DdsParser.Parse(imagePath);

            (byte[] rgba, int w, int h) = ImageIo.LoadRgba(imagePath, donorProps.SizeX, donorProps.SizeY);

            FileFormat fmt = donorProps.Format == MHTexLib.PixelFormat.PF_DXT1
                ? FileFormat.DXT1
                : FileFormat.DXT5;

            DdsFile dds = DdsFile.FromRgba(w, h, rgba, fmt);
            using var ms = new MemoryStream();

            dds.Save(ms, new DdsSaveConfig(fmt, 0, 0, false, true));
            return DdsParser.Parse(ms.ToArray());
        }

        private static string NameOf(UpkFile upk, ExportEntry e)
        {
            int i = e.ObjectNameIdx;
            return i >= 0 && i < upk.NameTable.Count ? upk.NameTable[i].String : "<idx " + i + ">";
        }

        private static void TryDelete(string path, Action<string> log)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { log?.Invoke("  could not remove partial file: " + ex.Message); }
        }
    }
}
