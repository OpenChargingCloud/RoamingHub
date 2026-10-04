/**
 * The configuration drawn, in a document of happy-dom, against a stand-in
 * hub: its cards - cardViews.ts's - stand as the markup they are, and Reload
 * draws them again with what the hub says then.
 */

import { open, until, type Asked } from '../../test/hub.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

const { configurationPage } = await import('./configuration.ts');


let uptime = '1 minute';

function hub({ path }: Asked): unknown {

    if (path === '/status')
        return { service: 'RoamingHub', version: '0.1.0', hermod: null, timestamp: '2026-10-04T12:00:00Z',
                 startedAt: '2026-10-04T11:59:00Z', uptime, sessions: 1, log: { entries: 0, capacity: 100, lastId: 0, tags: [] } };

    if (path === '/configuration')
        return { RoamingHub: { name: 'roaming hub' }, http: { port: 8080 }, web: {}, log: {}, time: {},
                 ocpi: { partyId: 'DE*GDH', versions: [ '2.2.1' ] }, traffic: { capacity: 5000 },
                 assemblies: [ { name: 'RoamingHub', version: '0.1.0', commit: 'abc1234' } ] };

    return undefined;

}


describe('the configuration', () => {

    it('draws its cards as markup, and again on Reload', async () => {

        uptime = '1 minute';

        const root = await open(configurationPage, '/configuration', [ 'configuration:read' ],
                                hub, root => root.querySelector('.cards') !== null);

        assert.equal(root.querySelectorAll('.cards > section.card').length, 8);
        assert.match(root.querySelector('.cards')!.textContent!, /OCPI - DE\*GDH/);
        assert.match(root.querySelector('.cards')!.textContent!, /Uptime\s+1 minute/);
        assert.doesNotMatch(root.textContent!, /<section/, 'a card was taken as text');

        uptime = '2 minutes';
        root.querySelector<HTMLButtonElement>('#reload')!.click();

        await until(() => /Uptime\s+2 minutes/.test(root.textContent!), 'Reload did not draw what the hub says then');

        assert.equal(root.querySelectorAll('.cards > section.card').length, 8);

    });

});
