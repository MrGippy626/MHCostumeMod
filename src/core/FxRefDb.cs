using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace CostumeManager.Core
{

    public static class FxRefDb
    {
        static readonly object Gate = new object();
        static bool _tried;
        static string _path;

        static Dictionary<string, int> _classExporters;

        static Dictionary<string, List<string>> _sharedExporterFiles;

        static Dictionary<string, List<string>> _subclasses;

        public static bool Available { get { EnsureLoaded(); return _classExporters != null; } }
        public static string DbPath { get { EnsureLoaded(); return _path; } }
        public static int KnownClassNames { get { EnsureLoaded(); return _classExporters?.Count ?? 0; } }

        public static void UseDatabase(string path)
        {
            lock (Gate) { _path = path; _tried = false; _classExporters = null; }
        }

        static void EnsureLoaded()
        {
            lock (Gate)
            {
                if (_tried) return;
                _tried = true;
                try
                {
                    string p = _path;
                    if (string.IsNullOrWhiteSpace(p))
                    {
                        string dir = Path.GetDirectoryName(
                            System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
                        p = Path.Combine(dir, "effect_reference.db");
                    }
                    if (!File.Exists(p)) return;

                    var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    using (var conn = new SqliteConnection("Data Source=" + p + ";Mode=ReadOnly"))
                    {
                        conn.Open();
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText =
                            "SELECT p.leaf, COUNT(DISTINCT o.pkg_id) FROM objects o " +
                            "JOIN paths p ON p.path_id = o.path_id " +
                            "WHERE o.kind = 0 GROUP BY p.leaf";
                        using (var r = cmd.ExecuteReader())
                            while (r.Read()) map[r.GetString(0)] = r.GetInt32(1);

                        var files = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                        using var cmd2 = conn.CreateCommand();
                        cmd2.CommandText =
                            "SELECT p.leaf, pk.file_name FROM objects o " +
                            "JOIN paths p ON p.path_id = o.path_id " +
                            "JOIN packages pk ON pk.pkg_id = o.pkg_id " +
                            "WHERE o.kind = 0 AND p.leaf IN (" +
                            "  SELECT p2.leaf FROM objects o2 JOIN paths p2 ON p2.path_id = o2.path_id " +
                            "  WHERE o2.kind = 0 GROUP BY p2.leaf HAVING COUNT(DISTINCT o2.pkg_id) > 1)";
                        using (var r2 = cmd2.ExecuteReader())
                            while (r2.Read())
                            {
                                string leaf = r2.GetString(0), file = r2.GetString(1);
                                if (!files.TryGetValue(leaf, out List<string> lst))
                                    files[leaf] = lst = new List<string>();
                                if (!lst.Contains(file, StringComparer.OrdinalIgnoreCase)) lst.Add(file);
                            }
                        _sharedExporterFiles = files;

                        var subs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                        try
                        {
                            using var cmd3 = conn.CreateCommand();
                            cmd3.CommandText =
                                "SELECT p.path, pk.file_name FROM objects o " +
                                "JOIN paths p ON p.path_id = o.path_id " +
                                "JOIN packages pk ON pk.pkg_id = o.pkg_id " +
                                "WHERE o.kind = 3";
                            using var r3 = cmd3.ExecuteReader();
                            while (r3.Read())
                            {
                                string path = r3.GetString(0), file = r3.GetString(1);
                                if (!subs.TryGetValue(path, out List<string> lst))
                                    subs[path] = lst = new List<string>();
                                if (!lst.Contains(file, StringComparer.OrdinalIgnoreCase)) lst.Add(file);
                            }
                        }
                        catch {  }
                        if (subs.Count > 0) _subclasses = subs;
                    }
                    _path = p;
                    _classExporters = map;
                }
                catch {  }
            }
        }

        public static IReadOnlyList<string> SubclassStubsOf(string classPath)
        {
            EnsureLoaded();
            if (_subclasses == null || string.IsNullOrWhiteSpace(classPath))
                return Array.Empty<string>();
            return _subclasses.TryGetValue(classPath, out List<string> lst)
                ? (IReadOnlyList<string>)lst : Array.Empty<string>();
        }

        public static bool SubclassIndexAvailable
        {
            get { EnsureLoaded(); return _subclasses != null && _subclasses.Count > 0; }
        }

        public static int OfficialClassExporters(string classLeaf)
        {
            EnsureLoaded();
            if (_classExporters == null || string.IsNullOrWhiteSpace(classLeaf)) return -1;
            return _classExporters.TryGetValue(classLeaf, out int n) ? n : 0;
        }

        public static bool IsSharedClassName(string classLeaf, out int exporters, out string why)
        {
            return IsSharedClassName(classLeaf, null, out exporters, out why);
        }

        public static bool IsSharedClassName(string classLeaf, ICollection<string> coveredStockFiles,
                                             out int exporters, out string why)
        {
            exporters = OfficialClassExporters(classLeaf);
            if (exporters < 0)
            {
                bool hardcoded = !string.IsNullOrEmpty(classLeaf) &&
                    classLeaf.StartsWith("powerfxhit", StringComparison.OrdinalIgnoreCase);
                why = hardcoded
                    ? "shared generic class (hardcoded prefix - effect_reference.db not found)"
                    : null;
                return hardcoded;
            }
            if (exporters > 1)
            {
                if (CoveredBy(classLeaf, coveredStockFiles))
                {
                    why = null;
                    return false;
                }
                why = "exported by " + exporters + " official package(s) and this install does "
                    + "not cover them all - renaming only some cannot change what renders";
                return true;
            }
            why = null;
            return false;
        }

        public static bool CoveredBy(string classLeaf, ICollection<string> coveredStockFiles)
        {
            EnsureLoaded();
            if (coveredStockFiles == null || coveredStockFiles.Count == 0) return false;
            if (_sharedExporterFiles == null || string.IsNullOrWhiteSpace(classLeaf)) return false;
            if (!_sharedExporterFiles.TryGetValue(classLeaf, out List<string> files)) return false;
            if (files == null || files.Count == 0) return false;

            var have = new HashSet<string>(coveredStockFiles, StringComparer.OrdinalIgnoreCase);
            foreach (string f in files) if (!have.Contains(f)) return false;
            return true;
        }

        public static IReadOnlyList<string> SharedExporterFiles(string classLeaf)
        {
            EnsureLoaded();
            if (_sharedExporterFiles != null && !string.IsNullOrWhiteSpace(classLeaf) &&
                _sharedExporterFiles.TryGetValue(classLeaf, out List<string> f))
                return f;
            return Array.Empty<string>();
        }

        public static string StatusLine()
        {
            EnsureLoaded();
            return _classExporters != null
                ? "effect reference: " + KnownClassNames.ToString("N0") + " official class name(s) from "
                  + Path.GetFileName(_path)
                : "effect reference: NOT FOUND - falling back to the hardcoded powerfxhit* prefix, "
                  + "which misses powerfxtell*/powerfxparticle_* and other shared classes";
        }
    }
}
