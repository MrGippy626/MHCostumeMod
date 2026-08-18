using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Pfim;

namespace CostumeManager
{

    internal static class DdsPreview
    {
        internal sealed class Loaded
        {
            public WriteableBitmap Image { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public string Format { get; set; }
            public string Error { get; set; }
        }

        internal static Loaded Load(string path)
        {
            try
            {
                using IImage image = Pfimage.FromFile(path);
                return FromPfim(image);
            }
            catch (Exception ex) { return new Loaded { Error = ex.Message }; }
        }

        internal static Loaded Load(byte[] bytes)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                using IImage image = Pfimage.FromStream(ms);
                return FromPfim(image);
            }
            catch (Exception ex) { return new Loaded { Error = ex.Message }; }
        }

        static Loaded FromPfim(IImage image)
        {
            int w = image.Width, h = image.Height;
            byte[] src = image.Data;
            int stride = image.Stride;

            var dst = new byte[w * h * 4];

            switch (image.Format)
            {

                case Pfim.ImageFormat.Rgba32:
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int s = y * stride + x * 4, d = (y * w + x) * 4;
                            byte a = src[s + 3];
                            dst[d + 0] = Mul(src[s + 0], a);
                            dst[d + 1] = Mul(src[s + 1], a);
                            dst[d + 2] = Mul(src[s + 2], a);
                            dst[d + 3] = a;
                        }
                    break;

                case Pfim.ImageFormat.Rgb24:
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int s = y * stride + x * 3, d = (y * w + x) * 4;
                            dst[d + 0] = src[s + 0];
                            dst[d + 1] = src[s + 1];
                            dst[d + 2] = src[s + 2];
                            dst[d + 3] = 255;
                        }
                    break;

                case Pfim.ImageFormat.Rgb8:
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int s = y * stride + x, d = (y * w + x) * 4;
                            byte g = src[s];
                            dst[d + 0] = dst[d + 1] = dst[d + 2] = g;
                            dst[d + 3] = 255;
                        }
                    break;

                default:
                    return new Loaded { Error = $"unsupported DDS pixel format: {image.Format}" };
            }

            var bmp = new WriteableBitmap(w, h);
            using (Stream s = bmp.PixelBuffer.AsStream())
                s.Write(dst, 0, dst.Length);
            bmp.Invalidate();

            return new Loaded { Image = bmp, Width = w, Height = h, Format = image.Format.ToString() };
        }

        static byte Mul(byte c, byte a) => a == 255 ? c : (byte)((c * a + 127) / 255);
    }
}
