import { config } from '../config';


// What the JSON API answers. Everything below /api/v1 except the sign-in needs
// the session cookie, which the browser sends by itself because every request
// here is same-origin.

/** How loudly a log entry asks to be read. */
export type LogLevel = 'debug' | 'info' | 'notice' | 'warning' | 'error' | 'critical';

/** The levels in the order the hub defines them, quietest first. */
export const logLevels: LogLevel[] = ['debug', 'info', 'notice', 'warning', 'error', 'critical'];

/** One thing that happened inside the hub. */
export interface LogEntry {
    /** A number that only ever grows, so the page can tell what it has seen. */
    id:         number;
    timestamp:  string;
    level:      LogLevel;
    /** What it is about: "ocpi", "partner", "http", ... - without the level. */
    tags:       string[];
    message:    string;
    /** Whatever else belongs to it, when there is more than one line to say. */
    data?:      unknown;
}

/** What a page of the log brings back. */
export interface LogPage {
    /** The newest id of the whole log, whatever this page was filtered by. */
    lastId:    number;
    capacity:  number;
    tags:      string[];
    entries:   LogEntry[];
}

/**
 * What somebody signed in to this hub may do.
 *
 * A copy of what the hub enforces, not the enforcement: it is here so a page
 * can grey out what this person may not do instead of offering it and letting
 * them find out by being refused. Every request is checked again on arrival,
 * so editing this list in a browser buys a button that answers 403.
 *
 * Reading the traffic is its own permission and deliberately not part of
 * reading the configuration: the configuration is what this hub is, and the
 * traffic is what its peers did through it.
 */
export type Permission = 'readConfiguration'
                       | 'changeNetworkSettings'
                       | 'runDiagnostics'
                       | 'readTraffic'
                       | 'manageRoamingPartners';

/** Who is signed in to the web interface. */
export interface Me {
    username:     string;
    roles:        string[];
    permissions:  Permission[];
}

/** How the hub is doing right now. */
export interface Status {
    service:    string;
    version:    string;
    partyId:    string;
    hermod:     string | null;
    timestamp:  string;
    startedAt:  string;
    uptime:     string;
    sessions:   number;
    log:        { entries: number; capacity: number; lastId: number; tags: string[] };
}

/**
 * What the hub is made of. Only the shape the Configuration page relies on is
 * named; the rest is rendered from whatever the hub sends, so that a new
 * section on the server needs no change here.
 */
export interface Configuration {
    RoamingHub:  Record<string, unknown>;
    http:        Record<string, unknown>;
    web:         Record<string, unknown>;
    log:         Record<string, unknown>;
    time:        Record<string, unknown>;
    ocpi:        Record<string, unknown>;
    traffic:     Record<string, unknown>;
    assemblies:  Record<string, unknown>[];
}


/** One name server this hub asks. */
export interface DNSServer {
    /** An IP address or a host name. */
    address:              string;
    port:                 number;
    transport:            string;
    queryTimeoutSeconds:  number | null;
}

/** What may be changed about the name resolution while the hub runs. */
export interface DNSSettings {
    queryTimeoutSeconds:  number;
    /** null leaves it to the server's own default. */
    recursionDesired:     boolean | null;
    useCache:             boolean;
    dnssecOK:             boolean;
    followCNAMEs:         boolean;
    maxCNAMEFollows:      number;
    maxRetries:           number;
}

/** How this hub resolves names. */
export interface DNSConfiguration {
    enabled:    boolean;
    servers:    DNSServer[];
    settings:   DNSSettings;
    /** What was decided when the client was made, and is not on offer. */
    fixed:      Record<string, unknown>;
    limits: {
        maxServers:       number;
        maxQueryTimeout:  number;
        transports:       string[];
        recordTypes:      string[];
    };
    file:       string;
}

/** What a PUT to the DNS configuration may carry; everything is optional. */
export interface DNSUpdate {
    enabled?:              boolean;
    servers?:              DNSServer[];
    queryTimeoutSeconds?:  number;
    recursionDesired?:     boolean | null;
    useCache?:             boolean;
    dnssecOK?:             boolean;
    followCNAMEs?:         boolean;
    maxCNAMEFollows?:      number;
    maxRetries?:           number;
}

/** One resource record a test query brought back. */
export interface DNSRecord {
    name:        string;
    type:        string;
    timeToLive:  number;
    value:       string;
}

/** What a test query brought back. */
export interface DNSQueryResult {
    name:           string;
    recordTypes:    string[];
    ok:             boolean;
    error?:         string;
    responseCode?:  string;
    server?:        string;
    runtime_ms?:    number;
    authoritative?: boolean;
    truncated?:     boolean;
    dnssec?:        string | null;
    timedOut?:      boolean;
    answers:        DNSRecord[];
    more?:          number;
}


/** What may be changed about the time client while the hub runs. */
export interface NTSUpdate {
    enabled?:         boolean;
    hostname?:        string;
    ntsKEPort?:       number;
    ntpPort?:         number;
    timeoutSeconds?:  number;
}

/** How one synchronisation went, step by step. */
export interface NTSSyncResult {
    ok:           boolean;
    server:       string;
    at:           string;
    error?:       string;
    step?:        string;
    runtime_ms?:  number;
    offset_ms?:   number | null;
    ntske?:       Record<string, unknown>;
    ntp?:         Record<string, unknown>;
}

/** Where this hub gets the time from, and how its key exchange is doing. */
export interface NTSConfiguration {
    enabled:   boolean;
    server:    { hostname: string; ntsKEPort: number; ntpPort: number } & Record<string, unknown>;
    settings:  { timeoutSeconds: number | null };
    cookies: {
        available:     number;
        maxPoolSize:   number;
        lowWatermark:  number;
        seeded:        number;
        received:      number;
        consumed:      number;
        dropped:       number;
        isLow:         boolean;
        isEmpty:       boolean;
        isFull:        boolean;
    };
    policy:  Record<string, unknown>;
    keyExchange: {
        automatic:                 number;
        aeadAlgorithms:            string[];
        compliantExporterContext:  boolean;
        lastExchange:              { error: string | null; warnings: string[]; servers: string[] } | null;
    };
    lastSync:  NTSSyncResult | null;
    limits:    { maxTimeout: number };
    file:      string;
    /** Only on the answer to a synchronisation, which carries both. */
    result?:   NTSSyncResult;
}

/**
 * What time it is here, and what that is worth. `now` is the hub's own
 * system clock, and everything under `nts` is what happened when it last asked
 * a server that knows. `legal` is decided by the hub and never by this page.
 */
export interface Clock {
    now:        string;
    source:     string;
    nts: {
        enabled:       boolean;
        server:        string | null;
        lastServer:    string | null;
        checkedAt:     string | null;
        ageSeconds:    number | null;
        offset_ms:     number | null;
        everySeconds:  number;
    };
    legal:            boolean;
    authority:        string | null;
    why:              string | null;
    toleranceSeconds: number;
    maxAgeSeconds:    number;
}


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


export class ApiError extends Error {

    constructor(public readonly status:  number,
                message:                 string,
                public readonly body?:   unknown) {
        super(message);
        this.name = 'ApiError';
    }

    get isUnauthorized(): boolean {
        return this.status === 401;
    }

}


let unauthorizedHandler: (() => void) | null = null;

/** Called whenever the API answers 401, i.e. the session is gone. */
export function onUnauthorized(handler: () => void): void {
    unauthorizedHandler = handler;
}


/**
 * Sign in at the HTTPExt API and answer with who is now signed in.
 *
 * Two requests rather than one: the HTTPExt API is the only place that can
 * check a password, but it knows nothing of this hub's roles. So it sets the
 * session cookie, and "me" is asked afterwards for the roles and permissions
 * this frontend actually works from.
 */
async function signIn(username: string, password: string): Promise<Me> {

    const response = await fetch(config.extBase + '/login', {
                               method:       'POST',
                               headers:      {
                                                 'Content-Type':  'application/x-www-form-urlencoded',
                                                 'Accept':        'application/json'
                                             },
                               credentials:  'same-origin',
                               body:         new URLSearchParams({ login: username, password }).toString()
                           });

    if (!response.ok) {

        let message = `${response.status} ${response.statusText}`;

        try {
            const json = JSON.parse(await response.text());
            if (typeof json === 'object' && json !== null) {
                if      ('description' in json && typeof json.description === 'string')  message = json.description;
                else if ('error'       in json && typeof json.error       === 'string')  message = json.error;
            }
        }
        catch { /* the status line says enough */ }

        throw new ApiError(response.status, message, null);

    }

    return request<Me>('GET', '/auth/me');

}



async function request<T>(method: string, path: string, body?: unknown): Promise<T> {

    const headers: Record<string, string> = { 'Accept': 'application/json' };

    if (body !== undefined)
        headers['Content-Type'] = 'application/json';

    const response = await fetch(config.apiBase + path, {
                               method,
                               headers,
                               credentials: 'same-origin',
                               body: body !== undefined ? JSON.stringify(body) : undefined
                           });

    if (response.status === 401)
        unauthorizedHandler?.();

    if (response.status === 204) {
        await response.arrayBuffer();
        return undefined as T;
    }

    const text = await response.text();
    let json: unknown = null;

    try {
        json = text.length > 0 ? JSON.parse(text) : null;
    }
    catch {
        if (response.ok)
            throw new ApiError(response.status, `Invalid JSON in the response of ${method} ${path}`, text);
    }

    if (!response.ok) {

        const message = typeof json === 'object' && json !== null
                            ? 'error'   in json && typeof json.error   === 'string' ? json.error
                            : 'message' in json && typeof json.message === 'string' ? json.message
                            : `${response.status} ${response.statusText}`
                            : `${response.status} ${response.statusText}`;

        throw new ApiError(response.status, message, json);

    }

    return json as T;

}


export const api = {

    /** The Server-Sent Events stream; the browser sends the session cookie along. */
    eventsURL:         `${config.apiBase}/events`,

    /** The same, for the traffic - a stream of its own, behind a permission of its own. */
    trafficEventsURL:  `${config.apiBase}/traffic/events`,

    auth: {
        me:      ()                                    => request<Me>  ('GET',  '/auth/me'),
        login:   signIn,
        logout:  ()                                    => request<void>('POST', '/auth/logout')
    },

    status:         () => request<Status>       ('GET', '/status'),
    configuration:  () => request<Configuration>('GET', '/configuration'),

    /** What time it is here and what that is worth; cheap, and safe to poll. */
    clock:          () => request<Clock>        ('GET', '/configuration/time'),

    dns: {
        get:   ()                    => request<DNSConfiguration>('GET', '/configuration/dns'),
        save:  (update: DNSUpdate)   => request<DNSConfiguration>('PUT', '/configuration/dns', update),
        query: (name: string, recordTypes: string[]) =>
                   request<DNSQueryResult>('POST', '/configuration/dns/query', { name, recordTypes })
    },

    nts: {
        get:   ()                    => request<NTSConfiguration>('GET', '/configuration/nts'),
        save:  (update: NTSUpdate)   => request<NTSConfiguration>('PUT', '/configuration/nts', update),
        sync:  ()                    => request<NTSConfiguration>('POST', '/configuration/nts/sync', {})
    },

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

            /** Start the peering from here: fetch their versions, POST our credentials. */
            register:  (version: string, id: string) =>
                           request<{ ok: boolean; message: string; partners: Partners }>(
                               'POST', `/ocpi/partners/${encodeURIComponent(version)}/${encodeURIComponent(id)}/register`, {}),

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
     * Its own permission - readTraffic - and its own stream, beside the event
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

    /**
     * A page of the log, oldest of the returned entries first.
     *
     * @param limit  at most this many entries
     * @param after  only what is newer than this id
     * @param tag    only entries carrying this tag - a level counting as one
     */
    logs: (limit?: number, after?: number, tag?: string) => {

        const query = new URLSearchParams();

        if (limit !== undefined)  query.set('limit', String(limit));
        if (after !== undefined)  query.set('after', String(after));
        if (tag)                  query.set('tag',   tag);

        const suffix = query.size > 0 ? `?${query}` : '';

        return request<LogPage>('GET', `/logs${suffix}`);

    }

};
