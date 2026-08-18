using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using CostumeManager.Core;

namespace CostumeManager.Views
{

    public sealed partial class ManagePage : Page
    {
        public sealed class Row
        {
            public string DisplayName { get; set; }
            public uint Enum { get; set; }
            public string EnumText => $"enum {Enum}";

            public Microsoft.UI.Xaml.Media.ImageSource Thumb => CostumeThumb.For(Enum);

            public string EnumAndDonor => $"enum {Enum}  ·  {DonorClass}";
            public string DonorClass { get; set; }
            public string Upk { get; set; }
            public string SubText { get; set; }
            public ulong CustomId { get; set; }
            public bool Enabled { get; set; } = true;
            public string SearchBlob { get; set; }

            public Visibility PickerVisibility { get; set; } = Visibility.Collapsed;

            public bool Selected { get; set; }
            public Visibility ExportPickVisibility { get; set; } = Visibility.Collapsed;

            public Visibility ActionsVisibility { get; set; } = Visibility.Visible;

            public double RowOpacity => Enabled ? 1.0 : 0.45;
        }

        List<Row> _all = new List<Row>();
        bool _picking;
        bool _exporting;

        public ManagePage()
        {
            InitializeComponent();
            Load();
        }

        void Load()
        {
            try
            {
                string dir = AppState.GameDir;
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                {
                    InstalledList.ItemsSource = null;
                    TxtEmpty.Visibility = Visibility.Visible;
                    TxtEmpty.Text = "Pick your game folder on the Settings tab first.";
                    return;
                }

                _all = Installer.ListInstalled(dir).OrderBy(c => c.Enum).Select(c => new Row
                {
                    DisplayName = c.DisplayName,
                    Enum = c.Enum,
                    DonorClass = c.DonorClass,
                    Upk = c.Upk,
                    CustomId = c.CustomId,
                    Enabled = c.Enabled,
                    PickerVisibility = _picking ? Visibility.Visible : Visibility.Collapsed,
                    ExportPickVisibility = _exporting ? Visibility.Visible : Visibility.Collapsed,
                    ActionsVisibility = (_picking || _exporting) ? Visibility.Collapsed : Visibility.Visible,
                    SubText = (c.Enabled ? "" : "HIDDEN — installed but not loaded by the game  ·  ")
                            + (c.InLedger
                                ? $"installed {FormatUtc(c.InstalledUtc)}  ·  in ledger"
                                : "installed by an older manager (no ledger record — uninstall will reconstruct from JSON)"),

                    SearchBlob = string.Join(" ", c.DisplayName, c.DonorClass, c.Upk,
                        FxCompatibility.HeroOfCostume(c.DonorClass), "enum" + c.Enum).ToLowerInvariant(),
                }).ToList();

                ApplySearch();

                int hidden = _all.Count(r => !r.Enabled);
                TxtSub.Text = _exporting
                    ? "Tick the costumes to put in the bundle, then press Export selected. Each "
                      + "costume's FX pack is included automatically — a shared pack only once."
                    : _picking
                    ? "Tick the costumes the game should load, then press Apply. Unticking HIDES a "
                      + "costume — it stays installed and nothing is deleted."
                    : $"{_all.Count} costume(s) installed"
                      + (hidden > 0 ? $", {hidden} hidden" : "")
                      + ". Uninstall removes the UPK, the JSON entry, and the TFC manifest rows (with a backup first).";
            }
            catch (Exception ex)
            {
                InstalledList.ItemsSource = null;
                TxtEmpty.Visibility = Visibility.Visible;
                TxtEmpty.Text = "Could not read the installed list: " + ex.Message;
                AppState.Log("manage: " + ex.Message);
            }
        }

        static string FormatUtc(string iso)
        {
            return DateTime.TryParse(iso, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out DateTime dt)
                ? dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : (iso ?? "");
        }

        void Search_Changed(object sender, TextChangedEventArgs e) => ApplySearch();

        void ApplySearch()
        {
            string q = (TxtSearch.Text ?? "").Trim().ToLowerInvariant();
            List<Row> shown = q.Length == 0
                ? _all
                : _all.Where(r => q.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                   .All(t => r.SearchBlob != null && r.SearchBlob.Contains(t))).ToList();

            InstalledList.ItemsSource = shown;
            TxtEmpty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (shown.Count == 0 && _all.Count > 0) TxtEmpty.Text = "Nothing matches that search.";
            else if (_all.Count == 0) TxtEmpty.Text = "No installed costumes found.";
        }

        void Refresh_Click(object sender, RoutedEventArgs e) => Load();

        void ToggleEdit_Click(object sender, RoutedEventArgs e) => SetPicking(true);
        void Cancel_Click(object sender, RoutedEventArgs e) => SetPicking(false);

        void SetPicking(bool on)
        {
            _picking = on;
            BtnToggleEdit.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            BtnApply.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            BtnCancel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            BtnRefresh.IsEnabled = !on;

            BtnBulkExport.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            Load();
        }

        async void Apply_Click(object sender, RoutedEventArgs e)
        {

            var wanted = _all.Where(r => r.CustomId != 0)
                             .ToDictionary(r => r.CustomId, r => r.Enabled);

            var (_, _, bin) = GamePaths.Resolve(AppState.GameDir);
            string jsonPath = CostumeConfig.ExistingPath(bin);

            int moved;
            try
            {
                if (CostumeConfig.Exists(jsonPath)) Backup.Timestamped(jsonPath);
                moved = CostumeLibrary.SetEnabled(jsonPath, wanted);
            }
            catch (Exception ex)
            {
                await AppDialog.ShowAsync("Could not update the costume config:\n\n" + ex.Message,
                    "Show / hide", DialogButtons.OK, DialogKind.Error);
                return;
            }

            SetPicking(false);
            if (moved == 0) { AppState.Log("show/hide: nothing changed"); return; }

            int hidden = wanted.Count(kv => !kv.Value);
            AppState.Log($"show/hide: {moved} costume(s) changed — {hidden} now hidden");
            await AppDialog.ShowAsync($"{moved} costume(s) changed.\n\n"
                + "Hidden costumes are still installed — nothing was deleted. "
                + "Restart the client to see the change.",
                "Show / hide", DialogButtons.OK);
        }

        async void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not uint enumId) return;

            string dir = AppState.GameDir;
            if (string.IsNullOrWhiteSpace(dir))
            {
                await AppDialog.ShowAsync("Pick your game folder on the Settings tab first.",
                    "Uninstall", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            InstalledCostume match = Installer.ListInstalled(dir).FirstOrDefault(c => c.Enum == enumId);
            string label = match?.DisplayName ?? ("enum " + enumId);

            DialogResult confirm = await AppDialog.ShowAsync(
                $"Uninstall \"{label}\"?\n\n"
                + "This deletes its UPK, its entry in CustomCostumes.json, and the TFC manifest "
                + "rows it added. The manifest is backed up first.\n\n"
                + "Any tokens bought for it are purged by the server at its next startup.",
                "Uninstall", DialogButtons.OKCancel, DialogKind.Warning);
            if (confirm != DialogResult.OK) return;

            AppState.Log("");
            AppState.Log($"── uninstalling {label} ──");

            var res = await Task.Run(() => Installer.Uninstall(dir, enumId, AppState.Logger, AppState.ServerDir));

            if (res.Ok)
            {
                AppState.Log("✓ uninstalled: " + string.Join(", ", res.Steps));
                Load();
                await AppDialog.ShowAsync(
                    $"\"{label}\" was uninstalled.\n\nRestart the client, and restart the server so "
                    + "its token purge runs.",
                    "Uninstall", DialogButtons.OK);
            }
            else
            {

                AppState.Log("✗ uninstall failed: " + (res.Error ?? "unknown error"));
                await AppDialog.ShowAsync(res.Error ?? "Uninstall failed. See the log.",
                    "Uninstall", DialogButtons.OK, DialogKind.Error);
            }
        }

        async void Export_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not uint enumId) return;

            string dir = AppState.GameDir;
            if (string.IsNullOrWhiteSpace(dir))
            {
                await AppDialog.ShowAsync("Pick your game folder on the Settings tab first.",
                    "Export", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            InstalledCostume rec = Installer.ListInstalled(dir).FirstOrDefault(c => c.Enum == enumId);
            string suggested = (rec?.Name ?? "costume");

            var picker = new FileSavePicker();
            picker.SuggestedFileName = suggested;
            picker.FileTypeChoices.Add("Costume pack", new List<string> { Installer.PackExtension });
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            Window w = App.MainWindowRef;
            if (w == null) return;
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker, WinRT.Interop.WindowNative.GetWindowHandle(w));

            StorageFile file = await picker.PickSaveFileAsync();
            if (file == null) return;

            AppState.Log("");
            AppState.Log($"── exporting {rec?.DisplayName ?? enumId.ToString()} ──");

            Installer.PackResult res = await Task.Run(
                () => Installer.ExportPack(dir, enumId, file.Path, AppState.Logger));

            if (!res.Ok)
            {
                await AppDialog.ShowAsync(
                    "Export failed at step: " + res.FailedStep + "\n\nSee the log for details.",
                    "Export", DialogButtons.OK, DialogKind.Error);
                return;
            }

            await AppDialog.ShowAsync(
                $"Exported to:\n{file.Path}\n\nThe pack carries the costume's own enum. Importing it "
                + "elsewhere keeps that enum when it is free.",
                "Export", DialogButtons.OK);
        }

        void BulkExport_Click(object sender, RoutedEventArgs e) => SetExporting(true);

        void ExportCancel_Click(object sender, RoutedEventArgs e)
        {
            foreach (Row r in _all) r.Selected = false;
            SetExporting(false);
        }

        void SetExporting(bool on)
        {
            _exporting = on;
            BtnBulkExport.Visibility     = on ? Visibility.Collapsed : Visibility.Visible;
            BtnExportSelected.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            BtnExportAll.Visibility      = on ? Visibility.Visible : Visibility.Collapsed;
            BtnExportCancel.Visibility   = on ? Visibility.Visible : Visibility.Collapsed;
            BtnToggleEdit.Visibility     = on ? Visibility.Collapsed : Visibility.Visible;
            BtnRefresh.IsEnabled = !on;
            Load();
        }

        async void ExportSelected_Click(object sender, RoutedEventArgs e)
        {

            List<uint> picked = _all.Where(r => r.Selected).Select(r => r.Enum).ToList();

            if (picked.Count == 0)
            {
                await AppDialog.ShowAsync("Tick at least one costume first.",
                    "Export bundle", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            await RunBulkExport(picked, $"{picked.Count} selected costume(s)");
        }

        async void ExportAll_Click(object sender, RoutedEventArgs e)
            => await RunBulkExport(_all.Select(r => r.Enum).ToList(), $"all {_all.Count} costume(s)");

        async Task RunBulkExport(List<uint> enums, string label)
        {
            string dir = AppState.GameDir;
            if (string.IsNullOrWhiteSpace(dir))
            {
                await AppDialog.ShowAsync("Pick your game folder on the Settings tab first.",
                    "Export bundle", DialogButtons.OK, DialogKind.Warning);
                return;
            }
            if (enums.Count == 0)
            {
                await AppDialog.ShowAsync("There is nothing installed to export.",
                    "Export bundle", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            if (await AppDialog.ShowAsync(
                    $"This exports {label} — and, automatically, every FX pack they use.\n\n"
                    + "A pack shared by several costumes is included once, so the bundle is one "
                    + "self-sufficient file: whoever imports it gets the costumes AND their "
                    + "effects, in the right order.\n\n"
                    + "Effect packs are large (roughly 14 MB each), so the file can run to "
                    + "tens of megabytes.",
                    "Export bundle", DialogButtons.OKCancel, DialogKind.Info,
                    primaryText: "Choose a file…") != DialogResult.OK)
                return;

            var picker = new FileSavePicker();
            picker.SuggestedFileName = "costumes_and_fx";
            picker.FileTypeChoices.Add("Costume bundle", new List<string> { BulkPack.Extension });
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            Window w = App.MainWindowRef;
            if (w == null) return;
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker, WinRT.Interop.WindowNative.GetWindowHandle(w));

            StorageFile file = await picker.PickSaveFileAsync();
            if (file == null) return;

            string outPath = file.Path;

            AppState.Log("");
            AppState.Log($"── exporting a bundle of {label} ──");
            AppState.Log($"  destination: {outPath}");

            SetExporting(false);

            BulkPack.BulkResult res = await Task.Run(
                () => BulkPack.ExportBundle(dir, enums, outPath, AppState.Logger,
                                            registryPath: null,
                                            title: $"{enums.Count} costume(s) and their FX packs"));

            if (!res.Ok)
            {
                await AppDialog.ShowAsync(
                    "The bundle was not written (step: " + (res.FailedStep ?? "unknown")
                    + ").\n\nSee the log for details.",
                    "Export bundle", DialogButtons.OK, DialogKind.Error);
                return;
            }

            string failed = res.Failed == 0
                ? ""
                : "\n\nNOT included (" + res.Failed + "):\n"
                  + string.Join("\n", res.Members.Where(m => !m.Ok)
                                         .Select(m => "  • " + m.Name + " — " + m.FailedStep));

            await AppDialog.ShowAsync(
                "Finished exporting!\n\n"
                + $"{res.OkCostumes} costume(s) and {res.OkFxPacks} FX pack(s) are in the bundle.\n\n"
                + "Players import this one file — the FX packs go in first automatically."
                + failed,
                "Export bundle", DialogButtons.OK,
                res.Failed == 0 ? DialogKind.Info : DialogKind.Warning);
        }
    }
}
