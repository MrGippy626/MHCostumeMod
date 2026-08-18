using System.Collections.Generic;

namespace MHTexLib;

public class TextureAllocation
{
	public int Width { get; set; }

	public int Height { get; set; }

	public int MipCount { get; set; }

	public uint TextureFormat { get; set; }

	public uint CreateFlags { get; set; }

	public List<int> TextureIndices { get; set; } = new List<int>();
}
