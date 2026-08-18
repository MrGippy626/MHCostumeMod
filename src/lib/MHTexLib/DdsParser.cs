using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MHTexLib;

public static class DdsParser
{
	private static readonly Dictionary<string, int> FourCCMap = new Dictionary<string, int>
	{
		["DXT1"] = 5,
		["DXT3"] = 6,
		["DXT5"] = 7
	};

	public static DdsInfo Parse(string path)
	{
		return Parse(File.ReadAllBytes(path));
	}

	public static DdsInfo Parse(byte[] data)
	{
		if (data.Length < 128 || Encoding.ASCII.GetString(data, 0, 4) != "DDS ")
		{
			throw new InvalidDataException("Not a valid DDS file");
		}
		int num = BitConverter.ToInt32(data, 4);
		if (num != 124)
		{
			throw new InvalidDataException($"Unexpected DDS header size: {num}");
		}
		int num2 = BitConverter.ToInt32(data, 12);
		int num3 = BitConverter.ToInt32(data, 16);
		uint num4 = BitConverter.ToUInt32(data, 80);
		string text = Encoding.ASCII.GetString(data, 84, 4);
		if ((num4 & 4) == 0)
		{
			throw new InvalidDataException($"Unsupported DDS format: not FourCC (flags=0x{num4:X})");
		}
		if (!FourCCMap.TryGetValue(text, out var value))
		{
			throw new InvalidDataException("Unsupported DDS FourCC: " + text);
		}
		int num5 = ((value == 5) ? 8 : 16);
		int num6 = Math.Max(1, (num3 + 3) / 4);
		int num7 = Math.Max(1, (num2 + 3) / 4);
		int num8 = num6 * num7 * num5;
		byte[] array = new byte[num8];
		if (data.Length - 128 < num8)
		{
			throw new InvalidDataException($"DDS pixel data too small: {data.Length - 128} < {num8}");
		}
		Array.Copy(data, 128, array, 0, num8);
		return new DdsInfo
		{
			Width = num3,
			Height = num2,
			PixelFormat = value,
			FourCC = text,
			PixelData = array
		};
	}
}
