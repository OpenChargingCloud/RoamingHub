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

using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.OCPI;

using V = cloud.charging.open.protocols.OCPIv2_3_0;

#endregion

namespace cloud.charging.open.RoamingHub.OCPI
{

    /// <summary>
    /// OCPI 2.3.0, as this hub speaks it.
    /// </summary>
    /// <remarks>
    /// The same parties, the same registration and the same base64 encoded
    /// tokens as 2.2.1. Offered only where the configuration file asks for
    /// it, because 2.2.1 is the one both ends of a peering have been run
    /// against.
    /// </remarks>
    public sealed class OCPIv2_3_0 : OCPIVersion
    {

        #region Data

        private readonly V.CommonAPI  commonAPI;

        #endregion

        #region Properties

        public override Version_Id  Id
            => V.Version.Id;

        /// <summary>
        /// The library's Common API for this version.
        /// </summary>
        public V.CommonAPI    CommonAPI
            => commonAPI;

        #endregion

        #region Constructor(s)

        public OCPIv2_3_0(Hub            Hub,
                          CommonHTTPAPI  BaseAPI,
                          String         Directory)

            : base(Hub)

        {

            commonAPI = new V.CommonAPI(

                            OurPartyData:              [
                                                           new V.PartyData(
                                                               Hub.PartyId,
                                                               Role.HUB,
                                                               Hub.BusinessDetails,
                                                               Hub.OCPI.AllowDowngrades
                                                           )
                                                       ],
                            DefaultPartyId:            Hub.PartyId,

                            BaseAPI:                   BaseAPI,

                            // Only in the URLs it advertises, never in the paths
                            // it serves: see Hub.OCPI.cs for why.
                            AdditionalURLPathPrefix:   Hub.ExtAPI.RootPath,

                            HTTPServerName:            $"OpenChargingCloud RoamingHub v{Hub.Version}",
                            HTTPServiceName:           $"OpenChargingCloud RoamingHub v{Hub.Version}",

                            LoggingPath:               Directory,
                            DisableLogging:            true

                        );

            // The library's HUB_HTTPAPI is deliberately not built here.
            //
            // Two reasons, and either would be enough. Nothing it serves is
            // wired up yet - this hub does the peering and no forwarding - so
            // it would add endpoints that answer nothing. And it cannot be
            // constructed at all as the library stands: it registers
            // "hub/locations/{country_code}/{party_id}" beside
            // "hub/locations/{locationId}", and Hermod refuses two sibling
            // parameter routes on the same segment as ambiguous, which throws
            // in its constructor.
            //
            // The peering needs none of it: the versions, the version details
            // and the credentials are all on the Common API above, and the
            // client that walks to a peer is built from the peer itself - see
            // Register below.

            WireEvents();

        }

        #endregion

        #region (private) WireEvents()

        /// <summary>
        /// The credentials handshake of this version, into the event log.
        /// </summary>
        /// <remarks>
        /// The traffic log gets the call itself, from the HTTP server, which
        /// is what somebody asking "did that peer ever reach us" needs. This
        /// is the sentence a person reads beside it.
        /// </remarks>
        private void WireEvents()
        {

            commonAPI.OnPostCredentialsResponse   += new V.OCPIResponseLogHandler((timestamp, api, request, response, cancellationToken) => {
                LogHandshake("registered with", request, response);
                return Task.CompletedTask;
            });

            commonAPI.OnPutCredentialsResponse    += new V.OCPIResponseLogHandler((timestamp, api, request, response, cancellationToken) => {
                LogHandshake("renewed its registration with", request, response);
                return Task.CompletedTask;
            });

            commonAPI.OnDeleteCredentialsResponse += new V.OCPIResponseLogHandler((timestamp, api, request, response, cancellationToken) => {
                LogHandshake("unregistered from", request, response);
                return Task.CompletedTask;
            });


            void LogHandshake(String What, V.OCPIRequest Request, V.OCPIResponse Response)
            {

                var who     = Request.RemoteParty?.Id.ToString() ?? $"somebody at {Request.HTTPRequest.RemoteSocket}";
                var worked  = Response.StatusCode == StatusCode.Success;

                if (Hub.OCPI.Logging?.Requests != false || !worked)
                    Hub.Log.Log(
                        worked ? Logging.LogLevel.Notice : Logging.LogLevel.Warning,
                        worked
                            ? $"The peer {who} {What} this hub (OCPI {Label})."
                            : $"The peer {who} tried to {What.Split(' ')[0]} this hub and was answered {Response.StatusCode}: {Response.StatusMessage} (OCPI {Label}).",
                        "ocpi", "credentials", "peer"
                    );

            }

        }

        #endregion


        #region Roaming partners

        public override IEnumerable<RemotePartySummary> RemoteParties

            => commonAPI.RemoteParties.Select(party => {

                   var roles = party.Roles.ToArray();
                   var role  = roles.Length > 0 ? roles[0] : (CredentialsRole?) null;

                   return Summarize(
                              party,
                              role?.PartyId.CountryCode ?? party.Id.CountryCode,
                              role?.PartyId.PartyId     ?? party.Id.PartyId,
                              role?.Role                ?? party.Id.Role,
                              role?.BusinessDetails     ?? new BusinessDetails(party.Id.ToString())
                          );

               });


        public override async Task<String?> AddRemoteParty(RemotePartySpec Spec)
        {

            var roles = new[] {
                            new CredentialsRole(
                                Spec.CountryCode,
                                Spec.PartyId,
                                Spec.Role,
                                new BusinessDetails(Spec.Name, Spec.Website),
                                AllowDowngrades: false
                            )
                        };

            var result = Spec.CanRegister

                             ? await commonAPI.AddRemoteParty(
                                         Id:                                Spec.Id,
                                         CredentialsRoles:                  roles,
                                         LocalAccessToken:                  Spec.OurToken,
                                         RemoteVersionsURL:                 Spec.TheirVersionsURL!.Value,
                                         RemoteAccessToken:                 Spec.TheirToken!.Value,
                                         RemoteAccessTokenBase64Encoding:   true,
                                         LocalAccessStatus:                 AccessStatus.ALLOWED,
                                         RemoteStatus:                      RemoteAccessStatus.ONLINE,
                                         Status:                            PartyStatus.ENABLED
                                     )

                             : await commonAPI.AddRemoteParty(
                                         Id:                                Spec.Id,
                                         CredentialsRoles:                  roles,
                                         LocalAccessToken:                  Spec.OurToken,
                                         LocalAccessStatus:                 AccessStatus.ALLOWED,
                                         Status:                            PartyStatus.ENABLED
                                     );

            if (!result.IsSuccess)
                return result.ErrorResponse ?? "The library declined to add the peer and did not say why.";

            // Nothing to register the peer with beyond the Common API: the
            // hub API of this version has no registry of remote parties at
            // all, where 2.2.1's has one for the CPO side. It costs nothing
            // for the peering, which is all this version is used for here,
            // and it would cost something the day a peer pushes.
            return null;

        }


        public override Task<Boolean> RemoveRemoteParty(RemoteParty_Id Id)
            => commonAPI.RemoveRemoteParty(Id);


        public override async Task<OCPIOperationResult> Register(RemoteParty_Id Id)
        {

            if (!commonAPI.TryGetRemoteParty(Id, out var party))
                return OCPIOperationResult.Failed($"There is no peer '{Id}' on OCPI {Label}.");

            if (!party.RemoteAccessInfos.Any())
                return OCPIOperationResult.Failed($"'{Id}' has not handed out a token and a versions URL, so there is nowhere to send this hub's credentials.");

            // Built from the peer rather than asked of a role-shaped client
            // factory. A hub talks to a CPO where an EMSP would and to an
            // EMSP where a CPO would, and the credentials handshake - the
            // only thing asked of a client here - is the same either way and
            // lives on the client both of those are.
            using var client = new V.CommonHTTPClient(
                                   CommonAPI:    commonAPI,
                                   RemoteParty:  party,
                                   DNSClient:    Hub.DNSClient
                               );

            var response = await client.Register();

            return DescribeRegistration(Id, response.StatusCode, response.StatusMessage, response.Data is not null);

        }

        #endregion

        #region Modules

        /// <summary>
        /// What a peer is told this hub serves.
        /// </summary>
        /// <remarks>
        /// Only the module a hub has of its own for now. The modules it would
        /// forward between its peers are not wired up, and advertising an
        /// endpoint that answers nothing is worse than not advertising it.
        /// </remarks>
        protected override IEnumerable<String> Modules
            => [ "hubclientinfo" ];

        #endregion

    }

}
