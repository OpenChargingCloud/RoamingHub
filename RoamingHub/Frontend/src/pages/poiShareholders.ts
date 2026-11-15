/**
 * The keys of roaming network operators, as far as this hub has to do with them (SHAREHOLDERS.md): the roaming
 * network operator this hub is, the roaming networks of other servers it follows, who holds the keys of the operator
 * of a roaming network, what of that waits for the other holders, and the change sets waiting for their signatures.
 *
 * What is shown is public, as all of it is signed and told by the POI API. What is done - a key replaced, a roaming
 * network followed, a holder admitted or dismissed, a change set signed - is poi:edit, and decided by somebody
 * here, once per change: the hub does by itself only what follows from that (SHAREHOLDERS.md, phase 6).
 *
 * Each of the two parts draws itself into an element a page gives it, with a state of its own: the page is a list
 * or a roaming network, and neither is asked again because a key was renewed. What is typed into one of its forms
 * is a draft it holds itself, as long as it is there: leaving the page asks first.
 */

import { api, type FollowedNetwork, type NetworkKey, type NetworkOperator, type NetworkOperatorUpdate, type PendingChangeSet, type ShareholderChange, type Shareholders } from '../api/client';
import { textOf } from '../api/poi';
import { auth } from '../auth';
import { toURL } from '@node/basePath';
import { errorMessage, field } from '@node/ui';
import { anyFormTypedSinceDrawn, unsaved } from '@node/unsaved';
import { html, nothing, render, repeat, type TemplateResult } from '@node/view';


/** A part of a page that draws itself: asked again with the page, and let go of with it. */
export interface Mounted {
    reload:   () => void;
    release:  () => void;
}

/**
 * How long after something was done what is shown is asked for once more: a copy here follows what was committed
 * where its roaming network is made, a moment later.
 */
const followsWithin_ms = 1500;

/**
 * How often the page of a roaming network asks again by itself, while it is looked at: the hubs go on by themselves
 * with what follows from a decision (SHAREHOLDERS.md, phase 6), within moments once they are told, so what waits is
 * another thing soon; and what somebody asks for at another hub appears here without anybody doing anything here.
 */
const asksAgainWhileWaiting_ms = 3000;
const asksAgainOtherwise_ms    = 15000;

/** What something done said: what became of it, or why it was not done. */
interface Said {
    text:    string;
    failed:  boolean;
}

/** The page of a roaming network. */
function networkURL(roamingNetworkId: string): string {
    return toURL(`/poi/RNs/${encodeURIComponent(roamingNetworkId)}`);
}

/** The thumbprint of a key in short, in full as its title. */
function keyView(id: string): TemplateResult {
    return html`<div><code class="path" title=${id}>${id.length > 16 ? `${id.slice(0, 16)}…` : id}</code></div>`;
}

/** Who holds the given keys: their holders, each once; nobody named where one decides alone. */
export function holdersOf(keys: NetworkKey[] | undefined): string[] {
    return [ ...new Set((keys ?? []).map(key => key.holder ?? '')) ].filter(holder => holder.length > 0);
}

/** What a change of the holders is, in words: with who it is about after it. */
export function changeLabel(kind: ShareholderChange['kind']): string {
    switch (kind) {
        case 'admit':    return 'A new holder';
        case 'dismiss':  return 'A holder dismissed';
        case 'addKey':   return 'A new identity key, next to the old one, of';
        case 'dropKey':  return 'The old identity key out, of';
        default:         return 'The keys stated anew';
    }
}

/** What was said, below what was done. */
function saidView(id: string, said: Said | null): TemplateResult {
    return html`<p id=${id} class=${said?.failed ? 'form-error' : 'notice ok'} role=${said?.failed ? 'alert' : 'status'} ?hidden=${said === null}>${said?.text ?? ''}</p>`;
}


/**
 * The roaming networks of other servers this hub follows, with a form to follow one, and the roaming network operator
 * this hub is, with its keys replaced and itself renamed: below the list of the roaming networks.
 *
 * What is followed is shown to everybody, once there is one. The forms and the operator are for whoever may edit the
 * roaming networks, poi:edit.
 */
export function mountHubKeys(content: HTMLElement): Mounted {

    const mayEdit = auth.can('poi', 'edit');

    let followed:      FollowedNetwork[] | null      = null;
    let waiting:       Record<string, string[]>      = {};
    let operator:      NetworkOperator | null        = null;
    let update:        NetworkOperatorUpdate | null  = null;
    let followSaid:    Said | null                   = null;
    let operatorSaid:  Said | null                   = null;
    let busy                                         = false;
    let released                                     = false;
    let again:         ReturnType<typeof setTimeout> | undefined;

    /** Do it, say what became of it, and show what is there afterwards. */
    async function act(doing: () => Promise<void>, failed: (said: Said) => void): Promise<void> {

        if (busy)
            return;

        busy = true;
        draw();

        try
        {
            await doing();
        }
        catch (problem)
        {
            failed({ text: errorMessage(problem), failed: true });
        }

        busy = false;

        if (released)
            return;

        await load();

        clearTimeout(again);
        again = setTimeout(() => void load(), followsWithin_ms);

    }

    function follow(form: HTMLFormElement): void {

        // Read before anything is switched off: a field switched off is left out of its form.
        const trustAnchor  = field(form, 'trustAnchor');
        const url          = field(form, 'url');

        void act(async () => {
            const answer = await api.poi.followed.follow(trustAnchor, url || undefined);
            followSaid   = { text: answer.message ?? `The roaming network '${answer['@id']}' is followed.`, failed: false };
            form.reset();
        }, said => followSaid = said);

    }

    function unfollow(roamingNetworkId: string): void {

        if (!window.confirm(`Follow the roaming network '${roamingNetworkId}' no longer?\n\nIts copy here is deleted. The roaming network itself is where it was followed, and can be followed again by its trust anchor.`))
            return;

        void act(async () => {
            followSaid = { text: (await api.poi.followed.unfollow(roamingNetworkId)).message, failed: false };
        }, said => followSaid = said);

    }

    /** What a change of the operator said, in every roaming network: shown with where it waits and where it was refused. */
    function changed(asking: () => Promise<NetworkOperatorUpdate>): void {

        void act(async () => {
            update        = null;
            update        = await asking();
            operator      = update.operator;
            operatorSaid  = { text: update.message, failed: Object.keys(update.refused).length > 0 };
        }, said => operatorSaid = said);

    }

    function rotateIdentityKey(): void {

        if (!window.confirm('Replace the identity key of this hub by a new one?\n\nIn two changes of every roaming network that names it: the new key next to the old one, then the old one out. Where several hold the keys of the operator, each change waits until the others agreed at their hubs; asked again here, it goes on where it was.'))
            return;

        changed(() => api.poi.operator.rotate('identityKey'));

    }

    function nameHolder(form: HTMLFormElement): void {

        const holder = field(form, 'holder');

        if (!window.confirm(`Name '${holder}' as the holder of the keys of this hub?\n\nBy a new identity key and a new signing key in every roaming network it operates. The trust anchor of each is another one afterwards, and nothing leads there from the old one: whoever follows one of them, or pinned its trust anchor, needs the new one.`))
            return;

        changed(() => api.poi.operator.nameHolder(holder).then(answer => { form.reset(); return answer; }));

    }

    function rename(form: HTMLFormElement): void {
        const name = field(form, 'name');
        changed(() => api.poi.operator.rename(name).then(answer => { form.reset(); return answer; }));
    }

    function followedCard(): TemplateResult | typeof nothing {

        if (followed === null || (!mayEdit && followed.length === 0))
            return nothing;

        return html`
            <section class="card wide poi-followed">

                <h2><i class="fa-solid fa-satellite-dish"></i> Roaming networks followed</h2>

                <p class="hint">
                    Copies of roaming networks other servers make, kept current over their history there and checked
                    against the trust anchor each was followed by. Nothing in them is changed here: this hub serves
                    them as they are.
                </p>

                ${followed.length === 0
                    ? html`<div class="hint" id="followed-none">This hub follows none.</div>`
                    : html`
                        <div class="table-scroll">
                            <table class="table poi-table" id="followed-table">
                                <thead><tr><th>Roaming network</th><th>Where it is made</th><th>Connection</th><th>Revision</th>${mayEdit ? html`<th></th>` : nothing}</tr></thead>
                                <tbody>
                                    ${repeat(followed, network => network['@id'], network => html`
                                        <tr data-network=${network['@id']}>
                                            <td>
                                                <a href=${networkURL(network['@id'])}><code>${network['@id']}</code></a>
                                                ${(waiting[network['@id']]?.length ?? 0) > 0
                                                    ? html`<span class="badge warn poi-waits" title=${waiting[network['@id']]!.join('\n')}>waits for somebody here</span>`
                                                    : nothing}
                                            </td>
                                            <td><code class="path">${network.url}</code></td>
                                            <td>
                                                <span class="badge ${network.connected ? 'ok' : ''}">${network.connected ? 'connected' : 'not connected'}</span>
                                                ${network.error ? html`<div class="muted small poi-value">${network.error}</div>` : nothing}
                                            </td>
                                            <td class="poi-number">${network.revision ?? html`<span class="muted">no copy yet</span>`}</td>
                                            ${mayEdit
                                                ? html`<td><button type="button" class="btn small danger" ?disabled=${busy}
                                                                   @click=${() => unfollow(network['@id'])}>Unfollow</button></td>`
                                                : nothing}
                                        </tr>`)}
                                </tbody>
                            </table>
                        </div>`}

                ${mayEdit ? html`
                    <form id="follow-form" class="form-stack"
                          @submit=${(event: SubmitEvent) => { event.preventDefault(); follow(event.currentTarget as HTMLFormElement); }}>

                        <label>Trust anchor
                            <textarea name="trustAnchor" rows="2" spellcheck="false" required placeholder="WWCP/TA-2:... or https://hub.example/poi/RNs/QA?keys=..."></textarea>
                            <span class="hint">
                                The text of its QR code, or its URI: the identity keys of its operator, which every
                                commit taken from there is checked against. From its operator, not from the server asked.
                            </span>
                        </label>

                        <label>Where it is made
                            <input type="url" name="url" placeholder="https://hub.example/poi/RNs/QA" autocomplete="off" />
                            <span class="hint">Unless the trust anchor names it.</span>
                        </label>

                        <div class="form-actions">
                            <button type="submit" class="btn primary" ?disabled=${busy}>Follow the roaming network</button>
                        </div>

                    </form>

                    ${saidView('follow-said', followSaid)}` : nothing}

            </section>`;

    }

    /** Where a change of the operator waits, or was refused: each roaming network leading to its page. */
    function whereView(id: string, heading: string, where: Record<string, string> | undefined): TemplateResult | typeof nothing {

        const entries = Object.entries(where ?? {});

        return entries.length === 0
                   ? nothing
                   : html`
                       <div id=${id} class="poi-where">
                           <h3>${heading}</h3>
                           ${entries.map(([ roamingNetworkId, why ]) => html`
                               <div class="kv">
                                   <span class="k"><a href=${networkURL(roamingNetworkId)}><code>${roamingNetworkId}</code></a></span>
                                   <span class="poi-value">${why}</span>
                               </div>`)}
                       </div>`;

    }

    function operatorCard(): TemplateResult | typeof nothing {

        if (!mayEdit || operator === null)
            return nothing;

        const holders = holdersOf(operator.identityKeys);

        return html`
            <section class="card wide poi-operator">

                <h2><i class="fa-solid fa-key"></i> This hub as a roaming network operator</h2>

                <p class="hint">
                    Who this hub is in every roaming network it makes, and as a holder of the keys of the operator of
                    those it follows. Its identity key says who it is, and certifies its signing key, which signs what
                    it commits. The private halves never leave this hub.
                </p>

                <div class="kv-list">
                    <div class="kv"><span class="k">Identification</span><span class="poi-value"><code>${operator['@id']}</code></span></div>
                    <div class="kv"><span class="k">Name</span>
                        <span class="poi-value" id="operator-name">${textOf(operator.name) || html`<span class="muted">not named</span>`}</span></div>
                    <div class="kv"><span class="k">Holder of its keys</span>
                        <span class="poi-value" id="operator-holder">${holders.length > 0 ? holders.join(', ') : html`<span class="muted">not named</span>`}</span></div>
                    <div class="kv"><span class="k">Identity key</span>
                        <span class="poi-value" id="operator-identity-keys">${(operator.identityKeys ?? []).map(key => keyView(key['@id']))}</span></div>
                    <div class="kv"><span class="k">Signing key</span>
                        <span class="poi-value" id="operator-signing-keys">${(operator.signingKeyCertificates ?? []).map(certificate => keyView(certificate.signingKey['@id']))}</span></div>
                </div>

                <div class="form-actions">
                    <button type="button" id="rotate-signing" class="btn" ?disabled=${busy}
                            @click=${() => changed(() => api.poi.operator.rotate('signingKey'))}>Renew the signing key</button>
                    <button type="button" id="rotate-identity" class="btn" ?disabled=${busy}
                            @click=${rotateIdentityKey}>Replace the identity key</button>
                </div>

                <p class="hint">
                    A signing key is renewed by this hub alone, everywhere. An identity key is replaced in two changes
                    of each roaming network, so that whoever follows it by its old trust anchor follows on; where
                    several hold the keys, the others agree on the page of that roaming network at their hubs, and
                    "Replace the identity key" asked again goes on where it was.
                </p>

                ${saidView('operator-said', operatorSaid)}

                ${whereView('operator-waiting', 'Waiting for the other holders', update?.waiting)}
                ${whereView('operator-refused', 'Refused',                       update?.refused)}

                <form id="operator-name-form" class="form-stack"
                      @submit=${(event: SubmitEvent) => { event.preventDefault(); rename(event.currentTarget as HTMLFormElement); }}>
                    <label>New name
                        <input type="text" name="name" maxlength="100" required placeholder="GraphDefined GmbH" />
                    </label>
                    <div class="form-actions">
                        <button type="submit" class="btn" ?disabled=${busy}>Rename the operator</button>
                    </div>
                </form>

                <form id="operator-holder-form" class="form-stack"
                      @submit=${(event: SubmitEvent) => { event.preventDefault(); nameHolder(event.currentTarget as HTMLFormElement); }}>
                    <label>Holder of its keys
                        <input type="text" name="holder" maxlength="100" required placeholder="GraphDefined GmbH" autocomplete="off" />
                        <span class="hint">
                            The organization this hub is, as others holding keys of the same operator see it. Named by
                            new keys: the trust anchor of every roaming network this hub operates changes, and nothing
                            leads there from the old one.
                        </span>
                    </label>
                    <div class="form-actions">
                        <button type="submit" class="btn danger" ?disabled=${busy}>Name the holder</button>
                    </div>
                </form>

            </section>`;

    }

    function draw(): void {
        if (!released)
            render(content, html`${followedCard()}${operatorCard()}`);
    }

    async function load(): Promise<void> {

        // Each on its own: what is followed is shown although the operator could not be asked, and the other way round.
        const [ nowFollowed, nowOperator, nowWaiting ] = await Promise.all([
            api.poi.followed.get().then(answer => answer.followed, () => null),
            mayEdit ? api.poi.operator.get().catch(() => null) : Promise.resolve(null),
            api.poi.waiting().catch(() => ({}))
        ]);

        if (released)
            return;

        followed  = nowFollowed;
        waiting   = nowWaiting;
        operator  = nowOperator ?? operator;

        draw();

    }

    const letGo = unsaved.heldBy(() => anyFormTypedSinceDrawn(content));

    void load();

    return {
        reload:   () => void load(),
        release:  () => { released = true; clearTimeout(again); letGo(); }
    };

}


/**
 * Who holds the keys of the operator of a roaming network, what of that waits, and the change sets waiting for
 * signatures: on the page of the roaming network. Shown to everybody; what this hub does as one of the holders is
 * for whoever may edit the roaming networks, poi:edit.
 */
export function mountNetworkKeys(content: HTMLElement, roamingNetworkId: string): Mounted {

    const mayEdit = auth.can('poi', 'edit');

    let holders:   Shareholders | null        = null;
    let pending:   PendingChangeSet[] | null  = null;
    let said:      Said | null                = null;
    let busy                                  = false;
    let released                              = false;
    let again:     ReturnType<typeof setTimeout> | undefined;
    let watching:  ReturnType<typeof setTimeout> | undefined;
    let shown                                 = '';

    /** Ask again later, by itself: sooner while something waits. Not while somebody types, or the page is hidden. */
    function watch(): void {

        clearTimeout(watching);

        if (released)
            return;

        const waits = (holders?.changes?.length ?? 0) > 0 || (pending?.length ?? 0) > 0;

        watching = setTimeout(() => {

            if (busy || document.hidden || anyFormTypedSinceDrawn(content))
                watch();
            else
                void load();

        }, waits ? asksAgainWhileWaiting_ms : asksAgainOtherwise_ms);

        // Where a timer is more than a number, outside a browser: asking again shall keep nothing running.
        (watching as { unref?: () => void }).unref?.();

    }

    /** Do it as a holder, say what the hub says of it, and show what is there afterwards. */
    function act(doing: () => Promise<{ message: string }>): void {

        if (busy)
            return;

        busy = true;
        draw();

        void doing().
            then (answer  => { said = { text: answer.message,         failed: false }; },
                  problem => { said = { text: errorMessage(problem),  failed: true  }; }).
            then (()      => { busy = false; shown = ''; return released ? undefined : load(); }).
            then (()      => {
                      clearTimeout(again);
                      if (!released)
                          again = setTimeout(() => void load(), followsWithin_ms);
                  });

    }

    function dismiss(holder: string): void {

        const self = holder === holders?.thisHub;

        if (!window.confirm(self
                                ? `Resign as a holder of the keys of the operator of the roaming network '${roamingNetworkId}'?\n\nThis hub signs for it; it is done once as many holders signed as decide.`
                                : `Dismiss '${holder}' as a holder of the keys of the operator of the roaming network '${roamingNetworkId}'?\n\nThis hub signs for it; it is done once as many holders signed as decide.`))
            return;

        act(() => api.poi.shareholders.change(roamingNetworkId, 'dismiss', holder));

    }

    function refuse(changeSetId: string): void {

        if (!window.confirm(`Withdraw the change set '${changeSetId}'?\n\nIt waits no longer, for nobody.`))
            return;

        act(() => api.poi.shareholders.decide(roamingNetworkId, changeSetId, false));

    }

    /** What this hub can do about a change waiting, as one of the holders. */
    function changeAction(change: ShareholderChange, thisHub: string): TemplateResult {

        // What this hub asked for itself goes on where it was asked: its identity key replaced.
        if ((change.kind === 'addKey' || change.kind === 'dropKey') && change.holder === thisHub)
            return html`<span class="muted small">asked for by this hub</span>`;

        // A dismissal begins where the roaming network is made, by one who stays: who is dismissed resigns by
        // agreeing to it.
        if (change.kind === 'dismiss')
            return html`<button type="button" class="btn small danger" ?disabled=${busy}
                                @click=${() => dismiss(change.holder)}>${change.holder === thisHub ? 'Resign' : 'Agree'}</button>`;

        const [ label, doing ] = change.kind === 'admit'    ? [ 'Admit',  () => api.poi.shareholders.change(roamingNetworkId, 'admit', change.holder) ] as const
                               : change.kind === 'restate'  ? [ 'Sign',   () => api.poi.shareholders.restate(roamingNetworkId)                       ] as const
                               :                              [ 'Agree',  () => api.poi.shareholders.change(roamingNetworkId, 'rekey', change.holder) ] as const;

        return html`<button type="button" class="btn small" ?disabled=${busy} @click=${() => act(doing)}>${label}</button>`;

    }

    function shareholdersCard(): TemplateResult | typeof nothing {

        if (holders === null)
            return nothing;

        const { minConsensus, thisHub }  = holders;
        const several                    = holders.holders.length > 1;
        const acts                       = mayEdit && thisHub !== undefined;

        // A holder is dismissed where the roaming network is made, by one who stays there.
        const dismisses                  = acts && several && !holders.followed;

        return html`
            <section class="card poi-shareholders">

                <h2><i class="fa-solid fa-key"></i> Keys of the operator</h2>

                <p class="hint" id="shareholders-quorum">
                    ${several
                        ? `The keys of the roaming network operator ${holders.roamingNetworkOperator} are held by ${holders.holders.length}: ${minConsensus} of them decide together what it signs${minConsensus > 1 ? ', none of them alone' : ''}.`
                        : `The keys of the roaming network operator ${holders.roamingNetworkOperator} are held by one, deciding alone.`}
                    ${holders.followed ? 'This roaming network is a copy here: it is made by another server.' : ''}
                </p>

                <div class="table-scroll">
                    <table class="table poi-table" id="shareholders-table">
                        <thead><tr><th>Holder</th><th>Identity keys</th><th>Signing keys</th>${dismisses ? html`<th></th>` : nothing}</tr></thead>
                        <tbody>
                            ${repeat(holders.holders, holder => holder.holder, holder => html`
                                <tr data-holder=${holder.holder}>
                                    <td>${holder.holder || html`<span class="muted">not named</span>`}
                                        ${holder.holder !== '' && holder.holder === thisHub ? html`<span class="badge ok">this hub</span>` : nothing}</td>
                                    <td>${holder.identityKeys.map(keyView)}</td>
                                    <td>${holder.signingKeys.map(keyView)}</td>
                                    ${dismisses
                                        // The hub that makes the roaming network does not resign.
                                        ? html`<td>${holder.holder === '' || holder.holder === thisHub
                                                         ? nothing
                                                         : html`<button type="button" class="btn small danger" ?disabled=${busy}
                                                                        @click=${() => dismiss(holder.holder)}>Dismiss</button>`}</td>`
                                        : nothing}
                                </tr>`)}
                        </tbody>
                    </table>
                </div>

                ${holders.changes === undefined
                    ? html`<p class="notice" id="shareholders-unasked">What waits for the holders could not be asked where the roaming network is made.</p>`
                    : holders.changes.length === 0
                          ? nothing
                          : html`
                              <h3>Waiting for the other holders</h3>
                              <div class="kv-list" id="shareholders-changes">
                                  ${holders.changes.map(change => html`
                                      <div class="kv poi-change" data-kind=${change.kind} data-holder=${change.holder}>
                                          <span class="k">${changeLabel(change.kind)} <strong>${change.holder}</strong></span>
                                          <span class="poi-value">
                                              <span class="muted">its key statement signed by ${change.keyStatement.signatures?.length ?? 0},
                                                  ${change.changeSet ? html`change set <code class="path">${change.changeSet}</code>` : 'no change set yet'}</span>
                                              ${acts ? changeAction(change, thisHub!) : nothing}
                                          </span>
                                      </div>`)}
                              </div>`}

                ${acts && (several || holders.followed) ? html`
                    <div class="form-actions">
                        ${several
                            ? html`<button type="button" id="restate" class="btn" ?disabled=${busy}
                                           title="Before their statement ends: signed by as many holders as decide"
                                           @click=${() => act(() => api.poi.shareholders.restate(roamingNetworkId))}>State the keys anew</button>`
                            : nothing}
                        ${holders.followed
                            ? html`<button type="button" id="sign-head" class="btn" ?disabled=${busy}
                                           title="As one of the holders: its head is attested by this hub from then on"
                                           @click=${() => act(() => api.poi.followed.sign(roamingNetworkId))}>Sign its head commit</button>`
                            : nothing}
                    </div>` : nothing}

                ${mayEdit && holders.followed && thisHub === undefined ? html`
                    <form id="join-form" class="form-stack"
                          @submit=${(event: SubmitEvent) => {
                              event.preventDefault();
                              const url = field(event.currentTarget as HTMLFormElement, 'url');
                              act(() => api.poi.shareholders.join(roamingNetworkId, url || undefined));
                          }}>
                        <label>Where the copy of this hub is
                            <input type="url" name="url" placeholder="https://this-hub.example/poi/RNs/${roamingNetworkId}" autocomplete="off" />
                            <span class="hint">
                                Optional, over https: told in the roaming network, so that it is found here as well.
                            </span>
                        </label>
                        <div class="form-actions">
                            <button type="submit" class="btn primary" ?disabled=${busy}>Ask to hold the keys</button>
                        </div>
                        <p class="hint">
                            With the identity key of this hub. The holders there admit it at their hubs; then this
                            hub signs the change set admitting it by itself, as soon as it is told, as the proof
                            that it holds its key.
                        </p>
                    </form>` : nothing}

                ${saidView('shareholders-said', said)}

            </section>`;

    }

    function pendingCard(): TemplateResult | typeof nothing {

        if (pending === null || pending.length === 0)
            return nothing;

        const acts = mayEdit && holders?.thisHub !== undefined;

        return html`
            <section class="card poi-pending">

                <h2><i class="fa-solid fa-hourglass-half"></i> Change sets waiting for signatures</h2>

                <p class="hint">
                    Committed once everybody who owns one of their changes signed; what the operator owns, once as many
                    of the holders of its keys signed as decide. This hub does not look at what it signs: read what a
                    change set changes before approving it.
                </p>

                ${repeat(pending, waiting => waiting.changeSet['@id'], waiting => html`
                    <div class="poi-pending-set" data-change-set=${waiting.changeSet['@id']}>
                        <div class="kv-list">
                            <div class="kv"><span class="k">Change set</span>
                                <span class="poi-value"><code class="path">${waiting.changeSet['@id']}</code>
                                    ${textOf(waiting.changeSet.description)}</span></div>
                            <div class="kv"><span class="k">Changes</span>
                                <span class="poi-value">${(waiting.changeSet.changes ?? []).map(change => html`
                                    <div>${change.kind} ${change.entityType} <code>${change.entityId}</code>${change.propertyName ? ` (${change.propertyName})` : ''}</div>`)}</span></div>
                            <div class="kv"><span class="k">Signed by</span>
                                <span class="poi-value">${waiting.changeSet.signatures?.length ?? 0}</span></div>
                            <div class="kv"><span class="k">Waits for</span>
                                <span class="poi-value">${(waiting.missing ?? []).map(missing => html`<div>${missing}</div>`)}</span></div>
                        </div>
                        ${acts ? html`
                            <div class="form-actions">
                                <button type="button" class="btn small primary" data-do="approve" ?disabled=${busy}
                                        @click=${() => act(() => api.poi.shareholders.decide(roamingNetworkId, waiting.changeSet['@id'], true))}>Approve</button>
                                <button type="button" class="btn small danger" data-do="refuse" ?disabled=${busy}
                                        @click=${() => refuse(waiting.changeSet['@id'])}>Withdraw</button>
                            </div>` : nothing}
                    </div>`)}

            </section>`;

    }

    function draw(): void {
        if (!released)
            render(content, html`${shareholdersCard()}${pendingCard()}`);
    }

    async function load(): Promise<void> {

        // A roaming network without an operator has no holders, and one whose server could not be asked tells no
        // change sets waiting: no card then.
        const [ nowHolders, nowPending ] = await Promise.all([
            api.poi.shareholders.get(roamingNetworkId).catch(() => null),
            api.poi.shareholders.pending(roamingNetworkId).then(answer => answer.pendingChangeSets, () => null)
        ]);

        if (released)
            return;

        holders  = nowHolders;
        pending  = nowPending;

        // Drawn anew only where something is another thing: asked again by itself, nothing shall flicker.
        const now = JSON.stringify([ holders, pending, said, busy ]);

        if (now !== shown)
        {
            shown = now;
            draw();
        }

        watch();

    }

    const letGo = unsaved.heldBy(() => anyFormTypedSinceDrawn(content));

    void load();

    return {
        reload:   () => void load(),
        release:  () => { released = true; clearTimeout(again); clearTimeout(watching); letGo(); }
    };

}
