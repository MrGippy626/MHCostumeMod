using Gazillion;
using MHServerEmu.Core.Config;
using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Memory;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Inventories;
using MHServerEmu.Games.Entities.Items;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Loot;
using MHServerEmu.Games.MTXStore.Catalogs;
using MHServerEmu.Games.Social.Communities;

namespace MHServerEmu.Games.MTXStore
{
    public class CatalogManager
    {
        private const int GiftMessageMaxLength = 100;   // matching the client-side limit

        private static readonly Logger Logger = LogManager.CreateLogger();
        private static readonly string MTXStoreDataDirectory = Path.Combine(FileHelper.DataDirectory, "Game", "MTXStore");

        private readonly Catalog _catalog = new();

        private long _giftingOmegaLevelRequired = 0;
        private long _giftingInfinityLevelRequired = 0;
        private ulong _currentGiftId = 1;   // used by the client to differentiate notifications

        public static CatalogManager Instance { get; } = new();

        private CatalogManager() { }

        public bool Initialize()
        {
            if (_catalog.Count != 0)
                return false;

            _catalog.Initialize();
            LoadEntries();

            var config = ConfigManager.Instance.GetConfig<MTXStoreConfig>();
            _giftingOmegaLevelRequired = config.GiftingOmegaLevelRequired;
            _giftingInfinityLevelRequired = config.GiftingInfinityLevelRequired;

            return true;
        }

        public void LoadEntries()
        {
            lock (_catalog)
            {
                _catalog.ClearEntries();

                foreach (string filePath in FileHelper.GetFilesWithPrefix(MTXStoreDataDirectory, "Catalog", "json"))
                {
                    CatalogEntry[] entries = FileHelper.DeserializeJson<CatalogEntry[]>(filePath);
                    _catalog.AddEntries(entries);
                    Logger.Trace($"Parsed catalog entries from {Path.GetFileName(filePath)}");
                }

                AddCustomCostumeEntries();

                Logger.Info($"Loaded {_catalog.Count} store catalog entries");
            }
        }

        private void AddCustomCostumeEntries()
        {
            var storeEntries = CustomCostumeLoader.StoreEntries;
            if (storeEntries.Count == 0)
                return;

            CatalogEntry[] entries = new CatalogEntry[storeEntries.Count];
            for (int i = 0; i < storeEntries.Count; i++)
            {
                var storeEntry = storeEntries[i];
                entries[i] = new CatalogEntry(storeEntry.SkuId, storeEntry.CustomId, storeEntry.DisplayName, storeEntry.Price);
            }

            _catalog.AddEntries(entries);
            Logger.Info($"Added {entries.Length} custom costume token(s) to the store catalog", LogCategory.MTXStore);
        }

        #region Message Handling

        public bool OnGetCatalog(Player player, NetMessageGetCatalog getCatalog)
        {
            // Send the catalog only if the client is out of date.
            TimeSpan clientTimestamp = TimeSpan.FromMicroseconds(getCatalog.TimestampSeconds * 1000000 + getCatalog.TimestampMicroseconds);

            lock (_catalog)
            {
                if (clientTimestamp != _catalog.Timestamp)
                    player.SendMessage(_catalog.ToProtobuf());
            }

            return true;
        }

        public bool OnGetCurrencyBalance(Player player)
        {
            player.SendMessage(NetMessageGetCurrencyBalanceResponse.CreateBuilder()
                .SetCurrencyBalance(player.GazillioniteBalance)
                .Build());

            return true;
        }

        public bool OnBuyItemFromCatalog(Player player, NetMessageBuyItemFromCatalog buyItemFromCatalog)
        {
            if (!Verify.IsTrue(buyItemFromCatalog.HasSkuId, $"No SkuId received from player [{player}]"))
                return false;

            long skuId = buyItemFromCatalog.SkuId;
            long clientPrice = buyItemFromCatalog.ItemUnitPrice;

            // In normal non-gift purchases the buyer is the recipient
            BuyItemResultErrorCodes result = BuyItem(player, player, skuId, clientPrice);

            player.SendMessage(NetMessageBuyItemFromCatalogResponse.CreateBuilder()
                .SetDidSucceed(result == BuyItemResultErrorCodes.BUY_RESULT_ERROR_SUCCESS)
#if GAME_VERSION_1_53
                .SetBalance(NetMessageGetCurrencyBalanceResponse.CreateBuilder()
                    .SetCurrencyBalance(player.GazillioniteBalance))
#else
                .SetCurrentCurrencyBalance(player.GazillioniteBalance)
#endif
                .SetErrorcode(result)
                .SetSkuId(skuId)
                .Build());

            return true;
        }

        public bool OnBuyGiftForOtherPlayer(Player buyer, NetMessageBuyGiftForOtherPlayer buyGiftForOtherPlayer)
        {
            if (!Verify.IsTrue(buyGiftForOtherPlayer.HasSkuId, $"No SkuId received from player [{buyer}]"))
                return false;

            long skuId = buyGiftForOtherPlayer.SkuId;
            long clientPrice = buyGiftForOtherPlayer.ItemUnitPrice;
            string recipientName = buyGiftForOtherPlayer.RecipientName;
            string giftMessage = buyGiftForOtherPlayer.HasGiftMessage ? buyGiftForOtherPlayer.GiftMessage : null;

            Game game = buyer.Game;

            if (game.GiftingEnabled == false || buyer.IsGiftingAllowed() == false)
            {
                SendBuyGiftForOtherPlayerResponse(buyer, skuId, BuyItemResultErrorCodes.BUY_RESULT_ERROR_GIFTING_UNAVAILABLE);
                return false;
            }

            // CUSTOM: Check Omega/Infinity level requirement
#if GAME_VERSION_1_52 || GAME_VERSION_1_53
            if (game.InfinitySystemEnabled)
            {
                if (_giftingInfinityLevelRequired > 0 && buyer.GetTotalInfinityPoints() < _giftingInfinityLevelRequired)
                {
                    SendBuyGiftForOtherPlayerResponse(buyer, skuId, BuyItemResultErrorCodes.BUY_RESULT_ERROR_GIFTING_UNAVAILABLE);
                    game.ChatManager.SendChatFromCustomSystem(buyer, $"Infinity level {_giftingInfinityLevelRequired} is required to send gifts.");
                    return false;
                }
            }
            else
#endif
            {
#if !GAME_VERSION_1_53
                if (_giftingOmegaLevelRequired > 0 && buyer.GetOmegaPoints() < _giftingOmegaLevelRequired)
                {
                    SendBuyGiftForOtherPlayerResponse(buyer, skuId, BuyItemResultErrorCodes.BUY_RESULT_ERROR_GIFTING_UNAVAILABLE);
                    game.ChatManager.SendChatFromCustomSystem(buyer, $"Omega level {_giftingOmegaLevelRequired} is required to send gifts.");
                    return false;
                }
#endif
            }

            if (giftMessage != null && giftMessage.Length > GiftMessageMaxLength)
            {
                SendBuyGiftForOtherPlayerResponse(buyer, skuId, BuyItemResultErrorCodes.BUY_RESULT_ERROR_GIFT_MESSAGE_TOO_LONG);
                return false;
            }

            // Currently we allow only local synchronous gifts to nearby players.
            Player recipient = game.EntityManager.GetPlayerByName(recipientName);

            Community community = buyer.Community;
            CommunityMember recipientMember = community.GetMemberByName(recipientName);
            CommunityCircle nearbyCircle = community.GetCircle(CircleId.__Nearby);

            if (recipient == null || recipientMember == null || recipientMember.IsInCircle(nearbyCircle) == false)
            {
                SendBuyGiftForOtherPlayerResponse(buyer, skuId, BuyItemResultErrorCodes.BUY_RESULT_ERROR_UNKNOWN_RECIPIENT);
                game.ChatManager.SendChatFromCustomSystem(buyer, $"Player {recipientName} not found. You must be near the recipient player to send gifts.");
                return false;
            }

            if (recipient == buyer)
            {
                SendBuyGiftForOtherPlayerResponse(buyer, skuId, BuyItemResultErrorCodes.BUY_RESULT_ERROR_UNKNOWN);
                game.ChatManager.SendChatFromCustomSystem(buyer, $"You cannot purchase gifts for yourself.");
                return false;
            }

            // All good, do the purchase.
            BuyItemResultErrorCodes result = BuyItem(buyer, recipient, skuId, clientPrice);
            SendBuyGiftForOtherPlayerResponse(buyer, skuId, result);

            // Notify the recipient if successful.
            if (result == BuyItemResultErrorCodes.BUY_RESULT_ERROR_SUCCESS)
            {
                var giftNotification = NetMessageReceivedGift.CreateBuilder()
                    .SetSkuId((ulong)skuId)
                    .SetTransId(Interlocked.Increment(ref _currentGiftId))
                    .SetSender(buyer.GetName());

                if (giftMessage != null)
                    giftNotification.SetMessage(giftMessage);

                recipient.SendMessage(giftNotification.Build());
            }

            return true;
        }

        private static void SendBuyGiftForOtherPlayerResponse(Player buyer, long skuId, BuyItemResultErrorCodes result)
        {
            buyer.SendMessage(NetMessageBuyGiftForOtherPlayerResponse.CreateBuilder()
                .SetDidSucceed(result == BuyItemResultErrorCodes.BUY_RESULT_ERROR_SUCCESS)
#if GAME_VERSION_1_53
                .SetBalance(NetMessageGetCurrencyBalanceResponse.CreateBuilder()
                    .SetCurrencyBalance(buyer.GazillioniteBalance))
#else
                .SetCurrentCurrencyBalance(buyer.GazillioniteBalance)
#endif
                .SetErrorcode(result)
                .SetSkuid(skuId)
                .Build());
        }

        #endregion

        private static bool IsCustomCostumeSku(long skuId)
        {
            return skuId >= CustomCostumeLoader.CustomCostumeSkuBase;
        }

        private BuyItemResultErrorCodes BuyItem(Player buyer, Player recipient, long skuId, long clientPrice)
        {
            var config = buyer.Game.CustomGameOptions;

            // Do not allow purchases during the tutorial if we are going to unlock avatars or team-ups after the player finishes it.
            if ((config.AutoUnlockAvatars || config.AutoUnlockTeamUps) && buyer.HasFinishedTutorial() == false)
                return BuyItemResultErrorCodes.BUY_RESULT_ERROR_UNKNOWN;

            // Validate the order
            CatalogEntry entry = null;

            lock (_catalog)
                entry = _catalog.GetEntry(skuId);

            if (entry == null || entry.GuidItems.IsNullOrEmpty() || entry.LocalizedEntries.IsNullOrEmpty())
                return BuyItemResultErrorCodes.BUY_RESULT_ERROR_UNKNOWN;

            if (IsCustomCostumeSku(skuId) && recipient.PlayerConnection?.CustomCostumesEnabled != true)
            {
                Game game = buyer.Game;

                game.ChatManager.SendChatFromCustomSystem(recipient,
                    "Custom costumes require the client mod. Once it is installed, run \"!player costumes enable\".");

                if (buyer != recipient)
                    game.ChatManager.SendChatFromCustomSystem(buyer,
                        $"{recipient.GetName()} does not have custom costumes enabled - gift refused.");

                Logger.Warn($"BuyItem(): refused custom SKU {skuId} for [{recipient}] - mod not enabled");
                return BuyItemResultErrorCodes.BUY_RESULT_ERROR_UNKNOWN;
            }

            LocalizedCatalogEntry localizedEntry = entry.LocalizedEntries[0];
            long itemPrice = localizedEntry.ItemPrice;

            // Do not allow the purchase if the price changed since the client requested it.
            if (clientPrice != itemPrice)
                return BuyItemResultErrorCodes.BUY_RESULT_ERROR_PRICE_MISMATCH;

            long balance = buyer.GazillioniteBalance;
            if (itemPrice > balance)
                return BuyItemResultErrorCodes.BUY_RESULT_ERROR_INSUFFICIENT_BALANCE;

            if (entry.GuidItems.Length == 1)
            {
                // For individual purchases it's all or nothing with early out if failed to fulfill.
                BuyItemResultErrorCodes result = AcquireCatalogGuid(recipient, entry.GuidItems[0], false);
                if (result != BuyItemResultErrorCodes.BUY_RESULT_ERROR_SUCCESS)
                    return result;
            }
            else
            {
                // Allow partial fulfillment of bundles (e.g. stash already owned)
                foreach (CatalogGuidEntry catalogItemEntry in entry.GuidItems)
                {
                    BuyItemResultErrorCodes result = AcquireCatalogGuid(recipient, catalogItemEntry, true);
                    switch (result)
                    {
                        case BuyItemResultErrorCodes.BUY_RESULT_ERROR_SUCCESS:
                        case BuyItemResultErrorCodes.BUY_RESULT_ERROR_ALREADY_HAVE_STASH_INV:
                        case BuyItemResultErrorCodes.BUY_RESULT_ERROR_ALREADY_HAVE_PERMABUFF:
                            // this is fine
                            break;

                        default:
                            // this is not fine
                            Logger.Warn($"BuyItem(): Partial fulfillment of sku! skuId={skuId}, entry={catalogItemEntry}, buyer=[{buyer}], recipient=[{recipient}]", LogCategory.MTXStore);
                            break;
                    }
                }
            }

            // Adjust currency balance (do not allow negative balance in case somebody figures out some kind of exploit to get here)
            balance = Math.Max(balance - itemPrice, 0);
            buyer.GazillioniteBalance = balance;
            Logger.Trace($"OnBuyItemFromCatalog(): Player [{buyer}] purchased [skuId={skuId}, itemPrice={itemPrice}] for recipient [{recipient}]. Balance={balance}", LogCategory.MTXStore);

            return BuyItemResultErrorCodes.BUY_RESULT_ERROR_SUCCESS;
        }

        private static BuyItemResultErrorCodes AcquireCatalogGuid(Player player, CatalogGuidEntry guidEntry, bool allowTokenReplacements)
        {
            PrototypeId catalogProtoRef = guidEntry.ItemPrototypeRuntimeIdForClient;

            if (CustomCostumeLoader.CustomInfo.ContainsKey(catalogProtoRef))
            {
                for (int i = 0; i < guidEntry.Quantity; i++)
                {
                    BuyItemResultErrorCodes tokenResult = AcquireCustomCostumeToken(player, catalogProtoRef);
                    if (tokenResult != BuyItemResultErrorCodes.BUY_RESULT_ERROR_SUCCESS)
                        return tokenResult;
                }

                return BuyItemResultErrorCodes.BUY_RESULT_ERROR_SUCCESS;
            }

            Prototype proto = catalogProtoRef.As<Prototype>();
            if (!Verify.IsNotNull(proto)) return BuyItemResultErrorCodes.BUY_RESULT_ERROR_UNKNOWN;

            for (int i = 0; i < guidEntry.Quantity; i++)
            {
                BuyItemResultErrorCodes result;

                switch (proto)
                {
                    case ItemPrototype itemProto:
                        result = AcquireItem(player, itemProto);
                        break;

                    case PlayerStashInventoryPrototype playerStashInventoryProto:
                        result = AcquirePlayerStashInventory(player, playerStashInventoryProto);
                        break;

                    case AvatarPrototype avatarProto:
                        result = AcquireAvatar(player, avatarProto, allowTokenReplacements);
                        break;

                    case AgentTeamUpPrototype teamUpProto:
                        result = AcquireTeamUp(player, teamUpProto, allowTokenReplacements);
                        break;

                    case PowerSpecPrototype powerSpecProto:
                        result = AcquirePowerSpec(player, powerSpecProto);
                        break;

                    default:
                        Verify.IsTrue(false, $"Unimplemented catalog item type {proto.GetType().Name} for {proto}");
                        result = BuyItemResultErrorCodes.BUY_RESULT_ERROR_UNKNOWN;
                        break;
                }

                if (result != BuyItemResultErrorCodes.BUY_RESULT_ERROR_SUCCESS)
                    return result;
            }

            return BuyItemResultErrorCodes.BUY_RESULT_ERROR_SUCCESS;
        }

        private static BuyItemResultErrorCodes AcquireItem(Player player, ItemPrototype itemProto)
        {
#if GAME_VERSION_1_53
            if (itemProto is CostumePrototype costumeProto)
                return AcquireCostume(player, costumeProto);
#endif

            if (player.Game.LootManager.GiveItem(itemProto.DataRef, LootContext.CashShop, player) == false)
                return BuyItemResultErrorCodes.BUY_RESULT_ERROR_UNKNOWN;

            return BuyItemResultErrorCodes.BUY_RESULT_ERROR_SUCCESS;
        }

        private static BuyItemResultErrorCodes AcquireCustomCostumeToken(Player player, PrototypeId customId)
        {
            ItemPrototype donorProto = customId.As<ItemPrototype>();
            if (donorProto == null)
            {
                Logger.Warn($"AcquireCustomCostumeToken(): no donor prototype for custom 0x{(ulong)customId:X}");
                return BuyItemResultErrorCodes.BUY_RESULT_ERROR_UNKNOWN;
            }

            Game game = player.Game;

            ItemSpec rolled = game.LootManager.CreateItemSpec(donorProto.DataRef, LootContext.CashShop, player);
            if (rolled == null)
            {
                Logger.Warn($"AcquireCustomCostumeToken(): failed to create ItemSpec for donor [{donorProto}]");
                return BuyItemResultErrorCodes.BUY_RESULT_ERROR_UNKNOWN;
            }

            ItemSpec itemSpec = new(customId, rolled.RarityProtoRef, rolled.ItemLevel,
                                    rolled.CreditsAmount, rolled.AffixSpecs, rolled.Seed,
                                    rolled.EquippableBy)
            {
                StackCount = rolled.StackCount
            };

            using var settingsHandle = EntitySettingsPool.Get(out EntitySettings settings);
            settings.EntityRef = customId;
            settings.ItemSpec = itemSpec;

            if (game.EntityManager.CreateEntity(settings) is not Item token)
            {
                Logger.Warn($"AcquireCustomCostumeToken(): failed to create token item for donor [{donorProto}]");
                return BuyItemResultErrorCodes.BUY_RESULT_ERROR_UNKNOWN;
            }

            CustomCostumeLoader.SetItemStamp(player.DatabaseUniqueId, token.DatabaseUniqueId, customId);

            InventoryResult result = player.AcquireItem(token, PrototypeId.Invalid);
            if (result != InventoryResult.Success)
            {
                CustomCostumeLoader.ClearItemStamp(player.DatabaseUniqueId, token.DatabaseUniqueId);
                token.Destroy();

                Logger.Warn($"AcquireCustomCostumeToken(): failed to give token to player [{player}], result={result}");
                return BuyItemResultErrorCodes.BUY_RESULT_ERROR_UNKNOWN;
            }

            Logger.Trace($"AcquireCustomCostumeToken(): gave custom costume token 0x{(ulong)customId:X} (donor [{donorProto}]) to player [{player}]", LogCategory.MTXStore);
            return BuyItemResultErrorCodes.BUY_RESULT_ERROR_SUCCESS;
        }

#if GAME_VERSION_1_53
        private static BuyItemResultErrorCodes AcquireCostume(Player player, CostumePrototype costumeProto)
        {
            PrototypeId costumeProtoRef = costumeProto.DataRef;

            if (player.HasCostumeUnlocked(costumeProtoRef) == false)
            {
                player.UnlockCostume(costumeProtoRef);
                if (player.HasCostumeUnlocked(costumeProtoRef) == false)
                    return BuyItemResultErrorCodes.BUY_RESULT_ERROR_SOLD_OUT;

                return BuyItemResultErrorCodes.BUY_RESULT_ERROR_SUCCESS;
            }

            // V53_TODO: consoles?
            PrototypeId duplicateItemProtoRef = costumeProto.FulfillmentDuplicateItemPC;
            if (!Verify.IsTrue(duplicateItemProtoRef != PrototypeId.Invalid)) return BuyItemResultErrorCodes.BUY_RESULT_ERROR_UNKNOWN;

            ItemPrototype duplicateItemProto = duplicateItemProtoRef.As<ItemPrototype>();
            if (!Verify.IsNotNull(duplicateItemProto)) return BuyItemResultErrorCodes.BUY_RESULT_ERROR_UNKNOWN;

            // Falling back from costume to costume can potentially create an infinite loop
            if (!Verify.IsTrue(duplicateItemProto != costumeProto)) return BuyItemResultErrorCodes.BUY_RESULT_ERROR_UNKNOWN;

            return AcquireItem(player, duplicateItemProto);
        }
#endif

        private static BuyItemResultErrorCodes AcquirePlayerStashInventory(Player player, PlayerStashInventoryPrototype playerStashInventoryProto)
        {
            if (player.IsInventoryUnlocked(playerStashInventoryProto.DataRef))
                return BuyItemResultErrorCodes.BUY_RESULT_ERROR_ALREADY_HAVE_STASH_INV;

            if (player.UnlockInventory(playerStashInventoryProto.DataRef) == false)
                return BuyItemResultErrorCodes.BUY_RESULT_ERROR_UNKNOWN;

            return BuyItemResultErrorCodes.BUY_RESULT_ERROR_SUCCESS;
        }

        private static BuyItemResultErrorCodes AcquireAvatar(Player player, AvatarPrototype avatarProto, bool allowTokenReplacements)
        {
            PrototypeId avatarProtoRef = avatarProto.DataRef;

            // Replace with token and starting costume if we are purchasing a bundle and we already have the hero.
            if (player.HasAvatarFullyUnlocked(avatarProtoRef))
            {
                if (allowTokenReplacements == false)
                    return BuyItemResultErrorCodes.BUY_RESULT_ERROR_ALREADY_HAVE_AVATAR;

                CharacterTokenPrototype tokenProto = GetCharacterTokenPrototype(avatarProtoRef);
                if (tokenProto == null)
                    return BuyItemResultErrorCodes.BUY_RESULT_ERROR_ALREADY_HAVE_AVATAR;

                CostumePrototype costumeProto = avatarProto.GetStartingCostumeForPlatform(Platforms.PC).As<CostumePrototype>();
                if (costumeProto == null)
                    return BuyItemResultErrorCodes.BUY_RESULT_ERROR_ALREADY_HAVE_AVATAR;

                var result = AcquireItem(player, tokenProto);
                if (result != BuyItemResultErrorCodes.BUY_RESULT_ERROR_SUCCESS)
                    return result;

                return AcquireItem(player, costumeProto);
            }

            // Unlock the avatar.
            if (player.UnlockAvatar(avatarProtoRef, AvatarUnlockType.Default, true) == false)
                return BuyItemResultErrorCodes.BUY_RESULT_ERROR_UNKNOWN;

            return BuyItemResultErrorCodes.BUY_RESULT_ERROR_SUCCESS;
        }

        private static BuyItemResultErrorCodes AcquireTeamUp(Player player, AgentTeamUpPrototype teamUpProto, bool allowTokenReplacements)
        {
            PrototypeId teamUpProtoRef = teamUpProto.DataRef;

            // Replace with token if we are purchasing a bundle and we already have the hero.
            if (player.IsTeamUpAgentUnlocked(teamUpProtoRef))
            {
                if (allowTokenReplacements == false)
                    return BuyItemResultErrorCodes.BUY_RESULT_ERROR_ALREADY_HAVE_AVATAR;

                CharacterTokenPrototype tokenProto = GetCharacterTokenPrototype(teamUpProtoRef, CharacterTokenType.None);
                if (tokenProto == null)
                    return BuyItemResultErrorCodes.BUY_RESULT_ERROR_ALREADY_HAVE_AVATAR;

                return AcquireItem(player, tokenProto);
            }

            if (player.UnlockTeamUpAgent(teamUpProtoRef, true) == false)
                return BuyItemResultErrorCodes.BUY_RESULT_ERROR_UNKNOWN;

            return BuyItemResultErrorCodes.BUY_RESULT_ERROR_SUCCESS;
        }

        private static BuyItemResultErrorCodes AcquirePowerSpec(Player player, PowerSpecPrototype powerSpecProto)
        {
            if (player.UnlockPowerSpecIndex(powerSpecProto.Index) == false)
                return BuyItemResultErrorCodes.BUY_RESULT_ERROR_UNKNOWN;

            return BuyItemResultErrorCodes.BUY_RESULT_ERROR_SUCCESS;
        }

        private static CharacterTokenPrototype GetCharacterTokenPrototype(PrototypeId agentProtoRef)
        {
            // Prefer UnlockCharOrUpgradeUlt tokens if available.
            CharacterTokenPrototype tokenProto = GetCharacterTokenPrototype(agentProtoRef, CharacterTokenType.UnlockCharOrUpgradeUlt);

            // Fall back to UpgradeUltimateOnly tokens for "removed" heroes.
            if (tokenProto == null)
                return GetCharacterTokenPrototype(agentProtoRef, CharacterTokenType.UpgradeUltimateOnly);

            return tokenProto;
        }

        private static CharacterTokenPrototype GetCharacterTokenPrototype(PrototypeId agentProtoRef, CharacterTokenType tokenType)
        {
            foreach (PrototypeId tokenProtoRef in DataDirectory.Instance.IteratePrototypesInHierarchy<CharacterTokenPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
            {
                CharacterTokenPrototype tokenProto = tokenProtoRef.As<CharacterTokenPrototype>();

                if (tokenProto.Character != agentProtoRef)
                    continue;

                if (tokenType != CharacterTokenType.None && tokenProto.TokenType != tokenType)
                    continue;

                ItemCostPrototype itemCostProto = tokenProto.Cost;

                if (itemCostProto == null)
                    continue;

                if (itemCostProto.HasEternitySplintersComponent() == false)
                    continue;

                return tokenProto;
            }

            return null;
        }
    }
}
