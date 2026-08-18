using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CostumeManager.Core
{

    public static class EnumAllocator
    {
        public const uint Base = 100000;

        public static uint NextFree(string customCostumesJsonPath, params string[] pendingPurgePaths)
        {
            var taken = new HashSet<uint>();

            ReadEnums(customCostumesJsonPath, "costumes", taken);

            foreach (string p in pendingPurgePaths ?? Array.Empty<string>())
                ReadEnums(p, null, taken);

            uint candidate = Base;
            while (taken.Contains(candidate))
                candidate++;

            return candidate;
        }

        private static void ReadEnums(string path, string arrayProperty, HashSet<uint> into)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            string text = null;
            if (path.EndsWith(CostumeConfig.PlainName, StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(CostumeConfig.PackedName, StringComparison.OrdinalIgnoreCase))
                text = CostumeConfig.ReadAllText(path);
            else if (File.Exists(path))
                text = File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(text)) return;

            try
            {
                using var doc = JsonDocument.Parse(text);

                JsonElement arr;
                if (arrayProperty == null)
                {
                    arr = doc.RootElement;
                }
                else if (!doc.RootElement.TryGetProperty(arrayProperty, out arr))
                {
                    return;
                }

                if (arr.ValueKind != JsonValueKind.Array) return;

                foreach (var e in arr.EnumerateArray())
                {
                    if (e.ValueKind == JsonValueKind.Object &&
                        e.TryGetProperty("enum", out var en) &&
                        en.TryGetUInt32(out uint v))
                    {
                        into.Add(v);
                    }
                }
            }
            catch {  }
        }
    }
}
