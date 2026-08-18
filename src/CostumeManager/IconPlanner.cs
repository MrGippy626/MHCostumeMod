using System.Collections.Generic;
using System.IO;
using System.Linq;
using IconPack.Core;

namespace CostumeManager
{

    internal sealed class IconPlan
    {
        public List<IconSource> Sources { get; } = new List<IconSource>();

        public List<string> DonorFallback { get; } = new List<string>();
    }

    internal static class IconPlanner
    {

        internal static IconPlan Resolve(bool installing)
        {
            var plan = new IconPlan();
            if (installing && !AppState.UseCustomIcons) return plan;

            var supplied = AppState.IconChoices
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value) && File.Exists(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            foreach (IconRoleInfo r in IconPackBuilder.Roles)
            {
                if (supplied.TryGetValue(r.Role, out string own))
                    plan.Sources.Add(new IconSource { Role = r.Role, ImagePath = own });
                else
                    plan.DonorFallback.Add($"{r.Role} — {r.Description}");
            }

            return plan;
        }
    }
}
