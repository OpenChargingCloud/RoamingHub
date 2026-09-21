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

using cloud.charging.open.RoamingHub.Logging;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// What went between the peers of this hub, and how it is written down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A hub is asked to account for traffic it did not originate. Two peers
    /// that talk directly can each read their own log and compare them; with
    /// a hub in between, neither of them can say what the other actually
    /// sent, so the hub has to be able to. Every OCPI call that touches this
    /// one is written down here and handed out as it happens.
    /// </para>
    /// <para>
    /// Taken off the HTTP server rather than out of the OCPI library. The
    /// server sees every request, whether or not the library recognised it,
    /// whether or not the token was one of ours, and whether or not the route
    /// existed - and a peer that cannot get in is exactly the case somebody
    /// is looking at the traffic to understand. A hook inside the library
    /// would see only the calls that got that far.
    /// </para>
    /// </remarks>
    public partial class Hub
    {

        #region Data

        private readonly OCPITrafficLog trafficLog;

        #endregion

        #region Properties

        /// <summary>
        /// What went between the peers.
        /// </summary>
        public OCPITrafficLog  Traffic
            => trafficLog;

        #endregion


        #region (private) RecordInboundCall(Request, Response)

        /// <summary>
        /// Write down one call a peer made to this hub.
        /// </summary>
        /// <remarks>
        /// Everything below the OCPI root and nothing else: the web interface
        /// and the JSON API are this hub's own business and are in the event
        /// log where they belong.
        /// </remarks>
        private void RecordInboundCall(HTTPRequest   Request,
                                       HTTPResponse  Response)
        {

            var path = Request.Path.ToString();

            if (!IsOCPIPath(path))
                return;

            var (version, module) = ReadVersionAndModule(path);

            var (ocpiStatusCode, ocpiStatusMessage) = ReadOCPIEnvelope(Response);

            trafficLog.Add(

                Direction:           CallDirection.Inbound,

                // Who it was, as this hub knows them; where the token was not
                // one of ours, the socket is all there is.
                Peer:                ReadPeer(Request),

                From:                ReadParty(Request, protocols.OCPI.HTTPHeaders.OCPI_From_Country_Code, protocols.OCPI.HTTPHeaders.OCPI_From_PartyId),
                To:                  ReadParty(Request, protocols.OCPI.HTTPHeaders.OCPI_To_Country_Code,   protocols.OCPI.HTTPHeaders.OCPI_To_PartyId),

                Version:             version,
                Module:              module,
                Method:              Request.HTTPMethod.ToString(),
                Path:                path,

                HTTPStatusCode:      (UInt16) Response.HTTPStatusCode.Code,
                OCPIStatusCode:      ocpiStatusCode,
                OCPIStatusMessage:   ocpiStatusMessage,

                Duration:            Response.Timestamp - Request.Timestamp,
                RequestSize:         (Int64) (Request. ContentLength ?? 0),
                ResponseSize:        (Int64) (Response.ContentLength ?? 0),

                RequestId:           Request.GetHeaderField(protocols.OCPI.HTTPHeaders.X_Request_ID),
                CorrelationId:       Request.GetHeaderField(protocols.OCPI.HTTPHeaders.X_Correlation_ID),
                RemoteSocket:        Request.RemoteSocket.ToString(),

                RequestBody:         KeepPayloads ? Request. HTTPBodyAsUTF8String : null,
                ResponseBody:        KeepPayloads ? Response.HTTPBodyAsUTF8String : null

            );

        }

        #endregion

        #region RecordOutboundCall(...)

        /// <summary>
        /// Write down one call this hub made to a peer.
        /// </summary>
        /// <remarks>
        /// Handed in rather than taken off a socket: what this hub sends goes
        /// out through the OCPI library's own HTTP clients, and what comes
        /// back to the caller is an answer rather than a response. So the
        /// caller says what it did - see
        /// <see cref="RegisterRemotePartyAsync"/> - and the line reads the
        /// same as an inbound one beside it.
        /// </remarks>
        internal void RecordOutboundCall(String     Peer,
                                         String?    Version,
                                         String     Module,
                                         String     Method,
                                         String     Path,
                                         Boolean    Succeeded,
                                         TimeSpan   Duration,
                                         String?    Message   = null)

            => trafficLog.Add(
                   Direction:           CallDirection.Outbound,
                   Peer:                Peer,

                   // Written the way the OCPI headers write a party rather
                   // than the way this hub writes itself elsewhere: "from"
                   // and "to" are the header pair, the headers carry no role,
                   // and one of the two ends of a line reading differently
                   // from the other would be a filter that quietly misses
                   // half its calls.
                   From:                $"{PartyId.CountryCode}-{PartyId.PartyId}",
                   To:                  Peer,
                   Version:             Version,
                   Module:              Module,
                   Method:              Method,
                   Path:                Path,
                   HTTPStatusCode:      (UInt16) (Succeeded ? 200 : 0),
                   OCPIStatusCode:      Succeeded ? 1000 : null,
                   OCPIStatusMessage:   Message,
                   Duration:            Duration,
                   Error:               Succeeded ? null : Message
               );

        #endregion

        #region (private) KeepPayloads

        /// <summary>
        /// Whether the bodies are kept beside the lines.
        /// </summary>
        /// <remarks>
        /// Off unless the operator asked. What travels through a hub is a
        /// location somebody operates, a session somebody is having, a card
        /// somebody is holding - none of it this hub's to keep, and all of it
        /// in memory where anybody who may read the traffic would see it.
        /// </remarks>
        private Boolean KeepPayloads
            => OCPI.Logging?.Payloads == true;

        #endregion

        #region (private) ReadPeer(Request)

        /// <summary>
        /// The peer a call came in on, resolved from the token it presented.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Asked of the Common HTTP API rather than read off the request,
        /// and that is not a detour. The OCPI library has one request type
        /// per version and another for the API the versions hang off, and
        /// none of them derives from the others - so a cast that works for a
        /// call to "/ext/versions" quietly returns nothing for the same
        /// peer's call to "/ext/v2.2.1/credentials", which is exactly the
        /// call somebody is looking for.
        /// </para>
        /// <para>
        /// The Common HTTP API knows the peers of every version attached to
        /// it, so one question here answers for all of them.
        /// </para>
        /// </remarks>
        private String? ReadPeer(HTTPRequest Request)
        {

            if (Request.Authorization is not HTTPTokenAuthentication tokenAuth)
                return null;

            // Both spellings, because OCPI 2.2 sends the token base64 encoded
            // and 2.1.1 sends it as it is - and a hub that only understood
            // one of them would name half its peers.
            foreach (var parse in new Func<String, protocols.OCPI.AccessToken?>[] {
                                      text => protocols.OCPI.AccessToken.TryParse          (text, out var token) ? token : null,
                                      text => protocols.OCPI.AccessToken.TryParseFromBASE64(text, out var token) ? token : null
                                  })
            {

                if (parse(tokenAuth.Token) is { } accessToken &&
                    OCPIAPI.TryGetRemoteParties(accessToken, null, null, out var parties, out _) &&
                    parties.FirstOrDefault() is { } found)
                {
                    return found.Item1.Id.ToString();
                }

            }

            return null;

        }

        #endregion

        #region (private) IsOCPIPath(Path)

        /// <summary>
        /// Whether a path is one a peer would have asked for.
        /// </summary>
        private Boolean IsOCPIPath(String Path)
        {

            var root = ExtAPI.RootPath.ToString().TrimEnd('/');

            return Path.StartsWith($"{root}/versions", StringComparison.OrdinalIgnoreCase) ||
                   Path.StartsWith($"{root}/v",        StringComparison.OrdinalIgnoreCase);

        }

        #endregion

        #region (private static) ReadVersionAndModule(Path)

        /// <summary>
        /// The OCPI version and module out of a path, where the path has
        /// them.
        /// </summary>
        /// <remarks>
        /// Read off the path rather than asked of the library, because a
        /// request that never reached a route still went somewhere and the
        /// somewhere is the interesting part. The two shapes are
        /// "/ext/versions[/2.2.1]" and "/ext/v2.2.1/hub/{module}/...".
        /// </remarks>
        private static (String?, String) ReadVersionAndModule(String Path)
        {

            var segments = Path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < segments.Length; i++)
            {

                // "/ext/versions" and "/ext/versions/2.2.1"
                if (segments[i].Equals("versions", StringComparison.OrdinalIgnoreCase))
                    return (i + 1 < segments.Length ? segments[i + 1] : null, "versions");

                // "/ext/v2.2.1/credentials" and "/ext/v2.2.1/hub/{module}"
                if (segments[i].Length > 1 &&
                    segments[i][0] is 'v' or 'V' &&
                    Char.IsAsciiDigit(segments[i][1]))
                {

                    var version = segments[i][1..];

                    // The role segment - "hub", "cpo", "emsp" - is not the
                    // module; what follows it is. A credentials call has no
                    // role segment at all.
                    var rest    = segments.Skip(i + 1).
                                           Where(segment => segment is not ("hub" or "cpo" or "emsp")).
                                           ToArray();

                    return (version, rest.Length > 0 ? rest[0] : "(none)");

                }

            }

            return (null, "(none)");

        }

        #endregion

        #region (private static) ReadParty(Request, CountryCodeHeader, PartyIdHeader)

        /// <summary>
        /// One of the two parties an OCPI request names in its headers,
        /// written the way everything else here writes one.
        /// </summary>
        private static String? ReadParty(HTTPRequest  Request,
                                         String       CountryCodeHeader,
                                         String       PartyIdHeader)
        {

            var countryCode = Request.GetHeaderField(CountryCodeHeader);
            var partyId     = Request.GetHeaderField(PartyIdHeader);

            return countryCode is { Length: > 0 } && partyId is { Length: > 0 }
                       ? $"{countryCode.ToUpperInvariant()}-{partyId.ToUpperInvariant()}"
                       : null;

        }

        #endregion

        #region (private static) ReadOCPIEnvelope(Response)

        /// <summary>
        /// The status code and message out of an OCPI envelope, where the
        /// answer had one.
        /// </summary>
        /// <remarks>
        /// OCPI answers a refusal with 200 and a status code inside the body
        /// as readily as with a 4xx, so a line without this says too little
        /// about whether the call did what it was asked. A body that is not
        /// an envelope - an HTML error page, an empty 404 - is not a fault
        /// here; it simply has nothing to read.
        /// </remarks>
        private static (Int32?, String?) ReadOCPIEnvelope(HTTPResponse Response)
        {

            if (Response.ContentLength is null or 0 ||
                Response.ContentType?.MediaSubType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
            {
                return (null, null);
            }

            try
            {

                var body = Response.HTTPBodyAsUTF8String;

                if (body is null || !body.TrimStart().StartsWith('{'))
                    return (null, null);

                var json = JObject.Parse(body);

                return (
                           json["status_code"]?.Value<Int32>(),
                           json["status_message"]?.Value<String>()
                       );

            }
            catch
            {
                // A body that will not parse says nothing about the call and
                // must not cost the response that carried it.
                return (null, null);
            }

        }

        #endregion

    }

}
