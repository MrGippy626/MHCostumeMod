namespace MHTexLib;

public static class PixelFormat
{
	public const int PF_DXT1 = 5;

	public const int PF_DXT3 = 6;

	public const int PF_DXT5 = 7;

	public const int PF_A8R8G8B8 = 2;

	public const int PF_G8 = 3;

	public static string ToName(int fmt)
	{
		return fmt switch
		{
			5 => "PF_DXT1",
			6 => "PF_DXT3",
			7 => "PF_DXT5",
			2 => "PF_A8R8G8B8",
			3 => "PF_G8",
			_ => $"PF_Unknown_{fmt}",
		};
	}
}
