/**
 * The keys of roaming network operators drawn, in a document of happy-dom, against a stand-in of the hub: what it
 * follows and the operator it is, below the list of the roaming networks; and who holds the keys of the operator of a
 * roaming network, what waits for them, and the change sets waiting, on its page. Shown to everybody, and done only
 * by whoever may edit the roaming networks - each thing asked of the hub where the command line asks it.
 */

import { asked, field, open, refused, said, submit, type, until, type Asked } from '../../test/hub.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

import type { Page } from '@node/router';

const { changeLabel, holdersOf, mountHubKeys, mountNetworkKeys } = await import('./poiShareholders.ts');
const { poiNetworksPage }                                         = await import('./poi.ts');


const alpha1  = 'a1'.repeat(32);
const alpha2  = 'a2'.repeat(32);
const alphaS  = 'a5'.repeat(32);
const beta1   = 'b1'.repeat(32);
const betaS   = 'b5'.repeat(32);

/** A page that is nothing but one of the two parts, as a page gives it an element. */
const hubKeys: Page = {
    title:  () => '',
    render: ({ root }: { root: HTMLElement }) => mountHubKeys(root).release
} as never;

function networkKeys(roamingNetworkId: string): Page {
    return {
        title:  () => '',
        render: ({ root }: { root: HTMLElement }) => mountNetworkKeys(root, roamingNetworkId).release
    } as never;
}

const operator = {
    '@id':         'GraphDefined',
    name:          { en: 'GraphDefined GmbH' },
    identityKeys:  [ { '@id': alpha1, key: 'pAEC', holder: 'Alpha' } ],
    signingKeyCertificates:  [ { signingKey: { '@id': alphaS, key: 'pAEC', holder: 'Alpha' }, keyId: alpha1 } ]
};

const followedQA = { '@id': 'QA', url: 'https://other.example/poi/RNs/QA', connected: true, trustAnchor: 'WWCP/TA-2:ABC', revision: 7, commit: 'c7' };

/** The stand-in of the hub: what it follows and the operator it is, and whatever else a test answers first. */
function hub(followed: unknown[], more: (one: Asked) => unknown = () => undefined) {
    return (one: Asked): unknown => {

        const answer = more(one);

        if (answer !== undefined)
            return answer;

        if (one.method === 'GET' && one.path === '/poi/followed')
            return { followed };

        if (one.method === 'GET' && one.path === '/poi/roamingNetworkOperator')
            return operator;

        return undefined;

    };
}

const twoHolders = {
    roamingNetworkOperator:  'GraphDefined',
    minConsensus:            2,
    holders:                 [ { holder: 'Alpha', identityKeys: [ alpha1 ], signingKeys: [ alphaS ] },
                               { holder: 'Beta',  identityKeys: [ beta1  ], signingKeys: [ betaS  ] } ],
    changes:                 [] as unknown[],
    followed:                false,
    thisHub:                 'Alpha' as string | undefined
};

/** The stand-in of the hub for a roaming network: its holders, its change sets waiting, and what a test answers first. */
function network(holders: unknown, pending: unknown[] = [], more: (one: Asked) => unknown = () => undefined) {
    return (one: Asked): unknown => {

        const answer = more(one);

        if (answer !== undefined)
            return answer;

        if (one.method === 'GET' && one.path === '/poi/shareholders/QA')
            return holders ?? undefined;

        if (one.method === 'GET' && one.path === '/poi/shareholders/QA/pending')
            return { pendingChangeSets: pending };

        return undefined;

    };
}

const posted = () => asked.filter(one => one.method !== 'GET').map(one => `${one.method} ${one.path}`);


describe('what this hub follows, and the operator it is', () => {

    it('shows what is followed to everybody, and nothing where nothing is', async () => {

        const empty = await open(hubKeys, '/poi/RNs', null, hub([]), () => asked.some(one => one.path === '/poi/followed'));
        await until(() => empty.querySelector('.poi-followed') === null, 'a card of nothing followed');
        assert.equal(empty.querySelector('.poi-operator'), null);

        const root = await open(hubKeys, '/poi/RNs', null, hub([ followedQA, { '@id': 'RN2', url: 'https://gone.example/poi/RNs/RN2', connected: false, trustAnchor: 'x', error: 'no answer' } ]),
                                root => root.querySelector('#followed-table') !== null);

        assert.deepEqual([ ...root.querySelectorAll('#followed-table tbody tr') ].map(row => row.getAttribute('data-network')), [ 'QA', 'RN2' ]);
        assert.equal(root.querySelector<HTMLAnchorElement>('tr[data-network="QA"] a')!.getAttribute('href'), '/poi/RNs/QA');
        assert.match(root.querySelector('tr[data-network="QA"]')!.textContent!,  /connected\s+7/);
        assert.match(root.querySelector('tr[data-network="RN2"]')!.textContent!, /not connected\s+no answer\s+no copy yet/);

        assert.equal(root.querySelector('#follow-form'),   null, 'a form for nobody signed in');
        assert.equal(root.querySelector('button'),         null, 'something to do for nobody signed in');
        assert.equal(root.querySelector('.poi-operator'),  null, 'the operator for nobody signed in');
        assert.ok(!asked.some(one => one.path === '/poi/roamingNetworkOperator'), 'the operator was asked for nobody');

    });

    it('follows a roaming network by its trust anchor, says what the hub says, and asks again what is followed', async () => {

        let followed: unknown[] = [];

        const root = await open(hubKeys, '/poi/RNs', [ 'poi:edit' ],
                                one => hub(followed, one => {
                                           if (one.method !== 'POST' || one.path !== '/poi/followed')
                                               return undefined;
                                           followed = [ followedQA ];
                                           return { ...followedQA, message: "The roaming network 'QA' is followed." };
                                       })(one),
                                root => root.querySelector('#follow-form') !== null);

        assert.ok(root.querySelector('#followed-none'));

        type(field<HTMLTextAreaElement>(root, '#follow-form', 'trustAnchor'), 'WWCP/TA-2:ABC');
        type(field(root, '#follow-form', 'url'), 'https://other.example/poi/RNs/QA');
        submit(root, '#follow-form');

        await until(() => root.querySelector('#followed-table') !== null, 'what is followed was not shown');

        assert.deepEqual(asked.find(one => one.method === 'POST')!.body, { trustAnchor: 'WWCP/TA-2:ABC', url: 'https://other.example/poi/RNs/QA' });
        assert.match(root.querySelector('#follow-said')!.textContent!, /'QA' is followed/);
        assert.equal(field<HTMLTextAreaElement>(root, '#follow-form', 'trustAnchor').value, '');

    });

    it('says why a roaming network is not followed, and keeps what was typed in', async () => {

        const root = await open(hubKeys, '/poi/RNs', [ 'poi:edit' ],
                                hub([], one => one.method === 'POST' ? refused(409, "A roaming network 'QA' is served here already.") : undefined),
                                root => root.querySelector('#follow-form') !== null);

        type(field<HTMLTextAreaElement>(root, '#follow-form', 'trustAnchor'), 'WWCP/TA-2:ABC');
        submit(root, '#follow-form');

        await until(() => !root.querySelector<HTMLElement>('#follow-said')!.hidden, 'nothing was said');

        assert.match(root.querySelector('#follow-said')!.textContent!, /served here already/);
        assert.equal(root.querySelector('#follow-said')!.className, 'form-error');
        assert.equal(field<HTMLTextAreaElement>(root, '#follow-form', 'trustAnchor').value, 'WWCP/TA-2:ABC');

    });

    it('follows one no longer only when that was confirmed', async () => {

        const root = await open(hubKeys, '/poi/RNs', [ 'poi:edit' ],
                                hub([ followedQA ], one => one.method === 'DELETE' ? { '@id': 'QA', message: 'Followed no longer.' } : undefined),
                                root => root.querySelector('#followed-table') !== null);

        const confirm  = window.confirm;
        window.confirm = (text?: unknown) => { said.push(String(text)); return false; };

        try
        {
            root.querySelector<HTMLButtonElement>('tr[data-network="QA"] button')!.click();
            assert.match(said.at(-1)!, /Its copy here is deleted/);
            assert.deepEqual(posted(), []);
        }
        finally
        {
            window.confirm = confirm;
        }

        root.querySelector<HTMLButtonElement>('tr[data-network="QA"] button')!.click();

        await until(() => !root.querySelector<HTMLElement>('#follow-said')!.hidden, 'nothing was said');
        assert.deepEqual(posted(), [ 'DELETE /poi/followed/QA' ]);

    });

    it('shows the operator this hub is to whoever may edit, and to nobody else', async () => {

        const viewer = await open(hubKeys, '/poi/RNs', [ 'peers:read' ], hub([ followedQA ]), root => root.querySelector('#followed-table') !== null);
        assert.equal(viewer.querySelector('.poi-operator'), null);
        assert.equal(viewer.querySelector('#follow-form'),  null);

        const root = await open(hubKeys, '/poi/RNs', [ 'poi:edit' ], hub([]), root => root.querySelector('.poi-operator') !== null);

        assert.equal(root.querySelector('#operator-holder')!.textContent, 'Alpha');
        assert.equal(root.querySelector('#operator-name')!.textContent,   'GraphDefined GmbH');
        assert.equal(root.querySelector('#operator-identity-keys code')!.getAttribute('title'), alpha1);
        assert.equal(root.querySelector('#operator-signing-keys code')!.getAttribute('title'),  alphaS);

        assert.deepEqual(holdersOf(operator.identityKeys),     [ 'Alpha' ]);
        assert.deepEqual(holdersOf([ { '@id': alpha1, key: 'x' } ]), [], 'a holder where none is named');

    });

    it('replaces the identity key once confirmed, and says where it waits for the other holders', async () => {

        const root = await open(hubKeys, '/poi/RNs', [ 'poi:edit' ],
                                hub([ followedQA ], one => one.method === 'POST' && one.path === '/poi/roamingNetworkOperator/identityKey/rotate'
                                                               ? { message:   'The identity key is replaced in 1 roaming network, and waits in 1.',
                                                                   changed:   [ 'Own' ],
                                                                   refused:   {},
                                                                   waiting:   { QA: "the other holders to agree: 'roamingNetworks QA rekey Alpha' at their hubs" },
                                                                   operator:  { ...operator, identityKeys: [ ...operator.identityKeys, { '@id': alpha2, key: 'pAEC', holder: 'Alpha' } ] } }
                                                               : undefined),
                                root => root.querySelector('#rotate-identity') !== null);

        root.querySelector<HTMLButtonElement>('#rotate-identity')!.click();

        await until(() => root.querySelector('#operator-waiting') !== null, 'where it waits was not shown');

        assert.match(said.at(-1)!, /In two changes of every roaming network/);
        assert.deepEqual(posted(), [ 'POST /poi/roamingNetworkOperator/identityKey/rotate' ]);
        assert.match(root.querySelector('#operator-said')!.textContent!, /waits in 1/);
        assert.equal(root.querySelector('#operator-said')!.className, 'notice ok');
        assert.equal(root.querySelector<HTMLAnchorElement>('#operator-waiting a')!.getAttribute('href'), '/poi/RNs/QA');
        assert.match(root.querySelector('#operator-waiting')!.textContent!, /rekey Alpha/);
        assert.equal(root.querySelector('#operator-refused'), null);

    });

    it('renews the signing key without asking twice, and says where it was refused', async () => {

        const root = await open(hubKeys, '/poi/RNs', [ 'poi:edit' ],
                                hub([], one => one.method === 'POST'
                                                   ? new Response(JSON.stringify({ message: 'Refused in 1 roaming network.', changed: [], refused: { Own: 'its history refused it' }, operator }),
                                                                  { status: 207, headers: { 'Content-Type': 'application/json' } })
                                                   : undefined),
                                root => root.querySelector('#rotate-signing') !== null);

        const before = said.length;

        root.querySelector<HTMLButtonElement>('#rotate-signing')!.click();

        await until(() => root.querySelector('#operator-refused') !== null, 'where it was refused was not shown');

        assert.equal(said.length, before, 'asked whether to');
        assert.deepEqual(posted(), [ 'POST /poi/roamingNetworkOperator/signingKey/rotate' ]);
        assert.equal(root.querySelector('#operator-said')!.className, 'form-error');
        assert.match(root.querySelector('#operator-refused')!.textContent!, /its history refused it/);

    });

    it('renames the operator, and names the holder of its keys once confirmed', async () => {

        const root = await open(hubKeys, '/poi/RNs', [ 'poi:edit' ],
                                hub([], one => one.method === 'PUT' ? { message: 'Done.', changed: [ 'Own' ], refused: {}, operator } : undefined),
                                root => root.querySelector('#operator-name-form') !== null);

        type(field(root, '#operator-name-form', 'name'), 'GraphDefined AG');
        submit(root, '#operator-name-form');

        await until(() => asked.some(one => one.method === 'PUT'), 'the hub was not asked');
        await until(() => !root.querySelector<HTMLButtonElement>('#rotate-signing')!.disabled, 'it did not end');

        type(field(root, '#operator-holder-form', 'holder'), 'Gamma');
        submit(root, '#operator-holder-form');

        await until(() => asked.filter(one => one.method === 'PUT').length === 2, 'the hub was not asked');

        assert.match(said.at(-1)!, /Name 'Gamma' as the holder[\s\S]*nothing leads there from the old one/);
        assert.deepEqual(asked.filter(one => one.method === 'PUT').map(one => [ one.path, one.body ]),
                         [ [ '/poi/roamingNetworkOperator/name',   { name:   'GraphDefined AG' } ],
                           [ '/poi/roamingNetworkOperator/holder', { holder: 'Gamma'           } ] ]);

    });

    it('stands below the list of the roaming networks', async () => {

        const root = await open(poiNetworksPage, '/poi/RNs', [ 'poi:edit' ],
                                one => one.method === 'GET' && one.path === '/poi/RNs'
                                           ? new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json', 'X-Total-Count': '0' } })
                                           : hub([ followedQA ])(one),
                                root => root.querySelector('#followed-table') !== null && root.querySelector('.poi-operator') !== null);

        assert.ok(root.querySelector('#poi-own #follow-form'));
        assert.ok(root.querySelector('#network-form'), 'the new roaming network is gone');

    });

});


describe('who holds the keys of the operator of a roaming network', () => {

    it('is shown to everybody, with how many decide, and with nothing to do', async () => {

        const root = await open(networkKeys('QA'), '/poi/RNs/QA', null,
                                network({ ...twoHolders, changes: [ { kind: 'admit', holder: 'Gamma', keyStatement: { signatures: [ {} ] } } ] },
                                        [ { changeSet: { '@id': 'cs1', signatures: [ {} ], changes: [ { kind: 'update', entityType: 'roamingNetwork', entityId: 'QA', propertyName: 'operator' } ] }, missing: [ 'Beta' ] } ]),
                                root => root.querySelector('#shareholders-table') !== null && root.querySelector('.poi-pending') !== null);

        assert.match(root.querySelector('#shareholders-quorum')!.textContent!, /held by 2: 2 of them decide together what it signs, none of them alone/);
        assert.deepEqual([ ...root.querySelectorAll('#shareholders-table tbody tr') ].map(row => row.getAttribute('data-holder')), [ 'Alpha', 'Beta' ]);
        assert.match(root.querySelector('tr[data-holder="Alpha"]')!.textContent!, /this hub/);
        assert.doesNotMatch(root.querySelector('tr[data-holder="Beta"]')!.textContent!, /this hub/);
        assert.equal(root.querySelector('tr[data-holder="Beta"] code')!.getAttribute('title'), beta1);

        assert.match(root.querySelector('#shareholders-changes')!.textContent!, /A new holder\s+Gamma[\s\S]*signed by 1,\s+no change set yet/);
        assert.match(root.querySelector('.poi-pending-set')!.textContent!, /cs1[\s\S]*update roamingNetwork QA \(operator\)[\s\S]*Beta/);

        assert.equal(root.querySelector('button'), null, 'something to do for nobody signed in');
        assert.equal(root.querySelector('form'),   null);

    });

    it('says one holder decides alone, and shows no card where there is no operator', async () => {

        const one = await open(networkKeys('QA'), '/poi/RNs/QA', [ 'poi:edit' ],
                               network({ ...twoHolders, minConsensus: 1, holders: [ { holder: '', identityKeys: [ alpha1 ], signingKeys: [ alphaS ] } ], thisHub: undefined }),
                               root => root.querySelector('#shareholders-table') !== null);

        assert.match(one.querySelector('#shareholders-quorum')!.textContent!, /held by one, deciding alone/);
        assert.match(one.querySelector('#shareholders-table tbody')!.textContent!, /not named/);
        assert.equal(one.querySelector('button'), null);

        const none = await open(networkKeys('QA'), '/poi/RNs/QA', [ 'poi:edit' ], network(null),
                                () => asked.some(one => one.path === '/poi/shareholders/QA/pending'));

        await until(() => none.querySelector('section') === null, 'a card without holders');

    });

    it('has this hub agree to what waits, each where the command line asks it', async () => {

        const changes = [
            { kind: 'admit',   holder: 'Gamma', keyStatement: { signatures: [ {} ] } },
            { kind: 'addKey',  holder: 'Beta',  keyStatement: { signatures: [ {}, {} ] }, changeSet: 'cs9' },
            { kind: 'dropKey', holder: 'Alpha', keyStatement: {} },
            { kind: 'dismiss', holder: 'Delta', keyStatement: {} },
            { kind: 'restate', holder: '',      keyStatement: {} }
        ];

        const root = await open(networkKeys('QA'), '/poi/RNs/QA', [ 'poi:edit' ],
                                network({ ...twoHolders, changes }, [], one => one.method === 'POST' ? { status: 'waiting', message: `Signed: ${one.path}` } : undefined),
                                root => root.querySelector('#shareholders-changes') !== null);

        const row = (kind: string) => root.querySelector<HTMLElement>(`.poi-change[data-kind="${kind}"]`)!;

        assert.match(row('addKey').textContent!, /change set\s+cs9/);
        assert.equal(row('dropKey').querySelector('button'), null, 'this hub agrees to what it asked for itself');
        assert.match(row('dropKey').textContent!, /asked for by this hub/);

        assert.equal(row('dismiss').querySelector('button')!.textContent, 'Agree');

        for (const kind of [ 'admit', 'addKey', 'dismiss', 'restate' ])
        {
            const before = posted().length;
            row(kind).querySelector<HTMLButtonElement>('button')!.click();
            await until(() => posted().length === before + 1, `nothing was asked for ${kind}`);
            await until(() => !row(kind).querySelector<HTMLButtonElement>('button')!.disabled, 'it did not end');
        }

        assert.deepEqual(posted(), [ 'POST /poi/shareholders/QA/admit/Gamma',
                                     'POST /poi/shareholders/QA/rekey/Beta',
                                     'POST /poi/shareholders/QA/dismiss/Delta',
                                     'POST /poi/shareholders/QA/restate' ]);

        assert.match(root.querySelector('#shareholders-said')!.textContent!, /Signed: \/poi\/shareholders\/QA\/restate/);
        assert.ok(said.some(text => /Dismiss 'Delta' as a holder/.test(text)), 'agreed to a dismissal without being asked');

        assert.equal(changeLabel('addKey'),  'A new identity key, next to the old one, of');
        assert.equal(changeLabel('restate'), 'The keys stated anew');

    });

    it('dismisses a holder once confirmed, states the keys anew, and says why the hub did not', async () => {

        const root = await open(networkKeys('QA'), '/poi/RNs/QA', [ 'poi:edit' ],
                                network(twoHolders, [], one => one.method !== 'POST'            ? undefined
                                                             : one.path.endsWith('/restate')    ? refused(502, 'The other hub did not answer.')
                                                             :                                    { status: 'waiting', message: 'Waits for Beta.' }),
                                root => root.querySelector('#shareholders-table') !== null);

        assert.equal(root.querySelector('tr[data-holder="Alpha"] button'), null, 'the hub that makes the roaming network resigns');
        assert.equal(root.querySelector('#sign-head'), null, 'a head signed where the roaming network is made');
        assert.equal(root.querySelector('#join-form'), null);

        root.querySelector<HTMLButtonElement>('tr[data-holder="Beta"] button')!.click();

        await until(() => !root.querySelector<HTMLElement>('#shareholders-said')!.hidden, 'nothing was said');

        assert.match(said.at(-1)!, /Dismiss 'Beta' as a holder/);
        assert.match(root.querySelector('#shareholders-said')!.textContent!, /Waits for Beta/);

        await until(() => !root.querySelector<HTMLButtonElement>('#restate')!.disabled, 'it did not end');
        root.querySelector<HTMLButtonElement>('#restate')!.click();

        await until(() => root.querySelector('#shareholders-said')!.className === 'form-error', 'the refusal was not said');

        assert.match(root.querySelector('#shareholders-said')!.textContent!, /did not answer/);
        assert.deepEqual(posted(), [ 'POST /poi/shareholders/QA/dismiss/Beta', 'POST /poi/shareholders/QA/restate' ]);

    });

    it('asks to hold the keys of the operator of a roaming network followed, with where its copy is', async () => {

        const root = await open(networkKeys('QA'), '/poi/RNs/QA', [ 'poi:edit' ],
                                network({ ...twoHolders, followed: true, thisHub: undefined, changes: undefined },
                                        [], one => one.method === 'POST' ? { status: 'waiting', message: 'Asked.' } : undefined),
                                root => root.querySelector('#join-form') !== null);

        assert.match(root.querySelector('#shareholders-quorum')!.textContent!, /is a copy here/);
        assert.ok(root.querySelector('#shareholders-unasked'), 'not said that what waits could not be asked');
        assert.equal(root.querySelector('#shareholders-table button'), null, 'a holder dismissed by a hub that is none');
        assert.equal(root.querySelector('#restate'), null);

        type(field(root, '#join-form', 'url'), 'https://this-hub.example/poi/RNs/QA');
        submit(root, '#join-form');

        await until(() => !root.querySelector<HTMLElement>('#shareholders-said')!.hidden, 'nothing was said');

        assert.deepEqual(asked.filter(one => one.method === 'POST').map(one => [ one.path, one.body ]),
                         [ [ '/poi/shareholders/QA/join', { url: 'https://this-hub.example/poi/RNs/QA' } ] ]);

    });

    it('resigns from a roaming network followed once that began where it is made, and signs its head, as one of the holders', async () => {

        const root = await open(networkKeys('QA'), '/poi/RNs/QA', [ 'poi:edit' ],
                                network({ ...twoHolders, followed: true, thisHub: 'Beta', changes: [ { kind: 'dismiss', holder: 'Beta', keyStatement: {} } ] },
                                        [], one => one.method === 'POST' ? { status: 'committed', message: 'Done.' } : undefined),
                                root => root.querySelector('#sign-head') !== null);

        assert.equal(root.querySelector('#join-form'), null, 'a holder asks to be one');
        assert.equal(root.querySelector('#shareholders-table button'), null, 'a dismissal begun where the roaming network is not made');

        const resign = () => root.querySelector<HTMLButtonElement>('.poi-change[data-kind="dismiss"] button')!;

        assert.equal(resign().textContent, 'Resign');

        root.querySelector<HTMLButtonElement>('#sign-head')!.click();
        await until(() => posted().length === 1, 'the head was not signed');
        await until(() => !root.querySelector<HTMLButtonElement>('#sign-head')!.disabled, 'it did not end');

        resign().click();
        await until(() => posted().length === 2, 'it did not resign');

        assert.match(said.at(-1)!, /Resign as a holder/);
        assert.deepEqual(posted(), [ 'POST /poi/followed/QA/signatures', 'POST /poi/shareholders/QA/dismiss/Beta' ]);

    });

    it('approves a change set waiting, and withdraws one once confirmed', async () => {

        const pending = [
            { changeSet: { '@id': 'cs1', description: { en: 'A new tariff' }, signatures: [], changes: [] }, missing: [ 'Alpha', 'Beta' ] },
            { changeSet: { '@id': 'cs2', signatures: [ {} ] } }
        ];

        const root = await open(networkKeys('QA'), '/poi/RNs/QA', [ 'poi:edit' ],
                                network(twoHolders, pending, one => one.method === 'POST' ? { status: 'waiting', message: 'Signed.' } : undefined),
                                root => root.querySelectorAll('.poi-pending-set').length === 2);

        const set = (id: string, what: string) => root.querySelector<HTMLButtonElement>(`.poi-pending-set[data-change-set="${id}"] button[data-do="${what}"]`)!;

        assert.match(root.querySelector('.poi-pending-set[data-change-set="cs1"]')!.textContent!, /A new tariff/);

        set('cs1', 'approve').click();
        await until(() => posted().length === 1, 'it was not approved');
        await until(() => !set('cs2', 'refuse').disabled, 'it did not end');

        set('cs2', 'refuse').click();
        await until(() => posted().length === 2, 'it was not withdrawn');

        assert.match(said.at(-1)!, /Withdraw the change set 'cs2'/);
        assert.deepEqual(posted(), [ 'POST /poi/shareholders/QA/pending/cs1/approve', 'POST /poi/shareholders/QA/pending/cs2/refuse' ]);

        const viewer = await open(networkKeys('QA'), '/poi/RNs/QA', [ 'peers:read' ], network(twoHolders, pending),
                                  root => root.querySelectorAll('.poi-pending-set').length === 2);

        assert.equal(viewer.querySelector('button'), null, 'something to do for somebody who may not edit');

    });

});
