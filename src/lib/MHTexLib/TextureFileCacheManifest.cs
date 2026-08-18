using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MHTexLib;

public sealed class TextureFileCacheManifest
{
	private readonly Dictionary<Guid, TfcMipEntry> _firstMip = new Dictionary<Guid, TfcMipEntry>();

	public TextureFileCacheManifest(string binPath)
	{
		using BinaryReader binaryReader = new BinaryReader(File.OpenRead(binPath), Encoding.UTF8, leaveOpen: false);
		uint num = binaryReader.ReadUInt32();
		for (uint num2 = 0u; num2 < num; num2++)
		{
			ReadString(binaryReader);
			Guid key = new Guid(binaryReader.ReadBytes(16));
			ReadString(binaryReader);
			uint num3 = binaryReader.ReadUInt32();
			TfcMipEntry? tfcMipEntry = null;
			for (uint num4 = 0u; num4 < num3; num4++)
			{
				uint index = binaryReader.ReadUInt32();
				uint offset = binaryReader.ReadUInt32();
				uint size = binaryReader.ReadUInt32();
				TfcMipEntry valueOrDefault = tfcMipEntry.GetValueOrDefault();
				if (!tfcMipEntry.HasValue)
				{
					valueOrDefault = new TfcMipEntry(index, offset, size);
					tfcMipEntry = valueOrDefault;
				}
			}
			if (tfcMipEntry.HasValue && !_firstMip.ContainsKey(key))
			{
				_firstMip[key] = tfcMipEntry.Value;
			}
		}
	}

	public bool TryGetByGuid(Guid guid, out TfcMipEntry entry)
	{
		return _firstMip.TryGetValue(guid, out entry);
	}

	private static string ReadString(BinaryReader reader)
	{
		uint count = reader.ReadUInt32();
		byte[] array = reader.ReadBytes((int)count);
		int num = Array.IndexOf(array, (byte)0);
		if (num >= 0)
		{
			array = array[..num];
		}
		return Encoding.UTF8.GetString(array);
	}
}
