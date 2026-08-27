using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace CostumeManager.Core
{

    public sealed class TokenReport
    {
        public string Name { get; set; }
        public uint Enum { get; set; }
        public ulong CustomId { get; set; }

        public int Overrides { get; set; }

        public int Tokens { get; set; }

        public List<ulong> Players { get; set; } = new List<ulong>();

        public bool Anything => Overrides > 0 || Tokens > 0;
    }

    public sealed class OrphanReport
    {
        public ulong CustomId { get; set; }
        public int Overrides { get; set; }
        public int Tokens { get; set; }
        public List<ulong> Players { get; set; } = new List<ulong>();
    }

    public sealed class TokenInspection
    {
        public string DbPath { get; set; }
        public bool Ok { get; set; }
        public string Error { get; set; }
        public int PlayersScanned { get; set; }
        public List<TokenReport> Costumes { get; set; } = new List<TokenReport>();
        public List<OrphanReport> Orphans { get; set; } = new List<OrphanReport>();
    }

    public static class TokenInspector
    {

        public static string FindDb(string serverDir)
        {
            if (string.IsNullOrWhiteSpace(serverDir)) return null;
            foreach (string rel in new[] { Path.Combine("Data", "Account.db"), "Account.db" })
            {
                string p = Path.Combine(serverDir, rel);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        public static TokenInspection Inspect(string serverDir,
                                              IEnumerable<(string Name, uint Enum, ulong CustomId)> known,
                                              Action<string> log = null)
        {
            var res = new TokenInspection();
            res.DbPath = FindDb(serverDir);
            if (res.DbPath == null)
            {

                res.Error = "Account.db not found under " + (serverDir ?? "(no server folder set)")
                          + " - set the Server folder on the Settings tab.";
                return res;
            }

            var byId = new Dictionary<ulong, TokenReport>();
            foreach (var k in known ?? Enumerable.Empty<(string, uint, ulong)>())
                if (k.CustomId != 0 && !byId.ContainsKey(k.CustomId))
                    byId[k.CustomId] = new TokenReport
                    { Name = k.Name, Enum = k.Enum, CustomId = k.CustomId };

            var orphans = new Dictionary<ulong, OrphanReport>();

            try
            {

                var csb = new SqliteConnectionStringBuilder
                {
                    DataSource = res.DbPath,
                    Mode = SqliteOpenMode.ReadOnly,
                };
                using var con = new SqliteConnection(csb.ToString());
                con.Open();

                using var cmd = con.CreateCommand();
                cmd.CommandText = "SELECT DbGuid, CustomCostumes FROM Player";
                using SqliteDataReader r = cmd.ExecuteReader();
                while (r.Read())
                {
                    res.PlayersScanned++;
                    ulong player = unchecked((ulong)r.GetInt64(0));
                    string raw = r.IsDBNull(1) ? "" : r.GetString(1);
                    if (string.IsNullOrWhiteSpace(raw)) continue;

                    foreach (string part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        int colon = part.LastIndexOf(':');
                        if (colon <= 0 || colon + 1 >= part.Length) continue;

                        string left = part.Substring(0, colon);
                        string right = part.Substring(colon + 1);
                        if (!ulong.TryParse(right, System.Globalization.NumberStyles.HexNumber,
                                            null, out ulong customId) || customId == 0)
                            continue;

                        bool isToken = left.StartsWith("I", StringComparison.OrdinalIgnoreCase);

                        if (byId.TryGetValue(customId, out TokenReport tr))
                        {
                            if (isToken) tr.Tokens++; else tr.Overrides++;
                            if (!tr.Players.Contains(player)) tr.Players.Add(player);
                        }
                        else
                        {

                            if (!orphans.TryGetValue(customId, out OrphanReport o))
                                orphans[customId] = o = new OrphanReport { CustomId = customId };
                            if (isToken) o.Tokens++; else o.Overrides++;
                            if (!o.Players.Contains(player)) o.Players.Add(player);
                        }
                    }
                }

                res.Ok = true;
                res.Costumes = byId.Values.OrderByDescending(t => t.Tokens + t.Overrides)
                                          .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                                          .ToList();
                res.Orphans = orphans.Values.OrderByDescending(o => o.Tokens + o.Overrides).ToList();
                log?.Invoke($"scanned {res.PlayersScanned} player row(s) in {Path.GetFileName(res.DbPath)}");
            }
            catch (Exception ex)
            {
                res.Error = ex.Message;
                log?.Invoke("token inspection failed: " + ex.Message);
            }
            return res;
        }

        public static string Format(TokenInspection i)
        {
            if (i == null) return "(no result)";
            if (!i.Ok) return "Could not read the database: " + (i.Error ?? "unknown error");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{i.DbPath}");
            sb.AppendLine($"{i.PlayersScanned} player row(s) scanned");
            sb.AppendLine();

            var carrying = i.Costumes.Where(c => c.Anything).ToList();
            sb.AppendLine($"Installed costumes with data in the database: {carrying.Count} "
                        + $"of {i.Costumes.Count}");
            foreach (TokenReport c in carrying)
                sb.AppendLine($"   {c.Name}  (enum {c.Enum})  {c.Tokens} token(s), "
                            + $"{c.Overrides} avatar override(s), {c.Players.Count} player(s)");

            var none = i.Costumes.Where(c => !c.Anything).ToList();
            if (none.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Installed but with NO data (never bought or worn): {none.Count}");
                foreach (TokenReport c in none.Take(20))
                    sb.AppendLine($"   {c.Name}  (enum {c.Enum})");
                if (none.Count > 20) sb.AppendLine($"   ... {none.Count - 20} more");
            }

            sb.AppendLine();
            if (i.Orphans.Count == 0)
                sb.AppendLine("No orphaned ids - every id in the database belongs to an installed costume.");
            else
            {
                sb.AppendLine($"ORPHANED ids ({i.Orphans.Count}) - present in the database, but no "
                            + "installed costume claims them.");
                sb.AppendLine("These are costumes removed whose token purge has not run, or has "
                            + "not been queued. Start the server once to run any pending purge.");
                foreach (OrphanReport o in i.Orphans)
                    sb.AppendLine($"   0x{o.CustomId:X16}  {o.Tokens} token(s), "
                                + $"{o.Overrides} override(s), {o.Players.Count} player(s)");
            }
            return sb.ToString();
        }
    }
}
