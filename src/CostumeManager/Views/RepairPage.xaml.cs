using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CostumeManager.Core;

namespace CostumeManager.Views
{

    public sealed partial class RepairPage : Page
    {
        public RepairPage() => InitializeComponent();

        async Task<string> ManifestPathOrNull()
        {
            string dir = AppState.GameDir;
            if (!GamePaths.LooksLikeGameFolder(dir))
            {
                await AppDialog.ShowAsync("Pick your game folder first (Settings tab).",
                    "Repair", DialogButtons.OK, DialogKind.Warning);
                return null;
            }
            var (_, manifest, _) = GamePaths.Resolve(dir);
            return manifest;
        }

        async void ManifestCheck_Click(object sender, RoutedEventArgs e)
        {
            string manifest = await ManifestPathOrNull();
            if (manifest == null) return;

            ManifestReport rep = ManifestDoctor.Check(manifest);

            try
            {
                ManifestDoctor.CheckCustomRows(rep, manifest,
                    Ledger().Select(r => (r.Name, (IEnumerable<string>)r.TfcAliasRows)));
            }
            catch (Exception ex)
            {
                AppState.Log("  (could not cross-check installed costumes: " + ex.Message + ")");
            }

            ManifestProblems.ItemsSource = null;
            ManifestProblems.ItemsSource = rep.Problems;

            TxtManifestResult.Text =
                $"{rep.Entries:N0} rows ({rep.Bytes:N0} bytes). "
                + (rep.PristineEntries > 0
                    ? $"Pristine backup has {rep.PristineEntries:N0}; this install added {rep.Added:N0} "
                      + $"and is missing {rep.LostRows.Count:N0}."
                    : "No pristine backup found to compare against.")
                + (rep.Ok ? "  No problems found." : "");

            BtnManifestRepair.IsEnabled = rep.LostRows.Count > 0 && rep.RoundTrips;

            AppState.Log($"manifest check: {rep.Entries:N0} rows, {rep.LostRows.Count} missing, "
                       + $"{rep.Problems.Count} problem(s)");
            foreach (string r in rep.LostRows.Take(20)) AppState.Log("   MISSING: " + r);
            if (rep.LostRows.Count > 20) AppState.Log($"   ... and {rep.LostRows.Count - 20} more");

            AppState.Log($"   installed-costume alias rows checked: {rep.CustomRowsChecked:N0}, "
                       + $"missing: {rep.MissingCustomRows.Count:N0}");
            foreach (string r in rep.MissingCustomRows.Take(20)) AppState.Log("   CUSTOM ROW MISSING: " + r);
            if (rep.MissingCustomRows.Count > 20)
                AppState.Log($"   ... and {rep.MissingCustomRows.Count - 20} more");
        }

        static List<InstallRecord> Ledger()
            => InstallLedger.Read(Path.Combine(AppContext.BaseDirectory, "installed.json"));

        async void ManifestRepair_Click(object sender, RoutedEventArgs e)
        {
            string manifest = await ManifestPathOrNull();
            if (manifest == null) return;

            DialogResult ok = await AppDialog.ShowAsync(
                "Restore the missing stock rows?\n\n"
                + "This ADDS ONLY — your custom costumes' rows are untouched, and the current "
                + "manifest is backed up first.",
                "Repair manifest", DialogButtons.OKCancel, DialogKind.Warning);
            if (ok != DialogResult.OK) return;

            bool done = await Task.Run(() =>
                ManifestDoctor.Repair(manifest, out int _, AppState.Logger));

            ManifestCheck_Click(sender, e);

            await AppDialog.ShowAsync(
                done ? "Missing stock rows were restored. The previous manifest was backed up."
                     : "Nothing was restored — see the log.",
                "Repair manifest", DialogButtons.OK,
                done ? DialogKind.Info : DialogKind.Warning);
        }

        async void RegenServer_Click(object sender, RoutedEventArgs e)
        {

            string dir = AppState.GameDir;
            string serverDir = AppState.ServerDir;

            if (!GamePaths.LooksLikeGameFolder(dir))
            {
                await AppDialog.ShowAsync("Pick your game folder first (Settings tab).",
                    "Server config", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(serverDir))
            {
                if (await AppDialog.ShowAsync(
                        "No server folder is set (Settings tab), so this will write "
                        + "ServerCostumes.json next to the Costume Manager instead of into your "
                        + "server - where the server will never read it."
                        + Environment.NewLine + Environment.NewLine + "Write it anyway?",
                        "Server config", DialogButtons.OKCancel, DialogKind.Warning,
                        primaryText: "Write anyway", closeText: "Cancel") != DialogResult.OK)
                    return;
            }

            BtnRegenServer.IsEnabled = false;
            try
            {
                Installer.UninstallResult r = await Task.Run(
                    () => Installer.RegenerateServerCostumes(dir, serverDir, AppState.Logger));

                foreach (string step in r.Steps) AppState.Log("  " + step);

                TxtRegenResult.Text = r.Ok
                    ? "Rewritten. Restart the server - it reads this file once, at startup."
                    : "Failed: " + (r.Error ?? "unknown");

                if (!r.Ok)
                    await AppDialog.ShowAsync(r.Error ?? "failed", "Server config",
                        DialogButtons.OK, DialogKind.Error);
            }
            catch (Exception ex)
            {
                TxtRegenResult.Text = "Failed: " + ex.Message;
                AppState.Log("regenerate ServerCostumes.json failed: " + ex.Message);
            }
            finally { BtnRegenServer.IsEnabled = true; }
        }

        async void InspectTokens_Click(object sender, RoutedEventArgs e)
        {

            string serverDir = AppState.ServerDir;
            string gameDir   = AppState.GameDir;

            if (string.IsNullOrWhiteSpace(serverDir))
            {
                await AppDialog.ShowAsync(
                    "Set your server folder on the Settings tab first - that is where "
                    + "Account.db lives.",
                    "Tokens", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            BtnInspectTokens.IsEnabled = false;
            try
            {

                string report = await Task.Run(() =>
                {
                    var known = new List<(string, uint, ulong)>();
                    try
                    {
                        var (_, _, bin) = GamePaths.Resolve(gameDir);
                        string cfg = CostumeConfig.ExistingPath(bin);
                        if (CostumeConfig.Exists(cfg) &&
                            JsonNode.Parse(CostumeConfig.ReadAllText(cfg)) is JsonObject root &&
                            root["costumes"] is JsonArray arr)
                        {
                            foreach (JsonNode n in arr)
                                if (n is JsonObject o && o["enum"] != null)
                                    known.Add(((string)o["name"], o["enum"].GetValue<uint>(),
                                               CostumeLibrary.ParseHex(o["customId"])));
                        }
                    }
                    catch {  }

                    TokenInspection ti = TokenInspector.Inspect(serverDir, known, AppState.Logger);
                    return TokenInspector.Format(ti);
                });

                TxtTokenReport.Text = report;
            }
            catch (Exception ex)
            {
                TxtTokenReport.Text = "Failed: " + ex.Message;
                AppState.Log("token inspection failed: " + ex.Message);
            }
            finally { BtnInspectTokens.IsEnabled = true; }
        }

        async void RebuildChains_Click(object sender, RoutedEventArgs e)
        {
            string dir = AppState.GameDir;
            if (!GamePaths.LooksLikeGameFolder(dir))
            {
                await AppDialog.ShowAsync("Pick your game folder first (Settings tab).",
                    "Load chains", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            BtnRebuildChains.IsEnabled = false;
            try
            {

                int changed = await Task.Run(() => Installer.RebuildChainsAsync(dir, AppState.Logger));
                TxtChainResult.Text = changed == 0
                    ? "Every installed costume's chain is already correct."
                    : $"{changed} costume(s) had their load chain rebuilt. Restart the client.";
                AppState.Log($"rebuild chains: {changed} changed");
            }
            catch (Exception ex)
            {
                TxtChainResult.Text = "Failed: " + ex.Message;
                AppState.Log("rebuild chains failed: " + ex.Message);
            }
            finally { BtnRebuildChains.IsEnabled = true; }
        }

        async void AuditImports_Click(object sender, RoutedEventArgs e)
        {
            string dir = AppState.GameDir;
            if (!GamePaths.LooksLikeGameFolder(dir))
            {
                await AppDialog.ShowAsync("Pick your game folder first (Settings tab).",
                    "Imports", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            BtnAuditImports.IsEnabled = false;
            try
            {
                int flagged = await Task.Run(() => Installer.AuditImportsAsync(dir, AppState.Logger));
                TxtAuditResult.Text = flagged == 0
                    ? "No costume has an orphaned import."
                    : $"{flagged} costume(s) flagged — see the log. A flagged costume is not "
                      + "necessarily broken: an import the mesh never samples is harmless, so "
                      + "check the model in game.";
                AppState.Log($"import audit: {flagged} flagged");
            }
            catch (Exception ex)
            {
                TxtAuditResult.Text = "Failed: " + ex.Message;
                AppState.Log("import audit failed: " + ex.Message);
            }
            finally { BtnAuditImports.IsEnabled = true; }
        }

        async void ScanRestore_Click(object sender, RoutedEventArgs e)
        {
            string dir = AppState.GameDir;
            if (!GamePaths.LooksLikeGameFolder(dir))
            {
                await AppDialog.ShowAsync("Pick your game folder first (Settings tab).",
                    "Restore config", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            var (_, _, bin) = GamePaths.Resolve(dir);
            List<CostumeConfig.RestoreSource> sources;
            try { sources = CostumeConfig.FindRestoreSources(bin); }
            catch (Exception ex)
            {
                TxtRestoreResult.Text = "Scan failed: " + ex.Message;
                return;
            }

            CmbRestoreSource.ItemsSource = sources;
            if (sources.Count > 0) CmbRestoreSource.SelectedIndex = 0;
            BtnRestoreConfig.IsEnabled = sources.Count > 0;

            TxtRestoreResult.Text = sources.Count == 0
                ? "No restore candidates found beside the config."
                : $"{sources.Count} candidate(s). Each shows how many costumes it actually contains, "
                  + "so pick on content rather than filename.";
            AppState.Log($"restore scan: {sources.Count} candidate(s)");
        }

        async void RestoreConfig_Click(object sender, RoutedEventArgs e)
        {
            if (CmbRestoreSource.SelectedItem is not CostumeConfig.RestoreSource src) return;

            DialogResult ok = await AppDialog.ShowAsync(
                $"Rebuild the costume config from:\n\n{src.Label}\n\n"
                + $"It contains {src.Costumes} costume(s). The current config is backed up first.",
                "Restore config", DialogButtons.OKCancel, DialogKind.Warning);
            if (ok != DialogResult.OK) return;

            var (_, _, bin) = GamePaths.Resolve(AppState.GameDir);
            try
            {
                int n = CostumeConfig.RestoreFrom(bin, src.Path);
                TxtRestoreResult.Text = $"Restored {n} costume(s) from {src.Label}.";
                AppState.Log($"restore config: {n} costume(s) from {src.Path}");
                await AppDialog.ShowAsync(
                    $"Restored {n} costume(s).\n\nRestart the client.",
                    "Restore config", DialogButtons.OK);
            }
            catch (Exception ex)
            {
                TxtRestoreResult.Text = "Restore failed: " + ex.Message;
                await AppDialog.ShowAsync("Restore failed:\n\n" + ex.Message,
                    "Restore config", DialogButtons.OK, DialogKind.Error);
            }
        }

        async void VerifyAll_Click(object sender, RoutedEventArgs e)
        {
            string dir = AppState.GameDir;
            if (!GamePaths.LooksLikeGameFolder(dir))
            {
                await AppDialog.ShowAsync("Pick your game folder first (Settings tab).",
                    "Verify", DialogButtons.OK, DialogKind.Warning);
                return;
            }

            BtnVerifyAll.IsEnabled = false;
            try
            {
                DonorTables tables = DonorTables.Load(AppContext.BaseDirectory);
                List<string> problems = await Task.Run(() => Installer.VerifyInstalled(dir, tables));

                AppState.Log("");
                AppState.Log("── verify all ──");
                foreach (string p in problems) AppState.Log("  " + p);

                await AppDialog.ShowAsync(
                    problems.Count == 0
                        ? "Every installed costume checks out."
                        : $"{problems.Count} problem(s) found — see the log.",
                    "Verify", DialogButtons.OK,
                    problems.Count == 0 ? DialogKind.Info : DialogKind.Warning);
            }
            catch (Exception ex)
            {
                AppState.Log("verify failed: " + ex.Message);
                await AppDialog.ShowAsync("Verify failed:\n\n" + ex.Message,
                    "Verify", DialogButtons.OK, DialogKind.Error);
            }
            finally { BtnVerifyAll.IsEnabled = true; }
        }
    }
}
