# A hub as a shareholder of a roaming network operator

A design, and how far it is built. Today a hub is a roaming network operator alone: it holds all keys of the
operator, and decides alone. This is how several hubs come to operate one roaming network together, each of them a
**shareholder** of its operator: one organization holding one voice among those who decide
([SPECIFICATION.md of WWCP POI](https://github.com/OpenChargingCloud/WWCP_POI/blob/master/SPECIFICATION.md),
sections 4.1 and 8.6; example A1 there plays it with three organizations in one program).

| Phase | What | State |
|---|---|---|
| 1 | The hub names itself as the holder of its keys | built |
| 2 | A hub follows a roaming network of another hub | open |
| 3 | A hub signs the commits it follows, and attests with a quorum | open |
| 4 | A hub is admitted as a shareholder, is dismissed, resigns | open |
| 5 | Operating under a quorum: what waits for the others | open |


## What is there, and what is missing

**WWCP POI has:** `RoamingShareholder`, an organization with its identity key, its signing key and the URL of its
copy; `RoamingNetwork.Admit` and `Dismiss`, the one change set by which a shareholder comes or goes; change sets
waiting for signatures (`history.CoSign`, `~/RNs/{id}/history/changeSets/pending`); signatures added to a commit
later (`TryAddSignatures`); the key statements one after the other, leading from an old trust anchor to the keys of
today; and `RoamingNetworkPeer`, by which one server follows the history of another.

**The hub lacks:** it follows no roaming network of another server; its keys named no holder; and where a roaming
network has a quorum of several holders, it states the keys of its operator no more.

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
   may come later; change sets of the operator stay with a person.
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

`roamingNetworks follow <URL> <trust anchor>`: read `~/RNs/{id}` there, check it against the trust anchor, start a
history from the checked version and keep it current with a `RoamingNetworkPeer`. Served below `/poi` like the
roaming networks made here, changed from here by nobody: this hub is no shareholder of its operator. Useful alone,
as a copy elsewhere.

Open: where the trust anchor is kept and how it is replaced; what the pages show of a roaming network followed;
what happens to OCPI and DATEX II, which read the roaming networks of this hub.


## Phase 3: signing what is followed

A hub that follows a roaming network whose operator it is a shareholder of signs the commits it took, when told to,
and sends its signatures back; and it attests a head together with the others, where today it attests alone or not
at all.

Open: how a signature gets back to the server that made the commit - pushed by the peer, or asked for; what a
reader is told of a commit that has not the signatures of its quorum yet.


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
