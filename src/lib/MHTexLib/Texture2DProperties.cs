using System;

namespace MHTexLib;

public sealed class Texture2DProperties
{
	public int SizeX { get; set; }

	public int SizeY { get; set; }

	public int Format { get; set; }

	public string FormatName => PixelFormat.ToName(Format);

	public int? OriginalSizeX { get; set; }

	public int? OriginalSizeY { get; set; }

	public string? LODGroup { get; set; }

	public int? LODBias { get; set; }

	public string? MipGenSettings { get; set; }

	public bool? NeverStream { get; set; }

	public string? TextureFileCacheName { get; set; }

	public int? MipTailBaseIdx { get; set; }

	public int? FirstResourceMemMip { get; set; }

	public bool? SRGB { get; set; }

	public float? UnpackMin0 { get; set; }

	public float? UnpackMin1 { get; set; }

	public float? UnpackMin2 { get; set; }

	public float? UnpackMin3 { get; set; }

	public float? UnpackMax0 { get; set; }

	public float? UnpackMax1 { get; set; }

	public float? UnpackMax2 { get; set; }

	public float? UnpackMax3 { get; set; }

	public string? CompressionSettings { get; set; }

	public uint SourceArtBulkDataFlags { get; set; }

	public int SourceArtSavedSize { get; set; }

	public uint CachedFlashBulkDataFlags { get; set; } = 33u;

	public int CachedFlashSavedSize { get; set; } = -1;

	public Guid TextureFileCacheGuid { get; set; }
}
