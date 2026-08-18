using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace MHTexLib;

public static class Lzo
{
	private const uint ChunkMagic = 2653586369u;

	private const int LzoWorkMemSize = 1835008;

	public static byte[] Decompress1X(byte[] src, int expectedSize)
	{
		List<byte> output = new List<byte>(expectedSize);
		int ip = 0;
		int num = Rd();
		int num2;
		if (num >= 22)
		{
			CopyLit(num - 17);
			num2 = 4;
		}
		else if (num >= 18)
		{
			CopyLit(num - 17);
			num2 = num - 17;
		}
		else
		{
			ip--;
			num2 = 0;
		}
		while (true)
		{
			num = Rd();
			if (num < 16)
			{
				if (num2 == 0)
				{
					int num3 = num & 0xF;
					if (num3 == 0)
					{
						num3 = 15;
						for (; src[ip] == 0; ip++)
						{
							num3 += 255;
						}
						num3 += src[ip++];
					}
					CopyLit(num3 + 3);
					num2 = 4;
				}
				else if (num2 <= 3)
				{
					int num4 = (num >> 2) & 3;
					int num5 = num & 3;
					int distance = (Rd() << 2) + num4 + 1;
					CopyMatch(2, distance);
					CopyLit(num5);
					num2 = num5;
				}
				else
				{
					int num6 = (num >> 2) & 3;
					int num7 = num & 3;
					int distance2 = (Rd() << 2) + num6 + 2049;
					CopyMatch(3, distance2);
					CopyLit(num7);
					num2 = num7;
				}
			}
			else if (num < 32)
			{
				int num8 = num & 7;
				int num9 = (num >> 3) & 1;
				if (num8 == 0)
				{
					num8 = 7;
					for (; src[ip] == 0; ip++)
					{
						num8 += 255;
					}
					num8 += src[ip++];
				}
				int num10 = src[ip] | (src[ip + 1] << 8);
				ip += 2;
				int num11 = num10 >> 2;
				int num12 = num10 & 3;
				int num13 = 16384 + (num9 << 14) + num11;
				if (num13 == 16384)
				{
					break;
				}
				CopyMatch(2 + num8, num13);
				CopyLit(num12);
				num2 = num12;
			}
			else if (num < 64)
			{
				int num14 = num & 0x1F;
				if (num14 == 0)
				{
					num14 = 31;
					for (; src[ip] == 0; ip++)
					{
						num14 += 255;
					}
					num14 += src[ip++];
				}
				int num15 = src[ip] | (src[ip + 1] << 8);
				ip += 2;
				int num16 = num15 >> 2;
				int num17 = num15 & 3;
				int distance3 = num16 + 1;
				CopyMatch(2 + num14, distance3);
				CopyLit(num17);
				num2 = num17;
			}
			else if (num < 128)
			{
				int num18 = (num >> 5) & 1;
				int num19 = (num >> 2) & 7;
				int num20 = num & 3;
				int distance4 = (Rd() << 3) + num19 + 1;
				CopyMatch(3 + num18, distance4);
				CopyLit(num20);
				num2 = num20;
			}
			else
			{
				int num21 = (num >> 5) & 3;
				int num22 = (num >> 2) & 7;
				int num23 = num & 3;
				int distance5 = (Rd() << 3) + num22 + 1;
				CopyMatch(5 + num21, distance5);
				CopyLit(num23);
				num2 = num23;
			}
		}
		return output.ToArray();
		void CopyLit(int n)
		{
			for (int i = 0; i < n; i++)
			{
				output.Add(src[ip++]);
			}
		}
		void CopyMatch(int length, int num25)
		{
			int num24 = output.Count - num25;
			for (int i = 0; i < length; i++)
			{
				output.Add(output[num24++]);
			}
		}
		byte Rd()
		{
			return src[ip++];
		}
	}

	public static byte[] DecompressChunk(byte[] data, int chunkOffset)
	{
		uint num = BitConverter.ToUInt32(data, chunkOffset);
		if (num != 2653586369u)
		{
			throw new InvalidDataException($"Bad chunk magic 0x{num:X8} at 0x{chunkOffset:X8}");
		}
		uint num2 = BitConverter.ToUInt32(data, chunkOffset + 4);
		uint num3 = BitConverter.ToUInt32(data, chunkOffset + 12);
		int num4 = (int)((num3 + num2 - 1) / num2);
		int num5 = chunkOffset + 16;
		(uint, uint)[] array = new(uint, uint)[num4];
		for (int i = 0; i < num4; i++)
		{
			array[i] = (BitConverter.ToUInt32(data, num5 + i * 8), BitConverter.ToUInt32(data, num5 + i * 8 + 4));
		}
		num5 += num4 * 8;
		using MemoryStream memoryStream = new MemoryStream((int)num3);
		(uint, uint)[] array2 = array;
		for (int j = 0; j < array2.Length; j++)
		{
			(uint, uint) tuple = array2[j];
			uint item = tuple.Item1;
			uint item2 = tuple.Item2;
			byte[] array3 = new byte[item];
			Array.Copy(data, num5, array3, 0, (int)item);
			byte[] array4 = Decompress1X(array3, (int)item2);
			if (array4.Length != (int)item2)
			{
				throw new InvalidDataException($"Sub-block decompressed to {array4.Length}, expected {item2}");
			}
			memoryStream.Write(array4, 0, array4.Length);
			num5 += (int)item;
		}
		return memoryStream.ToArray();
	}

	public static (byte[] Data, bool WasCompressed) DecompressUpk(byte[] data)
	{
		uint num = BitConverter.ToUInt32(data, 109);
		uint num2 = BitConverter.ToUInt32(data, 113);
		if (num == 0 || num2 == 0)
		{
			return (Data: data, WasCompressed: false);
		}
		(uint, uint, uint, uint)[] array = new(uint, uint, uint, uint)[num2];
		int num3 = 117;
		for (int i = 0; i < (int)num2; i++)
		{
			array[i] = (BitConverter.ToUInt32(data, num3), BitConverter.ToUInt32(data, num3 + 4), BitConverter.ToUInt32(data, num3 + 8), BitConverter.ToUInt32(data, num3 + 12));
			num3 += 16;
		}
		ref(uint, uint, uint, uint) reference = ref array[^1];
		byte[] array2 = new byte[reference.Item1 + reference.Item2];
		int item = (int)array[0].Item1;
		Array.Copy(data, 0, array2, 0, item);
		(uint, uint, uint, uint)[] array3 = array;
		for (int j = 0; j < array3.Length; j++)
		{
			(uint, uint, uint, uint) tuple = array3[j];
			byte[] array4 = DecompressChunk(data, (int)tuple.Item3);
			if (array4.Length != (int)tuple.Item2)
			{
				throw new InvalidDataException($"Chunk at 0x{tuple.Item3:X8}: decompressed {array4.Length} bytes, expected {tuple.Item2}");
			}
			Array.Copy(array4, 0, array2, (int)tuple.Item1, array4.Length);
		}
		return (Data: array2, WasCompressed: true);
	}

	public static void PatchDecompressedHeader(byte[] buf)
	{
		buf[24] = (byte)(buf[24] & -3);
		BitConverter.GetBytes(0u).CopyTo(buf, 109);
		BitConverter.GetBytes(0u).CopyTo(buf, 113);
		Array.Clear(buf, 117, 400);
	}

	[DllImport("lzo2_64.dll", CallingConvention = CallingConvention.Cdecl)]
	private static extern int lzo1x_999_compress(byte[] src, uint srcLen, byte[] dst, ref uint dstLen, byte[] wrkmem);

	public static byte[] Compress1X(byte[] data)
	{
		byte[] wrkmem = new byte[1835008];
		byte[] array = new byte[data.Length + data.Length / 16 + 64 + 3];
		uint dstLen = (uint)array.Length;
		int num = lzo1x_999_compress(data, (uint)data.Length, array, ref dstLen, wrkmem);
		if (num != 0)
		{
			throw new InvalidOperationException($"lzo1x_1_compress returned error code {num}");
		}
		byte[] array2 = new byte[dstLen];
		Array.Copy(array, array2, dstLen);
		return array2;
	}

	public static byte[] BuildChunk(byte[] pixelData)
	{
		List<(byte[], int)> list = new List<(byte[], int)>();
		for (int i = 0; i < pixelData.Length; i += 131072)
		{
			int num = Math.Min(131072, pixelData.Length - i);
			byte[] array = new byte[num];
			Array.Copy(pixelData, i, array, 0, num);
			list.Add((Compress1X(array), num));
		}
		uint value = (uint)list.Sum<(byte[], int)>(((byte[] Compressed, int Uncompressed) s) => s.Compressed.Length);
		uint value2 = (uint)pixelData.Length;
		using MemoryStream memoryStream = new MemoryStream();
		using BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
		binaryWriter.Write(2653586369u);
		binaryWriter.Write(131072u);
		binaryWriter.Write(value);
		binaryWriter.Write(value2);
		foreach (var (array2, value3) in list)
		{
			binaryWriter.Write((uint)array2.Length);
			binaryWriter.Write((uint)value3);
		}
		foreach (var item2 in list)
		{
			byte[] item = item2.Item1;
			binaryWriter.Write(item);
		}
		return memoryStream.ToArray();
	}
}
