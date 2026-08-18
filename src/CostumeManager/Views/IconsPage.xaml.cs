using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using CostumeManager.Core;

using IconPack.Core;
using IconSource = IconPack.Core.IconSource;

namespace CostumeManager.Views
{

    public sealed partial class IconsPage : Page
    {
        public IconsPage()
        {
            InitializeComponent();
            PopulateUpdateTargets();

            PopulateIcons(Installing ? Path.GetDirectoryName(AppState.PickedUpk) : null);
            ApplyMode();
        }

        static bool Installing => !string.IsNullOrWhiteSpace(AppState.PickedUpk);

        public sealed class IconRoleRow : INotifyPropertyChanged
        {
            public IconRole Role { get; set; }
            public string RoleName { get; set; }
            public string Surfaces { get; set; }
            public string FileName { get; set; }
            public ImageSource Image { get; set; }

            string _detail = "";
            public string Detail
            {
                get => _detail;
                set { _detail = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail))); }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        void PopulateIcons(string modDir)
        {
            int detected = 0;
            foreach (IconRoleInfo info in IconPackBuilder.Roles)
            {

                if (!AppState.IconChoices.ContainsKey(info.Role) && modDir != null)
                {
                    string guess = GuessIconFile(modDir, info.Role);
                    if (guess != null) { AppState.IconChoices[info.Role] = guess; detected++; }
                }
            }

            if (detected > 0 && Installing && !AppState.UseCustomIcons)
            {
                AppState.UseCustomIcons = true;
                AppState.Log($"icons: {detected} found beside the UPK — custom icons enabled "
                           + "(untick on Install to skip them)");
            }

            RefreshIconRows();
        }

        void RefreshIconRows()
        {
            var rows = new List<IconRoleRow>();
            foreach (IconRoleInfo info in IconPackBuilder.Roles)
            {
                AppState.IconChoices.TryGetValue(info.Role, out string chosen);
                rows.Add(BuildIconRow(info, chosen));
            }

            IconRoleList.ItemsSource = null;
            IconRoleList.ItemsSource = rows;
        }

        static IconRoleRow BuildIconRow(IconRoleInfo info, string chosen)
        {
            var row = new IconRoleRow
            {
                Role     = info.Role,
                RoleName = info.Role.ToString(),
                Surfaces = info.Description,
                FileName = chosen ?? "(none — the donor's icon will be used)",
            };

            if (chosen == null) return row;

            if (!File.Exists(chosen))
            {
                row.FileName = Path.GetFileName(chosen);
                row.Detail = "⚠ this file is no longer on disk - pick it again";
                return row;
            }

            try
            {
                if (chosen.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                {

                    DdsPreview.Loaded loaded = DdsPreview.Load(chosen);
                    row.Image  = loaded.Image;
                    row.Detail = loaded.Error ?? $"{loaded.Width}×{loaded.Height}  {loaded.Format}";
                }
                else
                {
                    var bmp = new BitmapImage(new Uri(Path.GetFullPath(chosen)));
                    row.Image  = bmp;
                    row.Detail = "(will be scaled and compressed)";

                    bmp.ImageOpened += (s, e) =>
                        row.Detail = $"{bmp.PixelWidth}×{bmp.PixelHeight}  (will be scaled and compressed)";
                    bmp.ImageFailed += (s, e) => row.Detail = "could not preview: " + e.ErrorMessage;
                }
                row.FileName = Path.GetFileName(chosen);
            }
            catch (Exception ex)
            {
                row.Detail = "could not preview: " + ex.Message;
            }
            return row;
        }

        static string GuessIconFile(string dir, IconRole role)
        {
            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { return null; }

            bool Match(string f, params string[] needles)
            {
                string n = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                return needles.Any(x => n.Contains(x));
            }

            IEnumerable<string> images = files.Where(f =>
            {
                string e = Path.GetExtension(f).ToLowerInvariant();
                return e == ".dds" || e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".bmp";
            });

            return role switch
            {
                IconRole.Store    => images.FirstOrDefault(f => Match(f, "store")),
                IconRole.Portrait => images.FirstOrDefault(f => Match(f, "herohor", "portrait")),

                IconRole.Token    => images.FirstOrDefault(f => Match(f, "costume", "icon")
                                                                && !Match(f, "store", "herohor", "portrait")),
                _ => null,
            };
        }

        async void BrowseIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not IconRole role) return;

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

            if (Installing) AppState.UseCustomIcons = true;

            RefreshIconRows();
            AppState.Log($"icons: {role} <- {Path.GetFileName(file.Path)}");
        }

        void ClearIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not IconRole role) return;
            AppState.IconChoices.Remove(role);
            RefreshIconRows();
            AppState.Log($"icons: {role} cleared (donor icon will be used)");
        }

        void ApplyMode()
        {
            bool installing = Installing;
            TxtIconSearch.IsEnabled = !installing;
            ListUpdateTargets.IsEnabled = !installing;
            BtnRefreshUpdateTargets.IsEnabled = !installing;
            BtnUpdateIcons.IsEnabled = !installing;
            TxtIconUpdateBlocked.Visibility = installing ? Visibility.Visible : Visibility.Collapsed;
        }

        public sealed class UpdateTarget
        {
            public uint Enum { get; set; }
            public string DisplayName { get; set; }

            public string Label { get; set; }

            public string SubLabel { get; set; }

            public Microsoft.UI.Xaml.Media.ImageSource Thumb => CostumeThumb.For(Enum);

            public string SearchBlob { get; set; }

            public override string ToString() => Label ?? DisplayName ?? base.ToString();
        }

        void PopulateUpdateTargets()
        {
            object previous = (ListUpdateTargets.SelectedItem as UpdateTarget)?.Enum;
            _targets.Clear();

            string dir = AppState.GameDir;
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) { ApplySearch(null); return; }

            List<InstalledCostume> installed;
            try { installed = Installer.ListInstalled(dir); }
            catch { return; }

            var withIcons = new HashSet<uint>();
            try
            {
                var (_, _, bin) = Installer.ResolvePaths(dir);

                string jsonPath = Path.Combine(bin, "CustomCostumes.json");
                if (CostumeConfig.Exists(jsonPath) &&
                    System.Text.Json.Nodes.JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath))?["costumes"]
                        is System.Text.Json.Nodes.JsonArray arr)
                {
                    foreach (var n in arr)
                        if (n is System.Text.Json.Nodes.JsonObject o && o["iconPackage"] != null && o["enum"] != null)
                            withIcons.Add(o["enum"].GetValue<uint>());
                }
            }
            catch {  }

            foreach (InstalledCostume c in installed.OrderBy(c => c.Enum))
            {
                string hero = FxCompatibility.HeroOfCostume(c.DonorClass);
                _targets.Add(new UpdateTarget
                {
                    Enum = c.Enum,
                    DisplayName = c.DisplayName,
                    Label = $"{c.DisplayName}  (enum {c.Enum}"
                          + (withIcons.Contains(c.Enum) ? ", has icons)" : ", no icons yet)"),
                    SubLabel = $"enum {c.Enum}  ·  "
                             + (withIcons.Contains(c.Enum) ? "has icons" : "no icons yet"),
                    SearchBlob = string.Join(" ", c.DisplayName, c.DonorClass, hero,
                                             "enum" + c.Enum).ToLowerInvariant(),
                });
            }

            ApplySearch(previous as uint?);
        }

        readonly List<UpdateTarget> _targets = new List<UpdateTarget>();

        void IconSearch_Changed(object sender, TextChangedEventArgs e)
            => ApplySearch((ListUpdateTargets.SelectedItem as UpdateTarget)?.Enum);

        void ApplySearch(uint? keep)
        {
            string q = TxtIconSearch?.Text;
            var shown = _targets.Where(t => MatchesSearch(t.SearchBlob, q)).ToList();

            ListUpdateTargets.ItemsSource = shown;
            if (keep is uint want)
            {
                UpdateTarget hit = shown.FirstOrDefault(t => t.Enum == want);
                if (hit != null) ListUpdateTargets.SelectedItem = hit;
            }

            TxtNoTargets.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (shown.Count == 0)
                TxtNoTargets.Text = _targets.Count == 0
                    ? "No installed costumes found. Pick your game folder on the Settings tab."
                    : $"No costume matches \"{q}\".";
        }

        static bool MatchesSearch(string blob, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            if (blob == null) return false;
            foreach (string term in query.ToLowerInvariant()
                                         .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (blob.IndexOf(term, StringComparison.Ordinal) < 0) return false;
            return true;
        }

        void RefreshUpdateTargets_Click(object sender, RoutedEventArgs e)
        {
            PopulateUpdateTargets();
            AppState.Log($"update targets: {_targets.Count} installed costume(s)");
        }

        static string IconCacheDir => CostumeThumb.IconArtDir;

        static void CacheIconArt(uint enumId, IEnumerable<IconSource> sources)
        {
            try
            {
                Directory.CreateDirectory(IconCacheDir);

                var supplied = new HashSet<IconRole>();
                foreach (IconSource s in sources)
                    if (!string.IsNullOrWhiteSpace(s.ImagePath)) supplied.Add(s.Role);

                foreach (IconRoleInfo r in IconPackBuilder.Roles)
                {
                    if (supplied.Contains(r.Role)) continue;
                    foreach (string stale in Directory.GetFiles(IconCacheDir, $"{enumId}_{r.Role}.*"))
                        File.Delete(stale);
                }

                foreach (IconSource s in sources)
                {
                    if (string.IsNullOrWhiteSpace(s.ImagePath) || !File.Exists(s.ImagePath)) continue;
                    string dest = Path.Combine(IconCacheDir,
                        $"{enumId}_{s.Role}{Path.GetExtension(s.ImagePath)}");

                    bool alreadyCached = SamePath(s.ImagePath, dest);

                    try
                    {

                        foreach (string old in Directory.GetFiles(IconCacheDir, $"{enumId}_{s.Role}.*"))
                            if (!SamePath(old, dest)) File.Delete(old);

                        if (!alreadyCached) File.Copy(s.ImagePath, dest, true);
                    }
                    catch (Exception ex)
                    {
                        AppState.Log($"icons: could not cache the {s.Role} art - {ex.Message}");
                    }
                }
            }
            catch {  }
        }

        static string FindCachedIconArt(uint enumId, IconRole role)
            => CostumeThumb.FindArt(enumId, role);

        static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        uint? _loadedFor;

        void UpdateTarget_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (ListUpdateTargets.SelectedItem is not UpdateTarget t) return;

            if (_loadedFor == t.Enum) return;
            _loadedFor = t.Enum;

            int found = 0;
            foreach (IconRoleInfo info in IconPackBuilder.Roles)
            {
                string cached = FindCachedIconArt(t.Enum, info.Role);
                if (cached != null) { AppState.IconChoices[info.Role] = cached; found++; }
                else AppState.IconChoices.Remove(info.Role);
            }

            RefreshIconRows();

            AppState.Log(found > 0
                ? $"icons: showing {found} saved image(s) for \"{t.DisplayName}\" - replace any role you want to change"
                : $"icons: no saved art for \"{t.DisplayName}\" (installed before art was kept, or it has no custom icons) - pick images to add them");
        }

        async void UpdateIcons_Click(object sender, RoutedEventArgs e)
        {
            string dir = AppState.GameDir;
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                await AppDialog.ShowAsync("Pick your game folder on the Settings tab first.",
                    "Update icons", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            IconPlan plan = IconPlanner.Resolve(Installing);
            if (plan.Sources.Count == 0)
            {
                await AppDialog.ShowAsync("Choose at least one image first.",
                    "Update icons", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            if (ListUpdateTargets.SelectedItem is not UpdateTarget match)
            {
                await AppDialog.ShowAsync("Choose which installed costume to update from the list.",
                    "Update icons", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            string changing = "\n\nWill use your art for:\n    "
                + string.Join("\n    ", plan.Sources.Select(s => s.Role.ToString()));

            string reverting = plan.DonorFallback.Count > 0
                ? "\n\nWill fall back to the DONOR's icon (no art chosen):\n    "
                  + string.Join("\n    ", plan.DonorFallback)
                : string.Empty;

            DialogResult confirm = await AppDialog.ShowAsync(
                $"Rebuild icons for \"{match.DisplayName}\" (enum {match.Enum})?\n\n"
                + "Only the icon package and its JSON entries change - the costume UPK, the TFC "
                + "aliases and the ledger are left alone." + changing + reverting,
                "Update icons", DialogButtons.OKCancel, DialogKind.Info);
            if (confirm != DialogResult.OK) return;

            try
            {
                AppState.Log("");
                AppState.Log($"=== update icons: {match.DisplayName} (enum {match.Enum}) ===");

                string serverDir = AppState.ServerDir;
                Installer.IconUpdateResult res = await System.Threading.Tasks.Task.Run(
                    () => Installer.UpdateIcons(dir, match.Enum, plan.Sources,
                                                match.DisplayName, AppState.Logger, serverDir));

                foreach (string s in res.Steps) AppState.Log("  " + s);

                if (res.Ok)
                {
                    CacheIconArt(match.Enum, plan.Sources);
                    CostumeThumb.Forget(match.Enum);
                    PopulateUpdateTargets();
                }
                else
                {
                    await AppDialog.ShowAsync($"Icon update failed at step: {res.FailedStep}. See the log.",
                        "Update icons", DialogButtons.OK, DialogKind.Error);
                }
            }
            catch (Exception ex)
            {
                AppState.Log("icon update failed: " + ex.Message);
                await AppDialog.ShowAsync(ex.Message, "Update icons", DialogButtons.OK, DialogKind.Error);
            }
        }
    }
}
