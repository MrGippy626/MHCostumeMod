using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.UI.Dispatching;

namespace CostumeManager
{

    internal static class AppState
    {
        static string Dir => AppContext.BaseDirectory;
        static string GameCfg => Path.Combine(Dir, "gamedir.txt");
        static string ServerCfg => Path.Combine(Dir, "serverdir.txt");

        internal static string GameDir => ReadCfg(GameCfg);
        internal static string ServerDir => ReadCfg(ServerCfg);

        static string ReadCfg(string p)
        {
            try { return File.Exists(p) ? File.ReadAllText(p).Trim() : ""; }
            catch { return ""; }
        }

        internal static void SaveGameDir(string v) => Write(GameCfg, v);
        internal static void SaveServerDir(string v) => Write(ServerCfg, v);

        static void Write(string p, string v)
        {
            try { File.WriteAllText(p, (v ?? "").Trim()); } catch { }
        }

        internal static string PickedUpk { get; set; }

        internal static Dictionary<IconPack.Core.IconRole, string> IconChoices { get; } =
            new Dictionary<IconPack.Core.IconRole, string>();

        internal static bool UseCustomIcons { get; set; }

        internal static void ResetIconChoices()
        {
            IconChoices.Clear();
            UseCustomIcons = false;
        }

        internal static ObservableCollection<string> LogLines { get; } = new ObservableCollection<string>();

        static DispatcherQueue _ui;

        internal static void CaptureDispatcher() => _ui = DispatcherQueue.GetForCurrentThread();

        internal static void Log(string line)
        {
            if (line == null) return;
            string stamped = DateTime.Now.ToString("HH:mm:ss") + "  " + line;

            DispatcherQueue q = _ui;
            if (q == null || q.HasThreadAccess) LogLines.Add(stamped);
            else q.TryEnqueue(() => LogLines.Add(stamped));
        }

        internal static Action<string> Logger => Log;

        internal static void ClearLog()
        {
            DispatcherQueue q = _ui;
            if (q == null || q.HasThreadAccess) LogLines.Clear();
            else q.TryEnqueue(() => LogLines.Clear());
        }
    }
}
