using MHServerEmu.Commands.Attributes;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Network;
using MHServerEmu.DatabaseAccess.Models;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Entities.Inventories;
using MHServerEmu.Games.Entities.Items;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Calligraphy;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Network;
using MHServerEmu.Games.Powers;
using MHServerEmu.Games.Powers.Conditions;
using MHServerEmu.Games.Properties;
using System.Globalization;

namespace MHServerEmu.Commands.Implementations
{
    [CommandGroup("player")]
    [CommandGroupDescription("Commands for managing player data for the invoker's account.")]
    public class PlayerCommands : CommandGroup
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        [Command("costume")]
        [CommandDescription("Changes costume for the current avatar.")]
        [CommandUsage("player costume [name|reset]")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string Costume(string[] @params, NetClient client)
        {
            PrototypeId costumeProtoRef;
            string input = @params[0];

            switch (input.ToLower())
            {
                case "reset":
                    costumeProtoRef = PrototypeId.Invalid;
                    break;

                default:
                    if (input.StartsWith("0x", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (ulong.TryParse(input.Substring(2), NumberStyles.HexNumber, null, out ulong rawId))
                        {
                            costumeProtoRef = (PrototypeId)rawId;
                            break;
                        }
                        return "Invalid hex ID.";
                    }

                    if (CustomCostumeLoader.NameToId.TryGetValue(input, out PrototypeId customProtoRef))
                    {
                        costumeProtoRef = customProtoRef;
                        break;
                    }

                    var matches = GameDatabase.SearchPrototypes(input,
                        DataFileSearchFlags.SortMatchesByName | DataFileSearchFlags.IgnoreCase,
                        HardcodedBlueprints.Costume);

                    if (matches.Any() == false)
                        return $"Failed to find any costumes containing '{input}'.";

                    if (matches.Count() > 1)
                    {
                        CommandHelper.SendMessage(client, $"Found multiple matches for '{input}':");
                        CommandHelper.SendMessages(client, matches.Select(match => GameDatabase.GetPrototypeName(match)), false);
                        return string.Empty;
                    }

                    costumeProtoRef = matches.First();
                    break;
            }

            PlayerConnection playerConnection = (PlayerConnection)client;
            var player = playerConnection.Player;
            var avatar = player.CurrentAvatar;

            bool wasCustom = avatar.ClientOnCustomCostume;

            if (avatar.ChangeCostume(costumeProtoRef) == false)
                return $"That costume cannot be worn by {avatar.PrototypeDataRef.GetName()}. " +
                       $"Costumes are hero-specific - switch to the right hero first.";

            bool isCustom = costumeProtoRef != PrototypeId.Invalid
                && CustomCostumeLoader.CustomInfo.ContainsKey(costumeProtoRef);

            if (isCustom)
                CustomCostumeLoader.SetOverride(player.DatabaseUniqueId, avatar.PrototypeDataRef, costumeProtoRef);
            else
                CustomCostumeLoader.ClearOverride(player.DatabaseUniqueId, avatar.PrototypeDataRef);

            Logger.Info($"[CostumeCmd] input='{input}' resolved=0x{(ulong)costumeProtoRef:X} isCustom={isCustom} wasCustom={wasCustom} -> refresh={(isCustom || wasCustom)}");

            avatar.NotifyLiveCustomCostumeChange();

            if (isCustom || wasCustom)
            {
                if (isCustom == false)
                    avatar.ClearClientCustomCostume();   // we are re-realizing out of it now
                player.RefreshCurrentAvatar();
            }

            if (costumeProtoRef == PrototypeId.Invalid)
                return "Resetting costume.";

            if (CustomCostumeLoader.CustomInfo.TryGetValue(costumeProtoRef, out var info))
                return $"Changing costume to {info.displayName}.";

            return $"Changing costume to {GameDatabase.GetPrototypeName(costumeProtoRef)}.";
        }

        [Command("costumes")]
        [CommandDescription("Enables custom costume store items for your account (requires the client mod).")]
        [CommandUsage("player costumes [enable|disable|status]")]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string Costumes(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;

            switch (@params[0].ToLower())
            {
                case "enable":
                    playerConnection.CustomCostumesEnabled = true;
                    return "Custom costumes ENABLED. Custom costume tokens are now purchasable. " +
                           "If you have not installed the client mod, they will not render.";

                case "disable":
                    playerConnection.CustomCostumesEnabled = false;
                    return "Custom costumes DISABLED. Custom costume tokens can no longer be bought or gifted to you. " +
                           "Tokens you already own are unaffected.";

                case "status":
                    return playerConnection.CustomCostumesEnabled
                        ? "Custom costumes are ENABLED for your account."
                        : "Custom costumes are DISABLED for your account. Use \"!player costumes enable\".";

                default:
                    return "Usage: !player costumes [enable|disable|status]";
            }
        }

        [Command("disablevu")]
        [CommandDescription("Forces the fallback costume for the current hero, reverting visual updates in some cases.")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string DisableVU(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;
            Avatar avatar = player.CurrentAvatar;

            if (avatar == null)
                return "Avatar is not available.";

            PrototypeId costumeProtoRef = PrototypeId.Invalid;
            string result;

            if (avatar.EquippedCostumeRef != (PrototypeId)HardcodedBlueprints.Costume)
            {
                // Apply fallback costume override
                costumeProtoRef = (PrototypeId)HardcodedBlueprints.Costume;
                result = "Applied fallback costume override.";
            }
            else
            {
                // Revert fallback costume override if it is currently applied
                Inventory costumeInv = avatar.GetInventory(InventoryConvenienceLabel.Costume);
                if (costumeInv != null && costumeInv.Count > 0)
                {
                    Item costume = avatar.Game.EntityManager.GetEntity<Item>(costumeInv.GetAnyEntity());
                    if (costume != null)
                        costumeProtoRef = costume.PrototypeDataRef;
                }

                result = "Reverted fallback costume override.";
            }

            avatar.ChangeCostume(costumeProtoRef);
            return result;
        }

        [Command("wipe")]
        [CommandDescription("Wipes all progress associated with the current account.")]
        [CommandUsage("player wipe [playerName]")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string Wipe(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            string playerName = playerConnection.Player.GetName();

            if (@params.Length == 0)
                return $"Type '!player wipe {playerName}' to wipe all progress associated with this account.\nWARNING: THIS ACTION CANNOT BE REVERTED.";

            if (string.Equals(playerName, @params[0], StringComparison.OrdinalIgnoreCase) == false)
                return "Incorrect player name.";

            playerConnection.WipePlayerData();
            return string.Empty;
        }

        [Command("givecurrency")]
        [CommandDescription("Gives all currencies.")]
        [CommandUsage("player givecurrency [amount]")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string GiveCurrency(string[] @params, NetClient client)
        {
            if (int.TryParse(@params[0], out int amount) == false)
                return $"Failed to parse amount from {@params[0]}.";

            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;

            foreach (PrototypeId currencyProtoRef in DataDirectory.Instance.IteratePrototypesInHierarchy<CurrencyPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                player.Properties.AdjustProperty(amount, new(PropertyEnum.Currency, currencyProtoRef));

            return $"Successfully given {amount} of all currencies.";
        }

        [Command("clearconditions")]
        [CommandDescription("Clears persistent conditions.")]
        [CommandUsage("player clearconditions")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string ClearConditions(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;
            Avatar avatar = player.CurrentAvatar;

            int count = 0;

            foreach (Condition condition in avatar.ConditionCollection)
            {
                if (condition.IsPersistToDB() == false)
                    continue;

                avatar.ConditionCollection.RemoveCondition(condition.Id);
                count++;
            }

            return $"Cleared {count} persistent conditions.";
        }

        [Command("die")]
        [CommandDescription("Kills the current avatar.")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string Die(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;

            Avatar avatar = playerConnection.Player.CurrentAvatar;
            if (avatar == null || avatar.IsInWorld == false)
                return "Avatar not found.";

            if (avatar.IsDead)
                return "You are already dead.";

            PowerResults powerResults = new();
            powerResults.Init(avatar.Id, avatar.Id, avatar.Id, avatar.RegionLocation.Position, null, default, true);
            powerResults.SetFlag(PowerResultFlags.InstantKill, true);
            avatar.ApplyDamageTransferPowerResults(powerResults);

            return $"You are now dead. Thank you for using Stop-and-Drop.";
        }
    }
}
