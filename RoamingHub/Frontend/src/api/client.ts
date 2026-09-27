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
 * What a role may be allowed to touch on this hub: what every node has, and
 * what a hub adds to it - its peers, and what went between them.
 *
 * The traffic is a resource of its own and deliberately not part of the
 * configuration: the configuration is what this hub is, and the traffic is
 * what its peers did through it.
 */
export type Resource = 'configuration' | 'dns' | 'nts' | 'certificates'
                     | 'peers' | 'traffic';

/** How a resource may be touched. */
export type Operation = 'read' | 'edit' | 'run';

/**
 * What somebody signed in to this hub may do: an operation on a resource,
 * written "dns:edit".
 *
 * A copy of what the hub enforces, not the enforcement: it is here so a page
 * can grey out what this person may not do instead of offering it and letting
 * them find out by being refused. Every request is checked again on arrival,
 * so editing this list in a browser buys a button that answers 403. Spelt out
 * resource by resource by the hub, so "*" never arrives here.
 */
export type Permission = `${Resource}:${Operation}`;

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


/** What a certificate other than the one a server is held to comes to. */
export type PinMismatch = 'refuse' | 'record' | 'accept';

/** What a server is held to from the first time it is believed. */
export type TrustOnFirstUse = 'root' | 'certificate';

/**
 * What one server is held to beyond what every server is held to, as the hub
 * reads it back: the certificates it may show and the roots its chain may end
 * at - any one of them - what a mismatch comes to, and what it learns the
 * first time it is believed. Every fingerprint is a SHA-256 one, in the 64
 * lower-case digits the hub keeps.
 */
export interface ServerPins {
    /** The first certificate and root once more, as they were read when there could be only one of each. */
    certificate:      string | null;
    root:             string | null;
    certificates:     string[];
    roots:            string[];
    onMismatch:       PinMismatch;
    trustOnFirstUse:  TrustOnFirstUse | null;
}

/**
 * What a server is held to, in the keys its entry is written with: one of a
 * kind under the singular key, several under the plural - the way the
 * configuration file says it, and the way the hub takes it back.
 */
export interface PinKeys {
    certificateFingerprint?:   string;
    certificateFingerprints?:  string[];
    rootFingerprint?:          string;
    rootFingerprints?:         string[];
    onMismatch?:               PinMismatch;
    trustOnFirstUse?:          TrustOnFirstUse;
}

/** What a server was last believed with - pinned or not, another one is noticed. */
export interface KnownServer {
    certificate:  string;
    root:         string | null;
    since:        string;
}

/** What the hub made of a server's certificate, in one word. */
export type JudgementOutcome = 'accepted' | 'recorded' | 'tolerated'
                             | 'pinMismatch' | 'untrusted' | 'wrongName' | 'noCertificate';

/** What the hub made of the certificate a server showed, the last time it showed one. */
export interface ServerJudgement {
    server:       string;
    service:      string;
    at:           string;
    /** Whether the server was used: "recorded" and "tolerated" are, although a fingerprint did not match. */
    accepted:     boolean;
    outcome:      JudgementOutcome;
    certificate:  string | null;
    root:         string | null;
    /** The hub's own root it was validated by, where this machine knows none. */
    anchoredBy:   string | null;
    heldTo:       Pick<ServerPins, 'certificate' | 'root' | 'certificates' | 'roots'> | null;
    /** What it was held to from this connection on, trusted on first use. */
    learned:      TrustOnFirstUse | null;
    /** What it had been believed with before, where this was another certificate. */
    previously:   KnownServer | null;
    /** Only in the answer to a test: what was found, one step after another. */
    steps?:       { level: 'info' | 'notice' | 'warning' | 'error'; text: string }[];
}


/**
 * One name server as the hub is told it: what its configuration keeps, with
 * what it is held to where it is asked over TLS or HTTPS.
 */
export interface DNSServerEntry extends PinKeys {
    /** An IP address or a host name. */
    address:              string;
    port:                 number;
    transport:            string;
    queryTimeoutSeconds:  number | null;
}

/**
 * One name server this hub asks, and what the hub says about it: what it is
 * held to once more, the way the NTS answer has it, what was made of its
 * certificate last, and what it was last believed with. Those three are read
 * and never sent back.
 */
export interface DNSServer extends DNSServerEntry {
    heldTo?:     ServerPins | null;
    judgement?:  ServerJudgement | null;
    known?:      KnownServer | null;
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
    servers?:              DNSServerEntry[];
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
    /** Which single name server was asked, or null when all of them were. */
    asked?:         string | null;
    /** Set when an address was typed and a reverse name was asked for instead. */
    turnedAround?:  string | null;
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
    /** What was made of the certificate of every server this asked over TLS or HTTPS, step by step. */
    certificates?:  ServerJudgement[];
}


/** One line of what happened while a time server was being asked. */
export interface TimeServerTestStep {
    at_ms:  number;
    level:  'info' | 'notice' | 'warning' | 'error';
    text:   string;
}

/** What came of asking one time server everything. */
export interface TimeServerTest {
    host:        string;
    ok:          boolean;
    runtime_ms:  number;
    steps:       TimeServerTestStep[];
}

/**
 * What may be changed about the time servers while the hub runs. What is
 * left out stays as it is; the list of servers is one value and replaces the
 * hub's whole.
 */
export interface NTSUpdate {
    enabled?:              boolean;
    servers?:              NTSServerEntry[];
    minServers?:           number;
    maxDeviationSeconds?:  number;
    checkEverySeconds?:    number;
    timeoutSeconds?:       number;
}

/**
 * One time server as the configuration names it. Whatever is left out is the
 * usual: priority 0, the usual ports, switched on, held to no fingerprint.
 */
export interface NTSServerEntry extends PinKeys {
    hostname:    string;
    priority?:   number;
    ntsKEPort?:  number;
    ntpPort?:    number;
    enabled?:    boolean;
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

    /** What the group concluded: the median, how many answered, how far apart. */
    group?:       {
        name:               string;
        answered:           number;
        required:           number;
        offset_ms:          number | null;
        spread_ms:          number | null;
        deviationExceeded:  boolean;
    };

    /** One entry per server asked, answered or not. */
    servers?:     NTSServerResult[];

    /** Only from the detailed test of a single server. */
    ntske?:       Record<string, unknown>;
    ntp?:         Record<string, unknown>;
}

/** What one time server of a group said. */
export interface NTSServerResult {
    hostname:       string;
    ok:             boolean;
    offset_ms?:     number | null;
    roundTrip_ms?:  number | null;
    authenticated?: boolean | null;
    keyExchange?:   string;
    error?:         string | null;
}

/** One server of this hub's group, and what its key exchange is doing. */
export interface NTSTimeSource {
    hostname:       string;
    priority:       number;
    ntsKEPort:      number;
    ntpPort:        number;
    enabled:        boolean;
    cookies?:       number | null;
    lastExchange?:  string | null;
    aeadAlgorithm?: string | null;

    /**
     * The root CA the certificate chain of the last key exchange ended at -
     * the chain this hub built, so the root it judged the certificate by -
     * or null before the first exchange.
     */
    rootCA?:        NTSRootCA | null;

    /** The SHA-256 fingerprint of the certificate the last key exchange showed, which a pin is written down from. */
    certificate?:   string | null;
    heldTo?:        ServerPins | null;
    judgement?:     ServerJudgement | null;
    known?:         KnownServer | null;
}

/** A root CA, by a name to call it, its subject, and its SHA-256 fingerprint. */
export interface NTSRootCA {
    name:         string;
    subject:      string;
    fingerprint:  string;
}

/** Where this hub gets the time from, and how its key exchange is doing. */
export interface NTSConfiguration {
    enabled:   boolean;

    /**
     * Every server this hub has, switched on or not, in the order they
     * were configured - and the rules for believing them.
     */
    timeSources?:  NTSTimeSource[];
    group?:        { name: string; minServers: number; maxDeviationSeconds: number };

    /**
     * What may be changed about the group and the test. The quorum is the one
     * wanted; the group's own can be lower while it has fewer servers on.
     */
    settings:  {
        timeoutSeconds:       number | null;
        checkEverySeconds:    number;
        minServers:           number;
        maxDeviationSeconds:  number;
    };
    /** What any new client starts with, the group's and the test's alike. */
    policy:    Record<string, unknown>;
    lastSync:  NTSSyncResult | null;
    limits:    {
        maxTimeout:        number;
        minCheckEvery:     number;
        maxCheckEvery:     number;
        minDeviation:      number;
        maxDeviation:      number;
        defaultNTSKEPort:  number;
        defaultNTPPort:    number;
    };
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
        /** The group the clock is checked against, its servers switched on and its quorum; null while switched off. */
        group:         string   | null;
        servers:       string[] | null;
        minServers:    number   | null;
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


// Certificates

/**
 * What a certificate is for. Roots are believed and the hub's own
 * certificates are presented; a server certificate is neither, but kept to
 * recognise a server by its fingerprint.
 *
 * Every kind a node's store knows, as the node names them. A hub keeps the
 * four of TLS - tlsRoot, clientRoot, tlsServer, tlsIdentity - and none of the
 * seven of ISO 15118, which are a vehicle's and a charging station's; the
 * store says which kinds it keeps, and a page shows those.
 */
export type CertificateKind = 'v2gRoot' | 'moRoot' | 'oemRoot'
                            | 'vehicle' | 'contract' | 'oemProvisioning' | 'tariffVerification'
                            | 'tlsRoot' | 'clientRoot' | 'tlsServer' | 'tlsIdentity';

/** One certificate in the store. Everything but label, active and usages is read out of the file. */
export interface Certificate {
    /** The handle it is addressed by: the first 16 digits of its fingerprint. */
    id:             string;
    kind:           CertificateKind;
    fileName:       string;
    label:          string;
    subject:        string;
    issuer:         string;
    serialNumber:   string;
    /** Its SHA-256 fingerprint in full, for comparing against what a CA said. */
    thumbprint:     string;
    notBefore:      string;
    notAfter:       string;
    keyAlgorithm:   string;
    hasPrivateKey:  boolean;
    /** How many further certificates travel with it, e.g. its sub-CAs. */
    chainLength:    number;
    /** Whether this hub is using it. Somebody switches this; time does not. */
    active:         boolean;
    importedAt:     string;
    expired:        boolean;
    notYetValid:    boolean;
    /** Active, and inside its own validity. */
    usable:         boolean;
    description:    string;
    /**
     * What it may be used for - "dns", "nts" - where its kind is kept for
     * some uses and not others, and null there for every use. Left out for
     * every other kind, which is for what its kind says.
     */
    usages?:        string[] | null;
}

/** The whole store, grouped the way it is shown. */
export interface CertificateStore {
    directory:     string;
    /** The kinds that are trust anchors, in the order they are shown. */
    trustAnchors:  CertificateKind[];
    /** The kinds that are presented, in the order they are shown. */
    credentials:   CertificateKind[];
    /** The kinds that are neither: kept to recognise a server by its fingerprint. */
    recognised?:   CertificateKind[];
    kinds:         Record<CertificateKind, {
                       description:     string;
                       trustAnchor:     boolean;
                       needsPrivateKey: boolean;
                       /** Whether one of this kind is told what it is for. */
                       hasUsages?:      boolean;
                   }>;
    /** What a certificate of a kind that has usages may be told it is for. */
    usages?:       string[];
    certificates:  Record<CertificateKind, Certificate[]>;
    /** Whether anything in the store carries a private key, which is kept unencrypted. */
    keysAreUnencrypted: boolean;
}

/** What an import sends: the file, base64-encoded, and what to make of it. */
export interface CertificateImport {
    kind:       CertificateKind;
    /** The file's bytes, base64-encoded. PEM, DER or PKCS#12. */
    content:    string;
    /** What opens it, where it is a protected PKCS#12. Used once and not kept. */
    password?:  string;
    /** What to call it; its common name where this is left out. */
    label?:     string;
    /** What it is for, where its kind has usages; left out for every use. */
    usages?:    string[];
}

/** What a change to a stored certificate may say. Everything else is read from the file. */
export interface CertificateUpdate {
    active?:  boolean;
    label?:   string | null;
    /** What it is for; null for every use again, and left out to leave it alone. */
    usages?:  string[] | null;
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


/**
 * The hub answered, and said no.
 *
 * The fields are written out rather than declared in the constructor, as are
 * NoAnswer's below: constructor parameter properties are one of the few pieces
 * of TypeScript that cannot simply be stripped away, and this file is read as
 * it stands by Node's own test runner, in client.test.ts beside it.
 */
export class ApiError extends Error {

    readonly status:  number;
    readonly body?:   unknown;

    constructor(status:   number,
                message:  string,
                body?:    unknown) {

        super(message);

        this.name    = 'ApiError';
        this.status  = status;
        this.body    = body;

    }

    get isUnauthorized(): boolean {
        return this.status === 401;
    }

}


/**
 * Nothing came back at all.
 *
 * Not an ApiError, because the two are different things to be told: an
 * ApiError is the hub answering and saying no, with a sentence of its own
 * about why. This is the hub saying nothing - and a page that can tell the
 * two apart can say so, instead of repeating a status that was never sent.
 */
export class NoAnswer extends Error {

    readonly reason:  'ran out of time' | 'could not be reached';

    constructor(reason:   'ran out of time' | 'could not be reached',
                message:  string) {

        super(message);

        this.name    = 'NoAnswer';
        this.reason  = reason;

    }

}


/**
 * How long the web interface waits for the hub to answer about itself.
 *
 * Measured on the vehicle, whose pages this comes from, against one that had
 * gone quiet rather than away - the case a refused connection does not cover,
 * and the one a car park's network actually produces: 98 seconds after Save,
 * the request was still open, both buttons of the form were still greyed out,
 * and the page said nothing at all. Seven pages clicked through in that state
 * left nine requests hanging, more than the browser will even keep
 * connections open for.
 *
 * Fifteen seconds is four orders of magnitude more than the vehicle needed
 * there: every read and write of its own configuration measured between 1 and
 * 7 milliseconds. That is the point. The deadline is here to notice silence
 * and not slowness, so it can be generous enough that a slow link never trips
 * it.
 */
export const answerWithin = 15_000;

/**
 * And how long for the hub to do something and then answer.
 *
 * Longer, because a write is a file and whatever is made again from it, and
 * because giving up on a write is the worse mistake of the two to make: the
 * hub may have carried it out and only been slow to say so.
 */
export const actWithin = 30_000;

/**
 * How long a question the hub has to put to somebody else may take: the
 * timeouts of the steps it takes one after another, added up, and the usual
 * allowance on top - so that what the page gives up on is silence from the
 * hub rather than patience it was told to have.
 */
export function afterAsking(Timeouts: number[]): number {
    return Timeouts.reduce((total, seconds) => total + seconds * 1000, 0) + answerWithin;
}


let unauthorizedHandler: (() => void) | null = null;

/** Called whenever the API answers 401, i.e. the session is gone. */
export function onUnauthorized(handler: () => void): void {
    unauthorizedHandler = handler;
}


/**
 * Sign in at the HTTPExt API and answer with who is now signed in.
 *
 * Two requests rather than one, and that is not a detour. The HTTPExt API is
 * the only place that can check a password - the store it reads is private to
 * it - but it answers in its own shape and knows nothing of this hub's roles.
 * So it sets the session cookie, and "me" is asked afterwards for the roles
 * and permissions this frontend actually works from.
 *
 * Form-urlencoded because that is what its sign-in route accepts, and the
 * field is called "login" rather than "username".
 */
async function signIn(username: string, password: string): Promise<Me> {

    const giveUp = new AbortController();
    const timer  = setTimeout(() => giveUp.abort(), actWithin);

    let response: Response;

    try
    {
        response = await fetch(config.extBase + '/login', {
                             method:       'POST',
                             headers:      {
                                               'Content-Type':  'application/x-www-form-urlencoded',
                                               'Accept':        'application/json'
                                           },
                             credentials:  'same-origin',
                             signal:       giveUp.signal,
                             body:         new URLSearchParams({ login: username, password }).toString()
                         });
    }
    catch (problem)
    {
        throw nothingCameBack(problem, 'POST', actWithin, giveUp.signal.aborted);
    }
    finally
    {
        clearTimeout(timer);
    }

    if (!response.ok) {

        // Its refusals carry a "description"; ours carry an "error". Both are
        // shown to somebody who just typed a password, so both are read.
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


/**
 * One request to the hub, with a deadline.
 *
 * The deadline covers reading the body as well as opening the connection: a
 * hub that sends its headers and then stops mid-answer hangs exactly as
 * thoroughly as one that never starts.
 *
 * Exported so that the tests can drive it at a deadline short enough to be a
 * test; everything the pages do goes through `api` below.
 *
 * @param within  how long to wait, in milliseconds: a read is given
 *                answerWithin, anything that changes something - a POST, a
 *                PUT, a PATCH, a DELETE - actWithin, and a question the hub
 *                has to put to somebody else what afterAsking says.
 */
export async function request<T>(method:  string,
                                 path:    string,
                                 body?:   unknown,
                                 within:  number = method === 'GET' ? answerWithin : actWithin): Promise<T> {

    const headers: Record<string, string> = { 'Accept': 'application/json' };

    if (body !== undefined)
        headers['Content-Type'] = 'application/json';

    const giveUp = new AbortController();
    const timer  = setTimeout(() => giveUp.abort(), within);

    let response:  Response;
    let text:      string;

    try
    {

        // Same origin, so the session cookie travels with every request.
        response = await fetch(config.apiBase + path, {
                             method,
                             headers,
                             credentials: 'same-origin',
                             signal:      giveUp.signal,
                             body:        body !== undefined ? JSON.stringify(body) : undefined
                         });

        if (response.status === 401)
            unauthorizedHandler?.();

        if (response.status === 204) {
            // Nothing to read, but reading it lets the browser finish the
            // request cleanly instead of aborting an unconsumed body.
            await response.arrayBuffer();
            return undefined as T;
        }

        text = await response.text();

    }
    catch (problem)
    {
        throw nothingCameBack(problem, method, within, giveUp.signal.aborted);
    }
    finally
    {
        clearTimeout(timer);
    }

    let json: unknown = null;

    try {
        json = text.length > 0 ? JSON.parse(text) : null;
    }
    catch {
        if (response.ok)
            throw new ApiError(response.status, `Invalid JSON in the response of ${method} ${path}`, text);
    }

    if (!response.ok) {

        // The hub's refusals carry an "error". A registration with a peer that
        // did not go through answers 502 with a "message" instead, beside
        // what it left behind - so that is read as well, rather than showing
        // the status line in place of the one sentence that says why.
        const message = typeof json === 'object' && json !== null
                            ? 'error'   in json && typeof json.error   === 'string' ? json.error
                            : 'message' in json && typeof json.message === 'string' ? json.message
                            : `${response.status} ${response.statusText}`
                            : `${response.status} ${response.statusText}`;

        throw new ApiError(response.status, message, json);

    }

    return json as T;

}


/**
 * What to say when nothing came back, in words somebody can act on.
 *
 * A read that runs out of time changed nothing, and can be told so. A write
 * that runs out of time is the honest awkward case: the page stopped waiting,
 * but the hub may well have done the thing and been slow to say so, and
 * telling somebody that it did not work would invite them to do it twice. So
 * it says what is actually known - that the waiting stopped - and where to
 * look for the rest.
 */
function nothingCameBack(Problem:  unknown,
                         Method:   string,
                         Within:   number,
                         GaveUp:   boolean): unknown {

    const seconds = Math.round(Within / 1000);

    if (GaveUp)
        return new NoAnswer(
                   'ran out of time',
                   Method === 'GET'
                       ? `The hub did not answer within ${seconds} seconds. ` +
                         'It may be busy, restarting, or no longer reachable from here.'
                       : `The hub did not answer within ${seconds} seconds, so this page ` +
                         'stopped waiting. It may still have carried this out - reload to see ' +
                         'what it now says.'
               );

    // The browser's own word for this is "Failed to fetch", which on a page
    // about a hub names neither the hub nor what to do next.
    if (Problem instanceof TypeError)
        return new NoAnswer(
                   'could not be reached',
                   'The hub could not be reached. It may be switched off, restarting, ' +
                   'or on the other side of a network that is down.'
               );

    return Problem;

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
        /** Only the fields given are changed; the answer is the whole configuration as it now stands. */
        save:  (update: DNSUpdate)   => request<DNSConfiguration>('PUT', '/configuration/dns', update),
        /**
         * Make the hub look a name up. A POST because it sends traffic.
         *
         * @param seconds  how long the name servers asked may take - see
         *                 pages/dnsServers.ts.
         * @param server   which configured name server to ask, by its place in
         *                 the list - or undefined to resolve the way the hub
         *                 resolves anything else, asking all of them at once.
         */
        query: (name: string, recordTypes: string[], seconds: number, server?: number) =>
                   request<DNSQueryResult>('POST', '/configuration/dns/query', { name, recordTypes, server },
                                           afterAsking([ seconds ]))
    },

    nts: {
        get:   ()                    => request<NTSConfiguration>('GET', '/configuration/nts'),
        save:  (update: NTSUpdate)   => request<NTSConfiguration>('PUT', '/configuration/nts', update),
        /**
         * Ask one time server everything: the name, the key exchange and what
         * the certificate claims, the authenticated NTP request, each one
         * written down as it happens.
         *
         * @param timeoutSeconds  what the hub allows each of the two steps.
         * @param host            which server, on the ports it is configured
         *                        with, or undefined for the configured one.
         */
        test:  (timeoutSeconds: number, host?: string) => request<TimeServerTest>(
                                               'POST', '/configuration/nts/test', { host },
                                               afterAsking([timeoutSeconds, timeoutSeconds])),
        /**
         * Ask every server of the group, with every step in the log - two steps
         * over the network per server, so two of the hub's own timeouts before
         * the page stops believing in it.
         *
         * @param timeoutSeconds  what the hub allows each of the two steps.
         */
        sync:  (timeoutSeconds: number) => request<NTSConfiguration>(
                                               'POST', '/configuration/nts/sync', {},
                                               afterAsking([timeoutSeconds, timeoutSeconds])
                                           )
    },

    certificates: {

        /** The whole store, grouped by kind. */
        get:     ()                                       => request<CertificateStore>('GET', '/certificates'),

        /**
         * Put a certificate into the store.
         *
         * Importing the same file twice is the same entry - the handle is its
         * fingerprint - so this is safe to repeat.
         */
        import:  (certificate: CertificateImport)         => request<Certificate>('POST', '/certificates', certificate),

        /** Switch one on or off, rename it, or say again what it is for. */
        update:  (id: string, update: CertificateUpdate)  => request<Certificate>('PATCH', `/certificates/${encodeURIComponent(id)}`, update),

        /** Take one out of the store and delete its file. */
        remove:  (id: string)                             => request<CertificateStore>('DELETE', `/certificates/${encodeURIComponent(id)}`),

        /**
         * Read the store directory again.
         *
         * For certificates somebody copied in rather than uploaded - which is a
         * perfectly good way to install one on a machine you already have a
         * shell on.
         */
        reload:  ()                                       => request<CertificateStore>('POST', '/certificates/reload', {})

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
