/**
 * The trust anchor of a roaming network: what GET ~/poi/RNs/{id}/trustAnchor answers is read only as one, with its
 * quorum and the text of its QR code, and drawn as a QR code - the same one for the same text, another one for another.
 */

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

const { qrSVG, trustAnchorOf } = await import('./poiTrustAnchor.ts');

const id     = 'ab'.repeat(32);
const other  = 'cd'.repeat(32);


describe('the trust anchor of a roaming network', () => {

    it('is read only from what is one', () => {

        assert.deepEqual(trustAnchorOf({ roamingNetworkId: 'rn1', identityKeyIds: [ id ], uri: `https://hub.example/poi/RNs/rn1?identityKeys=${id}` }),
                         { roamingNetworkId: 'rn1', identityKeyIds: [ id ], minConsensus: 1, uri: `https://hub.example/poi/RNs/rn1?identityKeys=${id}`, qrCode: null },
                         'from a hub writing neither the quorum nor the QR code');

        assert.deepEqual(trustAnchorOf({ roamingNetworkId: 'rn1', identityKeyIds: [ id, other ], minConsensus: 2, uri: 'https://hub.example/poi/RNs/rn1', qrCode: 'WWCPPOI1:Q1L..DF$D4IK1 ' }),
                         { roamingNetworkId: 'rn1', identityKeyIds: [ id, other ], minConsensus: 2, uri: 'https://hub.example/poi/RNs/rn1', qrCode: 'WWCPPOI1:Q1L..DF$D4IK1 ' });

        assert.equal(trustAnchorOf({ roamingNetworkId: 'rn1', identityKeyIds: [ id ], minConsensus: 2, uri: 'https://hub.example' }),          null, 'more holders than keys');
        assert.equal(trustAnchorOf({ roamingNetworkId: 'rn1', identityKeyIds: [ id ], minConsensus: 0, uri: 'https://hub.example' }),          null, 'nobody');
        assert.equal(trustAnchorOf({ roamingNetworkId: 'rn1', identityKeyIds: [ id ], uri: 'https://hub.example', qrCode: 'WWCPPOI1:abc' }),    null, 'not Base45');
        assert.equal(trustAnchorOf({ roamingNetworkId: 'rn1', identityKeyIds: [ id ], uri: 'https://hub.example', qrCode: 'https://evil' }),    null, 'not the text of a trust anchor');

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

    it('draws the text of its CBOR in the alphanumeric mode, with fewer modules than as bytes', () => {

        const text = 'WWCPPOI1:Q1L..DF$D4IK1$CM2D$9FQ$DTVD38DUPCG/DB$CQWE-69MWEW9E0G7%:5I9E -DNPCGECD3D3/D+%5*VD$$EQ:5-3E7WEBAFCPES4FVPC:L5VQE$-D0LEBECZEDV2D7';
        const size = (svg: string) => Number(/viewBox="0 0 (\d+)/.exec(svg)![1]);

        assert.match(qrSVG(text, 'Alphanumeric'), /^<svg[^>]*viewBox/);
        assert.ok(size(qrSVG(text, 'Alphanumeric')) < size(qrSVG(text)));

    });

});
