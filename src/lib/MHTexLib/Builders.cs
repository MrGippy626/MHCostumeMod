using System;
using System.Collections.Generic;
using System.IO;

namespace MHTexLib;

public static class Builders
{
	public static (byte[] Data, List<(int, int)> Fixups) BuildTexture2D(UpkFile upk, DdsInfo dds, Texture2DProperties props)
	{
		string text = (string.IsNullOrEmpty(props.LODGroup) ? null : props.LODGroup);
		string text2 = (string.IsNullOrEmpty(props.TextureFileCacheName) ? null : props.TextureFileCacheName);
		string text3 = (string.IsNullOrEmpty(props.CompressionSettings) ? null : props.CompressionSettings);
		string text4 = (string.IsNullOrEmpty(props.MipGenSettings) ? null : props.MipGenSettings);
		string text5 = PixelFormat.ToName(dds.PixelFormat);
		upk.RequireName("EPixelFormat");
		upk.RequireName(text5);
		if (text != null)
		{
			upk.RequireName("TextureGroup");
			upk.RequireName(text);
		}
		if (text2 != null)
		{
			upk.RequireName(text2);
		}
		if (text3 != null)
		{
			upk.RequireName("TextureCompressionSettings");
			upk.RequireName(text3);
		}
		if (text4 != null)
		{
			upk.RequireName("TextureMipGenSettings");
			upk.RequireName(text4);
		}
		using MemoryStream memoryStream = new MemoryStream();
		using BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
		binaryWriter.Write(-1);
		PropertyBuilder propertyBuilder = new PropertyBuilder(upk);
		propertyBuilder.IntProp("SizeX", props.SizeX);
		propertyBuilder.IntProp("SizeY", props.SizeY);
		int? originalSizeX = props.OriginalSizeX;
		if (originalSizeX.HasValue)
		{
			int valueOrDefault = originalSizeX.GetValueOrDefault();
			propertyBuilder.IntProp("OriginalSizeX", valueOrDefault);
		}
		originalSizeX = props.OriginalSizeY;
		if (originalSizeX.HasValue)
		{
			int valueOrDefault2 = originalSizeX.GetValueOrDefault();
			propertyBuilder.IntProp("OriginalSizeY", valueOrDefault2);
		}
		propertyBuilder.ByteProp("Format", "EPixelFormat", text5);
		if (text2 != null)
		{
			propertyBuilder.NameProp("TextureFileCacheName", text2);
		}
		originalSizeX = props.MipTailBaseIdx;
		if (originalSizeX.HasValue)
		{
			int valueOrDefault3 = originalSizeX.GetValueOrDefault();
			propertyBuilder.IntProp("MipTailBaseIdx", valueOrDefault3);
		}
		originalSizeX = props.FirstResourceMemMip;
		if (originalSizeX.HasValue)
		{
			int valueOrDefault4 = originalSizeX.GetValueOrDefault();
			propertyBuilder.IntProp("FirstResourceMemMip", valueOrDefault4);
		}
		bool? sRGB = props.SRGB;
		if (sRGB.HasValue)
		{
			bool valueOrDefault5 = sRGB == true;
			propertyBuilder.BoolProp("SRGB", valueOrDefault5);
		}
		sRGB = props.NeverStream;
		if (sRGB.HasValue)
		{
			bool valueOrDefault6 = sRGB == true;
			propertyBuilder.BoolProp("NeverStream", valueOrDefault6);
		}
		float? unpackMin = props.UnpackMin0;
		if (unpackMin.HasValue)
		{
			float valueOrDefault7 = unpackMin.GetValueOrDefault();
			propertyBuilder.FloatProp("UnpackMin", valueOrDefault7);
		}
		unpackMin = props.UnpackMin1;
		if (unpackMin.HasValue)
		{
			float valueOrDefault8 = unpackMin.GetValueOrDefault();
			propertyBuilder.FloatProp("UnpackMin", valueOrDefault8, 1);
		}
		unpackMin = props.UnpackMin2;
		if (unpackMin.HasValue)
		{
			float valueOrDefault9 = unpackMin.GetValueOrDefault();
			propertyBuilder.FloatProp("UnpackMin", valueOrDefault9, 2);
		}
		unpackMin = props.UnpackMin3;
		if (unpackMin.HasValue)
		{
			float valueOrDefault10 = unpackMin.GetValueOrDefault();
			propertyBuilder.FloatProp("UnpackMin", valueOrDefault10, 3);
		}
		unpackMin = props.UnpackMax0;
		if (unpackMin.HasValue)
		{
			float valueOrDefault11 = unpackMin.GetValueOrDefault();
			propertyBuilder.FloatProp("UnpackMax", valueOrDefault11);
		}
		unpackMin = props.UnpackMax1;
		if (unpackMin.HasValue)
		{
			float valueOrDefault12 = unpackMin.GetValueOrDefault();
			propertyBuilder.FloatProp("UnpackMax", valueOrDefault12, 1);
		}
		unpackMin = props.UnpackMax2;
		if (unpackMin.HasValue)
		{
			float valueOrDefault13 = unpackMin.GetValueOrDefault();
			propertyBuilder.FloatProp("UnpackMax", valueOrDefault13, 2);
		}
		unpackMin = props.UnpackMax3;
		if (unpackMin.HasValue)
		{
			float valueOrDefault14 = unpackMin.GetValueOrDefault();
			propertyBuilder.FloatProp("UnpackMax", valueOrDefault14, 3);
		}
		if (text3 != null)
		{
			propertyBuilder.ByteProp("CompressionSettings", "TextureCompressionSettings", text3);
		}
		if (text != null)
		{
			propertyBuilder.ByteProp("LODGroup", "TextureGroup", text);
		}
		originalSizeX = props.LODBias;
		if (originalSizeX.HasValue)
		{
			int valueOrDefault15 = originalSizeX.GetValueOrDefault();
			propertyBuilder.IntProp("LODBias", valueOrDefault15);
		}
		if (text4 != null)
		{
			propertyBuilder.ByteProp("MipGenSettings", "TextureMipGenSettings", text4);
		}
		propertyBuilder.None();
		binaryWriter.Write(propertyBuilder.ToArray());
		binaryWriter.Write(props.SourceArtBulkDataFlags);
		binaryWriter.Write(0);
		binaryWriter.Write(props.SourceArtSavedSize);
		binaryWriter.Write(uint.MaxValue);
		binaryWriter.Write(1);
		binaryWriter.Write(0u);
		binaryWriter.Write(dds.PixelData.Length);
		binaryWriter.Write(dds.PixelData.Length);
		int item = (int)memoryStream.Position;
		binaryWriter.Write(0u);
		int item2 = (int)memoryStream.Position;
		binaryWriter.Write(dds.PixelData);
		binaryWriter.Write(dds.Width);
		binaryWriter.Write(dds.Height);
		binaryWriter.Write(new byte[16]);
		binaryWriter.Write(0);
		binaryWriter.Write(0);
		binaryWriter.Write(0);
		binaryWriter.Write(props.CachedFlashBulkDataFlags);
		binaryWriter.Write(0);
		binaryWriter.Write(props.CachedFlashSavedSize);
		binaryWriter.Write(uint.MaxValue);
		binaryWriter.Write(0);
		binaryWriter.Flush();
		List<(int, int)> item3 = new List<(int, int)> { (item, item2) };
		return (Data: memoryStream.ToArray(), Fixups: item3);
	}

	public static int InjectTexture2D(UpkFile upk, string name, DdsInfo dds, Texture2DProperties props, int outerRef = 0, ulong objectFlags = 4222141830529024uL, uint exportFlags = 1u)
	{
		int classRef = upk.FindImport("Class", "Texture2D") ?? throw new InvalidOperationException("No Texture2D class import in package");
		(byte[] Data, List<(int, int)> Fixups) tuple = BuildTexture2D(upk, dds, props);
		byte[] item = tuple.Data;
		List<(int, int)> item2 = tuple.Fixups;
		int num = upk.AddExport(classRef, 0, outerRef, name, item, 0, objectFlags, exportFlags);
		upk.ExportTable[num - 1].BulkFixups = item2;
		return num;
	}

	public static void ReplaceTexture2D(UpkFile upk, int exportIndex, DdsInfo dds, Texture2DProperties props)
	{
		(byte[] Data, List<(int, int)> Fixups) tuple = BuildTexture2D(upk, dds, props);
		byte[] item = tuple.Data;
		List<(int, int)> item2 = tuple.Fixups;
		ExportEntry exportEntry = upk.ExportTable[exportIndex - 1];
		exportEntry.SerialData = item;
		exportEntry.BulkFixups = item2;
		foreach (TextureAllocation textureAllocation in upk.TextureAllocations)
		{
			textureAllocation.TextureIndices.Remove(exportIndex);
		}
		upk.TextureAllocations.RemoveAll((TextureAllocation ta) => ta.TextureIndices.Count == 0);
	}
}
