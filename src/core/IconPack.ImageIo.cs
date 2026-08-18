using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace IconPack.Core
{

    internal static class ImageIo
    {

        public static (byte[] Rgba, int Width, int Height) LoadRgba(string path, int targetW, int targetH)
        {
            if (targetW <= 0 || targetH <= 0)
                throw new ArgumentException("Target dimensions must be positive.");

            using Image<Rgba32> img = Image.Load<Rgba32>(path);

            img.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(targetW, targetH),

                Mode = ResizeMode.Stretch,

                PremultiplyAlpha = false,

                Sampler = KnownResamplers.Lanczos3,
            }));

            int w = img.Width;
            int h = img.Height;
            var rgba = new byte[w * h * 4];
            img.CopyPixelDataTo(rgba);
            return (rgba, w, h);
        }

        public static (int Width, int Height) PeekSize(string path)
        {
            IImageInfo info = Image.Identify(path);
            return (info.Width, info.Height);
        }
    }
}
