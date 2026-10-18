/**
 * The OCPI page drawn, in a document of happy-dom, against a stand-in hub:
 * who this hub is, its endpoints per version as the rows of a table, and
 * Reload draws what the hub says then.
 */

import { open, until, type Asked } from '../../test/hub.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

import type { OCPIConfiguration, TrafficFile } from '../api/client.ts';

const { ocpiPage } = await import('./ocpi.ts');


let peers = 2;

/** Whether this stand-in hub is appending its calls, which "What is held" says. */
let appendedTo: TrafficFile | null = null;

function hub({ path }: Asked): unknown {

    if (path === '/configuration/ocpi')
        return {
            party:          { countryCode: 'DE', partyId: 'GDH', id: 'DE*GDH', role: 'HUB', name: 'GraphDefined Roaming Hub', website: null },
            endpoints:      { base: 'https://hub.example/ocpi', versions: 'https://hub.example/ocpi/versions', externalURL: null,
                              byVersion: [ { version: '2.2.1', details: 'https://hub.example/ocpi/versions/2.2.1',
                                             credentials: 'https://hub.example/ocpi/2.2.1/credentials',
                                             modules: { hubclientinfo: 'https://hub.example/ocpi/2.2.1/hubclientinfo' } } ] },
            versions:       [ '2.2.1' ],
            knownVersions:  [ '2.1.1', '2.2.1', '2.3.0' ],
            settings:       { locationsAsOpenData: false, tariffsAsOpenData: false, allowDowngrades: false, logRequests: true,
                              logPayloads: false, logFile: appendedTo !== null },
            counts:         { partners: peers, registered: 1, calls: 0 },
            directory:      'ocpi',
            appendedTo,
            file:           'configuration.json'
        } satisfies OCPIConfiguration;

    return undefined;

}


describe('the OCPI page', () => {

    it('draws who the hub is and its endpoints, and again on Reload', async () => {

        peers = 2;

        const root = await open(ocpiPage, '/configuration/ocpi', [ 'configuration:read' ],
                                hub, root => root.querySelector('.cards') !== null);

        assert.match(root.querySelector('.cards')!.textContent!, /Party\s*DE\*GDH/);
        assert.equal(root.querySelectorAll('.endpoints table tbody tr').length, 3);
        assert.match(root.querySelector('.cards')!.textContent!, /Peers\s*2/);

        peers = 3;
        root.querySelector<HTMLButtonElement>('#reload')!.click();

        await until(() => /Peers\s*3/.test(root.querySelector('.cards')!.textContent!), 'Reload did not draw what the hub says then');

    });

    it('says a restart forgets the calls, where nothing is appending them', async () => {

        appendedTo = null;

        const root = await open(ocpiPage, '/configuration/ocpi', [ 'configuration:read' ],
                                hub, root => root.querySelector('.cards') !== null);

        const held = heldHint(root);

        assert.ok(held.includes('in memory and nowhere else, and a restart forgets them'), held);
        assert.ok(held.includes('ocpi.logging.file'),                                      held);

    });

    it('says where the calls are appended and how many, where they are', async () => {

        appendedTo = { directory: '/srv/hub/traffic', current: '/srv/hub/traffic/traffic-2026-10-18.jsonl', written: 1234 };

        const root = await open(ocpiPage, '/configuration/ocpi', [ 'configuration:read' ],
                                hub, root => root.querySelector('.cards') !== null);

        const held = heldHint(root);

        assert.ok(held.includes('appended to /srv/hub/traffic'),          held);
        assert.ok(held.includes('1234 of them since this hub started'),   held);

        // The claim it replaced: it must be gone rather than merely followed.
        assert.ok(!held.includes('nowhere else'),                         held);

        appendedTo = null;

    });

});


/**
 * The hint below "What is held", which says what becomes of the calls, as one
 * line: the template wraps where it reads well, and no assertion here is
 * about where that is.
 */
function heldHint(Root: HTMLElement): string {

    const held = [...Root.querySelectorAll('section.card')]
                     .find(card => card.querySelector('h2')?.textContent?.includes('What is held'));

    assert.ok(held, 'the card "What is held" was not drawn');

    return held.querySelector('p.hint')!.textContent!.replace(/\s+/g, ' ').trim();

}
