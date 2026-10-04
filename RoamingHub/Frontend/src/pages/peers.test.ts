/**
 * The peers drawn, in a document of happy-dom, against a stand-in hub: a peer
 * half typed - and its focus - outlives the list changing under it, the rows
 * keep their key, a Register button says it is on its way and comes back
 * when the hub has answered, and a peer added empties the form, hides what
 * only starting the peering here needs, and says its token once.
 */

import { asked, change, field, open, refused, said, submit, until, type Asked } from '../../test/hub.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

import type { Partner, Partners } from '../api/client.ts';

const { peersPage } = await import('./peers.ts');


function aPeer(partyId: string, more: Partial<Partner> = {}): Partner {
    return { version: '2.2.1', id: `DE-${partyId}_CPO`, countryCode: 'DE', partyId, role: 'CPO', name: `Peer ${partyId}`,
             website: null, status: 'ENABLED', ourToken: `our-token-of-${partyId}`, hasOurToken: true, ourTokenStatus: 'ALLOWED',
             theirToken: null, hasTheirToken: false, theirVersionsURL: null, remoteStatus: null, selectedVersion: null,
             canRegister: false, connection: 'CONNECTED', lastSeen: '2026-10-04T11:59:00Z', connectionSince: '2026-10-04T11:00:00Z',
             registered: true, created: '2026-10-01T00:00:00Z', lastUpdated: '2026-10-01T00:00:00Z', ...more };
}

let held: Partners;

/** What a registration answers: held back until told, and then a refusal or not. */
let registration: { release: () => void; refuse: boolean } | null = null;

function hub({ method, path, body }: Asked): unknown {

    if (path === '/ocpi/partners' && method === 'GET')
        return held;

    if (path === '/ocpi/partners' && method === 'POST') {
        const spec = body as { countryCode: string; partyId: string; role: string; version: string; name: string };
        const peer = aPeer(spec.partyId, { id: `${spec.countryCode}-${spec.partyId}_${spec.role}`, name: spec.name, version: spec.version });
        held = { ...held, partners: [ ...held.partners, peer ] };
        return { message: 'added', id: peer.id, version: peer.version, ourToken: 'made up, shown once', partners: held };
    }

    const suspension = /^\/ocpi\/peers\/([^/]+)\/(suspend|resume)$/.exec(path);

    if (suspension && method === 'POST') {
        held = { ...held, partners: held.partners.map(peer => `${peer.countryCode}${peer.partyId}` === suspension[1]
                                                                 ? { ...peer, connection: suspension[2] === 'suspend' ? 'SUSPENDED' : 'CONNECTED' }
                                                                 : peer) };
        return { ok: true, message: `${suspension[2]}ed`, partners: held };
    }

    if (/\/register$/.test(path) && method === 'POST')
        return new Promise(resolve => {
            registration = {
                refuse:  false,
                release: () => resolve(registration?.refuse
                                           ? refused(502, 'their versions could not be fetched', { ok: false, message: 'Their versions could not be fetched.', partners: held })
                                           : { ok: true, message: 'Registered.', partners: held })
            };
        });

    return undefined;

}

async function opened(permissions = [ 'peers:read', 'peers:edit', 'peers:run' ]): Promise<HTMLElement> {
    held = { partners: [ aPeer('AAA'), aPeer('BBB'), aPeer('CCC', { canRegister: true, registered: false, connection: 'PLANNED',
                                                                     hasTheirToken: true, theirVersionsURL: 'https://ccc.example/ocpi/versions' }) ],
             versions: [ '2.2.1', '2.3.0' ], roles: [ 'CPO', 'EMSP', 'HUB' ], ourVersionsURL: 'https://hub.example/ocpi/versions' };
    registration = null;
    return open(peersPage, '/configuration/ocpi/partners', permissions, hub,
                root => root.querySelector('#peer-list tbody tr') !== null);
}

const suspensionOf = (root: HTMLElement, party: string) => root.querySelector<HTMLButtonElement>(`.peer-suspension[data-party="${party}"]`)!;
const registerOf   = (root: HTMLElement, id: string)    => root.querySelector<HTMLButtonElement>(`.partner-register[data-id="${id}"]`)!;
const rowOf        = (root: HTMLElement, id: string)    => root.querySelector<HTMLElement>(`.partner-remove[data-id="${id}"]`)!.closest('tr')!;


describe('the peers', () => {

    it('keep a peer half typed, and its focus, while another is suspended', async () => {

        const root = await opened();
        const name = field(root, '#partner-form', 'name');
        const pid  = field(root, '#partner-form', 'partyId');

        pid. value = 'XYZ';
        name.value = 'XYZ Charging';
        name.focus();

        suspensionOf(root, 'DEAAA').click();
        await until(() => suspensionOf(root, 'DEAAA').textContent!.trim() === 'Resume', 'the peer suspended was not drawn so');

        assert.ok(field(root, '#partner-form', 'name') === name, 'the field was made anew');
        assert.equal(pid. value, 'XYZ');
        assert.equal(name.value, 'XYZ Charging');
        assert.ok(document.activeElement === name, 'the focus went');

    });

    it('keep a peer half typed, and its focus, while the tokens are shown', async () => {

        const root = await opened();
        const name = field(root, '#partner-form', 'name');

        name.value = 'XYZ Charging';
        name.focus();

        root.querySelector<HTMLButtonElement>('#reveal')!.click();
        await until(() => root.querySelector('code.token:not(.muted)') !== null, 'the tokens were not shown');

        assert.ok(field(root, '#partner-form', 'name') === name, 'the field was made anew');
        assert.equal(name.value, 'XYZ Charging');
        assert.ok(document.activeElement === name, 'the focus went');

    });

    it('keep each row where a peer is suspended', async () => {

        const root = await opened();
        const aaa  = rowOf(root, 'DE-AAA_CPO');
        const bbb  = rowOf(root, 'DE-BBB_CPO');

        // The first row goes, as a peer removed: the next keeps its element.
        held = { ...held, partners: held.partners.slice(1) };
        suspensionOf(root, 'DEBBB').click();
        await until(() => suspensionOf(root, 'DEBBB').textContent!.trim() === 'Resume', 'the peer suspended was not drawn so');

        assert.ok(root.querySelector('.partner-remove[data-id="DE-AAA_CPO"]') === null, 'the peer gone is still there');
        assert.ok(aaa.isConnected === false, 'the row of the peer gone is still in the page');
        assert.ok(rowOf(root, 'DE-BBB_CPO') === bbb, 'the row of the next peer was made anew');

    });

    it('say a registration is on its way, and are back when the hub refused it', async () => {

        const root = await opened();

        registerOf(root, 'DE-CCC_CPO').click();
        await until(() => registration !== null, 'the registration was not asked for');

        assert.equal(registerOf(root, 'DE-CCC_CPO').disabled, true);
        assert.equal(registerOf(root, 'DE-CCC_CPO').textContent!.trim(), 'Registering ...');

        registration!.refuse = true;
        registration!.release();

        await until(() => registerOf(root, 'DE-CCC_CPO').disabled === false, 'the Register button stayed disabled');

        assert.equal(registerOf(root, 'DE-CCC_CPO').textContent!.trim(), 'Register');
        assert.match(root.querySelector('#peer-notices')!.textContent!, /Their versions could not be fetched\./);
        assert.deepEqual(said, [], 'a refused handshake was not said above the list');

    });

    it('empty the form for the next peer, and say the token of the one added once', async () => {

        const root = await opened();
        const form = root.querySelector<HTMLFormElement>('#partner-form')!;

        field(root, '#partner-form', 'countryCode').value = 'DE';
        field(root, '#partner-form', 'partyId').    value = 'XYZ';
        field(root, '#partner-form', 'name').       value = 'XYZ Charging';
        change(field(root, '#partner-form', 'startHere'), true);
        field(root, '#partner-form', 'theirToken'). value = 'their-token';
        field(root, '#partner-form', 'versionsURL').value = 'https://xyz.example/ocpi/versions';

        assert.equal(root.querySelector<HTMLElement>('#start-here')!.hidden, false);

        submit(root, '#partner-form');
        await until(() => root.querySelector('.partner-remove[data-id="DE-XYZ_CPO"]') !== null, 'the peer added was not drawn');

        assert.ok(root.querySelector('#partner-form') === form, 'the form was made anew');
        assert.equal(field(root, '#partner-form', 'partyId').value, '', 'the form kept the peer added');
        assert.equal(field(root, '#partner-form', 'startHere').checked, false);
        assert.equal(root.querySelector<HTMLElement>('#start-here')!.hidden, true, 'what only starting here needs is still shown');
        assert.equal(field(root, '#partner-form', 'theirToken').disabled, true);
        assert.match(root.querySelector('#peer-notices')!.textContent!, /made up, shown once/);
        assert.equal(asked.filter(one => one.method === 'POST' && one.path === '/ocpi/partners').length, 1);

    });

    it('empty the form on Reload, once it was agreed to', async () => {

        const root = await opened();

        field(root, '#partner-form', 'name').value = 'XYZ Charging';

        root.querySelector<HTMLButtonElement>('#reload')!.click();
        await until(() => field(root, '#partner-form', 'name').value === '', 'Reload kept what was typed');

        assert.equal(asked.filter(one => one.method === 'GET' && one.path === '/ocpi/partners').length, 2);

    });

});
