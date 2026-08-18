namespace MHTexLib;

public readonly struct TfcMipEntry(uint index, uint offset, uint size)
{
	public readonly uint Index = index;

	public readonly uint Offset = offset;

	public readonly uint Size = size;
}
