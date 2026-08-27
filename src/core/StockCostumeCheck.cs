using System;
using System.IO;
using System.Threading.Tasks;

namespace CostumeManager.Core
{

    public sealed class StockCheckResult
    {

        public string Reason { get; set; }

        public string StockPath { get; set; }

        public bool ContentMatched { get; set; }

        public bool LooksStock => Reason != null;
    }

    public static class StockCostumeCheck
    {

        public static async Task<StockCheckResult> EvaluateAsync(
            string sourceUpkPath, string cookedDir,
            System.Collections.Generic.IEnumerable<string> ourInstalledFileNames = null)
        {
            var r = new StockCheckResult();
            if (string.IsNullOrWhiteSpace(sourceUpkPath) || !File.Exists(sourceUpkPath)) return r;
            if (string.IsNullOrWhiteSpace(cookedDir) || !Directory.Exists(cookedDir)) return r;

            string name = Path.GetFileName(sourceUpkPath);
            string stock = Path.Combine(cookedDir, name);

            if (IsInsideDir(sourceUpkPath, cookedDir))
            {
                r.StockPath = sourceUpkPath;
                r.Reason = "this file is already in the game's CookedPCConsole folder, so it is "
                         + "either stock content or a costume that is already installed. "
                         + "Installing it produces a costume that renders exactly like the one "
                         + "already there.";
                return r;
            }

            if (!File.Exists(stock)) return r;

            bool oursAlready = false;
            if (ourInstalledFileNames != null)
                foreach (string f in ourInstalledFileNames)
                    if (!string.IsNullOrWhiteSpace(f) &&
                        string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase))
                    { oursAlready = true; break; }
            r.StockPath = stock;

            try
            {
                FxDiffReport d = await FxDiff.CompareAsync(sourceUpkPath, stock);
                if (d.StockMissing) return r;

                bool identical = d.EditedObjects.Count == 0
                              && d.ExportsAdded.Count == 0
                              && d.ExportsRemoved.Count == 0
                              && d.NamesAdded.Count == 0
                              && d.NamesRemoved.Count == 0;
                if (identical)
                {
                    r.ContentMatched = true;
                    r.Reason = oursAlready
                        ? $"every export in this UPK is identical to {name}, which is ALREADY "
                          + "INSTALLED in this game folder - so this is a copy of a costume you "
                          + "already have, not new art. Installing it again just duplicates it."
                        : $"every export in this UPK is identical to the game's own {name}, so "
                          + "it is an UNMODIFIED stock costume. It will install cleanly and then "
                          + "render exactly like the stock costume - there is no custom art in it.";
                }
            }
            catch
            {

            }
            return r;
        }

        static bool IsInsideDir(string path, string dir)
        {
            try
            {
                string p = Path.GetFullPath(path);
                string d = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
                return p.StartsWith(d, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
