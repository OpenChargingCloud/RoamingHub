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
    /// The sections of the configuration file that are this hub's own, rather
    /// than every node's: who it is when it speaks OCPI.
    /// </summary>
    /// <remarks>
    /// One file for everything a hub can be told in writing, as for every WWCP
    /// node - the node reads it once, takes "dns", "nts" and "certificates" from
    /// it and keeps the whole document, and this takes the rest from that same
    /// document rather than reading the file a second time.
    ///
    /// Every section is optional and so is every field inside it. A section
    /// that is absent is not a section set to nothing: it means the file has no
    /// opinion, and whatever the hub was handed at construction stands. A hub
    /// handed nothing either falls back to the default. So the order is:
    /// default, then what the constructor was given, then what the file says -
    /// each one only where it actually speaks.
    /// </remarks>
    /// <param name="OCPI">Who this hub is when it speaks OCPI, and which versions of it it speaks.</param>
    public sealed record RoamingHubConfiguration(OCPIConfiguration?  OCPI   = null)
    {

        #region Properties

        /// <summary>
        /// Whether the document says anything this hub reads itself.
        /// </summary>
        public Boolean IsEmpty
            => OCPI is null;

        #endregion


        #region (static) TryParse(JSON, out Configuration, out Error)

        /// <summary>
        /// Take this hub's own sections from the whole document, and pass the
        /// others over without a word: they are the node's, or a newer hub's.
        /// </summary>
        /// <param name="JSON">The whole configuration document.</param>
        /// <param name="Configuration">What the document says, when it could be read.</param>
        /// <param name="Error">Why it could not be, in a sentence that names the field.</param>
        public static Boolean TryParse(JObject                                           JSON,
                                       [NotNullWhen(true)]  out RoamingHubConfiguration?  Configuration,
                                       [NotNullWhen(false)] out String?                   Error)
        {

            Configuration  = null;
            Error          = null;

            #region OCPI

            OCPIConfiguration? ocpi = null;

            if (JSON[OCPIConfiguration.SectionName] is JToken ocpiToken && ocpiToken.Type != JTokenType.Null)
            {

                if (ocpiToken is not JObject ocpiJSON)
                {
                    Error = $"'{OCPIConfiguration.SectionName}' must be a JSON object.";
                    return false;
                }

                if (!OCPIConfiguration.TryParse(ocpiJSON, out ocpi, out Error))
                    return false;

            }

            #endregion

            Configuration = new RoamingHubConfiguration(ocpi);
            return true;

        }

        #endregion

        #region ToJSON()

        /// <summary>
        /// This hub's own sections, as they are written into the file.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject();

            if (OCPI is not null)
                json.Add(OCPIConfiguration.SectionName, OCPI.ToJSON());

            return json;

        }

        #endregion

        #region (override) ToString()

        public override String ToString()

            => IsEmpty
                   ? "nothing configured"
                   : OCPI!.ToString();

        #endregion

    }

}
