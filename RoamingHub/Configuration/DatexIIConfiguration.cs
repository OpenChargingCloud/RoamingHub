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

using cloud.charging.open.protocols.WWCP.Node.Configuration;

#endregion

namespace cloud.charging.open.RoamingHub.Configuration
{

    /// <summary>
    /// The "datex2" section: which roaming networks this hub publishes as DATEX II 3.7 AFIR Energy Infrastructure
    /// publications, to be pulled from it and pushed to the Mobilithek, the German national access point.
    /// </summary>
    /// <remarks>
    /// AFIR wants the static and dynamic data of public recharging points at the national access point; the
    /// Mobilithek takes them pushed, over mutual TLS with the machine certificate it issued. Without a "mobilithek"
    /// block, a publication is only served to be pulled.
    /// </remarks>
    /// <param name="Pull">Whether the publications are served to be pulled, below "/datex2"; true by default. They are AFIR data, public.</param>
    /// <param name="Publications">The roaming networks published.</param>
    public sealed record DatexIIConfiguration(Boolean?                                         Pull           = null,
                                              IReadOnlyList<DatexIIPublicationConfiguration>?  Publications   = null)
    {

        #region Data

        /// <summary>
        /// The name of the section.
        /// </summary>
        public const String  SectionName      = "datex2";

        /// <summary>
        /// The most roaming networks published.
        /// </summary>
        public const Int32   MaxPublications  = 100;

        #endregion

        #region Properties

        /// <summary>
        /// Whether the publications are served to be pulled.
        /// </summary>
        public Boolean  EffectivePull
            => Pull ?? true;

        #endregion


        #region (static) TryParse(JSON, out Configuration, out Error)

        /// <summary>
        /// The "datex2" section, or the one sentence that says what is wrong with it.
        /// </summary>
        public static Boolean TryParse(JObject                                        JSON,
                                       [NotNullWhen(true)]  out DatexIIConfiguration?  Configuration,
                                       [NotNullWhen(false)] out String?                Error)
        {

            Configuration = null;

            if (!ConfigurationReader.TryReadBoolean(JSON, "pull", SectionName, out var pull, out Error))
                return false;

            List<DatexIIPublicationConfiguration>? publications = null;

            if (JSON["publications"] is JToken publicationsToken && publicationsToken.Type != JTokenType.Null)
            {

                if (publicationsToken is not JArray publicationsJSON)
                {
                    Error = $"'{SectionName}.publications' must be a JSON array.";
                    return false;
                }

                if (publicationsJSON.Count > MaxPublications)
                {
                    Error = $"'{SectionName}.publications' must not name more than {MaxPublications} roaming networks.";
                    return false;
                }

                publications = [];

                for (var i = 0; i < publicationsJSON.Count; i++)
                {

                    if (publicationsJSON[i] is not JObject publicationJSON)
                    {
                        Error = $"'{SectionName}.publications[{i}]' must be a JSON object.";
                        return false;
                    }

                    if (!DatexIIPublicationConfiguration.TryParse(publicationJSON, $"{SectionName}.publications[{i}]", out var publication, out Error))
                        return false;

                    if (publications.Any(known => known.RoamingNetworkId == publication.RoamingNetworkId))
                    {
                        Error = $"'{SectionName}.publications' must name the roaming network '{publication.RoamingNetworkId}' only once.";
                        return false;
                    }

                    publications.Add(publication);

                }

            }

            Configuration = new DatexIIConfiguration(pull, publications);
            return true;

        }

        #endregion

        #region ToJSON()

        /// <summary>
        /// The section as it is written to the file.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject();

            if (Pull.HasValue)         json.Add("pull",          Pull.Value);
            if (Publications is not null)  json.Add("publications",  new JArray(Publications.Select(publication => publication.ToJSON())));

            return json;

        }

        #endregion

        #region (override) ToString()

        public override String ToString()

            => $"DATEX II of {Publications?.Count ?? 0} roaming network(s){(EffectivePull ? ", to be pulled" : "")}" +
               $"{(Publications?.Any(publication => publication.Mobilithek is not null) == true ? ", pushed to the Mobilithek" : "")}";

        #endregion

    }


    /// <summary>
    /// One roaming network published as DATEX II.
    /// </summary>
    /// <param name="RoamingNetworkId">The roaming network published.</param>
    /// <param name="Country">The country of the publication creator, e.g. "DE".</param>
    /// <param name="NationalIdentifier">The national identifier of the publication creator, for the Mobilithek "DE-NAP-" and the name of the organisation there.</param>
    /// <param name="OperatorIds">The charging station operators published; all, unless given.</param>
    /// <param name="Language">The language of the publications, e.g. "de"; English by default.</param>
    /// <param name="EnergyStatusDeltaInterval">The least time between two status deltas of an EVSE for a changed energy status; one minute by default.</param>
    /// <param name="StatusDeltaInterval">The least time between two status deltas, the changes until then grouped; one second with the Mobilithek, else none.</param>
    /// <param name="SnapshotInterval">The longest time between two full status publications; six hours with the Mobilithek, else none.</param>
    /// <param name="MaxDeltasPerSnapshot">The most status deltas between two full status publications; 3500 with the Mobilithek, else none.</param>
    /// <param name="Mobilithek">Where and how the publications are pushed to the Mobilithek; not, unless given.</param>
    public sealed record DatexIIPublicationConfiguration(String                     RoamingNetworkId,
                                                         String                     Country,
                                                         String                     NationalIdentifier,
                                                         IReadOnlyList<String>?     OperatorIds                 = null,
                                                         String?                    Language                    = null,
                                                         TimeSpan?                  EnergyStatusDeltaInterval   = null,
                                                         TimeSpan?                  StatusDeltaInterval         = null,
                                                         TimeSpan?                  SnapshotInterval            = null,
                                                         UInt32?                    MaxDeltasPerSnapshot        = null,
                                                         MobilithekConfiguration?   Mobilithek                  = null)
    {

        #region (static) TryParse(JSON, Path, out Configuration, out Error)

        /// <summary>
        /// One publication, or the one sentence that says what is wrong with it.
        /// </summary>
        public static Boolean TryParse(JObject                                                   JSON,
                                       String                                                    Path,
                                       [NotNullWhen(true)]  out DatexIIPublicationConfiguration?  Configuration,
                                       [NotNullWhen(false)] out String?                           Error)
        {

            Configuration = null;

            if (!ConfigurationReader.TryReadString (JSON, "roamingNetworkId",           Path, 100,              out var roamingNetworkId,           out Error) ||
                !ConfigurationReader.TryReadStrings(JSON, "operatorIds",                Path, 1000, 50,         out var operatorIds,                out Error) ||
                !ConfigurationReader.TryReadString (JSON, "language",                   Path, 10,               out var language,                   out Error) ||
                !ConfigurationReader.TryReadSeconds(JSON, "energyStatusDeltaInterval",  Path, 0, 86400,         out var energyStatusDeltaInterval,  out Error) ||
                !ConfigurationReader.TryReadSeconds(JSON, "statusDeltaInterval",        Path, 0, 3600,          out var statusDeltaInterval,        out Error) ||
                !ConfigurationReader.TryReadSeconds(JSON, "snapshotInterval",           Path, 1, 7 * 86400,     out var snapshotInterval,           out Error) ||
                !ConfigurationReader.TryReadUInt32 (JSON, "maxDeltasPerSnapshot",       Path, 1, 1_000_000,     out var maxDeltasPerSnapshot,       out Error))
            {
                return false;
            }

            if (roamingNetworkId is null)
            {
                Error = $"'{Path}.roamingNetworkId' is missing: which roaming network to publish?";
                return false;
            }

            if (language is not null && !Enum.TryParse<Languages>(language, ignoreCase: true, out _))
            {
                Error = $"'{Path}.language' must be a language code, e.g. \"de\".";
                return false;
            }

            if (JSON["publicationCreator"] is not JObject creatorJSON)
            {
                Error = $"'{Path}.publicationCreator' must be a JSON object {{ country, nationalIdentifier }}.";
                return false;
            }

            if (!ConfigurationReader.TryReadString(creatorJSON, "country",             $"{Path}.publicationCreator", 2,   out var country,             out Error) ||
                !ConfigurationReader.TryReadString(creatorJSON, "nationalIdentifier",  $"{Path}.publicationCreator", 100, out var nationalIdentifier,  out Error))
            {
                return false;
            }

            if (country is null || !org.GraphDefined.Vanaheimr.Illias.Country.TryParseAlpha2Code(country, out _))
            {
                Error = $"'{Path}.publicationCreator.country' must be two letters of a country, e.g. \"DE\".";
                return false;
            }

            if (nationalIdentifier is null)
            {
                Error = $"'{Path}.publicationCreator.nationalIdentifier' is missing, e.g. \"DE-NAP-<your organisation at the Mobilithek>\".";
                return false;
            }

            MobilithekConfiguration? mobilithek = null;

            if (JSON["mobilithek"] is JToken mobilithekToken && mobilithekToken.Type != JTokenType.Null)
            {

                if (mobilithekToken is not JObject mobilithekJSON)
                {
                    Error = $"'{Path}.mobilithek' must be a JSON object.";
                    return false;
                }

                if (!MobilithekConfiguration.TryParse(mobilithekJSON, $"{Path}.mobilithek", out mobilithek, out Error))
                    return false;

            }

            Configuration = new DatexIIPublicationConfiguration(roamingNetworkId,
                                                                country.ToUpperInvariant(),
                                                                nationalIdentifier,
                                                                operatorIds,
                                                                language,
                                                                energyStatusDeltaInterval,
                                                                statusDeltaInterval,
                                                                snapshotInterval,
                                                                maxDeltasPerSnapshot,
                                                                mobilithek);

            return true;

        }

        #endregion

        #region ToJSON()

        /// <summary>
        /// The publication as it is written to the file.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject(
                           new JProperty("roamingNetworkId",    RoamingNetworkId),
                           new JProperty("publicationCreator",  new JObject(new JProperty("country",             Country),
                                                                            new JProperty("nationalIdentifier",  NationalIdentifier)))
                       );

            if (OperatorIds is not null)                 json.Add("operatorIds",                new JArray(OperatorIds));
            if (Language is not null)                    json.Add("language",                   Language);
            if (EnergyStatusDeltaInterval.HasValue)      json.Add("energyStatusDeltaInterval",  EnergyStatusDeltaInterval.Value.TotalSeconds);
            if (StatusDeltaInterval.HasValue)            json.Add("statusDeltaInterval",        StatusDeltaInterval.      Value.TotalSeconds);
            if (SnapshotInterval.HasValue)               json.Add("snapshotInterval",           SnapshotInterval.         Value.TotalSeconds);
            if (MaxDeltasPerSnapshot.HasValue)           json.Add("maxDeltasPerSnapshot",       MaxDeltasPerSnapshot.     Value);
            if (Mobilithek is not null)                  json.Add("mobilithek",                 Mobilithek.ToJSON());

            return json;

        }

        #endregion

    }


    /// <summary>
    /// Where and how a roaming network is pushed to the Mobilithek.
    /// </summary>
    /// <param name="StaticPublicationId">The publication (offer) of the Mobilithek taking the table publications.</param>
    /// <param name="DynamicPublicationId">The publication (offer) of the Mobilithek taking the status publications.</param>
    /// <param name="URL">The publication URL; that of the Mobilithek by default.</param>
    /// <param name="ClientCertificate">The machine certificate the Mobilithek issued, as a PKCS #12 file; relative to the configuration file.</param>
    /// <param name="ClientCertificatePassword">The password of the machine certificate.</param>
    /// <param name="ClientCertificatePasswordEnvironmentVariable">The environment variable holding the password, so that it need not be in the file.</param>
    /// <param name="TrustedCertificates">The certificate chain of the Mobilithek as a PEM file, relative to the configuration file; else the operating system checks its certificate.</param>
    /// <param name="Compress">Whether to push gzip compressed; true by default.</param>
    public sealed record MobilithekConfiguration(UInt64   StaticPublicationId,
                                                 UInt64   DynamicPublicationId,
                                                 String?  URL                                          = null,
                                                 String?  ClientCertificate                            = null,
                                                 String?  ClientCertificatePassword                    = null,
                                                 String?  ClientCertificatePasswordEnvironmentVariable = null,
                                                 String?  TrustedCertificates                          = null,
                                                 Boolean? Compress                                     = null)
    {

        #region (static) TryParse(JSON, Path, out Configuration, out Error)

        /// <summary>
        /// The "mobilithek" block, or the one sentence that says what is wrong with it.
        /// </summary>
        public static Boolean TryParse(JObject                                           JSON,
                                       String                                            Path,
                                       [NotNullWhen(true)]  out MobilithekConfiguration?  Configuration,
                                       [NotNullWhen(false)] out String?                   Error)
        {

            Configuration = null;

            if (!TryReadPublicationId(JSON, "staticPublicationId",  Path, out var staticPublicationId,  out Error) ||
                !TryReadPublicationId(JSON, "dynamicPublicationId", Path, out var dynamicPublicationId, out Error))
            {
                return false;
            }

            if (!ConfigurationReader.TryReadString (JSON, "url",                                          Path, 500, out var url,                       out Error) ||
                !ConfigurationReader.TryReadString (JSON, "clientCertificate",                            Path, 500, out var clientCertificate,         out Error) ||
                !ConfigurationReader.TryReadString (JSON, "clientCertificatePassword",                    Path, 500, out var clientCertificatePassword, out Error) ||
                !ConfigurationReader.TryReadString (JSON, "clientCertificatePasswordEnvironmentVariable", Path, 100, out var passwordVariable,          out Error) ||
                !ConfigurationReader.TryReadString (JSON, "trustedCertificates",                          Path, 500, out var trustedCertificates,       out Error) ||
                !ConfigurationReader.TryReadBoolean(JSON, "compress",                                     Path,      out var compress,                  out Error))
            {
                return false;
            }

            if (url is not null && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")))
            {
                Error = $"'{Path}.url' must be an absolute https URL, e.g. \"https://mobilithek.info:8443/mobilithek/api/v1.0/publication/\".";
                return false;
            }

            Configuration = new MobilithekConfiguration(staticPublicationId!.Value,
                                                        dynamicPublicationId!.Value,
                                                        url,
                                                        clientCertificate,
                                                        clientCertificatePassword,
                                                        passwordVariable,
                                                        trustedCertificates,
                                                        compress);

            return true;

        }

        #endregion

        #region (private static) TryReadPublicationId(JSON, Name, Path, out Value, out Error)

        /// <summary>
        /// A mandatory publication identification of the Mobilithek: a positive number, also given as a text, as the
        /// Mobilithek shows it; it may be larger than a JSON number keeps exactly.
        /// </summary>
        private static Boolean TryReadPublicationId(JObject                           JSON,
                                                    String                            Name,
                                                    String                            Path,
                                                    [NotNullWhen(true)]  out UInt64?  Value,
                                                    [NotNullWhen(false)] out String?  Error)
        {

            Value = null;
            Error = null;

            var text = JSON[Name] switch {
                           JValue { Type: JTokenType.Integer } number  => number.ToString(Newtonsoft.Json.Formatting.None),
                           JValue { Type: JTokenType.String  } quoted  => quoted.Value<String>()?.Trim(),
                           _                                           => null
                       };

            if (text is null || !UInt64.TryParse(text, out var id) || id == 0)
            {
                Error = $"'{Path}.{Name}' must be the publication identification of the Mobilithek, a positive number.";
                return false;
            }

            Value = id;
            return true;

        }

        #endregion

        #region ToJSON()

        /// <summary>
        /// The block as it is written to the file.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject(
                           new JProperty("staticPublicationId",   StaticPublicationId. ToString()),
                           new JProperty("dynamicPublicationId",  DynamicPublicationId.ToString())
                       );

            if (URL                                          is not null)  json.Add("url",                                          URL);
            if (ClientCertificate                            is not null)  json.Add("clientCertificate",                            ClientCertificate);
            if (ClientCertificatePassword                    is not null)  json.Add("clientCertificatePassword",                    ClientCertificatePassword);
            if (ClientCertificatePasswordEnvironmentVariable is not null)  json.Add("clientCertificatePasswordEnvironmentVariable", ClientCertificatePasswordEnvironmentVariable);
            if (TrustedCertificates                          is not null)  json.Add("trustedCertificates",                          TrustedCertificates);
            if (Compress.HasValue)                                         json.Add("compress",                                     Compress.Value);

            return json;

        }

        #endregion

        #region (override) ToString()

        /// <summary>
        /// Without the password.
        /// </summary>
        public override String ToString()

            => $"Mobilithek publications {StaticPublicationId} (static) and {DynamicPublicationId} (dynamic)";

        #endregion

    }

}
