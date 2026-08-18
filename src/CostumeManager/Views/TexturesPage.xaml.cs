using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using CostumeManager.Core;

namespace CostumeManager.Views
{

    public sealed partial class TexturesPage : Page
    {
        public sealed class Row
        {
            public WriteableBitmap Image { get; set; }
            public string Target { get; set; }
            public string Kind { get; set; }
            public string File { get; set; }
            public string Detail { get; set; }
        }

        public TexturesPage()
        {
            InitializeComponent();
            Load();
        }

        static string KindLabel(DdsKind k) => k switch
        {
            DdsKind.Portrait  => "HUD portrait (cosmetic — safe to skip)",
            DdsKind.StoreIcon => "store icon (cosmetic — safe to skip)",
            DdsKind.Skin      => "SKIN texture — UPK is incomplete without it",
            _                 => k.ToString(),
        };

        void Load()
        {
            string upk = AppState.PickedUpk;
            if (string.IsNullOrEmpty(upk) || !File.Exists(upk))
            {
                TxtHeader.Text = "Pick a UPK on the Install page to inspect its DDS textures.";
                DdsList.ItemsSource = null;
                return;
            }

            ManifestInfo manifest = ManifestReader.FindAndRead(upk);
            if (manifest == null || manifest.Dds.Count == 0)
            {
                TxtHeader.Text = "This mod's manifest lists no DDS textures.";
                DdsList.ItemsSource = null;
                return;
            }

            TxtHeader.Text = $"{manifest.Dds.Count} texture replacement(s) in {manifest.Format}. "
                           + "DDS files are expected in the same folder as the UPK.";

            string modDir = Path.GetDirectoryName(upk);
            var rows = new List<Row>();

            foreach (DdsReplacement d in manifest.Dds)
            {
                var row = new Row
                {
                    Target = d.TextureName,
                    Kind = KindLabel(d.Kind),
                    File = d.DdsFileName ?? "(no file named — TFC-embedded)",
                };

                if (!string.IsNullOrEmpty(d.DdsFileName) && modDir != null)
                {
                    string path = Path.Combine(modDir, d.DdsFileName);
                    if (!File.Exists(path))
                    {

                        string hit = Directory.EnumerateFiles(modDir, d.DdsFileName,
                                                              SearchOption.AllDirectories).FirstOrDefault();
                        if (hit != null) path = hit;
                    }

                    if (File.Exists(path))
                    {
                        DdsPreview.Loaded loaded = DdsPreview.Load(path);
                        if (loaded.Error == null)
                        {
                            row.Image = loaded.Image;
                            row.Detail = $"{loaded.Width}x{loaded.Height}  ·  {loaded.Format}";
                        }
                        else row.Detail = "could not decode: " + loaded.Error;
                    }
                    else row.Detail = "DDS not found beside the UPK";
                }

                rows.Add(row);
            }

            DdsList.ItemsSource = rows;
        }
    }
}
