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
| 4 | A hub is admitted as a shareholder, is dismissed, resigns | built |
| 5 | Operating under a quorum: what waits for the others | built |
| 6 | The hubs tell each other what waits, and a configuration says what a hub takes part in | 6.1 and 6.2 built, the rest designed |


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
   signing keys of the quorum so far and for the identity key of B, and is committed once it has them (phase 4).
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
- **The trust anchor changes, and nothing leads there from the old one**: the key statement of the new identity key is
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

- **A holder named** at the operator (phase 1) ends the following: the key statement of its new identity key is
  signed by it alone, so nothing leads there from the trust anchor here. Followed again with the new trust anchor,
  after `unfollow`. The same for a trust anchor older than the copy the other server starts this hub from. An
  identity key replaced does not: it is replaced in two changes, whose key statements follow one another (phase 5).
- **A hub does not say where its roaming networks are:** the `urls` of a roaming network it makes are empty, so
  the trust anchor it serves names the URL it was asked at.
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

Built (`RoamingHub.Shareholders.cs`). Who holds the keys of the operator changes by commands, each doing what its
hub can do right then, asked for by somebody with `poi:edit`, and saying what is missing. What follows from a
command without a further decision, a hub does by itself since phase 6; asked again, each command does the same.

| At the prompt | Over HTTP, below `/api/v1/poi/shareholders/{id}` | What it does |
|---|---|---|
| `roamingNetworks <id> shareholders` | `GET` | who holds the keys, how many decide, what waits |
| `roamingNetworks <id> join [at <url>]` | `POST …/join` | at a hub following: asks; later signs the change set admitting it |
| `roamingNetworks <id> admit <holder>` | `POST …/admit/{holder}` | at every holder's hub: signs the key statement; later the change set |
| `roamingNetworks <id> dismiss <holder>` | `POST …/dismiss/{holder}` | the same, to dismiss; begun where the roaming network is made |
| `roamingNetworks <id> resign` | `POST …/dismiss/{own holder}` | at the hub leaving: signs the change set dismissing it |

**A change waits where the roaming network is made**, in memory, told to everybody at
`GET /api/v1/poi/shareholders/{id}`, as all of it is signed: the kind, the holder, the key statement of the holders
afterwards with the signatures it has, and the change set once it was made. Two things are taken from everybody
there: a request, `POST …/requests`, which proves itself - its key statement is signed by the identity key it names,
its certificate by the same - and the key statement of a change with one more signature, `POST
…/changes/{holder}/keyStatement`, each signature verified.

**Asking.** The hub following makes the request itself: its public identity key, the certificate of its signing key
as a holder of the keys of that operator, the URL of its copy if it says one, and the key statement of the identity
keys so far and its own, more than half of the holders deciding, which it signs first.

**The key statement first, then the change set.** A key statement is content of the change set that enters it, so
it is complete before that change set is made. Every holder signs it at its own hub, until the quorum before and
its own have, as the key statements follow one another. Then the hub where the roaming network is made, asked
again, makes the one change set of it - `Admit` or `Dismiss` of WWCP POI - signs it and lets it wait, as every
change set of the operator waits: for the identity keys and the signing keys of the holders before, and for the
identity key of the one asking, its proof. Whoever signs it at another hub first sees that it is exactly the change
set of the change waiting, made again from the copy there.

So one hub admitting a second: `join` there, `admit` here, and the proof of the one asking. Two admitting a third:
`join`, `admit` here, `admit` at the second, and the proof of the third. Two of three dismiss the third without
it; of two the one leaving signs the change set, with `resign`. The proof of the one asking is its signature of
the change set admitting it: its hub signs it by itself when it looks (phase 6), or at once with `join` again.

Open:

- **A new holder is not vouched for yet.** After a hub was admitted, the head is attested by the hub that made it
  only, until the others signed that commit (phase 3): until then no further hub gets a first copy, as that is
  anchored in the attestations of as many holders as decide.
- **The last but one leaving** leaves one holder stating its keys alone, as the one leaving signs no statement
  that names it no longer. Whoever follows the roaming network does not get there from the trust anchor it has, and
  needs the new one. A key statement the holders before sign although it names them no longer would close that; it
  is a change of WWCP POI.
- **What waits is kept over a restart** of the hub where the roaming network is made since phase 6, beside its
  store; before, it was forgotten unless its change set was made, which the history keeps.
- **The hub that makes the roaming network does not resign**, and a dismissal begins there.
- **The URL of a copy** is taken into the roaming network only where it is one over https, `~/RNs/{id}`; a hub
  does not say its own by itself.


## Phase 5: operating under a quorum

Built (`RoamingHub.Pending.cs`, and `Restate`, `AddKey`, `DropKey` and `RotateNetworkOperatorKeyEverywhere` in
`RoamingHub.Shareholders.cs`).

- **What waits is shown.** `roamingNetworks <id> pending` at the prompt, `GET
  /api/v1/poi/shareholders/{id}/pending` over HTTP: the change sets waiting for signatures, each with what it
  changes and what it waits for. At the hub where the roaming network is made they are those of its history; at
  another holder's hub they are read where it is followed. A party the hub of the roaming network adds waits there
  now, where it was added at once while one decided.
- **Approved, or refused.** `roamingNetworks <id> approve <change set>` signs it as one of the holders of the keys
  of the operator - with the signing key of this hub, and with its identity key where a change needs identity
  keys - and it is committed once as many signed as decide. `roamingNetworks <id> refuse <change set>` withdraws
  it, signed by this hub as an owner of one of its changes: it waits no longer, for anybody. `POST
  …/pending/{changeSet}/approve` and `…/refuse` over HTTP. Neither happens by itself.
- **The keys are stated anew together.** Where one decides, the hub states the keys of its operator anew by itself
  before their statement ends; where several decide it does not, and says so in its log, once, a week before.
  `roamingNetworks <id> restate` at every holder's hub, `POST …/restate`: the same keys and the same quorum, from
  now, as a change of the shareholders is made - the key statement first, signed by each, then the change set of
  it, made where the roaming network is made and signed by each with its signing key.
- **A key held of an operator followed is kept.** A hub replacing its own signing key or identity key forgets the
  old one only where no roaming network it serves names it: also not one it follows, whose operator it holds keys
  of with others.

- **A holder renews its signing key alone.** `roamingNetworkOperator rotate signingKey` replaces the signing key
  of this hub in every roaming network it makes and in every one it follows whose operator it holds keys of. Where
  several decide, the change is one of its own certificate only, signed by its identity key, alone, as the
  specification lets a holder renew its signing keys without the others: committed at once where the roaming
  network is made, sent there by a hub following. Its commits, its signatures and its attestations are by the new
  key from then on; the old one is kept until no roaming network names it.
- **An identity key is replaced in two changes**, as example A11 of WWCP POI does it, in every roaming network
  that names it - those this hub makes, and those it follows whose operator it holds keys of. `roamingNetworkOperator
  rotate identityKey`, `POST /api/v1/poi/roamingNetworkOperator/identityKey/rotate`: the new identity key next to
  the old one first, both of the same holder, its key statement signed by the old one, which the statement before
  names, and by the new one, its proof; then the old one out, its key statement signed with the new one, which both
  versions name, and the signing key of the hub certified by it. So the key statements follow one another, and
  whoever follows the roaming network gets from the trust anchor it has to the new keys. Where this hub decides
  alone, both changes are committed at once. Where several decide, each is a change of the shareholders, the same
  holders and the same quorum, begun by the holder itself: the key statement first, then the change set, each
  waiting for the others. They agree with `roamingNetworks <id> rekey <holder>` at their hubs, `POST
  …/rekey/{holder}`; the hub replacing its key is asked again, `rotate identityKey`, and goes on where it was.
- **The next identity key.** While it is replaced, the new key is the next one of the hub, kept in
  `identity-key-next.pem`: a roaming network that names it already is signed for with it, the others with the
  current one. It becomes the current one, and the old one is forgotten, once no roaming network this hub serves
  names the old one any more - when `rotate identityKey` is asked again and finds it so.
- **A hub signs as a shareholder with the keys a roaming network names of it**, its current ones where it names
  none yet: also while a key is replaced, or a copy has not caught up with one renewed.

Open:

- **An identity key replaced half** stays so while a roaming network does not go on: one that can not be reached,
  or whose other holders do not agree. The old key stays the current one of the hub then, and the new one is
  asked for again and again; there is no way back to the old one alone.
- **Asked for only by the holder itself.** A holder that lost its identity key is not given another one by the
  others: they dismiss it, and admit it again.
- **A change set approved is not looked at by the hub.** Whoever approves it has seen what `pending` shows; the
  hub signs what it is told to.
- **What waits at the hub where the roaming network is made is not signed by it again** when it made the change
  set itself: its signature is on it. A change set somebody else sent there, e.g. another holder, it approves like
  every holder.


## Phase 6: the hubs tell each other, and a configuration says what a hub takes part in

Steps 6.1 and 6.2 are built (`RoamingHub.ShareholdersAdvance.cs`), the rest is a design. Example A1 with three hubs
([gui/exampleA1.md](https://github.com/OpenChargingCloud/WWCP_POI/blob/master/examples/gui/exampleA1.md)) took
three people telling each other what to do next: `admit` twice at every holder's hub, `join` twice, `sign` after
every commit. Two things were missing: a hub did not learn what waits elsewhere, and what it takes part in is said
by commands, one by one.

### What a hub may do by itself

Decision 3 stays: nothing is signed for another by itself. But most of the steps of today are no decisions. A
person decides **once per change, at each hub**; what follows from that, the hub does:

| Step | Today | Then |
|---|---|---|
| A holder signs the key statement of a change | a person, `admit`, `dismiss`, `rekey`, `restate` | a person: **the decision** |
| The hub where the roaming network is made makes the change set of a key statement complete | a person, the same command again | the hub: it signed that key statement |
| A holder signs the change set of a key statement it signed | a person, the same command again | the hub: it is exactly that change, which it checks today already |
| The hub joining signs the change set admitting it | a person, `join` again | the hub: it asked |
| A holder signs the commit of a change set it signed | a person, `sign` | the hub: it approved its content |
| A holder approves a change set of the operator, e.g. a party added | a person, `approve` | a person: **the decision** |
| A holder signs a commit it signed no change set of, e.g. the pools of a charging station operator | a person, `sign` | a person, or the configuration: `signCommits` |

So two hubs admitting a third are three decisions - `join` at the third, `admit` at each of the two - where they
were eight commands, and afterwards the head is signed and attested by all three.

**Built (6.1):**

- **At once, where a command or a signature arrives.** The hub where the roaming network is made makes the change
  set of a key statement when the last signature it lacked is taken there, where it signed that key statement
  itself; and the hub that sent that signature signs the change set in the same command. So the second of two
  admitting says `admit` once, and its answer says that the change set waits for the proof of the one asking.
- **When the hub looks**, `AdvanceShareholders()`: every minute, by the timer that attests
  (`AttestationCheckEvery`), in every roaming network it makes and in every one it follows at a hub. It reads what
  waits where the roaming network is made, as the pages do, and does of the table above what is this hub's: the
  proof of a hub that asked, the change set of a key statement it signed, and the commits of change sets it signed
  - the head and up to eight commits behind it, as far as they lack its signature - which it attests from then on.
- **What waits for somebody here** is said in the log, tagged `poi` and `shareholders`, once: admitting,
  dismissing, agreeing to another identity key, stating the keys anew, and resigning.
- **Kept:** the changes waiting where a roaming network is made are written beside its store,
  `shareholderChanges.json`, whenever they change, and read at the start - those that still fit its head.
- **Told since 6.2**, below: with 6.1 alone the proof of a hub admitted and the signatures of a commit took up to
  a minute each.
- **A hub following asks every hub it follows** what waits, every minute, also where it holds no key and asked for
  none: one public read. Only where it asked, or holds keys, were enough, and takes remembering that it asked.
- **Commits of change sets signed with a key this hub replaced since** are not signed by itself: it knows its
  current keys and the ones a roaming network names of it.

### Told, and asked

Everything waiting is signed and public already, `GET /api/v1/poi/shareholders/{id}` and `…/pending`. What is
missing is that somebody looks. Hubs talk over the POI API (decision 4), and a hub following has the WebSocket of
the history open anyway:

- **A notice, not the content.** Two more topics there, pushed by the hub where the roaming network is made:
  `shareholderChanges` whenever a change of the holders is asked for, signed once more, made a change set of or
  done with, `{ "holder", "kind" }`; and `pendingChangeSets` whenever a change set waits, is signed once more,
  withdrawn or committed, `{ "changeSet" }`. A notice says that something changed; the hub told reads it as it
  does today, and verifies it as it does today. A notice forged or lost does no harm.
- **Asked as well**, as a notice is missed while a hub is not connected: after every connect, and every minute by
  the timer that attests (`AttestationCheckEvery`), where the hub holds keys of the operator or has asked for
  them. Both are built (6.1, 6.2), for every roaming network followed at a hub.
- **The other way round** stays as it is: a hub following sends its request and its signatures to the hub where
  the roaming network is made, over HTTP and the WebSocket. That hub looks at what waits whenever something was
  taken there.
- **One loop**, `AdvanceShareholders()`, at every hub, after a notice, after the timer, and after a
  command: for every change waiting it finds what is the turn of this hub, does it where the table above lets it,
  and remembers the rest as *waiting for somebody here*. Doing a step twice is harmless, as every step says today
  that it was done already.
- **Shown.** What waits for somebody here is in the log, tagged `poi` and `shareholders`, once per change, and on
  the pages: in the list of the roaming networks, and on the page of the roaming network as today. The pages ask
  again when the hub was told (the open point of *The pages*).
- **Kept.** The changes waiting at the hub where the roaming network is made are written beside its store,
  `shareholderChanges.json`, and read at the start (6.1).

In WWCP POI: the topic `pendingChangeSets` of `POI_API`, from `OnPendingChangeSet` of the history, and an event of
`RoamingNetworkPeer` for a push it does not know, which it ignores today. The topic `shareholderChanges` is added
by the hub (`POIWebSocketServer.AddTopic`), as these changes are its own.

**Built (6.2):**

- **The notice of the hub**, the topic `shareholderChanges` of the WebSocket of every history it serves, action
  `shareholderChange`, `{ "holder", "kind", "waits" }`: pushed where the roaming network is made whenever a change
  of the holders waits anew, otherwise than told last - a signature more, its change set made - or no longer.
  Beside it the topic `pendingChangeSets`, which the POI API had already, with the change set itself.
- **A hub following subscribes to both** (`RoamingNetworkPeerOptions.FurtherTopics`) and looks when it is told
  (`RoamingNetworkPeer.OnNotice`), whatever the notice says: it reads what waits as the timer does. It looks as
  well after every connect, and whenever its copy took a commit, which is how it comes to sign the commit of a
  change set it signed. A hub followed that knows none of these topics is followed without them, and asked every
  minute.
- **One look at a time, and a command is one**: what is asked for while a hub looks is looked at once more
  afterwards, and a command - `join`, `admit`, `dismiss`, `rekey`, `restate` - waits for a look at it to end. So
  the hub told of the change set its own command just had made does not sign it a second time, and the command
  says what became of it.
- **How long it takes**: in a run of three hubs on one machine, the proof of a hub admitted arrived a quarter of
  a second after the last `admit`, and the commit was signed by the others a quarter of a second later; with 6.1
  alone up to a minute each.
- **The page of a roaming network asks again by itself**, every three seconds while something waits there and
  every fifteen otherwise, while it is looked at and nobody types into it; it is drawn anew only where something
  is another thing. It is not told: a browser asks.
- **The one the others dismiss** is told so in its log, with `resign` for where it takes its signature to leave,
  and not that it should dismiss itself.
- **Not built:** what waits for somebody here is not in the list of the roaming networks yet, only in the log and
  on the page of the roaming network. `LooksWhenTold` of the hub switches the looking when told off, for tests
  that look at every step alone.

### The configuration

Read at every start, and what it says is done as if somebody had said it then; what it names no longer is not
undone, as unfollowing deletes a copy. Commands and pages stay, for what is said once.

```json
"poi": {
  "url": "https://hub-b.example/poi",
  "follow": [
    { "trustAnchor":  "https://hub-a.example/poi/RNs/QA/trustAnchor/…",
      "hold":         true,
      "signCommits":  "approved" }
  ],
  "roamingNetworks": {
    "QA": { "signCommits": "approved",
            "admit":       [ { "holder": "GammaOrg", "identityKey": "63dfb2da…" } ] }
  }
}
```

- **`url`**: where this hub is reached. It is the `urls` of the roaming networks it makes, so their trust anchors
  name it whatever they were asked at; the URL of its copy when it asks to hold keys; and what a hub admitted
  adds to the `urls`. Under a quorum a change of the `urls` is a change set waiting, like every other.
- **`follow`**: the roaming networks followed, by their trust anchors; `url` as at the prompt. One followed already
  goes on from the trust anchor it has got to.
- **`hold`**: asks to hold the keys of its operator, once it is followed, and again while it is neither admitted
  nor refused.
- **`signCommits`**, per roaming network followed or made with others: `"none"`, only when told; `"approved"`,
  the commits of change sets this hub signed, the default; `"all"`, every commit this hub took, which it verified
  to take it. `"all"` is what decision 3 left for later: a commit of a charging station operator changes what is
  its own, and the signature of a holder says that the history took it rightly, not that the holder agrees.
- **`admit`**: whom this hub admits without being asked again, named by the holder and its identity key, as a
  name alone proves nothing. It is the decision of a person, written down before. Used once: a holder dismissed
  is not admitted again by it.

Not in the configuration: dismissing, approving change sets of the operator, replacing keys.

### In which order

| Step | What | Where |
|---|---|---|
| 6.1 | The changes waiting are kept; the loop, by the timer; the steps that are no decisions | RoamingHub, built |
| 6.2 | The topics and the notices; the pages ask again | WWCP POI, RoamingHub, built |
| 6.3 | `url`, `follow`, `hold` | RoamingHub |
| 6.4 | `signCommits`, `admit` | RoamingHub |

6.1 alone ends the telling: slower, by up to a minute per step, which 6.2 takes away.

Decided: the steps of the table are no decisions, also the commit of a change set a hub signed.

To decide:

- **Whether `admit` belongs into a configuration** at all, as decision 3 kept the changes of the operator with a
  person; without it, admitting stays one command per holder.

Open after it:

- **Everything goes through the hub where the roaming network is made.** While it is not reached, nothing is
  decided; another holder taking its place is a design of its own.
- **A new holder and an old trust anchor**: a hub following by a trust anchor older than the first copy it is
  given is refused (phase 2), also one a configuration names.


## The pages

Built (`Frontend/src/pages/poiShareholders.ts`). What the command line does, the web interface does at the same
routes of the JSON API; what it shows is public, as all of it is signed, and what it does is `poi:edit`.

- **Below the list of the roaming networks:** what this hub follows, each with where it is made, whether it is
  connected, its revision and what went wrong last, and a form to follow one by its trust anchor; and the roaming
  network operator this hub is, with its keys, `Renew the signing key`, `Replace the identity key` - which says
  where it waits for the other holders, each roaming network leading to its page - its name and the holder of its
  keys.
- **On the page of a roaming network:** who holds the keys of its operator, how many of them decide, and which of
  them this hub is; what of that waits for the other holders, each with what this hub can do about it - `Admit`,
  `Agree` to a holder dismissed or to one replacing its identity key, `Resign`, `Sign` the keys stated anew; a
  holder dismissed, where the roaming network is made; the keys stated anew; the head of a roaming network followed
  signed; and, at a hub following that holds no key yet, asking to hold them. Below it the change sets waiting for
  signatures, each with what it changes and what it waits for, approved or withdrawn.
- **`GET /api/v1/poi/shareholders/{id}`** answers for a roaming network followed as well, for these pages: the
  holders of the copy, `"followed": true`, and the changes waiting as the hub where it is made tells them -
  missing where that did not answer. `"thisHub"` names the holder this hub is there, if it is one.

Open:

- **A page does not follow what happens.** It shows what was there when it was asked, and asks again after
  something was done on it, and a moment later for a copy to have followed; what another hub did meanwhile shows
  with `Reload`.
- **The next identity key** of a hub replacing its own is not shown: only that it waits, and where.
