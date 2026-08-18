using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace CostumeManager.Core
{

    public static partial class BulkPack
    {
        public const string Extension = ".mhbundle";
        public const string ManifestName = "bundle.json";
        public const int Format = 1;

        public const string CostumesDir = "costumes/";

        public const string FxDir = "fx/";

        public const string KindCostume = "costume";
        public const string KindFx = "fx";

        public sealed class Member
        {

            public string Kind { get; set; }

            public string File { get; set; }

            public string Name { get; set; }

            public override string ToString() => Name ?? File;
        }

        public sealed class Info
        {
            public int Format { get; set; }
            public string CreatedUtc { get; set; }
            public string Title { get; set; }
            public List<Member> Members { get; set; } = new List<Member>();
        }

        public sealed class BulkProgress
        {

            public int Done { get; set; }

            public int Total { get; set; }

            public string Current { get; set; }

            public string Kind { get; set; }

            public double Fraction => Total <= 0 ? 0 : (double)Done / Total;
        }

        public sealed class MemberResult
        {
            public string Name { get; set; }
            public string Kind { get; set; }
            public bool Ok { get; set; }

            public string FailedStep { get; set; }
        }

        public sealed class BulkResult
        {
            public bool Ok { get; set; }

            public string FailedStep { get; set; }

            public List<MemberResult> Members { get; } = new List<MemberResult>();
            public List<string> Steps { get; } = new List<string>();
            public List<string> Warnings { get; } = new List<string>();

            public int Succeeded => Members.Count(m => m.Ok);
            public int Failed => Members.Count(m => !m.Ok);

            public int Costumes => Members.Count(m => m.Kind == KindCostume);
            public int FxPacks => Members.Count(m => m.Kind == KindFx);

            public int OkCostumes => Members.Count(m => m.Ok && m.Kind == KindCostume);
            public int OkFxPacks => Members.Count(m => m.Ok && m.Kind == KindFx);
        }

        internal static string WriteManifestJson(Info info)
        {
            var arr = new JsonArray();
            foreach (Member m in info.Members)
            {
                arr.Add(new JsonObject
                {
                    ["kind"] = m.Kind,
                    ["file"] = m.File,
                    ["name"] = m.Name,
                });
            }

            var root = new JsonObject
            {
                ["format"]     = info.Format,
                ["createdUtc"] = info.CreatedUtc,
                ["title"]      = info.Title,
                ["members"]    = arr,
            };

            return root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented    = true,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            });
        }

        public static Info ReadManifest(string bundlePath)
        {
            try
            {
                using var zip = ZipFile.OpenRead(bundlePath);
                ZipArchiveEntry e = zip.GetEntry(ManifestName);
                if (e == null) return null;

                using var sr = new StreamReader(e.Open());
                if (JsonNode.Parse(sr.ReadToEnd()) is not JsonObject root) return null;

                var info = new Info
                {
                    Format     = (int?)root["format"] ?? 0,
                    CreatedUtc = (string)root["createdUtc"],
                    Title      = (string)root["title"],
                };

                if (root["members"] is JsonArray members)
                {
                    foreach (JsonNode n in members)
                    {
                        if (n is not JsonObject o) continue;
                        info.Members.Add(new Member
                        {
                            Kind = (string)o["kind"],
                            File = (string)o["file"],
                            Name = (string)o["name"],
                        });
                    }
                }
                return info;
            }
            catch
            {
                return null;
            }
        }

        public static BulkResult ImportBundle(string gameRoot, string source, Action<string> log = null,
                                              Action<BulkProgress> progress = null)
        {
            var res = new BulkResult();

            if (string.IsNullOrWhiteSpace(source))
            {
                res.FailedStep = "source";
                log?.Invoke("No bundle or folder given.");
                return res;
            }

            string temp = null;
            try
            {
                string folder;

                if (Directory.Exists(source))
                {
                    folder = source;
                    log?.Invoke($"Importing from folder \"{source}\"");
                }
                else if (File.Exists(source))
                {
                    temp = Path.Combine(Path.GetTempPath(), "mhbundle-" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        ZipFile.ExtractToDirectory(source, temp);
                    }
                    catch (Exception ex)
                    {
                        res.FailedStep = "extract";
                        log?.Invoke($"Cannot open \"{Path.GetFileName(source)}\": {ex.Message}");
                        return res;
                    }

                    folder = temp;

                    Info manifest = ReadManifest(source);
                    log?.Invoke($"Importing bundle \"{manifest?.Title ?? Path.GetFileName(source)}\"");
                }
                else
                {
                    res.FailedStep = "source";
                    log?.Invoke($"Not found: {source}");
                    return res;
                }

                List<string> costumes = SafeEnumerate(folder, "*" + CostumePackFile.Extension, res, log);
                List<string> fx       = SafeEnumerate(folder, "*" + FxPackFile.Extension, res, log);

                if (costumes.Count == 0 && fx.Count == 0)
                {
                    res.FailedStep = "empty";
                    log?.Invoke($"Nothing to import - no {CostumePackFile.Extension} or "
                              + $"{FxPackFile.Extension} files found.");
                    return res;
                }

                log?.Invoke($"{fx.Count} FX pack(s) and {costumes.Count} costume(s) to import");
                log?.Invoke("");

                int total = fx.Count + costumes.Count, done = 0;

                void Report(string name, string kind)
                    => progress?.Invoke(new BulkProgress
                    {
                        Done = done, Total = total, Current = name, Kind = kind,
                    });

                foreach (string path in fx)
                {
                    Report(Path.GetFileNameWithoutExtension(path), KindFx);
                    res.Members.Add(ImportOne(gameRoot, path, KindFx, log));
                    done++;
                }

                foreach (string path in costumes)
                {
                    Report(Path.GetFileNameWithoutExtension(path), KindCostume);
                    res.Members.Add(ImportOne(gameRoot, path, KindCostume, log));
                    done++;
                }

                Report(null, null);

                res.Ok = true;

                res.Steps.Add($"{res.Succeeded} of {res.Members.Count} imported");
                log?.Invoke("");
                log?.Invoke($"Done: {res.Succeeded} imported, {res.Failed} failed "
                          + $"({res.FxPacks} FX pack(s), {res.Costumes} costume(s)).");

                foreach (MemberResult m in res.Members.Where(m => !m.Ok))
                    log?.Invoke($"  FAILED: {m.Name} ({m.FailedStep})");

                return res;
            }
            finally
            {
                if (temp != null)
                {
                    try { Directory.Delete(temp, true); } catch {  }
                }
            }
        }

        private static MemberResult ImportOne(string gameRoot, string path, string kind,
                                              Action<string> log)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            var mr = new MemberResult { Name = name, Kind = kind };

            log?.Invoke($"-- {(kind == KindFx ? "FX pack" : "costume")}: {name} --");

            try
            {
                if (kind == KindFx)
                {
                    FxPackInstall.Result r = FxPackInstall.Import(gameRoot, path, log);
                    mr.Ok = r.Ok;
                    mr.FailedStep = r.FailedStep;
                }
                else
                {
                    PlayerResult r = PlayerInstall.Import(gameRoot, path, log);
                    mr.Ok = r.Ok;
                    mr.FailedStep = r.FailedStep;
                }
            }
            catch (Exception ex)
            {
                mr.Ok = false;
                mr.FailedStep = ex.GetType().Name;
                log?.Invoke($"  {name} stopped with an error: {ex.Message}");
            }

            log?.Invoke("");
            return mr;
        }

        private static List<string> SafeEnumerate(string folder, string pattern, BulkResult res,
                                                  Action<string> log)
        {
            try
            {
                return Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories)
                                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                                .ToList();
            }
            catch (Exception ex)
            {

                res.Warnings.Add($"could not list {pattern}: {ex.Message}");
                log?.Invoke($"could not list {pattern} in that folder: {ex.Message}");
                return new List<string>();
            }
        }
    }
}
