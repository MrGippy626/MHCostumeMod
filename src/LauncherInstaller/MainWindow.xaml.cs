using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

using CostumeManager;
using CostumeManager.Core;

namespace LauncherInstaller
{

    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Title = "Marvel Heroes Costume Installer";
            CostumeManager.WindowIcon.Apply(this);

            _dq = DispatcherQueue;

            if (Content is FrameworkElement fe) fe.Loaded += OnLoaded;
        }

        static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "gamedir.txt");

        string GameDir => TxtGameDir.Text?.Trim();

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            string dir = null;
            try { if (File.Exists(SettingsPath)) dir = File.ReadAllText(SettingsPath).Trim(); }
            catch { }

            if (string.IsNullOrWhiteSpace(dir) || !GamePaths.LooksLikeGameFolder(dir))
            {
                string found = GamePaths.AutoDetect();
                if (found != null)
                {
                    dir = found;
                    Log("found Marvel Heroes at " + found);
                }
            }

            if (!string.IsNullOrWhiteSpace(dir))
            {
                TxtGameDir.Text = dir;
                ApplyGameDir();
            }
            else
            {
                Log("Could not find Marvel Heroes automatically.");
                Log("Click Browse and pick your game folder to get started.");
                ApplyGameDir();
            }
        }

        async Task<bool> RequireGameDir()
        {
            if (GamePaths.LooksLikeGameFolder(GameDir)) return true;
            await AppDialog.ShowAsync("Pick your Marvel Heroes folder first.",
                "Game folder", DialogButtons.OK, DialogKind.Warning);
            return false;
        }

        void ApplyGameDir()
        {
            bool ok = GamePaths.LooksLikeGameFolder(GameDir);

            if (ok)
            {
                var (cooked, _, bin) = GamePaths.Resolve(GameDir);
                TxtGameDirState.Text = "Looks right."
                    + Environment.NewLine + "Costumes go to: " + cooked
                    + Environment.NewLine + "Settings read from: " + bin;
                try { File.WriteAllText(SettingsPath, GameDir); } catch { }
            }
            else
            {
                TxtGameDirState.Text = string.IsNullOrWhiteSpace(GameDir)
                    ? "Not set."
                    : "No CookedPCConsole folder under here - this does not look like a Marvel "
                    + "Heroes install.";
            }

            LoadInstalled();
            LoadFxPacks();
            CaptureManifestBaseline();
        }

        void CaptureManifestBaseline()
        {
            if (!GamePaths.LooksLikeGameFolder(GameDir)) return;

            try
            {
                var (_, manifest, _) = GamePaths.Resolve(GameDir);
                if (!File.Exists(manifest)) return;
                if (File.Exists(manifest + ".bak")) return;

                bool hasCostumes = CostumeLibrary.ListInstalled(GameDir).Count > 0;
                if (hasCostumes) return;

                ManifestDoctor.EnsureBaseline(manifest, false, Log);
            }
            catch {  }
        }

        async void BrowseGameDir_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();

            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

            StorageFolder folder = await picker.PickSingleFolderAsync();
            if (folder == null) return;

            TxtGameDir.Text = folder.Path;
            ApplyGameDir();
        }

        void Refresh_Click(object sender, RoutedEventArgs e) => ApplyGameDir();

        void LoadInstalled()
        {
            ListCostumes.ItemsSource = null;

            if (!GamePaths.LooksLikeGameFolder(GameDir))
            {
                TxtCostumeHeader.Text = "COSTUMES - no game folder set";
                return;
            }

            string jsonPath = CostumeLibrary.CustomCostumesJson(GameDir);

            List<InstalledCostume> installed;
            try { installed = CostumeLibrary.ListInstalled(GameDir); }
            catch (Exception ex)
            {
                TxtCostumeHeader.Text = "COSTUMES - could not read the list";
                Log("could not read the costume list: " + ex.Message);
                return;
            }

            ListCostumes.ItemsSource = installed
                .OrderBy(c => c.Enum)
                .Select(c => new CostumeRow
                {
                    Enum = c.Enum,
                    DisplayName = string.IsNullOrWhiteSpace(c.DisplayName) ? "(unnamed)" : c.DisplayName,
                    SubText = Describe(c),
                })
                .ToList();

            TxtCostumeHeader.Text = File.Exists(jsonPath) || CostumeConfig.Exists(jsonPath)
                ? $"COSTUMES - {installed.Count} in {jsonPath}"
                : $"COSTUMES - nothing installed in {Path.GetDirectoryName(jsonPath)} yet";
        }

        static string Describe(InstalledCostume c)
        {
            var bits = new List<string>();
            bits.Add(string.IsNullOrWhiteSpace(c.IconPackage) ? "donor icons" : "custom icons");
            if (!string.IsNullOrWhiteSpace(c.Upk)) bits.Add(c.Upk);
            if (!c.InLedger) bits.Add("no install record - cannot be cleanly removed");
            return string.Join("  -  ", bits);
        }

        async void Import_Click(object sender, RoutedEventArgs e)
        {
            if (!await RequireGameDir()) return;

            StorageFile file = await PickOpen("Costume", CostumePackFile.Extension);
            if (file == null) return;

            CostumePackInfo info = CostumePackFile.Read(file.Path, out string err);
            if (info == null)
            {
                await AppDialog.ShowAsync("This file cannot be read as a costume:"
                    + Environment.NewLine + Environment.NewLine + err,
                    "Install costume", DialogButtons.OK, DialogKind.Error);
                return;
            }

            string warn = "";
            InstalledCostume occupant = CostumeLibrary.ListInstalled(GameDir)
                .FirstOrDefault(c => c.Enum == info.Enum);

            if (occupant != null && occupant.CustomId != info.CustomId)
                warn = Environment.NewLine + Environment.NewLine
                     + $"⚠ \"{occupant.DisplayName}\" currently uses slot {info.Enum} and will "
                     + "be removed." + Environment.NewLine
                     + "The server has reassigned that slot, so it no longer works anyway.";
            else if (occupant != null)
                warn = Environment.NewLine + Environment.NewLine
                     + "This costume is already installed and will be reinstalled.";

            if (!string.IsNullOrWhiteSpace(info.FxPackToken)
                && FxPackRegistry.Find(info.FxPackToken, FxRegistryPath()) == null)
                warn += Environment.NewLine + Environment.NewLine
                     + $"This costume uses effects pack \"{info.FxPackToken}\", which is not "
                     + "installed. It will work with the game's normal effects; install the pack "
                     + "later and its custom effects turn on automatically.";

            string summary =
                info.DisplayName + Environment.NewLine + Environment.NewLine
                + $"slot:   {info.Enum}" + Environment.NewLine
                + $"icons:  {(string.IsNullOrWhiteSpace(info.IconUpk) ? "the donor's" : "custom")}"
                + warn + Environment.NewLine + Environment.NewLine + "Install it?";

            if (await AppDialog.ShowAsync(summary, "Install costume",
                    DialogButtons.OKCancel, DialogKind.Info,
                    primaryText: "Install") != DialogResult.OK)
                return;

            Log("");
            Log($"-- installing {info.DisplayName} --");

            string path = file.Path;

            PlayerResult res;
            try
            {
                string root = GameDir;
                res = await Task.Run(() => PlayerInstall.Import(root, path, Log));
            }
            catch (Exception ex)
            {
                Log("INSTALL FAILED: " + ex.GetType().Name + ": " + ex.Message);
                await AppDialog.ShowAsync(
                    "The install stopped with an error:" + Environment.NewLine + Environment.NewLine
                    + ex.Message + Environment.NewLine + Environment.NewLine
                    + "If the game is running, close it and try again - it holds its own files open.",
                    "Install costume", DialogButtons.OK, DialogKind.Error);
                return;
            }

            if (!res.Ok)
            {
                await AppDialog.ShowAsync("Install failed (" + res.FailedStep + ")."
                    + Environment.NewLine + Environment.NewLine + "See the log for details.",
                    "Install costume", DialogButtons.OK, DialogKind.Error);
                return;
            }

            LoadInstalled();

            string msg = $"\"{info.DisplayName}\" is installed."
                       + Environment.NewLine + Environment.NewLine + "Restart the game to see it.";
            if (res.Replaced != null)
                msg += Environment.NewLine + Environment.NewLine + "Replaced: " + res.Replaced;
            if (res.Warnings.Count > 0)
                msg += Environment.NewLine + Environment.NewLine + "Warnings:" + Environment.NewLine
                     + "  - " + string.Join(Environment.NewLine + "  - ", res.Warnings);

            await AppDialog.ShowAsync(msg, "Install costume", DialogButtons.OK,
                res.Warnings.Count > 0 ? DialogKind.Warning : DialogKind.Info);
        }

        async void ImportBundle_Click(object sender, RoutedEventArgs e)
        {
            if (!await RequireGameDir()) return;

            StorageFile file = await PickOpen("Bundle", BulkPack.Extension);
            if (file == null) return;

            BulkPack.Info manifest = BulkPack.ReadManifest(file.Path);

            int nCost = manifest?.Members.Count(m => m.Kind == BulkPack.KindCostume) ?? 0;
            int nFx = manifest?.Members.Count(m => m.Kind == BulkPack.KindFx) ?? 0;

            string what = manifest == null
                ? Path.GetFileName(file.Path)
                : $"{nCost} costume(s) and {nFx} effects pack(s)"
                  + Environment.NewLine + Environment.NewLine
                  + "The effects packs are installed FIRST, so each costume gets its custom "
                  + "effects straight away."
                  + Environment.NewLine + Environment.NewLine
                  + Path.GetFileName(file.Path);

            if (await AppDialog.ShowAsync(
                    what + Environment.NewLine + Environment.NewLine + "Install everything in it?",
                    "Install a bundle", DialogButtons.OKCancel, DialogKind.Info,
                    primaryText: "Install") != DialogResult.OK)
                return;

            await RunBulkImport(file.Path, "bundle " + Path.GetFileName(file.Path));
        }

        async void ImportFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!await RequireGameDir()) return;

            var picker = new FolderPicker();

            picker.FileTypeFilter.Add("*");
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

            StorageFolder folder = await picker.PickSingleFolderAsync();
            if (folder == null) return;

            await RunBulkImport(folder.Path, "folder " + folder.Path);
        }

        async Task RunBulkImport(string source, string label)
        {
            Log("");
            Log($"-- installing {label} --");

            string root = GameDir;

            BulkPack.BulkResult res;
            ShowProgress(true);
            try
            {
                res = await Task.Run(() => BulkPack.ImportBundle(root, source, Log, ReportProgress));
            }
            catch (Exception ex)
            {
                ShowProgress(false);
                Log("INSTALL FAILED: " + ex.GetType().Name + ": " + ex.Message);
                await AppDialog.ShowAsync(
                    "The install stopped with an error:" + Environment.NewLine + Environment.NewLine
                    + ex.Message + Environment.NewLine + Environment.NewLine
                    + "If the game is running, close it and try again - it holds its own files open.",
                    "Install", DialogButtons.OK, DialogKind.Error);
                return;
            }

            ShowProgress(false);

            LoadInstalled();
            LoadFxPacks();

            if (!res.Ok)
            {
                await AppDialog.ShowAsync(
                    "Nothing was installed (" + (res.FailedStep ?? "unknown") + ")."
                    + Environment.NewLine + Environment.NewLine + "See the log for details.",
                    "Install", DialogButtons.OK, DialogKind.Error);
                return;
            }

            string failed = res.Failed == 0
                ? ""
                : Environment.NewLine + Environment.NewLine
                  + $"Could not install ({res.Failed}):" + Environment.NewLine
                  + string.Join(Environment.NewLine, res.Members.Where(m => !m.Ok)
                        .Select(m => "  - " + m.Name + " (" + m.FailedStep + ")"));

            await AppDialog.ShowAsync(
                $"Installed {res.OkFxPacks} effects pack(s) and {res.OkCostumes} costume(s)."
                + Environment.NewLine + Environment.NewLine + "Restart the game to see them."
                + failed,
                "Install", DialogButtons.OK,
                res.Failed == 0 ? DialogKind.Info : DialogKind.Warning);
        }

        async void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (ListCostumes.SelectedItem is not CostumeRow row)
            {
                await AppDialog.ShowAsync("Pick a costume in the list first.",
                    "Remove costume", DialogButtons.OK, DialogKind.Info);
                return;
            }
            if (!await RequireGameDir()) return;

            if (await AppDialog.ShowAsync(
                    $"Remove \"{row.DisplayName}\" from your game?"
                    + Environment.NewLine + Environment.NewLine
                    + "This only changes your own copy of the game - it does not affect the "
                    + "server or other players. You can install it again later.",
                    "Remove costume", DialogButtons.OKCancel, DialogKind.Warning,
                    primaryText: "Remove", closeText: "Keep") != DialogResult.OK)
                return;

            Log("");
            Log($"-- removing {row.DisplayName} --");

            uint enumId = row.Enum;
            string root = GameDir;
            PlayerResult res = await Task.Run(() => PlayerInstall.Uninstall(root, enumId, Log));

            if (!res.Ok)
            {
                await AppDialog.ShowAsync("Remove failed (" + res.FailedStep + ")."
                    + Environment.NewLine + Environment.NewLine + "See the log for details.",
                    "Remove costume", DialogButtons.OK, DialogKind.Error);
                return;
            }

            LoadInstalled();
            Log("removed. Restart the game to see the change.");
        }

        string FxRegistryPath()
        {
            if (!GamePaths.LooksLikeGameFolder(GameDir)) return null;
            return FxPackInstall.RegistryPathFor(GameDir);
        }

        void LoadFxPacks()
        {
            ListFxPacks.ItemsSource = null;

            string registry = FxRegistryPath();
            if (registry == null) { TxtFxHeader.Text = "EFFECTS PACKS"; return; }

            List<FxPack> packs;
            try { packs = FxPackRegistry.Read(registry); }
            catch (Exception ex)
            {
                TxtFxHeader.Text = "EFFECTS PACKS - could not read the list";
                Log("could not read the effects pack list: " + ex.Message);
                return;
            }

            List<string> waiting = WaitingTokens();

            ListFxPacks.ItemsSource = packs
                .OrderBy(p => p.DisplayName ?? p.Token, StringComparer.OrdinalIgnoreCase)
                .Select(p => new FxPackRow
                {
                    Token = p.Token,
                    DisplayName = p.DisplayName ?? p.Token,
                    SubText = $"{p.Effects?.Count ?? 0} effect package(s)"
                            + (string.IsNullOrWhiteSpace(p.Hero) ? "" : "  -  " + p.Hero),
                })
                .ToList();

            TxtFxHeader.Text = $"EFFECTS PACKS - {packs.Count} installed";

            if (waiting.Count > 0)
            {
                TxtFxWaiting.Text =
                    (waiting.Count == 1
                        ? $"A costume needs the effects pack \"{waiting[0]}\", which is not installed."
                        : $"Costumes need {waiting.Count} effects packs that are not installed: "
                          + string.Join(", ", waiting) + ".")
                    + " They work now and use the game's normal effects. Install the pack and their "
                    + "custom effects turn on - nothing has been lost.";
                FxWaitingBanner.Visibility = Visibility.Visible;
            }
            else
            {
                FxWaitingBanner.Visibility = Visibility.Collapsed;
            }
        }

        List<string> WaitingTokens()
        {
            var tokens = new List<string>();
            if (!GamePaths.LooksLikeGameFolder(GameDir)) return tokens;

            try
            {
                var (_, _, bin) = GamePaths.Resolve(GameDir);
                string cfg = CostumeConfig.ExistingPath(bin);
                if (cfg == null) return tokens;

                var root = System.Text.Json.Nodes.JsonNode.Parse(CostumeConfig.ReadAllText(cfg))
                           as System.Text.Json.Nodes.JsonObject;
                if (root == null) return tokens;

                foreach (string key in new[] { "costumes", "disabled" })
                {
                    if (root[key] is not System.Text.Json.Nodes.JsonArray arr) continue;
                    foreach (System.Text.Json.Nodes.JsonNode n in arr)
                    {
                        if (n is not System.Text.Json.Nodes.JsonObject entry) continue;
                        string t = FxPackInstall.PendingToken(entry);
                        if (!string.IsNullOrWhiteSpace(t)
                            && !tokens.Contains(t, StringComparer.OrdinalIgnoreCase))
                            tokens.Add(t);
                    }
                }
            }
            catch { }

            return tokens;
        }

        async void ImportFx_Click(object sender, RoutedEventArgs e)
        {
            if (!await RequireGameDir()) return;

            StorageFile file = await PickOpen("Effects pack", FxPackFile.Extension);
            if (file == null) return;

            FxPackFile.Info info = FxPackFile.Read(file.Path, out string err);
            if (info == null)
            {
                await AppDialog.ShowAsync("This file cannot be read as an effects pack:"
                    + Environment.NewLine + Environment.NewLine + err,
                    "Install effects pack", DialogButtons.OK, DialogKind.Error);
                return;
            }

            List<string> waiting = WaitingTokens();
            bool wanted = waiting.Contains(info.Token, StringComparer.OrdinalIgnoreCase);

            string summary =
                (info.DisplayName ?? info.Token) + Environment.NewLine + Environment.NewLine
                + $"effects:  {info.Effects.Count} package(s)" + Environment.NewLine
                + $"hero:     {(string.IsNullOrWhiteSpace(info.Hero) ? "(not recorded)" : info.Hero)}"
                + Environment.NewLine + Environment.NewLine
                + (wanted
                    ? "One or more of your installed costumes is waiting for this pack - their "
                    + "custom effects will turn on."
                    : "No installed costume uses this pack yet. That is fine - install one and it "
                    + "will pick this up automatically.")
                + Environment.NewLine + Environment.NewLine + "Install it?";

            if (await AppDialog.ShowAsync(summary, "Install effects pack",
                    DialogButtons.OKCancel, DialogKind.Info,
                    primaryText: "Install") != DialogResult.OK)
                return;

            Log("");
            Log($"-- installing effects pack {info.DisplayName ?? info.Token} --");

            string path = file.Path;
            string root = GameDir;
            FxPackInstall.Result res = await Task.Run(() => FxPackInstall.Import(root, path, Log));

            if (!res.Ok)
            {
                await AppDialog.ShowAsync("Install failed (" + res.FailedStep + ")."
                    + Environment.NewLine + Environment.NewLine + "See the log for details.",
                    "Install effects pack", DialogButtons.OK, DialogKind.Error);
                return;
            }

            LoadInstalled();
            LoadFxPacks();

            string msg = $"\"{info.DisplayName ?? info.Token}\" is installed."
                + Environment.NewLine + Environment.NewLine
                + (res.CostumesRestored > 0
                    ? $"{res.CostumesRestored} costume(s) now have their custom effects."
                    : "No installed costume uses it yet.")

                + Environment.NewLine + Environment.NewLine
                + "Restart the game (fully close it) to see the change.";

            if (res.Warnings.Count > 0)
                msg += Environment.NewLine + Environment.NewLine + "Warnings:" + Environment.NewLine
                     + "  - " + string.Join(Environment.NewLine + "  - ", res.Warnings);

            await AppDialog.ShowAsync(msg, "Install effects pack", DialogButtons.OK,
                res.Warnings.Count > 0 ? DialogKind.Warning : DialogKind.Info);
        }

        async void RemoveFx_Click(object sender, RoutedEventArgs e)
        {
            if (ListFxPacks.SelectedItem is not FxPackRow row)
            {
                await AppDialog.ShowAsync("Pick an effects pack in the list first.",
                    "Remove effects pack", DialogButtons.OK, DialogKind.Info);
                return;
            }
            if (!await RequireGameDir()) return;

            if (await AppDialog.ShowAsync(
                    $"Remove the effects pack \"{row.DisplayName}\"?"
                    + Environment.NewLine + Environment.NewLine
                    + "Any costume using it keeps working and falls back to the game's normal "
                    + "effects. Nothing is lost - install the pack again and they turn back on.",
                    "Remove effects pack", DialogButtons.OKCancel, DialogKind.Warning,
                    primaryText: "Remove", closeText: "Keep") != DialogResult.OK)
                return;

            Log("");
            Log($"-- removing effects pack {row.DisplayName} --");

            string token = row.Token;
            string root = GameDir;
            FxPackInstall.Result res = await Task.Run(() => FxPackInstall.Remove(root, token, Log));

            if (!res.Ok)
            {
                await AppDialog.ShowAsync("Remove failed (" + res.FailedStep + ")."
                    + Environment.NewLine + Environment.NewLine + "See the log for details.",
                    "Remove effects pack", DialogButtons.OK, DialogKind.Error);
                return;
            }

            LoadInstalled();
            LoadFxPacks();
            Log("removed. Restart the game to see the change.");
        }

        async void Check_Click(object sender, RoutedEventArgs e)
        {
            if (!await RequireGameDir()) return;

            List<string> problems;
            try { problems = PlayerInstall.Verify(GameDir); }
            catch (Exception ex)
            {
                await AppDialog.ShowAsync("The check itself failed: " + ex.Message,
                    "Check install", DialogButtons.OK, DialogKind.Error);
                return;
            }

            List<string> waiting = WaitingTokens();

            int count = CostumeLibrary.ListInstalled(GameDir).Count;
            int packs = FxPackRegistry.Read(FxPackInstall.RegistryPathFor(GameDir)).Count;
            string jsonPath = CostumeLibrary.CustomCostumesJson(GameDir);

            Log("");
            Log($"check: {problems.Count} problem(s) across {count} costume(s) and "
              + $"{packs} effects pack(s), against {jsonPath}");
            foreach (string p in problems) Log("  - " + p);

            string scope = $"{count} costume(s) and {packs} effects pack(s)";

            string msg = (problems.Count == 0
                    ? $"No problems found.{Environment.NewLine}{Environment.NewLine}"
                      + $"Checked {scope}: every costume UPK, icon package and effect package "
                      + "they load, plus the texture manifest."
                    : $"{problems.Count} problem(s) found across {scope}:"
                      + Environment.NewLine + "  - "
                      + string.Join(Environment.NewLine + "  - ", problems))
                + Environment.NewLine + Environment.NewLine + "Read from:" + Environment.NewLine + jsonPath;

            if (waiting.Count > 0)
                msg += Environment.NewLine + Environment.NewLine
                     + "Waiting for effects pack(s): " + string.Join(", ", waiting)
                     + Environment.NewLine
                     + "Those costumes work and use the game's normal effects until you install them.";

            await AppDialog.ShowAsync(msg, "Check install", DialogButtons.OK,
                problems.Count == 0 ? DialogKind.Info : DialogKind.Warning);
        }

        async Task<StorageFile> PickOpen(string label, string extension)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(extension);
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            return await picker.PickSingleFileAsync();
        }

        DispatcherQueue _dq;

        readonly System.Text.StringBuilder _logBuf = new System.Text.StringBuilder();
        readonly object _logLock = new object();
        bool _logFlushPending;

        void Log(string line)
        {
            DispatcherQueue dq = _dq;
            if (dq == null) return;

            bool schedule;
            lock (_logLock)
            {

                bool blank = string.IsNullOrWhiteSpace(line);
                if (blank && EndsWithBlankLine()) return;

                _logBuf.Append(line).Append(Environment.NewLine);
                schedule = !_logFlushPending;
                _logFlushPending = true;
            }

            if (!schedule) return;

            if (dq.HasThreadAccess) Flush();
            else dq.TryEnqueue(Flush);

            void Flush()
            {
                if (TxtLog == null) return;

                string all;
                lock (_logLock)
                {
                    all = _logBuf.ToString();
                    _logFlushPending = false;
                }

                TxtLog.Text = all;

                if (LogScroll != null && LogScroll.IsLoaded)
                    LogScroll.ChangeView(null, LogScroll.ScrollableHeight, null);
            }
        }

        bool EndsWithBlankLine()
        {
            int n = _logBuf.Length, nl = Environment.NewLine.Length;
            if (n == 0) return true;
            if (n < nl * 2) return false;

            for (int k = 0; k < nl; k++)
                if (_logBuf[n - nl + k] != Environment.NewLine[k]
                    || _logBuf[n - nl * 2 + k] != Environment.NewLine[k])
                    return false;
            return true;
        }

        void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            lock (_logLock) _logBuf.Clear();
            TxtLog.Text = "";
        }

        void ShowProgress(bool on)
        {
            if (ImportProgress == null) return;

            ImportProgress.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) return;

            BarProgress.Value = 0;
            TxtProgress.Text = "Preparing...";
            TxtProgressCount.Text = "";
        }

        void ReportProgress(BulkPack.BulkProgress p)
        {
            DispatcherQueue dq = _dq;
            if (dq == null || p == null) return;

            if (dq.HasThreadAccess) Apply();
            else dq.TryEnqueue(Apply);

            void Apply()
            {
                if (BarProgress == null) return;

                BarProgress.Maximum = Math.Max(1, p.Total);
                BarProgress.Value = p.Done;

                TxtProgressCount.Text = p.Total > 0
                    ? $"{Math.Min(p.Done + (p.Current == null ? 0 : 1), p.Total)} of {p.Total}"
                    : "";

                TxtProgress.Text = p.Current == null
                    ? "Finishing..."
                    : (p.Kind == BulkPack.KindFx ? "Effects pack: " : "Costume: ") + p.Current;
            }
        }
    }

    public sealed class CostumeRow
    {
        public uint Enum { get; set; }
        public string DisplayName { get; set; }
        public string SubText { get; set; }
        public override string ToString() => DisplayName;
    }

    public sealed class FxPackRow
    {
        public string Token { get; set; }
        public string DisplayName { get; set; }
        public string SubText { get; set; }
        public override string ToString() => DisplayName;
    }
}
