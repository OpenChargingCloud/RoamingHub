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

using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using System.Collections.Concurrent;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using Org.BouncyCastle.Crypto.Parameters;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Hermod.Mail;

using cloud.charging.open.protocols.WWCP.POI;
using cloud.charging.open.protocols.WWCP.Node.TestKit;
using cloud.charging.open.RoamingHub.Configuration;

// Those of POI 3, not those of WWCP_Core the OCPI side knows.
using ChargingStationOperator  = cloud.charging.open.protocols.WWCP.POI.ChargingStationOperator;
using ChargingPool             = cloud.charging.open.protocols.WWCP.POI.ChargingPool;
using ChargingStation          = cloud.charging.open.protocols.WWCP.POI.ChargingStation;
using EVSE                     = cloud.charging.open.protocols.WWCP.POI.EVSE;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    #region POIStatusConfigurationTests

    /// <summary>
    /// The "statusRoles" of the "poi" section: the roles whose accounts may set the runtime status, each bound to the
    /// charging station operators whose status it may set; the one sentence naming the field when it is wrong, and the
    /// start stopped by a role this hub does not have, or that may not set a status.
    /// </summary>
    [TestFixture]
    public class POIStatusConfigurationTests
    {

        #region RolesBoundToOperatorsAreReadAndWrittenAgain()

        [Test]
        public void RolesBoundToOperatorsAreReadAndWrittenAgain()
        {

            var section = new JObject(
                              new JProperty(POIConfiguration.StatusRolesName, new JObject(
                                  new JProperty("gef-feed",  new JArray("DE*GEF")),
                                  new JProperty("feeds",     new JArray("DE*GEF", "DE*QAC"))
                              ))
                          );

            Assert.That(RoamingHubConfiguration.TryParse(new JObject(new JProperty(POIConfiguration.SectionName, section)), out var configuration, out var error), Is.True, error);

            var statusRoles = configuration!.POI!.EffectiveStatusRoles;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(statusRoles.Keys,                                                         Is.EquivalentTo(new[] { "gef-feed", "feeds" }));
                Assert.That(statusRoles["gef-feed"].Select(operatorId => operatorId.ToString()),      Is.EqualTo(new[] { "DE*GEF" }));
                Assert.That(statusRoles["feeds"].   Select(operatorId => operatorId.ToString()),      Is.EqualTo(new[] { "DE*GEF", "DE*QAC" }));
                Assert.That(JToken.DeepEquals(configuration.ToJSON()[POIConfiguration.SectionName], section), Is.True);
                Assert.That(configuration.ToString(),                                                 Does.Contain("their status set by 'gef-feed' for DE*GEF"));
                Assert.That(new POIConfiguration().EffectiveStatusRoles,                              Is.Empty, "Nobody sets a status, unless the file says so.");
            }

        }

        #endregion

        #region AStatusRoleThatIsWrongIsNamed(Value, Said)

        [TestCase("[]",                               "'poi.statusRoles' must be a JSON object")]
        [TestCase("{ \"9feed\": [ \"DE*GEF\" ] }",    "'poi.statusRoles' names '9feed', which is not a role name")]
        [TestCase("{ \"gef-feed\": [] }",             "'poi.statusRoles.gef-feed' names no charging station operator")]
        [TestCase("{ \"gef-feed\": \"DE*GEF\" }",     "'poi.statusRoles.gef-feed' must be an array")]
        [TestCase("{ \"gef-feed\": [ \"GEF\" ] }",    "'poi.statusRoles.gef-feed' names 'GEF', which is no charging station operator")]
        public void AStatusRoleThatIsWrongIsNamed(String Value, String Said)
        {

            var json = new JObject(new JProperty(POIConfiguration.SectionName, new JObject(new JProperty(POIConfiguration.StatusRolesName, JToken.Parse(Value)))));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(RoamingHubConfiguration.TryParse(json, out _, out var error), Is.False);
                Assert.That(error,                                                         Does.Contain(Said));
            }

        }

        #endregion

        #region ARoleTheHubDoesNotHaveOrThatMayNotSetAStatusStopsTheStart(Roles, Said)

        /// <summary>
        /// A binding that would let nobody set anything is a typo, and stops the start: a role this hub does not have, and
        /// one the "roles" section did not give "poi-status:edit".
        /// </summary>
        [TestCase(null,                          "binds the role 'gef-feed', which this roaming hub does not have")]
        [TestCase("[ \"configuration:read\" ]",  "binds the role 'gef-feed', which may not \"poi-status:edit\"")]
        public void ARoleTheHubDoesNotHaveOrThatMayNotSetAStatusStopsTheStart(String? Roles, String Said)
        {

            var directory      = TestRoamingHubs.TemporaryDirectory("poi-status");
            var configuration  = TestRoamingHubs.Offline;

            if (Roles is not null)
                configuration.Add("roles", new JObject(new JProperty("gef-feed", JToken.Parse(Roles))));

            configuration.Add(POIConfiguration.SectionName, new JObject(new JProperty(POIConfiguration.StatusRolesName, new JObject(new JProperty("gef-feed", new JArray("DE*GEF"))))));

            try
            {
                Assert.That(() => TestRoamingHubs.New(directory, configuration),
                            Throws.InvalidOperationException.With.Message.Contains(Said));
            }
            finally
            {
                TestRoamingHubs.Remove(directory);
            }

        }

        #endregion

    }

    #endregion

    #region POIStatusTests

    /// <summary>
    /// The runtime status set over the WebSocket of a history: by a charging station operator among the peers, with the
    /// token it calls the hub with, and by an account whose role the "poi" section binds to DE*GEF, with its API key -
    /// each the status of what its operators own, and nothing else; asked again at every status, so that a peer suspended
    /// or removed sets nothing from then on. Anybody else reads and subscribes, and credentials the hub does not know are
    /// refused at the upgrade.
    /// </summary>
    public class POIStatusTests : ARoamingHubTests
    {

        #region Data

        private const String  GEFEVSE   = "DE*GEF*E1*1";
        private const String  QACEVSE   = "DE*QAC*E1*1";

        #endregion

        #region Configuration

        /// <summary>
        /// A role that may set a status, bound to DE*GEF.
        /// </summary>
        protected override JObject Configuration
        {
            get
            {

                var configuration = TestRoamingHubs.Offline;

                configuration.Add("roles",                       new JObject(new JProperty("gef-feed", new JArray($"{HubAccess.POIStatus}:edit"))));
                configuration.Add(POIConfiguration.SectionName,  new JObject(new JProperty(POIConfiguration.StatusRolesName, new JObject(new JProperty("gef-feed", new JArray("DE*GEF"))))));

                return configuration;

            }
        }

        #endregion


        #region (class) WebSocketClient

        /// <summary>
        /// A client of the WebSocket of a history, as example A12 of WWCP POI has one: a .NET ClientWebSocket, which
        /// matches the answers to its requests by their identifications and keeps the pushes.
        /// </summary>
        private sealed class WebSocketClient : IAsyncDisposable
        {

            private readonly ClientWebSocket                                                     webSocket  = new ();
            private readonly ConcurrentDictionary<String, TaskCompletionSource<POIWebSocketMessage>>  pending    = new ();
            private readonly Channel<POIWebSocketMessage>                                        pushes     = Channel.CreateUnbounded<POIWebSocketMessage>();
            private          Task?                                                               receiver;
            private          Int32                                                               nextId;

            /// <summary>
            /// Connect with the given credentials, and say why not: the HTTP status code of a refused upgrade.
            /// </summary>
            public static async Task<(WebSocketClient? Client, HttpStatusCode StatusCode)> TryConnect(Uri                               URL,
                                                                                                      Action<ClientWebSocketOptions>?  Credentials   = null)
            {

                var client = new WebSocketClient();

                client.webSocket.Options.CollectHttpResponseDetails = true;
                Credentials?.Invoke(client.webSocket.Options);

                try
                {
                    await client.webSocket.ConnectAsync(URL, new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
                }
                catch (WebSocketException)
                {
                    var statusCode = client.webSocket.HttpStatusCode;
                    client.webSocket.Dispose();
                    return (null, statusCode);
                }

                client.receiver = client.Receive();

                return (client, client.webSocket.HttpStatusCode);

            }

            /// <summary>
            /// Connect with the given credentials, or fail the test.
            /// </summary>
            public static async Task<WebSocketClient> Connect(Uri                               URL,
                                                              Action<ClientWebSocketOptions>?  Credentials   = null)
            {

                var (client, statusCode) = await TryConnect(URL, Credentials);

                Assert.That(client, Is.Not.Null, $"The upgrade was answered {(Int32) statusCode}.");

                return client!;

            }

            private async Task Receive()
            {

                var buffer = new Byte[64 * 1024];

                try
                {
                    while (webSocket.State == WebSocketState.Open)
                    {

                        using var message = new MemoryStream();
                        WebSocketReceiveResult result;

                        do
                        {
                            result = await webSocket.ReceiveAsync(buffer, CancellationToken.None);
                            message.Write(buffer, 0, result.Count);
                        }
                        while (!result.EndOfMessage);

                        if (result.MessageType == WebSocketMessageType.Close)
                            return;

                        if (!POIWebSocketMessage.TryParse(Encoding.UTF8.GetString(message.ToArray()), out var received, out _))
                            continue;

                        if (received.MessageType == POIWebSocketMessageType.Send)
                            pushes.Writer.TryWrite(received);

                        else if (pending.TryRemove(received.MessageId, out var waiting))
                            waiting.TrySetResult(received);

                    }
                }
                catch (WebSocketException)
                { }

            }

            /// <summary>
            /// Send a request and return its answer.
            /// </summary>
            public async Task<POIWebSocketMessage> Request(String    Action,
                                                           JObject?  Payload   = null)
            {

                var messageId  = Interlocked.Increment(ref nextId).ToString();
                var answer     = pending.GetOrAdd(messageId, _ => new TaskCompletionSource<POIWebSocketMessage>(TaskCreationOptions.RunContinuationsAsynchronously));

                await webSocket.SendAsync(Encoding.UTF8.GetBytes(POIWebSocketMessage.Request(messageId, Action, Payload ?? []).ToJSONText()),
                                          WebSocketMessageType.Text,
                                          true,
                                          CancellationToken.None);

                return await answer.Task.WaitAsync(TimeSpan.FromSeconds(10));

            }

            /// <summary>
            /// The next push of the given action, e.g. "statusUpdate".
            /// </summary>
            public async Task<POIWebSocketMessage> NextPush(String Action)
            {

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                while (true)
                {
                    var push = await pushes.Reader.ReadAsync(timeout.Token);
                    if (push.Action == Action)
                        return push;
                }

            }

            public async ValueTask DisposeAsync()
            {

                try
                {
                    if (webSocket.State == WebSocketState.Open)
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token);
                }
                catch (Exception)
                { }

                webSocket.Dispose();

                if (receiver is not null)
                    await Task.WhenAny(receiver, Task.Delay(TimeSpan.FromSeconds(2)));

            }

        }

        #endregion

        #region (helper) URL

        /// <summary>
        /// The WebSocket of the history of QA.
        /// </summary>
        private Uri URL
            => new ($"ws{BaseURL[4..]}{RoamingHub.POIPathSegment}/RNs/QA/history/ws");

        #endregion

        #region (helper) WithToken(Token) / WithBearer(Token) / WithAPIKey(Key)

        /// <summary>
        /// The token a peer calls this hub with over OCPI, as OCPI 2.2 writes it: base64 encoded.
        /// </summary>
        private static Action<ClientWebSocketOptions> WithToken(String Token)
            => options => options.SetRequestHeader("Authorization", "Token " + Convert.ToBase64String(Encoding.UTF8.GetBytes(Token)));

        /// <summary>
        /// The same token as a WebSocket client writes one: as it is.
        /// </summary>
        private static Action<ClientWebSocketOptions> WithBearer(String Token)
            => options => options.SetRequestHeader("Authorization", "Bearer " + Token);

        /// <summary>
        /// The API key of an account, as on every route of the JSON API.
        /// </summary>
        private static Action<ClientWebSocketOptions> WithAPIKey(String Key)
            => options => options.SetRequestHeader("API-Key", Key);

        #endregion

        #region (helper) QAWithTwoOperators()

        /// <summary>
        /// QA made, with DE*GEF and DE*QAC, each with a charging pool, a charging station and an EVSE.
        /// </summary>
        private async Task QAWithTwoOperators()
        {

            Assert.That(RoamingHub.CreateRoamingNetwork(RoamingNetwork_Id.Parse("QA")).Success, Is.True);

            foreach (var operatorId in new[] { "DE*GEF", "DE*QAC" })
            {

                var signingKey  = AddedOperator(operatorId);

                var pool        = new ChargingPool(
                                      ChargingPool_Id.Parse($"{operatorId}*P1"),
                                      ChargingStations:  [
                                          new ChargingStation(
                                              ChargingStation_Id.Parse($"{operatorId}*S1"),
                                              EVSEs: [ new EVSE(EVSE_Id.Parse($"{operatorId}*E1*1")) ]
                                          )
                                      ]
                                  );

                var changeSet   = History.Head.RoamingNetwork.CreateChangeSet([ RoamingNetworkChange.Add(pool) ]).
                                                             Sign(signingKey, POIPublicKey.From(signingKey).Id, COSEAlgorithm.ES256);

                using var anybody   = Anonymous();
                using var request   = new HttpRequestMessage(new HttpMethod("COMMIT"), $"{RoamingHub.POIPathSegment}/RNs/QA/history/changeSets") {
                                          Content = new StringContent(CanonicalJSON.Serialize(changeSet.ToJSON()), Encoding.UTF8, "application/json")
                                      };
                using var response  = await anybody.SendAsync(request);

                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());

            }

        }

        /// <summary>
        /// The charging station operator of the given identification added to QA by the hub, with keys of its own, once
        /// it signed with its identity key; its signing key.
        /// </summary>
        private ECPrivateKeyParameters AddedOperator(String Id)
        {

            var signingKey   = POITests.NewKey(COSECurve.P256);
            var identityKey  = POITests.NewKey(COSECurve.P521);

            var asked        = RoamingHub.AddParty(RoamingNetwork_Id.Parse("QA"),
                                                   nameof(ChargingStationOperator),
                                                   new JObject(new JProperty("@id",           Id),
                                                               new JProperty("signingKeys",   new JArray(POIPublicKey.From(signingKey). ToJSON())),
                                                               new JProperty("identityKeys",  new JArray(POIPublicKey.From(identityKey).ToJSON()))));

            Assert.That(asked.ChangeSet, Is.Not.Null, asked.Message);

            var proof        = asked.ChangeSet!.Sign(identityKey, POIPublicKey.From(identityKey).Id, COSEAlgorithm.ES512).Signatures[^1];
            var result       = History.AddPendingSignatures(asked.ChangeSet.Id, [ proof ]);

            Assert.That(result.IsCommitted, Is.True, result.ErrorResponse);

            return signingKey;

        }

        #endregion

        #region (helper) History

        private RoamingNetworkHistory History
        {
            get
            {
                Assert.That(RoamingHub.POI.TryGetRoamingNetworkHistory(RoamingNetwork_Id.Parse("QA"), out var history), Is.True, "QA is not served");
                return history!;
            }
        }

        #endregion

        #region (helper) AddPeer(Admin, PartyId, Role)

        /// <summary>
        /// A peer added through the JSON API, on OCPI 2.2.1; its identification and the token this hub made up for it.
        /// </summary>
        private static async Task<(String Id, String Token)> AddPeer(HttpClient  Admin,
                                                                     String      PartyId   = "GEF",
                                                                     String      Role      = "CPO")
        {

            var response  = await Admin.PostAsync("/api/v1/ocpi/partners",
                                                  JSONBody(new JProperty("version",      "2.2.1"),
                                                           new JProperty("countryCode",  "DE"),
                                                           new JProperty("partyId",      PartyId),
                                                           new JProperty("role",         Role),
                                                           new JProperty("name",         $"Test {Role}")));

            var text      = await response.Content.ReadAsStringAsync();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), text);

            var answer    = JObject.Parse(text);

            return (answer.Value<String>("id")!, answer.Value<String>("ourToken")!);

        }

        #endregion

        #region (helper) AccountWithAPIKey(Name, Role = null, Rights = ReadWrite)

        /// <summary>
        /// An account of the given name, in the group of the given role, if any, and an API key of it, made the way the
        /// "apiKeys" command makes one; the key.
        /// </summary>
        private async Task<String> AccountWithAPIKey(String         Name,
                                                     String?        Role     = null,
                                                     APIKeyRights   Rights   = APIKeyRights.ReadWrite)
        {

            Assert.That(RoamingHub.ExtAPI.TryGetOrganization(Organization_Id.Parse(RoamingHub.DefaultOrganization), out var organization) &&
                        organization is Organization, Is.True, "the hub's organization is not there");

            if (!RoamingHub.ExtAPI.TryGetUser(User_Id.Parse(Name), out var account))
            {

                account = await RoamingHub.ExtAPI.CreateUser(
                                    User_Id.Parse(Name),
                                    I18NString.Create(Languages.en, Name),
                                    SimpleEMailAddress.Parse($"{Name}@localhost"),
                                    User2OrganizationEdgeLabel.IsMember,
                                    (Organization) organization!,
                                    SkipDefaultNotifications:  true,
                                    SkipNewUserEMail:          true,
                                    SkipNewUserNotifications:  true,
                                    AcceptedEULA:              DateTimeOffset.UtcNow.AddSeconds(-1),
                                    IsAuthenticated:           true
                                );

                Assert.That(RoamingHub.ExtAPI.TryGetUser(User_Id.Parse(Name), out account), Is.True, $"the account '{Name}' was not made");

            }

            if (Role is not null)
            {

                Assert.That(RoamingHub.ExtAPI.TryGetUserGroup(UserGroup_Id.Parse(Role), out var group), Is.True, $"the hub has no group '{Role}'");

                var joined = await RoamingHub.ExtAPI.AddUserToUserGroup((User) account!, User2UserGroupEdgeLabel.IsMember, (UserGroup) group!);

                Assert.That(joined.IsSuccess, Is.True, $"'{Name}' could not be put in '{Role}'");

            }

            var key    = Guid.NewGuid().ToString("N");

            var added  = await RoamingHub.ExtAPI.AddAPIKey(new APIKey(APIKey_Id.Parse(key),
                                                                      account!.Id,
                                                                      AccessRights:  Rights,
                                                                      Created:       DateTimeOffset.UtcNow));

            Assert.That(added.Result, Is.EqualTo(CommandResult.Success), added.Description?.FirstText());

            return key;

        }

        #endregion

        #region (helper) SetStatus(Client, EntityId, Value)

        /// <summary>
        /// Set the status of the EVSE of the given identification.
        /// </summary>
        private static Task<POIWebSocketMessage> SetStatus(WebSocketClient  Client,
                                                           String           EVSEId,
                                                           String           Value)

            => Client.Request("setStatus", new JObject(
                                               new JProperty(POIStatusUpdate.PropertyKeys.Kind,        POIStatusUpdate.KindText(POIRuntimeStatusKind.Status)),
                                               new JProperty(POIStatusUpdate.PropertyKeys.EntityType,  nameof(EVSE)),
                                               new JProperty(POIStatusUpdate.PropertyKeys.EntityId,    EVSEId),
                                               new JProperty(POIStatusUpdate.PropertyKeys.Value,       Value)
                                           ));

        #endregion

        #region (helper) StatusOf(EVSEId)

        /// <summary>
        /// The status of the EVSE of the given identification in QA now, or null.
        /// </summary>
        private String? StatusOf(String EVSEId)

            => History.RuntimeStatus.TryGet(POIRuntimeStatusKind.Status, nameof(EVSE), EVSEId, out var status)
                   ? status.Value
                   : null;

        #endregion

        #region (helper) Said(Tag = "status")

        /// <summary>
        /// What the log says, tagged "status".
        /// </summary>
        private IReadOnlyList<String> Said()

            => [.. RoamingHub.Log.Recent(500, Tag: "status").Select(entry => entry.Message)];

        #endregion


        #region ACPOPeerSetsTheStatusOfItsOwnEVSEAndNotOfAnotherOperators()

        /// <summary>
        /// DE*GEF, a charging station operator among the peers, sets the status of its EVSE with the token it calls the hub
        /// with over OCPI, base64 encoded or as it is: a subscriber is told, and the runtime status says so. The EVSE of
        /// DE*QAC it may not set. The log names the peer, and not its token.
        /// </summary>
        [Test]
        public async Task ACPOPeerSetsTheStatusOfItsOwnEVSEAndNotOfAnotherOperators()
        {

            await QAWithTwoOperators();

            using var admin              = await SignedIn();

            var (peerId, token)          = await AddPeer(admin);

            await using var subscriber   = await WebSocketClient.Connect(URL);
            var subscribed               = await subscriber.Request("subscribe", new JObject(new JProperty("topics", new JArray(POI_API.StatusUpdatesTopic))));

            await using var gef          = await WebSocketClient.Connect(URL, WithToken(token));

            var charging                 = await SetStatus(gef, GEFEVSE, "charging");
            var pushed                   = await subscriber.NextPush("statusUpdate");
            var ofQAC                    = await SetStatus(gef, QACEVSE, "outOfService");

            await using var bearer       = await WebSocketClient.Connect(URL, WithBearer(token));

            var available                = await SetStatus(bearer, GEFEVSE, "available");
            var runtimeStatus            = await subscriber.Request("runtimeStatus");
            var said                     = Said();

            using (Assert.EnterMultipleScope())
            {

                Assert.That(subscribed.MessageType,                                   Is.EqualTo(POIWebSocketMessageType.Response));

                Assert.That(charging.MessageType,                                     Is.EqualTo(POIWebSocketMessageType.Response), charging.ErrorDescription);
                Assert.That(charging.Payload?["result"]?.Value<String>(),             Is.EqualTo("Updated"));
                Assert.That(pushed.Payload?["entityId"]?.Value<String>(),             Is.EqualTo(GEFEVSE));
                Assert.That(pushed.Payload?["value"]?.Value<String>(),                Is.EqualTo("charging"));

                Assert.That(ofQAC.MessageType,                                        Is.EqualTo(POIWebSocketMessageType.RequestError));
                Assert.That(ofQAC.ErrorCode,                                          Is.EqualTo(POIWebSocketErrorCodes.SecurityError));
                Assert.That(StatusOf(QACEVSE),                                        Is.Not.EqualTo("outOfService"));

                Assert.That(available.Payload?["result"]?.Value<String>(),            Is.EqualTo("Updated"), available.ErrorDescription);
                Assert.That(StatusOf(GEFEVSE),                                        Is.EqualTo("available"));
                Assert.That(runtimeStatus.Payload?["runtimeStatus"]?.Any(status => status["entityId"]?.Value<String>() == GEFEVSE &&
                                                                                   status["value"]?.   Value<String>() == "available"), Is.True,
                            runtimeStatus.Payload?.ToString());

                Assert.That(said, Has.Some.Contains($"The peer '{peerId}' sets the status of the EVSE '{GEFEVSE}' in the roaming network 'QA' to 'charging'"));
                Assert.That(said, Has.Some.Contains($"The peer '{peerId}' was refused the status of the EVSE '{QACEVSE}' in the roaming network 'QA': " +
                                                    "it may set the status of DE*GEF, not of DE*QAC"));
                Assert.That(said, Has.None.Contains(token));

            }

        }

        #endregion

        #region ASuspendedThenRemovedPeerIsRefused()

        /// <summary>
        /// Suspended, DE*GEF sets nothing more over the WebSocket it opened before, and opens none; removed, neither:
        /// asked again at every status, not only at the upgrade.
        /// </summary>
        [Test]
        public async Task ASuspendedThenRemovedPeerIsRefused()
        {

            await QAWithTwoOperators();

            using var admin          = await SignedIn();

            var (peerId, token)      = await AddPeer(admin);

            await using var gef      = await WebSocketClient.Connect(URL, WithToken(token));

            var before               = await SetStatus(gef, GEFEVSE, "charging");

            var suspend              = await admin.PostAsync("/api/v1/ocpi/peers/DEGEF/suspend", JSONBody());
            var whileSuspended       = await SetStatus(gef, GEFEVSE, "available");
            var (_, suspendedAgain)  = await WebSocketClient.TryConnect(URL, WithToken(token));

            var removed              = await RoamingHub.RemoveRemotePartyAsync("2.2.1", peerId);
            var afterRemoval         = await SetStatus(gef, GEFEVSE, "available");
            var (_, removedAgain)    = await WebSocketClient.TryConnect(URL, WithToken(token));

            using (Assert.EnterMultipleScope())
            {

                Assert.That(before.Payload?["result"]?.Value<String>(),   Is.EqualTo("Updated"), before.ErrorDescription);

                Assert.That(suspend.StatusCode,                           Is.EqualTo(HttpStatusCode.OK), await suspend.Content.ReadAsStringAsync());
                Assert.That(whileSuspended.ErrorCode,                     Is.EqualTo(POIWebSocketErrorCodes.SecurityError));
                Assert.That(suspendedAgain,                               Is.EqualTo(HttpStatusCode.Unauthorized));

                Assert.That(removed.Success,                              Is.True, removed.Message);
                Assert.That(afterRemoval.ErrorCode,                       Is.EqualTo(POIWebSocketErrorCodes.SecurityError));
                Assert.That(removedAgain,                                 Is.EqualTo(HttpStatusCode.Unauthorized));

                Assert.That(StatusOf(GEFEVSE),                            Is.EqualTo("charging"));
                Assert.That(Said(),                                       Has.Some.Contains($"the peer '{peerId}' is suspended"));
                Assert.That(Said(),                                       Has.Some.Contains("its token is the token of no peer of this hub"));

            }

        }

        #endregion

        #region AnEMSPPeerSetsNothing()

        /// <summary>
        /// An EMSP among the peers opens the WebSocket with its token, reads, and sets nothing: it owns no EVSE.
        /// </summary>
        [Test]
        public async Task AnEMSPPeerSetsNothing()
        {

            await QAWithTwoOperators();

            using var admin        = await SignedIn();

            var (peerId, token)    = await AddPeer(admin, PartyId: "EMP", Role: "EMSP");

            await using var emsp   = await WebSocketClient.Connect(URL, WithToken(token));

            var head               = await emsp.Request("head");
            var refused            = await SetStatus(emsp, GEFEVSE, "charging");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(head.MessageType,     Is.EqualTo(POIWebSocketMessageType.Response));
                Assert.That(refused.ErrorCode,    Is.EqualTo(POIWebSocketErrorCodes.SecurityError));
                Assert.That(StatusOf(GEFEVSE),    Is.Not.EqualTo("charging"));
                Assert.That(Said(),               Has.Some.Contains($"The peer '{peerId}' was refused the status of the EVSE '{GEFEVSE}' in the roaming network 'QA': " +
                                                                    "it is no charging station operator"));
            }

        }

        #endregion

        #region AnAccountWhoseRoleIsBoundToDEGEFSetsItsStatusOnly()

        /// <summary>
        /// An account in "gef-feed", which the "poi" section binds to DE*GEF, sets the status of the EVSE of DE*GEF with its
        /// API key, and not of DE*QAC; a read-only key of it sets nothing, and neither does an account without the role.
        /// </summary>
        [Test]
        public async Task AnAccountWhoseRoleIsBoundToDEGEFSetsItsStatusOnly()
        {

            await QAWithTwoOperators();

            var feedKey               = await AccountWithAPIKey("gefapp", "gef-feed");
            var readOnlyKey           = await AccountWithAPIKey("gefapp", Rights: APIKeyRights.ReadOnly);
            var otherKey              = await AccountWithAPIKey("other");

            await using var feed      = await WebSocketClient.Connect(URL, WithAPIKey(feedKey));
            await using var readOnly  = await WebSocketClient.Connect(URL, WithAPIKey(readOnlyKey));
            await using var other     = await WebSocketClient.Connect(URL, WithAPIKey(otherKey));

            var ofGEF                 = await SetStatus(feed,     GEFEVSE, "charging");
            var ofQAC                 = await SetStatus(feed,     QACEVSE, "charging");
            var byReadOnly            = await SetStatus(readOnly, GEFEVSE, "available");
            var byOther               = await SetStatus(other,    GEFEVSE, "available");

            using (Assert.EnterMultipleScope())
            {

                Assert.That(ofGEF.Payload?["result"]?.Value<String>(),   Is.EqualTo("Updated"), ofGEF.ErrorDescription);
                Assert.That(StatusOf(GEFEVSE),                           Is.EqualTo("charging"));

                Assert.That(ofQAC.ErrorCode,                             Is.EqualTo(POIWebSocketErrorCodes.SecurityError));
                Assert.That(byReadOnly.ErrorCode,                        Is.EqualTo(POIWebSocketErrorCodes.SecurityError));
                Assert.That(byOther.ErrorCode,                           Is.EqualTo(POIWebSocketErrorCodes.SecurityError));
                Assert.That(StatusOf(QACEVSE),                           Is.Not.EqualTo("charging"));

                Assert.That(Said(), Has.Some.Contains($"The account 'gefapp' sets the status of the EVSE '{GEFEVSE}'"));
                Assert.That(Said(), Has.Some.Contains($"The account 'gefapp' was refused the status of the EVSE '{GEFEVSE}' in the roaming network 'QA': its API key is read-only"));
                Assert.That(Said(), Has.Some.Contains($"The account 'other' was refused the status of the EVSE '{GEFEVSE}' in the roaming network 'QA': none of its roles"));
                Assert.That(Said(), Has.None.Contains(feedKey));

            }

        }

        #endregion

        #region AnybodyReadsAndSubscribesButSetsNothing()

        /// <summary>
        /// Without credentials, the WebSocket of a history is open as the history is over HTTP: its head, its runtime status
        /// and its topics; a status it sets nothing.
        /// </summary>
        [Test]
        public async Task AnybodyReadsAndSubscribesButSetsNothing()
        {

            await QAWithTwoOperators();

            await using var anybody  = await WebSocketClient.Connect(URL);

            var head                 = await anybody.Request("head");
            var runtimeStatus        = await anybody.Request("runtimeStatus");
            var subscribed           = await anybody.Request("subscribe", new JObject(new JProperty("topics", new JArray(POI_API.StatusUpdatesTopic))));
            var refused              = await SetStatus(anybody, GEFEVSE, "charging");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(head.MessageType,            Is.EqualTo(POIWebSocketMessageType.Response));
                Assert.That(runtimeStatus.MessageType,   Is.EqualTo(POIWebSocketMessageType.Response));
                Assert.That(subscribed.MessageType,      Is.EqualTo(POIWebSocketMessageType.Response));
                Assert.That(refused.ErrorCode,           Is.EqualTo(POIWebSocketErrorCodes.SecurityError));
                Assert.That(StatusOf(GEFEVSE),           Is.Not.EqualTo("charging"));
            }

        }

        #endregion

        #region CredentialsTheHubDoesNotKnowAreRefusedAtTheUpgrade()

        /// <summary>
        /// A token of no peer, an API key of no account and a password are answered 401 at the upgrade, and said in the
        /// log without the credentials.
        /// </summary>
        [Test]
        public async Task CredentialsTheHubDoesNotKnowAreRefusedAtTheUpgrade()
        {

            await QAWithTwoOperators();

            var (wrongToken,   tokenStatus)     = await WebSocketClient.TryConnect(URL, WithToken("wrong-token-of-nobody"));
            var (wrongKey,     keyStatus)       = await WebSocketClient.TryConnect(URL, WithAPIKey("wrongAPIKeyOfNoAccount0"));
            var (withPassword, passwordStatus)  = await WebSocketClient.TryConnect(URL, options => options.SetRequestHeader("Authorization",
                                                                                                                             "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{RoamingHub.DefaultAdminUser}:{Password}"))));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(wrongToken,      Is.Null);
                Assert.That(tokenStatus,     Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(wrongKey,        Is.Null);
                Assert.That(keyStatus,       Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(withPassword,    Is.Null);
                Assert.That(passwordStatus,  Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(Said(),          Has.Some.Contains("was refused the WebSocket of the roaming network 'QA'"));
                Assert.That(Said(),          Has.None.Contains("wrong-token-of-nobody"));
                Assert.That(Said(),          Has.None.Contains("wrongAPIKeyOfNoAccount0"));
                Assert.That(Said(),          Has.None.Contains(Password));
            }

        }

        #endregion

    }

    #endregion

}
