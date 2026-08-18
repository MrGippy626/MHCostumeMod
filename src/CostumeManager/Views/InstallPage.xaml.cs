using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using CostumeManager.Core;

namespace CostumeManager.Views
{

    public sealed partial class InstallPage : Page
    {
        DonorTables _tables;
        DonorGuess _donor;
        string _upkPath;
        bool _busy;

        public InstallPage()
        {
            InitializeComponent();

            NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

            LoadTables();
            RefreshIconSection();
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            RecomputeEnum();
            RefreshPreview();
        }

        void LoadTables()
        {
            try
            {

                string costumes = Path.Combine(AppContext.BaseDirectory, "Costumes.json");
                _tables = DonorTables.Load(costumes);

                CmbDonor.ItemsSource = _tables.AllDonorClasses.ToList();
                AppState.Log($"loaded {_tables.AssetIds.Count} costumes "
                           + $"({_tables.ProtoIds.Count} with prototype ids)");
                if (_tables.AssetIds.Count == 0)
                    AppState.Log("⚠ Costumes.json is empty or missing - donor detection cannot work.");

                else if (_tables.ProtoIds.Count == 0)
                    AppState.Log("⛔ Costumes.json has NO prototype ids - this is the old FLAT "
                               + "table, and no donor can resolve. Replace it with the MERGED "
                               + "file (pak\\buildcostumes.py) beside the exe.");
            }
            catch (Exception ex)
            {
                AppState.Log("could not load Costumes.json: " + ex.Message);
            }
        }

        async void BrowseUpk_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".upk");
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;

            Window w = App.MainWindowRef;
            if (w == null) return;
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker, WinRT.Interop.WindowNative.GetWindowHandle(w));

            StorageFile file = await picker.PickSingleFileAsync();
            if (file == null) return;

            _upkPath = file.Path;
            AppState.PickedUpk = _upkPath;

            AppState.ResetIconChoices();
            TxtUpk.Text = _upkPath;
            BtnClearUpk.IsEnabled = true;

            await DetectAsync();
        }

        void ClearUpk_Click(object sender, RoutedEventArgs e) => EndInstallSession();

        void EndInstallSession()
        {
            _upkPath = null;
            AppState.PickedUpk = null;
            AppState.ResetIconChoices();
            _donor = null;
            TxtUpk.Text = "";
            BtnClearUpk.IsEnabled = false;
            TxtDetected.Text = "Donor — pick a UPK to detect";
            TxtDonorIds.Text = "";
            RefreshPreview();
        }

        async Task DetectAsync()
        {
            BarDetect.Visibility = Visibility.Visible;
            TxtDetected.Text = "Detecting donor…";
            try
            {

                _donor = await Task.Run(() => DonorDetector.DetectAsync(_upkPath, _tables, AppState.Logger));

                if (_donor?.DonorClass == null)
                {
                    TxtDetected.Text = "Could not detect a donor — pick one below.";
                    TxtDonorIds.Text = "";
                }
                else
                {
                    CmbDonor.SelectedItem = _tables.AllDonorClasses
                        .FirstOrDefault(c => string.Equals(c, _donor.DonorClass, StringComparison.OrdinalIgnoreCase));
                    if (CmbDonor.SelectedItem == null) CmbDonor.Text = _donor.DonorClass;

                    TxtDetected.Text = _donor.Confident
                        ? $"Donor detected: {_donor.DonorClass}"
                        : $"Best guess: {_donor.DonorClass} — check this before installing.";
                    ShowDonorIds(_donor.DonorClass);
                }
            }
            catch (Exception ex)
            {
                TxtDetected.Text = "Detection failed: " + ex.Message;
                AppState.Log("detect failed: " + ex.Message);
            }
            finally
            {
                BarDetect.Visibility = Visibility.Collapsed;
                RecomputeEnum();
                RefreshPreview();
            }
        }

        void ShowDonorIds(string donorClass)
        {
            if (donorClass == null) { TxtDonorIds.Text = ""; return; }
            bool hasAsset = _tables.TryResolveAsset(donorClass, out ulong asset);
            bool hasProto = _tables.TryResolveProto(donorClass, out ulong proto);

            TxtDonorIds.Text = (hasAsset ? $"asset 0x{asset:X16}" : "NO AssetId")
                             + "   ·   "
                             + (hasProto ? $"proto 0x{proto:X16}" : "NO prototype id — cannot be a donor");
        }

        void Donor_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (CmbDonor.SelectedItem is string s) ShowDonorIds(s);
            RecomputeEnum();
            RefreshPreview();
        }

        void Inputs_Changed(object sender, TextChangedEventArgs e) => RefreshPreview();

        const string NoEnum = "—";

        void RecomputeEnum()
        {
            try
            {
                string dir = AppState.GameDir;
                if (string.IsNullOrWhiteSpace(dir)) { TxtEnum.Text = NoEnum; return; }
                var (_, _, bin) = GamePaths.Resolve(dir);
                string cfg = CostumeConfig.ExistingPath(bin);

                string purge = string.IsNullOrWhiteSpace(AppState.ServerDir)
                    ? null
                    : Path.Combine(AppState.ServerDir, "PendingCostumePurges.json");

                TxtEnum.Text = (purge == null
                    ? EnumAllocator.NextFree(cfg)
                    : EnumAllocator.NextFree(cfg, purge)).ToString();
            }
            catch (Exception ex)
            {
                TxtEnum.Text = NoEnum;
                AppState.Log("enum: " + ex.Message);
            }
        }

        long ReadStorePrice()
        {
            string s = (TxtStorePrice?.Text ?? "").Trim();
            if (s.Length == 0) return 0;
            if (!long.TryParse(s, out long v) || v < 0) return InstallPlan.DefaultStorePrice;
            return v;
        }

        CostumeInput CurrentInput()
        {
            string custom = Regex.Replace(TxtCustomName.Text ?? "", "[^A-Za-z0-9]", "");
            uint.TryParse(TxtEnum.Text, out uint en);

            return new CostumeInput
            {
                UpkPath = _upkPath,
                DonorClass = (CmbDonor.SelectedItem as string) ?? CmbDonor.Text,
                CustomName = custom,
                DisplayName = TxtDisplay.Text,
                Enum = en,
                StorePrice = ReadStorePrice(),
                GameDir = AppState.GameDir,
                ServerDir = AppState.ServerDir,

                Icons = IconPlanner.Resolve(installing: true).Sources,
            };
        }

        sealed class InstallIconRow
        {
            public IconPack.Core.IconRole Role { get; set; }
            public string RoleName { get; set; }
            public string FileName { get; set; }
            public string Tooltip { get; set; }
            public Microsoft.UI.Xaml.Media.ImageSource Image { get; set; }

            public string Placeholder { get; set; }
            public Visibility PlaceholderVisibility =>
                Image == null ? Visibility.Visible : Visibility.Collapsed;
        }

        async void PickIconForRole_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not IconPack.Core.IconRole role) return;
            if (string.IsNullOrWhiteSpace(_upkPath)) return;

            var picker = new FileOpenPicker();
            foreach (string ext in new[] { ".dds", ".png", ".jpg", ".jpeg", ".bmp" })
                picker.FileTypeFilter.Add(ext);
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;

            Window w = App.MainWindowRef;
            if (w == null) return;
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker, WinRT.Interop.WindowNative.GetWindowHandle(w));

            StorageFile file = await picker.PickSingleFileAsync();
            if (file == null) return;

            AppState.IconChoices[role] = file.Path;
            AppState.UseCustomIcons = true;

            AppState.Log($"icons: {role} <- {Path.GetFileName(file.Path)}");
            RefreshPreview();
        }

        void UseCustomIcons_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingIcons) return;
            AppState.UseCustomIcons = ChkUseCustomIcons.IsChecked == true;
            AppState.Log("icons: custom icons " + (AppState.UseCustomIcons ? "ON" : "OFF")
                       + " for this install");
            RefreshIconSection();
            RefreshPreview();
        }

        bool _loadingIcons;

        void RefreshIconSection()
        {
            bool installing = !string.IsNullOrWhiteSpace(_upkPath);

            _loadingIcons = true;
            ChkUseCustomIcons.IsChecked = AppState.UseCustomIcons;
            _loadingIcons = false;

            ChkUseCustomIcons.IsEnabled = installing;

            var rows = new List<InstallIconRow>();
            int chosen = 0;

            foreach (IconPack.Core.IconRoleInfo info in IconPack.Core.IconPackBuilder.Roles)
            {
                AppState.IconChoices.TryGetValue(info.Role, out string path);
                bool has = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
                if (has) chosen++;

                var row = new InstallIconRow
                {
                    Role = info.Role,
                    RoleName = info.Role.ToString(),
                    FileName = has ? Path.GetFileName(path) : "donor's icon",
                    Tooltip = info.Description + "  —  click to choose an image"
                            + (has ? "\n" + path : ""),
                };

                if (has)
                {
                    try
                    {
                        if (path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                        {
                            DdsPreview.Loaded loaded = DdsPreview.Load(path);
                            row.Image = loaded.Image;
                            if (loaded.Image == null && loaded.Error != null)
                                row.Placeholder = "could not\nread";
                        }
                        else
                        {
                            row.Image = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                                new Uri(Path.GetFullPath(path)));
                        }
                    }
                    catch { row.Placeholder = "could not\nread"; }
                }

                if (row.Image == null && row.Placeholder == null)
                    row.Placeholder = has ? "chosen\n(no preview)" : "click to\nchoose";

                rows.Add(row);
            }

            InstallIconList.ItemsSource = null;
            InstallIconList.ItemsSource = installing ? rows : new List<InstallIconRow>();

            if (!installing)
                TxtIconSummary.Text = "Pick a UPK first.";
            else if (!AppState.UseCustomIcons)
                TxtIconSummary.Text = "This costume will use the DONOR's icons. Tick the box, or "
                                    + "just click a tile to choose art — that ticks it for you.";
            else if (chosen == 0)
                TxtIconSummary.Text = "No art chosen yet — every role would still fall back to "
                                    + "the donor's icon. Click a tile to choose one.";
            else
                TxtIconSummary.Text = $"{chosen} of {IconPack.Core.IconPackBuilder.Roles.Count} role(s) have "
                                    + "custom art; the rest keep the donor's icon. "
                                    + "Click a tile to change it.";
        }

        void RefreshPreview()
        {
            if (_busy) return;
            RefreshIconSection();
            CostumeInput input = CurrentInput();

            bool ready = input.UpkPath != null
                      && !string.IsNullOrWhiteSpace(input.DonorClass)
                      && !string.IsNullOrWhiteSpace(input.CustomName)
                      && !string.IsNullOrWhiteSpace(input.GameDir)
                      && input.Enum > 0;

            if (!ready)
            {
                TxtPreview.Text = "Pick a UPK, a donor, a custom name — and set your game folder on Settings.";
                BarWarn.IsOpen = false;
                BtnInstall.IsEnabled = false;
                BtnVerify.IsEnabled = false;
                return;
            }

            try
            {
                InstallPlan plan = Installer.BuildPlan(input, _tables);
                TxtPreview.Text =
                      $"UPK      → {plan.OutputUpkName}\n"
                    + $"class    → {plan.ClassPath}\n"
                    + $"customId → 0x{plan.CustomId:X16}\n"
                    + $"enum     → {input.Enum}\n"
                    + $"donor    → {input.DonorClass}";

                BarWarn.IsOpen = plan.Warnings.Count > 0;
                BarWarn.Message = string.Join("\n", plan.Warnings);

                BtnVerify.IsEnabled = true;

                BtnInstall.IsEnabled = plan.Warnings.Count == 0;
            }
            catch (Exception ex)
            {
                TxtPreview.Text = "Cannot plan this install: " + ex.Message;
                BtnInstall.IsEnabled = false;
                BtnVerify.IsEnabled = false;
            }
        }

        async void Verify_Click(object sender, RoutedEventArgs e)
        {
            InstallPlan plan = Installer.BuildPlan(CurrentInput(), _tables);
            AppState.Log("");
            AppState.Log("── verify ──");
            foreach (string w in plan.Warnings) AppState.Log("  ⚠ " + w);
            if (plan.Warnings.Count == 0) AppState.Log("  no blocking problems found");

            await AppDialog.ShowAsync(
                plan.Warnings.Count == 0
                    ? "No blocking problems. See the log for detail."
                    : string.Join("\n", plan.Warnings),
                "Verify", DialogButtons.OK,
                plan.Warnings.Count == 0 ? DialogKind.Info : DialogKind.Warning);
        }

        async void Install_Click(object sender, RoutedEventArgs e)
        {
            CostumeInput input = CurrentInput();
            InstallPlan plan = Installer.BuildPlan(input, _tables);

            if (plan.Warnings.Count > 0)
            {
                AppState.Log("");
                AppState.Log("refusing to install — resolve these first:");
                foreach (string w in plan.Warnings) AppState.Log("  ⚠ " + w);
                await AppDialog.ShowAsync(string.Join("\n", plan.Warnings),
                    "Cannot install", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            string shown = string.IsNullOrWhiteSpace(input.DisplayName)
                ? input.CustomName
                : input.DisplayName;

            DialogResult ok = await AppDialog.ShowAsync(
                $"You're about to install costume \"{shown}\".\n\n"
                + $"This writes {plan.OutputUpkName} into the game's CookedPCConsole folder, "
                + "adds the costume to the config, and adds its texture rows to the manifest. "
                + "The manifest is backed up first, and no stock game file is overwritten.",
                "Install costume", DialogButtons.OKCancel, DialogKind.Info,
                primaryText: "Continue", closeText: "Stop");
            if (ok != DialogResult.OK) return;

            _busy = true;
            BtnInstall.IsEnabled = false;
            BtnVerify.IsEnabled = false;
            try
            {
                AppState.Log("");
                AppState.Log("══ INSTALL ══");
                InstallResult result = await Task.Run(
                    async () => await Installer.InstallAsync(plan, _upkPath, AppState.Logger));

                if (result.Ok)
                {
                    AppState.Log("");
                    AppState.Log("✓ installed:");
                    foreach (string s in result.Steps) AppState.Log("   " + s);
                    if (result.ManifestBackup != null)
                        AppState.Log("   manifest backup: " + Path.GetFileName(result.ManifestBackup));

                    await AppDialog.ShowAsync(
                        "Costume installed.\n\nRestart the client, and restart the server so it "
                        + "picks up the regenerated ServerCostumes.json.\n\n"
                        + "To change its icons later, use the Icons tab — it lists installed "
                        + "costumes.",
                        "Install", DialogButtons.OK);

                    EndInstallSession();
                    RecomputeEnum();
                }
                else
                {
                    AppState.Log("✗ install failed at: " + (result.FailedStep ?? "unknown"));
                    await AppDialog.ShowAsync(
                        "Install failed at: " + (result.FailedStep ?? "unknown") + "\n\nSee the log.",
                        "Install", DialogButtons.OK, DialogKind.Error);
                }
            }
            catch (Exception ex)
            {
                AppState.Log("✗ install threw: " + ex.Message);
                await AppDialog.ShowAsync("Install failed:\n\n" + ex.Message,
                    "Install", DialogButtons.OK, DialogKind.Error);
            }
            finally
            {
                _busy = false;
                RefreshPreview();
            }
        }
    }
}
