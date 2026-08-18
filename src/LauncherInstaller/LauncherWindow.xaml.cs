using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;

using CostumeManager;
using CostumeManager.Core;

namespace LauncherInstaller
{

    public sealed partial class LauncherWindow : Window
    {
        LauncherCore.Settings _settings;
        MainWindow _tools;

        const int WindowWidth = 960;
        const int WindowHeight = 540;

        public LauncherWindow()
        {
            InitializeComponent();
            Title = "Marvel Heroes Launcher";

            ResizeWindow();
            CostumeManager.WindowIcon.Apply(this);

            if (Content is FrameworkElement fe) fe.Loaded += OnLoaded;
        }

        void ResizeWindow()
        {
            try
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                Microsoft.UI.WindowId id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                Microsoft.UI.Windowing.AppWindow aw =
                    Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);

                aw?.Resize(new Windows.Graphics.SizeInt32(WindowWidth, WindowHeight));
                _appWindow = aw;
            }
            catch
            {

            }
        }

        Microsoft.UI.Windowing.AppWindow _appWindow;

        void RestoreWindowPosition()
        {
            if (_appWindow == null) return;

            _appWindow.Changed += (s, args) =>
            {
                if (!args.DidPositionChange || _settings == null) return;

                _settings.WindowX = _appWindow.Position.X;
                _settings.WindowY = _appWindow.Position.Y;
                try { LauncherCore.SaveSettings(_settings); } catch { }
            };

            if (_settings.WindowX < 0 && _settings.WindowY < 0) return;

            try
            {
                var area = Microsoft.UI.Windowing.DisplayArea.GetFromPoint(
                    new Windows.Graphics.PointInt32(_settings.WindowX, _settings.WindowY),
                    Microsoft.UI.Windowing.DisplayAreaFallback.None);

                if (area == null) return;

                _appWindow.Move(new Windows.Graphics.PointInt32(_settings.WindowX, _settings.WindowY));
            }
            catch { }

        }

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            _settings = LauncherCore.LoadSettings();
            RestoreWindowPosition();
            LoadBackground();
            RefreshState();
        }

        void LoadBackground()
        {
            string[] candidates =
            {
                Path.Combine(AppContext.BaseDirectory, "background.png"),
                Path.Combine(AppContext.BaseDirectory, "background.jpg"),
                Path.Combine(AppContext.BaseDirectory, "Assets", "background.jpg"),
            };

            foreach (string path in candidates)
            {
                try
                {
                    if (!File.Exists(path)) continue;

                    ImgBackground.Source = new BitmapImage(new Uri(path));
                    ImgBackground.Visibility = Visibility.Visible;
                    return;
                }
                catch
                {

                }
            }
        }

        void RefreshState()
        {
            RebuildServerPicker();
            ApplySkipModLook();

            TxtServerName.Text = string.IsNullOrWhiteSpace(_settings.SelectedName)
                ? (_settings.SiteConfigUrl ?? "No server")
                : _settings.SelectedName;

            List<string> problems = LauncherCore.Validate(_settings);

            if (problems.Count > 0)
            {
                TxtProblems.Text = string.Join(Environment.NewLine, problems);
                ProblemBanner.Visibility = Visibility.Visible;
                BtnPlay.IsEnabled = false;
            }
            else
            {
                ProblemBanner.Visibility = Visibility.Collapsed;
                BtnPlay.IsEnabled = true;
            }

            _ = CheckServerAsync();
        }

        void ApplySkipModLook()
        {

            _rebuilding = true;
            try { SwUseMod.IsOn = !_settings.SkipMod; }
            finally { _rebuilding = false; }
        }

        void UseMod_Toggled(object sender, RoutedEventArgs e)
        {
            if (_rebuilding || _settings == null) return;

            _settings.SkipMod = !SwUseMod.IsOn;
            try { LauncherCore.SaveSettings(_settings); } catch { }

            RefreshState();
        }

        bool _rebuilding;

        void RebuildServerPicker()
        {

            _rebuilding = true;
            try
            {
                CmbServer.Items.Clear();
                foreach (LauncherCore.ServerEntry e in _settings.Servers)
                    CmbServer.Items.Add(e.Name ?? e.SiteConfigUrl ?? "(unnamed)");

                if (CmbServer.Items.Count > 0)
                    CmbServer.SelectedIndex =
                        Math.Min(Math.Max(_settings.SelectedServer, 0), CmbServer.Items.Count - 1);
            }
            finally { _rebuilding = false; }
        }

        void Server_Changed(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
        {
            if (_rebuilding || CmbServer.SelectedIndex < 0) return;

            _settings.SelectedServer = CmbServer.SelectedIndex;
            try { LauncherCore.SaveSettings(_settings); } catch { }

            TxtServerName.Text = _settings.SelectedName ?? _settings.SiteConfigUrl ?? "No server";
            _ = CheckServerAsync();
        }

        void Recheck_Click(object sender, RoutedEventArgs e) => _ = CheckServerAsync();

        int _checkGeneration;

        async Task CheckServerAsync()
        {

            int mine = ++_checkGeneration;

            SetLamp(LauncherCore.ServerState.Checking);
            TxtStatus.Text = "Checking…";

            string url = _settings.SiteConfigUrl;
            LauncherCore.ServerStatus status = await LauncherCore.CheckServerAsync(url);

            if (mine != _checkGeneration) return;

            SetLamp(status.State);
            TxtStatus.Text = status.State switch
            {
                LauncherCore.ServerState.Online  => "Online",
                LauncherCore.ServerState.Offline => "Offline - " + status.Detail,
                LauncherCore.ServerState.Unknown => status.Detail ?? "No server selected",
                _ => "",
            };
        }

        void SetLamp(LauncherCore.ServerState state)
        {
            (Windows.UI.Color bright, Windows.UI.Color deep) = state switch
            {
                LauncherCore.ServerState.Online =>
                    (Windows.UI.Color.FromArgb(255, 0x6E, 0xE7, 0x8B),
                     Windows.UI.Color.FromArgb(255, 0x15, 0x80, 0x3D)),
                LauncherCore.ServerState.Offline =>
                    (Windows.UI.Color.FromArgb(255, 0xFC, 0xA5, 0xA5),
                     Windows.UI.Color.FromArgb(255, 0xB9, 0x1C, 0x1C)),
                LauncherCore.ServerState.Checking =>
                    (Windows.UI.Color.FromArgb(255, 0xFD, 0xE0, 0x68),
                     Windows.UI.Color.FromArgb(255, 0xA1, 0x62, 0x07)),
                _ =>
                    (Windows.UI.Color.FromArgb(255, 0xD1, 0xD5, 0xDB),
                     Windows.UI.Color.FromArgb(255, 0x6B, 0x72, 0x80)),
            };

            LampCore.Fill = Bead(bright, deep);
            LampGlow.Fill = Halo(deep);
        }

        static Microsoft.UI.Xaml.Media.RadialGradientBrush Bead(Windows.UI.Color bright, Windows.UI.Color deep)
        {
            var b = new Microsoft.UI.Xaml.Media.RadialGradientBrush
            {
                Center = new Windows.Foundation.Point(0.35, 0.32),
                GradientOrigin = new Windows.Foundation.Point(0.35, 0.32),
                RadiusX = 0.75,
                RadiusY = 0.75,
            };
            b.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = bright, Offset = 0.0 });
            b.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = deep,   Offset = 1.0 });
            return b;
        }

        static Microsoft.UI.Xaml.Media.RadialGradientBrush Halo(Windows.UI.Color deep)
        {
            var clear = Windows.UI.Color.FromArgb(0, deep.R, deep.G, deep.B);
            var mid   = Windows.UI.Color.FromArgb(150, deep.R, deep.G, deep.B);

            var b = new Microsoft.UI.Xaml.Media.RadialGradientBrush
            {
                Center = new Windows.Foundation.Point(0.5, 0.5),
                GradientOrigin = new Windows.Foundation.Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5,
            };
            b.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = mid,   Offset = 0.35 });
            b.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = clear, Offset = 1.0 });
            return b;
        }

        async void Play_Click(object sender, RoutedEventArgs e)
        {

            BtnPlay.IsEnabled = false;

            var log = new List<string>();
            LauncherCore.Settings s = _settings;

            LauncherCore.LaunchResult res =
                await Task.Run(() => LauncherCore.Launch(s, line => { lock (log) log.Add(line); }));

            BtnPlay.IsEnabled = true;

            if (!res.Ok)
            {

                await AppDialog.ShowAsync(res.Error, "Could not start the game",
                    DialogButtons.OK, DialogKind.Error);
                RefreshState();
                return;
            }

        }

        void Play_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
            => PlayFace.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                   Windows.UI.Color.FromArgb(0xFF, 0x3B, 0x6F, 0xF6));

        void Play_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
            => PlayFace.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                   Windows.UI.Color.FromArgb(0xFF, 0x1D, 0x4E, 0xD8));

        void Tools_Click(object sender, RoutedEventArgs e)
        {

            if (_tools != null)
            {
                try { _tools.Activate(); return; }
                catch { _tools = null; }
            }

            _tools = new MainWindow();
            _tools.Closed += (_, __) =>
            {
                _tools = null;

                AppDialog.Host = this;
            };

            AppDialog.Host = _tools;
            _tools.Activate();
        }

        void Settings_Click(object sender, RoutedEventArgs e)
        {
            TxtGameExe.Text   = _settings.GameExe ?? "";
            TxtDllPath.Text   = _settings.DllPath ?? "";
            TxtExtraArgs.Text = _settings.ExtraArgs ?? "";
            UpdateServerSummary();
            SettingsOverlay.Visibility = Visibility.Visible;
        }

        void UpdateServerSummary()
        {
            int n = _settings.Servers?.Count ?? 0;
            string selected = _settings.SelectedName;

            TxtServerSummary.Text = n == 0
                ? "None yet - add the one you were given."
                : $"{n} saved" + (string.IsNullOrWhiteSpace(selected) ? "" : $", using \"{selected}\"");
        }

        LauncherCore.ServerEntry _editing;

        void ManageServers_Click(object sender, RoutedEventArgs e)
        {
            EndEdit();
            RebuildServerList();
            ServersOverlay.Visibility = Visibility.Visible;
        }

        void EndEdit()
        {
            _editing = null;
            TxtServerNameEdit.Text = "";
            TxtSiteConfig.Text = "";
            TxtServerFormTitle.Text = "Add a server";
            BtnServerSave.Content = "Add";
            BtnServerCancelEdit.Visibility = Visibility.Collapsed;
        }

        void ServerEdit_Click(object sender, RoutedEventArgs e)
        {

            if (sender is not FrameworkElement fe || fe.Tag is not ServerRow row) return;
            if (row.Entry == null) return;

            _editing = row.Entry;
            TxtServerNameEdit.Text = row.Entry.Name ?? "";
            TxtSiteConfig.Text = row.Entry.SiteConfigUrl ?? "";
            TxtServerFormTitle.Text = "Edit server";

            BtnServerSave.Content = "Save server";
            BtnServerCancelEdit.Visibility = Visibility.Visible;
        }

        void ServerCancelEdit_Click(object sender, RoutedEventArgs e) => EndEdit();

        void ServersDone_Click(object sender, RoutedEventArgs e)
        {
            ServersOverlay.Visibility = Visibility.Collapsed;
            UpdateServerSummary();

            try { LauncherCore.SaveSettings(_settings); } catch { }
            RefreshState();
        }

        void RebuildServerList()
        {
            _rebuilding = true;
            try
            {
                var rows = new List<ServerRow>();
                foreach (LauncherCore.ServerEntry e in _settings.Servers)
                    rows.Add(new ServerRow
                    {

                        Entry = e,
                        Name = e.Name ?? "(unnamed)",
                        Url = e.SiteConfigUrl,
                    });

                ListServers.ItemsSource = rows;

                if (rows.Count > 0)
                    ListServers.SelectedIndex =
                        Math.Min(Math.Max(_settings.SelectedServer, 0), rows.Count - 1);
            }
            finally { _rebuilding = false; }
        }

        void ServerRow_Changed(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
        {
            if (_rebuilding || ListServers.SelectedIndex < 0) return;
            _settings.SelectedServer = ListServers.SelectedIndex;
        }

        async void ServerAdd_Click(object sender, RoutedEventArgs e)
        {
            OwnDialogs();

            string name = TxtServerNameEdit.Text?.Trim();
            string url  = TxtSiteConfig.Text?.Trim();

            if (string.IsNullOrWhiteSpace(url))
            {
                await AppDialog.ShowAsync("Enter the server address first - whoever runs the "
                    + "server gives you that.",
                    _editing == null ? "Add server" : "Edit server",
                    DialogButtons.OK, DialogKind.Info);
                return;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = url;
                int slash = name.IndexOf('/');
                if (slash > 0) name = name.Substring(0, slash);
            }

            if (_editing != null)
            {

                _editing.Name = name;
                _editing.SiteConfigUrl = url;
            }
            else
            {
                _settings.Servers.Add(new LauncherCore.ServerEntry { Name = name, SiteConfigUrl = url });
                _settings.SelectedServer = _settings.Servers.Count - 1;
            }

            EndEdit();
            RebuildServerList();
        }

        async void ServerRemove_Click(object sender, RoutedEventArgs e)
        {

            if (sender is not FrameworkElement fe || fe.Tag is not ServerRow row) return;
            if (row.Entry == null) return;

            OwnDialogs();

            if (await AppDialog.ShowAsync(
                    $"Remove \"{row.Name}\"?" + Environment.NewLine + Environment.NewLine
                    + row.Url + Environment.NewLine + Environment.NewLine
                    + "This only forgets the address here. Nothing on the server changes, and you "
                    + "can add it again.",
                    "Remove server", DialogButtons.OKCancel, DialogKind.Warning,
                    primaryText: "Remove", closeText: "Keep") != DialogResult.OK)
                return;

            _settings.Servers.Remove(row.Entry);

            if (ReferenceEquals(_editing, row.Entry)) EndEdit();

            if (_settings.SelectedServer >= _settings.Servers.Count)
                _settings.SelectedServer = Math.Max(0, _settings.Servers.Count - 1);

            RebuildServerList();
        }

        async void Verify_Click(object sender, RoutedEventArgs e)
        {
            VerifyOverlay.Visibility = Visibility.Visible;
            VerifyText.Blocks.Clear();
            VerifyFxText.Blocks.Clear();
            _verifyPlain.Clear();
            TxtVerifySummary.Text = "Asking the server what it has…";

            string url = _settings.SiteConfigUrl;

            string gameRoot = LauncherCore.GameRootFor(_settings);

            List<InstalledCostume> installed = new();
            List<FxPack> packs = new();
            bool localReadable = false;

            try
            {
                if (GamePaths.LooksLikeGameFolder(gameRoot))
                {
                    installed = CostumeLibrary.ListInstalled(gameRoot);
                    packs = FxPackRegistry.Read(FxPackInstall.RegistryPathFor(gameRoot));
                    localReadable = true;
                }
            }
            catch {  }

            CatalogCompare.Report report =
                await CatalogCompare.FetchAndCompareAsync(url, installed, packs, localReadable);

            if (!report.Ok)
            {
                TxtVerifySummary.Text = report.Error;
                VerifyText.Blocks.Clear();
                VerifyFxText.Blocks.Clear();
                _verifyPlain.Clear();

                _lastReport = null;
                BtnVerifyCopyMissing.IsEnabled = false;
                return;
            }

            VerifyText.Blocks.Clear();
            VerifyFxText.Blocks.Clear();
            _verifyPlain.Clear();

            RenderSection(VerifyText, "COSTUMES", report.Costumes);
            RenderSection(VerifyFxText, "EFFECTS PACKS", report.FxPacks);

            int missing = report.Count(CatalogCompare.Verdict.Missing);
            int conflict = report.Count(CatalogCompare.Verdict.Conflict);

            _lastReport = report;
            BtnVerifyCopyMissing.IsEnabled = missing > 0;

            TxtVerifySummary.Text = conflict > 0
                ? $"{conflict} item(s) will NOT work - the server reassigned those slots. Reinstall them."
                : missing > 0
                    ? $"{missing} item(s) this server has that you do not."
                    : "You have everything this server publishes.";
        }

        void RenderSection(Microsoft.UI.Xaml.Controls.RichTextBlock target, string title,
                           List<CatalogCompare.Row> rows)
        {
            var header = new Microsoft.UI.Xaml.Documents.Paragraph
            {
                Margin = new Thickness(0, 0, 0, 4),
            };
            header.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = $"{title}  ({rows.Count})",
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 0x1D, 0x4E, 0xD8)),
            });
            target.Blocks.Add(header);
            _verifyPlain.Add((_verifyPlain.Count == 0 ? "" : Environment.NewLine)
                             + $"{title}  ({rows.Count})");

            if (rows.Count == 0)
            {
                var none = new Microsoft.UI.Xaml.Documents.Paragraph();
                none.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                {
                    Text = "          this server publishes none",
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 0x6B, 0x72, 0x80)),
                });
                target.Blocks.Add(none);
                _verifyPlain.Add("          this server publishes none");
                return;
            }

            foreach (CatalogCompare.Row r in rows)
            {
                string badge = r.Verdict switch
                {
                    CatalogCompare.Verdict.Missing  => "MISSING ",
                    CatalogCompare.Verdict.Conflict => "CONFLICT",
                    CatalogCompare.Verdict.Extra    => "EXTRA   ",
                    _                               => "OK      ",
                };

                Windows.UI.Color colour = r.Verdict switch
                {
                    CatalogCompare.Verdict.Missing  => Windows.UI.Color.FromArgb(255, 0x92, 0x40, 0x0E),
                    CatalogCompare.Verdict.Conflict => Windows.UI.Color.FromArgb(255, 0xB9, 0x1C, 0x1C),
                    CatalogCompare.Verdict.Extra    => Windows.UI.Color.FromArgb(255, 0x6B, 0x72, 0x80),
                    _                               => Windows.UI.Color.FromArgb(255, 0x15, 0x80, 0x3D),
                };

                var para = new Microsoft.UI.Xaml.Documents.Paragraph();

                para.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                {
                    Text = badge + "  ",
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(colour),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                });
                para.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                {
                    Text = r.Name,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 0x1F, 0x29, 0x37)),
                });

                target.Blocks.Add(para);
                _verifyPlain.Add(badge.TrimEnd() + "  " + r.Name);

                if (!string.IsNullOrWhiteSpace(r.Detail))
                {
                    var sub = new Microsoft.UI.Xaml.Documents.Paragraph
                    { Margin = new Thickness(0, 0, 0, 6) };
                    sub.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                    {
                        Text = "          " + r.Detail,
                        Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            Windows.UI.Color.FromArgb(255, 0x6B, 0x72, 0x80)),
                    });
                    target.Blocks.Add(sub);
                    _verifyPlain.Add("          " + r.Detail);
                }
            }
        }

        void VerifyClose_Click(object sender, RoutedEventArgs e)
            => VerifyOverlay.Visibility = Visibility.Collapsed;

        readonly List<string> _verifyPlain = new();

        void VerifyCopy_Click(object sender, RoutedEventArgs e)
        {
            if (_verifyPlain.Count == 0) return;

            var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
            pkg.SetText(TxtVerifySummary.Text + Environment.NewLine + Environment.NewLine
                        + string.Join(Environment.NewLine, _verifyPlain));
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
        }

        CatalogCompare.Report _lastReport;

        void VerifyCopyMissing_Click(object sender, RoutedEventArgs e)
        {
            if (_lastReport == null) return;

            List<CatalogCompare.Row> costumes = _lastReport.Costumes
                .Where(r => r.Verdict == CatalogCompare.Verdict.Missing).ToList();
            List<CatalogCompare.Row> packs = _lastReport.FxPacks
                .Where(r => r.Verdict == CatalogCompare.Verdict.Missing).ToList();

            if (costumes.Count == 0 && packs.Count == 0) return;

            var sb = new System.Text.StringBuilder();
            string server = _settings?.SelectedName;
            sb.Append("Content I am missing")
              .Append(string.IsNullOrWhiteSpace(server) ? "" : " for \"" + server + "\"")
              .AppendLine(":").AppendLine();

            void Section(string title, List<CatalogCompare.Row> rows, string kind)
            {
                if (rows.Count == 0) return;
                sb.AppendLine($"{title} ({rows.Count})");
                foreach (CatalogCompare.Row r in rows)
                {

                    sb.AppendLine(string.IsNullOrWhiteSpace(r.File)
                        ? $"  - {r.Name}   (ask for the {kind})"
                        : $"  - {r.Name}   ->  {r.File}");
                }
                sb.AppendLine();
            }

            Section("COSTUMES", costumes, ".mhcostume");
            Section("EFFECTS PACKS", packs, ".mhfxpack");

            sb.Append("Install with the Marvel Heroes launcher: Tools -> ");

            if (costumes.Count + packs.Count == 1)
            {
                sb.AppendLine(costumes.Count == 1
                    ? "\"Install costume...\"."
                    : "\"Install effects pack...\".");
            }
            else
            {
                sb.AppendLine("\"Install a bundle...\" for a single .mhbundle,");
                sb.Append("or ");
                if (costumes.Count > 0) sb.Append("\"Install costume...\"");
                if (costumes.Count > 0 && packs.Count > 0) sb.Append(" / ");
                if (packs.Count > 0) sb.Append("\"Install effects pack...\"");
                sb.AppendLine(" for the files one at a time.");
            }

            var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
            pkg.SetText(sb.ToString());
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);

            int n = costumes.Count + packs.Count;
            TxtVerifySummary.Text = $"Copied a list of {n} missing item(s) to the clipboard - "
                                  + "paste it to whoever runs the server.";
        }

        void OwnDialogs() => AppDialog.Host = this;

        void SettingsCancel_Click(object sender, RoutedEventArgs e)
            => SettingsOverlay.Visibility = Visibility.Collapsed;

        async void SettingsSave_Click(object sender, RoutedEventArgs e)
        {
            var candidate = new LauncherCore.Settings
            {
                GameExe        = TxtGameExe.Text?.Trim(),
                DllPath        = TxtDllPath.Text?.Trim(),
                ExtraArgs      = TxtExtraArgs.Text?.Trim(),

                Servers        = _settings.Servers,
                SelectedServer = _settings.SelectedServer,
            };

            try { LauncherCore.SaveSettings(candidate); }
            catch (Exception ex)
            {
                await AppDialog.ShowAsync("Could not save the settings: " + ex.Message,
                    "Settings", DialogButtons.OK, DialogKind.Error);
                return;
            }

            _settings = candidate;
            SettingsOverlay.Visibility = Visibility.Collapsed;
            RefreshState();
        }

        async void BrowseExe_Click(object sender, RoutedEventArgs e)
        {
            StorageFile f = await PickFile(".exe");
            if (f != null) TxtGameExe.Text = f.Path;
        }

        async void BrowseDll_Click(object sender, RoutedEventArgs e)
        {
            StorageFile f = await PickFile(".dll");
            if (f != null) TxtDllPath.Text = f.Path;
        }

        async Task<StorageFile> PickFile(string extension)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(extension);
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;

            WinRT.Interop.InitializeWithWindow.Initialize(
                picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

            return await picker.PickSingleFileAsync();
        }
    }

    public sealed class VerifyRow
    {
        public string Badge { get; set; }
        public Microsoft.UI.Xaml.Media.Brush BadgeBrush { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public override string ToString() => Badge + " " + Title;
    }

    public sealed class ServerRow
    {

        public LauncherCore.ServerEntry Entry { get; set; }

        public string Name { get; set; }
        public string Url { get; set; }
        public override string ToString() => Name ?? Url;
    }
}
