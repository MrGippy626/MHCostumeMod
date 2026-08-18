
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IconPack.Core
{

    public enum IconRole
    {

        Token,

        Portrait,

        Store,
    }

    public sealed class IconRoleInfo
    {
        public IconRole Role { get; }
        public string DonorTexture { get; }
        public string ShortName { get; }

        public IReadOnlyList<uint> ProtoOffsets { get; }

        public string Description { get; }

        internal IconRoleInfo(IconRole role, string donorTexture, string shortName,
                              uint[] protoOffsets, string description)
        {
            Role = role;
            DonorTexture = donorTexture;
            ShortName = shortName;
            ProtoOffsets = protoOffsets;
            Description = description;
        }
    }

    public sealed class IconSource
    {
        public IconRole Role { get; set; }
        public string ImagePath { get; set; }
    }

    public sealed class IconPatch
    {
        public uint Offset { get; set; }
        public ulong AssetId { get; set; }
        public string Path { get; set; }

        public string OffsetHex => "0x" + Offset.ToString("X3");
        public string AssetHex => "0x" + AssetId.ToString("X16");
    }

    public sealed class IconPackResult
    {
        public bool Ok { get; set; }
        public string FailedStep { get; set; }
        public List<string> Steps { get; } = new List<string>();

        public List<IconPatch> Patches { get; } = new List<IconPatch>();

        public string PackageFName { get; set; }

        public string OutputUpkPath { get; set; }

        public List<string> RemainingTfcTextures { get; } = new List<string>();
    }

    public static partial class IconPackBuilder
    {

        public const string DefaultDonorUpk = "ICO__SilverSurferIcons_SF.upk";
        public const string DonorPackageName = "silversurfericons";
        public const string DonorSelfName = "ico__silversurfericons_sf";

        public static readonly IReadOnlyList<IconRoleInfo> Roles = new[]
        {

            new IconRoleInfo(IconRole.Token,    "costumesilversurfer_classic",  "tok",
                             new uint[] { 0x400, 0x078 },
                             "Inventory tile, costume slot, party frame, Social"),
            new IconRoleInfo(IconRole.Portrait, "herohor_silversurfer_classic", "por",
                             new uint[] { 0x3F0 },
                             "Character sheet portrait"),
            new IconRoleInfo(IconRole.Store,    "store_silversurfer_classic",   "str",
                             new uint[] { 0x408 },
                             "MTX store card"),
        };

        public static IconRoleInfo RoleInfo(IconRole role) => Roles.First(r => r.Role == role);

        public static string PackageNameForEnum(uint costumeEnum) => "CI" + (costumeEnum - 100000u);

        public static string UpkFileNameForEnum(uint costumeEnum) =>
            "ICO__" + PackageNameForEnum(costumeEnum) + "_SF.upk";

        public static string PackageFNameForEnum(uint costumeEnum) =>
            ("ico__" + PackageNameForEnum(costumeEnum) + "_sf").ToLowerInvariant();

        public static string IconPathFor(uint costumeEnum, IconRole role) =>
            PackageNameForEnum(costumeEnum) + "." + RoleInfo(role).ShortName.ToUpperInvariant();

        public static ulong AssetIdFor(uint costumeEnum, IconRole role)
        {
            ulong payload = ((ulong)(costumeEnum - 100000u) << 8) | (ulong)(int)role;
            return (0xC057UL << 48) | (payload << 16) | 0x1599UL;
        }
    }
}
