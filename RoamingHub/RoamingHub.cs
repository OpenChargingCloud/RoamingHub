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
using org.GraphDefined.Vanaheimr.Hermod.Mail;
using org.GraphDefined.Vanaheimr.Norn.NTS;

// Only the mailer, not the namespace: Hermod.SMTP carries a LogLevel of its
// own, and importing it would make every LogLevel in this file ambiguous with
// the one the event log uses.
using NullMailer = org.GraphDefined.Vanaheimr.Hermod.SMTP.NullMailer;

using cloud.charging.open.RoamingHub.Configuration;
using cloud.charging.open.RoamingHub.Logging;
using cloud.charging.open.RoamingHub.Web;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// One OCPI roaming hub: the endpoints its peers call, the HTTP server in
    /// front of them, and the JSON API at "/api".
    /// </summary>
    /// <remarks>
    /// <para>
    /// A hub sits between the charge point operators and the e-mobility
    /// service providers so that they do not each have to be peered with all
    /// the others. Every one of them is peered with the hub instead, once, and
    /// the hub is what turns that into a mesh.
    /// </para>
    /// <para>
    /// <b>What is here so far.</b> The base every one of these programs has -
    /// its name resolution, its time source, its accounts, its event log -
    /// and the peering: a peer added, a token handed out, and the credentials
    /// exchanged in either direction. Forwarding what one peer sends to
    /// another is not here yet, and neither is the hubclientinfo module that
    /// would tell a peer who else is on the hub.
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
    /// No web interface yet. The JSON API answers, the event stream runs, and
    /// a browser asking for "/" is told there is nothing to render.
    /// </para>
    /// </remarks>
    public partial class RoamingHub : IAsyncDisposable
    {

        #region Data

        /// <summary>
        /// The manifest resource prefix a frontend bundle would be embedded
        /// under, the way the other four components embed theirs.
        /// </summary>
        /// <remarks>
        /// Nothing is embedded under it yet: this hub has no web interface.
        /// The name is here rather than invented later so that a frontend,
        /// when it arrives, lands where a reader of the other four would
        /// look for it - and so that --frontend can point at a directory on
        /// disk in the meantime.
        /// </remarks>
        public const String  HTTPRoot            = "cloud.charging.open.RoamingHub.HTTPRoot.";

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
        public static readonly IPPort DefaultHTTPPort = IPPort.Parse(2356);

        /// <summary>
        /// Where the HTTPExt API - users, organizations, API keys - lives,
        /// unless another path is given.
        /// </summary>
        /// <remarks>
        /// Below a path of its own rather than at "/", because three things
        /// share this server: the JSON API of this RoamingHub at "/api", the web
        /// interface at "/", and this. The web interface is the catch-all of
        /// the three, so everything that is not it needs a prefix that says so
        /// before the stub gets the request.
        ///
        /// The OCPI endpoints sit below this path as well - see
        /// <see cref="OCPIBaseURL"/> - because the OCPI library attaches to the
        /// HTTPExt API rather than to the server.
        /// </remarks>
        public static readonly HTTPPath  ExtAPIPath                   = HTTPPath.Parse("/ext");

        /// <summary>
        /// The directory the HTTPExt API keeps its accounts in, unless another
        /// is given.
        /// </summary>
        public const String  DefaultAccountsPath           = "accounts";

        /// <summary>
        /// The file inside that directory that holds the accounts.
        /// </summary>
        public const String  DefaultAccountsDatabaseFile   = "users.db";

        /// <summary>
        /// The account made at a first start.
        /// </summary>
        public const String  DefaultAdminUser              = "root";

        /// <summary>
        /// The organization that account belongs to.
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
        /// </remarks>
        public const String  DefaultOrganization           = "RoamingHub";

        /// <summary>
        /// The file of the bundle that is the web interface; its presence is
        /// what says there is one to serve at all.
        /// </summary>
        public const String  IndexFile           = "index.html";

        /// <summary>
        /// The icon of the bundle, which /favicon.ico is pointed at.
        /// </summary>
        public const String  FaviconSVG          = "favicon.svg";

        private readonly  DNSClient                       dnsClient;
        private           NTSClient                       ntsClient;

        /// <summary>
        /// The name servers this RoamingHub would ask, whether or not name
        /// resolution is switched on at the moment.
        /// </summary>
        private           IReadOnlyList<DNSServerConfig>  configuredDNSServers;

        /// <summary>
        /// Serialises changes to what this RoamingHub is made of, so that two
        /// browsers saving at the same moment do not build half a RoamingHub each.
        /// </summary>
        private readonly  SemaphoreSlim                   reconfigureLock = new (1, 1);

        private readonly  HTTPServer                      httpServer;
        private readonly  HTTPPath                        httpRootPath;

        private           DateTimeOffset?                 lastTimeCheck;
        private           TimeSpan?                       lastTimeCheckOffset;
        private           String?                         lastTimeCheckServer;

        private           ITimer?                         timeCheckTimer;

        private           NTSConfiguration?               ntsSettings;

        private readonly  ConsoleLog?                     consoleLog;
        private readonly  TraceBridge?                    traceBridge;

        private           Boolean                         started;

        #endregion

        #region Properties

        /// <summary>
        /// Everything that happens inside this RoamingHub.
        /// </summary>
        public EventLog               Log                    { get; }

        /// <summary>
        /// The directory the accounts live in between starts.
        /// </summary>
        public String                 AccountsPath           { get; }

        /// <summary>
        /// Where everything this RoamingHub can be told in writing lives between
        /// starts: its name resolution, its time source, its OCPI identity.
        /// </summary>
        public RoamingHubConfigFile         ConfigFile             { get; }

        /// <summary>
        /// How this RoamingHub resolves names.
        /// </summary>
        public DNSClient              DNSClient
            => dnsClient;

        /// <summary>
        /// Where this RoamingHub reads the time.
        /// </summary>
        public NTSClient              NTSClient
            => ntsClient;

        /// <summary>
        /// Whether this RoamingHub resolves names at all.
        /// </summary>
        public Boolean                DNSEnabled             { get; private set; } = true;

        /// <summary>
        /// Whether this RoamingHub may ask its time server.
        /// </summary>
        public Boolean                NTSEnabled             { get; private set; } = true;

        /// <summary>
        /// The password this RoamingHub made up because there was no login file, or
        /// null when the login came from the file. It is shown once, on the
        /// console, and kept nowhere but in its hash.
        /// </summary>
        public String?                GeneratedPassword      { get; private set; }

        /// <summary>
        /// Where the web interface comes from: this assembly, or a directory
        /// on disk.
        /// </summary>
        public IStaticContentSource   Frontend               { get; }

        /// <summary>
        /// The HTTP server everything below is registered within.
        /// </summary>
        public HTTPServer             HTTPServer
            => httpServer;

        /// <summary>
        /// The HTTPExt API at "/ext/": the users, organizations and API keys
        /// this RoamingHub is administered with - and what the OCPI endpoints hang
        /// off.
        /// </summary>
        /// <remarks>
        /// The vehicle, the charging station, the local controller and the
        /// CSMS sign in against the same thing, which is what makes one of
        /// these serve all of them at once: their roles are groups in it, the
        /// names overlap, and an account in "systemadmin" is then an
        /// administrator of every one of them. See <see cref="OwnsExtAPI"/>.
        /// </remarks>
        public HTTPExtAPI             ExtAPI                 { get; }

        /// <summary>
        /// Whether those accounts are this RoamingHub's own, or somebody else's that
        /// it was handed.
        /// </summary>
        public Boolean                OwnsExtAPI             { get; }

        /// <summary>
        /// Whether the HTTP server is this RoamingHub's own, or one it was handed
        /// and shares with somebody else.
        /// </summary>
        public Boolean                OwnsHTTPServer         { get; }

        /// <summary>
        /// Everything of this RoamingHub - its web interface, its JSON API and,
        /// where the accounts are its own, those too - sits below this.
        /// </summary>
        public HTTPPath               BasePath               { get; }

        /// <summary>
        /// The base path as it is written into a URL: the empty string at the
        /// root, and "/RoamingHub" or the like below one.
        /// </summary>
        public String                 BasePathText
            => BasePath == HTTPPath.Root
                   ? ""
                   : BasePath.ToString().TrimEnd('/');

        /// <summary>
        /// The JSON API at "/api/".
        /// </summary>
        public RoamingHubHTTPAPI            API                    { get; }

        /// <summary>
        /// The web interface at "/", or null when no bundle was found to serve.
        /// </summary>
        public HTTPAPI?               WebInterface           { get; }

        /// <summary>
        /// The URL to open in a browser.
        /// </summary>
        public URL                    WebInterfaceURL        { get; }

        /// <summary>
        /// The JSON API as a browser would type it: the server and the API's
        /// root path, which already carries the base path, with a slash at the end.
        /// </summary>
        public URL                    APIURL                 { get; }

        /// <summary>
        /// The version of this RoamingHub.
        /// </summary>
        public String                 Version                { get; }

        /// <summary>
        /// Where this RoamingHub reads the time.
        /// </summary>
        /// <remarks>
        /// The clock everything a roaming partner sends is stamped against -
        /// see RoamingHub.Clock.cs for why it is handed in rather than reached for.
        /// The system clock by default; an NTS-disciplined or a fake one where
        /// a test or a calibration says so.
        /// </remarks>
        public TimeProvider           TimeProvider           { get; }

        /// <summary>
        /// When this RoamingHub was created, by its own clock.
        /// </summary>
        public DateTimeOffset         CreatedAt              { get; }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create a RoamingHub with a web interface in front of it.
        /// Nothing listens yet: <see cref="Start"/> does.
        /// </summary>
        /// <param name="DNSClient">The DNS client used by everything below.</param>
        /// <param name="NTSClient">The time client.</param>
        /// <param name="HTTPServer">An HTTP server to register within, or null to make one.</param>
        /// <param name="BasePath">What everything of this RoamingHub sits below; the root by default. Something else only where several of these programs share one HTTP server.</param>
        /// <param name="HTTPRootPath">The root path of the JSON API, "/api" below <paramref name="BasePath"/> by default.</param>
        /// <param name="ExtAPI">An HTTPExt API to sign in against, or null for one of this RoamingHub's own. Handing one in is what makes one sign-in open several of these programs at once.</param>
        /// <param name="HTTPExtAPIPath">Where this RoamingHub's own HTTPExt API sits below <paramref name="BasePath"/>, "/ext" by default. Ignored when one is handed in.</param>
        /// <param name="AccountsPath">The directory the accounts live in between starts.</param>
        /// <param name="HTTPHostname">The address to listen on; the loopback address by default.</param>
        /// <param name="HTTPPort">The TCP port to listen on.</param>
        /// <param name="ConfigFile">Where everything this RoamingHub can be told in writing lives; "configuration.json" beside the process by default.</param>
        /// <param name="OCPI">Who this RoamingHub is in OCPI, unless the configuration file says otherwise.</param>
        /// <param name="Frontend">Where the web interface comes from; the bundle embedded in this assembly by default.</param>
        /// <param name="Log">The event log; a new one by default.</param>
        /// <param name="LogToConsole">Whether the event log is also written to the console.</param>
        /// <param name="ConsoleLogLevel">What the console shows of it.</param>
        /// <param name="BridgeDebugLog">Whether what the libraries below write with DebugX ends up in the log.</param>
        /// <param name="TimeProvider">Where this RoamingHub reads the time; the system clock by default.</param>
        public RoamingHub(DNSClient?             DNSClient               = null,
                    NTSClient?             NTSClient               = null,
                    HTTPServer?            HTTPServer              = null,
                    HTTPPath?              BasePath                = null,
                    HTTPPath?              HTTPRootPath            = null,
                    HTTPExtAPI?            ExtAPI                  = null,
                    HTTPPath?              HTTPExtAPIPath          = null,
                    String?                AccountsPath            = null,
                    IIPAddress?            HTTPHostname            = null,
                    IPPort?                HTTPPort                = null,
                    RoamingHubConfigFile?        ConfigFile              = null,
                    OCPIConfiguration?     OCPI                    = null,
                    IStaticContentSource?  Frontend                = null,
                    EventLog?              Log                     = null,
                    Boolean                LogToConsole            = true,
                    LogLevel               ConsoleLogLevel         = LogLevel.Info,
                    Boolean                BridgeDebugLog          = true,
                    TimeProvider?          TimeProvider            = null)
        {

            #region The clock, before anything that wants to know the time

            // First of all, and not for tidiness: the event log below stamps
            // every entry with this, so a clock set afterwards would leave the
            // log reading the system one - and a log on a different clock than
            // the RoamingHub it belongs to cannot be held against anything.
            this.TimeProvider  = TimeProvider ?? System.TimeProvider.System;
            this.CreatedAt     = this.TimeProvider.GetUtcNow();

            #endregion

            #region The log, next - everything below it may want to say something

            this.Version      = typeof(RoamingHub).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            this.Log          = Log ?? new EventLog(TimeProvider: this.TimeProvider);

            // Beside the event log rather than inside it: the event log is
            // what this hub did, and the traffic is what its peers did
            // through it. They are read by different people asking different
            // questions, and a reader following one of them should not have
            // to sift the other out.
            this.trafficLog   = new OCPITrafficLog(TimeProvider: this.TimeProvider);

            this.consoleLog   = LogToConsole
                                    ? new ConsoleLog(this.Log, ConsoleLogLevel)
                                    : null;

            // Attached before anything else is built, so that what the DNS
            // client, the HTTP server and the OCPI library say while they are
            // being made is already in the log a browser will see later.
            this.traceBridge  = BridgeDebugLog
                                    ? TraceBridge.Attach(this.Log)
                                    : null;

            this.Log.Notice($"RoamingHub v{this.Version} starting up.", "hub");

            #endregion

            #region Where the accounts live

            // Ending in a separator, because the HTTPExt API builds the paths
            // of its files by putting strings together rather than with
            // Path.Combine: a directory that does not end in one would give it
            // "...accountsUsersAPI" and not "...accounts/UsersAPI".
            this.AccountsPath = AccountsPath ?? DefaultAccountsPath;

            if (!this.AccountsPath.EndsWith(Path.DirectorySeparatorChar))
                this.AccountsPath += Path.DirectorySeparatorChar;

            #endregion

            #region What the configuration file says

            this.ConfigFile = ConfigFile ?? new RoamingHubConfigFile(RoamingHubConfigFile.DefaultFileName);

            RoamingHubConfiguration? configuration = null;

            if (this.ConfigFile.Exists)
            {

                // A file that is there but cannot be read is not something to
                // paper over with defaults: somebody wrote down what their RoamingHub
                // is and got it wrong, and quietly running as something else
                // instead would be worse than stopping.
                if (!this.ConfigFile.TryLoad(out configuration, out var configError))
                    throw new InvalidOperationException($"{configError} Repair or remove '{this.ConfigFile.Path}' and start again.");

                this.Log.Info($"Configuration from '{this.ConfigFile.Path}': {configuration}.", "config");

            }

            #endregion

            #region The clients everything below shares

            this.dnsClient             = DNSClient ?? new DNSClient();
            this.configuredDNSServers  = [.. dnsClient.DNSServers];

            this.ntsClient     = NTSClient    ?? new NTSClient(
                                                     DomainName.Parse(NTSConfiguration.DefaultHostname),
                                                     Timeout:         TimeSpan.FromSeconds(10),
                                                     DNSClient:       dnsClient,
                                                     TimeProvider:    this.TimeProvider
                                                 );

            // Last, and that is the whole precedence rule: what this
            // constructor was handed holds until the file says otherwise, and
            // what the file does not mention is left exactly as it was.
            if (configuration?.DNS is not null)
                ApplyDNSConfiguration(configuration.DNS);

            if (configuration?.NTS is not null)
                ApplyNTSConfiguration(configuration.NTS);

            this.ntsSettings = configuration?.NTS;

            #endregion

            #region The HTTP server, the JSON API and the web interface

            var address        = HTTPHostname ?? IPv4Address.Localhost;
            var port           = HTTPPort     ?? DefaultHTTPPort;

            this.OwnsHTTPServer = HTTPServer is null;

            this.httpServer    = HTTPServer   ?? new HTTPServer(
                                                     IPAddress:       address,
                                                     TCPPort:         port,
                                                     HTTPServerName:  $"OpenChargingCloud RoamingHub v{Version}",
                                                     DNSClient:       dnsClient
                                                 );

            this.BasePath      = BasePath     ?? HTTPPath.Root;

            this.httpRootPath  = HTTPRootPath ?? this.BasePath + RoamingHubHTTPAPI.DefaultAPIPath;

            this.WebInterfaceURL = URL.Parse($"http://{address}:{port}{this.BasePath.ToString().TrimEnd('/')}/");

            // From the server rather than from the web interface's URL: the API's
            // root path already carries the base path, and behind a URL that ends
            // in the base path it would be named twice.
            this.APIURL          = URL.Parse($"http://{address}:{port}/{this.httpRootPath.ToString().Trim('/')}/");

            // 1) The HTTPExt API at "/ext". First of the three, because it is
            //    the one with a database behind it: whatever it finds wrong
            //    with its files, it should say so before a port is opened and
            //    before a roaming partner is let in against accounts that were
            //    not read.
            this.OwnsExtAPI    = ExtAPI is null;

            this.ExtAPI        = ExtAPI ?? new HTTPExtAPI(
                                     HTTPServer:             httpServer,
                                     RootPath:               this.BasePath + (HTTPExtAPIPath ?? ExtAPIPath),
                                     HTTPServerName:         $"OpenChargingCloud RoamingHub v{Version}",
                                     HTTPServiceName:        $"OpenChargingCloud RoamingHub v{Version}",
                                     APIRobotEMailAddress:   EMailAddress.Parse("OpenChargingCloud RoamingHub Robot <robot@charging.cloud>"),
                                     APIRobotGPGPassphrase:  "",

                                     // Nothing here sends mail. A RoamingHub that
                                     // notifies by e-mail is told so by its
                                     // operator, with a submission client of
                                     // their own; until then a mailer that
                                     // swallows what it is given is better
                                     // than one that quietly retries against
                                     // a host nobody configured.
                                     SMTPSubmissionClient:   new NullMailer(),
                                     DisableNotifications:   true,

                                     // The cookie has to reach "/api", and its
                                     // path would otherwise be the root path of
                                     // this API - "/ext" - so a browser signed
                                     // in at /ext/login would send nothing to
                                     // the API and look signed out everywhere
                                     // else.
                                     HTTPCookiePath:         "/",

                                     // A secure cookie is dropped by a browser
                                     // over plain HTTP, and a RoamingHub on a bench
                                     // is reached over plain HTTP.
                                     UseSecureCookies:       false,

                                     // The shortest name a role of this RoamingHub
                                     // has, because that is what a group
                                     // identification has to be allowed to be.
                                     MinUserGroupIdLength:   (Byte) UserRole.All.Min(role => role.Name.Length),

                                     LoggingPath:            AccountsPath,
                                     DatabaseFileName:       DefaultAccountsDatabaseFile,

                                     // Left on, and that is what makes the
                                     // directory above: switching it off skips
                                     // the CreateDirectory that the accounts
                                     // file is written into, and the first
                                     // account created would fail on a path
                                     // that was never made.
                                     DisableLogging:         false
                                 );

            this.Log.Info(
                OwnsExtAPI
                    ? $"The HTTPExt API is at '{this.ExtAPI.RootPath}', its accounts in '{this.ExtAPI.DatabaseFileName}'."
                    : $"This RoamingHub signs in against accounts it shares, at '{this.ExtAPI.RootPath}'.",
                "web", "http"
            );

            // 2) The JSON API at "/api". Before the web interface, so that it
            //    is the more specific API and an unknown /api path never
            //    reaches the single-page-application stub below.
            this.API           = new RoamingHubHTTPAPI(
                                     HTTPServer:  httpServer,
                                     RoamingHub:        this,
                                     ExtAPI:      this.ExtAPI,
                                     Log:         this.Log,
                                     APIPath:     httpRootPath,
                                     Version:     Version
                                 );

            // 3) The web interface at "/": the files of the bundle, and the
            //    single-page-application stub for every other page URL, so
            //    that a reload on /logs and a bookmark to it both work.
            this.Frontend      = Frontend ?? new EmbeddedContentSource(HTTPRoot, typeof(RoamingHub).Assembly);

            if (this.Frontend.TryGet(IndexFile, out _))
            {

                this.WebInterface = httpServer.AddHTTPAPI(this.BasePath);

                this.WebInterface.MapSinglePageApplication(
                    this.Frontend,
                    new SinglePageAppOptions {

                        // The bundle reads where it is and where its API is out
                        // of <meta> tags rather than assuming "/" and "/api/v1",
                        // because under a base path both of those are wrong.
                        IndexTransform = html => html.
                                                     Replace("{{ServerVersion}}", $"v{Version}",         StringComparison.Ordinal).
                                                     Replace("{{BasePath}}",      BasePathText,          StringComparison.Ordinal).
                                                     Replace("{{APIBase}}",       $"{httpRootPath.ToString().TrimEnd('/')}/v1", StringComparison.Ordinal).
                                                     Replace("{{ExtBase}}",       this.ExtAPI.RootPath.ToString().TrimEnd('/'), StringComparison.Ordinal)

                    }
                );

                // Browsers ask for /favicon.ico whatever the page says, and a
                // bundle built by webpack carries an SVG.
                if (this.Frontend.TryGet(FaviconSVG, out _))
                    this.WebInterface.AddHandler(
                        HTTPPath.Parse("/favicon.ico"),
                        request => Task.FromResult(
                                       new HTTPResponse.Builder(request) {
                                           HTTPStatusCode  = HTTPStatusCode.TemporaryRedirect,
                                           Location        = Location.From(HTTPPath.Parse($"{BasePathText}/{FaviconSVG}")),
                                           CacheControl    = "public, max-age=3600"
                                       }.AsImmutable
                                   ),
                        HTTPMethod.GET
                    );

            }

            // Not an error here, unlike in the four components that have one:
            // this hub has no web interface yet, and saying so at every start
            // in red would be crying about a thing nobody has built.
            else
                this.Log.Info(
                    $"No web interface ({this.Frontend.Description}): the JSON API answers and a browser asking for '/' gets nothing. " +
                    "Point this hub at a directory of one with --frontend, or read it with curl - see /api/v1/status.",
                    "web"
                );

            #region Every request, into the log

            httpServer.OnHTTPRequest  += (server, request, cancellationToken) => {

                // The event stream is one request that stays open for as long
                // as a browser has the page open; logging it would say nothing
                // and logging its response would say it at the wrong moment.
                if (!IsEventStream(request))
                    this.Log.Debug($"{request.HTTPMethod} {request.Path} from {request.RemoteSocket}", "http");

                return Task.CompletedTask;

            };

            // Only OnHTTPResponse, and not OnHTTPError beside it: Hermod raises
            // both for the same response, and one line per request is what a
            // log is for.
            httpServer.OnHTTPResponse += (server, request, response, cancellationToken) => {

                if (IsEventStream(request))
                    return Task.CompletedTask;

                var code = response.HTTPStatusCode.Code;

                this.Log.Log(
                    code >= 500 ? LogLevel.Error
                        // A 401 is how the web interface asks whether anybody
                        // is signed in, and the answer "nobody" is not a fault.
                        : code == 401 ? LogLevel.Debug
                        : code >= 400 ? LogLevel.Warning
                        : LogLevel.Debug,
                    $"{code} {response.HTTPStatusCode.Name} for {request.HTTPMethod} {request.Path}",
                    "http"
                );

                // And, where a peer was at the other end of it, into the
                // traffic log as well - see RoamingHub.Traffic.cs. Wrapped, because
                // an answer must go out even if writing it down does not.
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

            #endregion

            #region The OCPI endpoints the roaming partners call

            // After the HTTPExt API, because they hang off it, and after the
            // configuration file, because who this RoamingHub is in OCPI is written
            // there. They share the HTTP server above rather than opening a
            // port of their own: OCPI is plain HTTP, and one RoamingHub is one
            // address to point a partner at.
            BuildOCPI(configuration?.OCPI ?? OCPI);

            #endregion

        }

        #endregion


        #region Start()

        /// <summary>
        /// Start listening.
        /// </summary>
        public async Task Start()
        {

            if (started)
                return;

            // Before the port opens, and that order is the point: a web
            // interface reachable before its accounts exist is a door with
            // nobody behind it.
            await EnsureAccounts();

            // Only where it is ours: a shared server is started by whoever
            // made it, and starting it again would take the same socket twice.
            if (OwnsHTTPServer)
                await httpServer.Start();

            StartCheckingTheClock();

            // After the peers have been read back, because it seeds itself
            // from them - see RoamingHub.HubClientInfo.cs.
            StartWatchingThePeers();

            started = true;

            Log.Notice(WebInterface is not null
                           ? $"The web interface is listening on {WebInterfaceURL}"
                           : $"This hub is listening on {WebInterfaceURL} - the JSON API, and no web interface",
                       "web", "http");

            Log.Info   ($"The JSON API is at {APIURL}v1/status", "web", "http");
            Log.Notice ($"Peers find this hub at {OCPIVersionsURL} " +
                        $"(OCPI {String.Join(", ", OCPIVersions.Select(version => version.Label))}, " +
                        $"{RemotePartyCount} peer(s), {RegisteredPartyCount} of them registered).",
                        "ocpi");
            Log.Info   ($"What goes between the peers is at {APIURL}v1/traffic, and on the stream at {APIURL}v1/traffic/events.",
                        "ocpi", "traffic");

        }

        #endregion

        #region Stop()

        /// <summary>
        /// Stop listening.
        /// </summary>
        public async Task Stop()
        {

            if (!started)
                return;

            Log.Notice("This hub is shutting down.", "hub");

            timeCheckTimer?.Dispose();
            timeCheckTimer = null;

            presenceTimer?.Dispose();
            presenceTimer = null;

            // Before the server, and that order is the whole point: every
            // browser with the Logs page open holds a request that is waiting
            // for the next log entry rather than for its socket, and the HTTP
            // server waits for every request it started. Closing the sockets
            // does not wake those, so they are ended here first.
            API.CloseEventStreams();

            if (OwnsHTTPServer)
                await httpServer.Stop();

            started = false;

        }

        #endregion

        #region (private) EnsureAccounts()

        /// <summary>
        /// Make the three groups and, at a first start, the one account that
        /// is in the last of them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The groups are made every start rather than only the first, because
        /// they are this RoamingHub's vocabulary and not somebody's data: a group
        /// deleted by hand would otherwise leave a role that can never be held
        /// again, and the routes asking for it would refuse everybody with no
        /// way to put it right.
        /// </para>
        /// <para>
        /// The account is made only when there is none at all. Nobody can sign
        /// in to a web interface whose accounts are empty, and an
        /// unauthenticated setup page would be a door of its own - so the
        /// password is made up here and shown once, on the console, to whoever
        /// started the process. It is never written down: what the accounts
        /// hold is the hash the HTTPExt API makes of it.
        /// </para>
        /// </remarks>
        private async Task EnsureAccounts()
        {

            // Read what is on disk first. The HTTPExt API writes its accounts
            // as it goes but does not read them back when it is built, so an
            // RoamingHub that skipped this would find no accounts at every start,
            // make a second root beside the first, and refuse the password its
            // owner already has.
            await ExtAPI.LoadDatabase();

            var firstStart  = !ExtAPI.Users.Any();

            IUser?  admin   = null;

            #region The one account, when there is none

            if (firstStart)
            {

                var password  = RandomExtensions.RandomString(24);
                var userId    = User_Id.Parse(DefaultAdminUser);

                var organization  = await ExtAPI.CreateOrganizationIfNotExists(
                                              Organization_Id.Parse(DefaultOrganization),
                                              I18NString.Create(Languages.en, DefaultOrganization)
                                          );

                if (organization is not Organization emspOrganization)
                    throw new InvalidOperationException("The organization of this RoamingHub could not be created, and an account outside one cannot sign in.");

                // CreateUser rather than AddUser: the password is set from
                // inside the OnAdded callback, where the user already has its
                // API back-reference, and that is the only place the password
                // store can be reached.
                admin         = await ExtAPI.CreateUser(
                                          userId,
                                          I18NString.Create(Languages.en, DefaultAdminUser),
                                          SimpleEMailAddress.Parse($"{DefaultAdminUser}@localhost"),
                                          User2OrganizationEdgeLabel.IsAdmin,
                                          emspOrganization,
                                          Password:                  password,
                                          SkipDefaultNotifications:  true,
                                          SkipNewUserEMail:          true,
                                          SkipNewUserNotifications:  true,

                                          // Without this nobody can sign in, and
                                          // nothing says why: the sign-in paths
                                          // require an accepted EULA and refuse a
                                          // correct password without one.
                                          AcceptedEULA:              TimeProvider.GetUtcNow().AddSeconds(-1),

                                          IsAuthenticated:           true
                                      );

                if (admin is null)
                    throw new InvalidOperationException("The account of this RoamingHub could not be created, so nobody could sign in to it.");

                GeneratedPassword = password;

                Log.Notice($"No accounts were found, so '{DefaultAdminUser}' was made up and put in the {UserRole.SystemAdmin.Name} group.",
                           "web", "auth");

            }

            #endregion

            #region The three groups

            foreach (var role in UserRole.All)
            {

                if (ExtAPI.TryGetUserGroup(role.GroupId, out _))
                    continue;

                var added = await ExtAPI.AddUserGroup(
                                      new UserGroup(
                                          role.GroupId,
                                          I18NString.Create(Languages.en, role.Name)
                                      )
                                  );

                // Looked at, and that is the point: this answers with a result
                // rather than throwing, so a group it declined to make would
                // otherwise leave a role nobody can ever hold.
                if (added.Result != CommandResult.Success)
                    throw new InvalidOperationException(
                              $"The user group '{role.GroupId}' of this RoamingHub could not be made: " +
                              $"{added.Description.FirstText()} A role without its group is a role nobody can hold."
                          );

            }

            #endregion

            #region The one account joins the one group that can fix the rest

            if (admin is not null)
            {

                if (!ExtAPI.TryGetUser     (admin.Id,                     out var storedAdmin) ||
                    !ExtAPI.TryGetUserGroup(UserRole.SystemAdmin.GroupId, out var adminGroup)  ||
                     storedAdmin is not User      user ||
                     adminGroup  is not UserGroup group)
                {
                    throw new InvalidOperationException(
                              $"The account of this RoamingHub could not be put in the {UserRole.SystemAdmin.Name} group, " +
                               "so the one account it has would be allowed to do nothing at all."
                          );
                }

                var joined = await ExtAPI.AddUserToUserGroup(
                                       user,
                                       User2UserGroupEdgeLabel.IsAdmin,
                                       group
                                   );

                if (!joined.IsSuccess)
                    throw new InvalidOperationException(
                              $"The account '{DefaultAdminUser}' could not be put in the {UserRole.SystemAdmin.Name} group: " +
                              $"{joined.ErrorDescription?.FirstText()} It would be able to do nothing at all."
                          );

            }

            #endregion

        }

        #endregion

        #region ConfigurationJSON()

        /// <summary>
        /// What this RoamingHub is made of, as the Configuration page of the web
        /// interface reads it.
        /// </summary>
        /// <remarks>
        /// Read-only: it answers "what am I running", not "change it". Nothing
        /// here is a secret - the accounts appear as the path they live at and
        /// the route to sign in, and never as anything about a password; the
        /// roaming partners appear as a count, and never as their tokens.
        /// </remarks>
        public JObject ConfigurationJSON()

            => new (

                   new JProperty("RoamingHub", new JObject(
                       new JProperty("version",        Version),
                       new JProperty("createdAt",      CreatedAt.ToString("o")),
                       new JProperty("machine",        Environment.MachineName),
                       new JProperty("runtime",        Environment.Version.ToString()),
                       new JProperty("os",             Environment.OSVersion.ToString())
                   )),

                   new JProperty("http",       new JObject(
                       new JProperty("serverName",     httpServer.HTTPServerName),
                       new JProperty("url",            WebInterfaceURL.ToString()),
                       new JProperty("apiPath",        httpRootPath.ToString()),
                       new JProperty("running",        started),
                       new JProperty("frontend",       Frontend.Description),
                       new JProperty("webInterface",   WebInterface is not null)
                   )),

                   new JProperty("web",        new JObject(
                       new JProperty("accountsPath",   AccountsPath),
                       new JProperty("sharedAccounts", !OwnsExtAPI),
                       new JProperty("signInAt",       $"{ExtAPI.RootPath.ToString().TrimEnd('/')}/login"),
                       new JProperty("users",          ExtAPI.Users.     Count()),
                       new JProperty("groups",         ExtAPI.UserGroups.Count()),
                       new JProperty("cookie",         ExtAPI.SessionCookieName.ToString()),
                       new JProperty("maxLifetime",    ExtAPI.MaxSignInSessionLifetime.ToString())
                   )),

                   new JProperty("log",        new JObject(
                       new JProperty("capacity",       Log.Capacity),
                       new JProperty("entries",        Log.Count),
                       new JProperty("lastId",         Log.LastId),
                       new JProperty("debugBridge",    traceBridge is not null),
                       new JProperty("console",        consoleLog is not null),
                       new JProperty("tags",           new JArray(Log.KnownTags))
                   )),

                   new JProperty("time",       new JObject(
                       new JProperty("nts",            ntsClient.Hostname.ToString()),
                       new JProperty("now",            TimeProvider.GetUtcNow().ToString("o"))
                   )),

                   new JProperty("ocpi",       new JObject(
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
                   )),

                   new JProperty("traffic",    new JObject(
                       new JProperty("capacity",         Traffic.Capacity),
                       new JProperty("calls",            Traffic.Count),
                       new JProperty("lastId",           Traffic.LastId),
                       new JProperty("payloads",         OCPI.Logging?.Payloads == true),
                       new JProperty("parties",          new JArray(Traffic.KnownParties))
                   )),

                   new JProperty("assemblies", new JArray(
                       AssemblyJSON<HTTPServer>                                  ("Hermod"),
                       AssemblyJSON<NTSClient>                                   ("Norn"),
                       AssemblyJSON<protocols.OCPI.CommonHTTPAPI>                ("OCPI"),
                       AssemblyJSON<protocols.OCPIv2_2_1.CommonAPI>              ("OCPI 2.2.1"),
                       AssemblyJSON<protocols.OCPIv2_3_0.CommonAPI>              ("OCPI 2.3.0")
                   ))

               );

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
        /// Whether this request is a browser hanging on the event stream.
        /// </summary>
        private static Boolean IsEventStream(HTTPRequest Request)
            => Request.Path.ToString().EndsWith("/events", StringComparison.Ordinal);

        #endregion

        #region DisposeAsync()

        /// <summary>
        /// Stop listening and let go of the console and the debug bridge.
        /// </summary>
        public async ValueTask DisposeAsync()
        {

            await Stop();

            traceBridge?.Dispose();
            consoleLog? .Dispose();

            reconfigureLock.Dispose();

            GC.SuppressFinalize(this);

        }

        #endregion

    }

}
