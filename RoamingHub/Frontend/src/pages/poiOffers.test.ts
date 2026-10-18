/**
 * The charging tariff offers covering a charging pool, a charging station or an EVSE: when their list is asked again,
 * and how a time chosen in a field is told.
 */

import '../../test/hub.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

const { localTimeOf, nextRefresh, timeOf } = await import('./poiOffers.ts');


describe('the charging tariff offers covering a member', () => {

    it('are asked again at the next notBefore of one still planned, right after the next notAfter, and every minute while those applying now are shown', () => {

        const now     = Date.parse('2026-10-18T12:00:30.000Z');

        const offers  = [ { '@id': 'summer', notBefore: '2026-06-01T00:00:00.000Z', notAfter: '2026-08-31T23:59:59.999Z' },
                          { '@id': 'flash',  notBefore: '2026-10-18T12:30:00.000Z', notAfter: '2026-10-18T13:00:00.000Z' },
                          { '@id': 'hpc',    notBefore: '2026-10-01T00:00:00.000Z' },
                          { '@id': 'night' } ];

        // The flash offer begins next; the summer is over, the HPC offer and the night offer go on for good.
        assert.equal(nextRefresh(offers,            'all', now),  Date.parse('2026-10-18T12:30:00.000Z'));
        assert.equal(nextRefresh(offers.slice(2),   'all', now),  null);

        // Those applying now change as opening times open and close, by the minute.
        assert.equal(nextRefresh(offers.slice(2),   'now', now),  Date.parse('2026-10-18T12:01:00.000Z'));

        // Right after its notAfter, which is inclusive.
        assert.equal(nextRefresh([ { '@id': 'ending', notAfter: '2026-10-18T12:00:40.000Z' } ], 'at', now),  Date.parse('2026-10-18T12:00:40.001Z'));

        // An hour at the most: then it looks again.
        assert.equal(nextRefresh([ { '@id': 'later', notBefore: '2026-12-01T00:00:00.000Z' } ], 'all', now), now + 3_600_000);

    });

    it('tell a time chosen in a field as it is, in the time zone of the browser', () => {

        const time = new Date(2026, 9, 12, 20, 0);

        assert.equal(localTimeOf(time),                          '2026-10-12T20:00');
        assert.equal(timeOf('2026-10-12T20:00')!.getTime(),      time.getTime());
        assert.equal(timeOf(''),                                 null);
        assert.equal(timeOf('yesterday'),                        null);

    });

});
