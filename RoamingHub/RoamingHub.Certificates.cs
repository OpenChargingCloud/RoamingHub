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

using cloud.charging.open.protocols.WWCP.Node.Certificates;

#endregion

namespace cloud.charging.open.RoamingHub
{

    public partial class RoamingHub
    {

        #region CertificatesJSON()

        /// <summary>
        /// Everything in this hub's certificate store, as the Certificates page
        /// reads it: grouped by kind, with what each kind is and what a
        /// certificate of it may be told it is for.
        /// </summary>
        /// <remarks>
        /// The store is the node's; this is how a hub shows it. Its kinds are
        /// TLS's four - see the constructor - and the page offers those and
        /// nothing else, because offering a kind the store refuses would be
        /// offering a refusal.
        /// </remarks>
        public JObject CertificatesJSON()
        {

            var kinds  = Certificates.Kinds;
            var byKind = new JObject();

            foreach (var kind in kinds)
                byKind.Add(kind.AsText(),
                           new JArray(Certificates.ByKind(kind).Select(entry => entry.ToJSON(WithDiagnostics: true))));

            return new JObject(

                       new JProperty("directory",    Certificates.Directory),

                       // What this hub believes: the roots a server it connects to
                       // may chain to, and the roots a client connecting to it has
                       // to chain to.
                       new JProperty("trustAnchors", new JArray(
                           kinds.Where(kind =>  kind.IsTrustAnchor()).Select(kind => kind.AsText())
                       )),

                       // What this hub presents, with its key.
                       new JProperty("credentials",  new JArray(
                           kinds.Where(kind => !kind.IsTrustAnchor() && !kind.MustNotCarryPrivateKey()).Select(kind => kind.AsText())
                       )),

                       // Neither believed nor presented, and never with a key: a
                       // server certificate, kept to recognise a server by. Shown
                       // among what the hub presents, it would read as something
                       // the hub presents.
                       new JProperty("recognised",   new JArray(
                           kinds.Where(kind => !kind.IsTrustAnchor() &&  kind.MustNotCarryPrivateKey()).Select(kind => kind.AsText())
                       )),

                       new JProperty("kinds",        new JObject(
                           kinds.Select(kind =>
                               new JProperty(kind.AsText(), new JObject(
                                   new JProperty("description",     kind.Describe()),
                                   new JProperty("trustAnchor",     kind.IsTrustAnchor()),
                                   new JProperty("needsPrivateKey", kind.NeedsPrivateKey()),
                                   // Whether one of the kind is told what it is for
                                   // in this store, and what it may be told - the
                                   // store's word and not the kind's: a TLS identity
                                   // is told the listeners a kind of node names, and
                                   // a hub names none, so it is told nothing. Asked
                                   // of the kind, the page would offer an identity
                                   // "dns" and "nts", which the store refuses, as
                                   // the vehicle, the station and the local
                                   // controller found.
                                   new JProperty("hasUsages",       Certificates.HasUsages(kind)),
                                   new JProperty("usages",          new JArray(Certificates.UsagesFor(kind)))
                               )))
                       )),

                       // What a TLS root or a server certificate may be told it is
                       // for - the services it vouches for - as it was said before
                       // every kind said its own above.
                       new JProperty("usages",       new JArray(Certificates.Usages)),

                       new JProperty("certificates", byKind),

                       // Said here because this is the page where somebody is
                       // looking at the consequences of it, rather than only in the
                       // log at a start.
                       new JProperty("keysAreUnencrypted", Certificates.Entries.Any(entry => entry.HasPrivateKey))

                   );

        }

        #endregion

    }

}
