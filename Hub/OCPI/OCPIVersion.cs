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

using cloud.charging.open.protocols.OCPI;

#endregion

namespace cloud.charging.open.RoamingHub.OCPI
{

    #region RemotePartySpec

    /// <summary>
    /// What it takes to add a roaming partner: who they are, the token they
    /// will use with this hub, and - when this hub is the one that starts
    /// the peering - the token and the versions URL they handed out.
    /// </summary>
    /// <remarks>
    /// Two ways to add one, and they are the two halves of the OCPI
    /// registration. With only <paramref name="OurToken"/> the partner is
    /// expected to come to this hub: they GET the versions with that token
    /// and POST their credentials, and the library fills in the rest. With
    /// <paramref name="TheirToken"/> and <paramref name="TheirVersionsURL"/>
    /// as well, this hub can go to them - see
    /// <see cref="OCPIVersion.Register"/>.
    /// </remarks>
    /// <param name="CountryCode">The partner's country code.</param>
    /// <param name="PartyId">The partner's party identification.</param>
    /// <param name="Role">What the partner is - a CPO, almost always.</param>
    /// <param name="Name">Its business name.</param>
    /// <param name="Website">Its website, or null.</param>
    /// <param name="OurToken">The token the partner presents to this hub.</param>
    /// <param name="TheirToken">The token this hub presents to the partner, or null while they have not handed one out.</param>
    /// <param name="TheirVersionsURL">Where the partner's versions endpoint is, or null.</param>
    public sealed record RemotePartySpec(CountryCode      CountryCode,
                                         Party_Id         PartyId,
                                         Role             Role,
                                         String           Name,
                                         URL?             Website,
                                         AccessToken      OurToken,
                                         AccessToken?     TheirToken,
                                         URL?             TheirVersionsURL)
    {

        /// <summary>
        /// The identification the library files the partner under.
        /// </summary>
        public RemoteParty_Id  Id
            => RemoteParty_Id.From(CountryCode, PartyId, Role);

        /// <summary>
        /// Whether this hub has what it needs to start the peering itself.
        /// </summary>
        public Boolean  CanRegister
            => TheirToken.HasValue && TheirVersionsURL.HasValue;

    }

    #endregion

    #region RemotePartySummary

    /// <summary>
    /// A roaming partner as the web interface sees it: the same shape for
    /// every OCPI version, and never the whole of what the library keeps.
    /// </summary>
    public sealed record RemotePartySummary(String              Version,
                                            RemoteParty_Id      Id,
                                            CountryCode         CountryCode,
                                            Party_Id            PartyId,
                                            Role                Role,
                                            String              Name,
                                            URL?                Website,
                                            PartyStatus         Status,
                                            AccessToken?        OurToken,
                                            AccessStatus?       OurTokenStatus,
                                            AccessToken?        TheirToken,
                                            URL?                TheirVersionsURL,
                                            RemoteAccessStatus? RemoteStatus,
                                            Version_Id?         SelectedVersion,
                                            DateTimeOffset      Created,
                                            DateTimeOffset      LastUpdated)
    {

        /// <summary>
        /// Whether this hub holds what it needs to talk to the partner: a
        /// token of theirs and a place to send it.
        /// </summary>
        public Boolean  CanRegister
            => TheirToken.HasValue && TheirVersionsURL.HasValue;

        /// <summary>
        /// Whether the peering is complete in both directions.
        /// </summary>
        /// <remarks>
        /// A partner that only holds our token may still be on its way, and
        /// one whose token and versions URL were typed in by hand has not been
        /// spoken to yet. What the library writes down once the credentials
        /// have actually been exchanged - in either direction - is the version
        /// the two sides settled on, so that is what says "registered".
        /// </remarks>
        public Boolean  Registered
            => CanRegister && SelectedVersion.HasValue;

        /// <summary>
        /// The partner as the web interface reads it.
        /// </summary>
        /// <param name="IncludeSecrets">Whether the access tokens travel along. They are shown to whoever may manage partners, and to nobody else.</param>
        public JObject ToJSON(Boolean IncludeSecrets)

            => new (
                   new JProperty("version",            Version),
                   new JProperty("id",                 Id.ToString()),
                   new JProperty("countryCode",        CountryCode.ToString()),
                   new JProperty("partyId",            PartyId.ToString()),
                   new JProperty("role",               Role.ToString()),
                   new JProperty("name",               Name),
                   new JProperty("website",            Website?.ToString()),
                   new JProperty("status",             Status.ToString()),
                   new JProperty("ourToken",           IncludeSecrets ? OurToken?.ToString()   : null),
                   new JProperty("hasOurToken",        OurToken.HasValue),
                   new JProperty("ourTokenStatus",     OurTokenStatus?.ToString()),
                   new JProperty("theirToken",         IncludeSecrets ? TheirToken?.ToString() : null),
                   new JProperty("hasTheirToken",      TheirToken.HasValue),
                   new JProperty("theirVersionsURL",   TheirVersionsURL?.ToString()),
                   new JProperty("remoteStatus",       RemoteStatus?.ToString()),
                   new JProperty("selectedVersion",    SelectedVersion?.ToString()),
                   new JProperty("canRegister",        CanRegister),
                   new JProperty("registered",         Registered),
                   new JProperty("created",            Created.    ToString("o")),
                   new JProperty("lastUpdated",        LastUpdated.ToString("o"))
               );

    }

    #endregion

    #region OCPIOperationResult

    /// <summary>
    /// How something that was asked of a roaming partner, or of the store
    /// behind them, went.
    /// </summary>
    /// <param name="Success">Whether it worked.</param>
    /// <param name="Message">What happened, in a sentence the web interface can show.</param>
    /// <param name="Data">Whatever the operation has to hand back, or null.</param>
    public sealed record OCPIOperationResult(Boolean   Success,
                                             String    Message,
                                             JObject?  Data   = null)
    {

        public static OCPIOperationResult Ok    (String Message, JObject? Data = null)
            => new (true,  Message, Data);

        public static OCPIOperationResult Failed(String Message)
            => new (false, Message);

    }

    #endregion


    /// One OCPI version this hub speaks: the library's Common API and hub API
    /// for that version, behind one shape the rest of this hub can talk to
    /// without knowing which version it is holding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The library keeps a Common API per OCPI version, and each of those has
    /// its own peers - the data structures differ between the versions, so
    /// they cannot share one. Whoever is reading, on the other hand, wants
    /// one list of peers. This is where the two meet: every version answers
    /// the same questions, and the hub puts the answers side by side.
    /// </para>
    /// <para>
    /// Peers and nothing else, for now. A hub in OCPI also forwards what one
    /// peer sends to another and answers "who else is here" out of the
    /// hubclientinfo module; none of that is here yet, and the shape below
    /// says so by having nothing in it but the peering.
    /// </para>
    /// <para>
    /// There is no 2.1.1 binding and cannot be: the hub role arrived with
    /// OCPI 2.2 and the library has no hub side for the version before it.
    /// </para>
    /// </remarks>
    public abstract class OCPIVersion
    {

        #region Properties

        /// <summary>
        /// The hub this version belongs to.
        /// </summary>
        public Hub         Hub      { get; }

        /// <summary>
        /// The version identification, e.g. "2.2.1".
        /// </summary>
        public abstract Version_Id  Id  { get; }

        /// <summary>
        /// The version as the web interface writes it: "2.2.1".
        /// </summary>
        public String      Label
            => Id.ToString();

        #endregion

        #region Constructor(s)

        protected OCPIVersion(Hub Hub)
        {
            this.Hub = Hub;
        }

        #endregion


        #region Roaming partners

        /// <summary>
        /// Every roaming partner this version knows.
        /// </summary>
        public abstract IEnumerable<RemotePartySummary>  RemoteParties { get; }

        /// <summary>
        /// One roaming partner, or null.
        /// </summary>
        public RemotePartySummary? GetRemoteParty(RemoteParty_Id Id)
            => RemoteParties.FirstOrDefault(party => party.Id == Id);

        /// <summary>
        /// Add a roaming partner. Answers with what went wrong, or null.
        /// </summary>
        public abstract Task<String?>  AddRemoteParty(RemotePartySpec Spec);

        /// <summary>
        /// Forget a roaming partner: its tokens stop working the moment this
        /// returns.
        /// </summary>
        public abstract Task<Boolean>  RemoveRemoteParty(RemoteParty_Id Id);

        /// <summary>
        /// Start the peering with a partner that handed out its token and its
        /// versions URL: fetch their versions, and POST this hub's
        /// credentials to them.
        /// </summary>
        public abstract Task<OCPIOperationResult>  Register(RemoteParty_Id Id);

        #endregion


        #region EndpointsJSON()

        /// <summary>
        /// Where this version is: its version details, its credentials
        /// endpoint and its hub modules, as absolute URLs a peer would be
        /// told.
        /// </summary>
        /// <remarks>
        /// Computed here rather than read back from the library, because the
        /// library builds them per request from the Host header, and a page
        /// has no request to hand it. The shape is the library's: the version
        /// details at "versions/2.2.1", the credentials at
        /// "v2.2.1/credentials", the modules at "v2.2.1/hub/{module}".
        /// </remarks>
        public JObject EndpointsJSON()
        {

            var baseURL  = Hub.OCPIBaseURL.ToString().TrimEnd('/');
            var prefix   = $"{baseURL}/v{Label}";

            return new JObject(
                       new JProperty("version",       Label),
                       new JProperty("details",       $"{baseURL}/versions/{Label}"),
                       new JProperty("credentials",   $"{prefix}/credentials"),
                       new JProperty("modules",       new JObject(
                           Modules.Select(module => new JProperty(module, $"{prefix}/hub/{module}"))
                       ))
                   );

        }

        /// <summary>
        /// The hub modules this version offers, by the name OCPI gives them.
        /// </summary>
        protected abstract IEnumerable<String>  Modules { get; }

        #endregion

        #region (protected) Summarize(...)

        /// <summary>
        /// The parts of a remote party that every version keeps in the same
        /// place, plus the four that each version keeps somewhere else.
        /// </summary>
        protected RemotePartySummary Summarize(RemoteParty      Party,
                                               CountryCode      CountryCode,
                                               Party_Id         PartyId,
                                               Role             Role,
                                               BusinessDetails  BusinessDetails)
        {

            var local   = Party.LocalAccessInfos. FirstOrDefault();
            var remote  = Party.RemoteAccessInfos.FirstOrDefault();

            return new RemotePartySummary(
                       Label,
                       Party.Id,
                       CountryCode,
                       PartyId,
                       Role,
                       BusinessDetails.Name,
                       BusinessDetails.Website,
                       Party.Status,
                       local?. AccessToken,
                       local?. Status,
                       remote?.AccessToken,
                       remote?.VersionsURL,
                       remote?.Status,
                       remote?.SelectedVersionId,
                       Party.Created,
                       Party.LastUpdated
                   );

        }

        #endregion

        #region (protected) WithVersion(JSON, Extra...)

        /// <summary>
        /// An object as the library wrote it, with the version - and whatever
        /// else - put in front.
        /// </summary>
        protected JObject WithVersion(JObject JSON, params JProperty[] Extra)
        {

            var json = new JObject(new JProperty("version", Label));

            foreach (var property in Extra)
                json.Add(property);

            foreach (var property in JSON.Properties())
                json[property.Name] = property.Value;

            return json;

        }

        #endregion

        #region (protected) Describe(...)

        /// <summary>
        /// A registration outcome in one sentence.
        /// </summary>
        protected static OCPIOperationResult DescribeRegistration(RemoteParty_Id  Id,
                                                                  StatusCode?     StatusCode,
                                                                  String?         StatusMessage,
                                                                  Boolean         GotCredentials)
        {

            if (GotCredentials && StatusCode == protocols.OCPI.StatusCode.Success)
                return OCPIOperationResult.Ok($"Registered with '{Id}': they accepted this hub's credentials and handed out theirs.");

            return OCPIOperationResult.Failed(
                       $"The registration with '{Id}' did not go through" +
                       (StatusCode.HasValue ? $" ({StatusCode}" + (StatusMessage.IsNotNullOrEmpty() ? $": {StatusMessage}" : "") + ")" : "") +
                       ". Check their versions URL and the token they handed out; every step is in the log."
                   );

        }

        #endregion

    }

}
