using System;
using System.Collections.Generic;

namespace MHTexLib;

public class ExportEntry
{
	public int ClassIndex { get; set; }

	public int SuperIndex { get; set; }

	public int OuterIndex { get; set; }

	public int ObjectNameIdx { get; set; }

	public int ObjectNameNum { get; set; }

	public int ArchetypeIndex { get; set; }

	public ulong ObjectFlags { get; set; }

	public int SerialSize { get; set; }

	public int SerialOffset { get; set; }

	public uint ExportFlags { get; set; }

	public List<int> NetObjects { get; set; } = new List<int>();

	public byte[] PackageGuid { get; set; } = new byte[16];

	public uint PackageFlags { get; set; }

	public int TableIndex { get; set; }

	public byte[] SerialData { get; set; } = Array.Empty<byte>();

	public List<(int OffsetFieldPos, int DataRelOffset)> BulkFixups { get; set; } = new List<(int, int)>();
}
