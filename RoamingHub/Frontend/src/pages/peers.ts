import { api, type Partner, type Partners, type PartnerSpec } from '../api/client';
import { auth } from '../auth';
import { html, must, render, type HTMLFragment } from '../html';
import { logs } from '../logs/store';
import type { Page } from '../router';
import { shell } from '../shell';
import { errorMessage, field, formatSince, formatTimestamp } from '../ui';

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
 */
export const peersPage: Page = {

    title: 'Peers',

    render({ root }) {

        const content = shell(root, {
            active:    '/configuration/ocpi/partners',
            title:     'Peers',
            subtitle:  'Who is peered with this hub over OCPI, and who is on the way.',
            actions:   html`<button type="button" id="reload" class="btn small">Reload</button>`
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        must<HTMLButtonElement>(root, '#reload').addEventListener('click', () => void load());

        const mayManage = auth.can('manageRoamingPartners');

        let cancelled = false;
        let store: Partners | null = null;

        /** The token that was just made up, offered once with its instructions. */
        let justAdded: { id: string; token: string; version: string } | null = null;

        /** What the last registration said. */
        let lastRegistration: { ok: boolean; message: string } | null = null;

        /** Whether the form offers the fields for starting the peering from here. */
        let startHere = false;

        /** Whether the tokens in the list are readable or dotted out. */
        let revealTokens = false;


        function draw(): void {

            if (store === null)
                return;

            const partners = store;

            render(content, html`

                ${mayManage ? '' : html`
                    <div class="notice">
                        Signed in as ${auth.user?.roles.join(', ') ?? 'somebody'}, which may look at the peers but
                        not change them. That needs the system administrator role.
                    </div>
                `}

                ${justAdded === null ? '' : html`
                    <div class="notice ok">
                        <strong>'${justAdded.id}' was added on OCPI ${justAdded.version}. The token it signs in with is</strong>
                        <code class="password">${justAdded.token}</code><br />
                        Hand it to the peer. They fetch <code>${partners.ourVersionsURL}</code> with it and POST their
                        credentials to this hub; from then on the peering is complete. The token stays readable in the
                        list below for whoever may manage them.
                    </div>
                `}

                ${lastRegistration === null ? '' : html`
                    <div class="notice ${lastRegistration.ok ? 'ok' : 'warn'}">${lastRegistration.message}</div>
                `}

                <div class="cards">
                    ${listCard(partners)}
                    ${mayManage ? addCard(partners) : ''}
                </div>
            `);

            wire();

        }


        function listCard(partners: Partners): HTMLFragment {

            return html`
                <section class="card wide">

                    <h2>
                        <i class="fa-solid fa-handshake"></i> Peers
                        ${mayManage && partners.partners.some(partner => partner.hasOurToken) ? html`
                            <button type="button" id="reveal" class="btn small" style="margin-left:auto">
                                ${revealTokens ? 'Hide the tokens' : 'Show the tokens'}
                            </button>
                        ` : ''}
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
                                          ${partners.partners.map(partner => row(partner))}
                                      </tbody>
                                  </table>
                              </div>
                          `}

                </section>
            `;

        }


        function row(partner: Partner): HTMLFragment {

            const peering = partner.registered
                                ? html`<span class="badge ok">registered</span>`
                                : partner.canRegister
                                    ? html`<span class="badge">ready to register</span>`
                                    : html`<span class="badge warn">waiting for them</span>`;

            return html`
                <tr class="${partner.status === 'ENABLED' ? '' : 'dimmed'}">

                    <td>
                        <code>${partner.id}</code>
                        <div class="small muted">${partner.name}${partner.website ? html` &middot; <a href="${partner.website}" target="_blank" rel="noopener">${partner.website}</a>` : ''}</div>
                    </td>

                    <td>${partner.role}</td>

                    <td>${partner.version}${partner.selectedVersion && partner.selectedVersion !== partner.version ? html`<div class="small muted">they chose ${partner.selectedVersion}</div>` : ''}</td>

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
                        ${mayManage && partner.canRegister ? html`
                            <button type="button" class="btn small partner-register" data-version="${partner.version}" data-id="${partner.id}"
                                    title="Fetch their versions with the token they handed out, and POST this hub's credentials to them">
                                ${partner.registered ? 'Register again' : 'Register'}
                            </button>
                        ` : ''}
                        ${mayManage ? html`
                            <button type="button" class="btn small peer-suspension" data-party="${partner.countryCode}${partner.partyId}"
                                    data-suspend="${partner.connection === 'SUSPENDED' ? 'no' : 'yes'}"
                                    title="${partner.connection === 'SUSPENDED'
                                                ? 'Talk to this peer again, and tell the others'
                                                : 'Stop talking to this peer without forgetting it, and tell the others'}">
                                ${partner.connection === 'SUSPENDED' ? 'Resume' : 'Suspend'}
                            </button>
                        ` : ''}
                        <button type="button" class="btn small danger partner-remove" data-version="${partner.version}" data-id="${partner.id}"
                                ${mayManage ? '' : html`disabled`}>
                            Remove
                        </button>
                    </td>

                </tr>
            `;

        }


        /** A token as a cell: dotted out until revealed, "none" when there is none. */
        function token(value: string | null, present: boolean): HTMLFragment {

            if (!present)
                return html`<span class="muted">none</span>`;

            if (value === null)
                return html`<span class="muted" title="Only whoever may manage the peers sees the tokens">set</span>`;

            return revealTokens
                       ? html`<code class="token">${value}</code>`
                       : html`<code class="token muted">${'•'.repeat(Math.min(value.length, 24))}</code>`;

        }


        function addCard(partners: Partners): HTMLFragment {

            const newest = partners.versions[partners.versions.length - 1] ?? '';

            return html`
                <section class="card wide">

                    <h2><i class="fa-solid fa-plus"></i> Add a peer</h2>

                    <form id="partner-form" class="form-stack">

                        <div class="form-grid">

                            <label>OCPI version
                                <select name="version">
                                    ${partners.versions.map(version => html`
                                        <option value="${version}" ${version === newest ? html`selected` : ''}>${version}</option>
                                    `)}
                                </select>
                            </label>

                            <label>Role
                                <select name="role">
                                    ${partners.roles.map(role => html`
                                        <option value="${role}" ${role === 'CPO' ? html`selected` : ''}>${role}</option>
                                    `)}
                                </select>
                            </label>

                            <label>Country code
                                <input type="text" name="countryCode" placeholder="DE" maxlength="2" minlength="2" required
                                       pattern="[A-Za-z]{2}" style="text-transform:uppercase" />
                            </label>

                            <label>Party ID
                                <input type="text" name="partyId" placeholder="GEF" maxlength="3" minlength="3" required
                                       pattern="[A-Za-z0-9]{3}" style="text-transform:uppercase" />
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
                            <input type="checkbox" name="startHere" ${startHere ? html`checked` : ''} />
                            This hub starts the peering
                            <span class="hint">
                                Tick this when the peer has already handed out a token and a versions URL. Without
                                it, the peer is expected to register here with the token above.
                            </span>
                        </label>

                        ${startHere ? html`
                            <div class="form-grid">
                                <label>The token they handed out
                                    <input type="text" name="theirToken" maxlength="255" autocomplete="off" required />
                                </label>
                                <label>Their versions URL
                                    <input type="url" name="versionsURL" placeholder="https://cpo.example.org/ocpi/versions" maxlength="255" required />
                                </label>
                            </div>
                        ` : ''}

                        <div class="form-actions">
                            <button type="submit" class="btn primary">Add the peer</button>
                            <span id="partner-error" class="form-error" role="alert"></span>
                        </div>

                    </form>

                </section>
            `;

        }


        function wire(): void {

            content.querySelector<HTMLButtonElement>('#reveal')?.addEventListener('click', () => {
                revealTokens = !revealTokens;
                draw();
            });

            content.querySelectorAll<HTMLButtonElement>('.partner-remove').forEach(button => {
                button.addEventListener('click', () => void remove(button.dataset.version ?? '', button.dataset.id ?? ''));
            });

            content.querySelectorAll<HTMLButtonElement>('.partner-register').forEach(button => {
                button.addEventListener('click', () => void register(button, button.dataset.version ?? '', button.dataset.id ?? ''));
            });

            content.querySelectorAll<HTMLButtonElement>('.peer-suspension').forEach(button => {
                button.addEventListener('click', () => void setSuspension(
                    button,
                    button.dataset.party   ?? '',
                    button.dataset.suspend === 'yes'
                ));
            });

            const form = content.querySelector<HTMLFormElement>('#partner-form');

            if (!form)
                return;

            form.querySelector<HTMLInputElement>('[name="startHere"]')?.addEventListener('change', event => {
                startHere = (event.target as HTMLInputElement).checked;
                // The typed fields survive the redraw only if kept; the form
                // is short, so the redraw is cheap and the fields are re-read.
                draw();
                content.querySelector<HTMLInputElement>('#partner-form [name="theirToken"]')?.focus();
            });

            form.addEventListener('submit', event => {
                event.preventDefault();
                void add(form);
            });

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

            if (startHere) {
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
                startHere         = false;

                draw();

            }
            catch (problem)
            {
                if (!cancelled)
                    error.textContent = errorMessage(problem);
            }

        }


        async function register(button: HTMLButtonElement, version: string, id: string): Promise<void> {

            button.disabled     = true;
            button.textContent  = 'Registering ...';

            try
            {

                const answer = await api.ocpi.partners.register(version, id);

                if (cancelled)
                    return;

                store             = answer.partners;
                lastRegistration  = { ok: answer.ok, message: answer.message };
                justAdded         = null;

                draw();

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
                    draw();
                }
                else
                    window.alert(errorMessage(problem));

            }

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

                draw();

            }
            catch (problem)
            {
                if (!cancelled)
                {
                    window.alert(errorMessage(problem));
                    void load();
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


        async function setSuspension(button: HTMLButtonElement, partyId: string, suspend: boolean): Promise<void> {

            button.disabled = true;

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

                draw();

            }
            catch (problem)
            {
                if (!cancelled)
                {
                    window.alert(errorMessage(problem));
                    void load();
                }
            }

        }


        async function load(): Promise<void> {

            try
            {
                const partners = await api.ocpi.partners.get();

                if (cancelled)
                    return;

                store = partners;
                draw();
            }
            catch (problem)
            {
                if (!cancelled)
                    render(content, html`<div class="error-box">The roaming partners could not be loaded: ${errorMessage(problem)}</div>`);
            }

        }

        // A peer going quiet is something this hub notices by itself, without
        // anybody asking - so the page has to hear about it the same way. The
        // whole list is re-read rather than patched in place: a status change
        // is rare, and the alternative is a second copy of the merge rule.
        const stopListening = logs.onPeer(() => {
            if (!cancelled)
                void load();
        });

        void load();

        return () => {
            cancelled = true;
            stopListening();
        };

    }

};
