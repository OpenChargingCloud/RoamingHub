/**
 * The trust anchor of a roaming network: what GET ~/poi/RNs/{id}/trustAnchor answers is read only as one, and its URI
 * drawn as a QR code - the same one for the same URI, another one for another.
 */

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

const { qrSVG, trustAnchorOf } = await import('./poiTrustAnchor.ts');

const id = 'ab'.repeat(32);


describe('the trust anchor of a roaming network', () => {

    it('is read only from what is one', () => {

        assert.deepEqual(trustAnchorOf({ roamingNetworkId: 'rn1', identityKeyIds: [ id ], uri: `https://hub.example/poi/RNs/rn1?identityKeys=${id}` }),
                         { roamingNetworkId: 'rn1', identityKeyIds: [ id ], uri: `https://hub.example/poi/RNs/rn1?identityKeys=${id}` });

        assert.equal(trustAnchorOf(null),                                                                            null);
        assert.equal(trustAnchorOf({ roamingNetworkId: 'rn1', identityKeyIds: [], uri: 'https://hub.example' }),      null, 'no identity key');
        assert.equal(trustAnchorOf({ roamingNetworkId: 'rn1', identityKeyIds: [ 'AB' ], uri: 'https://hub.example' }), null, 'not a thumbprint');
        assert.equal(trustAnchorOf({ roamingNetworkId: 'rn1', identityKeyIds: [ id ], uri: 'javascript:alert(1)' }),  null, 'neither http nor https');

    });

    it('draws its URI as a QR code, the same one for the same URI', () => {

        const uri = `https://hub.example/poi/RNs/rn1?identityKeys=${id}`;

        assert.match(qrSVG(uri), /^<svg[^>]*viewBox/);
        assert.equal(qrSVG(uri), qrSVG(uri));
        assert.notEqual(qrSVG(uri), qrSVG(uri.replace('rn1', 'rn2')));
        assert.doesNotMatch(qrSVG(uri), /hub\.example/, 'the text itself does not go into the SVG');

    });

});
