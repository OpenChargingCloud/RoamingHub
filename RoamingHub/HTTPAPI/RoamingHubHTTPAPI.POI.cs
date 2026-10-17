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

using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// The part of the JSON API that is about the points of interest: a new roaming network.
    /// </summary>
    /// <remarks>
    /// Only the making of one: reading them is the POI API's, below "/poi", public as it is; and what is in them
    /// changes by change sets their owners sign, submitted there as well. Making one says whose keys rule it,
    /// and is the permission poi:edit.
    /// </remarks>
    public partial class RoamingHubHTTPAPI
    {

        #region (private) RegisterPOIRoutes()

        /// <summary>
        /// Everything under /v1/poi.
        /// </summary>
        private void RegisterPOIRoutes()
        {

            AddHandler(HTTPPath.Root + "v1/poi/roamingNetworks",  PostRoamingNetwork,  HTTPMethod.POST);

        }

        #endregion


        #region (private) PostRoamingNetwork(Request)

        /// <summary>
        /// POST /api/v1/poi/roamingNetworks with {"@id", "name"?, "description"?, "signingKeys",
        /// "identityKeys"}: make a new roaming network, with the public keys of its roaming network operator.
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

            Log.Notice($"'{user.Id}' made the roaming network '{network.Id}', with {network.SigningKeys.Length} signing and " +
                       $"{network.IdentityKeys.Length} identity key(s) of its roaming network operator.",
                       "poi", "web", "security");

            var summary = new JObject(new JProperty("@id", network.Id.ToString()));

            if (!network.Name.IsNullOrEmpty())
                summary["name"]         = network.Name.ToJSON();

            if (!network.Description.IsNullOrEmpty())
                summary["description"]  = network.Description.ToJSON();

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

    }

}
