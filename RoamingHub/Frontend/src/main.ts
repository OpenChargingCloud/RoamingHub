import './styles/app.scss';

// FontAwesome: the CSS ends up in the extracted stylesheet, the referenced
// font files become hashed assets below /assets/.
import '@fortawesome/fontawesome-free/css/fontawesome.css';
import '@fortawesome/fontawesome-free/css/solid.css';

import { html } from '@node/html';
import { nodeMenu, startNode } from '@node/start';

import { configurationPage }  from './pages/configuration';
import { ocpiPage }           from './pages/ocpi';
import { peersPage }          from './pages/peers';
import { trafficPage }        from './pages/traffic';

// What a hub has pages for beside what every node has: the traffic between its
// peers, and the peers themselves. The sign-in, the log, the name servers, the
// time servers, the certificate store, the frame and following the log while
// somebody it is for is signed in are every node's - see WWCP_Node's start.ts.
//
// The traffic has a stream of its own and is not followed there: it is behind
// its own permission, it can run at a rate the event log never does, and
// nothing outside its page shows it. It opens and closes with that page.
startNode({

    name:  'RoamingHub',
    icon:  'fa-circle-nodes',

    // The traffic first, and not the configuration: a hub is configured a few
    // times in its life and read all day, and what somebody walks up to it
    // with is "are these two seeing each other?". "/" opens the first entry
    // somebody may open - the traffic for whoever may read it, and the
    // configuration for a viewer, who may see what this hub is but not what
    // went between its peers.
    //
    // Each entry carries what its page asks for. A role from the
    // configuration file may read the configuration and not the store, or
    // the name servers and not the peers; the three roles a hub brings read
    // all of them, so for them nothing is hidden.
    menu: [
        { path: '/traffic',  label: 'Traffic',  icon: 'fa-right-left',  permission: [ 'traffic:read' ] },
        nodeMenu.configuration([
            nodeMenu.dns,
            nodeMenu.nts,
            nodeMenu.certificates,
            { path: '/configuration/ocpi',           label: 'OCPI',   icon: 'fa-plug',       permission: [ 'configuration:read' ] },
            { path: '/configuration/ocpi/partners',  label: 'Peers',  icon: 'fa-handshake',  permission: [ 'peers:read' ]         }
        ]),
        nodeMenu.logs
    ],

    pages: {

        '/traffic':                       trafficPage,

        '/configuration':                 configurationPage,
        '/configuration/ocpi':            ocpiPage,
        // The path stays "partners" - it is what the JSON API calls them, and
        // what the other four components call the same page. Only the word on
        // the screen is "peers", because that is what they are to a hub.
        '/configuration/ocpi/partners':   peersPage

    },

    signIn: {
        line: 'Sign in to look after this hub: its peers, what went between them, and its log.'
    },

    // The certificate store in the node's words, but for what a hub keeps and
    // nothing here uses yet: a client root, and the TLS identity it will
    // present. A hub has no session to choose a certificate for, so no row
    // says what it was chosen for.
    certificates: {
        hints: {
            believes:     html`Trust anchors. Every switched-on root of a kind is believed at once. A TLS root vouches
                               for the time servers and the name servers it is kept for, beside the roots of the
                               machine this hub runs on. A client root is what a client connecting to this hub will
                               have to chain to - kept, and used by nothing here yet.`,
            presents:     html`A TLS identity, with its private key: what this hub will present in TLS - kept, and
                               used by nothing here yet.`,
            unencrypted:  html`can take the TLS identity kept there for this hub.`
        }
    }

});
