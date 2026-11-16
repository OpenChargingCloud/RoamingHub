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
| 6 | The hubs tell each other what waits, and a configuration says what a hub takes part in | built |
| 7 | Every holder's hub makes changes: none of them is the one the others need | built, but for what 7.3 leaves open |


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
  together, signed by the identity key and the signing key it replaces and by the new identity key, its proof; the
  key statement by the new identity key and by the one it replaces. The commit is signed by the new signing key.
- **A roaming network that refuses it** keeps the keys it names, which this hub keeps for it; asked again with the
  same name, the hub makes no new keys and tries that roaming network again.
- **Not where others hold keys as well.** A roaming network naming keys of its operator this hub does not hold, or a
  quorum of several, is not changed from here: a shareholder is named by itself, at its own hub.
- **A key replaced later** names the holder the current ones name.
- **The trust anchor changes, and the old one leads there**: the key statement of the new identity key is signed by
  the one it replaces as well, which a version takes although it names that key no longer (*A holder named later*,
  phase 6). Whoever pinned the old trust anchor follows the key statements to the new one.


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

- **A holder named** at the operator (phase 1) ended the following, as the key statement of its new identity key
  was signed by it alone. It is signed by the identity key it replaces as well now, which it names no longer, and
  so follows the one before (*A holder named later*, phase 6): a hub following goes on. A trust anchor from before a holder was admitted or dismissed does not: the key statements
  in between lead on from it (*An old trust anchor*, phase 6). An
  identity key replaced does not either: it is replaced in two changes, whose key statements follow one another (phase 5).
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
| `roamingNetworks <id> resign` | `POST …/dismiss/{own holder}` | at the hub leaving: signs the key statement of those who stay, and the change set dismissing it |

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
it; of two the one leaving signs the key statement of the one staying and the change set, with `resign`. The proof of the one asking is its signature of
the change set admitting it: its hub signs it by itself when it looks (phase 6), or at once with `join` again.

Open:

- **A new holder is not vouched for yet.** After a hub was admitted, the head is attested by the hub that made it
  only, until the others signed that commit (phase 3): until then no further hub gets a first copy, as that is
  anchored in the attestations of as many holders as decide.
- **The last but one leaving** signs the key statement of the one staying as well, although it names it no
  longer (*A holder named later*, phase 6): `resign` at its hub signs that key statement first, then the change set
  made of it where the roaming network is made, in the one command. So the key statement follows the one before,
  and whoever follows the roaming network is led to the one staying. Until then the key statement waits, also for
  the one staying: of two, nobody states the keys alone.
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
  specification lets a holder renew its signing keys without the others: committed at once at its own hub,
  whether it made the roaming network or follows it, and sent on from there (phase 7.1; sent to the hub where
  the roaming network is made before). Its commits, its signatures and its attestations are by the new key from
  then on; the old one is kept until no roaming network names it.
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

Built: 6.1, 6.2 and 6.4 in `RoamingHub.ShareholdersAdvance.cs`, 6.3 in `RoamingHub.POIConfigured.cs`. Example A1
with three hubs
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
  dismissing, agreeing to another identity key, stating the keys anew, resigning, and a change set of the operator
  somebody else made, to approve or refuse. And in the list of the roaming networks (*The list of the roaming
  networks*, below).
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
- **The list of the roaming networks** says what waits for somebody here: `roamingNetworks` below each roaming
  network, `GET /api/v1/poi/waiting` as `{"waiting": {"QA": [ "…" ]}}`, public, and the page of the roaming
  networks as a word beside its identification and the sentences below its description, as well as a word in the
  table of what is followed; `WaitingHere` and `WaitingHereOf(…)` in C#. Each sentence names the command that
  decides it.
  - **It is what the hub found when it looked last**, kept in memory per roaming network, by the holder a change
    is about or by the change set: a look replaces it, and what somebody decides here - `admit`, `dismiss`,
    `resign`, `rekey`, `restate`, `approve`, `refuse` - is taken out at once. A hub that could not ask where a
    roaming network is followed keeps what it knew. After a restart it is there again with the first look.
  - **When a hub looks** is as before, and the hub where a roaming network is made looks now as well as soon as a
    change of the holders waits there anew, otherwise or no longer - within moments, as one told does. A change
    set of the operator another holder sent there it finds with its next look, within a minute.
  - **A change set of the operator waits for somebody here** where this hub holds a key of the operator, did not
    sign it with its signing key, and what it waits for names the operator; not the change set of a change of the
    holders, which follows from its key statement. Neither the log nor the list said these before.
  - **Not in it:** commits a hub did not sign by itself (`signCommits`), and what waits for the *other* holders,
    which the page of the roaming network shows. The pages are not told and do not ask again by themselves here:
    the list is as current as its last load.
- `LooksWhenTold` of the hub switches the looking when told off, for tests that look at every step alone.

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
  ]
}
```

- **`url`**: where this hub is reached. It is the `urls` of the roaming networks it makes, so their trust anchors
  name it whatever they were asked at; the URL of its copy when it asks to hold keys; and what a hub admitted
  adds to the `urls`. Under a quorum a change of the `urls` is a change set waiting, like every other.
- **`follow`**: the roaming networks followed, by their trust anchors; `url` as at the prompt. One followed already
  goes on from the trust anchor it has got to.
- **`hold`**: asks to hold the keys of its operator, once it is followed, and again while it is neither admitted
  nor refused.
- **`signCommits`**, per roaming network followed: `"none"`, only when told; `"approved"`,
  the commits of change sets this hub signed, the default; `"all"`, every commit this hub took, which it verified
  to take it. `"all"` is what decision 3 left for later: a commit of a charging station operator changes what is
  its own, and the signature of a holder says that the history took it rightly, not that the holder agrees.

Not in the configuration: admitting, dismissing, approving change sets of the operator, replacing keys. Whom a
hub admits was designed as `admit`, a list of holders and their identity keys, and decided against: decision 3
keeps the changes of the operator with a person, so admitting stays one command per holder.

**Built (6.3):** `url`, `follow` with `trustAnchor`, `url` and `hold`, in the `poi` section. An entry of
`follow` saying anything else is refused.

- **`url`** is a URL over http or https without a query. A roaming network made from then on names
  `{url}/RNs/{RN_Id}` as its `urls` from its genesis; every trust anchor served, of a roaming network made here or
  of a copy, names it first (`POI_API.PublicURL`); and a hub asking to hold keys says it as the URL of its copy,
  where it is https and nobody said another one.
- **Not built of `url`:** a roaming network made before does not get it into its `urls` - that were a change set,
  waiting for the others under a quorum - but its trust anchor names it all the same.
- **`follow`** is done after the start, in the background, `FollowAsConfigured()`: what is followed already is left
  as it is, and a roaming network this hub makes itself is not followed, which its log says. The trust anchor names
  where, unless `url` of the entry does.
- **`hold`** asks when the hub looks - after it connected, when told, every minute - where it holds no key there
  and did not ask yet. That it asked is kept beside the copy, `askedToHold` in `followed.json`: so it asks once, and
  a hub dismissed does not ask again by itself. Deviating from the design, which had it ask again while it is
  neither admitted nor refused: nothing says that a hub was refused, so it would ask forever. Following no longer
  deletes the copy and with it that it asked.
- **What could not be done** - no holder named, the other hub away - is said in the log once, tagged `config`, and
  tried again at the next look.
- **A trust anchor older than the roaming network** is followed all the same (*An old trust anchor*, below): a
  configuration written while one held the keys follows a roaming network two hold by now. One that leads
  nowhere is refused; the roaming network counts as followed then, without a copy, so another trust anchor in
  the configuration is not taken until somebody says `roamingNetworks unfollow <id>`.
- **Read at the start only**: a section changed while the hub runs is done at its next start.

**Built (6.4):** `signCommits` beside the trust anchor of an entry of `follow`: `"none"`, `"approved"` or `"all"`,
and `"approved"` where nothing is said, also of a roaming network followed that the section does not name.

- **When the hub looks**, as in 6.1: the head and up to eight commits behind it, as far as they lack its
  signature. `"approved"` signs those whose change set carries a signature of this hub, as before; `"none"` signs
  nothing by itself, also not those; `"all"` signs every one, which the log says with *As the configuration says*.
- **`"all"` goes back to the last commit this hub signed or approved**, and not further: the commit admitting it
  carries its proof, so nothing from before it held a key is signed by it. A commit lying behind one it approved
  and taken in the same look is left out by that; `sign <commit>` signs it.
- **Signed, so attested**: with `"all"` every head is signed by this hub, and so attested by it every five
  minutes, without anybody saying so. With `"none"` no head is, until somebody says `sign`.
- **Told to, a hub signs whatever it is**, with `roamingNetworks <id> sign [<commit>]` and on the page, whatever
  `signCommits` says.
- **Not for a roaming network made here**, deviating from the design: the hub where it is made signs every
  commit it makes, as it did before anybody else held a key, and a commit without that signature were none. So
  there is no `roamingNetworks` beside `follow`. Since phase 7.1 it takes commits of the other holders' hubs,
  which it signs as `"approved"` says.
- **Not shown**: which of the three a hub does is in its configuration only, not in its log and not on the
  page of the roaming network.

### In which order

| Step | What | Where |
|---|---|---|
| 6.1 | The changes waiting are kept; the loop, by the timer; the steps that are no decisions | RoamingHub, built |
| 6.2 | The topics and the notices; the pages ask again | WWCP POI, RoamingHub, built |
| 6.3 | `url`, `follow`, `hold` | RoamingHub, WWCP POI, built |
| 6.4 | `signCommits` | RoamingHub, built |

6.1 alone ends the telling: slower, by up to a minute per step, which 6.2 takes away.

Decided: the steps of the table are no decisions, also the commit of a change set a hub signed; and `admit` is not
in a configuration.

Open after it:

- **Everything goes through the hub where the roaming network is made.** While it is not reached, nothing is
  decided: phase 7, below, is its design.

### A holder named later

Built (`NameNetworkOperatorHolder`, `HubRoamingNetworkOperator.CoSignWith`; in WWCP POI the verifiers of a key
statement). A hub names the holder of the keys of its operator by new keys, as a key keeps its holder. The key
statement of the new identity key was signed by that key alone, of another holder than the one before: nothing led
there, and whoever followed the roaming network, or held its trust anchor, needed the new one.

- **The rule changed** (SPECIFICATION.md of WWCP POI, section 8.6): a key statement may carry signatures of
  identity keys of the statement before it that it names no longer. Verified alone, such a signature is passed
  over and counts for nothing, as its key is not given; where the statements are followed one after the other,
  the one before gives the key, and the signature is what leads on. It was refused before: *the identity key
  that signed the key statement is not given*.
- **Naming a holder** signs the key statement of the new identity key with the one it replaces as well. Still one
  change set per roaming network. Two changes - the new keys next to the old ones, then the old ones out - do not
  do it: two holders decide by two, and back to one the old key had to sign a statement not naming it all the same.
- **So** a hub following goes on following, its trust anchor led to the new key, and a hub following anew by the
  trust anchor from before is led there by the key statements (*An old trust anchor*).
- **The last but one leaving** (phase 4) uses it as well: `resign` of one of two signs the key statement of the
  one staying, with the identity key the roaming network names still, before its change set is made. The way
  around it - one staying alone stating its keys alone - is gone, so a hub following goes on following there too.
  Where the others dismiss a hub without it, two of three, nothing changed: their quorum signs, and `resign`
  adds its signatures where somebody says so.

### An old trust anchor

Built (`RoamingNetworkFollowerTrust.FollowChain`, `RoamingNetworkPeerOptions.FollowKeyStatements` of WWCP POI).
A trust anchor names the holders of the day it was handed out. A hub following by it later was refused its
first copy once a holder was admitted or dismissed since: the head is attested by the holders of today, of whom
the trust anchor knows too few. A hub following already had no such trouble, as it saw every key statement in
its turn.

- **Before its first copy** a hub asks the one it follows for the key statements of the operator, the oldest
  first (`keyStatements` over the WebSocket of the history, as `GET ~/RNs/{id}/keyStatements` gives them), and
  follows them from its trust anchor: those before the first one the trust anchor takes are passed over, and
  every one after it is signed by the quorum of the one before it and by its own (SPECIFICATION.md of WWCP POI,
  section 8.6, succession over several statements). The trust anchor they lead to is the one the copy is
  anchored in, and is kept beside it, `followed.json`.
- **Nothing is trusted that was not before**: the key statements are public and prove nothing by themselves; each
  counts by the signatures of those the one before names. Where they lead nowhere - another operator's roaming
  network, an identity key replaced at once - the trust anchor stays as it is, and the copy is refused as before.
- **Also later**: a hub that starts its copy anew, e.g. in the next epoch, asks again, from the trust anchor it
  has got to.
- **As far as the hub followed knows them**: a history gives the key statements since its first commit, at most
  the latest 100. A hub that itself began from a copy, or dropped older commits, begins later: a trust anchor
  older than that is refused there, and followed where the roaming network is made.
- **Not for an app**: a driver's app verifying a proof by an old trust anchor follows the key statements itself,
  `RoamingNetworkKeyStatement.TryVerifyChain`; nothing changed there.


## Phase 7: every holder's hub makes changes

Built, see *Built (7.1)* and *Built (7.2 and 7.3)* below, each with what it leaves open. Until here one hub writes the history of a roaming network, the one where it was made, and
every other hub, also one holding keys of its operator, has a copy nobody changes from there. That is a question
of who is reached, not of whom to trust: under a quorum that hub decides nothing alone. But while it is away
nothing is decided at all, although as many holders as decide may be there.

The history itself needs no such hub. In WWCP POI everybody commits, tips that do not conflict are merged the same
way everywhere, and a key removed on one tip carries no change set of the other into the merge (SPECIFICATION.md,
section 5.3). What binds a roaming network to one hub is in the hub:

- **A copy is changed by nobody.** A hub following starts its peer with `PushCommits = false`, takes no change set
  for a roaming network followed (`AuthorizeChangeSetSubmission`), and no hub takes commits pushed to it
  (`AuthorizeCommitPush` is not set).
- **What waits is in one place.** The change sets waiting for signatures are those of the history where the
  roaming network is made, and the changes of the holders are kept there, `shareholderChanges.json`. Every other
  hub reads them there and sends its signatures there.
- **Only that hub makes a commit of signatures**, and the change set of a key statement that is complete.
- **A hub follows at one URL**, although a trust anchor names several.

Decided (2026-11-15):

1. **Holders first.** A hub writes to a roaming network where its head names a key of this hub as one of the keys
   of its operator. A hub that only follows, e.g. the one of a charging station operator changing what it owns,
   stays as it is, and is a step of its own later.
2. **No rule for which of two key statements wins.** Where more than half of the holders decide, two changes of
   the holders that exclude each other never both get there. Where half of them or fewer decide, a change of the
   holders is begun only while every holder's hub is reached and has the same head.

Decision 3 of the beginning stays: nothing is signed for another by itself. A hub committing a change set that
has the signatures it needs decides nothing, and neither does one merging two tips.

### What a hub is in a roaming network

Not where it was made, but what its head says: a **holder's hub**, where the head names a key of this hub for its
operator, or a **hub following**. A hub admitted becomes a holder's hub with the commit admitting it, a hub
dismissed one following with the commit dismissing it, without a restart and without a command. The hub where a
roaming network was made is a holder's hub like every other from then on: it resigns as the others do, a
dismissal begins at any of them, and `signCommits` is said for it as for them.

### 7.1 Several ways

- **A holder's hub is connected to every other holder's hub**: one peer for each, on the one history, at the URL
  of the copy each holder said when it asked (`RoamingShareholder`), and the `urls` of the roaming network for
  those that said none. Commits go both ways (`PushCommits`), and so does the runtime status (`PushStatus`).
- **A commit is taken from another hub as a change set is taken from everybody**: it carries the signatures of
  its owners, which the history checks as for its own. `AuthorizeCommitPush` lets it in for a roaming network
  this hub is a holder's hub of.
- **A hub following tries the next URL** of its trust anchor when the one it follows does not answer, and goes
  back to reading there. It pushes nothing, as today.
- **Tips are merged by every hub**, as WWCP POI does it, to the same merge commit everywhere. A merge commit has
  no signatures; a holder's hub signs it by itself where it signed both commits merged, and `signCommits: "all"`
  signs every one. So a head made by merging is attested by as many holders as decide, like every other.
- **Tips that conflict** stay beside each other until the owner of what conflicts says which it is, with a change
  set on one of them. Where that owner is the operator, it is in what waits for somebody here, at every holder's
  hub.
- **Attested where the holders have got to the same head.** While two hubs commit at once their heads differ for
  a moment, and no head has the attestations of the quorum until they merged: a first copy waits that long.

Useful alone: what one holder commits alone - its signing key renewed, the commits of a charging station
operator sending its change sets there - gets to everybody while any way between the hubs is open.

**Built (7.1)** (`KeepPeersOf`, `NewPeer`, `SignCommitAsHolder`, `IsMadeBySeveral` in `RoamingHub.Follow.cs`;
`SignMerges` and `AttestOnlySignedHeads` of the history in WWCP POI):

- **What a hub is** in a roaming network it follows is read from its head whenever it looks: where the head names
  a signing key held here, the commits made here are signed with it, and they and those taken here go to every
  server this hub is connected with. Its log says when that begins and ends.
- **Connected with the copies of the other holders**: those the roaming network names, its `urls` - which are
  `https` only, as WWCP POI specifies them - and those this hub was told of: `"alsoAt": [ "…/poi/RNs/QA" ]`
  beside a trust anchor in the configuration, or `FollowAlsoAt(…)` in C#, kept beside the copy in
  `followed.json`. `GET /api/v1/poi/followed` says them, `"alsoAt": [ { "url", "connected" } ]`. The hub where
  a roaming network was made connects with nobody: the others connect with it, and one connection carries both
  ways.
- **Commits of other hubs are taken** where several hold the keys of the operator and this hub is one of them
  (`AuthorizeCommitPush`), whether it made the roaming network or follows it, from everybody, as a change set
  is: each is checked as every commit. Not where a hub makes a roaming network alone, or only follows it.
- **A holder renews its signing key at its own hub**: committed in its copy, signed by its new key, and sent on
  from there. It was sent to the hub where the roaming network is made before. Nothing else is committed at a
  hub following yet: change sets and the runtime status sent there are refused as before (7.3).
- **A signature goes to every server connected**, and is taken where one of them took it; after every connect a
  hub sends its signatures of the head and the eight commits behind it once more, as one made while that server
  was away is known there only so. A holder's hub takes a signature and an attestation of another holder for
  its copy as well.
- **`sign` at every holder's hub**, also where the roaming network was made, `roamingNetworks <id> sign
  [<commit>]`, `POST /api/v1/poi/followed/{id}/signatures` and `Sign its head commit` on its page: a hub signs
  the commits it makes itself, and those of the others when told to or as `signCommits` says.
- **A merge is not signed by being made** where several hold the keys: it is signed by itself where both
  commits merged are signed here, when the hub looks, which it does with every commit - also where the roaming
  network was made. And a head is attested only where its commit is signed here, there as well. A hub holding
  the keys alone signs and attests everything at once, as before.

What changed by it, and stays so (decided 2026-11-15):

- **A commit of another holder is signed by nobody else by itself.** A holder renewing its signing key was
  committed where the roaming network is made, and signed by that hub by making it; now it is committed at the
  holder's own hub and signed by that one alone. With two of two deciding its head is attested by one until
  somebody says `sign` at the other hub, or `signCommits` says `"all"` there; a first copy waits that long.
  Decision 3 says so - nothing is signed for another by itself - and no exception is made for this one commit,
  although the hub where the roaming network is made signed it before.

Found by it:

- **Two holders renewing their signing keys at once conflict**: both replace the certificates of the operator,
  one property. Where the commit of the first one arrived, the second one is refused and says so, *prepare it
  again for the head* - its hub has its new key already, and the roaming network names the old one until it is
  asked again. Where both were committed, the two tips conflict and stay; no hub resolves that yet. The same
  holds for every two changes of one property of the operator, which 7.3 makes more of.

Not built of 7.1:

- **The runtime status both ways**, and whatever else a hub following does not take yet.
- **A hub that only follows tries the next URL** of its trust anchor: it stays with the one it follows.
- **`signCommits` for a roaming network made here**: it signs the commits of the others as `"approved"` says,
  and nothing says otherwise.
- **Telling a hub of a copy** at the prompt or on a page: the configuration and C# only.
- **The signatures of others are not passed on**: a hub sends its own again after a connect, not those it took.
- **Not tested with a merge between hubs**: that a merge is left unsigned and signed later is tested in WWCP
  POI, and that a hub signs one where it signed both commits is not tested.

### 7.2 What waits is with every holder

- **Every holder's hub keeps what waits**, in its own history and beside its own store, as the hub where the
  roaming network is made does today: nothing is read at one other hub any more, and nothing is lost with one.
- **Exchanged between the holders' hubs**, each with each: after a connect, when told, and every minute. A change
  set waiting elsewhere that this hub lacks is taken as if it was submitted here, verified as every one; where
  both have it, each takes the signatures the other has more. A change of the holders likewise, by its kind, its
  holder and the content of its key statement, with the signatures of that key statement united.
- **A signature goes to every holder's hub that is reached**, not to one. That it got to one is enough: the others
  get it from there.
- **Withdrawn** is signed, by an owner of one of its changes, so a withdrawal is passed on like a signature, and a
  hub that has it takes the change set no more.
- **Done with** where the history says so: a change set committed waits nowhere, and neither does one that fits
  the head no longer. No hub tells another that something is over.
- **Every holder's hub pushes the two notices** of phase 6.2, `shareholderChanges` and `pendingChangeSets`, and
  every one subscribes to them at each of the others.

Useful alone: what waits survives the hub it was begun at, and every hub shows it without asking another.

### 7.3 Begun and completed anywhere

- **A change set of the operator is submitted at any holder's hub**, e.g. a party added, and waits there and, by
  7.2, at the others.
- **Committed where the last signature it lacked arrives**, by that hub, signed with its signing key. Two hubs
  getting there at once make two commits of the same change set, one on each tip. To check when it is built:
  that the history takes these as the one change they are, and does not hold them for a conflict.
- **A change of the holders begins at any holder's hub**: a hub asks to hold keys at whichever it follows, and
  `dismiss`, `rekey` and `restate` begin wherever somebody says so.
- **The change set of a key statement is made by the hub that has it complete and signed it**, whichever that
  is. It is made from the key statement and the head alone, so two hubs making it from the same head make the
  same one, as a hub signing it checks today already by making it again.
- **One key statement after another.** A hub signs no second key statement following the same one while the
  first waits: somebody withdraws the first, or it is done. With more than half deciding, two that exclude each
  other so never both have the quorum, as any two such groups of holders share one.
- **Half or fewer deciding** - one of two, two of four - a change of the holders is refused while one of the
  holders' hubs is not reached or is at another head, and says which. A hub asking to hold keys names more than
  half today, so this is where somebody chose otherwise.
- **Two key statements on two tips all the same**, e.g. made before a hub knew of this rule: the tips conflict
  and stay, as every conflict of the operator does. Which of them goes on is said by the holders, not by a hub.

**Built (7.2 and 7.3)** (`ExchangePending` in `RoamingHub.Pending.cs`; `TakeChange`,
`ExchangeShareholderChanges` and `GoOnHere` in `RoamingHub.Shareholders.cs`):

- **The change sets waiting are those of the history here**, at every holder's hub: a hub following that holds a
  key of the operator takes change sets into its copy (`AuthorizeChangeSetSubmission`), shows its own with
  `pending`, and approves and refuses there. A hub that only follows reads them where it follows, as before.
- **Exchanged, each with each it is connected with**, when a hub looks, when it is told - every history tells of
  its change sets waiting - and after somebody decided something at it: a change set waiting there is taken here
  as if it was submitted here, with the signatures it has there; one waiting here is sent there where it lacks it,
  or signatures of it. The hub where the roaming network was made asks nobody: the others ask it, and send.
- **Committed where it got the last signature**, by that hub, signed with its signing key, and sent on from
  there. Two hubs getting there at once, at the same head, make the same commit: what was to be checked is
  tested in WWCP POI (`The_same_change_set_committed_at_two_servers_is_one_change`).
- **Withdrawn everywhere**: the withdrawal of somebody refusing a change set is kept by the hub, told at `GET
  /api/v1/poi/shareholders/{id}/withdrawals`, public, taken by the others when they ask, and sent to a hub where
  the change set still waits.
- **The changes of the holders wait at every holder's hub**, beside its store, `shareholderChanges.json`, also
  beside a copy; `GET /api/v1/poi/shareholders/{id}` tells those waiting here. They are exchanged like the change
  sets, after them: taken with `POST …/changes`, from everybody, as the key statement fits the head and each of
  its signatures is verified.
- **One code for every holder's hub**: a dismissal, the keys stated anew and a new identity key begin wherever
  somebody says so; the key statement is signed in what waits here; and the change set of a key statement that
  is signed by all who must is made by a hub that signed it. Told by somebody, a hub following signs first and
  tells the others, so that one of them makes the change set, as before; where none did - they are away - it
  makes it itself. A change set made elsewhere is taken with its change, and signed here by itself where this
  hub signed its key statement.
- **Two key statements of one change** begun at two hubs: what a holder asks for itself, it asks again, and the
  later one stays; what the others begin - dismissing, stating the keys anew - the earlier one, at every hub alike.
- **One key statement after another** (decision 2): a hub signs no second key statement following the same one
  while one it signed waits, and begins none; it says which one waits.
- **Half or fewer deciding does not arise**: a change of the holders is taken only where more than half of the
  holders afterwards decide, so no rule for it was needed.
- **The one leaving commits its own dismissal** where it signs last, but does not sign that commit: the version
  it leads to names its signing key no longer.
- **Dismissing the hub that made the roaming network** is begun, signed and committed by the others without it:
  played with three hubs, the first one stopped. Back again, it takes the commit dismissing it.
- **A server that did not answer is not asked again for half a minute**, while this hub is not connected with it.

Changed by it: a command at a hub following commits there, where it got the last signature, and the hub where
the roaming network was made has it a moment later. And stating the keys anew by two holders takes two commands,
not four.

Open after it:

- **The hub that made a roaming network, dismissed, keeps a copy nobody feeds**: it holds no key, so it takes no
  commits of the others, and it follows nobody. It is not turned into a hub following. It does not resign by
  itself either, as before.
- **Change sets of the operator begin where the roaming network was made**: a party added, the name of the
  operator. A hub following takes one somebody submits to its POI API, but its own commands and pages do not make
  them for a copy. The runtime status is not taken for a copy either.
- **Two changes of one property of the operator made at once** conflict, as found with 7.1: the tips stay, and no
  hub resolves them.
- **A withdrawal is kept in memory**: after a restart a hub does not pass it on any more.
- **A change set made twice**, by two hubs at once from different signatures of its key statement, waits twice:
  the first one committed, the other one fits the head no longer and is dropped.
- **Not tested**: a withdrawal passed on to a third hub, and two key statements of one change begun at two hubs.

### In WWCP POI

Nothing of its specification changes. Checked by building it: several peers on one history, each to another
server, as the hub runs them; and the change sets waiting are exchanged over HTTP, `GET` and `POST
~/RNs/{id}/history/changeSets/pending`, told of by the topic `pendingChangeSets`.

### In which order

| Step | What | Where |
|---|---|---|
| 7.1 | What a hub is, read from the head; a peer to every holder; commits both ways; merges signed | RoamingHub |
| 7.2 | What waits kept by every holder's hub, and exchanged | RoamingHub, WWCP POI |
| 7.3 | Change sets and changes of the holders begun and completed anywhere; one key statement after another | RoamingHub |

7.2 comes before 7.3: a hub completes only what it has. Built, the two came together: a history that takes a
change set commits it once it has its signatures, wherever that is.

Not designed here:

- **A hub that only follows writes** (decision 1).
- **Checkpoints and epochs**: no hub makes them today.
- **A holder's hub that is gone for good**: the others dismiss it, as today. With two of two that takes its
  signature, so of two neither may be lost.
- **The same status set at two hubs within a millisecond** may stay different on three and more, as in WWCP POI.


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
