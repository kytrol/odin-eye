namespace OdinEye.Http.Api.Controllers
{
    using Extensions;
    using Models;
    using Models.Api;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using WebSocketSharp.Server;

    // Backs both halves of the character-stats ingest API (ODINEYE-12/
    // ODINEYE-19): GET /players/stats (IController, matches the existing
    // exact-route pattern) and POST /players/{id}/stats (IPostController,
    // the plugin's first parameterized/mutating route). One class because
    // they share the same in-memory store.
    //
    // OdinEye stays stateless by design here -- accepted values live only in
    // this process's memory (cleared on restart), never written to disk.
    // The valheim_server-side poller (see VALSER) is what makes the data
    // durable, by reading GET /players/stats into its own SQLite DB.
    public class CharacterStatsController : IController, IPostController
    {
        // Valheim's Early Access release (2021-02-02 UTC) -- a hard ceiling
        // for any submitted time-based stat's plausible value, regardless of
        // how much anyone's actually played. Deliberately not a tighter
        // "this looks too big" guess: some real characters already have
        // extensive playtime, and a guessed threshold risks rejecting a
        // genuine long-time player's real data. This ceiling only catches
        // actually-impossible values (overflow, an uninitialized sentinel,
        // corruption) -- see ODINEYE-15 (Decision 3).
        private static readonly DateTime ValheimReleaseDate = new DateTime(2021, 2, 2, 0, 0, 0, DateTimeKind.Utc);

        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, float>> StatsByPlayerId =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, float>>();

        public string Route => "/players/stats";

        public string RoutePrefix => "/players/";

        public string RouteSuffix => "/stats";

        public void OnGet(HttpRequestEventArgs requestArguments)
        {
            var snapshot = StatsByPlayerId.ToDictionary(
                player => player.Key,
                player => (IReadOnlyDictionary<string, float>)new Dictionary<string, float>(player.Value));

            requestArguments.Response.Ok(snapshot);
        }

        public void OnPost(HttpRequestEventArgs requestArguments, string routeParameter)
        {
            if (!Guid.TryParse(routeParameter, out var playerId))
            {
                requestArguments.Response.Error(400);
                return;
            }

            if (!IsConnectedPlayer(playerId))
            {
                requestArguments.Response.Error(404);
                return;
            }

            CharacterStatsSubmission submission;
            try
            {
                submission = SafeJsonSerializer.Deserialize<CharacterStatsSubmission>(requestArguments.Request.InputStream);
            }
            catch
            {
                requestArguments.Response.Error(400);
                return;
            }

            if (submission?.Stats == null || submission.Stats.Count == 0)
            {
                requestArguments.Response.Error(400);
                return;
            }

            var playerIdKey = playerId.ToString();
            var existingStats = StatsByPlayerId.GetOrAdd(playerIdKey, _ => new ConcurrentDictionary<string, float>());

            // Validate every value before accepting any of them -- a
            // partially-applied submission (some stats updated, others
            // silently dropped for failing validation) would be a worse,
            // harder-to-notice failure mode than rejecting the whole thing.
            foreach (var stat in submission.Stats)
            {
                existingStats.TryGetValue(stat.Key, out var previousValue);
                if (!IsValidStatValue(stat.Value, previousValue))
                {
                    requestArguments.Response.Error(400);
                    return;
                }
            }

            foreach (var stat in submission.Stats)
            {
                existingStats[stat.Key] = stat.Value;
            }

            requestArguments.Response.Ok(new AcceptedResponse());
        }

        // Deliberately NOT ZNet.instance.GetAllPeers() (the extension used by
        // PlayersController/GameStatsSnapshotCoroutine for live health/
        // stamina data): that one only yields a peer once it also has a
        // live character ZDO (ZDOMan.instance.GetZDO(peer.m_characterID) !=
        // null), which is a real, confirmed-live race against
        // OdinEyeClientPlugin submitting on its very first Update() tick
        // after spawning -- the client's own log ("Character ID for player
        // (..., 0:0). Skipping.") shows the character ID can genuinely
        // still read as 0:0 at that exact instant. That gate exists for a
        // different reason (PlayersController needs real health/stamina
        // numbers, which do need a live ZDO) and has nothing to do with
        // "is this GUID a currently-connected, authenticated player" --
        // all this needs is the peer's identity, which ZNetPatch's own
        // RPC_PeerInfo handler shows is reliably available (peer.m_socket/
        // peer.m_playerName) from the moment a peer joins, well before any
        // character spawns. Iterating ZNet.instance.m_peers directly via
        // ZNetPeerExtensions.ToPlayer() (the same identity computation,
        // matching peer.SteamId/peer.Name field-for-field) avoids the race
        // entirely (ODINEYE-25).
        private static bool IsConnectedPlayer(Guid playerId) =>
            ZNet.instance.m_peers.Any(peer => peer.ToPlayer().Id == playerId);

        // internal (not private) + InternalsVisibleTo (OdinEye.csproj's
        // AssemblyInfo.cs) so OdinEye.Tests can exercise this pure
        // validation logic directly -- no live ZNet/EnvMan/ZDOMan
        // dependency here at all, unlike IsConnectedPlayer above, so it
        // doesn't need a real game process the way that does (ODINEYE-18).
        internal static bool IsValidStatValue(float value, float previousValue)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
            {
                return false;
            }

            var maxPlausibleValue = (float)(DateTime.UtcNow - ValheimReleaseDate).TotalSeconds;
            if (value > maxPlausibleValue)
            {
                return false;
            }

            // previousValue defaults to 0 when the stat hasn't been
            // submitted before, which is never greater than a valid
            // (non-negative) new value -- no special-casing needed for a
            // first submission.
            return value >= previousValue;
        }
    }
}
