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

    public sealed partial class EffectsPage : Page
    {
        EffectTables _fx;
        Installer.CostumeEffects _fxSel;
        List<FxGroupRow> _fxAllRows = new List<FxGroupRow>();
        string _lastFxQuery;

        FxScanCache.Scan _fxScan;
        string _fxScanFolder;

        List<FxCandidate> _fxCands = new List<FxCandidate>();
        List<FxCandidateRow> _fxCandRows = new List<FxCandidateRow>();

        public EffectsPage()
        {
            InitializeComponent();
            SyncFxScanVisibility();
            LoadFxList();
        }

        EffectTables FxTables()
        {
            if (_fx == null) _fx = EffectTables.Load(EffectTables.DefaultPath());
            return _fx;
        }

        public sealed class FxRow
        {
            public string DisplayName { get; set; }
            public uint Enum { get; set; }
            public string EnumText => $"enum {Enum}";

            public Microsoft.UI.Xaml.Media.ImageSource Thumb => CostumeThumb.For(Enum);

            public string EnumAndCount => $"enum {Enum}  ·  {FxCountText}";
            public string FxCountText { get; set; }
            public string FxSummary { get; set; }

            public string SubLabel => $"enum {Enum}  ·  {FxCountText}  ·  {FxSummary}";

            public string ProblemText { get; set; }
            public Visibility ProblemVisibility =>
                string.IsNullOrEmpty(ProblemText) ? Visibility.Collapsed : Visibility.Visible;

            public string SearchBlob { get; set; }
        }

        public sealed class FxAssignCandidate
        {
            public uint Enum { get; set; }
            public string DisplayName { get; set; }

            public string CurrentToken { get; set; }

            public override string ToString()
                => DisplayName + (CurrentToken != null ? $"   (currently on \"{CurrentToken}\")" : "");
        }

        public sealed class FxGroupRow
        {
            public string Token { get; set; }
            public string DisplayName { get; set; }
            public string SubLabel { get; set; }
            public string ProblemText { get; set; }
            public Visibility ProblemVisibility =>
                string.IsNullOrEmpty(ProblemText) ? Visibility.Collapsed : Visibility.Visible;

            public List<FxRow> Users { get; set; } = new List<FxRow>();
            public List<FxAssignCandidate> Candidates { get; set; } = new List<FxAssignCandidate>();

            public FxAssignCandidate PickedCandidate { get; set; }

            public bool Expanded { get; set; }
            public string Chevron => Expanded ? "▾" : "▸";
            public Visibility BodyVisibility => Expanded ? Visibility.Visible : Visibility.Collapsed;

            public string EmptyText { get; set; }
            public Visibility EmptyVisibility =>
                Users.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            public Visibility AssignVisibility =>
                Token != null && Candidates.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            public string SearchBlob { get; set; }
        }

        public sealed class FxCandidateRow
        {
            public string Title { get; set; }
            public string FileName { get; set; }
            public string Detail { get; set; }
            public string StateText { get; set; }
            public string WarnText { get; set; }
            public Visibility WarnVisibility =>
                string.IsNullOrEmpty(WarnText) ? Visibility.Collapsed : Visibility.Visible;
            public bool Selected { get; set; }
            public bool CanSelect { get; set; }

            public bool Installed { get; set; }
            public ulong From { get; set; }
            public string Package { get; set; }

            public bool Suggested { get; set; }

            public bool SiblingOffered { get; set; }
            public bool SiblingEnabled { get; set; }
            public string SiblingText { get; set; }
            public string SiblingTooltip { get; set; }
            public Visibility SiblingVisibility =>
                SiblingOffered ? Visibility.Visible : Visibility.Collapsed;

            public string SiblingLabel => "Fix shared name — " + SiblingText;
        }

        void FxRefresh_Click(object sender, RoutedEventArgs e) => LoadFxList();

        void LoadFxList()
        {
            try
            {
                EffectTables tables = FxTables();

                if (tables.IsEmpty)
                {
                    FxBanner.Visibility = Visibility.Visible;
                    TxtFxBanner.Text = "Effects.json was not found next to CostumeManager.exe. "
                        + "Effects can still be listed, but they cannot be named and new FX "
                        + "cannot be identified. Rebuild it with pak\\buildeffects.py.";
                }
                else
                {
                    FxBanner.Visibility = Visibility.Collapsed;
                }

                string dir = AppState.GameDir;
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                {
                    FxPackList.ItemsSource = null;
                    TxtNoFx.Visibility = Visibility.Visible;
                    TxtNoFx.Text = "Pick your game folder on the Settings tab first.";
                    return;
                }

                try
                {
                    Installer.UninstallResult mig = Installer.MigrateFxPacks(dir);
                    if (mig.Ok && mig.Steps.Any(x => x.StartsWith("migrated")))
                        foreach (string x in mig.Steps) AppState.Log("  " + x);
                    else if (!mig.Ok && mig.Error != null)
                        AppState.Log("⚠ could not adopt existing FX as packs: " + mig.Error);
                }
                catch (Exception ex) { AppState.Log("⚠ FX pack migration: " + ex.Message); }

                List<Installer.CostumeEffects> all = Installer.ListEffects(dir, tables);

                var rows = all.Select(c =>
                {
                    var r = new FxRow
                    {
                        DisplayName = c.DisplayName + (c.Enabled ? "" : "   (hidden)"),
                        Enum = c.Enum,
                        FxCountText = !c.OptedIn ? "no custom FX"
                                    : c.Count == 0 ? "isolation mode"
                                    : c.Count + " effect(s)",
                    };

                    if (!c.OptedIn)
                    {
                        r.FxSummary = "Uses the donor's stock effects. Scan a folder to add custom FX.";
                    }
                    else if (c.Count == 0)
                    {

                        r.FxSummary = "Opted in with an EMPTY effects list - the isolation test. "
                                    + "The costume gets its own forged asset but no effect is redirected.";
                    }
                    else
                    {
                        var names = c.Effects.Select(x => x.EffectName ?? x.Package).Take(3).ToList();
                        string more = c.Count > 3 ? ", +" + (c.Count - 3) : "";
                        r.FxSummary = string.Join(", ", names) + more;
                    }

                    int broken = c.BrokenCount;
                    if (broken > 0)
                    {
                        var missing = c.Effects.Where(x => !x.UpkExists)
                                               .Select(x => x.Package).Take(3).ToList();
                        r.ProblemText = "⛔ " + broken + " effect package(s) missing from CookedPCConsole ("
                            + string.Join(", ", missing) + (broken > missing.Count ? ", …" : "") + "). "
                            + "This costume will NOT arm - the donor renders instead. Remove those "
                            + "entries or re-install the FX.";
                    }

                    r.SearchBlob = string.Join(" ",
                        c.DisplayName, c.DonorClass,
                        FxCompatibility.HeroOfCostume(c.DonorClass), "enum" + c.Enum).ToLowerInvariant();
                    return r;
                }).ToList();

                _fxAllRows = GroupByPack(dir, all, rows);
                ApplyFxSearch();

                int withFx = all.Count(c => c.OptedIn && c.Count > 0);
                int brokenTotal = all.Sum(c => c.BrokenCount);
                int packCount = _fxAllRows.Count(g => g.Token != null);
                TxtFxSub.Text = $"{packCount} FX pack(s)  ·  {rows.Count} costume(s), {withFx} with custom FX"
                      + (brokenTotal > 0 ? $"  ·  ⛔ {brokenTotal} missing package(s)" : "")
                      + (tables.IsEmpty ? "" : $"  ·  Effects.json: {tables.Count} known effects");
            }
            catch (Exception ex)
            {
                AppState.Log("could not list effects: " + ex.Message);
            }
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

        List<FxGroupRow> GroupByPack(string dir, List<Installer.CostumeEffects> all, List<FxRow> rows)
        {
            Dictionary<uint, string> assigned = AssignedPackTokens(dir);
            Dictionary<uint, FxRow> byEnum = rows.ToDictionary(r => r.Enum);
            Dictionary<uint, string> heroOf = all.ToDictionary(
                c => c.Enum, c => FxCompatibility.HeroOfCostume(c.DonorClass) ?? "");

            List<FxPack> packs;
            try { packs = FxPackRegistry.Read(FxPackInstall.RegistryPathFor(dir)); }
            catch (Exception ex) { AppState.Log("packs: " + ex.Message); packs = new List<FxPack>(); }

            var groups = new List<FxGroupRow>();

            foreach (FxPack p in packs.OrderBy(p => p.DisplayName ?? p.Token,
                                               StringComparer.OrdinalIgnoreCase))
            {
                List<uint> users = assigned.Where(kv => string.Equals(kv.Value, p.Token,
                                                       StringComparison.OrdinalIgnoreCase))
                                           .Select(kv => kv.Key).ToList();

                var g = new FxGroupRow
                {
                    Token = p.Token,
                    DisplayName = p.DisplayName ?? p.Token,
                    SubLabel = $"{p.Effects.Count} effect(s)  ·  used by {users.Count} costume(s)"
                             + (string.IsNullOrWhiteSpace(p.Hero) ? "" : "  ·  " + p.Hero),
                    Users = users.Where(byEnum.ContainsKey).Select(u => byEnum[u])
                                 .OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
                                 .ToList(),
                    EmptyText = "No costume uses this pack yet. Its packages are on disk and cost "
                              + "nothing until one does.",
                };

                g.Candidates = all
                    .Where(c => !string.IsNullOrWhiteSpace(p.Hero)
                             && string.Equals(heroOf.GetValueOrDefault(c.Enum, ""), p.Hero,
                                              StringComparison.OrdinalIgnoreCase)
                             && !users.Contains(c.Enum))
                    .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(c => new FxAssignCandidate
                    {
                        Enum = c.Enum,
                        DisplayName = c.DisplayName,
                        CurrentToken = assigned.GetValueOrDefault(c.Enum),
                    }).ToList();

                int broken = g.Users.Count(u => !string.IsNullOrEmpty(u.ProblemText));
                if (broken > 0)
                    g.ProblemText = "⛔ " + broken + " costume(s) here name effect packages that are "
                                  + "missing from CookedPCConsole - open one to see which.";

                g.SearchBlob = string.Join(" ",
                    new[] { p.Token, p.DisplayName, p.Hero }
                        .Concat(g.Users.Select(u => u.SearchBlob))).ToLowerInvariant();

                groups.Add(g);
            }

            List<FxRow> orphans = rows.Where(r => !assigned.ContainsKey(r.Enum)
                                               || string.IsNullOrWhiteSpace(assigned[r.Enum]))
                                      .OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
                                      .ToList();
            if (orphans.Count > 0)
                groups.Add(new FxGroupRow
                {
                    Token = null,
                    DisplayName = "Not using an FX pack",
                    SubLabel = $"{orphans.Count} costume(s)  ·  stock effects, or their own "
                             + "per-costume FX. Open one to scan a folder or give it a pack.",
                    Users = orphans,
                    EmptyText = "",
                    SearchBlob = string.Join(" ", orphans.Select(o => o.SearchBlob)).ToLowerInvariant()
                               + " no pack none",
                });

            return groups;
        }

        static Dictionary<uint, string> AssignedPackTokens(string gameRoot)
        {
            var map = new Dictionary<uint, string>();
            try
            {
                string jsonPath = CostumeLibrary.CustomCostumesJson(gameRoot);
                if (!CostumeConfig.Exists(jsonPath)) return map;
                if (System.Text.Json.Nodes.JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath))
                        is not System.Text.Json.Nodes.JsonObject root) return map;

                foreach (string key in new[] { "costumes", "disabled" })
                {
                    if (root[key] is not System.Text.Json.Nodes.JsonArray arr) continue;
                    foreach (var n in arr)
                    {
                        if (n is not System.Text.Json.Nodes.JsonObject o) continue;
                        uint en = o["enum"]?.GetValue<uint>() ?? 0;
                        string tok = (string)o["fxPack"];
                        if (en != 0 && !string.IsNullOrWhiteSpace(tok)) map[en] = tok;
                    }
                }
            }
            catch { }
            return map;
        }

        void FxSearch_Changed(object sender, TextChangedEventArgs e) => ApplyFxSearch();

        void ApplyFxSearch()
        {
            string q = TxtFxSearch?.Text;
            var shown = _fxAllRows.Where(r => MatchesSearch(r.SearchBlob, q)).ToList();

            if (!string.IsNullOrWhiteSpace(q) && q != _lastFxQuery)
                foreach (FxGroupRow g in shown) g.Expanded = true;
            _lastFxQuery = q;

            FxPackList.ItemsSource = null;
            FxPackList.ItemsSource = shown;

            TxtNoFx.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (shown.Count == 0)
                TxtNoFx.Text = _fxAllRows.Count == 0
                    ? "No FX packs and no installed costumes found in CustomCostumes.json."
                    : $"Nothing matches \"{q}\" - the search covers pack names AND costume names.";
        }

        void FxToggleGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;

            FxGroupRow row = fe.DataContext as FxGroupRow
                          ?? _fxAllRows.FirstOrDefault(g => g.Token != null
                                                         && Equals(g.Token, fe.Tag));
            if (row == null) return;

            row.Expanded = !row.Expanded;
            ApplyFxSearch();
        }

        async void FxAssignFromPack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            FxGroupRow group = fe.DataContext as FxGroupRow;
            FxAssignCandidate pick = group?.PickedCandidate;

            if (group?.Token == null || pick == null)
            {
                await AppDialog.ShowAsync("Pick a costume in the drop-down first.",
                    "Use FX pack", DialogButtons.OK, DialogKind.Info);
                return;
            }

            bool swapping = !string.IsNullOrWhiteSpace(pick.CurrentToken);
            if (swapping)
            {
                DialogResult go = await AppDialog.ShowAsync(
                    $"\"{pick.DisplayName}\" is already using pack \"{pick.CurrentToken}\".\n\n"
                    + $"Move it to \"{group.DisplayName}\"?\n\n"
                    + "Its current effects are removed from the config first. No file is deleted - "
                    + "both packs keep their packages for anyone else using them.",
                    "Change FX pack", DialogButtons.OKCancel, DialogKind.Warning,
                    primaryText: "Move it", closeText: "Cancel");
                if (go != DialogResult.OK) return;
            }

            string dir = AppState.GameDir, serverDir = AppState.ServerDir;
            string token = group.Token;
            uint enumId = pick.Enum;
            EffectTables tables = FxTables();

            AppState.Log("");
            AppState.Log($"── giving \"{pick.DisplayName}\" the pack \"{token}\" ──");

            Installer.UninstallResult r = await Task.Run(() =>
            {
                if (swapping)
                {
                    Installer.UninstallResult drop =
                        Installer.UnassignFxPack(dir, enumId, tables, AppState.Logger, serverDir);
                    if (!drop.Ok) return drop;
                }
                return Installer.AssignFxPack(dir, enumId, token, tables, AppState.Logger, serverDir);
            });

            foreach (string s in r.Steps) AppState.Log("  " + s);

            if (!r.Ok)
            {
                await AppDialog.ShowAsync(r.Error ?? "failed", "Use FX pack",
                    DialogButtons.OK, DialogKind.Error);
                return;
            }

            LoadFxList();
            await AppDialog.ShowAsync(
                $"\"{pick.DisplayName}\" now uses \"{group.DisplayName}\".\n\n"
                + "Nothing was written to disk - the packages already exist. Restart the client "
                + "AND the server: both read their config once at startup.",
                "Use FX pack", DialogButtons.OK);
        }

        async void FxExportOnePack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string token) return;

            StorageFile file = await PickSave(token, "Effects pack", FxPackFile.Extension);
            if (file == null) return;

            string dir = AppState.GameDir, outPath = file.Path;

            AppState.Log("");
            AppState.Log($"── exporting FX pack {token} ──");
            AppState.Log($"  destination: {outPath}");

            Installer.UninstallResult r = await Task.Run(
                () => Installer.ExportFxPack(dir, token, outPath, AppState.Logger));

            await AppDialog.ShowAsync(
                r.Ok ? $"Finished exporting!\n\nPlayers need this AND a costume whose pack "
                       + $"token is \"{token}\". Either order works."
                     : (r.Error ?? "Export failed. See the log."),
                "Export pack", DialogButtons.OK, r.Ok ? DialogKind.Info : DialogKind.Error);
        }

        async void FxExportAllPacks_Click(object sender, RoutedEventArgs e)
        {
            string dir = AppState.GameDir;
            if (string.IsNullOrWhiteSpace(dir))
            {
                await AppDialog.ShowAsync("Pick your game folder on the Settings tab first.",
                    "Export packs", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            List<string> tokens = _fxAllRows.Where(g => g.Token != null).Select(g => g.Token).ToList();
            if (tokens.Count == 0)
            {
                await AppDialog.ShowAsync("No FX packs are installed.",
                    "Export packs", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            StorageFile file = await PickSave("fxpacks", "Costume bundle", BulkPack.Extension);
            if (file == null) return;

            string outPath = file.Path;

            AppState.Log("");
            AppState.Log($"── exporting {tokens.Count} FX pack(s) as a bundle ──");
            AppState.Log($"  destination: {outPath}");

            BulkPack.BulkResult res = await Task.Run(
                () => BulkPack.ExportFxBundle(dir, tokens, outPath, AppState.Logger,
                                              registryPath: null, title: "FX packs"));

            if (!res.Ok)
            {
                await AppDialog.ShowAsync(
                    "The bundle was not written (" + (res.FailedStep ?? "unknown") + ").\n\n"
                    + "See the log for details.",
                    "Export packs", DialogButtons.OK, DialogKind.Error);
                return;
            }

            string failed = res.Failed == 0
                ? ""
                : "\n\nNOT included (" + res.Failed + "):\n"
                  + string.Join("\n", res.Members.Where(m => !m.Ok)
                                         .Select(m => "  • " + m.Name + " — " + m.FailedStep));

            await AppDialog.ShowAsync(
                $"Finished exporting!\n\n{res.OkFxPacks} FX pack(s) are in the bundle.\n\n"
                + "On its own this installs packages nothing uses yet — players also need the "
                + "costumes that name these tokens." + failed,
                "Export packs", DialogButtons.OK,
                res.Failed == 0 ? DialogKind.Info : DialogKind.Warning);
        }

        async Task<StorageFile> PickSave(string suggested, string label, string extension)
        {
            var picker = new FileSavePicker();
            picker.SuggestedFileName = suggested;
            picker.FileTypeChoices.Add(label, new List<string> { extension });
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            Window w = App.MainWindowRef;
            if (w == null) return null;
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker, WinRT.Interop.WindowNative.GetWindowHandle(w));

            return await picker.PickSaveFileAsync();
        }

        void FxPickCostume_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not uint enumId) return;

            string dir = AppState.GameDir;
            _fxSel = Installer.ListEffects(dir, FxTables()).FirstOrDefault(c => c.Enum == enumId);
            if (_fxSel == null) return;

            ShowFxDetail();
        }

        void FxBack_Click(object sender, RoutedEventArgs e) => ShowFxList();

        void ShowFxList()
        {
            _fxSel = null;
            FxDetailView.Visibility = Visibility.Collapsed;
            FxListView.Visibility = Visibility.Visible;
            FxCandidateList.ItemsSource = null;
            FxCompatBanner.Visibility = Visibility.Collapsed;
            LoadFxList();
        }

        void ShowFxDetail()
        {
            FxListView.Visibility = Visibility.Collapsed;
            FxDetailView.Visibility = Visibility.Visible;

            string hero = FxCompatibility.HeroOfCostume(_fxSel.DonorClass);
            TxtFxDetailName.Text = _fxSel.DisplayName;
            TxtFxDetailSub.Text = $"enum {_fxSel.Enum}  ·  donor {_fxSel.DonorClass}"
                + (hero != null ? $"  ·  hero {hero}" : "")
                + "  ·  " + (!_fxSel.OptedIn ? "no custom FX yet"
                            : _fxSel.Count == 0 ? "opted in, isolation mode"
                            : $"{_fxSel.Count} effect(s) installed");

            bool isolationOn = _fxSel.OptedIn && _fxSel.Count == 0;
            BtnFxIsolation.Content = isolationOn ? "Undo isolation" : "Isolation test";

            TxtFxDetailHint.Text = isolationOn
                ? "ISOLATION MODE is on: this costume has an empty effects list, so it carries its "
                  + "own forged asset with nothing redirected. Equip it in game — if it still "
                  + "renders correctly the forged id works and effects can be added."
                : "Select the folder holding this mod's effect UPKs. Every file is checked against "
                  + "the stock package and against this costume's hero before anything is installed.";

            FxCandidateList.ItemsSource = null;
            FxCompatBanner.Visibility = Visibility.Collapsed;
            _fxCands = new List<FxCandidate>();
            _fxCandRows = new List<FxCandidateRow>();
            _fxScan = null;
            BtnFxInstall.Visibility = Visibility.Collapsed;
            BtnFxRemoveAll.Visibility = (_fxSel.OptedIn && _fxSel.Count > 0)
                ? Visibility.Visible : Visibility.Collapsed;

            FxScanCache.Scan sc = null;
            try { sc = FxScanCache.Get(_fxSel.Enum); } catch { }
            if (sc != null && sc.Items.Count > 0) ShowFxCachedScan(sc);

            LoadFxPacks();
        }

        void SyncFxScanVisibility()
        {
            BtnFxScan.Visibility = string.IsNullOrWhiteSpace(AppState.PickedUpk)
                ? Visibility.Collapsed : Visibility.Visible;
        }

        void RefreshFxRows()
        {
            FxCandidateList.ItemsSource = null;
            FxCandidateList.ItemsSource = _fxCandRows;
            UpdateFxButtons();
        }

        void UpdateFxButtons()
        {
            bool any = _fxCandRows != null && _fxCandRows.Count > 0;
            BtnFxInstall.Visibility = any && _fxCandRows.Any(x => x.CanSelect)
                ? Visibility.Visible : Visibility.Collapsed;
            BtnFxTickNone.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            BtnFxTickSuggested.Visibility = any && _fxCandRows.Any(x => x.Suggested)
                ? Visibility.Visible : Visibility.Collapsed;

            if (!any) return;
            int add = _fxCandRows.Count(r => r.Selected && !r.Installed && r.CanSelect);
            int drop = _fxCandRows.Count(r => !r.Selected && r.Installed);
            BtnFxInstall.Content = (add == 0 && drop == 0)
                ? "Apply changes"
                : $"Apply  (+{add} / −{drop})";
        }

        void FxTickNone_Click(object sender, RoutedEventArgs e)
        {
            if (_fxCandRows == null) return;
            foreach (FxCandidateRow r in _fxCandRows) r.Selected = false;
            RefreshFxRows();
        }

        void FxTickInstalled_Click(object sender, RoutedEventArgs e)
        {
            if (_fxCandRows == null) return;
            foreach (FxCandidateRow r in _fxCandRows) r.Selected = r.Installed;
            RefreshFxRows();
        }

        void FxTickSuggested_Click(object sender, RoutedEventArgs e)
        {
            if (_fxCandRows == null) return;
            foreach (FxCandidateRow r in _fxCandRows) r.Selected = r.Installed || r.Suggested;
            RefreshFxRows();
        }

        void MarkInstalledRows(List<FxCandidateRow> rows)
        {
            if (_fxSel == null || _fxSel.Effects == null) return;
            foreach (FxCandidateRow r in rows)
            {
                Installer.InstalledEffect hit = _fxSel.Effects.FirstOrDefault(
                    x => r.From != 0 && x.From == r.From);
                if (hit == null) continue;
                r.Installed = true;
                r.Selected = true;
                r.CanSelect = true;
                r.Package = hit.Package;
                r.StateText = hit.UpkExists ? "installed" : "INSTALLED, UPK MISSING";
                if (!hit.UpkExists)
                    r.WarnText = "the package file is gone - this costume will not arm until you "
                               + "untick it and apply, or use Prune missing FX";
            }
        }

        void ApplySiblingPolicy(List<FxCandidateRow> rows,
                                List<(string primary, List<string> exports, ulong from)> src,
                                List<string> coveredStock)
        {
            for (int n = 0; n < rows.Count && n < src.Count; n++)
            {
                var probe = new FxCandidate
                {
                    ClassLeaf = src[n].primary,
                    AllClassExports = src[n].exports ?? new List<string>(),
                };
                EffectRecord rec = null;
                if (src[n].from != 0) FxTables().ByAssetId.TryGetValue(src[n].from, out rec);

                FxSiblingDecision d = FxSiblingPolicy.Evaluate(probe, rec, coveredStock);
                rows[n].SiblingOffered = d.Offerable;
                rows[n].SiblingText = d.Headline;
                rows[n].SiblingTooltip = d.Tooltip;
            }
        }

        void ShowFxCachedScan(FxScanCache.Scan scan)
        {

            var coveredStock = scan.Items
                .Where(i => i.Installable && !string.IsNullOrEmpty(i.StockStem))
                .Select(i => "UC__" + i.StockStem + "_SF.upk")
                .ToList();

            var rows = scan.Items.Select(i => new FxCandidateRow
            {
                Title = i.EffectName ?? i.StockStem,
                FileName = i.File,
                Detail = (i.Kind ?? "?") + "  ·  from " + (i.FromHex ?? "?")
                       + (i.Bytes > 0 ? "  ·  " + (i.Bytes / 1024) + " KB" : ""),
                StateText = i.Installable ? "available" : "skipped",
                WarnText = string.Equals(i.Compat, "Mismatch", StringComparison.OrdinalIgnoreCase)
                    ? "⚠ wrong hero? " + i.CompatReason
                    : (!i.Installable ? i.SkipReason : null),
                Selected = false,
                CanSelect = i.Installable,
                From = i.From,
            }).ToList();

            ApplySiblingPolicy(rows,
                scan.Items.Select(i => (i.PrimaryClass, i.ClassExports, i.From)).ToList(),
                coveredStock);

            MarkInstalledRows(rows);

            _fxScan = scan;
            _fxCands = new List<FxCandidate>();
            _fxCandRows = rows;
            FxCandidateList.ItemsSource = null;
            FxCandidateList.ItemsSource = rows;
            UpdateFxButtons();

            int ok = rows.Count(r => r.CanSelect);
            int have = rows.Count(r => r.Installed);
            string when = scan.ScannedUtc;
            try { when = DateTime.Parse(scan.ScannedUtc).ToLocalTime().ToString("g"); } catch { }

            TxtFxDetailHint.Text = $"{scan.Items.Count} file(s) from a scan of {scan.Folder} ({when})  ·  "
                + $"{ok} available  ·  {have} installed. Tick to add, untick to remove, then Apply. "
                + "Add a few at a time - every installed package loads in one burst on equip.";

            if (scan.MissingSources > 0)
            {
                FxCompatBanner.Visibility = Visibility.Visible;
                TxtFxCompat.Text = $"⚠ {scan.MissingSources} source file(s) from that folder are no "
                    + "longer there. Those cannot be installed until you re-scan from wherever the "
                    + "mod now lives. Already-installed effects are unaffected.";
            }
            else FxCompatBanner.Visibility = Visibility.Collapsed;
        }

        void ShowFxCandidates(List<FxCandidate> cands, string hero)
        {
            var rows = cands.Select(c => new FxCandidateRow
            {
                Title = c.Known ? c.Record.Name : c.StockStem,
                FileName = c.FileName,
                Detail = c.Known
                    ? $"{c.Record.Kind}  ·  from {c.Record.AssetIdHex}"
                      + (c.Diff != null ? "  ·  " + c.Diff.Summary : "")
                    : "not in Effects.json",
                StateText = c.Installable ? "will install" : "skipped",
                WarnText = c.CompatWarn
                    ? "⚠ wrong hero? " + c.CompatReason + " — it would install but this costume's "
                      + "hero never resolves it"
                    : (!c.Installable ? c.SkipReason : null),

                Selected = false,
                CanSelect = c.Installable,
                From = c.FromAsset,
                Suggested = c.Selected && c.Installable,
            }).ToList();

            ApplySiblingPolicy(rows,
                cands.Select(c => (c.ClassLeaf, c.AllClassExports, c.FromAsset)).ToList(),
                cands.Where(c => c.Installable && !string.IsNullOrEmpty(c.StockStem))
                     .Select(c => "UC__" + c.StockStem + "_SF.upk").ToList());

            MarkInstalledRows(rows);

            _fxCands = cands;
            _fxCandRows = rows;
            FxCandidateList.ItemsSource = null;
            FxCandidateList.ItemsSource = rows;
            UpdateFxButtons();

            if (_fxSel != null && cands.Count > 0 && !string.IsNullOrWhiteSpace(_fxScanFolder))
            {
                try
                {
                    _fxScan = FxScanCache.FromCandidates(_fxSel.Enum, _fxScanFolder, cands);
                    FxScanCache.Save(_fxScan);
                    AppState.Log($"  [fx] scan cached ({cands.Count} file(s)) - it will be here next time");
                }
                catch (Exception ex) { AppState.Log("  ⚠ could not cache the scan: " + ex.Message); }
            }

            int ok = cands.Count(c => c.Installable);
            int bad = cands.Count(c => c.CompatWarn);
            long bytes = cands.Where(c => c.Installable && c.Selected)
                              .Sum(c => { try { return new FileInfo(c.SourcePath).Length; } catch { return 0L; } });

            TxtFxDetailHint.Text = $"{cands.Count} file(s) examined  ·  {ok} installable  ·  "
                + $"~{bytes / 1048576.0:0.0} MB if all ticked. Every installed package loads when "
                + "the costume is equipped, so this is memory cost per costume.";

            if (bad > 0)
            {
                FxCompatBanner.Visibility = Visibility.Visible;
                TxtFxCompat.Text = $"⚠ {bad} of {cands.Count} file(s) do not look like they belong to "
                    + $"\"{hero}\". They are unticked. This is a heuristic — shared projectiles are "
                    + "sometimes attributed to another hero in the effect table — so tick them if you "
                    + "know better.";
            }
            else
            {
                FxCompatBanner.Visibility = Visibility.Collapsed;
            }
        }

        public sealed class FxPackRow
        {
            public string Token { get; set; }
            public string DisplayName { get; set; }
            public string SubLabel { get; set; }
            public string SearchBlob { get; set; }
            public override string ToString() => DisplayName ?? Token;
        }

        List<FxPackRow> _packRows = new List<FxPackRow>();

        bool _packListLoading;

        void LoadFxPacks()
        {
            if (_fxSel == null) { FxPackCard.Visibility = Visibility.Collapsed; return; }
            FxPackCard.Visibility = Visibility.Visible;

            List<FxPack> packs;
            try { packs = Installer.PacksForCostume(_fxSel.DonorClass); }
            catch (Exception ex) { AppState.Log("packs: " + ex.Message); packs = new List<FxPack>(); }

            string assigned = AssignedPackToken(_fxSel.Enum);

            _packRows = packs.Select(p =>
            {
                int users = 0;
                try { users = FxPackRegistry.UsedBy(AppState.GameDir, p.Token).Count; } catch { }
                return new FxPackRow
                {
                    Token = p.Token,
                    DisplayName = (p.DisplayName ?? p.Token)
                                + (string.Equals(p.Token, assigned, StringComparison.OrdinalIgnoreCase)
                                   ? "   (in use here)" : ""),
                    SubLabel = $"{p.Effects.Count} effect(s)  ·  used by {users} costume(s)"
                             + (string.IsNullOrWhiteSpace(p.Hero) ? "" : "  ·  " + p.Hero),
                    SearchBlob = string.Join(" ", p.Token, p.DisplayName, p.Hero).ToLowerInvariant(),
                };
            }).ToList();

            ApplyPackSearch(assigned);

            string hero = FxCompatibility.HeroOfCostume(_fxSel.DonorClass);
            TxtFxPackState.Text = assigned != null
                ? "Using pack \"" + assigned + "\". Its packages are shared - stopping deletes nothing."
                : (_fxSel.OptedIn && _fxSel.Count > 0
                    ? "This costume has its own effects, not a shared pack. Remove them first to use one."
                    : "Pick a pack to give this costume its effects. Nothing is written to disk - the "
                    + "packages already exist.");

            TxtNoPacks.Visibility = _packRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_packRows.Count == 0)
                TxtNoPacks.Text = "No FX packs installed for " + (hero ?? "this hero")
                                + " yet. Use \"Scan folder…\" to install one.";

            BtnFxUnassign.Visibility = assigned != null ? Visibility.Visible : Visibility.Collapsed;
            UpdatePackButtons();
        }

        void ApplyPackSearch(string keepToken)
        {
            string q = TxtFxPackSearch?.Text;
            List<FxPackRow> shown = _packRows.Where(r => MatchesSearch(r.SearchBlob, q)).ToList();

            _packListLoading = true;
            ListFxPacks.ItemsSource = shown;
            if (keepToken != null)
            {
                FxPackRow hit = shown.FirstOrDefault(
                    r => string.Equals(r.Token, keepToken, StringComparison.OrdinalIgnoreCase));
                if (hit != null) ListFxPacks.SelectedItem = hit;
            }
            _packListLoading = false;
            UpdatePackButtons();
        }

        void FxPackSearch_Changed(object sender, TextChangedEventArgs e)
            => ApplyPackSearch((ListFxPacks.SelectedItem as FxPackRow)?.Token);

        void FxPack_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_packListLoading) return;
            UpdatePackButtons();
        }

        void UpdatePackButtons()
        {
            bool picked = ListFxPacks.SelectedItem is FxPackRow;
            BtnFxAssign.IsEnabled = picked;
            BtnFxDeletePack.IsEnabled = picked;
        }

        static string AssignedPackToken(uint enumId)
        {
            try
            {
                string jsonPath = CostumeLibrary.CustomCostumesJson(AppState.GameDir);
                if (!CostumeConfig.Exists(jsonPath)) return null;
                if (System.Text.Json.Nodes.JsonNode.Parse(CostumeConfig.ReadAllText(jsonPath))
                        is not System.Text.Json.Nodes.JsonObject root) return null;
                foreach (string key in new[] { "costumes", "disabled" })
                {
                    if (root[key] is not System.Text.Json.Nodes.JsonArray arr) continue;
                    foreach (var n in arr)
                        if (n is System.Text.Json.Nodes.JsonObject o
                            && (o["enum"]?.GetValue<uint>() ?? 0) == enumId)
                            return (string)o["fxPack"];
                }
            }
            catch { }
            return null;
        }

        async void FxAssign_Click(object sender, RoutedEventArgs e)
        {
            if (_fxSel == null || ListFxPacks.SelectedItem is not FxPackRow row) return;

            DialogResult ok = await AppDialog.ShowAsync(
                $"Give \"{_fxSel.DisplayName}\" the effects from \"{row.DisplayName}\"?\n\n"
                + "Nothing is written to disk - the packages already exist and are shared with "
                + "any other costume using this pack. Restart the client and the server to see it.",
                "Use FX pack", DialogButtons.OKCancel, DialogKind.Info,
                primaryText: "Use it", closeText: "Cancel");
            if (ok != DialogResult.OK) return;

            string dir = AppState.GameDir, serverDir = AppState.ServerDir;
            uint enumId = _fxSel.Enum;
            EffectTables tables = FxTables();

            Installer.UninstallResult r = await Task.Run(
                () => Installer.AssignFxPack(dir, enumId, row.Token, tables, AppState.Logger, serverDir));

            foreach (string s in r.Steps) AppState.Log("  " + s);
            if (!r.Ok)
            {
                await AppDialog.ShowAsync(r.Error ?? "failed", "Use FX pack",
                    DialogButtons.OK, DialogKind.Error);
                return;
            }

            _fxSel = Installer.ListEffects(dir, tables).FirstOrDefault(c => c.Enum == enumId);
            if (_fxSel != null) ShowFxDetail(); else ShowFxList();
        }

        async void FxUnassign_Click(object sender, RoutedEventArgs e)
        {
            if (_fxSel == null) return;
            DialogResult ok = await AppDialog.ShowAsync(
                $"Stop \"{_fxSel.DisplayName}\" using its FX pack?\n\n"
                + "It goes back to the donor's stock effects. No file is deleted - the pack "
                + "keeps its packages for any other costume using it.",
                "Stop using pack", DialogButtons.OKCancel, DialogKind.Info,
                primaryText: "Stop using", closeText: "Cancel");
            if (ok != DialogResult.OK) return;

            string dir = AppState.GameDir, serverDir = AppState.ServerDir;
            uint enumId = _fxSel.Enum;
            EffectTables tables = FxTables();

            Installer.UninstallResult r = await Task.Run(
                () => Installer.UnassignFxPack(dir, enumId, tables, AppState.Logger, serverDir));
            foreach (string s in r.Steps) AppState.Log("  " + s);
            if (!r.Ok)
            { await AppDialog.ShowAsync(r.Error ?? "failed", "Stop using pack", DialogButtons.OK, DialogKind.Error); return; }

            _fxSel = Installer.ListEffects(dir, tables).FirstOrDefault(c => c.Enum == enumId);
            if (_fxSel != null) ShowFxDetail(); else ShowFxList();
        }

        async void FxExportPack_Click(object sender, RoutedEventArgs e)
        {
            if (ListFxPacks.SelectedItem is not FxPackRow row) return;

            string dir = AppState.GameDir;
            if (string.IsNullOrWhiteSpace(dir))
            {
                await AppDialog.ShowAsync("Pick your game folder on the Settings tab first.",
                    "Export FX pack", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            var picker = new FileSavePicker();
            picker.SuggestedFileName = row.Token;
            picker.FileTypeChoices.Add("FX pack", new List<string> { FxPackFile.Extension });
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            Window w = App.MainWindowRef;
            if (w == null) return;
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker, WinRT.Interop.WindowNative.GetWindowHandle(w));

            StorageFile file = await picker.PickSaveFileAsync();
            if (file == null) return;

            AppState.Log("");
            AppState.Log($"── exporting FX pack {row.DisplayName} ──");

            Installer.UninstallResult res = await Task.Run(
                () => Installer.ExportFxPack(dir, row.Token, file.Path, AppState.Logger));

            foreach (string s in res.Steps) AppState.Log("  " + s);

            if (!res.Ok)
            {
                await AppDialog.ShowAsync(
                    res.Error ?? "export failed", "Export FX pack",
                    DialogButtons.OK, DialogKind.Error);
                return;
            }

            await AppDialog.ShowAsync(
                $"Exported \"{row.DisplayName}\".\n\n"
                + "Players need this AND the costume's own .mhcostume - export that from the "
                + "Manage tab. Either order works: importing the costume first installs it with "
                + "stock effects, and importing this pack turns them on.\n\n"
                + "One FX pack covers every costume of this hero, so it only ships once.",
                "Export FX pack", DialogButtons.OK, DialogKind.Info);
        }

        async void FxDeletePack_Click(object sender, RoutedEventArgs e)
        {
            if (ListFxPacks.SelectedItem is not FxPackRow row) return;

            string dir = AppState.GameDir;
            List<FxPackRegistry.PackUser> users;
            try { users = FxPackRegistry.UsedBy(dir, row.Token); }
            catch { users = new List<FxPackRegistry.PackUser>(); }

            if (users.Count > 0)
            {
                await AppDialog.ShowAsync(
                    $"\"{row.DisplayName}\" is still used by:\n    "
                    + string.Join("\n    ", users.Select(u => u.DisplayName))
                    + "\n\nStop using it there first. Deleting its packages now would stop those "
                    + "costumes arming at all - the donor would render instead of stock effects.",
                    "Delete FX pack", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            DialogResult ok = await AppDialog.ShowAsync(
                $"Delete \"{row.DisplayName}\" and its effect packages?\n\n"
                + "No costume is using it. The renamed UPKs are removed from CookedPCConsole; "
                + "stock game packages are never touched.",
                "Delete FX pack", DialogButtons.OKCancel, DialogKind.Warning,
                primaryText: "Delete", closeText: "Keep");
            if (ok != DialogResult.OK) return;

            EffectTables tables = FxTables();
            Installer.UninstallResult r = await Task.Run(
                () => Installer.DeleteFxPack(dir, row.Token, tables, AppState.Logger));
            foreach (string s in r.Steps) AppState.Log("  " + s);
            if (!r.Ok)
                await AppDialog.ShowAsync(r.Error ?? "failed", "Delete FX pack",
                    DialogButtons.OK, DialogKind.Error);

            LoadFxPacks();
        }

        async Task<string> PickFolderAsync(string commitText)
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add("*");
            picker.CommitButtonText = commitText;

            Window w = App.MainWindowRef;
            if (w == null) return null;
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker, WinRT.Interop.WindowNative.GetWindowHandle(w));

            StorageFolder f = await picker.PickSingleFolderAsync();
            return f?.Path;
        }

        async void FxSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_fxSel == null) return;

            EffectTables tables = FxTables();
            if (tables.IsEmpty)
            {
                await AppDialog.ShowAsync("Effects.json was not found next to CostumeManager.exe, so effect "
                    + "packages cannot be identified.\n\nRebuild it with pak\\buildeffects.py.",
                    "Select folder", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            string folder = await PickFolderAsync("Scan this folder");
            if (folder == null) return;

            string dir = AppState.GameDir;
            string cooked = GamePaths.Resolve(dir).cooked;
            string hero = FxCompatibility.HeroOfCostume(_fxSel.DonorClass);
            string selName = _fxSel.DisplayName;

            BtnFxSelectFolder.IsEnabled = false;
            List<FxCandidate> cands;
            try
            {
                AppState.Log("");
                AppState.Log($"[fx] scanning for \"{selName}\" (hero {hero ?? "?"}): {folder}");
                _fxScanFolder = folder;
                cands = await Task.Run(() => FxScanner.ScanAsync(folder, tables, cooked, AppState.Logger, hero));
            }
            catch (Exception ex)
            {
                await AppDialog.ShowAsync(ex.Message, "Select folder", DialogButtons.OK, DialogKind.Error);
                return;
            }
            finally { BtnFxSelectFolder.IsEnabled = true; }

            ShowFxCandidates(cands, hero);
        }

        async void FxScanFolder_Click(object sender, RoutedEventArgs e)
        {
            string dir = AppState.GameDir;
            if (!GamePaths.LooksLikeGameFolder(dir))
            {
                await AppDialog.ShowAsync("Pick your game folder first (Settings tab).",
                    "Scan FX folder", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            EffectTables tables = FxTables();
            if (tables.IsEmpty)
            {
                await AppDialog.ShowAsync("Effects.json was not found next to CostumeManager.exe, so effect "
                    + "packages cannot be identified.\n\nRebuild it with pak\\buildeffects.py.",
                    "Scan FX folder", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            string folder = await PickFolderAsync("Scan this folder");
            if (folder == null) return;

            string cooked = GamePaths.Resolve(dir).cooked;

            BtnFxScan.IsEnabled = false;
            List<FxCandidate> cands;
            try
            {
                AppState.Log("");
                AppState.Log("[fx scan] " + folder);
                _fxScanFolder = folder;
                cands = await Task.Run(() => FxScanner.ScanAsync(folder, tables, cooked, AppState.Logger));
            }
            catch (Exception ex)
            {
                await AppDialog.ShowAsync(ex.Message, "Scan FX folder", DialogButtons.OK, DialogKind.Error);
                return;
            }
            finally { BtnFxScan.IsEnabled = true; }

            int ok = cands.Count(c => c.Installable);
            int same = cands.Count(c => c.IdenticalToStock);
            int unknown = cands.Count(c => !c.Known);
            long bytes = cands.Where(c => c.Installable)
                              .Sum(c => { try { return new FileInfo(c.SourcePath).Length; } catch { return 0L; } });

            await AppDialog.ShowAsync(
                $"{cands.Count} effect-shaped file(s) examined.\n\n"
                + $"  {ok} would install  (~{bytes / 1048576.0:0.0} MB)\n"
                + $"  {same} identical to stock - skipped\n"
                + $"  {unknown} not a known stock effect - skipped\n\n"
                + "Every installed package is loaded when the costume is equipped, so this is "
                + "memory cost per costume. Full detail is in the log.",
                "Scan FX folder", DialogButtons.OK, DialogKind.Info);
        }

        async void FxInstall_Click(object sender, RoutedEventArgs e)
        {
            if (_fxSel == null || _fxCandRows.Count == 0) return;

            var addRows = _fxCandRows.Where(r => r.Selected && !r.Installed && r.CanSelect).ToList();
            var dropRows = _fxCandRows.Where(r => !r.Selected && r.Installed).ToList();

            if (addRows.Count == 0 && dropRows.Count == 0)
            {
                await AppDialog.ShowAsync("Nothing changed - the ticks already match what is installed.",
                    "Apply effects", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            var siblingOptIn = new HashSet<string>(
                addRows.Where(r => r.SiblingOffered && r.SiblingEnabled).Select(r => r.FileName),
                StringComparer.OrdinalIgnoreCase);

            var byFile = new Dictionary<string, FxCandidate>(StringComparer.OrdinalIgnoreCase);
            foreach (FxCandidate c in _fxCands) byFile[c.FileName] = c;
            var pick = new List<FxCandidate>();
            var needRehydrate = new List<string>();
            foreach (FxCandidateRow r in addRows)
            {
                if (byFile.TryGetValue(r.FileName, out FxCandidate c) && c.Installable) pick.Add(c);
                else needRehydrate.Add(r.FileName);
            }

            long bytes = pick.Sum(c => { try { return new FileInfo(c.SourcePath).Length; } catch { return 0L; } });
            int wrongHero = pick.Count(c => c.CompatWarn);
            int already = _fxCandRows.Count(r => r.Selected && r.Installed);

            var ask = new System.Text.StringBuilder();
            ask.AppendLine($"Apply effect changes for \"{_fxSel.DisplayName}\"?");
            ask.AppendLine();
            if (addRows.Count > 0)
                ask.AppendLine($"  + install {addRows.Count}   (~{bytes / 1048576.0:0.0} MB written)");
            if (dropRows.Count > 0)
                ask.AppendLine($"  − remove  {dropRows.Count}   (their UPKs are deleted)");
            ask.AppendLine($"  = keep    {already}");
            ask.AppendLine();
            int total = already + addRows.Count;
            ask.AppendLine($"This costume would end up with {total} effect(s).");
            if (total > 8)
                ask.AppendLine("\n⚠ Every effect loads in one burst when the costume is equipped. "
                             + "Large chains have crashed the client at load; add a few at a time.");
            if (wrongHero > 0)
                ask.AppendLine($"\n⚠ {wrongHero} do not look like they belong to this costume's hero.");
            ask.Append("\nStock effect packages are never modified.");

            if (await AppDialog.ShowAsync(ask.ToString(), "Apply effects",
                    DialogButtons.OKCancel, DialogKind.Info) != DialogResult.OK) return;

            string dir = AppState.GameDir;
            string serverDir = AppState.ServerDir;
            uint enumId = _fxSel.Enum;
            EffectTables tables = FxTables();
            string cooked = GamePaths.Resolve(dir).cooked;
            string hero = FxCompatibility.HeroOfCostume(_fxSel.DonorClass);
            var dropPkgs = dropRows.Select(r => r.Package).Where(p => !string.IsNullOrEmpty(p)).ToList();
            var rehydrate = needRehydrate;
            FxScanCache.Scan scan = _fxScan;

            BtnFxInstall.IsEnabled = false;
            var lines = new List<string>();
            try
            {

                foreach (string pkg in dropPkgs)
                {
                    Installer.UninstallResult rm = await Task.Run(
                        () => Installer.RemoveEffects(dir, enumId, pkg, tables, AppState.Logger));
                    lines.Add((rm.Ok ? "removed " : "REMOVE FAILED ") + pkg);
                    if (!rm.Ok && rm.Error != null) lines.Add("   " + rm.Error);
                }

                if (rehydrate.Count > 0 && scan != null)
                {

                    List<FxCandidate> extra = await Task.Run(
                        () => FxScanCache.RehydrateAsync(scan, rehydrate, tables, cooked, AppState.Logger, hero));
                    pick.AddRange(extra.Where(c => c.Installable));
                }

                if (siblingOptIn.Count > 0)
                    foreach (FxCandidate c in pick)
                        c.SiblingRenameOptIn = c != null && c.FileName != null
                                            && siblingOptIn.Contains(c.FileName);

                if (pick.Count > 0)
                {
                    Installer.UninstallResult r = await Task.Run(() => Installer.AddEffectsAsync(
                        dir, enumId, pick, tables, AppState.Logger, serverDir));
                    lines.AddRange(r.Ok ? r.Steps : new List<string> { r.Error ?? "install failed" });
                }
            }
            catch (Exception ex)
            {
                await AppDialog.ShowAsync(ex.Message, "Apply effects", DialogButtons.OK, DialogKind.Error);
                return;
            }
            finally { BtnFxInstall.IsEnabled = true; }

            await AppDialog.ShowAsync(string.Join("\n", lines), "Apply effects",
                DialogButtons.OK, DialogKind.Info);

            _fxSel = Installer.ListEffects(dir, FxTables()).FirstOrDefault(c => c.Enum == enumId);
            if (_fxSel != null) ShowFxDetail(); else ShowFxList();
        }

        async void FxRemoveAll_Click(object sender, RoutedEventArgs e)
        {
            if (_fxSel == null || !_fxSel.OptedIn) return;
            string dir = AppState.GameDir;
            uint enumId = _fxSel.Enum;

            if (await AppDialog.ShowAsync(
                    $"Remove all {_fxSel.Count} effect(s) from \"{_fxSel.DisplayName}\"?\n\n"
                    + "Their renamed UPKs are deleted and the costume goes back to its donor's "
                    + "stock effects. Stock packages are never touched.",
                    "Remove effects", DialogButtons.OKCancel, DialogKind.Info) != DialogResult.OK) return;

            EffectTables tables = FxTables();
            Installer.UninstallResult r = await Task.Run(
                () => Installer.RemoveEffects(dir, enumId, null, tables, AppState.Logger));

            await AppDialog.ShowAsync(r.Ok ? string.Join("\n", r.Steps) : (r.Error ?? "failed"),
                "Remove effects", DialogButtons.OK, r.Ok ? DialogKind.Info : DialogKind.Error);

            _fxSel = Installer.ListEffects(dir, tables).FirstOrDefault(c => c.Enum == enumId);
            if (_fxSel != null) ShowFxDetail(); else ShowFxList();
        }

        async void FxPrune_Click(object sender, RoutedEventArgs e)
        {
            string dir = AppState.GameDir;
            if (!GamePaths.LooksLikeGameFolder(dir))
            {
                await AppDialog.ShowAsync("Pick your game folder first (Settings tab).",
                    "Prune missing FX", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            EffectTables tables = FxTables();
            Installer.UninstallResult r = await Task.Run(
                () => Installer.PruneMissingEffects(dir, tables, AppState.Logger));

            await AppDialog.ShowAsync(r.Ok ? string.Join("\n", r.Steps) : (r.Error ?? "failed"),
                "Prune missing FX", DialogButtons.OK, r.Ok ? DialogKind.Info : DialogKind.Error);
            LoadFxList();
        }

        async void FxSyncHotspots_Click(object sender, RoutedEventArgs e)
        {
            string dir = AppState.GameDir;
            string serverDir = AppState.ServerDir;

            if (!GamePaths.LooksLikeGameFolder(dir))
            {
                await AppDialog.ShowAsync("Pick your game folder first (Settings tab).",
                    "Sync hotspot ids", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            EffectTables tables = FxTables();
            Installer.UninstallResult r = await Task.Run(
                () => Installer.SyncHotspots(dir, tables, AppState.Logger, serverDir));

            await AppDialog.ShowAsync(r.Ok ? string.Join("\n", r.Steps) : (r.Error ?? "failed"),
                "Sync hotspot ids", DialogButtons.OK, r.Ok ? DialogKind.Info : DialogKind.Error);
            LoadFxList();
        }

        async void FxIsolation_Click(object sender, RoutedEventArgs e)
        {
            if (_fxSel == null) return;
            string dir = AppState.GameDir;
            uint enumId = _fxSel.Enum;

            bool isolationOn = _fxSel.OptedIn && _fxSel.Count == 0;
            bool turningOn = !isolationOn;

            if (turningOn && _fxSel.OptedIn && _fxSel.Count > 0)
            {
                await AppDialog.ShowAsync(
                    $"\"{_fxSel.DisplayName}\" already has {_fxSel.Count} effect(s).\n\n"
                    + "The isolation test writes an EMPTY effects list, which would delete them. "
                    + "Remove the effects first if you really want to run it.",
                    "Isolation test", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            string ask = turningOn
                ? $"Write \"effects\": [] for \"{_fxSel.DisplayName}\"?\n\n"
                  + "This makes the DLL give the costume its OWN forged CostumeUnrealClass with "
                  + "no effect redirected — so the mesh resolves through the forged id instead of "
                  + "the donor's.\n\nPASS = the costume still renders correctly in game.\n"
                  + "FAIL = it renders as the donor, or the client crashes.\n\n"
                  + "The config is backed up first and this is reversible."
                : $"Remove the \"effects\" key from \"{_fxSel.DisplayName}\"?\n\n"
                  + "That puts the costume back on the untouched legacy path.";

            if (await AppDialog.ShowAsync(ask, "Isolation test",
                    DialogButtons.OKCancel, DialogKind.Info) != DialogResult.OK) return;

            Installer.UninstallResult r = Installer.SetIsolation(dir, enumId, turningOn, AppState.Logger);
            if (!r.Ok)
            {
                await AppDialog.ShowAsync(r.Error ?? "failed", "Isolation test",
                    DialogButtons.OK, DialogKind.Error);
                return;
            }

            await AppDialog.ShowAsync(string.Join("\n", r.Steps)
                + (turningOn ? "\n\nLaunch the game, equip the costume, then check CostumeMod.log for:\n"
                             + "  custom FX opt-in -> forged CostumeUnrealClass\n"
                             + "  [H3] matched via FORGED CostumeUnrealClass" : ""),
                "Isolation test", DialogButtons.OK, DialogKind.Info);

            _fxSel = Installer.ListEffects(dir, FxTables()).FirstOrDefault(c => c.Enum == enumId);
            if (_fxSel != null) ShowFxDetail(); else ShowFxList();
        }
    }
}
