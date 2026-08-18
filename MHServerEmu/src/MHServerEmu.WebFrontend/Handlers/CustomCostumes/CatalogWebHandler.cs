using System.Text.Json.Nodes;
using MHServerEmu.Core.Network.Web;
using MHServerEmu.Games.GameData;

namespace MHServerEmu.WebFrontend.Handlers.CustomCostumes
{
    public class CatalogWebHandler : WebHandler
    {
        protected override async Task Get(WebRequestContext context)
        {
            var costumes = new JsonArray();

            foreach (CustomCostumeLoader.CatalogCostume c in CustomCostumeLoader.Catalog)
            {
                costumes.Add(new JsonObject
                {
                    ["name"]     = c.Name,
                    ["token"]    = c.Token,
                    ["enum"]     = c.Enum,
                    ["customId"] = $"0x{c.CustomId:X16}",
                });
            }

            var packs = new JsonArray();
            foreach (CustomCostumeLoader.CatalogFxPack p in CustomCostumeLoader.FxPackCatalog)
            {
                packs.Add(new JsonObject
                {
                    ["token"]       = p.Token,
                    ["displayName"] = p.DisplayName,
                    ["hero"]        = p.Hero,
                    ["effects"]     = p.Effects,
                });
            }

            var root = new JsonObject
            {
                ["costumes"] = costumes,
                ["fxPacks"]  = packs,
            };

            await context.SendAsync(root.ToJsonString(), "application/json");
        }
    }
}
