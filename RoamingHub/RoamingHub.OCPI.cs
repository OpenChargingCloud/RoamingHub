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

using System.Diagnostics.CodeAnalysis;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.OCPI;

using cloud.charging.open.RoamingHub.Configuration;
using cloud.charging.open.RoamingHub.OCPI;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// The OCPI side of this hub: the endpoints the peers call,
    /// the partners themselves, the tokens of its customers, and what the
    /// partners pushed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Where the endpoints are.</b> The OCPI library attaches to Hermod's
    /// HTTPExt API rather than to the HTTP server, and it builds the URLs it
    /// advertises - in the versions list and in the version details - from
    /// the Host header and its own path prefix, as if that HTTPExt API sat at
    /// the root of the server. Here it sits at "/ext". So the OCPI endpoints
    /// are registered directly below the HTTPExt API ("/ext/versions",
    /// "/ext/v2.2.1/credentials", "/ext/v2.2.1/emsp/locations"), and the
    /// HTTPExt API's own root path is handed to the library as the prefix it
    /// puts into the URLs it advertises. Both then agree, which is what a
    /// partner that follows the versions list needs.
    /// </para>
    /// <para>
    /// <b>What is per version.</b> The library keeps peers, tokens
    /// and the objects partners push per OCPI version, because the data
    /// structures differ between them. The web interface wants one list of
    /// each, so every version answers the same questions behind
    /// <see cref="OCPIVersion"/> and the answers are put side by side here.
    /// A partner is on exactly one version: the one it was added under, which
    /// is the one it registers on.
    /// </para>
    /// <para>
    /// <b>What is written down.</b> The library keeps its partners and its
    /// assets in append-only files of its own, below an "ocpi" directory
    /// beside the configuration file - one set per version - and reads them
    /// back when it is built. Nothing about OCPI is therefore in
    /// configuration.json but who this hub is and which versions it offers.
    /// </para>
    /// </remarks>
    public partial class RoamingHub
    {

        #region Data

        /// <summary>
        /// The directory the OCPI library keeps its files in, below the
        /// directory of the configuration file.
        /// </summary>
        public const String  OCPIDirectoryName  = "ocpi";

        private CommonHTTPAPI            ocpiAPI       = default!;
        private OCPIConfiguration        ocpiSettings  = new ();

        private readonly List<OCPIVersion>  ocpiVersions  = [];

        #endregion

        #region Properties

        /// <summary>
        /// Who this hub is in OCPI, and which versions it offers - as it was
        /// read at the start. Not changeable while running: see
        /// <see cref="OCPIConfiguration"/> for why an identity is a different
        /// kind of setting from an address.
        /// </summary>
        public OCPIConfiguration            OCPI
            => ocpiSettings;

        /// <summary>
        /// The library's Common HTTP API: the versions list every partner
        /// starts from.
        /// </summary>
        public CommonHTTPAPI                OCPIAPI
            => ocpiAPI;

        /// <summary>
        /// The OCPI versions this hub speaks, oldest first.
        /// </summary>
        public IReadOnlyList<OCPIVersion>   OCPIVersions
            => ocpiVersions;

        /// <summary>
        /// The country code and party identification of this hub.
        /// </summary>
        public Party_Idv3                   PartyId          { get; private set; }

        /// <summary>
        /// The same, written the way a hub's is written: "DE*GDH".
        /// </summary>
        /// <remarks>
        /// <see cref="Party_Idv3"/> writes itself without a separator, and
        /// with one only when told which role it is for - an EMSP's with a
        /// hyphen and everybody else's, a hub included, with an asterisk.
        /// Everything this hub shows about itself uses this form.
        /// </remarks>
        public String                       PartyIdText
            => PartyId.ToString(Role.HUB);

        /// <summary>
        /// The business details of this hub, as its partners see them in the
        /// credentials.
        /// </summary>
        public BusinessDetails              BusinessDetails  { get; private set; } = default!;

        /// <summary>
        /// Where the OCPI endpoints of this hub are, from outside: the HTTPExt
        /// API's root on the external address.
        /// </summary>
        public URL                          OCPIBaseURL      { get; private set; }

        /// <summary>
        /// The versions endpoint: the one URL a peer is given.
        /// </summary>
        public URL                          OCPIVersionsURL  { get; private set; }

        /// <summary>
        /// The directory the OCPI library keeps its files in.
        /// </summary>
        public String                       OCPIDirectory    { get; private set; } = default!;

        /// <summary>
        /// How many peers there are, over every version.
        /// </summary>
        public Int32                        RemotePartyCount
            => ocpiVersions.Sum(version => version.RemoteParties.Count());

        /// <summary>
        /// How many of the peers have finished the handshake in both
        /// directions.
        /// </summary>
        /// <remarks>
        /// The number somebody setting a hub up is actually watching: a peer
        /// that was added but never registered is a peer that cannot do
        /// anything here yet.
        /// </remarks>
        public Int32                        RegisteredPartyCount
            => ocpiVersions.Sum(version => version.RemoteParties.Count(party => party.Registered));

        #endregion


        #region (private) BuildOCPI(Configuration)

        /// <summary>
        /// The Common HTTP API and one binding per version offered. Nothing
        /// listens here: the endpoints are on the HTTP server everything else
        /// is on, and it is started by <see cref="Start"/>.
        /// </summary>
        private void BuildOCPI(OCPIConfiguration? Configuration)
        {

            ocpiSettings     = Configuration ?? new OCPIConfiguration();

            #region Who this hub is

            PartyId          = Party_Idv3.From(
                                   CountryCode.Parse(ocpiSettings.CountryCode ?? OCPIConfiguration.DefaultCountryCode),
                                   Party_Id.   Parse(ocpiSettings.PartyId     ?? OCPIConfiguration.DefaultPartyId)
                               );

            BusinessDetails  = new BusinessDetails(
                                   ocpiSettings.Name ?? OCPIConfiguration.DefaultName,
                                   ocpiSettings.Website is { } website && URL.TryParse(website, out var websiteURL)
                                       ? websiteURL
                                       : null
                               );

            #endregion

            #region Where its partners find it

            // The external URL when the operator wrote one down - a hub
            // behind a reverse proxy is reached by a name this process has
            // never heard of - and the address this hub listens on otherwise.
            var external     = ocpiSettings.ExternalURL
                                   ?? WebInterfaceURL.ToString().TrimEnd('/')[..^BasePathText.Length];

            var extRoot      = ExtAPI.RootPath.ToString().TrimEnd('/');

            OCPIBaseURL      = URL.Parse($"{external}{extRoot}");
            OCPIVersionsURL  = URL.Parse($"{external}{extRoot}/versions");

            // Host and port, which is what the library puts in front of the
            // paths it advertises in the versions list.
            var externalDNSName = new Uri(external).Authority;

            #endregion

            #region Where the library writes

            OCPIDirectory    = Path.Combine(Path.GetDirectoryName(ConfigFile.Path) ?? ".", OCPIDirectoryName);

            #endregion

            #region The Common HTTP API, and the versions on it

            ocpiAPI = new CommonHTTPAPI(

                          HTTPAPI:                   ExtAPI,
                          OurBaseURL:                OCPIBaseURL,
                          OurVersionsURL:            OCPIVersionsURL,

                          Description:               I18NString.Create("The OCPI endpoints of this hub"),

                          // At the HTTPExt API's own root, so that the paths
                          // the library serves and the paths it advertises
                          // are the same thing - see the class remarks.
                          RootPath:                  null,
                          AdditionalURLPathPrefix:   ExtAPI.RootPath,

                          ExternalDNSName:           externalDNSName,
                          HTTPServerName:            $"OpenChargingCloud EMSP v{Version}",
                          HTTPServiceName:           $"OpenChargingCloud EMSP v{Version}",

                          // What a CPO pushed into this hub is the CPO's,
                          // and whether it may be handed on to anybody who
                          // asks is the operator's decision, not a default.
                          LocationsAsOpenData:       ocpiSettings.LocationsAsOpenData ?? false,
                          TariffsAsOpenData:         ocpiSettings.TariffsAsOpenData   ?? false,
                          AllowDowngrades:           ocpiSettings.AllowDowngrades     ?? false,

                          // The library's own HTTP logging writes files
                          // nobody reads; what happens here goes to the event
                          // log instead. Its databases are unaffected - they
                          // are not logging, whatever the parameter is called.
                          DisableLogging:            true,
                          LoggingPath:               OCPIDirectory

                      );

            foreach (var version in ocpiSettings.EffectiveVersions)
            {

                ocpiVersions.Add(
                    version switch {
                        "2.2.1"  => new OCPIv2_2_1(this, ocpiAPI, OCPIDirectory),
                        "2.3.0"  => new OCPIv2_3_0(this, ocpiAPI, OCPIDirectory),
                        _        => throw new InvalidOperationException($"'{version}' is not an OCPI version this hub speaks.")
                    }
                );

            }

            #endregion

            #region Somebody discovering this hub

            ocpiAPI.OnGetVersionsResponse += (timestamp, api, request, response, cancellationToken) => {

                if (ocpiSettings.Logging?.Requests != false)
                    Log.Info(
                        request.RemoteParty is not null
                            ? $"The peer '{request.RemoteParty.Id}' asked for the OCPI versions."
                            : $"Somebody at {request.HTTPRequest.RemoteSocket} asked for the OCPI versions" +
                              (request.AccessToken.HasValue ? " with a token this hub does not know." : " without a token."),
                        "ocpi", "versions"
                    );

                return Task.CompletedTask;

            };

            #endregion

            Log.Info(
                $"OCPI: this hub is {PartyIdText} '{BusinessDetails.Name}', speaking {String.Join(", ", ocpiVersions.Select(version => version.Label))}; " +
                $"its partners find it at {OCPIVersionsURL} and its files are below '{OCPIDirectory}'.",
                "ocpi"
            );

        }

        #endregion


        #region OCPIConfigurationJSON()

        /// <summary>
        /// The OCPI side of this hub, as its page in the web interface reads
        /// it: who it is, where it is, and the endpoints of every version.
        /// </summary>
        public JObject OCPIConfigurationJSON()

            => new (

                   new JProperty("party",        new JObject(
                       new JProperty("countryCode",    PartyId.CountryCode.ToString()),
                       new JProperty("partyId",        PartyId.PartyId.    ToString()),
                       new JProperty("id",             PartyIdText),
                       new JProperty("role",           "HUB"),
                       new JProperty("name",           BusinessDetails.Name),
                       new JProperty("website",        BusinessDetails.Website?.ToString())
                   )),

                   new JProperty("endpoints",    new JObject(
                       new JProperty("base",           OCPIBaseURL.    ToString()),
                       new JProperty("versions",       OCPIVersionsURL.ToString()),
                       new JProperty("externalURL",    ocpiSettings.ExternalURL),
                       new JProperty("byVersion",      new JArray(ocpiVersions.Select(version => version.EndpointsJSON())))
                   )),

                   new JProperty("versions",     new JArray(ocpiVersions.Select(version => version.Label))),
                   new JProperty("knownVersions", new JArray(OCPIConfiguration.KnownVersions)),

                   new JProperty("settings",     new JObject(
                       new JProperty("locationsAsOpenData",  ocpiAPI.LocationsAsOpenData),
                       new JProperty("tariffsAsOpenData",    ocpiAPI.TariffsAsOpenData),
                       new JProperty("allowDowngrades",      ocpiAPI.AllowDowngrades ?? false),
                       new JProperty("logRequests",          ocpiSettings.Logging?.Requests != false),
                       new JProperty("logPayloads",          ocpiSettings.Logging?.Payloads == true)
                   )),

                   new JProperty("counts",       new JObject(
                       new JProperty("partners",   RemotePartyCount),
                       new JProperty("registered", RegisteredPartyCount),
                       new JProperty("calls",      Traffic.Count)
                   )),

                   new JProperty("directory",    OCPIDirectory),
                   new JProperty("file",         ConfigFile.Path)

               );

        #endregion

        #region (private) WithPresence(JSON, Party)

        /// <summary>
        /// Add what this hub knows about a peer's connection to what it knows
        /// about its registration.
        /// </summary>
        /// <remarks>
        /// Merged into the same object rather than served beside it, because
        /// the two answer one question between them and a page that had to
        /// fetch them separately could show a peer as registered and say
        /// nothing about whether it is there. They are different kinds of
        /// fact, though, and the page keeps them apart: registration is what
        /// was agreed, connection is what is happening.
        /// </remarks>
        private JObject WithPresence(JObject JSON, RemotePartySummary Party)
        {

            var partyId = Party_Idv3.From(Party.CountryCode, Party.PartyId);

            JSON["connection"]       = presence.TryGetValue(partyId, out var peer)
                                           ? peer.Status.ToString()
                                           : PeerStatus.PLANNED.ToString();

            JSON["lastSeen"]         = peer?.LastSeen?.ToString("o");
            JSON["connectionSince"]  = peer?.LastUpdated.ToString("o");

            return JSON;

        }

        #endregion

        #region TryGetOCPIVersion(Label, out Version)

        /// <summary>
        /// The version written as "2.2.1", or false when this hub does not
        /// offer it.
        /// </summary>
        public Boolean TryGetOCPIVersion(String?                                 Label,
                                         [NotNullWhen(true)] out OCPIVersion?    Version)
        {

            Version = ocpiVersions.FirstOrDefault(version => String.Equals(version.Label, Label?.Trim(), StringComparison.OrdinalIgnoreCase));

            return Version is not null;

        }

        #endregion


        #region Peers

        #region RemotePartiesJSON(IncludeSecrets)

        /// <summary>
        /// Every peer, over every version, as the Peers
        /// page reads them.
        /// </summary>
        /// <param name="IncludeSecrets">Whether the access tokens travel along; only to whoever may manage partners.</param>
        public JObject RemotePartiesJSON(Boolean IncludeSecrets)

            => new (
                   new JProperty("partners",   new JArray(
                       ocpiVersions.SelectMany(version => version.RemoteParties).
                                    OrderBy   (party   => party.Id.ToString()).
                                    Select    (party   => WithPresence(party.ToJSON(IncludeSecrets), party))
                   )),
                   new JProperty("versions",   new JArray(ocpiVersions.Select(version => version.Label))),
                   new JProperty("roles",      new JArray("CPO", "EMSP", "HUB")),
                   new JProperty("ourVersionsURL", OCPIVersionsURL.ToString())
               );

        #endregion

        #region AddRemotePartyAsync(JSON)

        /// <summary>
        /// Add a peer, on the version the request names.
        /// </summary>
        /// <remarks>
        /// An empty token of ours means "make one up", and the made-up one
        /// comes back in the result - it is what the operator hands to the
        /// partner, so it has to be shown once. The library keeps it readable,
        /// because it has to compare it on every request; the page shows it
        /// only to whoever may manage partners.
        /// </remarks>
        public async Task<OCPIOperationResult> AddRemotePartyAsync(JObject JSON)
        {

            #region The version

            var label = JSON.Value<String>("version")?.Trim();

            if (String.IsNullOrEmpty(label))
                label = ocpiVersions.LastOrDefault()?.Label;

            if (!TryGetOCPIVersion(label, out var version))
                return OCPIOperationResult.Failed($"'{label}' is not an OCPI version this hub offers; there are {String.Join(", ", ocpiVersions.Select(v => v.Label))}.");

            #endregion

            #region Who they are

            if (!CountryCode.TryParse(JSON.Value<String>("countryCode")?.Trim().ToUpperInvariant() ?? "", out var countryCode))
                return OCPIOperationResult.Failed("A 'countryCode' of two letters is required, e.g. \"DE\".");

            if (!Party_Id.TryParse(JSON.Value<String>("partyId")?.Trim().ToUpperInvariant() ?? "", out var partyId))
                return OCPIOperationResult.Failed("A 'partyId' of three letters or digits is required, e.g. \"GEF\".");

            var roleText = JSON.Value<String>("role")?.Trim();

            if (String.IsNullOrEmpty(roleText))
                roleText = "CPO";

            if (!Role.TryParse(roleText.ToUpperInvariant(), out var role))
                return OCPIOperationResult.Failed($"'{roleText}' is not an OCPI role; there are CPO, EMSP, HUB, NSP, SCSP and OTHER.");

            // A hub is a room, and the two roles that belong in it are the
            // ones a roaming agreement has ends for. HUB is allowed as well,
            // because hubs peer with hubs; everything else OCPI names is a
            // role nothing here would know what to do with.
            if (role != Role.CPO && role != Role.EMSP && role != Role.HUB)
                return OCPIOperationResult.Failed($"'{role}' is not a role this hub peers with; there are CPO, EMSP and HUB.");

            if (countryCode == PartyId.CountryCode && partyId == PartyId.PartyId && role == Role.HUB)
                return OCPIOperationResult.Failed("That is this hub itself; a hub cannot be its own peer.");

            var name = JSON.Value<String>("name")?.Trim();

            if (String.IsNullOrEmpty(name))
                return OCPIOperationResult.Failed("A 'name' is required: what the partner calls itself.");

            URL? website = null;

            if (JSON.Value<String>("website")?.Trim() is { Length: > 0 } websiteText)
            {

                if (!URL.TryParse(websiteText, out var websiteURL))
                    return OCPIOperationResult.Failed($"'{websiteText}' is not a URL.");

                website = websiteURL;

            }

            #endregion

            #region The tokens, and where to send ours

            AccessToken ourToken;

            if (JSON.Value<String>("ourToken")?.Trim() is { Length: > 0 } ourTokenText)
            {

                if (!AccessToken.TryParse(ourTokenText, out ourToken))
                    return OCPIOperationResult.Failed("'ourToken' is not a token the OCPI library accepts.");

            }
            else
                ourToken = AccessToken.NewRandom();

            AccessToken?  theirToken        = null;
            URL?          theirVersionsURL  = null;

            var theirTokenText = JSON.Value<String>("theirToken")?.  Trim();
            var theirURLText   = JSON.Value<String>("versionsURL")?. Trim();

            if (!String.IsNullOrEmpty(theirTokenText) || !String.IsNullOrEmpty(theirURLText))
            {

                if (String.IsNullOrEmpty(theirTokenText) || String.IsNullOrEmpty(theirURLText))
                    return OCPIOperationResult.Failed("To start the peering from here both are needed: the partner's token ('theirToken') and its versions URL ('versionsURL'). Leave both empty to let the partner come to this hub.");

                if (!AccessToken.TryParse(theirTokenText, out var parsedTheirToken))
                    return OCPIOperationResult.Failed("'theirToken' is not a token the OCPI library accepts.");

                if (!URL.TryParse(theirURLText, out var parsedURL))
                    return OCPIOperationResult.Failed($"'{theirURLText}' is not a URL.");

                theirToken        = parsedTheirToken;
                theirVersionsURL  = parsedURL;

            }

            #endregion

            var spec = new RemotePartySpec(countryCode, partyId, role, name, website, ourToken, theirToken, theirVersionsURL);

            #region Not twice

            foreach (var other in ocpiVersions)
            {
                if (other.GetRemoteParty(spec.Id) is not null)
                    return OCPIOperationResult.Failed($"There already is a peer '{spec.Id}' on OCPI {other.Label}. Remove it first to add it again.");
            }

            #endregion

            var error = await version.AddRemoteParty(spec);

            if (error is not null)
                return OCPIOperationResult.Failed(error);

            Log.Notice(
                $"The peer '{spec.Id}' ('{name}') was added on OCPI {version.Label}" +
                (spec.CanRegister
                     ? $", with its token and its versions URL {theirVersionsURL}: this hub can start the peering."
                     : ": it is expected to register with the token it was given."),
                "ocpi", "partner"
            );

            var added = version.GetRemoteParty(spec.Id);

            RefreshPresence();

            return OCPIOperationResult.Ok(
                       $"The peer '{spec.Id}' was added on OCPI {version.Label}.",
                       new JObject(
                           new JProperty("id",        spec.Id.ToString()),
                           new JProperty("version",   version.Label),
                           new JProperty("ourToken",  ourToken.ToString()),
                           new JProperty("partner",   added?.ToJSON(IncludeSecrets: true))
                       )
                   );

        }

        #endregion

        #region RegisterRemotePartyAsync(Label, Id)

        /// <summary>
        /// Start the peering with a peer: fetch their versions with the token
        /// they handed out, and POST this hub's credentials to them.
        /// </summary>
        /// <remarks>
        /// The one thing this hub does that a peer did not ask it to, so it
        /// is also the one call that has to be put into the traffic log by
        /// hand - everything inbound is taken off the HTTP server. A
        /// registration that is in the event log and not in the traffic would
        /// leave somebody reading the traffic wondering where the peer's
        /// token suddenly came from.
        /// </remarks>
        public async Task<OCPIOperationResult> RegisterRemotePartyAsync(String? Label, String? Id)
        {

            if (!TryGetOCPIVersion(Label, out var version))
                return OCPIOperationResult.Failed($"'{Label}' is not an OCPI version this hub offers.");

            if (!RemoteParty_Id.TryParse(Id, out var remotePartyId))
                return OCPIOperationResult.Failed($"'{Id}' is not a remote party identification; they look like \"DE-GEF_CPO\".");

            Log.Info($"Starting the OCPI {version.Label} peering with '{remotePartyId}' ...", "ocpi", "credentials", "partner");

            var started = TimeProvider.GetTimestamp();

            var result  = await version.Register(remotePartyId);

            // Whether it worked or not: a registration that went through moves
            // the peer out of PLANNED, and one that did not may still have
            // changed what this hub holds about it.
            RefreshPresence();

            Log.Log(
                result.Success ? Logging.LogLevel.Notice : Logging.LogLevel.Warning,
                result.Message,
                "ocpi", "credentials", "partner"
            );

            RecordOutboundCall(
                Peer:       remotePartyId.ToString(),
                Version:    version.Label,
                Module:     "credentials",
                Method:     "POST",
                Path:       version.GetRemoteParty(remotePartyId)?.TheirVersionsURL?.ToString() ?? "(nowhere)",
                Succeeded:  result.Success,
                Duration:   TimeProvider.GetElapsedTime(started),
                Message:    result.Message
            );

            return result;

        }

        #endregion

        #region RemoveRemotePartyAsync(Label, Id)

        /// <summary>
        /// Forget a peer. Its token stops opening this hub the
        /// moment this returns; what it pushed stays, because that is a record
        /// and not a setting.
        /// </summary>
        public async Task<OCPIOperationResult> RemoveRemotePartyAsync(String? Label, String? Id)
        {

            if (!TryGetOCPIVersion(Label, out var version))
                return OCPIOperationResult.Failed($"'{Label}' is not an OCPI version this hub offers.");

            if (!RemoteParty_Id.TryParse(Id, out var remotePartyId))
                return OCPIOperationResult.Failed($"'{Id}' is not a remote party identification.");

            if (version.GetRemoteParty(remotePartyId) is null)
                return OCPIOperationResult.Failed($"There is no peer '{remotePartyId}' on OCPI {version.Label}.");

            if (!await version.RemoveRemoteParty(remotePartyId))
                return OCPIOperationResult.Failed($"The peer '{remotePartyId}' could not be removed.");

            Log.Notice($"The peer '{remotePartyId}' was removed from OCPI {version.Label}; its token no longer opens this hub.", "ocpi", "partner");

            RefreshPresence();

            return OCPIOperationResult.Ok($"The peer '{remotePartyId}' was removed.");

        }

        #endregion

        #endregion

    }

}
