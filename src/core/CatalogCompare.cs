using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace CostumeManager.Core
{

    public static class CatalogCompare
    {
        public enum Verdict
        {

            Installed,

            Missing,

            Extra,

            Conflict,
        }

        public sealed class Row
        {
            public Verdict Verdict { get; set; }
            public string Name { get; set; }
            public uint Enum { get; set; }
            public string Detail { get; set; }

            public string File { get; set; }
            public override string ToString() => $"{Verdict}: {Name}";
        }

        public sealed class Report
        {
            public List<Row> Costumes { get; } = new List<Row>();
            public List<Row> FxPacks { get; } = new List<Row>();
            public string Error { get; set; }

            public bool Ok => Error == null;
            public int Count(Verdict v) => Costumes.Count(r => r.Verdict == v)
                                         + FxPacks.Count(r => r.Verdict == v);
        }

        public sealed class CatalogCostume
        {

            public string Name { get; set; }

            public string Token { get; set; }

            public uint Enum { get; set; }
            public ulong CustomId { get; set; }
        }

        public sealed class CatalogFxPack
        {
            public string Token { get; set; }
            public string DisplayName { get; set; }
            public int Effects { get; set; }
        }

        public sealed class Catalog
        {
            public List<CatalogCostume> Costumes { get; } = new List<CatalogCostume>();
            public List<CatalogFxPack> FxPacks { get; } = new List<CatalogFxPack>();
        }

        public static Catalog ParseCatalog(string json, out string error)
        {
            error = null;
            try
            {
                if (JsonNode.Parse(json) is not JsonObject root)
                { error = "the server's reply was not a catalog"; return null; }

                var cat = new Catalog();

                if (root["costumes"] is JsonArray cs)
                    foreach (JsonNode n in cs)
                    {
                        if (n is not JsonObject o) continue;
                        cat.Costumes.Add(new CatalogCostume
                        {
                            Name = (string)o["name"],
                            Token = (string)o["token"],
                            Enum = o["enum"]?.GetValue<uint>() ?? 0,

                            CustomId = ParseHex(o["customId"]),
                        });
                    }

                if (root["fxPacks"] is JsonArray fx)
                    foreach (JsonNode n in fx)
                    {
                        if (n is not JsonObject o) continue;
                        string token = (string)o["token"];
                        if (string.IsNullOrWhiteSpace(token)) continue;
                        cat.FxPacks.Add(new CatalogFxPack
                        {
                            Token = token,
                            DisplayName = (string)o["displayName"] ?? token,
                            Effects = o["effects"]?.GetValue<int>() ?? 0,
                        });
                    }

                return cat;
            }
            catch (Exception ex)
            {
                error = "could not read the server's reply: " + ex.Message;
                return null;
            }
        }

        static ulong ParseHex(JsonNode n)
        {
            string s = n?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(s)) return 0;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            return ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out ulong v) ? v : 0;
        }

        public static Report Compare(Catalog catalog, List<InstalledCostume> installed,
                                     List<FxPack> localPacks)
        {
            var report = new Report();

            if (catalog == null)
            {
                report.Error = "no catalog";
                return report;
            }

            installed ??= new List<InstalledCostume>();
            localPacks ??= new List<FxPack>();

            var byEnum = new Dictionary<uint, InstalledCostume>();
            foreach (InstalledCostume c in installed) byEnum[c.Enum] = c;

            var seen = new HashSet<uint>();

            foreach (CatalogCostume s in catalog.Costumes)
            {
                seen.Add(s.Enum);

                if (byEnum.TryGetValue(s.Enum, out InstalledCostume mine) == false)
                {
                    report.Costumes.Add(new Row
                    {
                        Verdict = Verdict.Missing,
                        Name = Readable(s.Name, s.Token ?? ("slot " + s.Enum)),
                        Enum = s.Enum,

                        Detail = string.IsNullOrWhiteSpace(s.Token)
                            ? "This costume is not installed - ask for its .mhcostume file"
                            : $"This costume is not installed - ask for \"{s.Token}.mhcostume\"",
                        File = string.IsNullOrWhiteSpace(s.Token) ? null : s.Token + ".mhcostume",
                    });
                    continue;
                }

                if (s.CustomId != 0 && mine.CustomId != 0 && s.CustomId != mine.CustomId)
                {
                    report.Costumes.Add(new Row
                    {
                        Verdict = Verdict.Conflict,
                        Name = s.Name,
                        Enum = s.Enum,
                        Detail = $"slot {s.Enum} now holds \"{s.Name}\" on the server, but yours is "
                               + $"\"{Readable(mine.DisplayName, "yours")}\". Yours will not work - reinstall it.",
                    });
                    continue;
                }

                report.Costumes.Add(new Row
                {
                    Verdict = Verdict.Installed,
                    Name = Readable(mine.DisplayName, s.Name),
                    Enum = s.Enum,
                });
            }

            foreach (InstalledCostume c in installed)
            {
                if (seen.Contains(c.Enum)) continue;
                report.Costumes.Add(new Row
                {
                    Verdict = Verdict.Extra,
                    Name = c.DisplayName,
                    Enum = c.Enum,
                    Detail = "this server does not have it, so it cannot be equipped here",
                });
            }

            CompareFxPacks(catalog, localPacks, report);

            report.Costumes.Sort((a, b) =>
                string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            report.FxPacks.Sort((a, b) =>
                string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            return report;
        }

        static string Readable(string local, string fallback)
            => string.IsNullOrWhiteSpace(local) ? fallback : local;

        static void CompareFxPacks(Catalog catalog, List<FxPack> localPacks, Report report)
        {
            var mine = new Dictionary<string, FxPack>(StringComparer.OrdinalIgnoreCase);
            foreach (FxPack p in localPacks)
                if (!string.IsNullOrWhiteSpace(p?.Token)) mine[p.Token] = p;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (CatalogFxPack s in catalog.FxPacks)
            {
                seen.Add(s.Token);

                if (mine.TryGetValue(s.Token, out FxPack local) == false)
                {
                    report.FxPacks.Add(new Row
                    {
                        Verdict = Verdict.Missing,
                        Name = s.DisplayName ?? s.Token,
                        Detail = "This effects pack is not installed - costumes using it will "
                               + "show the game's normal effects",
                        File = string.IsNullOrWhiteSpace(s.Token) ? null : s.Token + ".mhfxpack",
                    });
                    continue;
                }

                int localCount = local.Effects?.Count ?? 0;
                if (s.Effects > 0 && localCount > 0 && s.Effects != localCount)
                {
                    report.FxPacks.Add(new Row
                    {
                        Verdict = Verdict.Conflict,
                        Name = s.DisplayName ?? s.Token,
                        Detail = $"the server publishes {s.Effects} effect package(s) and you have "
                               + $"{localCount} - yours is probably an older version",
                    });
                    continue;
                }

                report.FxPacks.Add(new Row
                {
                    Verdict = Verdict.Installed,
                    Name = s.DisplayName ?? s.Token,
                });
            }

            foreach (FxPack p in localPacks)
            {
                if (string.IsNullOrWhiteSpace(p?.Token) || seen.Contains(p.Token)) continue;
                report.FxPacks.Add(new Row
                {
                    Verdict = Verdict.Extra,
                    Name = p.DisplayName ?? p.Token,
                    Detail = "this server does not publish it - harmless, and it may belong to "
                           + "another server you play on",
                });
            }
        }

        public static async System.Threading.Tasks.Task<Report> FetchAndCompareAsync(
            string siteConfigUrl, List<InstalledCostume> installed, List<FxPack> localPacks,
            bool localSideReadable = true)
        {
            var report = new Report();

            if (localSideReadable == false)
            {
                report.Error = "could not find your Marvel Heroes folder, so there is nothing to "
                             + "compare against. Check the game path in Settings.";
                return report;
            }

            (string host, int port) = await LauncherCore.ResolveApiEndpointAsync(siteConfigUrl);
            if (host == null)
            {
                report.Error = "could not work out this server's address - is it online?";
                return report;
            }

            string json;
            try
            {
                using var http = new System.Net.Http.HttpClient
                { Timeout = TimeSpan.FromSeconds(8) };
                json = await http.GetStringAsync($"http://{host}:{port}/CustomCostumes/Catalog");
            }
            catch (Exception)
            {

                report.Error = "this server does not publish a costume list (it may be running an "
                             + "older build), so there is nothing to compare against";
                return report;
            }

            Catalog cat = ParseCatalog(json, out string parseError);
            if (cat == null) { report.Error = parseError; return report; }

            return Compare(cat, installed, localPacks);
        }
    }
}
