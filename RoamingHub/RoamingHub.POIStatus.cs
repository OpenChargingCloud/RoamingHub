/*
 * Copyright (c) 2014-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of RoamingHub <https://github.com/OpenChargingCloud/RoamingHub>
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using System.Diagnostics.CodeAnalysis;

using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.POI;
using cloud.charging.open.RoamingHub.Configuration;

using Operation  = cloud.charging.open.protocols.WWCP.Node.Web.Operation;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// Who may set the runtime status of the roaming networks of this hub - whether an EVSE is charging, the power it
    /// delivers, the price shown right now - over the WebSocket of their histories, and of what.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The atlas of a roaming network changes by change sets its owners sign, and the hub takes them from everybody. The
    /// runtime status is not signed: it is what a charging station operator says about its own charging stations, every
    /// minute, and whoever says it has to be somebody this hub knows. Nobody may set it unless this hub says so - POI_API
    /// refuses every "setStatus" without AuthorizeStatusUpdate - and this hub says so for two kinds of somebody, each of
    /// whom may set the status of what its charging station operators own and of nothing else:
    /// </para>
    /// <para>
    /// <b>The peers of this hub that are charging station operators.</b> They show the token they call this hub with over
    /// OCPI, as "Authorization: Token ..." - base64 encoded or as it is, as the OCPI side takes it - or as "Bearer ...",
    /// which is what a WebSocket client writes. The token is looked up as the traffic looks it up (TryGetPeerByToken), and
    /// its peer gets in while the OCPI library allows its token and the party, and while nobody suspended it here. Its
    /// parties with the role CPO are its charging station operators: DE/GEF is DE*GEF, as the OCPI library maps a CPO to
    /// WWCP. An EMSP among the peers gets in as well, and sets nothing.
    /// </para>
    /// <para>
    /// <b>The accounts of this hub, by their API keys.</b> A program feeding the status shows the API key of its account
    /// as "API-Key: ...", as on every route of the JSON API. Its account may set the status of the charging station
    /// operators the "poi" section binds its roles to - "statusRoles": { "gef-feed": [ "DE*GEF" ] } - where the role
    /// carries "poi-status:edit" as well, see <see cref="HubAccess.POIStatus"/>. A read-only API key sets nothing, as it
    /// changes nothing anywhere else. Not by password, and not by the cookie of the web interface: a feed is a program and
    /// not somebody signing in, and a cookie is what every other website opening a WebSocket here would bring along.
    /// </para>
    /// <para>
    /// Who shows nothing gets in as "anonymous": it may read and subscribe, as everybody may read a history over HTTP,
    /// and set nothing. Who shows something this hub does not know - a token of no peer, an API key of none of its
    /// accounts, a password, a peer suspended - does not get in at all: the upgrade is answered 401.
    /// </para>
    /// <para>
    /// Asked again at every status, not only at the upgrade: a WebSocket is open for days, and a peer suspended or
    /// removed, an API key taken back or a role that went must take effect for it at once, as they do on the JSON API.
    /// Who set what is in the log, tagged "poi" and "status", and so is what was refused and why - by the name of the
    /// peer or the account, never by its token or its key.
    /// </para>
    /// </remarks>
    public partial class RoamingHub
    {

        #region Data

        /// <summary>
        /// The login of a client of the WebSocket of a history that showed no credentials: it may read and subscribe, and
        /// set nothing.
        /// </summary>
        public const String  AnonymousPOIClient  = "anonymous";

        /// <summary>
        /// The types of the entities whose runtime status a charging station operator may set: those whose identification
        /// names their operator, and the operator itself.
        /// </summary>
        public static readonly IReadOnlyList<String>  OperatorEntityTypes = [
            nameof(protocols.WWCP.POI.ChargingStationOperator),
            nameof(protocols.WWCP.POI.ChargingPool),
            nameof(protocols.WWCP.POI.ChargingStation),
            nameof(protocols.WWCP.POI.EVSE),
            nameof(protocols.WWCP.POI.ChargingTariff),
            nameof(protocols.WWCP.POI.ChargingPoolGroup),
            nameof(protocols.WWCP.POI.ChargingStationGroup),
            nameof(protocols.WWCP.POI.EVSEGroup),
            nameof(protocols.WWCP.POI.ChargingTariffGroup)
        ];

        #endregion


        #region (private) POIClient

        /// <summary>
        /// Who opened the WebSocket of a history, by name, and whose status it may set.
        /// </summary>
        /// <param name="Login">The login of its WebSocket connection: "peer:DE-GEF_CPO" or "account:gef".</param>
        /// <param name="Name">Who it is at the beginning of a sentence: "The peer 'DE-GEF_CPO'".</param>
        /// <param name="Operators">The charging station operators whose status it may set.</param>
        /// <param name="WhyNone">Why it may set none, where it may set none.</param>
        private sealed record POIClient(String                                    Login,
                                        String                                    Name,
                                        IReadOnlySet<ChargingStationOperator_Id>  Operators,
                                        String?                                   WhyNone   = null);

        #endregion

        #region (private) CheckStatusRoles(Configuration)

        /// <summary>
        /// Every role the given "poi" section binds to charging station operators is a role of this hub, and carries the
        /// permission to set the runtime status - or the start stops: a binding that sets nothing is a typo.
        /// </summary>
        private void CheckStatusRoles(POIConfiguration? Configuration)
        {

            foreach (var statusRole in Configuration?.EffectiveStatusRoles ?? new Dictionary<String, IReadOnlyList<ChargingStationOperator_Id>>())
            {

                var where = $"'{POIConfiguration.SectionName}.{POIConfiguration.StatusRolesName}.{statusRole.Key}'";

                if (Access.RoleNamed(statusRole.Key) is not { } role)
                    throw new InvalidOperationException($"'{ConfigFile.Path}': {where} binds the role '{statusRole.Key}', which this {Kind.Name} does not have: " +
                                                        $"its roles are {String.Join(", ", Access.Roles.Select(role => role.Name))}, and the \"roles\" section adds one. " +
                                                        $"Repair or remove '{ConfigFile.Path}' and start again.");

                if (!role.Allows(HubAccess.POIStatus, Operation.Edit))
                    throw new InvalidOperationException($"'{ConfigFile.Path}': {where} binds the role '{role.Name}', which may not \"{HubAccess.POIStatus}:edit\", " +
                                                        $"so it would set nothing: give it that in the \"roles\" section. " +
                                                        $"Repair or remove '{ConfigFile.Path}' and start again.");

                Log.Info($"The accounts of the role '{role.Name}' may set the runtime status of {String.Join(", ", statusRole.Value)} over the WebSocket of a roaming network.", "poi", "status", "security");

            }

        }

        #endregion

        #region (private) AuthenticatePOIClient(Request, CancellationToken)

        /// <summary>
        /// Who opens the WebSocket of a history: a peer or an account by its credentials, "anonymous" without any, and
        /// nobody - 401 - with credentials this hub does not know.
        /// </summary>
        private Task<String?> AuthenticatePOIClient(HTTPRequest        Request,
                                                    CancellationToken  CancellationToken)
        {

            if (!TryIdentifyPOIClient(Request, out var client, out var refusal))
            {
                Log.Warning($"Somebody at {ClientOf(Request)} was refused the WebSocket of the roaming network '{RoamingNetworkIdOf(Request)}': {refusal}.", "poi", "status", "websocket");
                return Task.FromResult<String?>(null);
            }

            if (client is null)
                return Task.FromResult<String?>(AnonymousPOIClient);

            Log.Info($"{client.Name} opened the WebSocket of the roaming network '{RoamingNetworkIdOf(Request)}'" +
                     (client.Operators.Count > 0
                          ? $", and may set the status of {String.Join(", ", client.Operators.Order())}."
                          : $", and may set no status: {client.WhyNone}."),
                     "poi", "status", "websocket");

            return Task.FromResult<String?>(client.Login);

        }

        #endregion

        #region (private) AuthorizePOIStatusUpdate(Request, Login, StatusUpdate)

        /// <summary>
        /// Whether whoever opened the WebSocket of a history with the given upgrade request may set the given status: asked
        /// again of its credentials, so that a peer suspended or an API key taken back sets nothing from now on.
        /// </summary>
        private Boolean AuthorizePOIStatusUpdate(HTTPRequest      Request,
                                                 String?          Login,
                                                 POIStatusUpdate  StatusUpdate)
        {

            var what = $"the {POIStatusUpdate.KindText(StatusUpdate.Kind)} of the {StatusUpdate.EntityType} '{StatusUpdate.EntityId}' " +
                       $"in the roaming network '{RoamingNetworkIdOf(Request)}'";

            if (!TryIdentifyPOIClient(Request, out var client, out var refusal))
            {
                Log.Warning($"'{Login}' was refused {what}: {refusal}.", "poi", "status");
                return false;
            }

            if (client is null)
            {
                Log.Warning($"Somebody without credentials at {ClientOf(Request)} was refused {what}: who sets a status shows who it is.", "poi", "status");
                return false;
            }

            if (client.Operators.Count == 0)
            {
                Log.Warning($"{client.Name} was refused {what}: {client.WhyNone}.", "poi", "status");
                return false;
            }

            if (OperatorOf(StatusUpdate.EntityType, StatusUpdate.EntityId) is not { } operatorId)
            {
                Log.Warning($"{client.Name} was refused {what}: that is nothing a charging station operator owns.", "poi", "status");
                return false;
            }

            if (!client.Operators.Contains(operatorId))
            {
                Log.Warning($"{client.Name} was refused {what}: it may set the status of {String.Join(", ", client.Operators.Order())}, not of {operatorId}.", "poi", "status");
                return false;
            }

            Log.Info($"{client.Name} sets {what} to {Shortened(StatusUpdate.Value)}" +
                     (StatusUpdate.DataSource is { Length: > 0 } dataSource ? $", from '{Shortened(dataSource)}'." : "."),
                     "poi", "status");

            return true;

        }

        #endregion


        #region (private) TryIdentifyPOIClient(Request, out Client, out Refusal)

        /// <summary>
        /// Who sent the given upgrade request, by its credentials: a peer by its token, an account by its API key, nobody
        /// (null) without credentials - or why credentials it showed are refused.
        /// </summary>
        private Boolean TryIdentifyPOIClient(HTTPRequest                       Request,
                                             out POIClient?                    Client,
                                             [NotNullWhen(false)] out String?  Refusal)
        {

            Client   = null;
            Refusal  = null;

            var apiKey         = Request.GetHeaderField(HTTPRequestHeaderField.API_Key.Name);
            var authorization  = Request.GetHeaderField(HTTPRequestHeaderField.Authorization.Name);

            if (apiKey is null && authorization is null)
                return true;

            if (apiKey is not null && authorization is not null)
            {
                Refusal = "it showed an API key and an Authorization header, and one of them says who it is";
                return false;
            }

            if (apiKey is not null)
                return TryIdentifyAccount(Request, out Client, out Refusal);

            return Request.Authorization switch {
                       HTTPTokenAuthentication   token   => TryIdentifyPeer(token. Token, out Client, out Refusal),
                       HTTPBearerAuthentication  bearer  => TryIdentifyPeer(bearer.Token, out Client, out Refusal),
                       _                                 => Refused("its Authorization header is neither \"Token\" nor \"Bearer\" with the token of a peer; " +
                                                                    "an account shows its API key, as \"API-Key\"", out Client, out Refusal)
                   };

        }

        #endregion

        #region (private) TryIdentifyPeer(Token, out Client, out Refusal)

        /// <summary>
        /// The peer of the given token, while it is allowed in, with its parties that are charging station operators.
        /// </summary>
        private Boolean TryIdentifyPeer(String                            Token,
                                        out POIClient?                    Client,
                                        [NotNullWhen(false)] out String?  Refusal)
        {

            if (!TryGetPeerByToken(Token, out var peer, out var accessInfo))
                return Refused("its token is the token of no peer of this hub", out Client, out Refusal);

            // As this hub decides it - a peer suspended is not talked to, nor listened to - said first, for
            // it says why; its tokens are BLOCKED as well, see SuspendPeer. And as the OCPI side decides it:
            // the token and the party allowed.
            if (FindPeer(peer.Id.ToString())?.Status == PeerStatus.SUSPENDED)
                return Refused($"the peer '{peer.Id}' is suspended", out Client, out Refusal);

            if (accessInfo.Status != protocols.OCPI.AccessStatus.ALLOWED || peer.Status != protocols.OCPI.PartyStatus.ENABLED)
                return Refused($"the peer '{peer.Id}' is not allowed in (its token {accessInfo.Status}, the peer {peer.Status})", out Client, out Refusal);

            // Its parties as its credentials name them, several behind one token where it has several; as it was added,
            // where its version names none.
            var parties    = peer is protocols.OCPI.ARemotePartyV2x withRoles && withRoles.Roles.Any()
                                 ? withRoles.Roles.Select(role => (role.PartyId, role.Role))
                                 : new[] { (protocols.OCPI.Party_Idv3.From(peer.Id.CountryCode, peer.Id.PartyId), peer.Id.Role) };

            var operators  = new HashSet<ChargingStationOperator_Id>();

            foreach (var (partyId, role) in parties)
                if (role == protocols.OCPI.Role.CPO &&
                    protocols.OCPI.OCPIMapper.ToWWCP(protocols.OCPI.CPO_Id.From(partyId)) is { } operatorId)
                {
                    operators.Add(operatorId);
                }

            Client   = new POIClient($"peer:{peer.Id}",
                                     $"The peer '{peer.Id}'",
                                     operators,
                                     operators.Count == 0 ? "it is no charging station operator, as its role is not CPO" : null);
            Refusal  = null;
            return true;

        }

        #endregion

        #region (private) TryIdentifyAccount(Request, out Client, out Refusal)

        /// <summary>
        /// The account of the API key of the given request, while it may sign in, with the charging station operators the
        /// "poi" section binds its roles to.
        /// </summary>
        private Boolean TryIdentifyAccount(HTTPRequest                       Request,
                                           out POIClient?                    Client,
                                           [NotNullWhen(false)] out String?  Refusal)
        {

            if (!ExtAPI.TryGetValidAPIKey(Request.API_Key, out var apiKey) || apiKey is null)
                return Refused("its API key is none of this hub, or no longer valid", out Client, out Refusal);

            if (!ExtAPI.TryGetUser(apiKey.UserId, out var user) || !HTTPExtAPI.CanAuthenticate(user))
                return Refused($"the account '{apiKey.UserId}' of its API key may not sign in", out Client, out Refusal);

            var operators   = new HashSet<ChargingStationOperator_Id>();
            var statusRoles = poiConfiguration.EffectiveStatusRoles;

            // A read-only API key changes nothing, here as on the JSON API, whatever the roles of its account.
            if (apiKey.AccessRights != APIKeyRights.ReadOnly)
                foreach (var role in RolesOf(user))
                    if (role.Allows(HubAccess.POIStatus, Operation.Edit) &&
                        statusRoles.TryGetValue(role.Name, out var roleOperators))
                    {
                        operators.UnionWith(roleOperators);
                    }

            Client   = new POIClient($"account:{user.Id}",
                                     $"The account '{user.Id}'",
                                     operators,
                                     operators.Count > 0
                                         ? null
                                         : apiKey.AccessRights == APIKeyRights.ReadOnly
                                               ? "its API key is read-only"
                                               : $"none of its roles may \"{HubAccess.POIStatus}:edit\" and is bound to charging station operators in " +
                                                 $"'{POIConfiguration.SectionName}.{POIConfiguration.StatusRolesName}'");
            Refusal  = null;
            return true;

        }

        #endregion

        #region (static) OperatorOf(EntityType, EntityId)

        /// <summary>
        /// The charging station operator the entity of the given type and identification belongs to, as its identification
        /// names it - DE*GEF*E1*1 is an EVSE of DE*GEF - or null for an entity no charging station operator owns.
        /// </summary>
        /// <param name="EntityType">The type of an entity, as a status update names it: "EVSE".</param>
        /// <param name="EntityId">Its identification.</param>
        public static ChargingStationOperator_Id? OperatorOf(String  EntityType,
                                                             String  EntityId)

            => EntityType switch {
                   nameof(protocols.WWCP.POI.ChargingStationOperator)  when ChargingStationOperator_Id.TryParse(EntityId, out var id)  => id,
                   nameof(protocols.WWCP.POI.ChargingPool)             when ChargingPool_Id.           TryParse(EntityId, out var id)  => id.OperatorId,
                   nameof(protocols.WWCP.POI.ChargingStation)          when ChargingStation_Id.        TryParse(EntityId, out var id)  => id.OperatorId,
                   nameof(protocols.WWCP.POI.EVSE)                     when EVSE_Id.                   TryParse(EntityId, out var id)  => id.OperatorId,
                   nameof(protocols.WWCP.POI.ChargingTariff)           when ChargingTariff_Id.         TryParse(EntityId, out var id)  => id.OperatorId,
                   nameof(protocols.WWCP.POI.ChargingPoolGroup)        when ChargingPoolGroup_Id.      TryParse(EntityId, out var id)  => id.OperatorId,
                   nameof(protocols.WWCP.POI.ChargingStationGroup)     when ChargingStationGroup_Id.   TryParse(EntityId, out var id)  => id.OperatorId,
                   nameof(protocols.WWCP.POI.EVSEGroup)                when EVSEGroup_Id.              TryParse(EntityId, out var id)  => id.OperatorId,
                   nameof(protocols.WWCP.POI.ChargingTariffGroup)      when ChargingTariffGroup_Id.    TryParse(EntityId, out var id)  => id.OperatorId,
                   _                                                                                                                   => null
               };

        #endregion

        #region (private static) RoamingNetworkIdOf(Request)

        /// <summary>
        /// The roaming network of the WebSocket the given request asked for: ~/RNs/{roamingNetworkId}/history/ws.
        /// </summary>
        private static String RoamingNetworkIdOf(HTTPRequest Request)
        {

            var segments  = Request.Path.ToString().Split('/', StringSplitOptions.RemoveEmptyEntries);
            var index     = Array.LastIndexOf(segments, "RNs");

            return index >= 0 && index + 1 < segments.Length
                       ? Uri.UnescapeDataString(segments[index + 1])
                       : "?";

        }

        #endregion

        #region (private static) Shortened(Text)

        /// <summary>
        /// The given text for the log, at most 120 characters of it: the value of an energy or a price status is an
        /// object, and a long one.
        /// </summary>
        private static String Shortened(String Text)

            => Text.Length <= 120
                   ? $"'{Text}'"
                   : $"'{Text[..117]}...'";

        #endregion

        #region (private static) Refused(Why, out Client, out Refusal)

        private static Boolean Refused(String                            Why,
                                       out POIClient?                    Client,
                                       [NotNullWhen(false)] out String?  Refusal)
        {

            Client   = null;
            Refusal  = Why;
            return false;

        }

        #endregion

    }

}
