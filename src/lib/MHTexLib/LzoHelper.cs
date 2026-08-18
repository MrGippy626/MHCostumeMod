using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MHTexLib;

public static class LzoHelper
{
	private const string LzoDll = "lzo2_64.dll";

	private static bool _initialized;

	private static bool _available;

	private const uint UPK_MAGIC = 2653586369u;

	[DllImport("lzo2_64.dll", EntryPoint = "__lzo_init_v2")]
	private static extern int lzo_init(uint v, int s1, int s2, int s3, int s4, int s5, int s6, int s7, int s8, int s9);

	[DllImport("lzo2_64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "lzo1x_decompress_safe")]
	private static extern int lzo1x_decompress(byte[] src, int src_len, byte[] dst, ref int dst_len, byte[]? wrkmem);

	public static bool IsAvailable()
	{
		if (_initialized)
		{
			return _available;
		}
		_initialized = true;
		try
		{
			_available = lzo_init(1u, -1, -1, -1, -1, -1, -1, -1, -1, -1) == 0;
		}
		catch (DllNotFoundException)
		{
			_available = false;
		}
		catch (BadImageFormatException)
		{
			_available = false;
		}
		return _available;
	}

	public static byte[] Decompress(byte[] source, int uncompressedSize)
	{
		if (!IsAvailable())
		{
			throw new InvalidOperationException("lzo2_64.dll not found. Place it next to the executable.");
		}
		byte[] array = new byte[uncompressedSize];
		int dst_len = uncompressedSize;
		int num = lzo1x_decompress(source, source.Length, array, ref dst_len, null);
		if (num != 0)
		{
			throw new InvalidOperationException($"LZO decompression failed with error code {num}");
		}
		return array;
	}

	public static byte[] DecompressTfcChunk(byte[] data, int offset)
	{
		if (!IsAvailable())
		{
			throw new InvalidOperationException("lzo2_64.dll not found. Place it next to the executable.");
		}
		int num = offset;
		uint num2 = BitConverter.ToUInt32(data, num);
		num += 4;
		if (num2 != 2653586369u)
		{
			throw new InvalidDataException($"Bad TFC chunk magic at 0x{offset:X}: 0x{num2:X8}");
		}
		int num3 = BitConverter.ToInt32(data, num);
		num += 4;
		BitConverter.ToInt32(data, num);
		num += 4;
		int num4 = BitConverter.ToInt32(data, num);
		num += 4;
		int num5 = (num4 + num3 - 1) / num3;
		(int, int)[] array = new(int, int)[num5];
		for (int i = 0; i < num5; i++)
		{
			array[i].Item1 = BitConverter.ToInt32(data, num);
			num += 4;
			array[i].Item2 = BitConverter.ToInt32(data, num);
			num += 4;
		}
		byte[] array2 = new byte[num4];
		int num6 = 0;
		for (int j = 0; j < num5; j++)
		{
			byte[] array3 = new byte[array[j].Item1];
			Array.Copy(data, num, array3, 0, array[j].Item1);
			num += array[j].Item1;
			Array.Copy(Decompress(array3, array[j].Item2), 0, array2, num6, array[j].Item2);
			num6 += array[j].Item2;
		}
		return array2;
	}
}
