using System.Net;
using System.Text.Json.Serialization;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Network;
using MHServerEmu.Core.Network.Web;
using MHServerEmu.Games.GameData;
using MHServerEmu.WebFrontend.Network;

namespace MHServerEmu.WebFrontend.Handlers.CustomCostumes
{
    public class RegisterWebHandler : WebHandler
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        protected override async Task Post(WebRequestContext context)
        {
            RegisterRequest request;

            try
            {
                request = await context.ReadJsonAsync<RegisterRequest>();
            }
            catch (Exception e)
            {
                Logger.Warn($"Post(): Malformed registration body: {e.Message}");
                context.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            if (TryParseId(request.SessionId, out ulong sessionId) == false || sessionId == 0)
            {
                Logger.Warn($"Post(): Missing or zero sessionId in a registration");
                context.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            if (TryParseRefs(request.ForgedRefs, out ulong[] forgedRefs) == false)
            {
                context.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            ServiceMessage.CustomCostumeRegisterResponse response =
                await GameServiceTaskManager.Instance.RegisterCustomCostumesAsync(sessionId, forgedRefs);

            context.StatusCode = response.StatusCode;
        }

        private static bool TryParseId(string raw, out ulong value)
        {
            value = 0;
            string v = raw?.Trim();
            if (string.IsNullOrWhiteSpace(v)) return false;

            return v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? ulong.TryParse(v.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out value)
                : ulong.TryParse(v, out value);
        }

        private static bool TryParseRefs(string[] raw, out ulong[] forgedRefs)
        {
            forgedRefs = Array.Empty<ulong>();

            if (raw == null)
                return false;

            if (raw.Length > CustomCostumeRegistry.MaxRefsPerRegistration)
            {
                Logger.Warn($"TryParseRefs(): {raw.Length} refs exceeds the cap of {CustomCostumeRegistry.MaxRefsPerRegistration}");
                return false;
            }

            ulong[] parsed = new ulong[raw.Length];

            for (int i = 0; i < raw.Length; i++)
            {
                string value = raw[i]?.Trim();

                if (string.IsNullOrWhiteSpace(value))
                {
                    Logger.Warn("TryParseRefs(): Empty ref");
                    return false;
                }

                bool ok = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? ulong.TryParse(value.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out parsed[i])
                    : ulong.TryParse(value, out parsed[i]);

                if (ok == false)
                {
                    Logger.Warn($"TryParseRefs(): Unparsable ref '{value}'");
                    return false;
                }
            }

            forgedRefs = parsed;
            return true;
        }

        private readonly struct RegisterRequest
        {
            [JsonPropertyName("forgedRefs")]
            public string[] ForgedRefs { get; init; }

            [JsonPropertyName("sessionId")]
            public string SessionId { get; init; }
        }
    }
}
