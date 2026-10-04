/**
 * The OCPI page drawn, in a document of happy-dom, against a stand-in hub:
 * who this hub is, its endpoints per version as the rows of a table, and
 * Reload draws what the hub says then.
 */

import { open, until, type Asked } from '../../test/hub.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

import type { OCPIConfiguration } from '../api/client.ts';

const { ocpiPage } = await import('./ocpi.ts');


let peers = 2;

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
            settings:       { locationsAsOpenData: false, tariffsAsOpenData: false, allowDowngrades: false, logRequests: true, logPayloads: false },
            counts:         { partners: peers, registered: 1, calls: 0 },
            directory:      'ocpi',
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

});
