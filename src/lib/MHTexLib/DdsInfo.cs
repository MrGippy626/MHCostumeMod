using System;

namespace MHTexLib;

public class DdsInfo
{
	public int Width { get; set; }

	public int Height { get; set; }

	public int PixelFormat { get; set; }

	public string FourCC { get; set; } = "";

	public byte[] PixelData { get; set; } = Array.Empty<byte>();
}
