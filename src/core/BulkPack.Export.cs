using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace CostumeManager.Core
{

    public static partial class BulkPack
    {

        public static BulkResult ExportBundle(string gameRoot, IEnumerable<uint> enums,
                                              string outPath, Action<string> log = null,
                                              string registryPath = null, string title = null)
        {
            var res = new BulkResult();
            List<uint> list = enums?.Distinct().ToList() ?? new List<uint>();

            if (list.Count == 0)
            {
                res.FailedStep = "empty";
                log?.Invoke("Nothing selected to export.");
                return res;
            }

            string temp = Path.Combine(Path.GetTempPath(), "mhbundle-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(temp, CostumesDir));
                Directory.CreateDirectory(Path.Combine(temp, FxDir));

                var info = new Info
                {
                    Format     = Format,
                    CreatedUtc = DateTime.UtcNow.ToString("o"),
                    Title      = title,
                };

                var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                log?.Invoke($"Exporting {list.Count} costume(s)...");
                log?.Invoke("");

                foreach (uint enumId in list)
                {
                    var mr = new MemberResult { Kind = KindCostume, Name = "enum " + enumId };

                    try
                    {

                        string file = Sanitize("costume-" + enumId) + CostumePackFile.Extension;
                        string path = Path.Combine(temp, CostumesDir, file);

                        Installer.PackResult pr = Installer.ExportPack(gameRoot, enumId, path, log);
                        mr.Ok = pr.Ok;
                        mr.FailedStep = pr.FailedStep;

                        if (pr.Ok)
                        {

                            CostumePackInfo cp = CostumePackFile.Read(path, out string err);
                            if (cp == null)
                            {
                                mr.Ok = false;
                                mr.FailedStep = "verify";
                                log?.Invoke($"  exported pack could not be re-read: {err}");
                            }
                            else
                            {
                                mr.Name = cp.DisplayName ?? cp.Name ?? mr.Name;

                                string better = Path.Combine(temp, CostumesDir,
                                    Sanitize(cp.Name ?? cp.DisplayName ?? ("costume-" + enumId))
                                    + CostumePackFile.Extension);
                                if (!string.Equals(better, path, StringComparison.OrdinalIgnoreCase)
                                    && !File.Exists(better))
                                {
                                    File.Move(path, better);
                                    file = Path.GetFileName(better);
                                }

                                info.Members.Add(new Member
                                {
                                    Kind = KindCostume,
                                    File = CostumesDir + file,
                                    Name = mr.Name,
                                });

                                if (!string.IsNullOrWhiteSpace(cp.FxPackToken))
                                    tokens.Add(cp.FxPackToken);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        mr.Ok = false;
                        mr.FailedStep = ex.GetType().Name;
                        log?.Invoke($"  enum {enumId} stopped with an error: {ex.Message}");
                    }

                    res.Members.Add(mr);
                    log?.Invoke("");
                }

                if (tokens.Count > 0)
                {
                    log?.Invoke($"{tokens.Count} FX pack(s) referenced by those costumes:");

                    foreach (string token in tokens.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
                    {
                        var mr = new MemberResult { Kind = KindFx, Name = token };
                        try
                        {
                            string file = Sanitize(token) + FxPackFile.Extension;
                            string path = Path.Combine(temp, FxDir, file);

                            Installer.UninstallResult fr =
                                Installer.ExportFxPack(gameRoot, token, path, log, registryPath);

                            mr.Ok = fr.Ok;
                            mr.FailedStep = fr.Error;

                            if (fr.Ok)
                            {
                                info.Members.Add(new Member
                                {
                                    Kind = KindFx,
                                    File = FxDir + file,
                                    Name = token,
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            mr.Ok = false;
                            mr.FailedStep = ex.GetType().Name;
                            log?.Invoke($"  FX pack \"{token}\" stopped with an error: {ex.Message}");
                        }

                        res.Members.Add(mr);
                    }
                    log?.Invoke("");
                }
                else
                {
                    res.Warnings.Add("none of those costumes is assigned to an FX pack, so the "
                                   + "bundle carries meshes and icons only");
                }

                if (info.Members.Count == 0)
                {
                    res.FailedStep = "export";
                    log?.Invoke("Nothing could be exported - the bundle was not written.");
                    return res;
                }

                File.WriteAllText(Path.Combine(temp, ManifestName), WriteManifestJson(info),
                                  new UTF8Encoding(false));

                if (File.Exists(outPath)) File.Delete(outPath);
                ZipFile.CreateFromDirectory(temp, outPath, CompressionLevel.Optimal, false);

                res.Ok = true;
                res.Steps.Add($"{res.OkCostumes} costume(s) + {res.OkFxPacks} FX pack(s) -> "
                            + Path.GetFileName(outPath));

                var fi = new FileInfo(outPath);
                log?.Invoke($"Bundle written: {Path.GetFileName(outPath)} "
                          + $"({fi.Length / (1024.0 * 1024.0):0.#} MB) - "
                          + $"{res.OkCostumes} costume(s), {res.OkFxPacks} FX pack(s), "
                          + $"{res.Failed} failed.");

                foreach (MemberResult m in res.Members.Where(m => !m.Ok))
                    log?.Invoke($"  NOT INCLUDED: {m.Name} ({m.FailedStep})");

                return res;
            }
            catch (Exception ex)
            {
                res.FailedStep = "bundle";
                log?.Invoke("Export failed: " + ex.Message);
                return res;
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch {  }
            }
        }

        public static BulkResult ExportFxBundle(string gameRoot, IEnumerable<string> tokens,
                                                string outPath, Action<string> log = null,
                                                string registryPath = null, string title = null)
        {
            var res = new BulkResult();
            List<string> list = tokens?.Where(t => !string.IsNullOrWhiteSpace(t))
                                       .Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                                ?? new List<string>();

            if (list.Count == 0)
            {
                res.FailedStep = "empty";
                log?.Invoke("No FX packs selected to export.");
                return res;
            }

            string temp = Path.Combine(Path.GetTempPath(), "mhbundle-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(temp, FxDir));

                var info = new Info
                {
                    Format     = Format,
                    CreatedUtc = DateTime.UtcNow.ToString("o"),
                    Title      = title,
                };

                log?.Invoke($"Exporting {list.Count} FX pack(s)...");

                foreach (string token in list.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
                {
                    var mr = new MemberResult { Kind = KindFx, Name = token };
                    try
                    {
                        string file = Sanitize(token) + FxPackFile.Extension;
                        string path = Path.Combine(temp, FxDir, file);

                        Installer.UninstallResult fr =
                            Installer.ExportFxPack(gameRoot, token, path, log, registryPath);

                        mr.Ok = fr.Ok;
                        mr.FailedStep = fr.Error;

                        if (fr.Ok)
                            info.Members.Add(new Member
                            {
                                Kind = KindFx,
                                File = FxDir + file,
                                Name = token,
                            });
                    }
                    catch (Exception ex)
                    {
                        mr.Ok = false;
                        mr.FailedStep = ex.GetType().Name;
                        log?.Invoke($"  FX pack \"{token}\" stopped with an error: {ex.Message}");
                    }

                    res.Members.Add(mr);
                }

                if (info.Members.Count == 0)
                {
                    res.FailedStep = "export";
                    log?.Invoke("Nothing could be exported - the bundle was not written.");
                    return res;
                }

                File.WriteAllText(Path.Combine(temp, ManifestName), WriteManifestJson(info),
                                  new UTF8Encoding(false));

                if (File.Exists(outPath)) File.Delete(outPath);
                ZipFile.CreateFromDirectory(temp, outPath, CompressionLevel.Optimal, false);

                res.Ok = true;
                res.Steps.Add($"{res.OkFxPacks} FX pack(s) -> " + Path.GetFileName(outPath));

                var fi = new FileInfo(outPath);
                log?.Invoke($"Bundle written: {Path.GetFileName(outPath)} "
                          + $"({fi.Length / (1024.0 * 1024.0):0.#} MB) - "
                          + $"{res.OkFxPacks} FX pack(s), {res.Failed} failed.");
                log?.Invoke("Players also need a costume whose pack token matches, or this "
                          + "installs packages that nothing yet uses.");

                foreach (MemberResult m in res.Members.Where(m => !m.Ok))
                    log?.Invoke($"  NOT INCLUDED: {m.Name} ({m.FailedStep})");

                return res;
            }
            catch (Exception ex)
            {
                res.FailedStep = "bundle";
                log?.Invoke("Export failed: " + ex.Message);
                return res;
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch {  }
            }
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "unnamed";

            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);

            string s = sb.ToString().Trim();
            return s.Length == 0 ? "unnamed" : s;
        }
    }
}
