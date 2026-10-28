# RoamingHub

One OCPI roaming hub, with a JSON API in front of it: a C# HTTP backend built
on [Hermod](https://github.com/Vanaheimr/Hermod) and the
[OCPI library](https://github.com/OpenChargingCloud/WWCP_OCPI).

A hub sits between the charge point operators and the e-mobility service
providers so that they do not each have to be peered with all the others.
Every one of them is peered with the hub instead, once, and the hub is what
turns that into a mesh.

```
  CPO  ──OCPI 2.2.1──▶  RoamingHub  ◀──OCPI 2.2.1──  EMSP
                        every call through it written down,
                        with the two parties it was between
```

This is built the same way as
[EMSP](https://github.com/OpenChargingCloud/EMSP) and the OCPI side of
[CSMS](https://github.com/OpenChargingCloud/CSMS) - the same configuration
file, the same accounts, the same event log, the same shape of peering - so
that somebody who has read one of them has read this one.

Below it is [WWCP_Node](https://github.com/OpenChargingCloud/WWCP_Node): what
every one of these programs is before it is anything in particular - the log,
the configuration file, name resolution and the time, a certificate store, the
accounts, and the HTTP server with the web interface behind it. The vehicle of
[EV](https://github.com/OpenChargingCloud/EV) is one of those with a battery,
the charging station one with EVSEs; this hub is one with OCPI, its peers and
what goes between them.


## What is here

| | |
|---|---|
| The base | WWCP_Node's - name resolution, the time source, the certificate store, the accounts, the event log, and the JSON API every node answers with its event stream, to which the hub adds its own routes |
| The peering | a peer added, a token handed out, and the credentials exchanged in **either** direction |
| The traffic | every OCPI call that touched this hub, both ways, with the two parties of it - on its own page and its own stream |
| Who may do what | three roles - `viewer`, `hub` and `systemadmin` - over the node's resources and the hub's four, `peers`, `traffic`, `poi` and `poi-status`, and whatever the configuration file adds |
| The web interface | all of the above in a browser: the traffic as it happens, the peers, the name servers, the time servers, the certificates and the log |
| HubClientInfo | who is on this hub and whether they can be reached, over OCPI and on the Peers page |
| The points of interest | roaming networks of POI 3 below `/poi`, public: operated by this hub, which is a roaming network operator with keys of its own, and filled by change sets their owners sign; their runtime status set over the WebSocket of a history by the charging station operators among the peers, and by accounts bound to an operator |

**What is not here yet.** Forwarding what one peer sends to another, which is
what would make this hub more than a directory. That is the next thing.

**No OCPI 2.1.1, and there cannot be.** The hub role arrived with OCPI 2.2 and
the library has no hub side for the version before it. A CPO or an EMSP that
speaks only 2.1.1 cannot be peered with this hub; it has to talk to its
counterpart directly.


## The web interface

`Frontend/` - TypeScript and SCSS, bundled by webpack, embedded into the
assembly by `RoamingHub.csproj` so that the hub is one thing to deploy. Built
like the other components': one entry point, one sign-in at Hermod's HTTPExt
API under `/ext`, and a menu down the left.

What its pages stand on is not here but WWCP_Node's, the same for every kind
of node: the HTML template, the router, the base path, what the page is told
by the hub that serves it, and the question before a page's changes are left
behind - asked in the name the hub goes by; how the hub is asked, and the
types and routes of everything every node answers; the log's store and the
order its lines are drawn in; who is signed in; the small things every page
formats and says alike; the frame with its menu, the sign-in, the Logs page,
the page for an address with none, and how all of it starts; the DNS page and
the NTS page, and what a server's certificate is held to; the certificate
store's page; and the stylesheet, in the hub's colour. It is in
`libs/WWCP_Node/Frontend/src` and bundled in as `@node/...` through a
webpack alias, so the hub gets that of the WWCP_Node it pins, and a change
there rebuilds the bundle as a change here does. What is the hub's own:
`main.ts`, one call that says what the hub is called, what its menu has -
the traffic first, each entry for whoever may open its page - which pages
are its own, and what the store's page says in the hub's words: a client
root and a TLS identity kept, and used by nothing here yet; `app.scss`,
what only those pages need; and `api/client.ts`, what a hub adds to what
every node answers - its resources, its status with its party, its
configuration's own sections and the four kinds of TLS its store keeps, its
peers and the traffic between them, and their routes.

What it opens on is the traffic, and that is the whole difference. The other
components open on their configuration, because that is what somebody sets up
once and then leaves alone. A hub is set up once and *watched*, and the
question somebody walks up to it with is "are these two seeing each other?".

```
dotnet build                            builds the frontend when its inputs changed
dotnet build -p:SkipFrontendBuild=true  backend only, reuses the existing dist/
npm run watch                           in Frontend/, beside a hub started with
                                        --frontend Frontend/dist
```

Building it needs Node.js; `SkipFrontendBuild` is there for a machine that has
none, and `--frontend <dir>` serves the bundle off disk instead of out of the
assembly, which is what makes `npm run watch` show up on a reload.


## HubClientInfo: who is on the hub

A peer that is peered is not the same as a peer that is *there*, and the
difference is the question somebody actually walks up to a hub with. So every
peer carries a connection status beside its registration:

| | |
|---|---|
| `PLANNED` | peered, but the registration is not complete |
| `CONNECTED` | heard from within the last five minutes |
| `OFFLINE` | registered, and has gone quiet |
| `SUSPENDED` | switched off here by an operator |

Served at `GET ~/v2.3.0/hubclientinfo` to the peers, with `date_from`,
`date_to`, `offset` and `limit`, and on the Peers page of the web interface
for the people who run the hub. Both read the same answer.

**Nothing is ever deleted**, and the specification is explicit about why: a
ClientInfo object that simply vanished would leave every other peer holding a
party the hub has forgotten, with no way to tell that from one that is merely
quiet. A peer that is not to be talked to is set to `SUSPENDED` instead, and
it then also leaves the `roles` this hub advertises in its credentials -
nobody is invited to address a party the hub will not forward to. Nor is it
listened to: the tokens of this hub it holds are `BLOCKED` in the OCPI
library, on every version it is peered on, so that it neither lists who is on
the hub nor reads or renews its credentials until it is resumed. That is
written to the file of the peers - a suspension the file cannot take is a
500, and nothing changed - and a peer is still suspended after a restart.

**How a status is decided: by not asking.** Every OCPI call a peer makes
already passes the traffic recorder, which identifies it by its token - so a
call *is* the liveness signal, and a hub whose peers are busy never has to
check anything. A timer only catches the quiet ones. That is the specification's
keepalive seen from the other side, and it costs no traffic at all.

**Two things OCPI 2.3.0 changed** for a hub, both around this module and both
implemented:

- `hub_party_id` in the credentials, which a platform with hub message routing
  SHALL set. A hub names itself.
- The `roles` in a hub's credentials now list the parties reachable *through*
  it, not only the hub itself, as they did in 2.2 and 2.2.1.

**Both halves are here.** A peer can ask, and it is also told: when a status
changes, the hub `PUT`s the new ClientInfo to every other peer's receiver
endpoint. Where that endpoint is comes out of the peer's own version details,
so a peer that does not offer the module needs no configuring - it is simply
not told.

Three peers are skipped, and each for its own reason: the subject, which
already knows; one that has handed out nothing to call it with; and one that
is itself `OFFLINE` or `SUSPENDED`, because the specification asks that
nothing be queued for it, and because news about a third party is not worth
waiting out a timeout for.

The push runs off the thread that caused it, and that is not an optimisation.
A status changes inside the traffic recorder - a peer's own call is what
reveals it - so pushing inline would make one peer's OCPI request wait on an
HTTP round trip to every other peer, with the unreachable ones slowest of all.
One push at a time, ten seconds per peer, and a hub that is shutting down
does not wait for any of it.

**2.3.0 only.** The module is also in OCPI 2.2 and 2.2.1, but the store and
the endpoint live in the library's 2.3.0 Common API; a hub offering 2.2.1
keeps the status on its own page and has no OCPI endpoint for it. The hook
each version fills is `OCPIVersion.PublishClientInfo`.


## The traffic, which is the point of a hub

Two peers that talk directly can each read their own log and compare them. The
moment a hub is between them, neither can say what the other actually sent,
and "it works for us" is an answer nobody can check. So every call is written
down here:

```
GET /api/v1/traffic?limit=200&after=1234&peer=DE*GEF
GET /api/v1/traffic/events                        ← the same, as it happens
GET /api/v1/traffic/peers                         ← everyone seen, for a filter
```

A line says which way the call went, which peer was at the other end, the two
parties out of the OCPI `from` and `to` headers, the version and module, the
HTTP status **and** the OCPI status inside the envelope - because OCPI answers
a refusal with `200` and a status code as readily as with a `4xx` - how long it
took, and how big it was.

The bodies are not kept unless `ocpi.logging.payloads` says so: what travels
through a hub is a location somebody operates, a session somebody is having, a
card somebody is holding, and none of it is the hub's to keep.

A call this hub fails to handle - a peer's, or one at the versions list from
anybody - is answered OCPI 3000 with HTTP 500, a message that says nothing of
the hub, and the request and correlation ids to quote, as headers and in the
body. What failed is an error in the log, tagged `ocpi` and `http`, with the
call's method and path, the peer or the address it came from, and the same
ids - not its headers, whose `Authorization` is a token.

It is in memory and nowhere else to begin with, because how long a hub may
keep its peers' business is a question with a different answer in every
jurisdiction - so the hub does not answer it, and an upgrade must not answer
it either.

A deployment that has decided may say so:

```json
"ocpi": { "logging": { "file": true } }
```

Every call is then also appended to `traffic/` beside the configuration, one
file per day, one JSON object per line, flushed as it is written. The ring
buffer stays what it was - the fast answer for the page and the stream - and
the file is the part that forgets nothing. The `id` of a line belongs to the
buffer and starts again at one after a restart; a line is found by when it was
written, and across systems by the OCPI `X-Request-ID` and `X-Correlation-ID`
that are on the record already.

The day's file is held open while the hub runs, and reading it then needs a
reader that allows a writer: `Get-Content`, `type` and most of what is at hand
ask for exclusive-enough sharing and are refused, which looks like a broken
file and is not one. `tail -f` and anything else that opens it read-write can
follow it live, and once the day turns or the hub stops, the file is an
ordinary one that anything will open.

Reading it is its own permission - `traffic:read` - and not part of
`configuration:read`: the configuration is what this hub is, and the traffic is
what its peers did through it. The `hub` role may read it and the `viewer` may
not; see [Who may do what](#who-may-do-what).

**Behind a proxy.** Both streams - this one and the log's at `/api/v1/events` -
say `X-Accel-Buffering: no`, which nginx honours without a change to its
configuration, and send a comment down the line whenever they have been silent
for 15 seconds. Without the header nginx buffers a stream until it gives up on
it, and the browser sees nothing at all, not even that it opened; without the
comment a hub whose peers are quiet reaches nginx's 60 seconds without data,
and nginx ends the stream. `EventStreamHeartbeat` on the API sets the interval;
zero switches the comment off.


## What it can be told

The `configuration.json` beside it, in the same shape as the EMSP's:

| Section | |
|---|---|
| `dns` | the name servers, how they are asked, and what a server over TLS or HTTPS is held to |
| `nts` | the time servers, the rules for believing them, what each is held to, and how often the clock is checked |
| `certificates` | where the certificate store is: `directory`, `certificates/` beside the file by default |
| `roles` | roles beyond the three a hub brings, or what one of them may do |
| `web` | how the web interface, the JSON API and the sign-in are reached safely: HTTPS of the hub's own - with its store's `tlsServerIdentity` or certbot's PEM files, read again when they change - with HSTS and a port that redirects to it, or the reverse proxies in front that do the HTTPS and are believed when they name the client |
| `ocpi` | who this hub is - country code, party identification, name - which versions it offers, and in which networks it may reach its peers |
| `datex2` | which roaming networks are published as DATEX II, to be pulled and pushed to the Mobilithek |
| `poi` | what the histories of the roaming networks keep, whether the commits they drop are archived, and which roles may set the runtime status of which charging station operators |

The node below reads the sections every one of these programs has - `dns`,
`nts`, `certificates`, `roles`, `ssh` and `web` - and the hub reads its own,
`ocpi`, `datex2` and `poi`, from the same document; each passes over what is
the other's.

`web` is what a hub listening beyond its machine needs most: `--any`, which
its OCPI peers need, puts the sign-in's password, every API key and the
session cookie on the network, in the clear unless one of two things is said
(POI3-ISSUES.md S13). Either HTTPS of the hub's own,

```json
"web": { "tls": { "certificate": { "fullchain": "/etc/letsencrypt/live/hub.example.org/fullchain.pem",
                                   "privateKey": "/etc/letsencrypt/live/hub.example.org/privkey.pem" },
                  "redirectPort": 80 } }
```

or `"certificate": "store"` with the store's TLS server identity, put there
with `--import-certificate tlsServerIdentity=<file>` or on the Certificates
page - or the proxy in front that does the HTTPS, nginx as the hub has been
run so far:

```json
"web": { "trustedProxies": [ "127.0.0.1", "::1" ] }
```

Then the client nginx names in X-Forwarded-For is who the log, the sign-in's
ration of guesses and an API key bound to addresses see, and the session
cookie is a secure one where X-Forwarded-Proto says `https`. The switches
`--https`, `--https-cert <file>` with `--https-key <file>`, `--redirect-port
<number>` and `--trusted-proxy <address>` say the same and win over the file.
Neither said, with `--any`, the hub warns in its log, below its banner and
above every page to its administrators. WWCP_Node's README has all of it.

Everything in `dns` and `nts` takes effect the moment it is saved. The `ocpi`
section is read once at the start and deliberately not changeable while
running: it is what every peer wrote into its credentials, and changing it
under a live registration would not rename the hub, it would make it a second
one nobody is peered with.

The peers themselves are in none of it. The OCPI library keeps them in
append-only files of its own below an `ocpi/` directory beside the
configuration, one set per version, and reads them back at every start; a line
it cannot read is passed over, and an error in the log names the file and why,
not the line. A peer
is added, removed or registered only once its file has it: where that file
cannot be written, the Peers page is answered 500 with why, and nothing
changes - but for a registration the peer has accepted already. That one is in
effect, the peer uses the new tokens, and it is written down with the next
change the file takes, or when the hub stops; the log says so where it still
could not be. A peer that registers here or unregisters meanwhile is answered
OCPI 3000 with HTTP 500, its token as it was.

A peer tells the hub where to go: the URL its versions are fetched from when
it registers, and the URLs those lead to. The hub goes to the public internet
and nowhere else unless `peerNetworks` says so - not to itself, its private,
shared or link-local networks, the metadata service of a cloud at
169.254.169.254, nor their IPv6 and NAT64 twins (POI3-ISSUES.md S19). Every
address a name resolves to is checked when it is dialled, so a name rebound
to a private address after a first look does not get there; an address that
is refused is answered like one nobody answers at. Rules come first, in their
order, the first that matches deciding:

```json
"ocpi": { "peerNetworks": [ "192.168.7.0/24 => ALLOW", "203.0.113.0/24 => DENY" ] }
```

A peer in a test network, or on the same machine, needs its network allowed
here - `"127.0.0.0/8 => ALLOW"` for one on this host; a rule that is none stops
the start and says which.

```json
{
  "dns": { "enabled": true, "servers": [ { "address": "9.9.9.9" } ], "useCache": true },
  "nts": { "groups": {
    "legal": { "enabled": true, "servers": [ "ptbtime1.ptb.de", "ptbtime2.ptb.de",
                                             "ptbtime3.ptb.de", "ptbtime4.ptb.de" ],
               "minServers": 2 }
  } }
}
```

What a section does not mention is left as it is, and a section that is
missing leaves everything as the hub was built.

### DNS

An entry of `dns.servers` is an address or a host name, as a string or as the
object the example above uses - which may say more, and is the form the DNS
page writes the list back in:

```json
{ "address": "9.9.9.9", "port": 853, "transport": "TLS", "queryTimeoutSeconds": 2 }
```

Without a port, the transport's own is used. `udp://9.9.9.9:53` is how the log
and the banner name a name server, and not a form the file takes: a file saying
it is refused at the start, with the file and the entry named, and the page
refuses it the same way.

A name server reached over TLS or HTTPS shows a certificate, and is judged by
it at every handshake the way a time server is at its key exchange - see
below: issued for the address it is dialled at, chaining to a root this
machine trusts or to a TLS root of the hub's store kept for `dns`, and, where
its entry says so, one it is held to:

```json
{ "address": "1.1.1.1", "transport": "TLS", "trustOnFirstUse": "root" }
```

The keys are a time server's, and on an entry asked over UDP, TCP or plain
HTTP they are refused, because such a server shows no certificate to hold it
to. The DNS page looks a name up the way the hub resolves anything, or asks
one of the name servers on its own, and says per server what it is held to,
what was made of its certificate last and what it was last believed with.

### NTS

**It asks a group, not a server.** `nts.groups.legal.servers` is a list, and
by default it is the PTB's four, of which `minServers` - two - have to answer
before the group has a time at all. One host being rebooted does not leave
this hub without a check, and two servers that agree catch what one server
cannot: one that is wrong rather than absent. What the check reports is what the servers
that answered and authenticated agree on, with a line for each of them, so a
failure says which of the four failed and how. The time is measured against
this hub's clock and never set from it.

Every key of one group of time servers, and what it is when absent - the same
under each group the section names, `nts.groups.legal` among them:

| Key | Default | |
|---|---|---|
| `enabled` | `true` | whether to ask at all |
| `servers` | the PTB's four | a list, see below |
| `minServers` | `2`, or all of them when fewer | how many must answer for the group to have a time |
| `maxDeviationSeconds` | `60` | how far apart they may be before it is written down |
| `hostname` | - | one server instead of a list |
| `ntsKEPort`, `ntpPort` | `4460`, `123` | for that one server |
| `timeoutSeconds` | `10` | per request of a server's test |
| `checkEverySeconds` | `900` | how often the clock is checked |
| `errorLogging` | `5` | failed checks in a row before that is written down |
| `maxErrors` | `10`, `50` for "local" | failed checks in a row before what rests on the group is limited |
| `displayName` | - | what a page calls the group, see below |
| `legalTimeAuthority` | - | who the operator says stands behind it |
| `legalTimeToleranceSeconds` | `1` | how far off the clock may be |
| `legalTimeMaxAgeSeconds` | `3600` | how old the last check may be |

**A hub may ask more than one group.** The word does double duty here, and it
is worth separating: above, a group is the set of servers a quorum is taken
over; here it is a named group of its own, with its own servers, its own
client and its own checks. `legal` and `local` are the well-known names. The
section names them under `groups`:

```json
"nts": { "groups": {
  "legal": { "servers": [ "ptbtime1.ptb.de", "ptbtime2.ptb.de" ], "minServers": 2,
             "legalTimeAuthority": "PTB" },
  "local": { "servers": [ "lc1.local" ], "checkEverySeconds": 30,
             "displayName": { "en": "Local time", "de": "Lokale Zeit" } }
} }
```

A group's name is lower-case letters, digits and hyphens, beginning with a
letter, and `displayName` is what a page calls it - one text for every
language or one per language, and without it a page says "<id> time". Only
the legal group may say `legalTimeAuthority`, `legalTimeToleranceSeconds`
and `legalTimeMaxAgeSeconds`, and its checks are what goes into the
metrological log; every other group's go into the log like anything else.
The legal group naming no server is the PTB's four, while any other group
has no default: it is off while it names nobody, and refused switched on
without one. `groups` with keys beside it is refused at the start, since
those keys would belong to no group.

**A file written flat is refused.** The section of the files from before
there were groups - `nts.servers` and the rest directly under `nts` - is not
read any more: the hub does not start with it, and says which keys to move
into `nts.groups.legal`. A key is named by its place,
`nts.groups.local.ntpPort`. `"nts": { }`, like no section at all, is the
legal group as the hub makes it, the PTB's four.

Servers sharing a priority are **one band** and are asked together; a lower
priority is asked first. The four it asks by default share one, because they
are peers - putting them in separate bands would say something about them that
is not true. An entry may be a bare host name or an object saying more:
`{ "hostname": "time.local", "priority": 0, "ntsKEPort": 4460, "enabled": true }`.

Servers that disagree by more than `nts.maxDeviationSeconds` are written down
rather than acted on. The disagreement belongs in the log, and the time is
still a time.

A section naming a single `hostname` and no list becomes a group of one,
which is what every file written before a group could be a list says, and
it keeps working. A group of one is held to a quorum of one, and a section
asking two of it is refused. A list without `minServers` is held to two, as
the default four are, or to all of its servers when it has fewer switched on.

A section mentioning neither leaves the servers alone rather than quietly
reducing four to one, and one mentioning nothing but `minServers` or
`maxDeviationSeconds` holds the servers the hub already has to it. A quorum
those servers could never reach is refused: at the start, before anything is
asked, and over the API, before anything is written into the file.

A server may be held to more than a certificate authority vouching for it:
the certificates it may show and the roots its chain may end at, each by its
SHA-256 fingerprint, and what a mismatch comes to:

```json
{ "hostname": "time.local", "rootFingerprint": "4F:1C:…", "onMismatch": "record" }
```

Several of a kind are a list under `certificateFingerprints` or
`rootFingerprints` - two certificates are a renewal that has been announced,
two roots a CA moving to a new one. A server showing another certificate is
refused - its key exchange ends, and it gives no time - unless `onMismatch`
says `record`, which uses its time and writes the mismatch into the
metrological log, or `accept`, which uses it and says so in the log.
`"trustOnFirstUse": "root"` - or `"certificate"` - holds a server to what it
was first believed with, written into its entry of the file the moment it is
learned. That can be while the NTS or the DNS page is open, and a page sends
the whole list back from what it loaded - so beside each server it sends what
it showed that server held to, as `pinsAsShown`, and the node changes only
what was changed on the page: a root learned in between stays, and a pin the
page showed and no longer sends still goes. A pin narrows what is believed
and never widens it: the chain still
has to end at a root this machine trusts, at a TLS root of the hub's store
kept for `nts`, or at a root the server is held to that the store keeps.

What every server - a time server, a name server over TLS - was last
believed with is kept in `known-servers.json` beside the configuration file,
fingerprints and nothing else, so that another certificate is noticed across
a restart even where a server is held to none.

The NTS page shows a tab per group, and a `+` tab that makes one, switched
off until it names a server. Within a tab it lists every server of that
group with a Test of its own - the name, the TLS handshake and what the
server's certificate claims, down to the root CA it ends at, with the
SHA-256 fingerprints of both, which are what a pin is compared with; the key
exchange and the authenticated request, each step timed - and an Edit, where
the pins go: the fingerprint the server showed
last and the certificates and roots the store keeps for it are offered with a
click. Below them is what the group is held to, and what counts as legal
time - who stands behind the servers' time, how far off the clock may be
found and how old its last check may be - on a card of its own, after whose
Save the clock is read again. "Sync now" asks the group the way the clock
check does. Neither steps the clock.

The check runs by itself every `checkEverySeconds` of its group, the first
one a minute after starting. A new interval, and switching NTS off or on,
reach a running check at once. What the clock is worth - the time, against which group it was
checked and how many of it had to answer, how long ago and how far off, and
whether all of that adds up to legal time and why not - is served at
`GET /api/v1/clock`, and is the first card of the NTS page.

A host name written back into the file carries the root label -
`ptbtime1.ptb.de.` - because that is the absolute form it was parsed into, and
not a stray character. What the hub prints for somebody to read - the log, the
banner and the pages - drops it again.


## The certificate store

Everything this hub believes in TLS, what it will present and every server it
recognises lives in one store: `certificates/` beside the configuration file,
unless the file's `certificates.directory` or the command line says
otherwise. It is the node's store, and a hub keeps the four kinds of TLS in it,
the identity its web interface shows over HTTPS, and none of the seven of ISO
15118, which are a vehicle's:

| Kind | |
|---|---|
| `tlsRoot` | what a time server, or a name server over TLS or HTTPS, may chain to - kept for `nts`, `dns` or both |
| `tlsServer` | a server's own certificate, kept to hold the server to by its fingerprint - for `nts`, `dns` or both |
| `clientRoot` | what a client connecting to this hub will have to chain to - the root, or the CA that issues the clients |
| `tlsIdentity` | who this hub is to a server that asks, with its private key - looked after on the **Identities** page, as every certificate with a key is |
| `tlsServerIdentity` | what the web interface, the JSON API and the sign-in show over HTTPS, with its private key, where the `web` section says `"tls": { "certificate": "store" }` or the command line `--https` |

The first two are used today, and the last where HTTPS of the hub's own is
asked for; `clientRoot` and `tlsIdentity` are kept, and used by nothing here
yet. The peers are what they are waiting for: a hub reaches more servers
over HTTPS, and is reached by more clients, than any other kind of node.

The store is the directory: one file per certificate below it, and an
`index.json` recording what a file cannot say about itself - what somebody
calls it, whether it is switched on and, for a root or a server certificate,
what it is kept for. So a store copied to another machine arrives complete,
and a lost index costs labels, switches and usages rather than certificates.
Certificates copied into the directory by hand are adopted at the next start,
or at once with a `POST` to `/api/v1/certificates/reload`. Switching
one off leaves its file where it is; deleting it deletes the file, private key
and all. Time switches a certificate off as well, and separately: an expired
one stays listed and stops being used.

**The private keys in the store are not encrypted.** A PKCS#12 is opened with
its password once, at import, and written back without one. What guards them
is the file system - the directory is made for its owner alone, on Windows
as on Unix (see WWCP_Node, "Who may sign in") - so the store belongs on a machine
whose users are all trusted with this hub's identity. The hub says so at every
start with a key in the store, and the Identities page says it where the
keys are listed.

| | |
|---|---|
| `GET /api/v1/certificates` | the store, grouped by kind, with what each kind is and what it may be kept for |
| `POST /api/v1/certificates` | an import: `kind`, the file base64-encoded as `content`, and `password`, `label` and `usages` where there are any |
| `POST /api/v1/certificates/reload` | read the directory again |
| `GET` / `PATCH` / `DELETE /api/v1/certificates/{id}` | one certificate; a `PATCH` sets `active`, `label` and `usages` |

Reading it is `certificates:read`, which all three roles have; changing it is
`certificates:edit`, which only a `systemadmin` has.


## The points of interest

`RoamingHub.POI.cs` serves the roaming networks of
[POI 3](https://github.com/OpenChargingCloud/WWCP_POI) below `/poi`, public:
`/poi/RNs` and everything below it, to query and to synchronise, the web
interface's **Points of interest** pages at the very same URLs. Each network is
a directory below `poi/` beside the configuration file, its history's store,
read back at every start; one that cannot be read is said in the log and left
alone.

A charging pool, a charging station, an EVSE and a parking space of a parking
operator are shown as they are at runtime, with the properties their templates
give them, and every value a template gives says so, leading to the template:
"from template DE*GEF*ST1", or of an array the elements each template gives.
The page asks the member as it is given (`?view=stored`) and its templates,
listed below it in its order (`…/CSTs` and the like), and decides as the POI
API does: a single value of the first template giving one, the charging tariffs
of an EVSE of its most specific templates (`poiOrigins.ts`). So are the energy
meters a pool, station, EVSE or grid connection point embeds, with what the
energy meter templates of their charging station operator or grid operator give
them (`…/EMTs`). Groups give nothing, they collect members; each template and
each group has a page below its owner.

A charging pool, a charging station and an EVSE show the charging tariff offers
covering them, of their operator and of e-mobility providers (`…/CTOs`): every
one, or those applying now or at a time chosen (`?applicable=true`, `&at=`),
with the status of each now - planned, active, exhausted or expired - as the
POI API tells it (`?status=`). The list is asked again when a status changes by
the times of an offer, at its `notBefore` and right after its `notAfter`, and
while those applying now are shown, at every minute; each offer leads to its
page below its owner, through `/poi/RNs/{RN_Id}/CTOs/{CTO_Id}` (`poiOffers.ts`).

The hub is a **roaming network operator** (`RoamingHub.RoamingNetworkOperator.cs`).
At its first start it makes a signing key (P-256) and an identity key (P-521)
and keeps them below `roamingNetworkOperator/` beside the configuration file:
`operator.json`, the operator as its roaming networks embed it, with the public
halves of the keys, and `signing-key.pem` and `identity-key.pem`, the private
keys as PKCS #8, readable only by the account the hub runs as. It is read back
at every start; a missing private key stops the start, as a new one would be
none its roaming networks know. Who it is is said once, at the first start, by
the section `roamingNetworkOperator` of the configuration file,
`{"@id": "GraphDefined", "name": "GraphDefined GmbH"}`; without it, it is
`RNO-` and the first eight hex digits of its identity key. Keep a copy of both
keys where nobody else can read it: without the identity key, the keys of the
roaming networks it operates can never be replaced. `roamingNetworkOperator` at
the command line and `GET /api/v1/poi/roamingNetworkOperator`, public, show it
with its public keys. A fresh hub serves no roaming network.

A new roaming network is made with nothing but a name and a description, and
this hub as its operator: `CreateRoamingNetwork(id, name)` in C#,
`POST /api/v1/poi/RNs` with `{"@id", "name"?, "description"?}`, the
form below the list of the roaming networks, or `roamingNetworks add <id>
[<name>]` at the command line. The identification is the name of its directory
as well: letters, digits, `-` and `_`, at most 64 of them. 201 with its genesis
commit, 400 where it is no roaming network this hub makes - one with keys, an
operator, a party or a registry in it among them - 409 where there is one of
the same identification already, also in another case, and 500 where its store
could not be written. Every commit the hub makes of a roaming network it
operates is signed by its signing key, in the role `roamingNetworkOperator`.

The hub adds the parties of a roaming network it operates - charging station
operators, e-mobility providers, grid operators, parking operators, charging
station and energy meter manufacturers, transparency software manufacturers and
conformity assessment bodies - with a
change set it signs: `AddParty(roamingNetworkId, entityType, json)` in C#,
`POST /api/v1/poi/RNs/{id}/CSOs` (or `EMPs`, `GOs`, `POs`, `CSMs`, `EMMs`, `TSMs`, `CABs`) with the party as
POI writes it - the path the POI API lists it at, `/poi/RNs/{id}/CSOs`, below
`/api/v1` - `{"@id", "name"?, …, "signingKeys", "identityKeys"}`, the second
form below the list, or `roamingNetworks <id> add chargingStationOperator
<operatorId> signingKey <key> identityKey <key> [<name>]` at the command line.
The party keeps its private keys: a key is the Base64 of its COSE key or a key
as POI writes it, its identification calculated where it is not given, and a
private key is refused, as it is private no more once it was sent. A grid
operator or a conformity assessment body not taking part comes without keys. The change set waits until the
party signed it with each of its identity keys, as the proof that it holds them
(SPECIFICATION.md of WWCP POI, section 4.1), with `POST
/poi/RNs/{id}/history/changeSets/pending/{changeSetId}/signatures`, and is
committed then. 202 with the change set waiting, `{message, status: "pending",
changeSetId, changeSet, missing}`, 201 with the commit of a grid operator or a
conformity assessment body without keys, 400 where it
is no party this hub adds, 403 where another operator operates the network,
404 where there is no such network, 409 where it has that party already, and
422 where its history refused the commit, e.g. for a party with its charging
pools in it: they are its own to add. `roamingNetworks <id>` shows a network
with its parties and their keys. Making a network and adding a party is
`poi:edit`.

The manufacturers the charging stations and energy meters of the parties name
are parties of their own, added as the others, with their keys:
`roamingNetworks <id> add chargingStationManufacturer <id> signingKey <key>
identityKey <key> [homepage <url>] [<name>]` (or `energyMeterManufacturer`) at
the command line. Each states its models itself, with change sets it signs with
a key that may sign `models`, and a station or a meter names only a model of its
manufacturer, so they come before the stations and meters naming them; the hub
signs no model. The POI API lists them at `/poi/RNs/{id}/CSMs` and
`/poi/RNs/{id}/EMMs`, their models at `…/CSMs/{id}/models` and
`…/EMMs/{id}/models`; the web interface has their models below each.

A transparency software manufacturer states its releases itself, and a
conformity assessment body with keys its certificates, with change sets they
sign and submit to the POI API. For a conformity assessment body without keys,
which does not take part yet, the hub adds its certificates in its place:
`AddTransparencySoftwareCertificate(roamingNetworkId, bodyId, json)` in C#, or
`POST /api/v1/poi/RNs/{id}/CABs/{bodyId}/TSCs` with the certificate as POI
writes it; 201 with the commit, 400 where the body has keys of its own or the
certificate is invalid, e.g. under a legislation the body is not `notifiedFor`,
409 where the network has it already; `poi:edit`. Such a certificate is a
statement of the hub as the roaming network operator, not of the body, and says
so: the hub gives it `"statedBy": "roamingNetworkOperator"`. The POI
API lists them at `/poi/RNs/{id}/TSMs` and `/poi/RNs/{id}/CABs`, their releases
and certificates below them, `…/TSMs/{id}/TSs` and `…/CABs/{id}/TSCs`; the web
interface has a section for each.

A key of the operator is replaced by a new one in every roaming network it
operates (`RoamingHub.RoamingNetworkOperator.Changes.cs`):
`RotateNetworkOperatorKey(Signing | Identity)` in C#,
`POST /api/v1/poi/roamingNetworkOperator/signingKey/rotate` or
`…/identityKey/rotate`, or `roamingNetworkOperator rotate signingKey` (or
`identityKey`) at the command line. Each roaming network gets a change set the
key replaced signs - a signing key by the old signing key, an identity key by
the old identity key and, for the key statement made anew, the signing key - and its commits are signed by the new signing key from
then on; `operator.json` names the new key, so every roaming network made
afterwards does. The old key is forgotten, its file deleted, once no roaming
network the hub serves names it any more. A roaming network whose history
refuses the change keeps it, and so do all while a directory below `poi/` could
not be read at the start, whose network may name it: it is kept beside the
current one as `signing-key-` or `identity-key-` and the first 16 hex digits of
its identification, read back at every start, and signs for whoever still
names it. The operator is renamed the same way, `RenameNetworkOperator(name)`,
`PUT /api/v1/poi/roamingNetworkOperator/name` with `{"name": "…"}`, or
`roamingNetworkOperator name <name>`; a network called so already gets no
commit. 200 with what became of it in each roaming network and the operator as
it is now, 207 where a roaming network refused it. Both are `poi:edit`.

Everything else in a roaming network comes as change sets, which the hub takes
from everybody, over HTTP and over the WebSocket of its history: what a change
set may change is decided by the signatures of its owners, which every commit
checks against the keys in the network, not by who sent it. Every commit the
history takes, a merge as well, is in the log, tagged `poi` and `commit`.

The **runtime status** - whether an EVSE is charging, the power it delivers,
the price shown right now - is no change set and signed by nobody, so it is
decided by who sends it (`RoamingHub.POIStatus.cs`). It is set with
`setStatus` over the WebSocket of a history,
`ws://…/poi/RNs/{RN_Id}/history/ws`, and only for what a charging station
operator owns - the operator itself, its charging pools, stations, EVSEs,
tariffs and their groups, whose identifications name it: `DE*GEF*E1*1` is
DE*GEF's. Who may:

| Who | Shows | May set the status of |
|---|---|---|
| a peer of the hub | the token it calls the hub with over OCPI, as `Authorization: Token …` (base64 encoded or as it is, as the OCPI side takes it) or `Authorization: Bearer …` | its parties with the role CPO, DE/GEF as `DE*GEF`, as the OCPI library maps a CPO to WWCP - while the library allows its token and the party and nobody suspended it here; an EMSP sets nothing |
| an account | its API key, as `API-Key: …`, as on the JSON API | the operators the `poi` section binds its roles to, where the role may `poi-status:edit`; with a read-only key, nothing |
| anybody else | nothing | nothing: it reads and subscribes, as everybody may read a history over HTTP |

Credentials the hub does not know - a token of no peer, a peer suspended, an
API key of no account, a password - are refused at the upgrade with 401. Not
by password and not by the cookie of the web interface: a feed is a program,
and a cookie is what any other website opening a WebSocket here would bring
along. Asked again at every status, not only at the upgrade, so a peer
suspended or removed, an API key taken back or a role that went sets nothing
from then on. Who set what, and what was refused and why, is in the log,
tagged `poi` and `status`, by the name of the peer or the account and never by
its token or key.

```json
"roles": { "gef-feed": [ "poi-status:edit" ] },
"poi":   { "statusRoles": { "gef-feed": [ "DE*GEF" ] } }
```

Both are written down on purpose: a role in `statusRoles` the hub does not
have, or one that may not `poi-status:edit`, stops the start, as it would be
a binding that quietly lets nobody set anything. No role of the hub carries
`poi-status:edit`; `systemadmin` does, as it may do everything, and sets a
status only where `statusRoles` names it as well.

A browser may open the WebSocket of a history from the hub's own pages, and
from the pages of the sites `allowedOrigins` names - `"poi": { "allowedOrigins": [
"https://map.example.org" ] }` - and from no other site's: an upgrade request
whose `Origin` is neither is refused with 403. Programs send no `Origin`. What
changes a history over HTTP takes `application/json` or `application/cbor`, and
answers anything else with 415.

A driver's app checks a charging station offline (`RoamingHub.Attestations.cs`,
SPECIFICATION.md of WWCP POI, sections 8.4 to 8.6). A roaming network made here
states the keys of its operator from its genesis on: a key statement of the
signing keys, signed by the identity key, valid for 30 days and stated anew by a
commit a week before it ends, and in the same change set that replaces a key.
Every head is attested by the signing key, again every minute once its
attestation is older than five minutes. The page of a roaming network in the web
interface shows its **trust anchor** as a QR code: `WWCP/TA-2:` and the Base45 of
its CBOR, with the name and description of the network, the thumbprints of its
identity keys and its `minConsensus`; the URI
`https://…/poi/RNs/<id>?identityKeys=<thumbprint>` to copy says the same without
name and description. Both are at `GET /poi/RNs/<id>/trustAnchor`: an
app that scanned it gets `/poi/RNs/<id>/CSs/<CS_Id>/proof` and trusts the proof by
the identity keys alone. Show it where it cannot be swapped for another one, on a
website or in documents, not stuck on a charging station.

The keys of the operator are held here, one holder. A roaming network whose
operator shares its keys with other holders as a quorum (`minConsensus` above 1,
SPECIFICATION.md of WWCP POI, section 4.1) needs their signatures as well: its
key statement and what the operator owns are signed by each holder on its own,
and the hub does not state its keys anew there alone.

Every roaming network is kept in its directory below `poi/`: a log of every
commit and status update, compacted once it has `compactAfterLogEntries`
entries (100 000 by default) - the status goes into a new base, and the commits
more than `retainCommits` revisions behind the head (1000) are dropped from it,
into `poi/<id>/archive/`, one segment per compaction. From there they are served
and proved as before, `GET /poi/RNs/<id>/history/commits/<commitId>` and
`GET /poi/RNs/<id>/history/archive`, also after a restart. `"archive": false`
drops them for good. What a store could not do, e.g. compact, is in the log,
tagged `poi` and `store`.

```json
"poi": {
  "retainCommits":              1000,
  "compactAfterLogEntries":     100000,
  "versionCheckpointInterval":  100,
  "recentVersions":             100,
  "archive":                    true,
  "statusRoles":                { "gef-feed": [ "DE*GEF" ] },
  "allowedOrigins":             [ "https://map.example.org" ]
}
```


## DATEX II and the Mobilithek

`RoamingHub.DatexII.cs` publishes the roaming networks the `datex2` section
names as DATEX II 3.7 AFIR Energy Infrastructure publications
([WWCP_DatexII](https://github.com/OpenChargingCloud/WWCP_DatexII)): the table
of the sites, stations and refill points, and their status - status, planned
status, energy status and prices - as a full status and as deltas of what
changed. A network it names that is not there yet is published the moment it
is made; one it does not name is not published at all.

To be pulled, open as AFIR data is, below
`/datex2/RNs/{roamingNetworkId}/`: `table.xml` and `status.xml` (DATEX II 3.7
XML), `table.json` and `status.json` (DATEX II 3.7 JSON), and
`mobilithek/table.json` and `mobilithek/status.json` (the JSON of the
Mobilithek profile 01-00-00). Each tells its publication time as
`Last-Modified` and answers `If-Modified-Since` with 304; before the first
publication it is 503. `"pull": false` switches them off.

To be pushed, a publication takes a `mobilithek` block: the two publications
(offers) of the data provider there - the static one takes the table, the
dynamic one the status, as snapshots and deltas - and the machine certificate
the Mobilithek issued, for the mutual TLS it asks for. With it, the defaults are
what the Mobilithek profile wants: deltas at most once a second, a full status
at the latest every 6 hours or after 3,500 deltas. A failed push is said in the
log, tagged `datex2` and `mobilithek`; after a status that could not be pushed,
the next one is a full status again.

```json
"datex2": {
  "pull": true,
  "publications": [ {
    "roamingNetworkId":           "rn1",
    "publicationCreator":         { "country": "DE", "nationalIdentifier": "DE-NAP-GraphDefined" },
    "operatorIds":                [ "DE*GEF" ],
    "language":                   "de",
    "energyStatusDeltaInterval":  60,
    "statusDeltaInterval":        1,
    "snapshotInterval":           21600,
    "maxDeltasPerSnapshot":       3500,
    "mobilithek": {
      "staticPublicationId":                          "123456",
      "dynamicPublicationId":                         "123457",
      "url":                                          "https://mobilithek.info:8443/mobilithek/api/v1.0/publication/",
      "clientCertificate":                            "certs/mobilithek.p12",
      "clientCertificatePasswordEnvironmentVariable": "MOBILITHEK_P12_PASSWORD",
      "trustedCertificates":                          "certs/mobilithek-ca.pem",
      "compress":                                     true
    }
  } ]
}
```

Only `roamingNetworkId` and `publicationCreator` are needed; `operatorIds`
limits a publication to these operators, the intervals are seconds. Files are
relative to the configuration file. The password of the machine certificate
may be in the file as `clientCertificatePassword`, better in the environment
variable named; without `trustedCertificates` the operating system checks the
certificate of the Mobilithek, which its own certification authority signed.
The section is read once at the start; the banner then tells a `DATEX II`
line for every network it names - where it is pulled and where it is pushed
to, or that it is not made yet.


## Who may do what

A role is a user group of the accounts, and what it may do is a list of
permissions, each an operation on a resource: `dns:edit`. The operations are
the node's three - `read`, `edit`, and `run` for asking a server something -
and the resources are the node's four, `configuration`, `dns`, `nts` and
`certificates`, and the hub's four:

| Resource | |
|---|---|
| `peers` | who is on this hub - read; edited, a peer added, suspended, resumed or removed, and the token it signs in with shown; run, the credentials handshake with one |
| `traffic` | what went between the peers - read, and nothing else |
| `poi` | the roaming networks of the points of interest - edited: a new one made, operated by this hub, and a party added to one, signed with the keys of this hub; reading them needs nothing, as the POI API is public, and what is in them changes by change sets their owners sign, not by a role |
| `poi-status` | the runtime status of the roaming networks - edited: set over the WebSocket of a history with an API key, and only that of the charging station operators the `poi` section binds the role to (`statusRoles`, see [The points of interest](#the-points-of-interest)) |

A hub brings three roles:

| | `viewer` | `hub` | `systemadmin` |
|---|---|---|---|
| `configuration` | read | read | everything |
| `dns`, `nts` | read | read, edit, run | everything |
| `certificates` | read | read | everything |
| `peers` | read | read | everything |
| `traffic` | - | read | everything |
| `poi` | - | - | everything |
| `poi-status` | - | - | everything, where `statusRoles` binds it |

`systemadmin` is the node's: it may do everything, the first account goes
there, and nobody but the node says what it may do, because a role that could
be narrowed could leave nobody able to put that right. `hub` is whoever runs
the hub from day to day - where it resolves names and reads the time, asking
those servers whether they work, and the traffic, because the question the
person running a hub is asked is why a CPO and an EMSP are not seeing each
other, and the answer is in the traffic. Which peers are let in, which
certificates are believed, and which roaming networks there are, stays with
the administrators: somebody who can add a peer lets a foreign system into a
room with all the others, somebody who can add a root makes this hub believe a
server nobody else would, and somebody who can make a roaming network says
whose keys rule it.

The `viewer` is the hub's own rather than the node's. The node's may read
everything, and on a hub that would include the traffic - what the peers say
to each other, which is their business passing through and not something
everybody who may look at the configuration should read.

The `roles` section of the configuration file adds roles, or says differently
what `viewer` or `hub` may do:

```json
"roles": { "support": [ "configuration:read", "dns:read", "nts:read", "traffic:read" ] }
```

A role there naming a resource a hub does not have stops the start, because a
typo would otherwise be a role that quietly grants nothing; what the file
added or changed is said in the log at every start, tagged `security`.

What an account may do is asked on every request, so taking somebody out of a
group takes effect on their next one, and a refusal names the roles that
would have been let in - "This needs the hub or systemadmin role." A stream is
one request answered for hours, so both streams ask again, before every event
and at every heartbeat: whether the session that opened one is still there,
or the API key, or the password - a new one ends it - and, for the
traffic's, whether its reader may still read the traffic. The answer no ends
the stream, and the page behind it asks the hub why: signed out goes to the
sign-in, and a role that went is said on the Traffic page rather than
retried. The status, the log and its stream are for anybody signed in; the
clock at `/api/v1/clock`, where every node has it, is `nts:read` on a hub,
as it was while it sat below the time servers' configuration.
`GET /api/v1/auth/me` lists what the account signed in may do, spelled out
resource by resource, and says whether the log is for it - on a hub, always.
The web interface shows the rest by it: a button its user may not press is
greyed out, and a menu entry is there only for a page it may open. A page
below Configuration that a role may open, where it may not read the
configuration itself, stands in the menu in Configuration's place.


## At a console somebody types at

The event log writes to the console whenever something happens, from whichever
thread it happened on. A program that reads commands on the same console hands
the log a way to write around the line being typed, so that an entry arriving
mid-word neither lands inside the command nor waits for it:

```csharp
roamingHub.ShareConsoleWith(cli.WriteBlock);   // line off, entry whole, line back
```


## The tests

```
dotnet test RoamingHubTests
npm test                     in RoamingHub/Frontend: node --test over src/**/*.test.ts,
                             with WWCP_Node's hook that finds @node/... as webpack does
npm run typecheck:test       the same files, typechecked as the page is
```

Both directions of the peering - a peer coming here, and this hub walking to a
peer that handed out a token and a versions URL - against a stub with the three
routes the credentials handshake touches; and a peer added, removed or
registered, in either direction, while the file of its version cannot be
written - 500, and nothing changed, then or at the next start, but for a
registration the peer accepted, which is kept and written down later, when
the hub stops at the latest, and where stopping it fails as well. A hub whose
stopping fails every time closes its port all the same, and a call whose
handling fails tells its caller its ids and nothing else, and the log the
rest; a line of a peers' file the next start cannot read is passed over, and
in that start's log. And the
traffic: what a call is
written down as, what a stranger's refused call is written down as, that the
hub's own JSON API is not traffic, the filter, catching up with `after`, the
permission, and a call arriving on the stream while it is open.

Beside those: the traffic's stream as a proxy sees it, saying so while the
peers are quiet, ending with the role that let it in, and ended when the hub
stops; who may do what - what each of the three roles may and may not do, the
viewer kept out of the traffic and the hub role out of the peering over the
API, a refusal naming the roles that would have been let in, a role the
configuration file adds heard by the API, and the clock read with the time
servers' permission; and what the certificate store keeps - TLS's four kinds
and none of a vehicle's, and what each of them may be told, as the store says
it. And the points of interest: the hub a roaming network operator from its
first start, its keys kept and served as their public halves, the same one
after a restart and none without its private key, and the one the
configuration names; a roaming network made by an administrator, operated by
the hub and its genesis signed by it, served and stored, and nobody signed in
making one or adding a party; one with keys, an operator, a party in it or a
path for a name refused, and a second one of the same identification in
another case; a charging station operator added by the hub and its pool
submitted by somebody not signed in, signed by the operator, and the pool
signed by another key refused; a party of no network, of no kind, without a
signing key, with a private key, with its pools or a second time refused; a
network of another operator served but not added to; and the network there
after a restart, signed by the hub again; its signing key and its identity key
replaced in every roaming network, each by a change set the old key signs, the
old key forgotten and the new ones those of the hub after a restart, an old key
kept beside the new one while a directory below `poi/` could not be read, and
after the next restart as well; the operator renamed in every roaming network
and in those made afterwards; and over HTTP, only by whoever may edit the
points of interest. And the runtime status over the WebSocket of a history: a
CPO among the peers setting that of its EVSE with its token, as `Token` and as
`Bearer`, a subscriber told and the runtime status saying so, and refused for
another operator's EVSE; a peer suspended refused on the WebSocket it opened
before and at a new upgrade, and removed as well; an EMSP among the peers
reading and setting nothing; an account whose role `statusRoles` binds to
DE*GEF setting DE*GEF's and not DE*QAC's with its API key, and neither its
read-only key nor an account without the role setting anything; anybody
without credentials reading and subscribing, and setting nothing; a token of
no peer, an API key of no account and a password refused at the upgrade with
401, none of them in the log; and `statusRoles` read and written again, what
is wrong in it named, and a role the hub does not have, or one that may not
`poi-status:edit`, stopping the start. And
DATEX II: the `datex2` section read and written again, pull on by default,
what is wrong in it named by its field, a network named twice refused; a
network the section names published as XML, JSON and the JSON of the
Mobilithek when it is made and after a restart, one it does not name not,
nothing served with `"pull": false`, and what the banner says of each; and
pushed to a Mobilithek on this machine over mutual TLS - its server
certificate of a certification authority only the section makes trusted, the
machine certificate a PKCS #12 file beside the configuration whose password
is in an environment variable - the table to the static publication and the
full status as a snapshot to the dynamic one. None of them asks a name server
or a time server anything. And the hub's own code is held to what the kit
holds every kind's to, SourceRules: nothing it says puts "a" or "an" in front
of a name it is handed.

What the node below does on its own - the configuration file and its
sections, the log and the console, the time servers and what they are held
to, name resolution, the certificate store, the accounts' roles, the ports,
what it was built from and its command line - is tested in WWCP_Node's own
`WWCP_Node_Tests`, against a node of no particular kind. And what every node
has to answer over HTTP alike is WWCP_Node's conformance suite,
`NodeConformanceTests` in `WWCP_Node_TestKit`, which `RoamingHubConformance`
runs against a hub - the sign-in and the roles a configuration file adds, the
start, the configuration and the commit it names for the hub's own assembly,
name resolution and the time servers, the log and its stream, stopping with
browsers watching, the certificate store and the web interface; see
[WWCP_Node's README](https://github.com/OpenChargingCloud/WWCP_Node#testing-a-kind-of-node).

The web interface has tests of its own for the traffic's stream when the
browser has given up on it: the hub asked why, and a session that is gone, a
role that went and a stream that was merely cut each answered in its own
way. And for a page the browser keeps for the way back: it lets go of the
stream while it waits there, says so rather than "live", and opens it
again when it is shown. What the pages stand on is tested where it lives,
in WWCP_Node - what a page does with a hub that does not answer and what
it then says, which line of the log shows which entry, the log's stream,
what the DNS page and the NTS page send when one server of the list is
changed and what a server is held to, and a page with something typed
into it asking before it is left, among the rest.


## Your participation

This software is Open Source under the **Affero GPL 3.0 license**.
We appreciate your participation in this ongoing project, and your help to
improve it and the e-mobility ICT in general. If you find bugs, want to
request a feature or send us a pull request, feel free to use the normal
GitHub features to do so. For this please read the Contributor License
Agreement carefully and send us a signed copy or use a similar free and
open license.
