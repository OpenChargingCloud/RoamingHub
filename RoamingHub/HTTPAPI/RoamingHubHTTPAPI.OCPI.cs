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

using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.OCPI;

using cloud.charging.open.RoamingHub.OCPI;
using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// The part of the JSON API that is about OCPI: who this hub is and where
    /// it is, the peers, the tokens of its customers, and what the
    /// partners pushed.
    /// </summary>
    /// <remarks>
    /// Who this hub is in OCPI is configuration, and read as configuration.
    /// Its peers are a resource of their own - see <see cref="HubAccess.Peers"/>:
    /// reading them shows who is on this hub; editing them adds, suspends,
    /// resumes or removes one, which hands a foreign system the right to push
    /// into this hub or takes it away; and running them is the credentials
    /// handshake with one, which asks a server somebody named something and
    /// believes the answer. Out of the box only the administrators may do the
    /// last two.
    ///
    /// A partner's tokens travel to the browser only for whoever may edit the
    /// peers: the token this hub handed out is what the operator has to give
    /// the partner, so it has to be shown to them - and to nobody else,
    /// because it opens this hub.
    /// </remarks>
    public partial class RoamingHubHTTPAPI
    {

        #region (private) RegisterOCPIRoutes()

        /// <summary>
        /// Everything under /v1/configuration/ocpi and /v1/ocpi.
        /// </summary>
        private void RegisterOCPIRoutes()
        {

            AddHandler(HTTPPath.Root + "v1/configuration/ocpi",                    GetOCPIConfiguration,  HTTPMethod.GET);

            AddHandler(HTTPPath.Root + "v1/ocpi/partners",                         GetPartners,           HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/ocpi/partners",                         PostPartner,           HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "v1/ocpi/partners/{version}/{id}/register", PostPartnerRegister,   HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "v1/ocpi/partners/{version}/{id}",          DeletePartner,         HTTPMethod.DELETE);

            // The HubClientInfo side of a peer: not whether it is peered, but
            // whether this hub is willing to talk to it at all.
            AddHandler(HTTPPath.Root + "v1/ocpi/peers/{partyId}/suspend",           PostPeerSuspend,       HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "v1/ocpi/peers/{partyId}/resume",            PostPeerResume,        HTTPMethod.POST);


        }

        #endregion


        #region (private) GetOCPIConfiguration(Request)

        /// <summary>
        /// GET /api/v1/configuration/ocpi: who this hub is in OCPI, where its
        /// endpoints are, and how much its partners have sent.
        /// </summary>
        private Task<HTTPResponse> GetOCPIConfiguration(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(NodeResources.Configuration), false, out _, out var refused))
                return Task.FromResult(refused);

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, RoamingHub.OCPIConfigurationJSON())
                   );

        }

        #endregion


        #region (private) The peers

        /// <summary>
        /// GET /api/v1/ocpi/partners: every peer, over every
        /// version. The tokens travel along for whoever may manage partners.
        /// </summary>
        private Task<HTTPResponse> GetPartners(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(HubAccess.Peers), false, out var user, out var refused))
                return Task.FromResult(refused);

            var mayManage = RoamingHub.IsAllowed(user, HubAccess.Peers, Operation.Edit);

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, RoamingHub.RemotePartiesJSON(IncludeSecrets: mayManage))
                   );

        }

        /// <summary>
        /// POST /api/v1/ocpi/partners with {"version", "countryCode",
        /// "partyId", "role", "name", "website", "ourToken", "theirToken",
        /// "versionsURL"}: add a peer.
        /// </summary>
        /// <remarks>
        /// An empty token of ours means "make one up", and it comes back in
        /// this response: the operator hands it to the partner, who registers
        /// with it. With the partner's own token and versions URL as well,
        /// this hub can start the peering itself - see
        /// <see cref="PostPartnerRegister"/>.
        /// </remarks>
        private async Task<HTTPResponse> PostPartner(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(HubAccess.Peers), true, out var user, out var refused))
                return refused;

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return errorResponse;

            var result = await RoamingHub.AddRemotePartyAsync(json);

            if (!result.Success)
                return NotChanged(Request, HTTPStatusCode.BadRequest, result.Message, result.NotSaved);

            Log.Info($"'{user.Id}' added the peer '{result.Data?.Value<String>("id")}'.", "ocpi", "partner", "web");

            var response = new JObject(
                               new JProperty("message",   result.Message),
                               new JProperty("partners",  RoamingHub.RemotePartiesJSON(IncludeSecrets: true))
                           );

            if (result.Data is not null)
                foreach (var property in result.Data.Properties())
                    response[property.Name] = property.Value;

            return JSONResponse(Request, HTTPStatusCode.Created, response);

        }

        /// <summary>
        /// POST /api/v1/ocpi/partners/{version}/{id}/register: start the
        /// peering with a partner that handed out its token and versions URL.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A POST that sends traffic to a host somebody named, and every step
        /// of it is in the log - so the Logs page of anybody watching shows
        /// the handshake as it happens.
        /// </para>
        /// <para>
        /// 502 where the partner did not go along, 500 where the file of the
        /// partners refused what it changed - both with the partners, as the
        /// page redraws them either way.
        /// </para>
        /// </remarks>
        private async Task<HTTPResponse> PostPartnerRegister(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Run(HubAccess.Peers), true, out var user, out var refused))
                return refused;

            if (!TryGetVersionAndId(Request, out var version, out var id, out var badRequest))
                return badRequest;

            Log.Info($"'{user.Id}' asked this hub to register with the peer '{id}' (OCPI {version}).", "ocpi", "credentials", "partner", "web");

            var result = await RoamingHub.RegisterRemotePartyAsync(version, id);

            return JSONResponse(
                       Request,
                       result.Success  ? HTTPStatusCode.OK
                     : result.NotSaved ? HTTPStatusCode.InternalServerError
                     :                   HTTPStatusCode.BadGateway,
                       new JObject(
                           new JProperty("ok",        result.Success),
                           new JProperty("message",   result.Message),
                           new JProperty("partners",  RoamingHub.RemotePartiesJSON(IncludeSecrets: true))
                       )
                   );

        }

        /// <summary>
        /// POST /api/v1/ocpi/peers/{partyId}/suspend: stop talking to a peer
        /// without forgetting it.
        /// </summary>
        /// <remarks>
        /// The specification has no deletion in HubClientInfo, and this is
        /// why: a peer that is switched off is still a peer every other one
        /// has heard of, and they are told it is SUSPENDED rather than left
        /// to wonder why it went quiet. Removing it altogether is the other
        /// button, and it means something else.
        /// </remarks>
        private Task<HTTPResponse> PostPeerSuspend(HTTPRequest Request)
            => SetPeerSuspension(Request, Suspend: true);

        /// <summary>
        /// POST /api/v1/ocpi/peers/{partyId}/resume: talk to it again.
        /// </summary>
        private Task<HTTPResponse> PostPeerResume(HTTPRequest Request)
            => SetPeerSuspension(Request, Suspend: false);

        /// <summary>
        /// Both of the above, which differ by one word.
        /// </summary>
        private Task<HTTPResponse> SetPeerSuspension(HTTPRequest Request, Boolean Suspend)
        {

            if (!TryAuthorize(Request, Permission.Edit(HubAccess.Peers), true, out var user, out var refused))
                return Task.FromResult(refused);

            if (!Request.ParsedURLParameters.Any() ||
                !Party_Idv3.TryParse(Request.ParsedURLParameters[0], out var partyId))
            {
                return Task.FromResult(
                           JSONResponse(
                               Request,
                               HTTPStatusCode.BadRequest,
                               new JObject(new JProperty("error", "A party identification of five characters is needed, e.g. 'DEGEF'."))
                           )
                       );
            }

            var ok = Suspend
                         ? RoamingHub.SuspendPeer(partyId)
                         : RoamingHub.ResumePeer (partyId);

            if (!ok)
                return Task.FromResult(
                           JSONResponse(
                               Request,
                               HTTPStatusCode.NotFound,
                               new JObject(new JProperty("error", $"This hub knows no peer '{partyId}'."))
                           )
                       );

            Log.Info($"'{user.Id}' {(Suspend ? "suspended" : "resumed")} the peer '{partyId}'.", "ocpi", "hubclientinfo", "web");

            return Task.FromResult(
                       JSONResponse(
                           Request,
                           HTTPStatusCode.OK,
                           new JObject(
                               new JProperty("ok",        true),
                               new JProperty("message",   $"'{partyId}' was {(Suspend ? "suspended" : "resumed")}."),
                               new JProperty("partners",  RoamingHub.RemotePartiesJSON(IncludeSecrets: true))
                           )
                       )
                   );

        }

        /// <summary>
        /// DELETE /api/v1/ocpi/partners/{version}/{id}: forget a roaming
        /// partner.
        /// </summary>
        private async Task<HTTPResponse> DeletePartner(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(HubAccess.Peers), true, out var user, out var refused))
                return refused;

            if (!TryGetVersionAndId(Request, out var version, out var id, out var badRequest))
                return badRequest;

            var result = await RoamingHub.RemoveRemotePartyAsync(version, id);

            if (!result.Success)
                return NotChanged(Request, HTTPStatusCode.NotFound, result.Message, result.NotSaved);

            Log.Info($"'{user.Id}' removed the peer '{id}' (OCPI {version}).", "ocpi", "partner", "web");

            return JSONResponse(Request, HTTPStatusCode.OK, RoamingHub.RemotePartiesJSON(IncludeSecrets: true));

        }

        #endregion




        #region (private static) TryGetVersionAndId(Request, out Version, out Id, out ErrorResponse)

        /// <summary>
        /// The version and the identification out of the path, or the 400
        /// that says one of them was missing.
        /// </summary>
        private static Boolean TryGetVersionAndId(HTTPRequest       Request,
                                                  out String        Version,
                                                  out String        Id,
                                                  out HTTPResponse  ErrorResponse)
        {

            var parameters = Request.ParsedURLParameters;

            Version        = parameters.Length > 0 ? parameters[0].Trim() : "";
            Id             = parameters.Length > 1 ? parameters[1].Trim() : "";
            ErrorResponse  = default!;

            if (Version.Length == 0 || Id.Length == 0)
            {
                ErrorResponse = ErrorJSON(Request, HTTPStatusCode.BadRequest, "An OCPI version and an identification are required.");
                return false;
            }

            return true;

        }

        #endregion

    }

}
