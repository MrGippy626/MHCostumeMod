using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace CostumeManager.Core
{

    public static class LauncherCore
    {

        public sealed class ServerEntry
        {
            public string Name { get; set; }
            public string SiteConfigUrl { get; set; }
            public override string ToString() => Name ?? SiteConfigUrl ?? "(unnamed)";
        }

        public sealed class Settings
        {

            public string GameExe { get; set; }

            public string DllPath { get; set; }

            public List<ServerEntry> Servers { get; set; } = new List<ServerEntry>();

            public int SelectedServer { get; set; }

            public string SiteConfigUrl
            {
                get
                {
                    if (Servers == null || Servers.Count == 0) return null;
                    int i = SelectedServer;
                    if (i < 0 || i >= Servers.Count) i = 0;
                    return Servers[i]?.SiteConfigUrl;
                }
            }

            public string SelectedName
            {
                get
                {
                    if (Servers == null || Servers.Count == 0) return null;
                    int i = SelectedServer;
                    if (i < 0 || i >= Servers.Count) i = 0;
                    return Servers[i]?.Name;
                }
            }

            public string ExtraArgs { get; set; } = "-robocopy -nosteam";

            public int WindowX { get; set; } = -1;
            public int WindowY { get; set; } = -1;

            public bool SkipMod { get; set; }

            public override string ToString() => GameExe ?? "(not set)";
        }

        public static string SettingsPath =>
            Path.Combine(AppContext.BaseDirectory, "launcher.json");

        public static Settings LoadSettings(string path = null)
        {
            path ??= SettingsPath;
            var s = new Settings();

            try
            {
                if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject o)
                {
                    s.GameExe   = (string)o["gameExe"];
                    s.DllPath   = (string)o["dllPath"];
                    s.ExtraArgs = (string)o["extraArgs"] ?? s.ExtraArgs;
                    s.SkipMod   = o["skipMod"]?.GetValue<bool>() ?? false;
                    s.WindowX   = o["windowX"]?.GetValue<int>() ?? -1;
                    s.WindowY   = o["windowY"]?.GetValue<int>() ?? -1;

                    if (o["servers"] is JsonArray arr)
                        foreach (JsonNode n in arr)
                            if (n is JsonObject e && !string.IsNullOrWhiteSpace((string)e["url"]))
                                s.Servers.Add(new ServerEntry
                                {
                                    Name = (string)e["name"],
                                    SiteConfigUrl = (string)e["url"],
                                });

                    s.SelectedServer = o["selectedServer"]?.GetValue<int>() ?? 0;

                    string legacy = (string)o["siteConfigUrl"];
                    if (s.Servers.Count == 0 && !string.IsNullOrWhiteSpace(legacy))
                        s.Servers.Add(new ServerEntry { Name = "My server", SiteConfigUrl = legacy });
                }
            }
            catch {  }

            if (string.IsNullOrWhiteSpace(s.GameExe))
            {
                string root = GamePaths.AutoDetect();
                if (root != null)
                {
                    var (_, _, bin) = GamePaths.Resolve(root);
                    string exe = Path.Combine(bin, "MarvelHeroesOmega.exe");
                    if (File.Exists(exe)) s.GameExe = exe;
                }
            }

            if (string.IsNullOrWhiteSpace(s.DllPath) || !File.Exists(s.DllPath))
                s.DllPath = FindDllBesideExe() ?? s.DllPath;

            if (s.Servers.Count == 0)
                s.Servers.Add(new ServerEntry { Name = "Local", SiteConfigUrl = "localhost/SiteConfig.xml" });

            return s;
        }

        public const string DllFileName = "MRGIPPY_COSTUME_MOD.dll";

        public const string LegacyDllFileName = "FinalMergedDLL.dll";

        public static string FindDllBesideExe(string dir = null)
        {
            dir ??= AppContext.BaseDirectory;
            foreach (string name in new[] { DllFileName, LegacyDllFileName })
            {
                string p = Path.Combine(dir, name);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        public static void SaveSettings(Settings s, string path = null)
        {
            path ??= SettingsPath;

            var servers = new JsonArray();
            foreach (ServerEntry e in s.Servers ?? new List<ServerEntry>())
            {
                if (string.IsNullOrWhiteSpace(e?.SiteConfigUrl)) continue;
                servers.Add(new JsonObject { ["name"] = e.Name, ["url"] = e.SiteConfigUrl });
            }

            var o = new JsonObject
            {
                ["gameExe"]        = s.GameExe,
                ["dllPath"]        = s.DllPath,
                ["servers"]        = servers,
                ["selectedServer"] = s.SelectedServer,
                ["extraArgs"]      = s.ExtraArgs,
                ["skipMod"]        = s.SkipMod,
                ["windowX"]        = s.WindowX,
                ["windowY"]        = s.WindowY,
            };
            File.WriteAllText(path, o.ToJsonString(JsonOpts));
        }

        static System.Text.Json.JsonSerializerOptions JsonOpts =>
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
            };

        public static List<string> Validate(Settings s)
        {
            var problems = new List<string>();

            if (string.IsNullOrWhiteSpace(s?.GameExe))
                problems.Add("The game has not been chosen yet - pick MarvelHeroesOmega.exe in Settings.");
            else if (!File.Exists(s.GameExe))
                problems.Add("The game is not where the settings say: " + s.GameExe);

            if (s?.SkipMod != true)
            {
                if (string.IsNullOrWhiteSpace(s?.DllPath))
                    problems.Add("The mod file has not been chosen yet - pick the .dll in Settings.");
                else if (!File.Exists(s.DllPath))
                    problems.Add("The mod file is not where the settings say: " + s.DllPath);
                else if (!Is64Bit(s.DllPath, out string archProblem))
                    problems.Add(archProblem);
            }

            if (string.IsNullOrWhiteSpace(s?.SiteConfigUrl))
                problems.Add("No server address - without a SiteConfig URL the game cannot find a "
                           + "server to log in to.");

            return problems;
        }

        public static bool Is64Bit(string path, out string problem)
        {
            problem = null;
            try
            {
                using var fs = File.OpenRead(path);
                using var br = new BinaryReader(fs);

                fs.Position = 0x3C;
                int peOffset = br.ReadInt32();
                fs.Position = peOffset;

                if (br.ReadUInt32() != 0x00004550)
                { problem = Path.GetFileName(path) + " is not a Windows program file."; return false; }

                ushort machine = br.ReadUInt16();
                const ushort IMAGE_FILE_MACHINE_AMD64 = 0x8664;

                if (machine != IMAGE_FILE_MACHINE_AMD64)
                {
                    problem = Path.GetFileName(path) + " is a 32-bit file. The game is 64-bit, so "
                            + "this cannot load into it - rebuild the mod for x64.";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                problem = "Could not read " + Path.GetFileName(path) + ": " + ex.Message;
                return false;
            }
        }

        public static string GameRootFor(Settings s)
        {

            try
            {
                string saved = Path.Combine(AppContext.BaseDirectory, "gamedir.txt");
                if (File.Exists(saved))
                {
                    string dir = File.ReadAllText(saved).Trim();
                    if (GamePaths.LooksLikeGameFolder(dir)) return dir;
                }
            }
            catch { }

            try
            {
                if (!string.IsNullOrWhiteSpace(s?.GameExe))
                {
                    string dir = Path.GetDirectoryName(s.GameExe);
                    for (int i = 0; i < 6 && !string.IsNullOrWhiteSpace(dir); i++)
                    {
                        if (GamePaths.LooksLikeGameFolder(dir)) return dir;
                        string parent = Path.GetDirectoryName(dir);
                        if (parent == dir) break;
                        dir = parent;
                    }
                }
            }
            catch { }

            string found = GamePaths.AutoDetect();
            return GamePaths.LooksLikeGameFolder(found) ? found : null;
        }

        public enum ServerState { Unknown, Checking, Online, Offline }

        public sealed class ServerStatus
        {
            public ServerState State { get; set; }
            public string Detail { get; set; }
            public override string ToString() => State + (Detail == null ? "" : " - " + Detail);
        }

        public static async System.Threading.Tasks.Task<ServerStatus> CheckServerAsync(string siteConfigUrl)
        {
            if (string.IsNullOrWhiteSpace(siteConfigUrl))
                return new ServerStatus { State = ServerState.Unknown, Detail = "no server selected" };

            string url = siteConfigUrl.Trim();
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = "http://" + url;

            string xml;
            try
            {
                using var http = new System.Net.Http.HttpClient
                { Timeout = TimeSpan.FromSeconds(6) };
                xml = await http.GetStringAsync(url);
            }
            catch (Exception)
            {

                return new ServerStatus
                {
                    State = ServerState.Offline,
                    Detail = "cannot reach " + HostOf(url),
                };
            }

            string addr = SiteConfigValue(xml, "AuthServerAddress");
            string port = SiteConfigValue(xml, "AuthServerPort");

            if (string.IsNullOrWhiteSpace(addr) || !int.TryParse(port, out int portN)
                || portN <= 0 || portN > 65535)
                return new ServerStatus
                {
                    State = ServerState.Offline,
                    Detail = "that address answered, but it is not a game server",
                };

            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                var connect = tcp.ConnectAsync(addr, portN);
                var timeout = System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(5));

                if (await System.Threading.Tasks.Task.WhenAny(connect, timeout) == timeout)
                    return new ServerStatus
                    {
                        State = ServerState.Offline,
                        Detail = "the game server at " + addr + " is not responding",
                    };

                await connect;
            }
            catch (Exception)
            {
                return new ServerStatus
                {
                    State = ServerState.Offline,
                    Detail = "the game server at " + addr + " is not running",
                };
            }

            return new ServerStatus { State = ServerState.Online, Detail = addr };
        }

        static string HostOf(string url)
        {
            try { return new Uri(url).Host; } catch { return url; }
        }

        public static async System.Threading.Tasks.Task<(string host, int port)> ResolveApiEndpointAsync(
            string siteConfigUrl)
        {
            if (string.IsNullOrWhiteSpace(siteConfigUrl)) return (null, 0);

            string url = siteConfigUrl.Trim();
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = "http://" + url;

            try
            {
                using var http = new System.Net.Http.HttpClient
                { Timeout = TimeSpan.FromSeconds(6) };
                string xml = await http.GetStringAsync(url);

                string addr = SiteConfigValue(xml, "AuthServerAddress");
                string port = SiteConfigValue(xml, "AuthServerPort");

                if (!string.IsNullOrWhiteSpace(addr) && int.TryParse(port, out int p)
                    && p > 0 && p <= 65535)
                    return (addr, p);
            }
            catch { }

            return (null, 0);
        }

        static string SiteConfigValue(string xml, string field)
        {
            if (string.IsNullOrEmpty(xml)) return null;

            int at = xml.IndexOf("name=\"" + field + "\"", StringComparison.Ordinal);
            if (at < 0) return null;

            int tagEnd = xml.IndexOf('>', at);
            if (tagEnd < 0) return null;

            int val = xml.IndexOf("value=\"", at, StringComparison.Ordinal);
            if (val < 0 || val > tagEnd) return null;

            val += 7;
            int close = xml.IndexOf('"', val);
            if (close < 0 || close > tagEnd) return null;

            return xml.Substring(val, close - val);
        }

        public sealed class LaunchResult
        {
            public bool Ok => Error == null;
            public string Error { get; set; }
            public int ProcessId { get; set; }
            public List<string> Steps { get; } = new List<string>();
        }

        public static string BuildCommandLine(Settings s)
        {
            string args = "\"" + s.GameExe + "\"";

            if (!string.IsNullOrWhiteSpace(s.ExtraArgs))
                args += " " + s.ExtraArgs.Trim();

            if (!string.IsNullOrWhiteSpace(s.SiteConfigUrl))
                args += " -siteconfigurl=" + s.SiteConfigUrl.Trim();

            return args;
        }

        public static LaunchResult Launch(Settings s, Action<string> log = null)
        {
            var res = new LaunchResult();

            List<string> problems = Validate(s);
            if (problems.Count > 0)
            {
                res.Error = string.Join(Environment.NewLine, problems);
                return res;
            }

            string workDir = Path.GetDirectoryName(s.GameExe);
            string cmd = BuildCommandLine(s);
            log?.Invoke("starting " + s.GameExe);
            log?.Invoke("  args: " + cmd);

            var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
            PROCESS_INFORMATION pi;

            var cmdBuf = new System.Text.StringBuilder(cmd);

            if (!CreateProcessW(s.GameExe, cmdBuf, IntPtr.Zero, IntPtr.Zero, false,
                                CREATE_SUSPENDED, IntPtr.Zero, workDir, ref si, out pi))
            {
                res.Error = "Could not start the game (error " + Marshal.GetLastWin32Error() + ").";
                return res;
            }

            res.ProcessId = pi.dwProcessId;
            res.Steps.Add("started suspended, pid " + pi.dwProcessId);
            log?.Invoke("  started suspended, pid " + pi.dwProcessId);

            if (s.SkipMod)
            {
                res.Steps.Add("mod NOT injected - launching the game on its own");
                log?.Invoke("  mod skipped by request - this is a plain, unmodded game");

                if (ResumeThread(pi.hThread) == unchecked((uint)-1))
                    return Fail(res, pi, "Could not resume the game (error "
                                       + Marshal.GetLastWin32Error() + ").");

                CloseHandle(pi.hThread);
                CloseHandle(pi.hProcess);
                return res;
            }

            try
            {
                byte[] dllBytes = System.Text.Encoding.Unicode.GetBytes(s.DllPath + "\0");

                IntPtr mem = VirtualAllocEx(pi.hProcess, IntPtr.Zero, (uint)dllBytes.Length,
                                            MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                if (mem == IntPtr.Zero)
                    return Fail(res, pi, "Could not reserve memory in the game (error "
                                       + Marshal.GetLastWin32Error() + ").");

                if (!WriteProcessMemory(pi.hProcess, mem, dllBytes, (uint)dllBytes.Length, out _))
                    return Fail(res, pi, "Could not write to the game's memory (error "
                                       + Marshal.GetLastWin32Error() + ").");

                IntPtr loadLibrary = GetProcAddress(GetModuleHandleW("kernel32.dll"), "LoadLibraryW");
                if (loadLibrary == IntPtr.Zero)
                    return Fail(res, pi, "Could not find LoadLibraryW.");

                IntPtr th = CreateRemoteThread(pi.hProcess, IntPtr.Zero, 0, loadLibrary, mem, 0, out _);
                if (th == IntPtr.Zero)
                    return Fail(res, pi, "Could not start the injection thread (error "
                                       + Marshal.GetLastWin32Error() + ").");

                WaitForSingleObject(th, 10000);

                GetExitCodeThread(th, out uint moduleBase);
                CloseHandle(th);
                VirtualFreeEx(pi.hProcess, mem, 0, MEM_RELEASE);

                if (moduleBase == 0)
                    return Fail(res, pi,
                        "The mod did not load into the game." + Environment.NewLine
                        + "Everything else worked, so this is usually a missing dependency next "
                        + "to the .dll rather than the .dll itself.");

                res.Steps.Add("injected " + Path.GetFileName(s.DllPath));
                log?.Invoke("  injected " + Path.GetFileName(s.DllPath));

                if (ResumeThread(pi.hThread) == unchecked((uint)-1))
                    return Fail(res, pi, "Could not resume the game (error "
                                       + Marshal.GetLastWin32Error() + ").");

                res.Steps.Add("resumed - hooks are live before the game's entry point");
                log?.Invoke("  resumed. The mod is active before the game starts.");
                return res;
            }
            finally
            {
                if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
                if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
            }
        }

        static LaunchResult Fail(LaunchResult res, PROCESS_INFORMATION pi, string error)
        {

            try { TerminateProcess(pi.hProcess, 1); } catch { }
            res.Error = error;
            return res;
        }

        const uint CREATE_SUSPENDED = 0x00000004;
        const uint MEM_COMMIT = 0x1000, MEM_RESERVE = 0x2000, MEM_RELEASE = 0x8000;
        const uint PAGE_READWRITE = 0x04;

        [StructLayout(LayoutKind.Sequential)]
        struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct STARTUPINFO
        {
            public int cb;
            public IntPtr lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateProcessW(string lpApplicationName,
            System.Text.StringBuilder lpCommandLine, IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags,
            IntPtr lpEnvironment, string lpCurrentDirectory, ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize,
            uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
            byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr GetModuleHandleW(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes,
            uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags,
            out IntPtr lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);
    }
}
