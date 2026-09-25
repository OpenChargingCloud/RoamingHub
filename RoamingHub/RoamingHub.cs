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
using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.DNS;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Norn.NTS;

using cloud.charging.open.protocols.WWCP.Node;
using cloud.charging.open.protocols.WWCP.Node.Configuration;
using cloud.charging.open.protocols.WWCP.Node.Logging;

using cloud.charging.open.RoamingHub.Configuration;
using cloud.charging.open.RoamingHub.Logging;
using cloud.charging.open.RoamingHub.Web;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// One OCPI roaming hub: the endpoints its peers call, the HTTP server in
    /// front of them, the JSON API at "/api" and the web interface at "/".
    /// </summary>
    /// <remarks>
    /// <para>
    /// A hub sits between the charge point operators and the e-mobility
    /// service providers so that they do not each have to be peered with all
    /// the others. Every one of them is peered with the hub instead, once, and
    /// the hub is what turns that into a mesh.
    /// </para>
    /// <para>
    /// <b>What it is before it is a hub.</b> A WWCP node, as the vehicle and
    /// the charging station are: the log, the configuration file, name
    /// resolution and the time, the accounts and the HTTP server with the web
    /// interface in front are <see cref="WWCPNode"/>'s, and this class is what
    /// a hub adds to one - its section of the file, its roles, its JSON API,
    /// and OCPI.
    /// </para>
    /// <para>
    /// <b>What is here so far.</b> The peering: a peer added, a token handed
    /// out, and the credentials exchanged in either direction. And the one
    /// module a hub has of its own: hubclientinfo, which tells the peers who
    /// else is on this hub and whether they can be reached right now - see
    /// RoamingHub.HubClientInfo.cs. Forwarding what one peer sends to another
    /// is not here yet.
    /// </para>
    /// <para>
    /// <b>What is here that the others do not have.</b> A record of every
    /// OCPI call that touched this hub, in both directions, with the two
    /// parties of it, on a stream a browser or a script can follow as it
    /// happens - see <see cref="Traffic"/>. Two peers that talk directly can
    /// each read their own log; with a hub between them, neither of them can
    /// say what the other actually sent, and that is the hub's to answer.
    /// </para>
    /// <para>
    /// The OCPI endpoints sit below the HTTPExt API's "/ext" - see
    /// <see cref="OCPIBaseURL"/> - because the OCPI library attaches to the
    /// HTTPExt API rather than to the server.
    /// </para>
    /// <para>
    /// The web interface is a bundle of HTML, CSS and JavaScript built by
    /// webpack from Frontend/ and embedded into this assembly - see
    /// <see cref="HTTPRoot"/> - so that a hub needs nothing installed beside
    /// it to be looked at in a browser. The browser and the hub talk over the
    /// JSON API and its Server-Sent Events streams; nothing is rendered on
    /// the server.
    /// </para>
    /// </remarks>
    public partial class RoamingHub : WWCPNode
    {

        #region Data

        /// <summary>
        /// The manifest resource prefix the frontend bundle is embedded under
        /// (see the EmbedFrontend target of RoamingHub.csproj).
        /// </summary>
        /// <remarks>
        /// Named the way the other four components name theirs, so that the
        /// bundle is where a reader of any of them would look for it. The
        /// build stops rather than make this assembly without one, so a hub
        /// always has a web interface to serve - unless --frontend points it
        /// at a directory on disk instead.
        /// </remarks>
        public const String  HTTPRoot                  = "cloud.charging.open.RoamingHub.HTTPRoot.";

        /// <summary>
        /// The TCP port the web interface listens on, unless another is given.
        /// </summary>
        /// <remarks>
        /// Beyond the ports the other OpenChargingCloud boxes use - a vehicle
        /// 2347, a charging station 2348 and 2349, a local controller 2350, a
        /// CSMS 2351 and its OCPP ports up to 2354, an EMSP 2355 - and not one
        /// of them: all of these are routinely tried out on the same bench,
        /// and two web interfaces fighting over one socket is a confusing way
        /// to find that out.
        /// </remarks>
        public static new readonly IPPort DefaultHTTPPort   = IPPort.Parse(2356);

        /// <summary>
        /// The organization the accounts of this hub belong to.
        /// </summary>
        /// <remarks>
        /// A hub on a bench has no organizations to speak of, and this one
        /// exists because the HTTPExt API's sign-in refuses an account that is
        /// in none - "You do not have access to any organization!" - however
        /// right its password is. So there is exactly one, named after the
        /// thing it stands for. Not "Hub", which an organization identification
        /// is too short to be: they have to be at least four characters, and a
        /// name that is refused at the first start is a program that does not
        /// start.
        ///
        /// Written into the accounts at the first start and read back at every
        /// start after it, which makes it the one name of this kind of node that
        /// must never change: a hub that has run is in this organization.
        /// </remarks>
        public const String  DefaultOrganization       = "RoamingHub";

        /// <summary>
        /// What a line the libraries below write has to contain to be tagged,
        /// and with what: the table the debug bridge of a hub reads by.
        /// Ordered, and searched regardless of case; a line may collect
        /// several.
        /// </summary>
        /// <remarks>
        /// What a hub overhears is mostly OCPI going past it in both
        /// directions, so the table leans that way: which module a line is
        /// about - the credentials, the locations, the tokens - is worth more
        /// here than which layer it came from. None of the vehicle's ISO 15118
        /// and SLAC, which a hub never hears. The table the hub read by before
        /// it was a node, word for word.
        /// </remarks>
        public static readonly IReadOnlyList<(String Needle, String Tag)> TraceTags = [
            ("ocpi",           "ocpi"),
            ("credentials",    "credentials"),
            ("versions",       "versions"),
            ("location",       "locations"),
            ("evse",           "locations"),
            ("tariff",         "tariffs"),
            ("session",        "sessions"),
            ("cdr",            "cdrs"),
            ("charge detail",  "cdrs"),
            ("token",          "tokens"),
            ("command",        "commands"),
            ("remote party",   "partner"),
            ("remoteparty",    "partner"),
            ("websocket",      "websocket"),
            ("http",           "http"),
            ("tls",            "tls"),
            ("certificate",    "tls"),
            ("dns",            "dns"),
            ("nts",            "nts"),
            ("ntp",            "nts"),
            ("emsp",           "peer"),
            ("cpo",            "peer"),
            ("hub",            "hub")
        ];

        #endregion

        #region Properties

        /// <summary>
        /// The JSON API at "/api/".
        /// </summary>
        public RoamingHubHTTPAPI      API                    { get; }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create a RoamingHub with a web interface in front of it.
        /// Nothing listens yet: <see cref="WWCPNode.Start"/> does.
        /// </summary>
        /// <param name="DNSClient">The DNS client used by everything below.</param>
        /// <param name="NTSClient">The time client.</param>
        /// <param name="HTTPServer">An HTTP server to register within, or null to make one.</param>
        /// <param name="BasePath">What everything of this RoamingHub sits below; the root by default. Something else only where several of these programs share one HTTP server.</param>
        /// <param name="HTTPRootPath">The root path of the JSON API, "/api" below <paramref name="BasePath"/> by default.</param>
        /// <param name="ExtAPI">An HTTPExt API to sign in against, or null for one of this RoamingHub's own. Handing one in is what makes one sign-in open several of these programs at once.</param>
        /// <param name="AccountsPath">The directory the accounts live in between starts.</param>
        /// <param name="HTTPHostname">The address to listen on; the loopback address by default.</param>
        /// <param name="HTTPPort">The TCP port to listen on; <see cref="DefaultHTTPPort"/> by default.</param>
        /// <param name="ConfigFile">Where everything this RoamingHub can be told in writing lives; "configuration.json" beside the process by default.</param>
        /// <param name="OCPI">Who this RoamingHub is in OCPI, unless the configuration file says otherwise.</param>
        /// <param name="Frontend">Where the web interface comes from; the bundle embedded in this assembly by default.</param>
        /// <param name="CertificatesPath">The directory of the node's certificate store; what the file says, or "certificates" beside it, by default.</param>
        /// <param name="Log">The event log; a new one by default.</param>
        /// <param name="LogToConsole">Whether the event log is also written to the console.</param>
        /// <param name="ConsoleLogLevel">What the console shows of it.</param>
        /// <param name="LogPath">The directory a log file per day is written to, or null to write none.</param>
        /// <param name="BridgeDebugLog">Whether what the libraries below write with DebugX ends up in the log.</param>
        /// <param name="TimeProvider">Where this RoamingHub reads the time; the system clock by default.</param>
        public RoamingHub(DNSClient?             DNSClient          = null,
                          NTSClient?             NTSClient          = null,
                          HTTPServer?            HTTPServer         = null,
                          HTTPPath?              BasePath           = null,
                          HTTPPath?              HTTPRootPath       = null,
                          HTTPExtAPI?            ExtAPI             = null,
                          String?                AccountsPath       = null,
                          IIPAddress?            HTTPHostname       = null,
                          IPPort?                HTTPPort           = null,
                          WWCPConfigFile?        ConfigFile         = null,
                          OCPIConfiguration?     OCPI               = null,
                          IStaticContentSource?  Frontend           = null,
                          String?                CertificatesPath   = null,
                          EventLog?              Log                = null,
                          Boolean                LogToConsole       = true,
                          LogLevel               ConsoleLogLevel    = LogLevel.Info,
                          String?                LogPath            = null,
                          Boolean                BridgeDebugLog     = true,
                          TimeProvider?          TimeProvider       = null)

            : base(Kind:               new NodeKind(
                                           Name:           "roaming hub",
                                           Tag:            "roamingHub",
                                           Product:        "RoamingHub",
                                           Organization:   DefaultOrganization,
                                           LogFilePrefix:  "rn"
                                       ),
                   Version:            typeof(RoamingHub).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                   HTTPPort:           HTTPPort ?? DefaultHTTPPort,
                   HTTPHostname:       HTTPHostname,
                   HTTPServer:         HTTPServer,
                   BasePath:           BasePath,
                   HTTPRootPath:       HTTPRootPath,
                   ExtAPI:             ExtAPI,
                   AccountsPath:       AccountsPath,
                   Roles:              UserRole.All.Select(role => role.Name),
                   ConfigFile:         ConfigFile,
                   DNSClient:          DNSClient,
                   NTSClient:          NTSClient,
                   Frontend:           Frontend ?? new EmbeddedContentSource(HTTPRoot, typeof(RoamingHub).Assembly),
                   CertificatesPath:   CertificatesPath,
                   Log:                Log,
                   LogToConsole:       LogToConsole,
                   ConsoleLogLevel:    ConsoleLogLevel,
                   LogPath:            LogPath,
                   BridgeDebugLog:     BridgeDebugLog,
                   TraceTags:          TraceTags,
                   TimeProvider:       TimeProvider)

        {

            // "this." throughout, and not for tidiness: the parameters of this
            // constructor shadow the properties of the same name, and a
            // parameter such as "Log" or "ConfigFile" is null whenever the
            // caller did not bring one of its own.

            #region The traffic, beside the event log

            // Beside the event log rather than inside it: the event log is
            // what this hub did, and the traffic is what its peers did
            // through it. They are read by different people asking different
            // questions, and a reader following one of them should not have
            // to sift the other out.
            this.trafficLog = new OCPITrafficLog(TimeProvider: this.TimeProvider);

            #endregion

            #region What the configuration file says of this hub

            // From the document the node has read already: one file, read
            // once. A section of this hub that is there but cannot be read is
            // not something to paper over with defaults - somebody wrote down
            // who their hub is and got it wrong, and quietly running as
            // somebody else instead would be worse than stopping.
            if (!RoamingHubConfiguration.TryParse(ConfigurationDocument, out var configuration, out var problem))
                throw new InvalidOperationException($"'{this.ConfigFile.Path}': {problem} Repair or remove '{this.ConfigFile.Path}' and start again.");

            if (!configuration.IsEmpty)
                this.Log.Info($"Hub configuration from '{this.ConfigFile.Path}': {configuration}.", "config");

            #endregion

            this.Log.Info(
                OwnsExtAPI
                    ? $"The HTTPExt API is at '{this.ExtAPI.RootPath}', its accounts in '{this.ExtAPI.DatabaseFileName}'."
                    : $"This RoamingHub signs in against accounts it shares, at '{this.ExtAPI.RootPath}'.",
                "web", "http"
            );

            #region The JSON API at "/api"

            // Below the web interface's single-page-application stub, and the
            // more specific of the two, so that an unknown /api path is
            // answered by the API and never reaches the stub.
            this.API = new RoamingHubHTTPAPI(
                           HTTPServer:  this.HTTPServer,
                           RoamingHub:  this,
                           ExtAPI:      this.ExtAPI,
                           Log:         this.Log,
                           APIPath:     this.HTTPRootPath,
                           Version:     this.Version
                       );

            #endregion

            #region Every call of a peer, into the traffic log

            // Beside the node's line per request in the event log, and where a
            // peer was at the other end of it - see RoamingHub.Traffic.cs.
            // Wrapped, because an answer must go out even if writing it down
            // does not.
            this.HTTPServer.OnHTTPResponse += (server, request, response, cancellationToken) => {

                if (IsEventStream(request))
                    return Task.CompletedTask;

                try
                {
                    RecordInboundCall(request, response);
                }
                catch (Exception e)
                {
                    this.Log.Exception(e, "A call could not be written to the traffic log.", "ocpi", "traffic");
                }

                return Task.CompletedTask;

            };

            #endregion

            #region The OCPI endpoints the roaming partners call

            // After the HTTPExt API, because they hang off it, and after the
            // configuration file, because who this RoamingHub is in OCPI is
            // written there. They share the HTTP server above rather than
            // opening a port of their own: OCPI is plain HTTP, and one
            // RoamingHub is one address to point a partner at.
            BuildOCPI(configuration.OCPI ?? OCPI);

            #endregion

        }

        #endregion


        #region (protected override) OnStarted()

        /// <summary>
        /// Watch the peers, and say where they and their traffic are found.
        /// </summary>
        protected override Task OnStarted()
        {

            // After the peers have been read back, because it seeds itself
            // from them - see RoamingHub.HubClientInfo.cs.
            StartWatchingThePeers();

            Log.Info   ($"The JSON API is at {APIURL}v1/status", "web", "http");
            Log.Notice ($"Peers find this hub at {OCPIVersionsURL} " +
                        $"(OCPI {String.Join(", ", OCPIVersions.Select(version => version.Label))}, " +
                        $"{RemotePartyCount} peer(s), {RegisteredPartyCount} of them registered).",
                        "ocpi");
            Log.Info   ($"What goes between the peers is at {APIURL}v1/traffic, and on the stream at {APIURL}v1/traffic/events.",
                        "ocpi", "traffic");

            return Task.CompletedTask;

        }

        #endregion

        #region (protected override) OnStopping()

        /// <summary>
        /// End what this hub holds open beyond the server: the watch over the
        /// peers, the pushes in flight and the event streams.
        /// </summary>
        protected override Task OnStopping()
        {

            presenceTimer?.Dispose();
            presenceTimer = null;

            // Before the sockets close: a push in flight holds one, and a hub
            // that waited for it would take its shutdown from whoever it was
            // talking to.
            if (!pushShutdown.IsCancellationRequested)
                pushShutdown.Cancel();

            // Before the server, and that order is the whole point: every
            // browser with the Logs page open holds a request that is waiting
            // for the next log entry rather than for its socket, and the HTTP
            // server waits for every request it started. Closing the sockets
            // does not wake those, so they are ended here first.
            API.CloseEventStreams();

            return Task.CompletedTask;

        }

        #endregion

        #region ConfigurationJSON()

        /// <summary>
        /// What this RoamingHub is made of, as the Configuration page of the
        /// web interface reads it: what the node below says of itself, and on
        /// top the hub, who it is in OCPI, its traffic, and the assemblies it
        /// was built from.
        /// </summary>
        /// <remarks>
        /// Read-only: it answers "what am I running", not "change it". Nothing
        /// here is a secret - the accounts appear as the path they live at and
        /// the route to sign in, and never as anything about a password; the
        /// roaming partners appear as a count, and never as their tokens.
        /// </remarks>
        public override JObject ConfigurationJSON()
        {

            var json = base.ConfigurationJSON();

            // First, because it is the card the page leads with.
            json.AddFirst(new JProperty("RoamingHub", new JObject(
                              new JProperty("version",        Version),
                              new JProperty("createdAt",      CreatedAt.ToString("o")),
                              new JProperty("machine",        Environment.MachineName),
                              new JProperty("runtime",        Environment.Version.ToString()),
                              new JProperty("os",             Environment.OSVersion.ToString())
                          )));

            json.Add(new JProperty("ocpi",       new JObject(
                         new JProperty("role",             "HUB"),
                         new JProperty("partyId",          PartyIdText),
                         new JProperty("countryCode",      PartyId.CountryCode.ToString()),
                         new JProperty("party",            PartyId.PartyId.ToString()),
                         new JProperty("name",             BusinessDetails.Name),
                         new JProperty("website",          BusinessDetails.Website?.ToString()),
                         new JProperty("versions",         new JArray(OCPIVersions.Select(version => version.Label))),
                         new JProperty("versionsURL",      OCPIVersionsURL.ToString()),
                         new JProperty("peers",            RemotePartyCount),
                         new JProperty("registered",       RegisteredPartyCount),
                         new JProperty("file",             ConfigFile.Path)
                     )));

            json.Add(new JProperty("traffic",    new JObject(
                         new JProperty("capacity",         Traffic.Capacity),
                         new JProperty("calls",            Traffic.Count),
                         new JProperty("lastId",           Traffic.LastId),
                         new JProperty("payloads",         OCPI.Logging?.Payloads == true),
                         new JProperty("parties",          new JArray(Traffic.KnownParties))
                     )));

            json.Add(new JProperty("assemblies", new JArray(
                         AssemblyJSON<HTTPServer>                                  ("Hermod"),
                         AssemblyJSON<NTSClient>                                   ("Norn"),
                         AssemblyJSON<WWCPNode>                                    ("WWCP Node"),
                         AssemblyJSON<protocols.OCPI.CommonHTTPAPI>                ("OCPI"),
                         AssemblyJSON<protocols.OCPIv2_2_1.CommonAPI>              ("OCPI 2.2.1"),
                         AssemblyJSON<protocols.OCPIv2_3_0.CommonAPI>              ("OCPI 2.3.0")
                     )));

            return json;

        }

        #endregion


        #region (private static) AssemblyJSON<T>(Name)

        private static JObject AssemblyJSON<T>(String Name)
        {

            var assembly = typeof(T).Assembly.GetName();

            return new JObject(
                       new JProperty("name",      Name),
                       new JProperty("assembly",  assembly.Name),
                       new JProperty("version",   assembly.Version?.ToString(3))
                   );

        }

        #endregion

        #region (private static) IsEventStream(Request)

        /// <summary>
        /// Whether this request is a browser hanging on an event stream.
        /// </summary>
        private static Boolean IsEventStream(HTTPRequest Request)
            => Request.Path.ToString().EndsWith("/events", StringComparison.Ordinal);

        #endregion

    }

}
