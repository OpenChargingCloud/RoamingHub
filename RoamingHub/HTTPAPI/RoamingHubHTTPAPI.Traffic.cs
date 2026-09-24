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

using cloud.charging.open.RoamingHub.Web;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// The part of the JSON API that hands out what went between the peers:
    /// a page of it to read, and a Server-Sent Events stream to follow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Its own event source rather than a second kind of message on the one
    /// beside it, and that is a permission boundary rather than tidiness: the
    /// event log is what this hub did and anybody who may read the
    /// configuration may read it, and the traffic is what the peers did
    /// through this hub, which is a different question and its own
    /// permission. One stream carrying both would hand the second to whoever
    /// was granted the first.
    /// </para>
    /// <para>
    /// Both routes are the same shape as the log's: fetch what is there, note
    /// the highest identification, then follow the stream from it. A reader
    /// that was away for a minute asks again with "after" and gets exactly
    /// what it missed, which is what makes the two of them one picture rather
    /// than a snapshot and a guess.
    /// </para>
    /// </remarks>
    public partial class RoamingHubHTTPAPI
    {

        #region Data

        /// <summary>
        /// The Server-Sent Events source the traffic travels on.
        /// </summary>
        public const String  TrafficEventSourceName  = "traffic";

        /// <summary>
        /// The sub-event every call travels as.
        /// </summary>
        public const String  CallEventName           = "call";

        /// <summary>
        /// The most calls one request will hand out, however many are asked
        /// for.
        /// </summary>
        public const Int32   MaxCallsPerRequest      = 1_000;

        #endregion

        #region Properties

        /// <summary>
        /// The Server-Sent Events source of the traffic (/api/v1/traffic/events).
        /// </summary>
        public HTTPEventSource<JObject>  TrafficEvents  { get; private set; } = default!;

        #endregion


        #region (private) RegisterTrafficRoutes()

        /// <summary>
        /// Everything under /v1/traffic.
        /// </summary>
        private void RegisterTrafficRoutes()
        {

            TrafficEvents = this.AddJSONEventSource(
                                HTTPEventSource_Id.Parse(TrafficEventSourceName),
                                MaxNumberOfCachedEvents:  500,
                                RetryInterval:            TimeSpan.FromSeconds(2),
                                EnableLogging:            false
                            );

            RoamingHub.Traffic.OnCall += call => Publish(TrafficEvents, CallEventName, call.ToJSON());

            AddHandler(HTTPPath.Root + "v1/traffic",         GetTraffic,     HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/traffic/peers",   GetPartiesSeen, HTTPMethod.GET);

            AddHandler(HTTPMethod.GET,
                       HTTPPath.Root + "v1/traffic/events",
                       HTTPContentType.Text.EVENTSTREAM,
                       StreamTraffic);

        }

        #endregion

        #region (private) GetTraffic(Request)

        /// <summary>
        /// GET /api/v1/traffic?limit=&amp;after=&amp;peer=: what went between
        /// the peers, oldest of what comes back first.
        /// </summary>
        /// <remarks>
        /// "after" is how a reader catches up without asking for everything
        /// again, and "peer" narrows it to one party at either end of a call -
        /// which is the question somebody actually walks up with: "what has
        /// DE*GEF been doing here?"
        /// </remarks>
        private Task<HTTPResponse> GetTraffic(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permissions.ReadTraffic, false, out _, out var refused))
                return Task.FromResult(refused);

            var limit  = Request.QueryString.GetUInt32("limit") is { } asked
                             ? (Int32) Math.Min(asked, MaxCallsPerRequest)
                             : 200;

            var after  = Request.QueryString.GetUInt64("after");
            var peer   = Request.QueryString.GetString("peer")?.Trim();

            var calls  = RoamingHub.Traffic.Recent(limit, after, peer).ToArray();

            return Task.FromResult(
                       JSONResponse(
                           Request,
                           HTTPStatusCode.OK,
                           new JObject(
                               new JProperty("calls",     new JArray(calls.Select(call => call.ToJSON()))),
                               new JProperty("lastId",    RoamingHub.Traffic.LastId),
                               new JProperty("count",     RoamingHub.Traffic.Count),
                               new JProperty("capacity",  RoamingHub.Traffic.Capacity),
                               new JProperty("payloads",  RoamingHub.OCPI.Logging?.Payloads == true),
                               new JProperty("peers",     new JArray(RoamingHub.Traffic.KnownParties))
                           )
                       )
                   );

        }

        #endregion

        #region (private) GetPartiesSeen(Request)

        /// <summary>
        /// GET /api/v1/traffic/peers: every party that has been at either end
        /// of a call, which is what a filter is built from.
        /// </summary>
        private Task<HTTPResponse> GetPartiesSeen(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permissions.ReadTraffic, false, out _, out var refused))
                return Task.FromResult(refused);

            return Task.FromResult(
                       JSONResponse(
                           Request,
                           HTTPStatusCode.OK,
                           new JObject(
                               new JProperty("peers",  new JArray(RoamingHub.Traffic.KnownParties))
                           )
                       )
                   );

        }

        #endregion

        #region (private) StreamTraffic(Request)

        /// <summary>
        /// GET /api/v1/traffic/events: every call as it happens.
        /// </summary>
        /// <remarks>
        /// The same shape as the event stream beside it, and for the same
        /// reasons - see StreamEvents in RoamingHubHTTPAPI.cs, which this follows
        /// line for line, the header a proxy is told not to buffer it by and
        /// the comment it sends while silent included. What differs is the
        /// permission it asks for and the source it reads from.
        /// </remarks>
        private Task<HTTPResponse> StreamTraffic(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permissions.ReadTraffic, false, out _, out var refused))
                return Task.FromResult(refused);

            var clientId = Request.RemoteSocket.ToString();

            return Task.FromResult(
                       new HTTPResponse.Builder(Request) {

                           HTTPStatusCode  = HTTPStatusCode.OK,
                           Server          = HTTPServer.HTTPServerName,
                           ContentType     = HTTPContentType.Text.EVENTSTREAM,
                           CacheControl    = "no-cache",
                           Connection      = ConnectionType.KeepAlive,

                           HTTPSSEWorker   = async (response, stream) => {

                               using var ending = CancellationTokenSource.CreateLinkedTokenSource(
                                                      Request.CancellationToken,
                                                      shutdown.Token
                                                  );

                               try
                               {

                                   await stream.WriteAsync("retry: ");
                                   await stream.WriteAsync(((UInt32) TrafficEvents.RetryInterval.TotalMilliseconds).ToString());
                                   await stream.WriteAsync("\n\n");

                                   // Out of the buffer now rather than with the
                                   // first call: a hub whose peers are quiet
                                   // would otherwise leave the reader waiting
                                   // for its first byte until its own timeout.
                                   await stream.FlushAsync(ending.Token);

                                   await CarryEvents(TrafficEvents, clientId, Request, stream, ending);

                               }
                               catch (OperationCanceledException)
                               {
                                   await TrafficEvents.Unsubscribe(clientId);
                               }
                               catch (ObjectDisposedException)
                               {
                                   await TrafficEvents.Unsubscribe(clientId);
                               }
                               catch (Exception e)
                               {
                                   await TrafficEvents.Unsubscribe(clientId);

                                   // Not through the event log: a stream that
                                   // ends because the reader went away is the
                                   // normal end of one.
                                   System.Diagnostics.Debug.WriteLine($"The traffic stream of {clientId} ended: {e.Message}");
                               }

                           }

                       }.Set("X-Accel-Buffering", "no").
                         WithCommonSecurityHeaders().
                         AsImmutable
                   );

        }

        #endregion

    }

}
