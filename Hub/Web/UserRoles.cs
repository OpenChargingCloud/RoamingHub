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

using org.GraphDefined.Vanaheimr.Hermod.HTTP;

#endregion

namespace cloud.charging.open.RoamingHub.Web
{

    /// <summary>
    /// What somebody signed in to this Hub is allowed to do.
    /// </summary>
    /// <remarks>
    /// Flags rather than a list, because a permission is asked about one at a
    /// time and answered by a single test - and because the set a role grants
    /// is then a constant instead of a collection to be built and searched.
    ///
    /// Only what this hub actually enforces is named here. A permission with
    /// nothing behind it is a promise made to whoever reads the login file and
    /// not kept - so these arrived one at a time, as the things they guard did:
    /// which peers may call this hub is its own permission rather than
    /// something that grew quietly inside one that already existed, and so is
    /// the record of what went between them.
    /// </remarks>
    [Flags]
    public enum Permissions : UInt32
    {

        /// <summary>
        /// Nothing at all. What an unknown role would grant.
        /// </summary>
        None                    = 0,

        /// <summary>
        /// See how this hub is configured, and which peers are on it.
        /// </summary>
        ReadConfiguration       = 1,

        /// <summary>
        /// Change how this Hub reaches the network: its name resolution and
        /// where it reads the time.
        /// </summary>
        /// <remarks>
        /// Reversible, and it complains: a wrong name server makes the Hub say
        /// so, and the next change puts it right. That is what separates it
        /// from the settings describing who is around this Hub - the roaming
        /// partners - where being wrong is quiet and somebody else notices
        /// first.
        /// </remarks>
        ChangeNetworkSettings   = 2,

        /// <summary>
        /// Make this Hub ask a name server or a time server something, to
        /// find out whether it can.
        /// </summary>
        /// <remarks>
        /// Its own permission and not part of reading: a diagnostic sends
        /// traffic from this Hub to a host somebody named, which is more than
        /// it sounds like to hand to everybody who may look at a page.
        /// </remarks>
        RunDiagnostics          = 4,

        /// <summary>
        /// Read what went between the peers: which of them called which, on
        /// what, and what came back.
        /// </summary>
        /// <remarks>
        /// Its own permission rather than part of
        /// <see cref="ReadConfiguration"/>, because it is a different kind of
        /// reading. The configuration says what this hub is; the traffic says
        /// who its peers are talking to and when, which is their business
        /// passing through here and not this hub's own.
        /// </remarks>
        ReadTraffic             = 8,

        /// <summary>
        /// Add and remove peers, hand out the access token a peer signs in
        /// with, and start the OCPI peering with one.
        /// </summary>
        /// <remarks>
        /// The highest of these. Everything else here is about what this hub
        /// does; this is about whom it believes. A hub is a room its peers are
        /// let into, and somebody who can add one hands a foreign system the
        /// right to be in that room with all the others - and no other
        /// permission here reaches that far.
        /// </remarks>
        ManageRoamingPartners   = 16

    }


    /// <summary>
    /// A role somebody signs in as: a name, and the permissions it carries.
    /// </summary>
    /// <remarks>
    /// A closed set: a role this Hub has never heard of is a role it cannot
    /// enforce. So an unrecognised name is refused when the login file is
    /// read, rather than quietly granting nothing - or, far worse, being taken
    /// for a known one because it looks similar.
    ///
    /// The names overlap with those of the charging station, the local
    /// controller, the CSMS and the EMSP on purpose - all of them have
    /// "systemadmin" and "viewer" - so that one HTTPExt API handed to several
    /// of these programs makes one sign-in open all of them. What does not
    /// overlap is the operator role: a CPO's operator is not this one, and
    /// "hub" here is what "cpo" is there.
    /// </remarks>
    /// <param name="Name">How the role is written, and the identification of the group whose members hold it.</param>
    /// <param name="Permissions">What it grants.</param>
    public sealed record UserRole(String       Name,
                                  Permissions  Permissions)
    {

        #region Properties

        /// <summary>
        /// The user group in the HTTPExt API whose members hold this role.
        /// </summary>
        public UserGroup_Id  GroupId
            => UserGroup_Id.Parse(Name);

        #endregion


        #region Data

        /// <summary>
        /// May look at this Hub, and do nothing to it.
        /// </summary>
        public static readonly UserRole  Viewer       = new ("viewer",
                                                             Permissions.ReadConfiguration);

        /// <summary>
        /// The operator of this hub: may point it at other name and time
        /// servers, may test them, and may read what went between the peers.
        /// </summary>
        /// <remarks>
        /// Day-to-day operation. A hub is run by whoever is asked why a CPO
        /// and an EMSP are not seeing each other, long before it is run by
        /// whoever installed it, and the answer to that question is in the
        /// traffic.
        /// </remarks>
        public static readonly UserRole  Hub          = new ("hub",
                                                             Permissions.ReadConfiguration      |
                                                             Permissions.ChangeNetworkSettings  |
                                                             Permissions.RunDiagnostics         |
                                                             Permissions.ReadTraffic);

        /// <summary>
        /// Everything this Hub can be told, by whoever is trusted with all of
        /// it at once.
        /// </summary>
        /// <remarks>
        /// What separates it from the operator is the peers: whoever runs the
        /// hub watches the traffic all day, and whoever decides which CPO and
        /// which EMSP this hub lets in does it a few times in the life of the
        /// box. A peer that is let in can ask this hub about every other peer
        /// on it, which is what a hub is for and why it is the highest
        /// permission here.
        /// </remarks>
        public static readonly UserRole  SystemAdmin  = new ("systemadmin",
                                                             Permissions.ReadConfiguration      |
                                                             Permissions.ChangeNetworkSettings  |
                                                             Permissions.RunDiagnostics         |
                                                             Permissions.ReadTraffic            |
                                                             Permissions.ManageRoamingPartners);

        /// <summary>
        /// Every role this Hub knows.
        /// </summary>
        public static readonly IReadOnlyList<UserRole>  All = [ Viewer, Hub, SystemAdmin ];

        #endregion


        #region (static) TryParse(Text, out Role, out Error)

        /// <summary>
        /// A role by the name the login file writes it under, in any case.
        /// </summary>
        public static Boolean TryParse(String?                           Text,
                                       [NotNullWhen(true)]  out UserRole?  Role,
                                       [NotNullWhen(false)] out String?    Error)
        {

            Role   = All.FirstOrDefault(role => String.Equals(role.Name, Text?.Trim(), StringComparison.OrdinalIgnoreCase));

            Error  = Role is null
                         ? $"\"{Text}\" is not a role this Hub knows. Known roles: {String.Join(", ", All.Select(role => role.Name))}."
                         : null;

            return Role is not null;

        }

        #endregion

        #region (override) ToString()

        public override String ToString()
            => Name;

        #endregion

    }


    /// <summary>
    /// What a set of roles adds up to.
    /// </summary>
    public static class UserRoleExtensions
    {

        #region PermissionsOf(this Roles)

        /// <summary>
        /// Everything the given roles grant together.
        /// </summary>
        public static Permissions PermissionsOf(this IEnumerable<UserRole> Roles)
        {

            var permissions = Permissions.None;

            foreach (var role in Roles)
                permissions |= role.Permissions;

            return permissions;

        }

        #endregion

        #region Names(this Permissions)

        /// <summary>
        /// The permissions as the web interface reads them, so that a page can
        /// grey out what this browser may not do instead of finding out by
        /// being refused.
        /// </summary>
        /// <remarks>
        /// What the browser is told is a copy of what the Hub enforces, and
        /// not the enforcement: every request is checked again on arrival. A
        /// greyed-out button is a courtesy, not a lock.
        /// </remarks>
        public static IEnumerable<String> Names(this Permissions Permissions)

            => Enum.GetValues<Permissions>().
                    Where (permission => permission != Web.Permissions.None && Permissions.HasFlag(permission)).
                    Select(permission => Char.ToLowerInvariant(permission.ToString()[0]) + permission.ToString()[1..]);

        #endregion

    }

}
