import { api, type OCPIConfiguration } from '../api/client';
import { html, must, render } from '../html';
import type { Page } from '../router';
import { shell } from '../shell';
import { errorMessage, formatValue, humanizeKey } from '../ui';

/**
 * Who this hub is in OCPI, where its peers find it, and how many of them there
 * are - read-only, on purpose.
 *
 * The country code, the party identification and the versions offered are what
 * every peer wrote into its credentials; changing them under a live
 * registration would not rename the hub, it would make it a second one nobody
 * is peered with. So they are read from the configuration file at the start,
 * and this page says where that file is.
 */
export const ocpiPage: Page = {

    title: 'OCPI',

    render({ root }) {

        const content = shell(root, {
            active:    '/configuration/ocpi',
            title:     'OCPI',
            subtitle:  'Who this hub is to its peers, and where they find it.',
            actions:   html`<button type="button" id="reload" class="btn small">Reload</button>`
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        must<HTMLButtonElement>(root, '#reload').addEventListener('click', () => void load());

        let cancelled = false;

        function draw(ocpi: OCPIConfiguration): void {

            render(content, html`

                <div class="cards">

                    <section class="card">
                        <h2><i class="fa-solid fa-id-badge"></i> This hub</h2>
                        <div class="kv-list">
                            <div class="kv"><span class="k">Party</span><span class="v">${ocpi.party.id}</span></div>
                            <div class="kv"><span class="k">Country code</span><span class="v">${ocpi.party.countryCode}</span></div>
                            <div class="kv"><span class="k">Party ID</span><span class="v">${ocpi.party.partyId}</span></div>
                            <div class="kv"><span class="k">Role</span><span class="v">${ocpi.party.role}</span></div>
                            <div class="kv"><span class="k">Name</span><span class="v">${ocpi.party.name}</span></div>
                            <div class="kv"><span class="k">Website</span><span class="v">${formatValue(ocpi.party.website)}</span></div>
                            <div class="kv"><span class="k">Versions</span><span class="v">${ocpi.versions.join(', ')}</span></div>
                        </div>
                        <p class="hint">
                            Read once, at the start, from the <code>ocpi</code> section of ${ocpi.file}. A peer
                            knows this hub by these, so they are not changed while it runs.
                        </p>
                    </section>

                    <section class="card">
                        <h2><i class="fa-solid fa-link"></i> Where peers find it</h2>
                        <div class="kv-list">
                            <div class="kv"><span class="k">Versions URL</span><span class="v"><code class="password">${ocpi.endpoints.versions}</code></span></div>
                            <div class="kv"><span class="k">Base</span><span class="v">${ocpi.endpoints.base}</span></div>
                            <div class="kv"><span class="k">External URL</span><span class="v">${formatValue(ocpi.endpoints.externalURL)}</span></div>
                        </div>
                        <p class="hint">
                            The versions URL is the one thing a peer is given: everything else is discovered from
                            it. Behind a reverse proxy or a public name, set <code>ocpi.externalURL</code> so that the
                            URLs advertised here are the ones a peer can reach.
                        </p>
                    </section>

                    <section class="card">
                        <h2><i class="fa-solid fa-sliders"></i> Settings</h2>
                        <div class="kv-list">
                            ${Object.entries(ocpi.settings).map(([key, value]) => html`
                                <div class="kv">
                                    <span class="k">${humanizeKey(key)}</span>
                                    <span class="v">${formatValue(value)}</span>
                                </div>
                            `)}
                        </div>
                    </section>

                    <section class="card">
                        <h2><i class="fa-solid fa-database"></i> What is held</h2>
                        <div class="kv-list">
                            <div class="kv"><span class="k">Peers</span><span class="v"><a href="/configuration/ocpi/partners">${ocpi.counts.partners}</a></span></div>
                            <div class="kv"><span class="k">Of them registered</span><span class="v">${ocpi.counts.registered}</span></div>
                            <div class="kv"><span class="k">Calls written down</span><span class="v"><a href="/traffic">${ocpi.counts.calls}</a></span></div>
                            <div class="kv"><span class="k">Directory</span><span class="v">${ocpi.directory}</span></div>
                        </div>
                        <p class="hint">
                            The peers are kept by the OCPI library in append-only files below that directory, one set
                            per version, and read back at every start. The calls are not: they are in memory and
                            nowhere else, and a restart forgets them.
                        </p>
                    </section>

                    <section class="card wide">
                        <h2><i class="fa-solid fa-diagram-project"></i> Endpoints, per version</h2>
                        <p class="hint">
                            What a peer is told when it asks for the version details. Every module here is served
                            by this hub; a peer needs the token it was given to call any of them.
                        </p>
                        ${ocpi.endpoints.byVersion.map(version => html`
                            <h3>OCPI ${version.version}</h3>
                            <div class="table-scroll">
                                <table class="records">
                                    <thead><tr><th>Module</th><th>URL</th></tr></thead>
                                    <tbody>
                                        <tr><td>version details</td><td class="value">${version.details}</td></tr>
                                        <tr><td>credentials</td><td class="value">${version.credentials}</td></tr>
                                        ${Object.entries(version.modules).map(([module, url]) => html`
                                            <tr><td>${module}</td><td class="value">${url}</td></tr>
                                        `)}
                                    </tbody>
                                </table>
                            </div>
                        `)}
                    </section>

                </div>

            `);

        }

        async function load(): Promise<void> {

            try
            {
                const ocpi = await api.ocpi.configuration();

                if (!cancelled)
                    draw(ocpi);
            }
            catch (problem)
            {
                if (!cancelled)
                    render(content, html`<div class="error-box">The OCPI configuration could not be loaded: ${errorMessage(problem)}</div>`);
            }

        }

        void load();

        return () => { cancelled = true; };

    }

};
