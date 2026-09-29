import { afterAsking,
         apiURL,
         nodeAPI,
         request,
         type Certificate        as NodeCertificate,
         type CertificateImport  as NodeCertificateImport,
         type CertificateStore   as NodeCertificateStore,
         type NodeConfiguration,
         type NodeMe,
         type NodeResource,
         type NodeStatus,
         type Operation }  from '@node/api/client';


// What every node answers - the log, name resolution, the time, the store,
// who is signed in - and how it is asked are WWCP_Node's, and every page here
// reads them from this module as before. What follows is what a hub adds: its
// resources, what its status and its configuration say beyond every node's,
// the kinds its store keeps, and its peers and the traffic between them, with
// their routes.
export * from '@node/api/client';


/**
 * What a role may be allowed to touch on this hub: what every node has, and
 * what a hub adds to it - its peers, and what went between them.
 *
 * The traffic is a resource of its own and deliberately not part of the
 * configuration: the configuration is what this hub is, and the traffic is
 * what its peers did through it.
 */
export type Resource = NodeResource | 'peers' | 'traffic';

/** What somebody signed in to this hub may do: an operation on a resource, written "dns:edit". */
export type Permission = `${Resource}:${Operation}`;

/** Who is signed in to the web interface. */
export type Me = NodeMe<Resource>;

/** How the hub is doing right now: every node's, and who it is in OCPI. */
export interface Status extends NodeStatus {
    partyId:  string;
}

/**
 * What the hub is made of: every node's sections, and its own. Only the shape
 * the Configuration page relies on is named; the rest is rendered from
 * whatever the hub sends, so that a new section on the server needs no change
 * here.
 */
export interface Configuration extends NodeConfiguration {
    RoamingHub:  Record<string, unknown>;
    ocpi:        Record<string, unknown>;
    traffic:     Record<string, unknown>;
    assemblies:  Record<string, unknown>[];
}


// Certificates

/**
 * What a certificate in this hub's store is for: the four kinds of TLS, and
 * none of the seven of ISO 15118, which are a vehicle's and a charging
 * station's. A root is believed, an identity is presented, and a server
 * certificate is neither, but kept to recognise a server by its fingerprint.
 */
export type CertificateKind = 'tlsRoot' | 'clientRoot' | 'tlsServer' | 'tlsIdentity';

/** One certificate in the store. */
export type Certificate = NodeCertificate<CertificateKind>;

/** What an import sends. */
export type CertificateImport = NodeCertificateImport<CertificateKind>;

/** The whole store, grouped the way it is shown. */
export type CertificateStore = NodeCertificateStore<CertificateKind>;


// OCPI

/** Where one OCPI version is: its endpoints as absolute URLs a partner is told. */
export interface OCPIVersionEndpoints {
    version:      string;
    details:      string;
    credentials:  string;
    modules:      Record<string, string>;
}

/** The OCPI side of this hub: who it is, where it is, and how many peers are on it. */
export interface OCPIConfiguration {
    party: {
        countryCode:  string;
        partyId:      string;
        id:           string;
        role:         string;
        name:         string;
        website:      string | null;
    };
    endpoints: {
        base:         string;
        versions:     string;
        externalURL:  string | null;
        byVersion:    OCPIVersionEndpoints[];
    };
    versions:       string[];
    knownVersions:  string[];
    settings: {
        locationsAsOpenData:  boolean;
        tariffsAsOpenData:    boolean;
        allowDowngrades:      boolean;
        logRequests:          boolean;
        logPayloads:          boolean;
    };
    counts: {
        partners:    number;
        registered:  number;
        calls:       number;
    };
    directory:  string;
    file:       string;
}

/**
 * Whether a peer can be reached, as OCPI writes it.
 *
 * PLANNED is a peer that is not registered yet - the contracts are not
 * established. SUSPENDED is a decision somebody made and it sticks; the
 * other three follow from what the hub observes.
 */
export type ConnectionStatus = 'CONNECTED' | 'OFFLINE' | 'PLANNED' | 'SUSPENDED';

/** One peer of this hub, as the event stream announces a change to it. */
export interface PeerPresence {
    partyId:      string;
    countryCode:  string;
    party:        string;
    role:         string;
    status:       ConnectionStatus;
    registered:   boolean;
    lastUpdated:  string;
    lastSeen:     string | null;
}

/**
 * One peer of this hub - a CPO, an EMSP, or another hub. The tokens are
 * present only for whoever may manage partners; everybody else sees that
 * there is one.
 */
export interface Partner {
    version:           string;
    id:                string;
    countryCode:       string;
    partyId:           string;
    role:              string;
    name:              string;
    website:           string | null;
    status:            string;
    ourToken:          string | null;
    hasOurToken:       boolean;
    ourTokenStatus:    string | null;
    theirToken:        string | null;
    hasTheirToken:     boolean;
    theirVersionsURL:  string | null;
    remoteStatus:      string | null;
    selectedVersion:   string | null;
    /** Whether this hub holds a token of theirs and a place to send it. */
    canRegister:       boolean;
    /**
     * Whether this peer can be reached right now - the OCPI HubClientInfo
     * status, and a different kind of fact from `registered` below.
     *
     * Registration is what was agreed, once; this is what is happening. A
     * peer can be perfectly registered and OFFLINE, which is exactly the
     * situation somebody walks up to a hub to ask about.
     */
    connection:        ConnectionStatus;
    /** When this hub last heard from it, or null when it never has. */
    lastSeen:          string | null;
    /** Since when it has been in the status above. */
    connectionSince:   string | null;
    /** Whether the peering is complete in both directions. */
    registered:        boolean;
    created:           string;
    lastUpdated:       string;
}

/** Every peer, and what the add form may choose from. */
export interface Partners {
    partners:        Partner[];
    versions:        string[];
    roles:           string[];
    ourVersionsURL:  string;
}

/** What it takes to add a peer. */
export interface PartnerSpec {
    version:       string;
    countryCode:   string;
    partyId:       string;
    role:          string;
    name:          string;
    website?:      string;
    /** Empty means "make one up"; it comes back in the answer. */
    ourToken?:     string;
    /** Both or neither: with both, this hub can start the peering itself. */
    theirToken?:   string;
    versionsURL?:  string;
}


// The traffic, which is the point of a hub

/** Which way a call went: a peer called this hub, or this hub called a peer. */
export type CallDirection = 'in' | 'out';

/**
 * One OCPI call that touched this hub.
 *
 * Both statuses, and not one: OCPI answers a refusal with `200` and a status
 * code inside the envelope as readily as with a `4xx`, so a line that carried
 * only the HTTP status would call half the failures a success.
 *
 * The bodies are absent unless `ocpi.logging.payloads` says otherwise - what
 * travels through a hub is a location somebody operates, a session somebody is
 * having and a card somebody is holding, and none of it is the hub's to keep.
 */
export interface Call {
    /** A number that only ever grows, so a reader can catch up with `after`. */
    id:              number;
    timestamp:       string;
    direction:       CallDirection;
    /** The peer at the other end, as far as this hub could tell. */
    peer:            string | null;
    /** The two parties out of the OCPI `from` and `to` headers. */
    from:            string | null;
    to:              string | null;
    version:         string | null;
    module:          string | null;
    method:          string;
    path:            string;
    httpStatusCode:  number;
    /** The status inside the OCPI envelope, when there was one. */
    ocpiStatusCode:  number | null;
    statusMessage:   string | null;
    /** Whether it worked, by both of the two statuses above. */
    ok:              boolean;
    error:           string | null;
    durationMs:      number;
    requestSize:     number | null;
    responseSize:    number | null;
    requestId:       string | null;
    correlationId:   string | null;
    remoteSocket:    string | null;
    requestBody:     string | null;
    responseBody:    string | null;
}

/** What a page of the traffic brings back. */
export interface CallPage {
    calls:     Call[];
    /** The newest id of the whole log, whatever this page was filtered by. */
    lastId:    number;
    count:     number;
    capacity:  number;
    /** Whether the bodies are being kept at all. */
    payloads:  boolean;
    /** Every party seen at either end of a call, for the filter. */
    peers:     string[];
}


/** The routes every node has, typed with what a hub says its own of them are. */
const node = nodeAPI<{ me: Me; status: Status; configuration: Configuration; kind: CertificateKind; store: CertificateStore }>();

export const api = {

    ...node,

    /** The event stream of the traffic - a stream of its own, behind a permission of its own. */
    trafficEventsURL:  apiURL('/traffic/events'),

    /** The OCPI side: who this hub is, and the peers that are on it. */
    ocpi: {

        configuration: () => request<OCPIConfiguration>('GET', '/configuration/ocpi'),

        partners: {

            get:       ()                     => request<Partners>('GET', '/ocpi/partners'),

            /**
             * Add a partner. The answer carries the token this hub made up
             * for them - the one thing that has to be handed over by hand.
             */
            add:       (spec: PartnerSpec)    => request<{ message: string; id: string; version: string; ourToken: string; partners: Partners }>(
                                                     'POST', '/ocpi/partners', spec),

            /**
             * Start the peering from here: fetch their versions, POST our credentials.
             *
             * A question the hub puts to somebody else, and waited for as one:
             * the peer's versions, the details of one of them and the
             * credentials are three requests, one after another, and the hub
             * gives each the two minutes its HTTP client allows a request.
             * Waiting less would have the page say the hub did not answer while
             * it is still patiently waiting for the peer.
             */
            register:  (version: string, id: string) =>
                           request<{ ok: boolean; message: string; partners: Partners }>(
                               'POST', `/ocpi/partners/${encodeURIComponent(version)}/${encodeURIComponent(id)}/register`, {},
                               afterAsking([ 120, 120, 120 ])),

            remove:    (version: string, id: string) =>
                           request<Partners>('DELETE', `/ocpi/partners/${encodeURIComponent(version)}/${encodeURIComponent(id)}`),

            /**
             * Stop talking to a peer, or start again.
             *
             * Not a deletion, and the OCPI specification is explicit that
             * there is none here: a peer that simply vanished would leave
             * every other peer holding a party the hub has forgotten, with
             * no way to tell that from one that is merely quiet. So it is
             * switched off, and everybody is told.
             */
            suspend:   (partyId: string) =>
                           request<{ ok: boolean; message: string; partners: Partners }>(
                               'POST', `/ocpi/peers/${encodeURIComponent(partyId)}/suspend`, {}),

            resume:    (partyId: string) =>
                           request<{ ok: boolean; message: string; partners: Partners }>(
                               'POST', `/ocpi/peers/${encodeURIComponent(partyId)}/resume`, {})

        },


    },

    /**
     * What went between the peers, oldest of what comes back first.
     *
     * Its own permission - traffic:read - and its own stream, beside the event
     * log rather than inside it: the log is what this hub did, and this is
     * what its peers did through it.
     *
     * @param limit  at most this many calls
     * @param after  only what is newer than this id
     * @param peer   only calls with this party at either end
     */
    traffic: (limit?: number, after?: number, peer?: string) => {

        const query = new URLSearchParams();

        if (limit !== undefined)  query.set('limit', String(limit));
        if (after !== undefined)  query.set('after', String(after));
        if (peer)                 query.set('peer',  peer);

        const suffix = query.size > 0 ? `?${query}` : '';

        return request<CallPage>('GET', `/traffic${suffix}`);

    },

    /** Every party that has been at either end of a call. */
    trafficPeers: () => request<{ peers: string[] }>('GET', '/traffic/peers')

};
