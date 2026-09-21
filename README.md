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


## What is here

| | |
|---|---|
| The base | name resolution, the time source, the accounts, the event log, the JSON API and its event stream |
| The peering | a peer added, a token handed out, and the credentials exchanged in **either** direction |
| The traffic | every OCPI call that touched this hub, both ways, with the two parties of it - on its own page and its own stream |
| The web interface | all of the above in a browser: the traffic as it happens, the peers, the configuration and the log |
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

Reading it is its own permission - `readTraffic` - and not part of
`readConfiguration`: the configuration is what this hub is, and the traffic is
what its peers did through it.


## What it can be told

The `configuration.json` beside it, in the same shape as the EMSP's:

| Section | |
|---|---|
| `dns` | the name servers and how they are asked |
| `nts` | the time server and how often the clock is checked |
| `ocpi` | who this hub is - country code, party identification, name - and which versions it offers |

Everything in `dns` and `nts` takes effect the moment it is saved. The `ocpi`
section is read once at the start and deliberately not changeable while
running: it is what every peer wrote into its credentials, and changing it
under a live registration would not rename the hub, it would make it a second
one nobody is peered with.

The peers themselves are in none of it. The OCPI library keeps them in
append-only files of its own below an `ocpi/` directory beside the
configuration, one set per version, and reads them back at every start.


## The tests

```
dotnet test RoamingHubTests
```

Both directions of the peering - a peer coming here, and this hub walking to a
peer that handed out a token and a versions URL - against a stub with the three
routes the credentials handshake touches. And the traffic: what a call is
written down as, what a stranger's refused call is written down as, that the
hub's own JSON API is not traffic, the filter, catching up with `after`, the
permission, and a call arriving on the stream while it is open.


## Your participation

This software is Open Source under the **Affero GPL 3.0 license**.
We appreciate your participation in this ongoing project, and your help to
improve it and the e-mobility ICT in general. If you find bugs, want to
request a feature or send us a pull request, feel free to use the normal
GitHub features to do so.
