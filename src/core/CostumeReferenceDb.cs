using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.Data.Sqlite;

namespace CostumeManager.Core
{

    public static class CostumeReferenceDb
    {

        public const string DefaultFileName = "costume_reference.db";

        public static HashSet<string> GetDonorExportNames(string dbPath, string donorPkgLeaf)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(dbPath)) return set;

            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT o.leaf_name FROM objects o " +
                "JOIN packages p ON p.pkg_id = o.pkg_id " +
                "WHERE p.pkg_leaf = $leaf COLLATE NOCASE AND o.is_export = 1;";
            var lp = cmd.CreateParameter(); lp.ParameterName = "$leaf"; lp.Value = donorPkgLeaf;
            cmd.Parameters.Add(lp);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string n = r.IsDBNull(0) ? null : r.GetString(0);
                if (!string.IsNullOrEmpty(n)) set.Add(n);
            }
            return set;
        }

        public static string DonorPkgLeafFromClass(string donorClass)
        {
            return "uc__" + (donorClass ?? "").ToLowerInvariant() + "_sf";
        }

        public static bool Exists(string dbPath) => File.Exists(dbPath);

        public static bool HasPackage(string dbPath, string donorPkgLeaf)
        {
            if (!File.Exists(dbPath)) return false;
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM packages WHERE pkg_leaf = $leaf COLLATE NOCASE LIMIT 1;";
            var lp = cmd.CreateParameter(); lp.ParameterName = "$leaf"; lp.Value = donorPkgLeaf;
            cmd.Parameters.Add(lp);
            using var r = cmd.ExecuteReader();
            return r.Read();
        }

        public static int PackageCount(string dbPath)
        {
            if (!File.Exists(dbPath)) return 0;
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM packages;";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }
}
