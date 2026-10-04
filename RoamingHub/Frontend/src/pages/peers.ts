import { api, type Partner, type Partners, type PartnerSpec, type PeerPresence } from '../api/client';
import { auth } from '../auth';
import { must } from '@node/html';
import { logs } from '@node/logs/store';
import type { Page } from '@node/router';
import { mayButNot, reloadButton, shell } from '@node/shell';
import { errorMessage, field, formatSince, formatTimestamp, isChecked } from '@node/ui';
import { anyFormTypedSinceDrawn, unsaved } from '@node/unsaved';
import { html, nothing, render, repeat, type TemplateResult } from '@node/view';

/**
 * The peers: who may call this hub and be reached through it, on which OCPI
 * version, and how far the peering has got.
 *
 * A hub is a room its peers are let into, and this is the page that lets one
 * in - which is why it is behind the highest permission here. A peer that is
 * added can ask this hub about every other peer on it.
 *
 * Two ways to add one, and they are the two halves of the OCPI registration.
 * With only a token of ours the peer is expected to come here: they fetch the
 * versions with that token and POST their credentials, and the library fills
 * in the rest. With their token and their versions URL as well, this hub can
 * go to them - the Register button does exactly that, and every step of it is
 * in the log, and in the traffic.
 *
 * The token this hub made up is shown once, when the peer is added, and again
 * in the list to whoever may manage them: it is what the operator has to hand
 * over, and it opens this hub, so it goes to nobody else.
 *
 * The wire names stay "partner" - it is what the JSON API calls them and what
 * the other components call the same page. Only the word on the screen is
 * "peer", because that is what they are to a hub.
 *
 * Drawn by view.ts: a draw changes only what differs, so that a peer half
 * typed into the form below the list outlives every change of the list - a
 * peer registered, suspended or removed, one going quiet somewhere else, the
 * tokens shown or hidden - and so does its focus.
 */
export const peersPage: Page = {

    title: 'Peers',

    render({ root }) {

        const content = shell(root, {
            active:    '/configuration/ocpi/partners',
            title:     'Peers',
            subtitle:  'Who is peered with this hub over OCPI, and who is on the way.',
            // Reload throws a peer typed in and not yet added away as thoroughly
            // as leaving the page does, so it asks first - reloadButton does -
            // and then resets the forms as well: reload(), not load().
            actions:   reloadButton(() => reload())
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        const mayManage   = auth.can('peers', 'edit');

        // Starting the credentials handshake with a peer is running something
        // against it rather than changing who is on this hub, so it is asked
        // for on its own - the hub checks it on its own as well.
        const mayRegister = auth.can('peers', 'run');

        let cancelled = false;
        let store: Partners | null = null;

        /** The token that was just made up, offered once with its instructions. */
        let justAdded: { id: string; token: string; version: string } | null = null;

        /** What the last registration, suspension or resumption said. */
        let lastRegistration: { ok: boolean; message: string } | null = null;

        /** Why the list could not be read again, while it could not. */
        let listError: string | null = null;

        /** Whether the tokens in the list are readable or dotted out. */
        let revealTokens = false;

        /**
         * The buttons whose call is on its way: drawn disabled, the Register
         * one saying so, until the hub has answered. Kept here rather than on
         * the button, which a draw would leave as it was set by hand.
         */
        const busy = new Set<string>();


        /** The whole page, as it is now; what is typed into the form stays. */
        function draw(): void {

            if (store === null)
                return;

            const partners = store;

            render(content, html`

                ${mayManage ? nothing : html`
                    <div class="notice">
                        ${mayButNot('look at the peers', 'change them')}
                    </div>
                `}

                <div id="peer-notices">

                    ${listError === null ? nothing : html`
                        <div class="error-box">The peers could not be read again: ${listError}</div>
                    `}

                    ${justAdded === null ? nothing : html`
                        <div class="notice ok">
                            <strong>'${justAdded.id}' was added on OCPI ${justAdded.version}. The token it signs in with is</strong>
                            <code class="password">${justAdded.token}</code><br />
                            Hand it to the peer. They fetch <code class="path">${partners.ourVersionsURL}</code> with it and POST their
                            credentials to this hub; from then on the peering is complete. The token stays readable in the
                            list below for whoever may manage them.
                        </div>
                    `}

                    ${lastRegistration === null ? nothing : html`
                        <div class="notice ${lastRegistration.ok ? 'ok' : 'warn'}">${lastRegistration.message}</div>
                    `}

                </div>

                <div class="cards">
                    <section class="card wide" id="peer-list">${listCard(partners)}</section>
                    ${mayManage ? addCard(partners) : nothing}
                </div>
            `);

        }


        /** What is on the list's card: the peers, and what its columns say. */
        function listCard(partners: Partners): TemplateResult {

            return html`

                <h2>
                    <i class="fa-solid fa-handshake"></i> Peers
                    ${mayManage && partners.partners.some(partner => partner.hasOurToken) ? html`
                        <button type="button" id="reveal" class="btn small heading-action"
                                @click=${() => { revealTokens = !revealTokens; draw(); }}>
                            ${revealTokens ? 'Hide the tokens' : 'Show the tokens'}
                        </button>
                    ` : nothing}
                </h2>

                <p class="hint">
                    A peer that is not in this list cannot call this hub, whatever token it presents. Each peer
                    is on one OCPI version - the one it was added under, which is the one it registers on.
                </p>

                <p class="hint">
                    <b>Connection</b> and <b>Peering</b> answer different questions, and a peer can be
                    <span class="badge ok">registered</span> and <span class="chip conn OFFLINE">OFFLINE</span>
                    at the same time. Peering is what was agreed once; connection is what is happening now, and
                    it is what this hub tells every other peer over <code>hubclientinfo</code>. A peer this hub
                    has not heard from for five minutes is taken to be offline - it does not have to be asked,
                    because every OCPI call it makes is the answer.
                </p>

                ${partners.partners.length === 0
                      ? html`<p class="muted">No peer yet - this hub is a mesh of one.</p>`
                      : html`
                          <div class="table-scroll">
                              <table class="table">
                                  <thead>
                                      <tr>
                                          <th>Peer</th>
                                          <th>Role</th>
                                          <th>OCPI</th>
                                          <th>Connection</th>
                                          <th>Peering</th>
                                          <th>Their token, our token</th>
                                          <th>Their versions URL</th>
                                          <th>Added</th>
                                          <th></th>
                                      </tr>
                                  </thead>
                                  <tbody>
                                      ${repeat(partners.partners, partner => `${partner.version}/${partner.id}`, partner => row(partner))}
                                  </tbody>
                              </table>
                          </div>
                      `}

            `;

        }


        function row(partner: Partner): TemplateResult {

            const peering    = partner.registered
                                   ? html`<span class="badge ok">registered</span>`
                                   : partner.canRegister
                                       ? html`<span class="badge">ready to register</span>`
                                       : html`<span class="badge warn">waiting for them</span>`;

            const party      = `${partner.countryCode}${partner.partyId}`;
            const suspended  = partner.connection === 'SUSPENDED';
            const registers  = busy.has(`register ${partner.version}/${partner.id}`);
            const switching  = busy.has(`suspension ${party}`);

            return html`
                <tr class="${partner.status === 'ENABLED' ? '' : 'dimmed'}">

                    <td>
                        <code>${partner.id}</code>
                        <div class="small muted">${partner.name}${partner.website ? html` &middot; <a href="${partner.website}" target="_blank" rel="noopener">${partner.website}</a>` : nothing}</div>
                    </td>

                    <td>${partner.role}</td>

                    <td>${partner.version}${partner.selectedVersion && partner.selectedVersion !== partner.version ? html`<div class="small muted">they chose ${partner.selectedVersion}</div>` : nothing}</td>

                    <td>
                        <span class="chip conn ${partner.connection}" title="${connectionTitle(partner)}">${partner.connection}</span>
                        <div class="small muted">
                            ${partner.lastSeen
                                  ? html`seen ${formatSince(partner.lastSeen)}`
                                  : html`never heard from`}
                        </div>
                    </td>

                    <td>
                        ${peering}
                        <div class="small muted">
                            ${partner.status}${partner.remoteStatus ? ` · remote ${partner.remoteStatus}` : ''}
                        </div>
                    </td>

                    <td class="small">
                        <div>${token(partner.theirToken, partner.hasTheirToken)}</div>
                        <div>${token(partner.ourToken, partner.hasOurToken)}</div>
                    </td>

                    <td class="small">${partner.theirVersionsURL ?? html`<span class="muted">-</span>`}</td>

                    <td class="small muted">${formatTimestamp(partner.created)}</td>

                    <td class="right">
                        ${mayRegister && partner.canRegister ? html`
                            <button type="button" class="btn small partner-register" data-version="${partner.version}" data-id="${partner.id}"
                                    title="Fetch their versions with the token they handed out, and POST this hub's credentials to them"
                                    ?disabled=${registers}
                                    @click=${() => void register(partner.version, partner.id)}>
                                ${registers ? 'Registering ...' : partner.registered ? 'Register again' : 'Register'}
                            </button>
                        ` : nothing}
                        ${mayManage ? html`
                            <button type="button" class="btn small peer-suspension" data-party="${party}"
                                    data-suspend="${suspended ? 'no' : 'yes'}"
                                    title="${suspended
                                                ? 'Talk to this peer again, and tell the others'
                                                : 'Stop talking to this peer without forgetting it, and tell the others'}"
                                    ?disabled=${switching}
                                    @click=${() => void setSuspension(party, !suspended)}>
                                ${suspended ? 'Resume' : 'Suspend'}
                            </button>
                        ` : nothing}
                        <button type="button" class="btn small danger partner-remove" data-version="${partner.version}" data-id="${partner.id}"
                                ?disabled=${!mayManage}
                                @click=${() => void remove(partner.version, partner.id)}>
                            Remove
                        </button>
                    </td>

                </tr>
            `;

        }


        /** A token as a cell: dotted out until revealed, "none" when there is none. */
        function token(value: string | null, present: boolean): TemplateResult {

            if (!present)
                return html`<span class="muted">none</span>`;

            if (value === null)
                return html`<span class="muted" title="Only whoever may manage the peers sees the tokens">set</span>`;

            return revealTokens
                       ? html`<code class="token">${value}</code>`
                       : html`<code class="token muted">${'•'.repeat(Math.min(value.length, 24))}</code>`;

        }


        function addCard(partners: Partners): TemplateResult {

            const newest = partners.versions[partners.versions.length - 1] ?? '';

            return html`
                <section class="card wide">

                    <h2><i class="fa-solid fa-plus"></i> Add a peer</h2>

                    <form id="partner-form" class="form-stack"
                          @submit=${(event: SubmitEvent) => { event.preventDefault(); void add(event.currentTarget as HTMLFormElement); }}
                          @reset=${(event: Event) => startHere(event.currentTarget as HTMLFormElement, false)}>

                        <div class="form-grid">

                            <label>OCPI version
                                <select name="version">
                                    ${partners.versions.map(version => html`
                                        <option value="${version}" ?selected=${version === newest}>${version}</option>
                                    `)}
                                </select>
                            </label>

                            <label>Role
                                <select name="role">
                                    ${partners.roles.map(role => html`
                                        <option value="${role}" ?selected=${role === 'CPO'}>${role}</option>
                                    `)}
                                </select>
                            </label>

                            <label>Country code
                                <input type="text" name="countryCode" class="capitals" placeholder="DE" maxlength="2" minlength="2" required
                                       pattern="[A-Za-z]{2}" />
                            </label>

                            <label>Party ID
                                <input type="text" name="partyId" class="capitals" placeholder="GEF" maxlength="3" minlength="3" required
                                       pattern="[A-Za-z0-9]{3}" />
                            </label>

                            <label>Name
                                <input type="text" name="name" placeholder="Example Charging GmbH" maxlength="100" required />
                            </label>

                            <label>Website
                                <input type="url" name="website" placeholder="https://example.org" maxlength="255" />
                            </label>

                            <label>The token they will use with this hub
                                <input type="text" name="ourToken" placeholder="leave empty to make one up" maxlength="255" autocomplete="off" />
                            </label>

                        </div>

                        <label class="checkbox">
                            <input type="checkbox" name="startHere"
                                   @change=${(event: Event) => {
                                       const box = event.target as HTMLInputElement;
                                       startHere(box.form!, box.checked);
                                   }} />
                            This hub starts the peering
                            <span class="hint">
                                Tick this when the peer has already handed out a token and a versions URL. Without
                                it, the peer is expected to register here with the token above.
                            </span>
                        </label>

                        <div class="form-grid" id="start-here" hidden>
                            <label>The token they handed out
                                <input type="text" name="theirToken" maxlength="255" autocomplete="off" required disabled />
                            </label>
                            <label>Their versions URL
                                <input type="url" name="versionsURL" placeholder="https://cpo.example.org/ocpi/versions" maxlength="255" required
                                       disabled />
                            </label>
                        </div>

                        <div class="form-actions">
                            <button type="submit" class="btn primary">Add the peer</button>
                            <span id="partner-error" class="form-error" role="alert"></span>
                        </div>

                    </form>

                </section>
            `;

        }


        /**
         * The two fields for starting the peering from here, shown or hidden -
         * and, hidden, disabled as well, so that the browser does not ask for
         * them. Whether the peering starts here is the box's to say, and read
         * from it when the peer is added: kept beside it as well, the box came
         * back ticked after a Reload that had been agreed to throw the form
         * away. A reset - after a peer was added, or on Reload - hides them
         * with the box it unticks.
         */
        function startHere(form: HTMLFormElement, ticked: boolean): void {

            const fields = must<HTMLElement>(form, '#start-here');

            fields.hidden = !ticked;

            for (const input of fields.querySelectorAll<HTMLInputElement>('input'))
                input.disabled = !ticked;

            if (ticked)
                form.querySelector<HTMLInputElement>('[name="theirToken"]')?.focus();

        }


        async function add(form: HTMLFormElement): Promise<void> {

            const error = must<HTMLElement>(content, '#partner-error');
            error.textContent = '';

            const spec: PartnerSpec = {
                version:      field(form, 'version'),
                role:         field(form, 'role'),
                countryCode:  field(form, 'countryCode').toUpperCase(),
                partyId:      field(form, 'partyId').toUpperCase(),
                name:         field(form, 'name'),
                website:      field(form, 'website') || undefined,
                ourToken:     field(form, 'ourToken') || undefined
            };

            if (isChecked(form, 'startHere')) {
                spec.theirToken   = field(form, 'theirToken');
                spec.versionsURL  = field(form, 'versionsURL');
            }

            try
            {

                const answer = await api.ocpi.partners.add(spec);

                if (cancelled)
                    return;

                store             = answer.partners;
                justAdded         = { id: answer.id, token: answer.ourToken, version: answer.version };
                lastRegistration  = null;
                listError         = null;

                draw();

                // A draw leaves the form as it is typed into; this peer was
                // added, so the form is emptied for the next one.
                form.reset();

            }
            catch (problem)
            {
                if (!cancelled)
                    error.textContent = errorMessage(problem);
            }

        }


        async function register(version: string, id: string): Promise<void> {

            const key = `register ${version}/${id}`;

            busy.add(key);
            draw();

            try
            {

                const answer = await api.ocpi.partners.register(version, id);

                if (cancelled)
                    return;

                store             = answer.partners;
                lastRegistration  = { ok: answer.ok, message: answer.message };
                justAdded         = null;
                listError         = null;

            }
            catch (problem)
            {

                if (cancelled)
                    return;

                // A failed handshake answers 502 with the same shape; anything
                // else is a fault of this hub.
                const body = (problem as { body?: { ok?: boolean; message?: string; partners?: Partners } }).body;

                if (body && typeof body.message === 'string') {
                    if (body.partners)
                        store = body.partners;
                    lastRegistration = { ok: false, message: body.message };
                }
                else
                    window.alert(errorMessage(problem));

            }
            finally
            {
                busy.delete(key);
            }

            if (!cancelled)
                draw();

        }


        async function remove(version: string, id: string): Promise<void> {

            if (!window.confirm(`Remove the peer '${id}' (OCPI ${version})?\n\nIts token stops opening this hub the moment it is gone, and it drops out of the mesh for everybody else on it.`))
                return;

            try
            {

                const answer = await api.ocpi.partners.remove(version, id);

                if (cancelled)
                    return;

                store             = answer;
                justAdded         = null;
                lastRegistration  = null;
                listError         = null;

                draw();

            }
            catch (problem)
            {
                if (!cancelled)
                {
                    window.alert(errorMessage(problem));
                    void reloadList();
                }
            }

        }


        /** Why a peer is in the status it is in, in a sentence. */
        function connectionTitle(partner: Partner): string {

            switch (partner.connection) {

                case 'CONNECTED':
                    return 'Heard from within the last five minutes';

                case 'OFFLINE':
                    return partner.lastSeen
                               ? 'Registered, but has not called for over five minutes'
                               : 'Registered, but has never called this hub';

                case 'PLANNED':
                    return 'Peered, but the registration is not complete yet';

                case 'SUSPENDED':
                    return 'Switched off here; the other peers have been told';

            }

        }


        async function setSuspension(partyId: string, suspend: boolean): Promise<void> {

            const key = `suspension ${partyId}`;

            busy.add(key);
            draw();

            try
            {

                const answer = suspend
                                   ? await api.ocpi.partners.suspend(partyId)
                                   : await api.ocpi.partners.resume (partyId);

                if (cancelled)
                    return;

                store             = answer.partners;
                lastRegistration  = { ok: answer.ok, message: answer.message };
                justAdded         = null;
                listError         = null;

            }
            catch (problem)
            {
                if (!cancelled)
                {
                    window.alert(errorMessage(problem));
                    void reloadList();
                }
            }
            finally
            {
                busy.delete(key);
            }

            if (!cancelled)
                draw();

        }


        /**
         * The peers as the hub has them now, drawn over the page as it is -
         * what is typed into the form kept, as a draw keeps it. Reload empties
         * it itself.
         */
        async function load(): Promise<void> {

            try
            {
                const partners = await api.ocpi.partners.get();

                if (cancelled)
                    return;

                store      = partners;
                listError  = null;
                draw();
            }
            catch (problem)
            {
                if (!cancelled)
                    render(content, html`<div class="error-box">The roaming partners could not be loaded: ${errorMessage(problem)}</div>`);
            }

        }


        /**
         * Loaded anew - Reload - is what the hub has, the form too, which a
         * draw on its own would leave as typed.
         */
        async function reload(): Promise<void> {

            await load();

            if (!cancelled)
                content.querySelectorAll('form').forEach(form => form.reset());

        }


        /**
         * The list from the hub again: when a peer comes or goes, and after a
         * change the hub refused. A list that cannot be read again is said
         * above it, and the form below stays as it is.
         */
        async function reloadList(): Promise<void> {

            try
            {
                const partners = await api.ocpi.partners.get();

                if (cancelled)
                    return;

                store      = partners;
                listError  = null;
            }
            catch (problem)
            {
                if (cancelled)
                    return;

                listError = errorMessage(problem);
            }

            draw();

        }

        // A peer going quiet is something this hub notices by itself, without
        // anybody asking - so the page has to hear about it the same way. The
        // whole list is re-read rather than patched in place: a status change
        // is rare, and the alternative is a second copy of the merge rule.
        const stopListening = logs.onEvent<PeerPresence>('peer', () => {
            if (!cancelled)
                void reloadList();
        });

        // A peer typed into the form and not yet added is a draft like any
        // other page's: leaving the page asks first.
        const release = unsaved.heldBy(() => anyFormTypedSinceDrawn(content));

        void load();

        return () => {
            cancelled = true;
            stopListening();
            release();
        };

    }

};
