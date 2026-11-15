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
 * what a hub adds to it - its peers, what went between them, and the roaming
 * networks of its points of interest.
 *
 * The traffic is a resource of its own and deliberately not part of the
 * configuration: the configuration is what this hub is, and the traffic is
 * what its peers did through it. The roaming networks are read by everybody,
 * as the POI API is; editing them is making a new one. Their runtime status
 * is set over the WebSocket of a history, by the feed of a charging station
 * operator, and nowhere on these pages: "poi-status:edit" is here because
 * /api/v1/auth/me spells it out for whoever holds it.
 */
export type Resource = NodeResource | 'peers' | 'traffic' | 'poi' | 'poi-status';

/** What somebody signed in to this hub may do: an operation on a resource, written "dns:edit". */
export type Permission = `${Resource}:${Operation}`;

/** Who is signed in to the web interface. */
export type Me = NodeMe<Resource>;

/** How the hub is doing right now: every node's, and who it is in OCPI. */
export interface Status extends NodeStatus {
    partyId:  string;
}

/**
 * What the hub is made of: every node's sections - what it was built from
 * among them - and its own. Only the shape the Configuration page relies on is
 * named; the fields of each section are rendered from whatever the hub sends,
 * so that a field added on the server needs no change here.
 */
export interface Configuration extends NodeConfiguration {
    RoamingHub:  Record<string, unknown>;
    ocpi:        Record<string, unknown>;
    traffic:     Record<string, unknown>;
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
        logFile:              boolean;
    };
    counts: {
        partners:    number;
        registered:  number;
        calls:       number;
    };
    directory:   string;
    /** Where the calls are appended as well, or null where they are not kept beyond this run. */
    appendedTo:  TrafficFile | null;
    file:        string;
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

/**
 * Where the hub appends every call as well, where it was asked to keep them
 * beyond this run. Null where it was not, which is how it starts.
 */
export interface TrafficFile {
    directory:  string;
    /** The file being appended to, or null before the first call of this run. */
    current:    string | null;
    /** How many calls have been written since the hub started. */
    written:    number;
}

/** What a page of the traffic brings back. */
export interface CallPage {
    calls:       Call[];
    /** The newest id of the whole log, whatever this page was filtered by. */
    lastId:      number;
    count:       number;
    capacity:    number;
    /** Whether the bodies are being kept at all. */
    payloads:    boolean;
    /** Where the calls are appended as well, or null where the ring is all there is. */
    appendedTo:  TrafficFile | null;
    /** Every party seen at either end of a call, for the filter. */
    peers:       string[];
}


/**
 * A public key of a party: the Base64 of its COSE key, or the key as POI 3
 * writes it, {"@id"?, "key", "notBefore"?, "notAfter"?}.
 */
export type PublicKey = string | { '@id'?: string; key: string; notBefore?: string; notAfter?: string };

/** A roaming network to be made: what names it. Its operator is this hub. */
export interface RoamingNetworkSpec {
    '@id':         string;
    name?:         Record<string, string>;
    description?:  Record<string, string>;
}

/** What the hub says of a roaming network it made: at revision 0, with its genesis commit, operated by the hub. */
export interface RoamingNetworkMade {
    message:         string;
    roamingNetwork:  { '@id': string; name?: Record<string, string>; description?: Record<string, string>; operator?: { '@id': string } };
    revision:        number;
    commitId:        string;
}

/** The parties of a roaming network the hub adds: the segment the POI API lists them at, which is the last one of where a party is added. */
export type PartySegment = 'CSOs' | 'EMPs' | 'GOs' | 'POs' | 'CSMs' | 'EMMs' | 'TSMs' | 'CABs';

/** A party to be added: what names it, and its public keys. A grid operator or a conformity assessment body not taking part comes without keys. */
export interface PartySpec {
    '@id':          string;
    name?:          Record<string, string>;
    signingKeys?:   PublicKey[];
    identityKeys?:  PublicKey[];
}

/** What the hub says of a party it added, with a change set it signed: the commit. */
export interface PartyAdded {
    message:       string;
    /** Committed: a grid operator without keys. */
    revision?:     number;
    commitId?:     string;
    /** Waiting for the signatures of the identity keys of the party, as the proof that it holds them. */
    status?:       'pending';
    changeSetId?:  string;
    missing?:      string[];
}


/** A public key as POI writes it embedded: its identification is its thumbprint. */
export interface NetworkKey {
    '@id':       string;
    key:         string;
    notBefore?:  string;
    notAfter?:   string;
    /** Who holds it, where several hold the keys of a party. */
    holder?:     string;
}

/**
 * The roaming network operator this hub is, as every roaming network it operates embeds it: its identity keys, and
 * its signing keys each in the certificate an identity key of the same holder signed for it.
 */
export interface NetworkOperator {
    '@id':                    string;
    name?:                    Record<string, string>;
    identityKeys?:            NetworkKey[];
    signingKeyCertificates?:  { signingKey: NetworkKey; /** The identity key that signed it. */ keyId?: string }[];
}

/**
 * What became of a change of the roaming network operator this hub is, in every roaming network it serves: those
 * that name it as it is now, those that refused it and why, and those where it waits for the other holders of the
 * keys of their operator, and for what.
 */
export interface NetworkOperatorUpdate {
    message:   string;
    changed:   string[];
    refused:   Record<string, string>;
    waiting?:  Record<string, string>;
    operator:  NetworkOperator;
}

/** A roaming network of another server this hub follows: where, whether it is connected, and how far its copy is. */
export interface FollowedNetwork {
    '@id':        string;
    url:          string;
    connected:    boolean;
    trustAnchor:  string;
    revision?:    number;
    commit?:      string;
    /** What went wrong last. */
    error?:       string;
    /** What the hub says of following it, when it was just asked to. */
    message?:     string;
}

/** A change of who holds the keys of the operator of a roaming network, waiting for the other holders. */
export interface ShareholderChange {
    kind:          'admit' | 'dismiss' | 'addKey' | 'dropKey' | 'restate';
    /** Who it is about; empty where the keys are stated anew, by all of them. */
    holder:        string;
    keyStatement:  { signatures?: unknown[] };
    url?:          string;
    /** Its change set, once enough holders signed its key statement. */
    changeSet?:    string;
}

/** Who holds the keys of the operator of a roaming network, and how many of them decide. */
export interface Shareholders {
    roamingNetworkOperator:  string;
    minConsensus:            number;
    holders:                 { holder: string; identityKeys: string[]; signingKeys: string[] }[];
    /** What waits; missing for a roaming network followed whose server could not be asked. */
    changes?:                ShareholderChange[];
    /** Whether the roaming network is a copy here, of one another server makes. */
    followed:                boolean;
    /** The holder this hub is there, if it is one. */
    thisHub?:                string;
}

/** What the hub says of something it did, or began, as a holder of the keys of an operator. */
export interface ShareholderAnswer {
    status:   string;
    message:  string;
}

/** A change set of a roaming network waiting for signatures, and for which. */
export interface PendingChangeSet {
    status?:    string;
    changeSet:  {
        '@id':          string;
        description?:   Record<string, string>;
        signatures?:    unknown[];
        changes?:       { kind?: string; entityType?: string; entityId?: string; propertyName?: string }[];
    };
    missing?:   string[];
}

/** How long another hub is given to answer what this one asks it. */
const otherHub = [ 15 ];


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
    trafficPeers: () => request<{ peers: string[] }>('GET', '/traffic/peers'),

    /** The points of interest: a new roaming network operated by this hub, a new party of one, and a new manufacturer in its registries. */
    poi: {

        create:    (spec: RoamingNetworkSpec) => request<RoamingNetworkMade>('POST', '/poi/RNs', spec),

        addParty:  (roamingNetworkId: string, segment: PartySegment, spec: PartySpec) =>
                       request<PartyAdded>('POST', `/poi/RNs/${encodeURIComponent(roamingNetworkId)}/${segment}`, spec),

        /** The roaming network operator this hub is: its keys replaced, itself renamed, the holder of its keys named. */
        operator: {

            get:        ()                                => request<NetworkOperator>('GET', '/poi/roamingNetworkOperator'),

            /**
             * A key replaced by a new one, in every roaming network that names it. An identity key in two changes,
             * each waiting for the other holders where several decide: asked again, it goes on where it was.
             */
            rotate:     (kind: 'signingKey' | 'identityKey') => request<NetworkOperatorUpdate>('POST', `/poi/roamingNetworkOperator/${kind}/rotate`, {},
                                                                                              afterAsking([ 60 ])),

            rename:     (name: string)                    => request<NetworkOperatorUpdate>('PUT', '/poi/roamingNetworkOperator/name',   { name }),

            nameHolder: (holder: string)                  => request<NetworkOperatorUpdate>('PUT', '/poi/roamingNetworkOperator/holder', { holder })

        },

        /**
         * What waits for somebody at this hub as a holder of the keys of an operator, by roaming network, as the hub
         * found it when it looked last: a roaming network nothing waits in is not named.
         */
        waiting:  () => request<{ waiting: Record<string, string[]> }>('GET', '/poi/waiting').then(answer => answer.waiting ?? {}),

        /** The roaming networks of other servers this hub follows. */
        followed: {

            get:       ()                                   => request<{ followed: FollowedNetwork[] }>('GET', '/poi/followed'),

            follow:    (trustAnchor: string, url?: string)  => request<FollowedNetwork>('POST', '/poi/followed', { trustAnchor, url },
                                                                                        afterAsking([ 60 ])),

            unfollow:  (roamingNetworkId: string)           => request<{ '@id': string; message: string }>(
                                                                   'DELETE', `/poi/followed/${encodeURIComponent(roamingNetworkId)}`),

            /** Sign the head of a roaming network followed, as a holder of the keys of its operator. */
            sign:      (roamingNetworkId: string)           => request<{ message: string; commit?: string }>(
                                                                   'POST', `/poi/followed/${encodeURIComponent(roamingNetworkId)}/signatures`, {},
                                                                   afterAsking(otherHub))

        },

        /** Who holds the keys of the operator of a roaming network, and what this hub does as one of them. */
        shareholders: {

            get:      (roamingNetworkId: string) => request<Shareholders>('GET', `/poi/shareholders/${encodeURIComponent(roamingNetworkId)}`, undefined,
                                                                          afterAsking(otherHub)),

            /** Ask to hold the keys of the operator of a roaming network followed, or sign for it once that waits. */
            join:     (roamingNetworkId: string, url?: string) =>
                          request<ShareholderAnswer>('POST', `/poi/shareholders/${encodeURIComponent(roamingNetworkId)}/join`, url ? { url } : {},
                                                     afterAsking(otherHub)),

            /** Admit who asked, dismiss a holder, or agree to one replacing its identity key. */
            change:   (roamingNetworkId: string, what: 'admit' | 'dismiss' | 'rekey', holder: string) =>
                          request<ShareholderAnswer>('POST', `/poi/shareholders/${encodeURIComponent(roamingNetworkId)}/${what}/${encodeURIComponent(holder)}`, {},
                                                     afterAsking(otherHub)),

            /** State the keys of the operator anew, together with the other holders. */
            restate:  (roamingNetworkId: string) =>
                          request<ShareholderAnswer>('POST', `/poi/shareholders/${encodeURIComponent(roamingNetworkId)}/restate`, {},
                                                     afterAsking(otherHub)),

            pending:  (roamingNetworkId: string) =>
                          request<{ pendingChangeSets: PendingChangeSet[] }>('GET', `/poi/shareholders/${encodeURIComponent(roamingNetworkId)}/pending`, undefined,
                                                                             afterAsking(otherHub)),

            /** Sign a change set waiting for signatures as a holder of the keys of the operator, or withdraw it. */
            decide:   (roamingNetworkId: string, changeSetId: string, approve: boolean) =>
                          request<ShareholderAnswer>('POST', `/poi/shareholders/${encodeURIComponent(roamingNetworkId)}/pending/${encodeURIComponent(changeSetId)}/${approve ? 'approve' : 'refuse'}`, {},
                                                     afterAsking(otherHub))

        }

    }

};
