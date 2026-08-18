using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using CostumeManager.Core;

namespace CostumeManager.Views
{

    public sealed partial class HomePage : Page
    {
        public HomePage()
        {
            InitializeComponent();
            Refresh();
        }

        void Refresh()
        {
            string dir = AppState.GameDir;
            string server = AppState.ServerDir;

            TxtGameDir.Text = "game    " + (string.IsNullOrWhiteSpace(dir)
                ? "(not set — pick it on Settings)"
                : dir + (GamePaths.LooksLikeGameFolder(dir) ? "" : "   ⛔ does not look like a game folder"));

            TxtServerDir.Text = "server  " + (string.IsNullOrWhiteSpace(server)
                ? "(not set — ServerCostumes.json will not be written)"
                : server);

            try
            {
                var (_, _, bin) = GamePaths.Resolve(dir);
                string cfg = CostumeConfig.ExistingPath(bin);
                TxtConfig.Text = "config  " + (cfg != null && File.Exists(cfg)
                    ? Path.GetFileName(cfg) + "   (" + File.GetLastWriteTime(cfg).ToString("g") + ")"
                    : "(none yet)");
            }
            catch { TxtConfig.Text = "config  (not resolvable until the game folder is set)"; }

            CheckLedger(dir);
            CheckFxPacks(dir);
        }

        void CheckFxPacks(string dir)
        {
            try
            {
                List<FxPack> packs = FxPackRegistry.Read();
                if (packs.Count == 0)
                {
                    BarPacks.IsOpen = true;
                    BarPacks.Severity = InfoBarSeverity.Informational;
                    BarPacks.Message = "No FX packs yet. Installing effects for a costume "
                                     + "creates one, and any other costume of that hero can then use it.";
                    return;
                }

                string cooked = null;
                try { cooked = GamePaths.Resolve(dir).cooked; } catch { }

                var noParents = new List<string>();
                var missing = new List<string>();
                int assignedTotal = 0;

                foreach (FxPack p in packs)
                {
                    int users = 0;
                    try { users = FxPackRegistry.UsedBy(dir, p.Token).Count; } catch { }
                    assignedTotal += users;

                    if ((p.Parents == null || p.Parents.Count == 0) && p.Effects != null
                        && p.Effects.Any(e => (e.Package ?? "").Contains("marvelentity_hotspot")))
                        noParents.Add(p.Token);

                    if (cooked != null && Directory.Exists(cooked) && p.Effects != null)
                    {
                        int gone = p.Effects.Count(
                            e => !string.IsNullOrWhiteSpace(e.Package)
                              && !File.Exists(Path.Combine(cooked, e.Package + ".upk")));
                        if (gone > 0) missing.Add($"{p.Token} ({gone} of {p.Effects.Count} package(s) gone)");
                    }
                }

                BarPacks.IsOpen = true;
                if (noParents.Count == 0 && missing.Count == 0)
                {
                    BarPacks.Severity = InfoBarSeverity.Success;
                    BarPacks.Message = $"{packs.Count} pack(s), {assignedTotal} costume assignment(s). "
                                     + "Every pack has its base packages and all its files.";
                }
                else
                {
                    BarPacks.Severity = InfoBarSeverity.Warning;
                    var lines = new List<string>();
                    if (noParents.Count > 0)
                        lines.Add("⛔ no base packages recorded: " + string.Join(", ", noParents)
                                + " — a costume using one of these can fault the loader on its "
                                + "first hotspot. Re-assign it, which recovers them.");
                    if (missing.Count > 0)
                        lines.Add("⛔ files missing from CookedPCConsole: " + string.Join(", ", missing)
                                + " — a costume using one of these will NOT arm and the donor renders.");
                    BarPacks.Message = string.Join(Environment.NewLine + Environment.NewLine, lines);
                }
            }
            catch (Exception ex)
            {
                BarPacks.IsOpen = true;
                BarPacks.Severity = InfoBarSeverity.Error;
                BarPacks.Message = "Could not read the FX pack registry: " + ex.Message;
            }
        }

        void CheckLedger(string dir)
        {
            try
            {

                string ledgerPath = Path.Combine(AppContext.BaseDirectory, "installed.json");
                List<InstallRecord> ledger = InstallLedger.Read(ledgerPath);

                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                {
                    ListCostumes.ItemsSource = ledger
                        .OrderBy(r => r.Enum)
                        .Select(r => $"{r.Enum}   {r.DisplayName ?? r.Name}")
                        .ToList();
                    TxtListHeader.Text = $"Ledger records ({ledger.Count})";
                    BarLedger.IsOpen = true;
                    BarLedger.Severity = InfoBarSeverity.Informational;
                    BarLedger.Message = "Set the game folder on Settings to check these against "
                                      + "what is actually installed.";
                    return;
                }

                List<InstalledCostume> installed = Installer.ListInstalled(dir);
                var known = new HashSet<uint>(ledger.Select(r => r.Enum));
                var unrecorded = installed.Where(c => !known.Contains(c.Enum)).ToList();

                ListCostumes.ItemsSource = installed
                    .OrderBy(c => c.Enum)
                    .Select(c => $"{c.Enum}   {c.DisplayName}"
                               + (known.Contains(c.Enum) ? "" : "     ⚠ not in this ledger"))
                    .ToList();
                TxtListHeader.Text = $"Installed costumes ({installed.Count})";

                BarLedger.IsOpen = true;
                if (unrecorded.Count == 0)
                {
                    BarLedger.Severity = InfoBarSeverity.Success;
                    BarLedger.Message = $"{installed.Count} installed, all {installed.Count} recorded "
                                      + $"in installed.json ({ledger.Count} record(s) total).";
                }
                else
                {
                    BarLedger.Severity = InfoBarSeverity.Warning;
                    BarLedger.Message =
                        $"{installed.Count - unrecorded.Count} of {installed.Count} installed costume(s) "
                        + $"are recorded here — {unrecorded.Count} are not: "
                        + string.Join(", ", unrecorded.Take(4).Select(c => c.DisplayName))
                        + (unrecorded.Count > 4 ? ", …" : "") + ".\n\n"
                        + "installed.json sits beside this exe and is the only record of the TFC "
                        + "manifest rows each install added. Uninstalling one of those from HERE "
                        + "cannot remove rows it never saw. They were most likely installed by "
                        + "another copy of the Manager — copy its installed.json next to this exe, "
                        + "or uninstall them from the copy that installed them.";
                }
            }
            catch (Exception ex)
            {
                BarLedger.IsOpen = true;
                BarLedger.Severity = InfoBarSeverity.Error;
                BarLedger.Message = "Could not read the ledger: " + ex.Message;
            }
        }
    }
}
