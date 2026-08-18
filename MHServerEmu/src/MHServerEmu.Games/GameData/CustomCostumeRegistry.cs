using System.Collections.Concurrent;
using System.Net;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.System.Time;

namespace MHServerEmu.Games.GameData
{
    public static class CustomCostumeRegistry
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private static readonly TimeSpan EntryLifetime = TimeSpan.FromHours(12);

        private const int MaxEntries = 1024;

        public const int MaxRefsPerRegistration = 8192;

        private sealed class Entry
        {
            public HashSet<ulong> Refs;
            public TimeSpan Registered;
        }

        private static readonly ConcurrentDictionary<ulong, Entry> _bySession = new();

        private static bool _capacityReported = false;

        public static bool HasRegistrations { get => _bySession.IsEmpty == false; }

        public static int Register(ulong sessionId, ulong[] forgedRefs)
        {
            if (sessionId == 0)
            {
                Logger.Warn("Register(): Session 0 - a failed login ticket carries " +
                            "exactly that, and accepting it would give every failed " +
                            "login the same key");
                return -1;
            }

            if (forgedRefs == null)
                forgedRefs = Array.Empty<ulong>();

            if (forgedRefs.Length > MaxRefsPerRegistration)
            {
                Logger.Warn($"Register(): {forgedRefs.Length} refs from session 0x{sessionId:X} exceeds the cap of {MaxRefsPerRegistration}");
                return -1;
            }

            if (_bySession.ContainsKey(sessionId) == false && _bySession.Count >= MaxEntries)
            {
                Prune();

                if (_bySession.ContainsKey(sessionId) == false && _bySession.Count >= MaxEntries)
                {
                    if (_capacityReported == false)
                    {
                        _capacityReported = true;
                        Logger.Warn($"Register(): At capacity ({MaxEntries} sessions), refusing 0x{sessionId:X}. " +
                                    $"Further refusals will not be reported.");
                    }
                    return -1;
                }
            }

            Entry entry = new()
            {
                Refs = new HashSet<ulong>(forgedRefs),
                Registered = Clock.UnixTime
            };

            _bySession[sessionId] = entry;

            Logger.Info($"Registered {entry.Refs.Count} decodable forged ref(s) for session 0x{sessionId:X}");
            return entry.Refs.Count;
        }

        public static bool CanDecode(ulong sessionId, ulong forgedRef, out bool known)
        {
            known = false;

            if (sessionId == 0)
                return false;

            if (_bySession.TryGetValue(sessionId, out Entry entry) == false)
                return false;

            if (IsExpired(entry))
                return false;       // known stays false, so the flag decides

            known = true;
            return entry.Refs.Contains(forgedRef);
        }

        public static string ExplainDecode(ulong sessionId, ulong forgedRef)
        {
            if (sessionId == 0)
                return "the recipient has no session id - it cannot be matched to any registration";

            if (_bySession.TryGetValue(sessionId, out Entry entry) == false)
                return $"no registration from session 0x{sessionId:X} ({_bySession.Count} session(s) " +
                       "registered) - that client is running a DLL without registration, or its POST " +
                       "never arrived";

            if (IsExpired(entry))
                return $"session 0x{sessionId:X} registered but its entry has EXPIRED";

            if (entry.Refs.Contains(forgedRef))
                return $"session 0x{sessionId:X} holds this id";

            return $"session 0x{sessionId:X} registered {entry.Refs.Count} id(s) and this is not " +
                   "among them - that client does not have this costume's FX pack";
        }

        private static void Prune()
        {
            foreach (var pair in _bySession)
                if (IsExpired(pair.Value))
                    _bySession.TryRemove(pair.Key, out _);
        }

        private static bool IsExpired(Entry entry)
        {
            return Clock.UnixTime - entry.Registered > EntryLifetime;
        }
    }
}
