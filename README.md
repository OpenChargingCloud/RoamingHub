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
| Who may do what | three roles - `viewer`, `hub` and `systemadmin` - over the node's resources and the hub's two, `peers` and `traffic`, and whatever the configuration file adds |
| The web interface | all of the above in a browser: the traffic as it happens, the peers, the name servers, the time servers, the certificates and the log |
| HubClientInfo | who is on this hub and whether they can be reached, over OCPI and on the Peers page |

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
nobody is invited to address a party the hub will not forward to.

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

It is in memory and nowhere else, because how long a hub may keep its peers'
business is a question with a different answer in every jurisdiction. A
deployment that has to keep more should read the stream and put it where it
has decided to keep it.

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
| `ocpi` | who this hub is - country code, party identification, name - and which versions it offers |

The node below reads the sections every one of these programs has - `dns`,
`nts`, `certificates` and `roles` - and the hub reads its own, `ocpi`, from
the same document; each passes over what is the other's.

Everything in `dns` and `nts` takes effect the moment it is saved. The `ocpi`
section is read once at the start and deliberately not changeable while
running: it is what every peer wrote into its credentials, and changing it
under a live registration would not rename the hub, it would make it a second
one nobody is peered with.

The peers themselves are in none of it. The OCPI library keeps them in
append-only files of its own below an `ocpi/` directory beside the
configuration, one set per version, and reads them back at every start.

```json
{
  "dns": { "enabled": true, "servers": [ { "address": "9.9.9.9" } ], "useCache": true },
  "nts": { "enabled": true, "servers": [ "ptbtime1.ptb.de", "ptbtime2.ptb.de",
                                         "ptbtime3.ptb.de", "ptbtime4.ptb.de" ],
           "minServers": 2 }
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

**It asks a group, not a server.** `nts.servers` is a list, and by default it
is the PTB's four, of which `nts.minServers` - two - have to answer before the
group has a time at all. One host being rebooted does not leave this hub
without a check, and two servers that agree catch what one server cannot: one
that is wrong rather than absent. What the check reports is what the servers
that answered and authenticated agree on, with a line for each of them, so a
failure says which of the four failed and how. The time is measured against
this hub's clock and never set from it.

Every key of the `nts` section, and what it is when absent:

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
| `legalTimeAuthority` | - | who the operator says stands behind it |
| `legalTimeToleranceSeconds` | `1` | how far off the clock may be |
| `legalTimeMaxAgeSeconds` | `3600` | how old the last check may be |

Servers sharing a priority are **one band** and are asked together; a lower
priority is asked first. The four it asks by default share one, because they
are peers - putting them in separate bands would say something about them that
is not true. An entry may be a bare host name or an object saying more:
`{ "hostname": "time.local", "priority": 0, "ntsKEPort": 4460, "enabled": true }`.

Servers that disagree by more than `nts.maxDeviationSeconds` are written down
rather than acted on. The disagreement belongs in the log, and the time is
still a time.

A section naming a single `hostname` and no list becomes a group of one, which
is what every file written before there were groups says, and it keeps working.
A group of one is held to a quorum of one, and a section asking two of it is
refused. A list without `minServers` is held to two, as the default four are,
or to all of its servers when it has fewer switched on.

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

The NTS page lists every server of the group with a Test of its own - the
name, the TLS handshake and what the server's certificate claims, down to the
root CA it ends at, with the SHA-256 fingerprints of both, which are what a
pin is compared with; the key exchange and the authenticated request, each
step timed - and an Edit, where the pins go: the fingerprint the server showed
last and the certificates and roots the store keeps for it are offered with a
click. Below them is what the group is held to, and what counts as legal
time - who stands behind the servers' time, how far off the clock may be
found and how old its last check may be - on a card of its own, after whose
Save the clock is read again. "Sync now" asks the group the way the clock
check does. Neither steps the clock.

The check runs by itself every `nts.checkEverySeconds`, the first one a minute
after starting. A new interval, and switching NTS off or on, reach a running
check at once. What the clock is worth - the time, against which group it was
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
otherwise. It is the node's store, and a hub keeps the four kinds of TLS in it
and none of the seven of ISO 15118, which are a vehicle's:

| Kind | |
|---|---|
| `tlsRoot` | what a time server, or a name server over TLS or HTTPS, may chain to - kept for `nts`, `dns` or both |
| `tlsServer` | a server's own certificate, kept to hold the server to by its fingerprint - for `nts`, `dns` or both |
| `clientRoot` | what a client connecting to this hub will have to chain to - the root, or the CA that issues the clients |
| `tlsIdentity` | what this hub will present in TLS, with its private key - on every listener, as a hub names none it could be kept for |

The first two are used today; the other two are kept, and used by nothing
here yet. The peers are what they are waiting for: a hub reaches more servers
over HTTPS, and is reached by more clients, than any other kind of node.

The store is the directory: one file per certificate below it, and an
`index.json` recording what a file cannot say about itself - what somebody
calls it, whether it is switched on and, for a root or a server certificate,
what it is kept for. So a store copied to another machine arrives complete,
and a lost index costs labels, switches and usages rather than certificates.
Certificates copied into the directory by hand are adopted at the next start,
or at once with **Re-read the directory** on the Certificates page. Switching
one off leaves its file where it is; deleting it deletes the file, private key
and all. Time switches a certificate off as well, and separately: an expired
one stays listed and stops being used.

**The private keys in the store are not encrypted.** A PKCS#12 is opened with
its password once, at import, and written back without one. What guards them
is the file system - the directory is made for its owner alone where the
platform allows saying so in one call - so the store belongs on a machine
whose users are all trusted with this hub's identity. The hub says so at every
start with a key in the store, and the Certificates page says it where the
keys are listed.

| | |
|---|---|
| `GET /api/v1/certificates` | the store, grouped by kind, with what each kind is and what it may be kept for |
| `POST /api/v1/certificates` | an import: `kind`, the file base64-encoded as `content`, and `password`, `label` and `usages` where there are any |
| `POST /api/v1/certificates/reload` | read the directory again |
| `GET` / `PATCH` / `DELETE /api/v1/certificates/{id}` | one certificate; a `PATCH` sets `active`, `label` and `usages` |

Reading it is `certificates:read`, which all three roles have; changing it is
`certificates:edit`, which only a `systemadmin` has.


## Who may do what

A role is a user group of the accounts, and what it may do is a list of
permissions, each an operation on a resource: `dns:edit`. The operations are
the node's three - `read`, `edit`, and `run` for asking a server something -
and the resources are the node's four, `configuration`, `dns`, `nts` and
`certificates`, and the hub's two:

| Resource | |
|---|---|
| `peers` | who is on this hub - read; edited, a peer added, suspended, resumed or removed, and the token it signs in with shown; run, the credentials handshake with one |
| `traffic` | what went between the peers - read, and nothing else |

A hub brings three roles:

| | `viewer` | `hub` | `systemadmin` |
|---|---|---|---|
| `configuration` | read | read | everything |
| `dns`, `nts` | read | read, edit, run | everything |
| `certificates` | read | read | everything |
| `peers` | read | read | everything |
| `traffic` | - | read | everything |

`systemadmin` is the node's: it may do everything, the first account goes
there, and nobody but the node says what it may do, because a role that could
be narrowed could leave nobody able to put that right. `hub` is whoever runs
the hub from day to day - where it resolves names and reads the time, asking
those servers whether they work, and the traffic, because the question the
person running a hub is asked is why a CPO and an EMSP are not seeing each
other, and the answer is in the traffic. Which peers are let in, and which
certificates are believed, stays with the administrators: somebody who can
add a peer lets a foreign system into a room with all the others, and
somebody who can add a root makes this hub believe a server nobody else would.

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
routes the credentials handshake touches. And the traffic: what a call is
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
it. None of them asks a name server or a time server anything.

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
