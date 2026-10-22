/**
 * The epochs of a roaming network: what the POI API answers is read only as an epoch, and the current one is shown
 * with its profiles, the epoch before it and the final checkpoint, and every epoch where there are more.
 */

import '../../test/hub.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

const { algorithmsText, epochCard, epochOf } = await import('./poiEpochs.ts');
const { render } = await import('@node/view');


const profiles = {
    canonicalization:     'styx-canonical-json-v1',
    identificationHash:   'sha256',
    commit:               'https://open.charging.cloud/contexts/wwcp+json/roamingNetwork/commit/v3',
    checkpoint:           'https://open.charging.cloud/contexts/wwcp+json/roamingNetwork/checkpoint/v1',
    priceStatement:       'https://open.charging.cloud/contexts/wwcp+json/roamingNetwork/priceStatement/v1',
    signatureAlgorithms:  []
};


describe('the epochs of a roaming network', () => {

    it('are read only from what is one', () => {

        assert.deepEqual(epochOf({ epoch: 1, profiles, status: 'active' }),  { epoch: 1, profiles, status: 'active' });

        assert.deepEqual(epochOf({ epoch: 2, profiles: { ...profiles, identificationHash: 'sha512', signatureAlgorithms: [ 'ML-DSA-65' ] }, status: 'closing',
                                   previous: { epoch: 1, checkpointId: 'ab'.repeat(32), eTags: [] }, finalCheckpointId: 'cd'.repeat(64) }),
                         { epoch: 2, profiles: { ...profiles, identificationHash: 'sha512', signatureAlgorithms: [ 'ML-DSA-65' ] }, status: 'closing',
                           previous: { epoch: 1, checkpointId: 'ab'.repeat(32) }, finalCheckpointId: 'cd'.repeat(64) });

        assert.equal(epochOf(null),                                                   null);
        assert.equal(epochOf({ epoch: 0, profiles, status: 'active' }),               null, 'epochs count from 1');
        assert.equal(epochOf({ epoch: 1, profiles, status: 'open' }),                 null, 'no status of an epoch');
        assert.equal(epochOf({ epoch: 1, status: 'active' }),                         null, 'no profiles');
        assert.equal(epochOf({ epoch: 1, profiles: { ...profiles, signatureAlgorithms: 'all' }, status: 'active' }), null);

    });

    it('say which signature algorithms they take', () => {

        assert.equal(algorithmsText([]),                          'any supported');
        assert.equal(algorithmsText([ 'ML-DSA-65', 'ES512' ]),    'ML-DSA-65, ES512');

    });

    it('show the current one with its profiles, alone while it is the first', () => {

        const root = document.createElement('div');

        render(root, epochCard({ current: epochOf({ epoch: 1, profiles, status: 'active' })!, all: [ epochOf({ epoch: 1, profiles, status: 'active' })! ] }));

        const text = root.textContent ?? '';

        assert.match(text,  /Epoch 1\s*active/);
        assert.match(text,  /Identifications\s*sha256/);
        assert.match(text,  /Signature algorithms\s*any supported/);
        assert.match(text,  /Contexts\s*\S+\/roamingNetwork\/commit\/v3\s*\S+\/roamingNetwork\/checkpoint\/v1\s*\S+\/roamingNetwork\/priceStatement\/v1/);
        assert.doesNotMatch(text, /After|Final checkpoint/);
        assert.equal(root.querySelectorAll('table').length, 0);

    });

    it('show the epoch before, the final checkpoint and every epoch', () => {

        const root    = document.createElement('div');
        const first   = epochOf({ epoch: 1, profiles, status: 'closed', finalCheckpointId: 'ab'.repeat(32) })!;
        const second  = epochOf({ epoch: 2, profiles: { ...profiles, identificationHash: 'sha512' }, status: 'active', previous: { epoch: 1, checkpointId: 'ab'.repeat(32) } })!;

        render(root, epochCard({ current: second, all: [ first, second ] }));

        const text = root.textContent ?? '';

        assert.match(text,  /Epoch 2\s*active/);
        assert.match(text,  new RegExp(`After\\s*epoch 1,\\s*final checkpoint ${'ab'.repeat(32)}`));
        assert.deepEqual([ ...root.querySelectorAll('tbody tr') ].map(row => row.textContent?.replace(/\s+/g, ' ').trim()),
                         [ '1 closed sha256 any supported', '2 active sha512 any supported' ]);

    });

});
