using System;
using System.IO;
using System.Text;

namespace MHTexLib;

public static class DdsHelper
{
	private const uint DDPF_FOURCC = 4u;

	private const uint DDSD_CAPS = 1u;

	private const uint DDSD_HEIGHT = 2u;

	private const uint DDSD_WIDTH = 4u;

	private const uint DDSD_PIXELFORMAT = 4096u;

	private const uint DDSD_LINEARSIZE = 524288u;

	private const uint DDSCAPS_TEXTURE = 4096u;

	private static readonly byte[] FourCC_DXT1 = Encoding.ASCII.GetBytes("DXT1");

	private static readonly byte[] FourCC_DXT3 = Encoding.ASCII.GetBytes("DXT3");

	private static readonly byte[] FourCC_DXT5 = Encoding.ASCII.GetBytes("DXT5");

	private static (byte[] FourCC, int BlockSize) GetFormat(string ue3FmtName)
	{
		return ue3FmtName.ToLowerInvariant() switch
		{
			"pf_dxt1" => (FourCC: FourCC_DXT1, BlockSize: 8),
			"pf_dxt3" => (FourCC: FourCC_DXT3, BlockSize: 16),
			"pf_dxt5" => (FourCC: FourCC_DXT5, BlockSize: 16),
			_ => throw new NotSupportedException("Unsupported texture format: " + ue3FmtName),
		};
	}

	public static byte[] BuildDds(int width, int height, string ue3FmtName, byte[] pixelData)
	{
		(byte[] FourCC, int BlockSize) format = GetFormat(ue3FmtName);
		byte[] item = format.FourCC;
		int item2 = format.BlockSize;
		int num = Math.Max(1, (width + 3) / 4);
		int num2 = Math.Max(1, (height + 3) / 4);
		int value = num * num2 * item2;
		uint value2 = 528391u;
		using MemoryStream memoryStream = new MemoryStream(128 + pixelData.Length);
		using BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
		binaryWriter.Write(542327876u);
		binaryWriter.Write(124u);
		binaryWriter.Write(value2);
		binaryWriter.Write((uint)height);
		binaryWriter.Write((uint)width);
		binaryWriter.Write((uint)value);
		binaryWriter.Write(0u);
		binaryWriter.Write(1u);
		binaryWriter.Write(new byte[44]);
		binaryWriter.Write(32u);
		binaryWriter.Write(4u);
		binaryWriter.Write(item);
		binaryWriter.Write(0u);
		binaryWriter.Write(0u);
		binaryWriter.Write(0u);
		binaryWriter.Write(0u);
		binaryWriter.Write(0u);
		binaryWriter.Write(4096u);
		binaryWriter.Write(0u);
		binaryWriter.Write(0u);
		binaryWriter.Write(0u);
		binaryWriter.Write(0u);
		binaryWriter.Write(pixelData);
		return memoryStream.ToArray();
	}
}
