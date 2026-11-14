# A hub as a shareholder of a roaming network operator

A design, and how far it is built. Today a hub is a roaming network operator alone: it holds all keys of the
operator, and decides alone. This is how several hubs come to operate one roaming network together, each of them a
**shareholder** of its operator: one organization holding one voice among those who decide
([SPECIFICATION.md of WWCP POI](https://github.com/OpenChargingCloud/WWCP_POI/blob/master/SPECIFICATION.md),
sections 4.1 and 8.6; example A1 there plays it with three organizations in one program).

| Phase | What | State |
|---|---|---|
| 1 | The hub names itself as the holder of its keys | built |
| 2 | A hub follows a roaming network of another hub | built |
| 3 | A hub signs the commits it follows, and attests with a quorum | built |
| 4 | A hub is admitted as a shareholder, is dismissed, resigns | open |
| 5 | Operating under a quorum: what waits for the others | open |


## What is there, and what is missing

**WWCP POI has:** `RoamingShareholder`, an organization with its identity key, its signing key and the URL of its
copy; `RoamingNetwork.Admit` and `Dismiss`, the one change set by which a shareholder comes or goes; change sets
waiting for signatures (`history.CoSign`, `~/RNs/{id}/history/changeSets/pending`); signatures added to a commit
later (`TryAddSignatures`); the key statements one after the other, leading from an old trust anchor to the keys of
today; and `RoamingNetworkPeer`, by which one server follows the history of another.

**The hub lacked:** it followed no roaming network of another server, and its keys named no holder (phases 1 and
2). **It lacks still:** where a roaming network has a quorum of several holders, it states the keys of its
operator no more.

**WWCP POI lacks:** a way to sign one key statement on several machines. A key statement is content of the change
set that enters it, so it is signed completely before that change set is made.


## Decisions

1. **One keyring per hub.** A hub is one organization: its identity key and its signing keys are its own, in every
   operator it is a shareholder of. Simpler than keys per operator; a key that leaks is replaced in all of them.
2. **A holder from the first start.** A new hub names itself as the holder of its keys: what the section
   `roamingNetworkOperator` of its configuration says as `holder`, the identification of its operator otherwise. A
   hub made before names itself later, once, by replacing its keys.
3. **Nothing is signed for another by itself.** A hub signs a commit or a change set of another shareholder when
   somebody allowed to change its points of interest says so, and never on its own. Signing commits automatically
   may come later; change sets of the operator stay with a person. One thing follows from a signature and is left
   to the hub: a head whose commit it signed, when told to, it attests again and again while it is the head, as an
   attestation says no more than that it still is.
4. **Hubs talk over the POI API**, below `/poi`: everything exchanged is signed, so nothing between two hubs needs
   an account at the other.


## How hub B joins the operator of hub A

1. **A has a name.** Its keys name `AlphaOrg` as their holder (phase 1).
2. **B follows the roaming network.** It reads `QA` at A, checks it against the trust anchor of A, and follows its
   history from then on (phase 2). Its copy is one more of the `urls` of `QA`.
3. **B asks.** Its public identity key naming `BetaOrg`, the certificate of its signing key and the URL of its
   copy, signed with its identity key: the proof that it holds it, and asked.
4. **A accepts.** It states the keys of both, a quorum of two, and signs the key statement; the shareholders so far
   and B sign it as well, each at its own hub.
5. **The change set.** `Admit` makes it of the key statement, complete now; it waits for the identity keys and the
   signing keys of the quorum so far and for the identity key of B, and is committed once it has them.
6. **Both sign from then on.** A commit made at A is checked at B, which adds its signature when told to; with as
   many signatures as the quorum requires, whoever checks `QA` takes it.

Leaving is the same the other way round, by `Dismiss`: two of three dismiss the third without it, of two the one
leaving signs as well, and the last one does not leave.


## What changes for whoever runs a hub

With two deciding together, no hub adds a party, replaces a key of the operator or states its keys alone any more.
What the operator owns waits until the other one signed. So a hub shows what waits, lets it be signed or refused,
and says why something it was asked for is not there yet (phase 5).


## Phase 1: the hub names itself

Built. A key names its holder and keeps it (SPECIFICATION.md, section 2): a version naming a key with another
holder than the version before is refused. So a hub made before, whose keys name none, gets a holder by getting new
keys.

- **A new hub** makes its keys naming their holder: `"roamingNetworkOperator": { "@id": "…", "name": "…",
  "holder": "…" }` in the configuration, read at the first start only; without `holder`, the identification of the
  operator.
- **`roamingNetworkOperator holder <name>`** at the prompt, `PUT /api/v1/poi/roamingNetworkOperator/holder` with
  `{"holder": "…"}` over HTTP: a new identity key and a new signing key naming that holder, in the place of the
  current ones, in every roaming network this hub operates. One change set per roaming network, as an identity key
  certifies the signing keys of its own holder only: the identity keys, the certificates and the key statement
  together, signed by the identity key and the signing key it replaces and by the new identity key, its proof. The
  commit is signed by the new signing key.
- **A roaming network that refuses it** keeps the keys it names, which this hub keeps for it; asked again with the
  same name, the hub makes no new keys and tries that roaming network again.
- **Not where others hold keys as well.** A roaming network naming keys of its operator this hub does not hold, or a
  quorum of several, is not changed from here: a shareholder is named by itself, at its own hub.
- **A key replaced later** names the holder the current ones name.
- **The trust anchor changes**, as with an identity key replaced: the key statement of the new identity key is
  signed by it alone, as a version takes no signature by a key it names no longer. Whoever pinned the old trust
  anchor needs the new one. Leading over, as example A11 of WWCP POI does - the new keys next to the old ones first,
  the old ones out afterwards - takes two holders for a while, and a quorum of two this hub would hold alone; not
  built.


## Phase 2: a hub follows a roaming network of another

Built. A copy of a roaming network of another server, kept current over the WebSocket of its history there and
served below `/poi` like the roaming networks made here. Useful alone, as a copy elsewhere.

- **By its trust anchor.** `roamingNetworks follow <trust anchor> [at <url>]` at the prompt,
  `POST /api/v1/poi/followed` with `{"trustAnchor": "…", "url"?: "…"}` over HTTP, `FollowRoamingNetwork(…)` in C#.
  The trust anchor is the text of its QR code or its URL, as its operator hands it out; it names where the roaming
  network is, `~/RNs/{id}`, unless another URL is given. The answer waits 15 seconds for the first copy, and says
  so where there is none yet: this hub keeps trying.
- **Taken only as the trust anchor lets it be trusted** (`RoamingNetworkFollowerTrust` in WWCP POI): the first
  copy by the peer, anchored in the trust anchor; every commit after it where it is signed as the roaming network
  operator by a signing key its version certifies, by an identity key the trust anchor leads to. A roaming network
  of the same identification that others operate is not taken.
- **The trust anchor is followed.** A version stating the keys of its operator anew is taken where the keys
  trusted so far signed that, as the key statements follow one another: a holder joins, one leaves. The trust
  anchor as far as it was followed is kept beside the copy, in `followed.json` in its directory below `poi`, with
  the URL.
- **After a restart** the hub serves the copy it has and catches up.
- **Changed from here by nobody.** This hub is no shareholder of its operator. A change set and a runtime status
  sent here for a roaming network followed are refused, as they would be a commit of this copy alone; nothing of
  the copy is pushed back. Its operator's keys are not replaced or named from here.
- **`roamingNetworks unfollow <id>`**, `DELETE /api/v1/poi/followed/{id}`: followed no longer, and the copy
  deleted, as the roaming network itself is where it was followed. `roamingNetworks` and
  `GET /api/v1/poi/followed` show what is followed, whether it is connected, and what went wrong last.

Open:

- **An identity key replaced at once** at the operator, or its holder named (phase 1), ends the following: the key
  statement of the new key is signed by it alone, so nothing leads there from the trust anchor here. Followed again
  with the new trust anchor, after `unfollow`. The same for a trust anchor older than the copy the other server
  starts this hub from.
- **A hub does not say where its roaming networks are:** the `urls` of a roaming network it makes are empty, so
  the trust anchor it serves names the URL it was asked at.
- **What the pages show** of a roaming network followed, and a form to follow one.
- **OCPI and DATEX II** read the roaming networks of this hub: whether a copy is published by them is not decided,
  and DATEX II does not publish it.


## Phase 3: signing what is followed

Built.

- **A commit is signed when told to.** `roamingNetworks <id> sign [<commit>]` at the prompt,
  `POST /api/v1/poi/followed/{id}/signatures` with `{"commit"?: "…"}` over HTTP, `SignFollowedCommit(…)` in C#:
  the head of a roaming network followed, or the commit named, signed in the role of the roaming network operator
  with the signing key of this hub its operator certifies. Somebody with `poi:edit` asks for it, having looked at
  the commit; this hub signs none by itself (decision 3).
- **Sent where it is followed**, over the WebSocket of the history, and added to the copy here; from there it
  goes to everybody following. With as many holders signing as the quorum requires, whoever checks the roaming
  network takes the commit.
- **Taken by the hub of the operator** from everybody, as a signature in the role of the roaming network operator
  is one only by a signing key the version of the commit certifies: the one of another holder of its keys. Not for
  a roaming network that hub follows itself: its signatures go where it is followed.
- **Only a shareholder signs.** A hub holding no key of the operator of what it follows is told so.
- **The keyring as a shareholder** (`HubRoamingNetworkOperator`): `CommitSignerIn(network)`,
  `CertifySigningKeyFor(operator, now)`, `StateKeysOf(…)`, `CoSign(keyStatement)` and `SignAsShareholder(changeSet,
  kind)` sign with the current keys of the hub as one holder among several, whichever operator a roaming network
  names, by a `RoamingShareholder` kept inside. They are what phase 4 asks, accepts and admits with; the test of
  this phase admits the hub following by hand with them.
- `roamingNetworks <id>` says who signed its head as its operator, and how many of how many holders decide.

- **A head signed is a head attested.** An attestation says that a head is the current one, and is valid for a
  quarter of an hour; where two decide, a proof needs the attestations of two holders, and so does the first copy a
  new server following takes. Told to every quarter of an hour, nobody would keep that up. So a hub attests by
  itself the head of a roaming network followed whose commit it signed, when it was told to: at once, and again
  whenever its newest attestation of it is older than five minutes, as it does for the roaming networks it
  operates. What was decided is the commit, by somebody; saying again that it still is the head is left to the
  hub. A head nobody signed here is not attested, whatever the timer does.
- **Sent where it is followed** as well, and taken there as the signature of a commit is: `addAttestations` over
  the WebSocket of the history, `POI_API.VerifyAttestation` in WWCP POI. From there it goes to everybody
  following, and into the proofs and the first copies that server gives.

Open:

- **A new head is not attested by the others** until each of them signed its commit: between a commit and the
  signatures of the others, a proof of the head has the attestations of the hub that made it only.
- **Which commits are signed.** The head, or one named: a commit below the head that lacks a signature is not
  shown as such, and not signed with the head.
- **What a reader is told** of a commit that has not the signatures of its quorum yet.


## Phase 4: admitted, dismissed, resigning

The request of step 3 and the key statement of step 4 are new: a key statement waiting for signatures, as a change
set waits, at `~/RNs/{id}/keyStatements/pending`, in WWCP POI. From there on it is `Admit` and the change sets
waiting that are there already.

At the prompt: `roamingNetworks <id> shareholders` shows them; `… join <URL> <trust anchor>` asks; `… admit <holder>`
accepts a request; `… dismiss <holder>` and `… resign` end it. The same over HTTP and on the pages.

`RoamingShareholder` is how the hub then sees itself in a roaming network: its holder name with the identity key and
the signing key that roaming network names. It replaces what `HubRoamingNetworkOperator` signs with by hand today;
the keyring, its files and the first start stay there.


## Phase 5: operating under a quorum

What waits is shown and signed: `roamingNetworks <id> pending`, `… sign <change set>`, `… refuse <change set>`. The
key statement is stated anew before it ends by all of them, as in phase 4. A signing key is replaced by its holder
alone, an identity key by the quorum.

Open: whether a shareholder renews its signing key alone as the specification says, in the hub as it is built.
