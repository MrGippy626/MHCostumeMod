using System;
using System.IO;

namespace MHTexLib;

public static class UpkDecompressor
{
	private const uint UPK_MAGIC = 2653586369u;

	private const uint COMP_LZO = 2u;

	private const uint COMP_LZO_ENC = 8u;

	public static byte[]? TryDecompress(byte[] data)
	{
		if (data.Length < 128)
		{
			return null;
		}
		int num = 0;
		uint num2 = BitConverter.ToUInt32(data, num);
		num += 4;
		if (num2 != 2653586369u)
		{
			return null;
		}
		num += 4;
		num += 4;
		int num3 = BitConverter.ToInt32(data, num);
		num += 4;
		if (num3 > 0)
		{
			num += num3;
		}
		else if (num3 < 0)
		{
			num += -num3 * 2;
		}
		num += 4;
		num += 24;
		num += 4;
		num += 16;
		num += 16;
		int num4 = BitConverter.ToInt32(data, num);
		num += 4;
		num += num4 * 12;
		num += 8;
		uint num5 = BitConverter.ToUInt32(data, num);
		num += 4;
		if (num5 == 0)
		{
			return null;
		}
		if ((num5 & 0xA) == 0)
		{
			throw new NotSupportedException($"Unsupported compression type: 0x{num5:X8}");
		}
		int num6 = BitConverter.ToInt32(data, num);
		num += 4;
		if (num6 <= 0)
		{
			throw new InvalidDataException($"Compressed chunk count is {num6}");
		}
		(int, int, int, int)[] array = new(int, int, int, int)[num6];
		for (int i = 0; i < num6; i++)
		{
			array[i].Item1 = BitConverter.ToInt32(data, num);
			num += 4;
			array[i].Item2 = BitConverter.ToInt32(data, num);
			num += 4;
			array[i].Item3 = BitConverter.ToInt32(data, num);
			num += 4;
			array[i].Item4 = BitConverter.ToInt32(data, num);
			num += 4;
		}
		int item = array[0].Item1;
		int num7 = 0;
		(int, int, int, int)[] array2 = array;
		for (int j = 0; j < array2.Length; j++)
		{
			(int, int, int, int) tuple = array2[j];
			int num8 = tuple.Item1 + tuple.Item2;
			if (num8 > num7)
			{
				num7 = num8;
			}
		}
		byte[] array3 = new byte[num7];
		Array.Copy(data, 0, array3, 0, Math.Min(item, data.Length));
		array2 = array;
		for (int j = 0; j < array2.Length; j++)
		{
			(int, int, int, int) tuple2 = array2[j];
			int item2 = tuple2.Item3;
			uint num9 = BitConverter.ToUInt32(data, item2);
			item2 += 4;
			if (num9 != 2653586369u)
			{
				throw new InvalidDataException($"Bad chunk magic at 0x{tuple2.Item3:X}: 0x{num9:X8}");
			}
			int num10 = BitConverter.ToInt32(data, item2);
			item2 += 4;
			BitConverter.ToInt32(data, item2);
			item2 += 4;
			int num11 = BitConverter.ToInt32(data, item2);
			item2 += 4;
			int num12 = (num11 + num10 - 1) / num10;
			(int, int)[] array4 = new(int, int)[num12];
			for (int k = 0; k < num12; k++)
			{
				array4[k].Item1 = BitConverter.ToInt32(data, item2);
				item2 += 4;
				array4[k].Item2 = BitConverter.ToInt32(data, item2);
				item2 += 4;
			}
			int num13 = tuple2.Item1;
			for (int l = 0; l < num12; l++)
			{
				byte[] array5 = new byte[array4[l].Item1];
				Array.Copy(data, item2, array5, 0, array4[l].Item1);
				item2 += array4[l].Item1;
				Array.Copy(LzoHelper.Decompress(array5, array4[l].Item2), 0, array3, num13, array4[l].Item2);
				num13 += array4[l].Item2;
			}
		}
		int num14 = 0;
		num14 += 12;
		int num15 = BitConverter.ToInt32(array3, num14);
		num14 += 4;
		if (num15 > 0)
		{
			num14 += num15;
		}
		else if (num15 < 0)
		{
			num14 += -num15 * 2;
		}
		int start = num14;
		uint num16 = BitConverter.ToUInt32(array3, num14);
		num16 &= 0xFDFFFFFFu;
		BitConverter.TryWriteBytes(array3.AsSpan(start), num16);
		num14 += 4;
		num14 += 24;
		num14 += 4;
		num14 += 16;
		num14 += 16;
		int num17 = BitConverter.ToInt32(array3, num14);
		num14 += 4;
		num14 += num17 * 12;
		num14 += 8;
		int start2 = num14;
		BitConverter.TryWriteBytes(array3.AsSpan(start2), 0u);
		num14 += 4;
		int start3 = num14;
		BitConverter.TryWriteBytes(array3.AsSpan(start3), 0);
		num14 += 4;
		int num18 = Math.Min(num6 * 16, Math.Max(0, item - num14));
		if (num18 > 0)
		{
			Array.Clear(array3, num14, num18);
		}
		return array3;
	}
}
