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

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.POI;
using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// The part of the JSON API that is about the points of interest: the roaming network operator this hub is,
    /// its keys replaced and its name changed, a new roaming network, and the parties this hub adds to the roaming
    /// networks it operates.
    /// </summary>
    /// <remarks>
    /// Only that: reading the roaming networks is the POI API's, below "/poi", public as it is; and what is in them
    /// changes by change sets their owners sign, submitted there as well. Everything here but reading the operator
    /// signs with the keys of this hub, and is the permission poi:edit.
    /// </remarks>
    public partial class RoamingHubHTTPAPI
    {

        #region (private) RegisterPOIRoutes()

        /// <summary>
        /// Everything under /v1/poi.
        /// </summary>
        private void RegisterPOIRoutes()
        {

            AddHandler(HTTPPath.Root + "v1/poi/roamingNetworkOperator",                     GetRoamingNetworkOperator,  HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/poi/roamingNetworkOperator/signingKey/rotate",   request => PostRotateKey(request, POIKeyKind.Signing),   HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "v1/poi/roamingNetworkOperator/identityKey/rotate",  request => PostRotateKey(request, POIKeyKind.Identity),  HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "v1/poi/roamingNetworkOperator/name",                PutNetworkOperatorName,     HTTPMethod.PUT);
            AddHandler(HTTPPath.Root + "v1/poi/roamingNetworks",                            PostRoamingNetwork,         HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "v1/poi/roamingNetworks/{roamingNetworkId}/{parties}", PostParty,                  HTTPMethod.POST);

        }

        #endregion


        #region (private) GetRoamingNetworkOperator(Request)

        /// <summary>
        /// GET /api/v1/poi/roamingNetworkOperator: the roaming network operator this hub is, as every roaming network
        /// it operates embeds it, with the public halves of its keys - public, as they are in every one of them.
        /// </summary>
        private Task<HTTPResponse> GetRoamingNetworkOperator(HTTPRequest Request)

            => Task.FromResult(
                   JSONResponse(Request,
                                HTTPStatusCode.OK,
                                RoamingHub.NetworkOperator.Operator.ToJSON())
               );

        #endregion

        #region (private) PostRotateKey(Request, Kind)

        /// <summary>
        /// POST /api/v1/poi/roamingNetworkOperator/signingKey/rotate or …/identityKey/rotate: replace the current key
        /// of that kind of the roaming network operator this hub is by a new one, in every roaming network it operates.
        /// </summary>
        /// <remarks>
        /// 200 with what became of it and the operator as it is now; 207 where a roaming network refused it, which
        /// keeps the key it names.
        /// </remarks>
        private Task<HTTPResponse> PostRotateKey(HTTPRequest  Request,
                                                 POIKeyKind   Kind)
        {

            if (!TryAuthorize(Request, Permission.Edit(HubAccess.POI), true, out var user, out var refused))
                return Task.FromResult(refused);

            var result = RoamingHub.RotateNetworkOperatorKey(Kind);

            Log.Notice($"'{user.Id}' replaced the {(Kind == POIKeyKind.Signing ? "signing" : "identity")} key of the roaming network operator '{RoamingHub.NetworkOperator.Id}'.",
                       "poi", "web", "security");

            return Task.FromResult(NetworkOperatorUpdated(Request, result));

        }

        #endregion

        #region (private) PutNetworkOperatorName(Request)

        /// <summary>
        /// PUT /api/v1/poi/roamingNetworkOperator/name with {"name": "…"} or {"name": {"en": "…", …}}: give the roaming
        /// network operator this hub is a new name, in every roaming network it operates.
        /// </summary>
        /// <remarks>
        /// 200 with what became of it and the operator as it is now; 207 where a roaming network refused it; 400
        /// where there is no name.
        /// </remarks>
        private Task<HTTPResponse> PutNetworkOperatorName(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(HubAccess.POI), true, out var user, out var refused))
                return Task.FromResult(refused);

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return Task.FromResult(errorResponse);

            var name = json["name"] switch {
                           JValue  { Type: JTokenType.String } text  => I18NString.Create(Languages.en, text.Value<String>()!.Trim()),
                           JObject                             texts => I18NString.Parse(texts),
                           _                                         => null
                       };

            if (name is null || name.IsNullOrEmpty() || name.FirstText().Trim().Length == 0)
                return Task.FromResult(ErrorJSON(Request, HTTPStatusCode.BadRequest, "A roaming network operator is renamed with {\"name\": \"…\"} or {\"name\": {\"en\": \"…\"}}."));

            var result = RoamingHub.RenameNetworkOperator(name);

            Log.Notice($"'{user.Id}' renamed the roaming network operator '{RoamingHub.NetworkOperator.Id}' to '{name.FirstText()}'.",
                       "poi", "web");

            return Task.FromResult(NetworkOperatorUpdated(Request, result));

        }

        #endregion

        #region (private) NetworkOperatorUpdated(Request, Update)

        private HTTPResponse NetworkOperatorUpdated(HTTPRequest                   Request,
                                                    RoamingNetworkOperatorUpdate  Update)
        {

            var json = Update.ToJSON();

            json[RoamingNetworkOperator.JSONProperty] = RoamingHub.NetworkOperator.Operator.ToJSON();

            return JSONResponse(Request,
                                Update.Success ? HTTPStatusCode.OK : HTTPStatusCode.MultiStatus,
                                json);

        }

        #endregion

        #region (private) PostRoamingNetwork(Request)

        /// <summary>
        /// POST /api/v1/poi/roamingNetworks with {"@id", "name"?, "description"?}: make a new roaming network,
        /// operated by this hub.
        /// </summary>
        /// <remarks>
        /// 201 with the network as the POI API lists it and its genesis commit; 400 where it is no roaming network
        /// this hub makes, 409 where there is one of that identification already, 500 where its store could not be
        /// written.
        /// </remarks>
        private Task<HTTPResponse> PostRoamingNetwork(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(HubAccess.POI), true, out var user, out var refused))
                return Task.FromResult(refused);

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return Task.FromResult(errorResponse);

            var result = RoamingHub.CreateRoamingNetwork(json);

            if (!result.Success || result.History is null)
                return Task.FromResult(
                           ErrorJSON(Request,
                                     result.Status switch {
                                         RoamingNetworkCreationStatus.Exists    => HTTPStatusCode.Conflict,
                                         RoamingNetworkCreationStatus.NotSaved  => HTTPStatusCode.InternalServerError,
                                         _                                      => HTTPStatusCode.BadRequest
                                     },
                                     result.Message)
                       );

            var network = result.History.Head.RoamingNetwork;

            Log.Notice($"'{user.Id}' made the roaming network '{network.Id}', operated by this hub, '{RoamingHub.NetworkOperator.Id}'.",
                       "poi", "web", "security");

            var summary = new JObject(new JProperty("@id", network.Id.ToString()));

            if (!network.Name.IsNullOrEmpty())
                summary["name"]         = network.Name.ToJSON();

            if (!network.Description.IsNullOrEmpty())
                summary["description"]  = network.Description.ToJSON();

            summary[RoamingNetworkOperator.JSONProperty] = new JObject(new JProperty("@id", RoamingHub.NetworkOperator.Id.ToString()));

            return Task.FromResult(
                       JSONResponse(Request,
                                    HTTPStatusCode.Created,
                                    new JObject(
                                        new JProperty("message",         result.Message),
                                        new JProperty("roamingNetwork",  summary),
                                        new JProperty("revision",        result.History.Head.Commit.Revision),
                                        new JProperty("commitId",        result.History.Head.Commit.Id.ToString())
                                    ))
                   );

        }

        #endregion

        #region (private) PostParty(Request)

        /// <summary>
        /// POST /api/v1/poi/roamingNetworks/{roamingNetworkId}/{chargingStationOperators | eMobilityProviders |
        /// gridOperators | parkingOperators} with the party as POI 3 writes it, {"@id", "name"?, ..., "signingKeys",
        /// "identityKeys"}, its public keys: add it to a roaming network this hub operates, signed by this hub.
        /// </summary>
        /// <remarks>
        /// 201 with the commit that added it; 400 where it is no party this hub adds, 404 where there is no such
        /// roaming network, 403 where another roaming network operator operates it, 409 where it has that party
        /// already, 422 where its history refused the commit.
        /// </remarks>
        private Task<HTTPResponse> PostParty(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(HubAccess.POI), true, out var user, out var refused))
                return Task.FromResult(refused);

            var parameters = Request.ParsedURLParameters;

            if (parameters.Length < 2 ||
                !RoamingNetwork_Id.TryParse(parameters[0], out var roamingNetworkId) ||
                !RoamingHub.PartyTypes.TryGetValue(parameters[1], out var entityType))
            {
                return Task.FromResult(
                           ErrorJSON(Request,
                                     HTTPStatusCode.NotFound,
                                     $"A party is added at .../roamingNetworks/{{roamingNetworkId}}/{{{String.Join(" | ", RoamingHub.PartyTypes.Keys)}}}.")
                       );
            }

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return Task.FromResult(errorResponse);

            var result = RoamingHub.AddParty(roamingNetworkId, entityType, json);

            if (!result.Success || result.Commit is null)
                return Task.FromResult(
                           ErrorJSON(Request,
                                     result.Status switch {
                                         RoamingNetworkPartyStatus.UnknownRoamingNetwork  => HTTPStatusCode.NotFound,
                                         RoamingNetworkPartyStatus.NotOperatedHere        => HTTPStatusCode.Forbidden,
                                         RoamingNetworkPartyStatus.Exists                 => HTTPStatusCode.Conflict,
                                         RoamingNetworkPartyStatus.Refused                => HTTPStatusCode.UnprocessableEntity,
                                         _                                                => HTTPStatusCode.BadRequest
                                     },
                                     result.Message)
                       );

            Log.Notice($"'{user.Id}' added the {entityType} '{json["@id"]}' to the roaming network '{roamingNetworkId}', signed by this hub, '{RoamingHub.NetworkOperator.Id}'.",
                       "poi", "web", "security");

            return Task.FromResult(
                       JSONResponse(Request,
                                    HTTPStatusCode.Created,
                                    new JObject(
                                        new JProperty("message",   result.Message),
                                        new JProperty("revision",  result.Commit.Revision),
                                        new JProperty("commitId",  result.Commit.Id.ToString())
                                    ))
                   );

        }

        #endregion

    }

}
