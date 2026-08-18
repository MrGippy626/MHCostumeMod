using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CostumeManager.Core
{

    public enum DdsKind { Portrait, StoreIcon, Skin, Unknown }

    public sealed class DdsReplacement
    {
        public string TextureName { get; set; }
        public string DdsFileName { get; set; }
        public DdsKind Kind { get; set; }
    }

    public sealed class ManifestInfo
    {
        public string SourcePath { get; set; }
        public string Format     { get; set; }

        public string DonorClass { get; set; }

        public List<string> UpkFiles { get; } = new();
        public List<DdsReplacement> Dds { get; } = new();
        public bool HasAudio { get; set; }
        public bool IsTextureOnly => UpkFiles.Count == 0;

        public bool DdsAllCosmetic =>
            Dds.All(d => d.Kind == DdsKind.Portrait || d.Kind == DdsKind.StoreIcon);

        public List<string> SkinDds =>
            Dds.Where(d => d.Kind == DdsKind.Skin).Select(d => d.TextureName).ToList();
    }

    public static class ManifestReader
    {

        public static ManifestInfo FindAndRead(string upkOrFolderPath)
        {
            string dir = Directory.Exists(upkOrFolderPath)
                ? upkOrFolderPath
                : Path.GetDirectoryName(Path.GetFullPath(upkOrFolderPath));
            if (dir == null || !Directory.Exists(dir)) return null;

            foreach (var json in Directory.EnumerateFiles(dir, "*.json"))
            {
                var info = TryRead(json);
                if (info != null) return info;
            }
            return null;
        }

        public static ManifestInfo TryRead(string jsonPath)
        {
            string text;
            try { text = File.ReadAllText(jsonPath); }
            catch { return null; }

            JsonDocument doc;
            try { doc = JsonDocument.Parse(text); }
            catch { return null; }

            using (doc)
            {
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                    return ReadTextureManager(jsonPath, root);

                if (root.ValueKind == JsonValueKind.Object &&
                    (root.TryGetProperty("UpkReplacements", out _) ||
                     root.TryGetProperty("Replacements", out _)))
                    return ReadModManager(jsonPath, root);

                return null;
            }
        }

        static ManifestInfo ReadModManager(string path, JsonElement root)
        {
            var info = new ManifestInfo { SourcePath = path, Format = "MHModManager" };

            if (root.TryGetProperty("UpkReplacements", out var upks) &&
                upks.ValueKind == JsonValueKind.Array)
            {
                foreach (var u in upks.EnumerateArray())
                {
                    string name = u.GetString();
                    if (string.IsNullOrEmpty(name)) continue;
                    info.UpkFiles.Add(name);
                }
            }

            var first = info.UpkFiles.FirstOrDefault();
            if (first != null)
                info.DonorClass = DonorFromUpkFileName(first);

            ReadDds(root, "Replacements",      info);
            ReadDds(root, "StoreReplacements", info);

            if (root.TryGetProperty("AudioPacks", out var audio) &&
                audio.ValueKind == JsonValueKind.Array)
                info.HasAudio = audio.GetArrayLength() > 0;

            return info;
        }

        static void ReadDds(JsonElement root, string prop, ManifestInfo info)
        {
            if (!root.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
                return;
            foreach (var e in arr.EnumerateArray())
            {
                string tex = e.TryGetProperty("TextureName", out var t) ? t.GetString() : null;
                string dds = e.TryGetProperty("DdsFileName", out var d) ? d.GetString() : null;
                if (tex == null) continue;
                info.Dds.Add(new DdsReplacement { TextureName = tex, DdsFileName = dds, Kind = ClassifyDds(tex) });
            }
        }

        static ManifestInfo ReadTextureManager(string path, JsonElement arr)
        {
            var info = new ManifestInfo { SourcePath = path, Format = "MHTextureManager" };

            foreach (var el in arr.EnumerateArray())
            {
                foreach (var slot in el.EnumerateObject())
                {
                    if (slot.Value.ValueKind != JsonValueKind.Object) continue;
                    if (slot.Value.TryGetProperty("TextureName", out var tn))
                    {
                        string full = tn.GetString();
                        if (string.IsNullOrEmpty(full)) continue;
                        info.Dds.Add(new DdsReplacement { TextureName = full, Kind = ClassifyDds(full) });

                        if (info.DonorClass == null)
                        {
                            int dot = full.IndexOf('.');
                            string pkg = dot > 0 ? full.Substring(0, dot) : full;

                            info.DonorClass = "MarvelPlayer_" + pkg;
                        }
                    }
                }
            }
            return info;
        }

        public static string DonorFromUpkFileName(string upkFileName)
        {
            string n = Path.GetFileNameWithoutExtension(upkFileName);
            const string pre = "UC__";
            const string suf = "_SF";
            if (n.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) n = n.Substring(pre.Length);
            if (n.EndsWith(suf, StringComparison.OrdinalIgnoreCase))   n = n.Substring(0, n.Length - suf.Length);
            return n;
        }

        public static DdsKind ClassifyDds(string textureName)
        {
            string t = textureName.ToLowerInvariant();

            int dot = t.IndexOf('.');
            string tex = dot >= 0 ? t.Substring(dot + 1) : t;

            if (tex.StartsWith("herohor_") || t.StartsWith("herohor_")) return DdsKind.Portrait;
            if (tex.StartsWith("store_")   || t.StartsWith("store_"))   return DdsKind.StoreIcon;

            if (tex.EndsWith("_diff") || tex.EndsWith("_norm") || tex.EndsWith("_spec") ||
                tex.EndsWith("_d") || tex.EndsWith("_n") || tex.EndsWith("_sp") ||
                tex.Contains("_diffuse") || tex.Contains("_normal"))
                return DdsKind.Skin;

            return DdsKind.Unknown;
        }
    }
}
