using MHServerEmu.Core.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;      // the published FX pack list
using System.Text.Json.Serialization;
using MHServerEmu.Games.GameData;
using MHServerEmu.DatabaseAccess;          // IDBManager - purge writes player rows directly
using MHServerEmu.DatabaseAccess.SQLite;   // SQLiteDBManager.PurgeCustomCostume

namespace MHServerEmu.Games.GameData
{
    public static class CustomCostumeLoader
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        public static Dictionary<string, PrototypeId> NameToId { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static Dictionary<PrototypeId, (int enumValue, string displayName, string token)>
            CustomInfo { get; } = new();

        public const long CustomCostumeSkuBase = 52000000;

        public const long DefaultStorePrice = 1000;

        public readonly record struct CustomStoreEntry(PrototypeId CustomId, string DisplayName, long SkuId, long Price);

        public static List<CustomStoreEntry> StoreEntries { get; } = new();

        private static readonly Dictionary<(PrototypeId, PrototypeId), PrototypeId> _hotspotForges = new();

        private static readonly Dictionary<PrototypeId, string> _hotspotForgeOwner = new();

        private static readonly Dictionary<PrototypeId, PrototypeId> _forgedToStock = new();

        private static readonly HashSet<PrototypeId> _substitutedSeen = new();

        private static readonly HashSet<(PrototypeId, PrototypeId)> _hotspotForgeSeen = new();

        public static PrototypeId GetForgedHotspot(PrototypeId customCostumeId, PrototypeId stockHotspotId)
        {
            if (customCostumeId == PrototypeId.Invalid || stockHotspotId == PrototypeId.Invalid)
                return PrototypeId.Invalid;
            if (_hotspotForges.TryGetValue((customCostumeId, stockHotspotId), out PrototypeId forged) == false)
                return PrototypeId.Invalid;

            if (_hotspotForgeSeen.Add((customCostumeId, stockHotspotId)))
                Logger.Info($"Hotspot 0x{(ulong)stockHotspotId:X} summoned as 0x{(ulong)forged:X} " +
                            $"for '{(_hotspotForgeOwner.TryGetValue(forged, out string who) ? who : "?")}'");

            return forged;
        }

        public static bool HasForgedHotspots => _hotspotForges.Count > 0;

        public static PrototypeId GetStockForForged(PrototypeId forgedRef)
        {
            if (forgedRef == PrototypeId.Invalid || _forgedToStock.Count == 0)
                return PrototypeId.Invalid;

            return _forgedToStock.TryGetValue(forgedRef, out PrototypeId stock)
                ? stock
                : PrototypeId.Invalid;
        }

        public static void ReportForgedRefSubstituted(PrototypeId forgedRef, PrototypeId stockRef,
                                                      bool recipientRegistered,
                                                      ulong recipientSessionId = 0)
        {
            if (_substitutedSeen.Add(forgedRef) == false) return;

            string who = _hotspotForgeOwner.TryGetValue(forgedRef, out string owner) ? owner : "?";

            string detail = recipientSessionId == 0
                ? string.Empty
                : "  [" + CustomCostumeRegistry.ExplainDecode(recipientSessionId, (ulong)forgedRef) + "]";

            string why = recipientRegistered
                ? "that client registered but the server did not accept this id" + detail
                : "that client did not register, so the server does not know what it can decode "
                + "and is sending the stock id. Normal for a client without the mod. If a MODDED "
                + "client shows this, look for [REG-FX] in its CostumeMod.log - registration runs "
                + "at login, not at startup";

            Logger.Info($"Forged id 0x{(ulong)forgedRef:X} ('{who}') sent as STOCK " +
                        $"0x{(ulong)stockRef:X} - {why}. Reported once per forged id.");
        }

        private static readonly HashSet<(PrototypeId, PrototypeId)> _unforgedSeen = new();

        private static readonly HashSet<PrototypeId> _adviceShown = new();

        public static void ReportUnforgedHotspot(PrototypeId costumeId, PrototypeId summonedRef,
                                                 PrototypeId powerRef)
        {
            if (costumeId == PrototypeId.Invalid || summonedRef == PrototypeId.Invalid) return;
            if (CustomInfo.ContainsKey(costumeId) == false) return;   // not a custom costume
            if (_unforgedSeen.Add((costumeId, summonedRef)) == false) return;

            string summonedName = GameDatabase.GetPrototypeName(summonedRef);
            bool isHotspot = summonedName != null &&
                             summonedName.IndexOf("Hotspot", StringComparison.OrdinalIgnoreCase) >= 0;

            bool isDefaults = summonedName != null &&
                              summonedName.EndsWith(".defaults", StringComparison.OrdinalIgnoreCase);

            bool first = _adviceShown.Add(costumeId) || isDefaults;

            string kind = isDefaults ? "blueprint default"
                        : isHotspot  ? "hotspot"
                                     : "NON-hotspot entity";

            string advice = !first
                ? "(see the first Summon NOT forged line for what to do)"
                : isDefaults
                ? "*** BLUEPRINT DEFAULT *** shared by every entity of its class - do NOT " +
                  "put it in a \"stock\" field. It means the power leaves its Entity ref unset; " +
                  "there is nothing per-costume to forge here."
                : "If this costume HAS a custom package for it, this is the id to put in its " +
                  "\"stock\" field; if it has none, it correctly renders stock and there is " +
                  "nothing to do.";

            Logger.Warn($"Summon NOT forged for '{CustomInfo[costumeId].displayName}': power " +
                        $"[{GameDatabase.GetPrototypeName(powerRef)}] summons " +
                        $"[{summonedName}] = 0x{(ulong)summonedRef:X16} ({kind}). " +
                        advice);
        }

        private static readonly HashSet<(PrototypeId, PrototypeId)> _forgedSeen = new();

        public static void ReportForgedSummon(PrototypeId costumeId, PrototypeId stockRef,
                                              PrototypeId forgedRef, PrototypeId powerRef)
        {
            if (costumeId == PrototypeId.Invalid || stockRef == PrototypeId.Invalid) return;
            if (CustomInfo.ContainsKey(costumeId) == false) return;
            if (_forgedSeen.Add((costumeId, stockRef)) == false) return;

            Logger.Info($"Summon FORGED for '{CustomInfo[costumeId].displayName}': power " +
                        $"[{GameDatabase.GetPrototypeName(powerRef)}] summons " +
                        $"[{GameDatabase.GetPrototypeName(stockRef)}] = 0x{(ulong)stockRef:X16} " +
                        $"-> 0x{(ulong)forgedRef:X16}. The client must now decode it: look for " +
                        $"[HS5] in CostumeMod.log. No [HS5] means a client/config version skew.");
        }

        private const int CustomEnumFloor = 100000;

        private static readonly Dictionary<(ulong, ulong), ulong> _overrides = new();
        private static readonly object _overrideLock = new();

        private static readonly Dictionary<(ulong, ulong), ulong> _itemStamps = new();

        public static void SetOverride(ulong playerDbId, PrototypeId avatarProtoId, PrototypeId customId)
        {
            lock (_overrideLock)
                _overrides[(playerDbId, (ulong)avatarProtoId)] = (ulong)customId;

            Logger.Info($"CustomCostume override set: player 0x{playerDbId:X} avatar 0x{(ulong)avatarProtoId:X} -> custom 0x{(ulong)customId:X}");
        }

        public static void ClearOverride(ulong playerDbId, PrototypeId avatarProtoId)
        {
            bool removed;
            lock (_overrideLock)
                removed = _overrides.Remove((playerDbId, (ulong)avatarProtoId));

            if (removed)
                Logger.Info($"CustomCostume override cleared: player 0x{playerDbId:X} avatar 0x{(ulong)avatarProtoId:X}");
        }

        public static PrototypeId GetOverride(ulong playerDbId, PrototypeId avatarProtoId)
        {
            lock (_overrideLock)
            {
                if (_overrides.TryGetValue((playerDbId, (ulong)avatarProtoId), out ulong raw))
                {
                    var id = (PrototypeId)raw;
                    if (CustomInfo.ContainsKey(id))
                        return id;
                }
            }
            return PrototypeId.Invalid;
        }

        public static void SetItemStamp(ulong playerDbId, ulong itemDbGuid, PrototypeId customId)
        {
            if (itemDbGuid == 0)
            {
                Logger.Warn($"SetItemStamp: refusing to stamp item with no DbGuid (custom 0x{(ulong)customId:X})");
                return;
            }

            lock (_overrideLock)
                _itemStamps[(playerDbId, itemDbGuid)] = (ulong)customId;

            Logger.Info($"CustomCostume token stamped: player 0x{playerDbId:X} item 0x{itemDbGuid:X} -> custom 0x{(ulong)customId:X}");
        }

        public static PrototypeId GetItemStamp(ulong playerDbId, ulong itemDbGuid)
        {
            if (itemDbGuid == 0)
                return PrototypeId.Invalid;

            lock (_overrideLock)
            {
                if (_itemStamps.TryGetValue((playerDbId, itemDbGuid), out ulong raw))
                {
                    var id = (PrototypeId)raw;
                    if (CustomInfo.ContainsKey(id))
                        return id;
                }
            }
            return PrototypeId.Invalid;
        }

        public static void ClearItemStamp(ulong playerDbId, ulong itemDbGuid)
        {
            bool removed;
            lock (_overrideLock)
                removed = _itemStamps.Remove((playerDbId, itemDbGuid));

            if (removed)
                Logger.Info($"CustomCostume token stamp cleared: player 0x{playerDbId:X} item 0x{itemDbGuid:X}");
        }

        public static string SerializeOverridesForPlayer(ulong playerDbId)
        {
            lock (_overrideLock)
            {
                var parts = _overrides
                    .Where(kvp => kvp.Key.Item1 == playerDbId)
                    .Select(kvp => $"{kvp.Key.Item2:X}:{kvp.Value:X}")
                    .Concat(_itemStamps
                        .Where(kvp => kvp.Key.Item1 == playerDbId)
                        .Select(kvp => $"I{kvp.Key.Item2:X}:{kvp.Value:X}"));

                return string.Join(";", parts);
            }
        }

        public static void LoadOverridesForPlayer(ulong playerDbId, string serialized)
        {
            lock (_overrideLock)
            {
                var stale = _overrides.Keys.Where(k => k.Item1 == playerDbId).ToList();
                foreach (var k in stale) _overrides.Remove(k);

                var staleStamps = _itemStamps.Keys.Where(k => k.Item1 == playerDbId).ToList();
                foreach (var k in staleStamps) _itemStamps.Remove(k);

                if (string.IsNullOrWhiteSpace(serialized))
                    return;

                foreach (var pair in serialized.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    bool isStamp = pair[0] is 'I' or 'i';
                    ReadOnlySpan<char> body = isStamp ? pair.AsSpan(1) : pair.AsSpan();

                    int colon = body.IndexOf(':');
                    if (colon <= 0 || colon >= body.Length - 1)
                        continue;

                    if (ulong.TryParse(body[..colon], System.Globalization.NumberStyles.HexNumber, null, out ulong key) &&
                        ulong.TryParse(body[(colon + 1)..], System.Globalization.NumberStyles.HexNumber, null, out ulong customId))
                    {
                        if (isStamp)
                            _itemStamps[(playerDbId, key)] = customId;
                        else
                            _overrides[(playerDbId, key)] = customId;
                    }
                }
            }
        }

        public static void LoadFromJson(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Logger.Warn($"Custom costume file not found: {filePath}");
                return;
            }

            ServerCostumeFile file;
            try
            {
                var json = File.ReadAllText(filePath);
                file = JsonSerializer.Deserialize<ServerCostumeFile>(json, JsonOpts);
            }
            catch (Exception e)
            {
                Logger.Warn($"Failed to parse {filePath}: {e.Message}");
                return;
            }

            if (file?.Costumes == null || file.Costumes.Count == 0)
            {
                Logger.Warn($"No costumes found in {filePath}");
                return;
            }

            NameToId.Clear();
            CustomInfo.Clear();
            StoreEntries.Clear();

            _hotspotForges.Clear();
            _hotspotForgeOwner.Clear();
            _hotspotForgeSeen.Clear();
            _unforgedSeen.Clear();
            _forgedSeen.Clear();
            _forgedToStock.Clear();
            _substitutedSeen.Clear();

            int loaded = 0;
            foreach (var entry in file.Costumes)
            {
                if (string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.PrototypeId))
                {
                    Logger.Warn($"Skipping costume with missing name/prototypeId: {entry.Name}");
                    continue;
                }

                string hex = entry.PrototypeId.Trim();
                if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    hex = hex.Substring(2);

                if (!ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out ulong rawId))
                {
                    Logger.Warn($"Skipping '{entry.Name}': bad prototypeId '{entry.PrototypeId}'");
                    continue;
                }

                var protoId = (PrototypeId)rawId;
                string display = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.Name : entry.DisplayName;

                PrototypeId donorId = PrototypeId.Invalid;
                if (!string.IsNullOrWhiteSpace(entry.DonorId))
                {
                    string dhex = entry.DonorId.Trim();
                    if (dhex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) dhex = dhex.Substring(2);
                    if (ulong.TryParse(dhex, System.Globalization.NumberStyles.HexNumber, null, out ulong draw))
                        donorId = (PrototypeId)draw;
                }

                NameToId[entry.Name] = protoId;
                CustomInfo[protoId] = (entry.Enum, display, entry.Name);

                long skuId = entry.SkuId > 0 ? entry.SkuId : CustomCostumeSkuBase + entry.Enum;
                long price = entry.StorePrice > 0 ? entry.StorePrice : DefaultStorePrice;
                StoreEntries.Add(new CustomStoreEntry(protoId, display, skuId, price));

                RegisterHotspots(entry, protoId, display);

                DataDirectory.Instance.InjectCustomCostume(protoId, entry.Enum, donorId);

                Logger.Info($"Loaded custom costume: {entry.Name} (0x{rawId:X}) -> enum {entry.Enum}");
                loaded++;
            }

            Logger.Info($"Loaded {loaded} custom costume(s) from {Path.GetFileName(filePath)}");

            LoadFxPackList(Path.Combine(Path.GetDirectoryName(filePath) ?? "", "ServerFxPacks.json"));
            RebuildCatalog();
        }

        public readonly struct CatalogCostume(string name, string token, int enumValue, ulong customId)
        {
            public readonly string Name = name;

            public readonly string Token = token;

            public readonly int Enum = enumValue;
            public readonly ulong CustomId = customId;
        }

        public readonly struct CatalogFxPack(string token, string displayName, string hero, int effects)
        {
            public readonly string Token = token;
            public readonly string DisplayName = displayName;
            public readonly string Hero = hero;
            public readonly int Effects = effects;
        }

        public static CatalogCostume[] Catalog { get; private set; } = Array.Empty<CatalogCostume>();

        public static CatalogFxPack[] FxPackCatalog { get; private set; } = Array.Empty<CatalogFxPack>();

        static void RebuildCatalog()
        {
            var list = new List<CatalogCostume>(CustomInfo.Count);

            foreach (var kv in CustomInfo)
                list.Add(new CatalogCostume(kv.Value.displayName, kv.Value.token,
                                            kv.Value.enumValue, (ulong)kv.Key));

            list.Sort((a, b) => a.Enum.CompareTo(b.Enum));
            Catalog = list.ToArray();
        }

        static void LoadFxPackList(string path)
        {
            FxPackCatalog = Array.Empty<CatalogFxPack>();

            if (string.IsNullOrWhiteSpace(path) || File.Exists(path) == false)
                return;

            try
            {
                if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root) return;
                if (root["packs"] is not JsonArray arr) return;

                var list = new List<CatalogFxPack>();
                foreach (JsonNode n in arr)
                {
                    if (n is not JsonObject o) continue;
                    string token = (string)o["token"];
                    if (string.IsNullOrWhiteSpace(token)) continue;

                    list.Add(new CatalogFxPack(
                        token,
                        (string)o["displayName"] ?? token,
                        (string)o["hero"],
                        o["effects"]?.GetValue<int>() ?? 0));
                }

                FxPackCatalog = list.ToArray();
                Logger.Info($"Loaded {FxPackCatalog.Length} FX pack(s) from {Path.GetFileName(path)}");
            }
            catch (Exception e)
            {
                Logger.Warn($"Could not read {Path.GetFileName(path)}: {e.Message}");
            }
        }

        public const string PendingPurgeFileName = "PendingCostumePurges.json";

        public static void ProcessPendingPurges()
        {
            string path = Path.Combine(AppContext.BaseDirectory, PendingPurgeFileName);
            if (File.Exists(path) == false)
                return;

            List<PendingPurgeEntry> entries;
            try
            {
                entries = JsonSerializer.Deserialize<List<PendingPurgeEntry>>(File.ReadAllText(path), JsonOpts);
            }
            catch (Exception e)
            {
                Logger.ErrorException(e, $"ProcessPendingPurges(): could not read {PendingPurgeFileName} - leaving it in place");
                return;
            }

            if (entries == null || entries.Count == 0)
            {
                File.Delete(path);
                return;
            }

            if (IDBManager.Instance is not SQLiteDBManager sqlite)
            {
                Logger.Warn($"ProcessPendingPurges(): {entries.Count} purge(s) queued but the active DB manager " +
                            $"is not SQLite - skipping. Delete {PendingPurgeFileName} to dismiss.");
                return;
            }

            foreach (PendingPurgeEntry entry in entries)
            {
                if (TryParseHex(entry?.PrototypeId, out ulong customId) == false)
                    continue;

                TryParseHex(entry.DonorId, out ulong donorId);

                bool toDonor = false;

                if (string.Equals(entry.Mode, "donor", StringComparison.OrdinalIgnoreCase))
                    Logger.Warn($"ProcessPendingPurges(): '{entry.Name}' asked for donor conversion, which is " +
                                $"not supported (the item's ItemSpec would still name the deleted custom id) - " +
                                $"deleting the token(s) instead.");

                ulong forgedGuid = DataDirectory.ForgeCustomCostumeGuid(customId);
                ulong donorGuid = 0;
                if (toDonor)
                    donorGuid = (ulong)GameDatabase.GetPrototypeGuid((PrototypeId)donorId);

                if (toDonor && donorGuid == 0)
                {
                    Logger.Warn($"ProcessPendingPurges(): '{entry.Name}' - donor guid unresolved, deleting tokens instead");
                    toDonor = false;
                }

                if (sqlite.PurgeCustomCostume(customId, forgedGuid, toDonor ? donorGuid : 0,
                                              out int items, out int records))
                {
                    Logger.Info($"Purged custom costume '{entry.Name}' (0x{customId:X}, enum {entry.Enum}): " +
                                $"{items} token(s) {(toDonor ? "converted to the donor costume" : "deleted")}, " +
                                $"{records} stamp/override record(s) removed.");
                }
            }

            File.Delete(path);
            Logger.Info($"{PendingPurgeFileName} processed and removed - those enums are now free to reuse.");
        }

        private sealed class PendingPurgeEntry
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("prototypeId")]
            public string PrototypeId { get; set; }

            [JsonPropertyName("donorId")]
            public string DonorId { get; set; }

            [JsonPropertyName("enum")]
            public uint Enum { get; set; }

            [JsonPropertyName("mode")]
            public string Mode { get; set; }
        }

        private static bool TryParseHex(string text, out ulong value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string hex = text.Trim();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex.Substring(2);

            return ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out value);
        }

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        private static void RegisterHotspots(ServerCostumeEntry entry, PrototypeId customId, string display)
        {
            if (entry.Hotspots == null || entry.Hotspots.Count == 0)
                return;

            foreach (ServerHotspotEntry raw in entry.Hotspots)
            {
                if (raw == null) continue;

                if (!TryParseHex(raw.Stock, out ulong stockRaw) || !TryParseHex(raw.Forged, out ulong forgedRaw))
                {
                    Logger.Warn($"'{display}': bad hotspot id pair " +
                                $"(stock '{raw.Stock}', forged '{raw.Forged}') - skipped.");
                    continue;
                }

                var stockId = (PrototypeId)stockRaw;
                var forgedId = (PrototypeId)forgedRaw;

                if (forgedId == stockId)
                {
                    Logger.Warn($"'{display}': forged hotspot id equals the stock id for " +
                                $"0x{stockRaw:X} - skipped.");
                    continue;
                }

                if (raw.Enum < CustomEnumFloor)
                {
                    Logger.Warn($"'{display}': hotspot enum {raw.Enum} is below {CustomEnumFloor} " +
                                "- refusing; the client would resolve it to real content.");
                    continue;
                }

                if (DataDirectory.Instance.InjectCustomHotspot(forgedId, stockId, raw.Enum) == false)
                {
                    Logger.Warn($"'{display}': could not alias hotspot 0x{stockRaw:X} - " +
                                "its FX will render stock.");
                    continue;
                }

                _hotspotForges[(customId, stockId)] = forgedId;
                _hotspotForgeOwner[forgedId] = display;
                _forgedToStock[forgedId] = stockId;
                Logger.Info($"'{display}': hotspot 0x{stockRaw:X} -> forged 0x{(ulong)forgedId:X} " +
                            $"(enum {raw.Enum})");
            }
        }

        private sealed class ServerCostumeFile
        {
            [JsonPropertyName("costumes")]
            public List<ServerCostumeEntry> Costumes { get; set; }

        }

        private sealed class ServerCostumeEntry
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("displayName")]
            public string DisplayName { get; set; }

            [JsonPropertyName("prototypeId")]
            public string PrototypeId { get; set; }

            [JsonPropertyName("donorId")]
            public string DonorId { get; set; }

            [JsonPropertyName("enum")]
            public int Enum { get; set; }

            [JsonPropertyName("storePrice")]
            public long StorePrice { get; set; }

            [JsonPropertyName("skuId")]
            public long SkuId { get; set; }

            [JsonPropertyName("hotspots")]
            public List<ServerHotspotEntry> Hotspots { get; set; }
        }

        private sealed class ServerHotspotEntry
        {
            [JsonPropertyName("stock")]
            public string Stock { get; set; }

            [JsonPropertyName("forged")]
            public string Forged { get; set; }

            [JsonPropertyName("enum")]
            public int Enum { get; set; }
        }
    }
}
