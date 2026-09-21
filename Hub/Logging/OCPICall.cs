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

#endregion

namespace cloud.charging.open.RoamingHub.Logging
{

    /// <summary>
    /// Which way a call went: a peer called this hub, or this hub called a
    /// peer.
    /// </summary>
    public enum CallDirection
    {

        /// <summary>A peer called this hub.</summary>
        Inbound,

        /// <summary>This hub called a peer.</summary>
        Outbound

    }


    /// <summary>
    /// One OCPI call through this hub: who made it, who it was meant for,
    /// what it asked, and what came back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The thing a hub is asked to account for. A CPO and an EMSP that talk
    /// directly can each read their own logs and compare them; the moment a
    /// hub is between them, neither of them can say what the other actually
    /// sent, and "it works for us" is an answer nobody can check. So every
    /// call that touches this hub is written down as one of these, in both
    /// directions, and handed out over a Server-Sent Events stream as it
    /// happens.
    /// </para>
    /// <para>
    /// <b>From and To are the hub's own question.</b> OCPI 2.2 puts four
    /// headers on a request that passes through a hub - the country code and
    /// party identification it came from, and the ones it is meant for. On a
    /// direct peering they are decoration; here they are the routing, and
    /// they are what turns a list of requests into "who talked to whom".
    /// A call without them is a call about the hub itself, which is why they
    /// are allowed to be null rather than made up.
    /// </para>
    /// <para>
    /// <b>Not the payload, unless asked.</b> What travels through a hub is a
    /// location somebody operates, a session somebody is having, a card
    /// somebody is holding. The line says which module and how big the body
    /// was; the body itself is kept only where the operator asked for it -
    /// see the "logging.payloads" setting.
    /// </para>
    /// </remarks>
    /// <param name="Id">Counting up from one, so that a reader can ask for everything after what it has.</param>
    /// <param name="Timestamp">When the call was answered.</param>
    /// <param name="Direction">Whether a peer called this hub or this hub called a peer.</param>
    /// <param name="Peer">The peer at the other end, as this hub knows it, or null when the token was not one of ours.</param>
    /// <param name="From">The party the call says it is from, out of the OCPI headers.</param>
    /// <param name="To">The party the call says it is for, out of the OCPI headers.</param>
    /// <param name="Version">The OCPI version it came in on, e.g. "2.2.1".</param>
    /// <param name="Module">The OCPI module: "locations", "credentials", "versions", ...</param>
    /// <param name="Method">The HTTP method.</param>
    /// <param name="Path">The path that was asked for.</param>
    /// <param name="HTTPStatusCode">What HTTP said.</param>
    /// <param name="OCPIStatusCode">What OCPI said in its envelope, or null when there was no envelope.</param>
    /// <param name="OCPIStatusMessage">What OCPI said about it, where it said anything.</param>
    /// <param name="Duration">How long it took.</param>
    /// <param name="RequestSize">The size of the request body in bytes.</param>
    /// <param name="ResponseSize">The size of the response body in bytes.</param>
    /// <param name="RequestId">The X-Request-ID, which is how two logs of the same call are lined up.</param>
    /// <param name="CorrelationId">The X-Correlation-ID, which is how a chain of them is.</param>
    /// <param name="RemoteSocket">Where the other end was, for a call this hub could not name.</param>
    /// <param name="Error">What went wrong, where something did and OCPI had nothing to say about it.</param>
    /// <param name="RequestBody">The request body, where the operator asked for payloads.</param>
    /// <param name="ResponseBody">The response body, likewise.</param>
    public sealed record OCPICall(UInt64           Id,
                                  DateTimeOffset   Timestamp,
                                  CallDirection    Direction,
                                  String?          Peer,
                                  String?          From,
                                  String?          To,
                                  String?          Version,
                                  String           Module,
                                  String           Method,
                                  String           Path,
                                  UInt16           HTTPStatusCode,
                                  Int32?           OCPIStatusCode,
                                  String?          OCPIStatusMessage,
                                  TimeSpan         Duration,
                                  Int64            RequestSize,
                                  Int64            ResponseSize,
                                  String?          RequestId        = null,
                                  String?          CorrelationId    = null,
                                  String?          RemoteSocket     = null,
                                  String?          Error            = null,
                                  String?          RequestBody      = null,
                                  String?          ResponseBody     = null)
    {

        #region Properties

        /// <summary>
        /// Whether this call did what it was asked to do.
        /// </summary>
        /// <remarks>
        /// Both halves have to agree. OCPI answers a refusal with 200 and a
        /// status code in the envelope as readily as with a 4xx, so an HTTP
        /// status on its own says too little; a call with no envelope at all
        /// is judged by HTTP alone.
        /// </remarks>
        public Boolean  Succeeded
            => HTTPStatusCode is >= 200 and < 400 &&
               (OCPIStatusCode is null || OCPIStatusCode is >= 1000 and < 2000) &&
               Error is null;

        /// <summary>
        /// The two parties of this call, written the way a reader asks the
        /// question.
        /// </summary>
        public String   Between
            => From is not null || To is not null
                   ? $"{From ?? "?"} to {To ?? "?"}"
                   : Peer ?? RemoteSocket ?? "somebody";

        #endregion

        #region Matches(Party)

        /// <summary>
        /// Whether this call has anything to do with the given party, at
        /// either end of it.
        /// </summary>
        public Boolean Matches(String Party)

            => String.Equals(Peer, Party, StringComparison.OrdinalIgnoreCase) ||
               String.Equals(From, Party, StringComparison.OrdinalIgnoreCase) ||
               String.Equals(To,   Party, StringComparison.OrdinalIgnoreCase);

        #endregion

        #region ToJSON()

        /// <summary>
        /// The call as the traffic route and the event stream read it.
        /// </summary>
        public JObject ToJSON()

            => new (

                   new JProperty("id",             Id),
                   new JProperty("timestamp",      Timestamp.ToString("o")),
                   new JProperty("direction",      Direction == CallDirection.Inbound ? "in" : "out"),

                   new JProperty("peer",           Peer),
                   new JProperty("from",           From),
                   new JProperty("to",             To),

                   new JProperty("version",        Version),
                   new JProperty("module",         Module),
                   new JProperty("method",         Method),
                   new JProperty("path",           Path),

                   new JProperty("httpStatusCode", HTTPStatusCode),
                   new JProperty("ocpiStatusCode", OCPIStatusCode),
                   new JProperty("statusMessage",  OCPIStatusMessage),
                   new JProperty("ok",             Succeeded),
                   new JProperty("error",          Error),

                   new JProperty("durationMs",     Math.Round(Duration.TotalMilliseconds, 1)),
                   new JProperty("requestSize",    RequestSize),
                   new JProperty("responseSize",   ResponseSize),

                   new JProperty("requestId",      RequestId),
                   new JProperty("correlationId",  CorrelationId),
                   new JProperty("remoteSocket",   RemoteSocket),

                   new JProperty("requestBody",    RequestBody),
                   new JProperty("responseBody",   ResponseBody)

               );

        #endregion

        #region (override) ToString()

        public override String ToString()

            => $"{(Direction == CallDirection.Inbound ? "<-" : "->")} {Between}: " +
               $"{Method} {Path} -> {HTTPStatusCode}" +
               (OCPIStatusCode.HasValue ? $"/{OCPIStatusCode}" : "") +
               $" in {Duration.TotalMilliseconds:F0} ms";

        #endregion

    }

}
