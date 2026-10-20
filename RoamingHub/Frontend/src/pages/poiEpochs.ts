/**
 * The epochs of a roaming network (SPECIFICATION.md of WWCP POI, section 11): one history under one set of profiles,
 * the algorithms its identifications, trees and signatures are calculated with. A new epoch begins only when they must
 * change, e.g. from SHA-256 to SHA-512 identifications, or from ECDSA to ML-DSA signatures; its final checkpoint closes
 * the one before, and the first commit of the next one names it.
 *
 * - The current epoch, ~/RNs/{id}/history/epoch: its number, its status - active, closing once its final checkpoint is
 *   made, closed once it is signed - its profiles, the epoch before and the final checkpoint.
 * - Every epoch served here, ~/RNs/{id}/history/epochs, in their order.
 */

import { poiEntity, poiList, type Entity } from '../api/poi';
import { html, nothing, type TemplateResult } from '@node/view';


/** The profiles of an epoch. */
export interface EpochProfiles {
    canonicalization:     string;
    identificationHash:   string;
    commit:               string;
    checkpoint:           string;
    priceStatement:       string;
    signatureAlgorithms:  string[];
}

/** An epoch, as the POI API answers it. */
export interface Epoch {
    epoch:               number;
    status:              'active' | 'closing' | 'closed';
    profiles:            EpochProfiles;
    previous?:           { epoch: number; checkpointId: string };
    finalCheckpointId?:  string;
}


/** The given answer as an epoch, or null when it is none. */
export function epochOf(Answer: unknown): Epoch | null {

    if (typeof Answer !== 'object' || Answer === null)
        return null;

    const { epoch, status, profiles, previous, finalCheckpointId } = Answer as Record<string, unknown>;
    const given = profiles as Record<string, unknown> | null | undefined;

    if (typeof epoch !== 'number' || !Number.isInteger(epoch) || epoch < 1 ||
        (status !== 'active' && status !== 'closing' && status !== 'closed') ||
        typeof given !== 'object' || given === null ||
        typeof given.identificationHash !== 'string' ||
        !Array.isArray(given.signatureAlgorithms) || !given.signatureAlgorithms.every(algorithm => typeof algorithm === 'string'))
    {
        return null;
    }

    const before = previous as { epoch?: unknown; checkpointId?: unknown } | undefined;

    return {
        epoch,
        status,
        profiles: {
            canonicalization:     typeof given.canonicalization === 'string' ? given.canonicalization : '',
            identificationHash:   given.identificationHash,
            commit:               typeof given.commit           === 'string' ? given.commit           : '',
            checkpoint:           typeof given.checkpoint       === 'string' ? given.checkpoint       : '',
            priceStatement:       typeof given.priceStatement   === 'string' ? given.priceStatement   : '',
            signatureAlgorithms:  given.signatureAlgorithms as string[]
        },
        ...(typeof before?.epoch === 'number' && typeof before.checkpointId === 'string'
                ? { previous: { epoch: before.epoch, checkpointId: before.checkpointId } }
                : {}),
        ...(typeof finalCheckpointId === 'string' ? { finalCheckpointId } : {})
    };

}


/** The epochs of the roaming network at the given path, the current one and all served here. */
export interface Epochs {
    current:  Epoch;
    all:      Epoch[];
}


/**
 * The epochs of the roaming network at the given path below the POI API; null where it has no history here, or they
 * could not be asked: the page shows no card then.
 */
export async function loadEpochs(Path: string, Signal?: AbortSignal): Promise<Epochs | null> {

    try
    {

        const [ current, all ] = await Promise.all([
                                     poiEntity(`${Path}/history/epoch`, {}, Signal),
                                     poiList<Entity>(`${Path}/history/epochs`, {}, Signal)
                                 ]);

        const epoch = epochOf(current.entity);

        return epoch === null
                   ? null
                   : { current: epoch, all: all.entries.map(epochOf).filter((one): one is Epoch => one !== null) };

    }
    catch
    {
        return null;
    }

}


/** The status of an epoch as a chip. */
function statusChip(Status: Epoch['status']): TemplateResult {

    const colour = Status === 'active' ? ' ok' : Status === 'closing' ? ' warn' : '';

    return html`<span class="chip poi-epoch-status${colour}">${Status}</span>`;

}


/** The signature algorithms an epoch takes: every one, where it names none. */
export function algorithmsText(Algorithms: string[]): string {

    return Algorithms.length === 0
               ? 'any supported'
               : Algorithms.join(', ');

}


/**
 * The card of the epochs of a roaming network: the current one with its profiles, how it came after the one before, and
 * every epoch served here.
 *
 * @param Epochs  The epochs.
 */
export function epochCard(Epochs: Epochs): TemplateResult {

    const { current, all } = Epochs;

    return html`
        <section class="card poi-epochs">
            <h2><i class="fa-solid fa-timeline"></i> Epoch ${current.epoch} ${statusChip(current.status)}</h2>
            <p class="hint">
                The algorithms its commits, checkpoints and signatures are calculated with. A new epoch begins only when they
                must change; the final checkpoint of an epoch leads to the next one.
            </p>
            <div class="kv-list">
                <div class="kv"><span class="k">Identifications</span><span class="poi-value"><code>${current.profiles.identificationHash}</code></span></div>
                <div class="kv"><span class="k">Signature algorithms</span><span class="poi-value">${algorithmsText(current.profiles.signatureAlgorithms)}</span></div>
                <div class="kv"><span class="k">Canonicalization</span><span class="poi-value"><code>${current.profiles.canonicalization}</code></span></div>
                <div class="kv"><span class="k">Profiles</span><span class="poi-value">
                    <div><code>${current.profiles.commit}</code></div>
                    <div><code>${current.profiles.checkpoint}</code></div>
                    <div><code>${current.profiles.priceStatement}</code></div></span></div>
                ${current.previous
                      ? html`<div class="kv"><span class="k">After</span><span class="poi-value">epoch ${current.previous.epoch},
                                 final checkpoint <code class="path">${current.previous.checkpointId}</code></span></div>`
                      : nothing}
                ${current.finalCheckpointId
                      ? html`<div class="kv"><span class="k">Final checkpoint</span><span class="poi-value"><code class="path">${current.finalCheckpointId}</code></span></div>`
                      : nothing}
            </div>
            ${all.length > 1
                  ? html`
                      <div class="table-scroll">
                          <table class="table poi-table">
                              <thead><tr><th>Epoch</th><th>Status</th><th>Identifications</th><th>Signature algorithms</th></tr></thead>
                              <tbody>
                                  ${all.map(epoch => html`
                                      <tr>
                                          <td>${epoch.epoch}</td>
                                          <td>${statusChip(epoch.status)}</td>
                                          <td><code>${epoch.profiles.identificationHash}</code></td>
                                          <td>${algorithmsText(epoch.profiles.signatureAlgorithms)}</td>
                                      </tr>`)}
                              </tbody>
                          </table>
                      </div>`
                  : nothing}
        </section>`;

}
