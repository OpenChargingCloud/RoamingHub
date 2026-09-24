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

#endregion

namespace cloud.charging.open.RoamingHub.Configuration
{

    /// <summary>
    /// The "ocpi" section of the configuration file: who this hub is when it
    /// speaks OCPI, which versions of it it speaks, and where its roaming
    /// partners find it.
    /// </summary>
    /// <remarks>
    /// As in the DNS and NTS sections, null means "the file does not say": what
    /// is missing keeps whatever the hub was given at construction, and an
    /// hub given nothing keeps the default.
    ///
    /// Read once, at the start, and not changeable while running - unlike the
    /// name servers and the time server. A country code and a party
    /// identification are what every roaming partner knows this hub by, and
    /// what it wrote into their credentials; changing them under a live
    /// registration would not rename the hub, it would make it a second one
    /// nobody is registered with. The same goes for the versions offered: a
    /// version taken away while a partner is on it is a partner that stops
    /// mid-session.
    /// </remarks>
    /// <param name="CountryCode">The ISO 3166-1 alpha-2 country code of this hub, e.g. "DE".</param>
    /// <param name="PartyId">The three-character party identification, e.g. "GDH".</param>
    /// <param name="Name">The business name of this hub, as its partners see it in the credentials.</param>
    /// <param name="Website">Its website, or null when it has none to give.</param>
    /// <param name="Versions">Which OCPI versions are offered; every one this hub knows when nothing is said.</param>
    /// <param name="ExternalURL">Where roaming partners reach this hub from outside - scheme, host and port - or null to use the address it listens on.</param>
    /// <param name="LocationsAsOpenData">Whether anybody may read the locations this hub holds without a token.</param>
    /// <param name="TariffsAsOpenData">Whether anybody may read the tariffs this hub holds without a token.</param>
    /// <param name="AllowDowngrades">Whether a partner may overwrite an object with an older one.</param>
    /// <param name="Logging">What of the OCPI traffic ends up in the event log.</param>
    public sealed record OCPIConfiguration(String?                 CountryCode           = null,
                                           String?                 PartyId               = null,
                                           String?                 Name                  = null,
                                           String?                 Website               = null,
                                           IReadOnlyList<String>?  Versions              = null,
                                           String?                 ExternalURL           = null,
                                           Boolean?                LocationsAsOpenData   = null,
                                           Boolean?                TariffsAsOpenData     = null,
                                           Boolean?                AllowDowngrades       = null,
                                           OCPILogging?            Logging               = null)
    {

        #region Data

        /// <summary>
        /// The name of this section in the configuration file.
        /// </summary>
        public const String  SectionName                 = "ocpi";

        /// <summary>
        /// The country this hub is in when nothing says otherwise.
        /// </summary>
        public const String  DefaultCountryCode          = "DE";

        /// <summary>
        /// The party identification when nothing says otherwise.
        /// </summary>
        public const String  DefaultPartyId              = "GDH";

        /// <summary>
        /// The business name when nothing says otherwise.
        /// </summary>
        public const String  DefaultName                 = "GraphDefined Roaming Hub";

        /// <summary>
        /// The OCPI versions this hub knows how to speak, oldest first.
        /// </summary>
        /// <remarks>
        /// 2.1.1 is not among them, and cannot be: the hub role arrived with
        /// OCPI 2.2, and the library has no hub side for 2.1.1 at all. A CPO
        /// or an EMSP that speaks only 2.1.1 therefore cannot be peered with
        /// this hub - it has to talk to its counterpart directly.
        /// </remarks>
        public static readonly IReadOnlyList<String>  KnownVersions    = [ "2.2.1", "2.3.0" ];

        /// <summary>
        /// The version offered when the file does not say.
        /// </summary>
        /// <remarks>
        /// A peer picks the newest it speaks itself, and offering an older one
        /// beside it costs nothing but a line in the versions list - but there
        /// is no older one here. 2.3.0 is offered only where the file asks for
        /// it, because 2.2.1 is the one both ends of a peering have been run
        /// against.
        /// </remarks>
        public static readonly IReadOnlyList<String>  DefaultVersions  = [ "2.2.1" ];

        /// <summary>
        /// The longest a business name may be written.
        /// </summary>
        public const Int32   MaxNameLength               = 100;

        /// <summary>
        /// The longest a website may be written.
        /// </summary>
        public const Int32   MaxWebsiteLength            = 255;

        /// <summary>
        /// The longest the external URL may be written.
        /// </summary>
        public const Int32   MaxExternalURLLength        = 255;

        #endregion

        #region Properties

        /// <summary>
        /// The versions offered, with the defaults filled in: what the file
        /// said, or the default two.
        /// </summary>
        public IReadOnlyList<String>  EffectiveVersions
            => Versions is { Count: > 0 }
                   ? Versions
                   : DefaultVersions;

        #endregion


        #region (static) TryParse(JSON, out Configuration, out Error)

        /// <summary>
        /// The "ocpi" section, or the one sentence that says what is wrong with it.
        /// </summary>
        public static Boolean TryParse(JObject                                     JSON,
                                       [NotNullWhen(true)]  out OCPIConfiguration?  Configuration,
                                       [NotNullWhen(false)] out String?             Error)
        {

            Configuration  = null;
            Error          = null;

            if (!ConfigurationReader.TryReadString (JSON, "countryCode",          SectionName,   2,                    out var countryCode,  out Error) ||
                !ConfigurationReader.TryReadString (JSON, "partyId",              SectionName,   3,                    out var partyId,      out Error) ||
                !ConfigurationReader.TryReadString (JSON, "name",                 SectionName,   MaxNameLength,        out var name,         out Error) ||
                !ConfigurationReader.TryReadString (JSON, "website",              SectionName,   MaxWebsiteLength,     out var website,      out Error) ||
                !ConfigurationReader.TryReadStrings(JSON, "versions",             SectionName,   KnownVersions.Count, 8, out var versions,   out Error) ||
                !ConfigurationReader.TryReadString (JSON, "externalURL",          SectionName,   MaxExternalURLLength, out var externalURL,  out Error) ||
                !ConfigurationReader.TryReadBoolean(JSON, "locationsAsOpenData",  SectionName,                         out var locationsOpen, out Error) ||
                !ConfigurationReader.TryReadBoolean(JSON, "tariffsAsOpenData",    SectionName,                         out var tariffsOpen,  out Error) ||
                !ConfigurationReader.TryReadBoolean(JSON, "allowDowngrades",      SectionName,                         out var downgrades,   out Error))
            {
                return false;
            }

            #region The identification, which OCPI is strict about

            if (countryCode is not null && (countryCode.Length != 2 || !countryCode.All(Char.IsAsciiLetter)))
            {
                Error = $"'{SectionName}.countryCode' must be two letters, e.g. \"DE\".";
                return false;
            }

            if (partyId is not null && (partyId.Length != 3 || !partyId.All(Char.IsAsciiLetterOrDigit)))
            {
                Error = $"'{SectionName}.partyId' must be three letters or digits, e.g. \"GDH\".";
                return false;
            }

            #endregion

            #region The versions, which have to be ones this hub speaks

            if (versions is not null)
            {

                var unknown = versions.Where(version => !KnownVersions.Contains(version)).ToArray();

                if (unknown.Length > 0)
                {
                    Error = $"'{SectionName}.versions': {String.Join(", ", unknown.Select(version => $"\"{version}\""))} " +
                            $"{(unknown.Length == 1 ? "is not an OCPI version" : "are not OCPI versions")} this hub speaks. " +
                            $"Known versions: {String.Join(", ", KnownVersions)}.";
                    return false;
                }

                if (versions.Count == 0)
                {
                    Error = $"'{SectionName}.versions' must name at least one version; a hub speaking no OCPI at all has no roaming partners.";
                    return false;
                }

                versions = [.. versions.Distinct().OrderBy(version => KnownVersions.ToList().IndexOf(version))];

            }

            #endregion

            #region The external URL, which has to be one

            if (externalURL is not null)
            {

                if (!Uri.TryCreate(externalURL, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    Error = $"'{SectionName}.externalURL' must be an absolute http or https URL, e.g. \"https://hub.example.org\".";
                    return false;
                }

                externalURL = externalURL.TrimEnd('/');

            }

            #endregion

            #region Logging

            OCPILogging? logging = null;

            if (JSON["logging"] is JToken loggingToken && loggingToken.Type != JTokenType.Null)
            {

                if (loggingToken is not JObject loggingJSON)
                {
                    Error = $"'{SectionName}.logging' must be a JSON object.";
                    return false;
                }

                if (!OCPILogging.TryParse(loggingJSON, out logging, out Error))
                    return false;

            }

            #endregion

            Configuration = new OCPIConfiguration(
                                countryCode?.ToUpperInvariant(),
                                partyId?.    ToUpperInvariant(),
                                name,
                                website,
                                versions,
                                externalURL,
                                locationsOpen,
                                tariffsOpen,
                                downgrades,
                                logging
                            );

            return true;

        }

        #endregion

        #region ToJSON()

        /// <summary>
        /// The section as it is written to the file; what this hub was not
        /// told about is not written, so that the file keeps saying "the
        /// default decides" rather than freezing today's default.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject();

            if (CountryCode         is not null)  json.Add("countryCode",          CountryCode);
            if (PartyId             is not null)  json.Add("partyId",              PartyId);
            if (Name                is not null)  json.Add("name",                 Name);
            if (Website             is not null)  json.Add("website",              Website);
            if (Versions            is not null)  json.Add("versions",             new JArray(Versions));
            if (ExternalURL         is not null)  json.Add("externalURL",          ExternalURL);
            if (LocationsAsOpenData.HasValue)     json.Add("locationsAsOpenData",  LocationsAsOpenData.Value);
            if (TariffsAsOpenData.  HasValue)     json.Add("tariffsAsOpenData",    TariffsAsOpenData.  Value);
            if (AllowDowngrades.    HasValue)     json.Add("allowDowngrades",      AllowDowngrades.    Value);
            if (Logging             is not null)  json.Add("logging",              Logging.ToJSON());

            return json;

        }

        #endregion

        #region (override) ToString()

        public override String ToString()

            => String.Concat(
                   CountryCode ?? DefaultCountryCode, "-", PartyId ?? DefaultPartyId,
                   " (", Name ?? DefaultName, ", OCPI ", String.Join(", ", EffectiveVersions), ")"
               );

        #endregion

    }


    /// <summary>
    /// What of the OCPI traffic ends up in the event log.
    /// </summary>
    /// <remarks>
    /// The requests are on by default and the payloads are off: a request line
    /// says who asked what, and that is what somebody watching a peering wants
    /// to see; the payload of a charge detail record says who charged where and
    /// for how much, which is not something to keep in a log nobody asked for.
    /// </remarks>
    /// <param name="Requests">Whether every OCPI request a partner makes is logged.</param>
    /// <param name="Payloads">Whether the objects partners push - locations, sessions, charge detail records - are logged whole.</param>
    public sealed record OCPILogging(Boolean?  Requests   = null,
                                     Boolean?  Payloads   = null)
    {

        #region (static) TryParse(JSON, out Logging, out Error)

        public static Boolean TryParse(JObject                                JSON,
                                       [NotNullWhen(true)]  out OCPILogging?  Logging,
                                       [NotNullWhen(false)] out String?       Error)
        {

            Logging = null;

            if (!ConfigurationReader.TryReadBoolean(JSON, "requests", "ocpi.logging", out var requests, out Error) ||
                !ConfigurationReader.TryReadBoolean(JSON, "payloads", "ocpi.logging", out var payloads, out Error))
            {
                return false;
            }

            Logging = new OCPILogging(requests, payloads);
            return true;

        }

        #endregion

        #region ToJSON()

        public JObject ToJSON()
        {

            var json = new JObject();

            if (Requests.HasValue)  json.Add("requests",  Requests.Value);
            if (Payloads.HasValue)  json.Add("payloads",  Payloads.Value);

            return json;

        }

        #endregion

    }

}
