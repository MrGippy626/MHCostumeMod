using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MHTexLib;

public class UpkFile
{
	private const uint UPK_MAGIC = 2653586369u;

	private int _compressedChunkCount;

	private byte[] _compressedChunksRaw = Array.Empty<byte>();

	private Dictionary<string, int> _nameLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	public ushort Version { get; set; } = 868;

	public ushort Licensee { get; set; } = 3;

	public string FolderName { get; set; } = "None";

	public uint PkgFlags { get; set; }

	public byte[] Guid { get; set; } = new byte[16];

	public uint EngineVersion { get; set; } = 10897u;

	public uint CookerVersion { get; set; } = 136u;

	public uint CompressionFlags { get; set; }

	public uint PackageSource { get; set; }

	public int ImportExportGuidsOffset { get; set; }

	public int ImportGuidsCount { get; set; }

	public int ExportGuidsCount { get; set; }

	public int ThumbnailTableOffset { get; set; }

	public List<GenerationEntry> Generations { get; set; } = new List<GenerationEntry>();

	public List<NameEntry> NameTable { get; set; } = new List<NameEntry>();

	public List<ImportEntry> ImportTable { get; set; } = new List<ImportEntry>();

	public List<ExportEntry> ExportTable { get; set; } = new List<ExportEntry>();

	public List<int> DependsTable { get; set; } = new List<int>();

	public List<TextureAllocation> TextureAllocations { get; set; } = new List<TextureAllocation>();

	public List<string> AdditionalPackages { get; set; } = new List<string>();

	public bool WasCompressed { get; private set; }

	public static UpkFile Load(string path)
	{
		UpkFile upkFile = new UpkFile();
		byte[] data = File.ReadAllBytes(path);
		byte[] array = UpkDecompressor.TryDecompress(data);
		if (array != null)
		{
			List<TextureAllocation> list = ParseTextureAllocationsFromRaw(data);
			uint num = ParsePackageSourceFromRaw(data);
			upkFile.WasCompressed = true;
			upkFile.Parse(array);
			if (list != null && list.Count > 0 && upkFile.TextureAllocations.Count == 0)
			{
				upkFile.TextureAllocations = list;
			}
			if (upkFile.PackageSource == 0 && num != 0)
			{
				upkFile.PackageSource = num;
			}
		}
		else
		{
			upkFile.Parse(data);
		}
		return upkFile;
	}

	private static List<TextureAllocation>? ParseTextureAllocationsFromRaw(byte[] data)
	{
		try
		{
			int num = 12;
			int num2 = BitConverter.ToInt32(data, num);
			num += 4;
			if (num2 > 0)
			{
				num += num2;
			}
			else if (num2 < 0)
			{
				num += -num2 * 2;
			}
			num += 4;
			num += 60;
			int num3 = BitConverter.ToInt32(data, num);
			num += 4;
			num += num3 * 12;
			num += 12;
			int num4 = BitConverter.ToInt32(data, num);
			num += 4;
			num += num4 * 16;
			num += 4;
			int num5 = BitConverter.ToInt32(data, num);
			num += 4;
			for (int i = 0; i < num5; i++)
			{
				int num6 = BitConverter.ToInt32(data, num);
				num += 4;
				num += ((num6 < 0) ? (-num6 * 2) : num6);
			}
			int num7 = BitConverter.ToInt32(data, num);
			num += 4;
			if (num7 <= 0 || num7 > 1000)
			{
				return null;
			}
			List<TextureAllocation> list = new List<TextureAllocation>();
			for (int j = 0; j < num7; j++)
			{
				TextureAllocation textureAllocation = new TextureAllocation
				{
					Width = BitConverter.ToInt32(data, num),
					Height = BitConverter.ToInt32(data, num + 4),
					MipCount = BitConverter.ToInt32(data, num + 8),
					TextureFormat = BitConverter.ToUInt32(data, num + 12),
					CreateFlags = BitConverter.ToUInt32(data, num + 16)
				};
				num += 20;
				int num8 = BitConverter.ToInt32(data, num);
				num += 4;
				for (int k = 0; k < num8; k++)
				{
					textureAllocation.TextureIndices.Add(BitConverter.ToInt32(data, num));
					num += 4;
				}
				list.Add(textureAllocation);
			}
			return list;
		}
		catch
		{
			return null;
		}
	}

	private static uint ParsePackageSourceFromRaw(byte[] data)
	{
		try
		{
			int num = 12;
			int num2 = BitConverter.ToInt32(data, num);
			num += 4;
			if (num2 > 0)
			{
				num += num2;
			}
			else if (num2 < 0)
			{
				num += -num2 * 2;
			}
			num += 64;
			int num3 = BitConverter.ToInt32(data, num);
			num += 4;
			num += num3 * 12 + 4 + 4 + 4;
			int num4 = BitConverter.ToInt32(data, num);
			num += 4;
			num += num4 * 16;
			return BitConverter.ToUInt32(data, num);
		}
		catch
		{
			return 0u;
		}
	}

	private void Parse(byte[] data)
	{
		int o = 0;
		uint num = R_U32(data, ref o);
		if (num != 2653586369u)
		{
			throw new InvalidDataException($"Bad UPK magic: 0x{num:X8}");
		}
		Version = R_U16(data, ref o);
		Licensee = R_U16(data, ref o);
		R_I32(data, ref o);
		FolderName = ReadFString(data, ref o);
		PkgFlags = R_U32(data, ref o);
		int num2 = R_I32(data, ref o);
		int num3 = R_I32(data, ref o);
		int num4 = R_I32(data, ref o);
		int num5 = R_I32(data, ref o);
		int num6 = R_I32(data, ref o);
		int num7 = R_I32(data, ref o);
		int num8 = R_I32(data, ref o);
		ImportExportGuidsOffset = R_I32(data, ref o);
		ImportGuidsCount = R_I32(data, ref o);
		ExportGuidsCount = R_I32(data, ref o);
		ThumbnailTableOffset = R_I32(data, ref o);
		Guid = data[o..(o + 16)];
		o += 16;
		int num9 = R_I32(data, ref o);
		Generations.Clear();
		for (int i = 0; i < num9; i++)
		{
			int exportCount = R_I32(data, ref o);
			int nameCount = R_I32(data, ref o);
			int netObjectCount = R_I32(data, ref o);
			Generations.Add(new GenerationEntry
			{
				ExportCount = exportCount,
				NameCount = nameCount,
				NetObjectCount = netObjectCount
			});
		}
		EngineVersion = R_U32(data, ref o);
		CookerVersion = R_U32(data, ref o);
		CompressionFlags = R_U32(data, ref o);
		_compressedChunkCount = R_I32(data, ref o);
		int num10 = _compressedChunkCount * 16;
		_compressedChunksRaw = data[o..(o + num10)];
		o += num10;
		PackageSource = R_U32(data, ref o);
		int num11 = R_I32(data, ref o);
		AdditionalPackages.Clear();
		for (int j = 0; j < num11; j++)
		{
			AdditionalPackages.Add(ReadFString(data, ref o));
		}
		int num12 = R_I32(data, ref o);
		TextureAllocations.Clear();
		for (int k = 0; k < num12; k++)
		{
			TextureAllocation textureAllocation = new TextureAllocation
			{
				Width = R_I32(data, ref o),
				Height = R_I32(data, ref o),
				MipCount = R_I32(data, ref o),
				TextureFormat = R_U32(data, ref o),
				CreateFlags = R_U32(data, ref o)
			};
			int num13 = R_I32(data, ref o);
			for (int l = 0; l < num13; l++)
			{
				textureAllocation.TextureIndices.Add(R_I32(data, ref o));
			}
			TextureAllocations.Add(textureAllocation);
		}
		o = num3;
		NameTable.Clear();
		for (int m = 0; m < num2; m++)
		{
			string text = ReadFString(data, ref o);
			ulong flags = R_U64(data, ref o);
			NameTable.Add(new NameEntry
			{
				String = text,
				Flags = flags,
				TableIndex = m
			});
		}
		RebuildNameLookup();
		o = num7;
		ImportTable.Clear();
		for (int n = 0; n < num6; n++)
		{
			ImportEntry item = new ImportEntry
			{
				ClassPackageIdx = R_I32(data, ref o),
				ClassPackageNum = R_I32(data, ref o),
				ClassNameIdx = R_I32(data, ref o),
				ClassNameNum = R_I32(data, ref o),
				OuterIndex = R_I32(data, ref o),
				ObjectNameIdx = R_I32(data, ref o),
				ObjectNameNum = R_I32(data, ref o),
				TableIndex = -(n + 1)
			};
			ImportTable.Add(item);
		}
		o = num5;
		ExportTable.Clear();
		for (int num14 = 0; num14 < num4; num14++)
		{
			ExportEntry exportEntry = new ExportEntry
			{
				ClassIndex = R_I32(data, ref o),
				SuperIndex = R_I32(data, ref o),
				OuterIndex = R_I32(data, ref o),
				ObjectNameIdx = R_I32(data, ref o),
				ObjectNameNum = R_I32(data, ref o),
				ArchetypeIndex = R_I32(data, ref o),
				ObjectFlags = R_U64(data, ref o),
				SerialSize = R_I32(data, ref o),
				SerialOffset = R_I32(data, ref o),
				ExportFlags = R_U32(data, ref o)
			};
			int num15 = R_I32(data, ref o);
			for (int num16 = 0; num16 < num15; num16++)
			{
				exportEntry.NetObjects.Add(R_I32(data, ref o));
			}
			exportEntry.PackageGuid = data[o..(o + 16)];
			o += 16;
			exportEntry.PackageFlags = R_U32(data, ref o);
			exportEntry.TableIndex = num14 + 1;
			ExportTable.Add(exportEntry);
		}
		foreach (ExportEntry item2 in ExportTable)
		{
			if (item2.SerialSize > 0 && item2.SerialOffset > 0)
			{
				item2.SerialData = data[item2.SerialOffset..(item2.SerialOffset + item2.SerialSize)];
			}
			else
			{
				item2.SerialData = Array.Empty<byte>();
			}
		}
		o = num8;
		DependsTable.Clear();
		for (int num17 = 0; num17 < num4; num17++)
		{
			DependsTable.Add(R_I32(data, ref o));
		}
	}

	internal void RebuildNameLookup()
	{
		_nameLookup.Clear();
		foreach (NameEntry item in NameTable)
		{
			_nameLookup[item.String] = item.TableIndex;
		}
	}

	public int? GetNameIndex(string name)
	{
		if (!_nameLookup.TryGetValue(name, out var value))
		{
			return null;
		}
		return value;
	}

	public int RequireName(string name)
	{
		if (_nameLookup.TryGetValue(name, out var value))
		{
			return value;
		}
		return AddName(name);
	}

	public int AddName(string name, ulong flags = 1970393556451328uL)
	{
		int count = NameTable.Count;
		NameTable.Add(new NameEntry
		{
			String = name,
			Flags = flags,
			TableIndex = count
		});
		_nameLookup[name] = count;
		return count;
	}

	public string ResolveName(int idx, int number = 0)
	{
		if (idx < 0 || idx >= NameTable.Count)
		{
			return $"<{idx}>";
		}
		string text = NameTable[idx].String;
		if (number <= 0)
		{
			return text;
		}
		return $"{text}_{number - 1}";
	}

	public int? FindImport(string className, string? objectName = null)
	{
		foreach (ImportEntry item in ImportTable)
		{
			if (ResolveName(item.ClassNameIdx, item.ClassNameNum).Equals(className, StringComparison.OrdinalIgnoreCase) && (objectName == null || ResolveName(item.ObjectNameIdx, item.ObjectNameNum).Equals(objectName, StringComparison.OrdinalIgnoreCase)))
			{
				return item.TableIndex;
			}
		}
		return null;
	}

	public ExportEntry? FindExportByName(string name)
	{
		foreach (ExportEntry item in ExportTable)
		{
			if (ResolveName(item.ObjectNameIdx, item.ObjectNameNum).Equals(name, StringComparison.OrdinalIgnoreCase))
			{
				return item;
			}
		}
		return null;
	}

	public string GetClassName(ExportEntry exp)
	{
		if (exp.ClassIndex == 0)
		{
			return "Class";
		}
		if (exp.ClassIndex < 0)
		{
			ImportEntry importEntry = ImportTable[-(exp.ClassIndex + 1)];
			return ResolveName(importEntry.ObjectNameIdx, importEntry.ObjectNameNum);
		}
		ExportEntry exportEntry = ExportTable[exp.ClassIndex - 1];
		return ResolveName(exportEntry.ObjectNameIdx, exportEntry.ObjectNameNum);
	}

	public string ResolveObjectRef(int reference)
	{
		if (reference == 0)
		{
			return "null";
		}
		if (reference < 0 && -(reference + 1) < ImportTable.Count)
		{
			ImportEntry importEntry = ImportTable[-(reference + 1)];
			return ResolveName(importEntry.ObjectNameIdx, importEntry.ObjectNameNum);
		}
		if (reference <= 0 || reference - 1 >= ExportTable.Count)
		{
			return $"<ref:{reference}>";
		}
		ExportEntry exportEntry = ExportTable[reference - 1];
		return ResolveName(exportEntry.ObjectNameIdx, exportEntry.ObjectNameNum);
	}

	public IEnumerable<(int Index, ExportEntry Export)> TextureExports()
	{
		for (int i = 0; i < ExportTable.Count; i++)
		{
			ExportEntry exportEntry = ExportTable[i];
			if (GetClassName(exportEntry).Equals("Texture2D", StringComparison.OrdinalIgnoreCase))
			{
				yield return (Index: i + 1, Export: exportEntry);
			}
		}
	}

	public int AddExport(int classRef, int superRef, int outerRef, string name, byte[] serialData, int archetypeRef = 0, ulong objectFlags = 4222141830529024uL, uint exportFlags = 1u)
	{
		int objectNameIdx = RequireName(name);
		int num = ExportTable.Count + 1;
		ExportTable.Add(new ExportEntry
		{
			ClassIndex = classRef,
			SuperIndex = superRef,
			OuterIndex = outerRef,
			ObjectNameIdx = objectNameIdx,
			ObjectNameNum = 0,
			ArchetypeIndex = archetypeRef,
			ObjectFlags = objectFlags,
			SerialSize = serialData.Length,
			SerialOffset = 0,
			ExportFlags = exportFlags,
			PackageGuid = new byte[16],
			PackageFlags = 0u,
			TableIndex = num,
			SerialData = serialData
		});
		DependsTable.Add(0);
		return num;
	}

	public void Save(string path)
	{
		File.WriteAllBytes(path, Rebuild());
	}

	public byte[] Rebuild()
	{
		if (Generations.Count > 0)
		{
			List<GenerationEntry> generations = Generations;
			generations[generations.Count - 1].ExportCount = ExportTable.Count;
			List<GenerationEntry> generations2 = Generations;
			generations2[generations2.Count - 1].NameCount = NameTable.Count;
		}
		using MemoryStream memoryStream = new MemoryStream();
		using BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
		binaryWriter.Write(2653586369u);
		binaryWriter.Write(Version);
		binaryWriter.Write(Licensee);
		long position = memoryStream.Position;
		binaryWriter.Write(0);
		WriteFString(binaryWriter, FolderName);
		binaryWriter.Write(PkgFlags);
		long position2 = memoryStream.Position;
		binaryWriter.Write(0);
		binaryWriter.Write(0);
		binaryWriter.Write(0);
		binaryWriter.Write(0);
		binaryWriter.Write(0);
		binaryWriter.Write(0);
		long position3 = memoryStream.Position;
		binaryWriter.Write(0);
		long position4 = memoryStream.Position;
		binaryWriter.Write(0);
		binaryWriter.Write(ImportGuidsCount);
		binaryWriter.Write(ExportGuidsCount);
		binaryWriter.Write(ThumbnailTableOffset);
		binaryWriter.Write(Guid);
		binaryWriter.Write(Generations.Count);
		foreach (GenerationEntry generation in Generations)
		{
			binaryWriter.Write(generation.ExportCount);
			binaryWriter.Write(generation.NameCount);
			binaryWriter.Write(generation.NetObjectCount);
		}
		binaryWriter.Write(EngineVersion);
		binaryWriter.Write(CookerVersion);
		binaryWriter.Write(CompressionFlags);
		binaryWriter.Write(_compressedChunkCount);
		binaryWriter.Write(_compressedChunksRaw);
		binaryWriter.Write(PackageSource);
		binaryWriter.Write(AdditionalPackages.Count);
		foreach (string additionalPackage in AdditionalPackages)
		{
			WriteFString(binaryWriter, additionalPackage);
		}
		binaryWriter.Write(TextureAllocations.Count);
		foreach (TextureAllocation textureAllocation in TextureAllocations)
		{
			binaryWriter.Write(textureAllocation.Width);
			binaryWriter.Write(textureAllocation.Height);
			binaryWriter.Write(textureAllocation.MipCount);
			binaryWriter.Write(textureAllocation.TextureFormat);
			binaryWriter.Write(textureAllocation.CreateFlags);
			binaryWriter.Write(textureAllocation.TextureIndices.Count);
			foreach (int textureIndex in textureAllocation.TextureIndices)
			{
				binaryWriter.Write(textureIndex);
			}
		}
		int val = (int)memoryStream.Position;
		foreach (NameEntry item3 in NameTable)
		{
			WriteFString(binaryWriter, item3.String);
			binaryWriter.Write(item3.Flags);
		}
		int val2 = (int)memoryStream.Position;
		foreach (ImportEntry item4 in ImportTable)
		{
			binaryWriter.Write(item4.ClassPackageIdx);
			binaryWriter.Write(item4.ClassPackageNum);
			binaryWriter.Write(item4.ClassNameIdx);
			binaryWriter.Write(item4.ClassNameNum);
			binaryWriter.Write(item4.OuterIndex);
			binaryWriter.Write(item4.ObjectNameIdx);
			binaryWriter.Write(item4.ObjectNameNum);
		}
		int num = (int)memoryStream.Position;
		int num2 = ExportTable.Sum((ExportEntry e) => 48 + e.NetObjects.Count * 4 + 16 + 4);
		int num3 = num + num2;
		int num4 = DependsTable.Count * 4;
		int num5 = num3 + num4;
		int num6 = num5;
		int[] array = new int[ExportTable.Count];
		for (int num7 = 0; num7 < ExportTable.Count; num7++)
		{
			array[num7] = num6;
			num6 += ExportTable[num7].SerialData.Length;
		}
		for (int num8 = 0; num8 < ExportTable.Count; num8++)
		{
			ExportEntry exportEntry = ExportTable[num8];
			binaryWriter.Write(exportEntry.ClassIndex);
			binaryWriter.Write(exportEntry.SuperIndex);
			binaryWriter.Write(exportEntry.OuterIndex);
			binaryWriter.Write(exportEntry.ObjectNameIdx);
			binaryWriter.Write(exportEntry.ObjectNameNum);
			binaryWriter.Write(exportEntry.ArchetypeIndex);
			binaryWriter.Write(exportEntry.ObjectFlags);
			binaryWriter.Write(exportEntry.SerialData.Length);
			binaryWriter.Write(array[num8]);
			binaryWriter.Write(exportEntry.ExportFlags);
			binaryWriter.Write(exportEntry.NetObjects.Count);
			foreach (int netObject in exportEntry.NetObjects)
			{
				binaryWriter.Write(netObject);
			}
			binaryWriter.Write(exportEntry.PackageGuid);
			binaryWriter.Write(exportEntry.PackageFlags);
		}
		foreach (int item5 in DependsTable)
		{
			binaryWriter.Write(item5);
		}
		for (int num9 = 0; num9 < ExportTable.Count; num9++)
		{
			ExportEntry exportEntry2 = ExportTable[num9];
			byte[] array2 = (byte[])exportEntry2.SerialData.Clone();
			foreach (var bulkFixup in exportEntry2.BulkFixups)
			{
				int item = bulkFixup.OffsetFieldPos;
				int item2 = bulkFixup.DataRelOffset;
				uint value = (uint)(array[num9] + item2);
				BitConverter.TryWriteBytes(array2.AsSpan(item), value);
			}
			binaryWriter.Write(array2);
		}
		binaryWriter.Flush();
		byte[] buf = memoryStream.GetBuffer();
		PatchI(position, num5);
		PatchI(position2, NameTable.Count);
		long num10 = position2 + 4;
		PatchI(num10, val);
		long num11 = num10 + 4;
		PatchI(num11, ExportTable.Count);
		long num12 = num11 + 4;
		PatchI(num12, num);
		long num13 = num12 + 4;
		PatchI(num13, ImportTable.Count);
		PatchI(num13 + 4, val2);
		PatchI(position3, num3);
		PatchI(position4, num5);
		return memoryStream.ToArray();
		void PatchI(long pos, int value2)
		{
			BitConverter.TryWriteBytes(buf.AsSpan((int)pos), value2);
		}
	}

	internal static int R_I32(byte[] d, ref int o)
	{
		int result = BitConverter.ToInt32(d, o);
		o += 4;
		return result;
	}

	internal static uint R_U32(byte[] d, ref int o)
	{
		uint result = BitConverter.ToUInt32(d, o);
		o += 4;
		return result;
	}

	internal static ushort R_U16(byte[] d, ref int o)
	{
		ushort result = BitConverter.ToUInt16(d, o);
		o += 2;
		return result;
	}

	internal static ulong R_U64(byte[] d, ref int o)
	{
		ulong result = BitConverter.ToUInt64(d, o);
		o += 8;
		return result;
	}

	internal static string ReadFString(byte[] data, ref int o)
	{
		int num = R_I32(data, ref o);
		if (num == 0)
		{
			return "";
		}
		if (num < 0)
		{
			int num2 = -num * 2;
			string result = Encoding.Unicode.GetString(data, o, num2).TrimEnd('\0');
			o += num2;
			return result;
		}
		string result2 = Encoding.Latin1.GetString(data, o, num).TrimEnd('\0');
		o += num;
		return result2;
	}

	private static void WriteFString(BinaryWriter w, string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			w.Write(0);
		}
		else if (s.Any((char c) => c > 'ÿ'))
		{
			w.Write(-(s.Length + 1));
			w.Write(Encoding.Unicode.GetBytes(s + "\0"));
		}
		else
		{
			byte[] bytes = Encoding.Latin1.GetBytes(s + "\0");
			w.Write(bytes.Length);
			w.Write(bytes);
		}
	}

	public Texture2DProperties? ParseTex2DProperties(int exportIndex)
	{
		return ParseTex2DProperties(exportIndex, out List<(int, int)> _);
	}

	public int DiscoverBulkFixups()
	{
		int tracked = 0;
		for (int i = 1; i <= ExportTable.Count; i++)
		{
			ExportEntry exportEntry = ExportTable[i - 1];
			if (exportEntry.BulkFixups != null && exportEntry.BulkFixups.Count > 0)
			{
				tracked += exportEntry.BulkFixups.Count;
				continue;
			}
			List<(int, int)> fixups = FindInlineBulkOffsets(exportEntry);
			if (fixups.Count > 0)
			{
				exportEntry.BulkFixups = fixups;
				tracked += fixups.Count;
			}
		}
		return tracked;
	}

	public List<(int, int)> FindInlineBulkOffsets(ExportEntry exportEntry)
	{
		var found = new List<(int, int)>();
		byte[] d = exportEntry.SerialData;
		if (d == null || d.Length < 16)
		{
			return found;
		}

		for (int p = 12; p + 4 <= d.Length; p += 4)
		{
			uint v = BitConverter.ToUInt32(d, p);
			if (v != (uint)(exportEntry.SerialOffset + p + 4))
			{
				continue;
			}

			int sizeOnDisk = BitConverter.ToInt32(d, p - 4);
			if (sizeOnDisk < 0 || p + 4 + (long)sizeOnDisk > d.Length)
			{
				continue;
			}

			uint flags = BitConverter.ToUInt32(d, p - 12);
			if ((flags & 1) != 0)
			{
				continue;
			}

			found.Add((p, p + 4));
		}
		return found;
	}

	public Texture2DProperties? ParseTex2DProperties(int exportIndex, out List<(int, int)> inlineFixups)
	{
		inlineFixups = new List<(int, int)>();
		byte[] serialData = ExportTable[exportIndex - 1].SerialData;
		if (serialData.Length < 20)
		{
			return null;
		}
		Texture2DProperties texture2DProperties = new Texture2DProperties();
		int o = 0;
		R_I32(serialData, ref o);
		while (o < serialData.Length - 8)
		{
			int idx = R_I32(serialData, ref o);
			int number = R_I32(serialData, ref o);
			string text = ResolveName(idx, number);
			if (text.Equals("none", StringComparison.OrdinalIgnoreCase))
			{
				break;
			}
			int idx2 = R_I32(serialData, ref o);
			int number2 = R_I32(serialData, ref o);
			string text2 = ResolveName(idx2, number2);
			int num = R_I32(serialData, ref o);
			int num2 = R_I32(serialData, ref o);
			string text3 = text.ToLowerInvariant();
			switch (text2.ToLowerInvariant())
			{
			case "intproperty":
			{
				int num3 = R_I32(serialData, ref o);
				switch (text3)
				{
				case "sizex":
					texture2DProperties.SizeX = num3;
					break;
				case "sizey":
					texture2DProperties.SizeY = num3;
					break;
				case "originalsizex":
					texture2DProperties.OriginalSizeX = num3;
					break;
				case "originalsizey":
					texture2DProperties.OriginalSizeY = num3;
					break;
				case "miptailbaseidx":
					texture2DProperties.MipTailBaseIdx = num3;
					break;
				case "firstresourcememmip":
					texture2DProperties.FirstResourceMemMip = num3;
					break;
				case "lodbias":
					texture2DProperties.LODBias = num3;
					break;
				}
				break;
			}
			case "floatproperty":
			{
				float value2 = BitConverter.ToSingle(serialData, o);
				o += 4;
				if (text3 == "unpackmin")
				{
					switch (num2)
					{
					case 0:
						texture2DProperties.UnpackMin0 = value2;
						break;
					case 1:
						texture2DProperties.UnpackMin1 = value2;
						break;
					case 2:
						texture2DProperties.UnpackMin2 = value2;
						break;
					case 3:
						texture2DProperties.UnpackMin3 = value2;
						break;
					}
				}
				else if (text3 == "unpackmax")
				{
					switch (num2)
					{
					case 0:
						texture2DProperties.UnpackMax0 = value2;
						break;
					case 1:
						texture2DProperties.UnpackMax1 = value2;
						break;
					case 2:
						texture2DProperties.UnpackMax2 = value2;
						break;
					case 3:
						texture2DProperties.UnpackMax3 = value2;
						break;
					}
				}
				break;
			}
			case "boolproperty":
			{
				bool value = serialData[o++] != 0;
				if (!(text3 == "srgb"))
				{
					if (text3 == "neverstream")
					{
						texture2DProperties.NeverStream = value;
					}
				}
				else
				{
					texture2DProperties.SRGB = value;
				}
				break;
			}
			case "byteproperty":
			{
				R_I32(serialData, ref o);
				R_I32(serialData, ref o);
				int idx4 = R_I32(serialData, ref o);
				int number4 = R_I32(serialData, ref o);
				string text4 = ResolveName(idx4, number4);
				switch (text3)
				{
				case "format":
				{
					string text5 = text4.ToLowerInvariant();
					Texture2DProperties texture2DProperties2 = texture2DProperties;
					texture2DProperties2.Format = text5 switch
					{
						"pf_dxt1" => 5,
						"pf_dxt3" => 6,
						"pf_dxt5" => 7,
						"pf_a8r8g8b8" => 2,
						"pf_g8" => 3,
						_ => texture2DProperties.Format,
					};
					break;
				}
				case "lodgroup":
					texture2DProperties.LODGroup = text4;
					break;
				case "compressionsettings":
					texture2DProperties.CompressionSettings = text4;
					break;
				case "mipgensettings":
					texture2DProperties.MipGenSettings = text4;
					break;
				}
				break;
			}
			case "nameproperty":
			{
				int idx3 = R_I32(serialData, ref o);
				int number3 = R_I32(serialData, ref o);
				string textureFileCacheName = ResolveName(idx3, number3);
				if (text3 == "texturefilecachename")
				{
					texture2DProperties.TextureFileCacheName = textureFileCacheName;
				}
				break;
			}
			default:
				o += num;
				break;
			}
		}
		if (o + 16 <= serialData.Length)
		{
			texture2DProperties.SourceArtBulkDataFlags = R_U32(serialData, ref o);
			int num4 = R_I32(serialData, ref o);
			texture2DProperties.SourceArtSavedSize = R_I32(serialData, ref o);
			R_U32(serialData, ref o);
			if (num4 > 0 && (texture2DProperties.SourceArtBulkDataFlags & 1) == 0)
			{
				o += num4;
			}
			if (o + 4 > serialData.Length)
			{
				return texture2DProperties;
			}
			int num5 = R_I32(serialData, ref o);
			for (int i = 0; i < num5; i++)
			{
				if (o + 16 > serialData.Length)
				{
					break;
				}
				uint num6 = R_U32(serialData, ref o);
				int num7 = R_I32(serialData, ref o);
				R_I32(serialData, ref o);
				int offsetFieldPos = o;
				R_U32(serialData, ref o);
				int dataRelOffset = o;
				if ((num6 & 1) == 0 && num7 > 0 && o + num7 <= serialData.Length)
				{

					inlineFixups.Add((offsetFieldPos, dataRelOffset));
					o += num7;
				}
				if (o + 8 > serialData.Length)
				{
					return texture2DProperties;
				}
				R_I32(serialData, ref o);
				R_I32(serialData, ref o);
			}
			if (o + 16 + 12 > serialData.Length)
			{
				return texture2DProperties;
			}
			texture2DProperties.TextureFileCacheGuid = new Guid(new ReadOnlySpan<byte>(serialData, o, 16));
			o += 16;
			R_I32(serialData, ref o);
			R_I32(serialData, ref o);
			R_I32(serialData, ref o);
			if (o + 16 <= serialData.Length)
			{
				texture2DProperties.CachedFlashBulkDataFlags = R_U32(serialData, ref o);
				R_I32(serialData, ref o);
				texture2DProperties.CachedFlashSavedSize = R_I32(serialData, ref o);
				R_U32(serialData, ref o);
			}
			return texture2DProperties;
		}
		return texture2DProperties;
	}

	public (byte[]? PixelData, int Width, int Height, string FormatName) ExtractPixelData(int exportIndex)
	{
		byte[] serialData = ExportTable[exportIndex - 1].SerialData;
		if (serialData.Length < 20)
		{
			return (PixelData: null, Width: 0, Height: 0, FormatName: "unknown");
		}
		int o = 0;
		R_I32(serialData, ref o);
		int item = 0;
		int item2 = 0;
		string item3 = "unknown";
		while (o < serialData.Length - 8)
		{
			int idx = R_I32(serialData, ref o);
			int number = R_I32(serialData, ref o);
			string text = ResolveName(idx, number);
			if (text.Equals("none", StringComparison.OrdinalIgnoreCase))
			{
				break;
			}
			int idx2 = R_I32(serialData, ref o);
			int number2 = R_I32(serialData, ref o);
			string text2 = ResolveName(idx2, number2);
			int num = R_I32(serialData, ref o);
			R_I32(serialData, ref o);
			string text3 = text.ToLowerInvariant();
			switch (text2.ToLowerInvariant())
			{
			case "intproperty":
			{
				int num2 = R_I32(serialData, ref o);
				if (text3 == "sizex")
				{
					item = num2;
				}
				else if (text3 == "sizey")
				{
					item2 = num2;
				}
				break;
			}
			case "boolproperty":
				o++;
				break;
			case "byteproperty":
			{
				o += 8;
				int idx3 = R_I32(serialData, ref o);
				int number3 = R_I32(serialData, ref o);
				if (text3 == "format")
				{
					item3 = ResolveName(idx3, number3);
				}
				break;
			}
			case "nameproperty":
				o += 8;
				break;
			case "floatproperty":
				o += 4;
				break;
			default:
				o += num;
				break;
			}
		}
		if (o + 16 > serialData.Length)
		{
			return (PixelData: null, Width: item, Height: item2, FormatName: item3);
		}
		uint num3 = R_U32(serialData, ref o);
		int num4 = R_I32(serialData, ref o);
		R_I32(serialData, ref o);
		R_U32(serialData, ref o);
		if (num4 > 0 && (num3 & 1) == 0)
		{
			o += num4;
		}
		if (o + 4 > serialData.Length)
		{
			return (PixelData: null, Width: item, Height: item2, FormatName: item3);
		}
		if (R_I32(serialData, ref o) < 1)
		{
			return (PixelData: null, Width: item, Height: item2, FormatName: item3);
		}
		if (o + 16 > serialData.Length)
		{
			return (PixelData: null, Width: item, Height: item2, FormatName: item3);
		}
		uint num5 = R_U32(serialData, ref o);
		int num6 = R_I32(serialData, ref o);
		R_I32(serialData, ref o);
		R_U32(serialData, ref o);
		if ((num5 & 1) != 0 || num6 <= 0)
		{
			return (PixelData: null, Width: item, Height: item2, FormatName: item3);
		}
		if (o + num6 > serialData.Length)
		{
			return (PixelData: null, Width: item, Height: item2, FormatName: item3);
		}
		byte[] array = new byte[num6];
		Array.Copy(serialData, o, array, 0, num6);
		return (PixelData: array, Width: item, Height: item2, FormatName: item3);
	}
}
