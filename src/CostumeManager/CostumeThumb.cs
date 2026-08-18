using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using CostumeManager.Core;
using IconPack.Core;

namespace CostumeManager
{

    internal static class CostumeThumb
    {

        const int DecodePx = 96;

        internal static string IconArtDir =>
            Path.Combine(Path.GetDirectoryName(InstallLedger.DefaultPath) ?? AppContext.BaseDirectory,
                         "IconArt");

        internal static string FindArt(uint enumId, IconRole role)
        {
            try
            {
                if (!Directory.Exists(IconArtDir)) return null;
                return Directory.GetFiles(IconArtDir, $"{enumId}_{role}.*").FirstOrDefault();
            }
            catch { return null; }
        }

        static readonly IconRole[] Preference = { IconRole.Portrait, IconRole.Token, IconRole.Store };

        static readonly Dictionary<uint, ImageSource> _cache = new Dictionary<uint, ImageSource>();

        internal static ImageSource For(uint enumId)
        {
            if (_cache.TryGetValue(enumId, out ImageSource hit)) return hit;

            ImageSource img = Load(enumId) ?? Missing;
            _cache[enumId] = img;
            return img;
        }

        internal static void Forget(uint enumId) => _cache.Remove(enumId);

        internal static void ForgetAll() => _cache.Clear();

        static ImageSource Load(uint enumId)
        {
            foreach (IconRole role in Preference)
            {
                string path = FindArt(enumId, role);
                if (path == null) continue;

                try
                {
                    if (path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                    {

                        DdsPreview.Loaded d = DdsPreview.Load(path);
                        if (d?.Image != null) return d.Image;
                        continue;
                    }

                    var bmp = new BitmapImage { DecodePixelWidth = DecodePx };
                    bmp.UriSource = new Uri(Path.GetFullPath(path));
                    return bmp;
                }
                catch {  }
            }
            return null;
        }

        static ImageSource _missing;

        static ImageSource Missing
        {
            get
            {
                if (_missing != null) return _missing;
                try
                {
                    var bmp = new BitmapImage { DecodePixelWidth = DecodePx };
                    bmp.UriSource = new Uri("ms-appx:///Assets/missing.png");
                    _missing = bmp;
                }
                catch { _missing = null; }
                return _missing;
            }
        }
    }
}
