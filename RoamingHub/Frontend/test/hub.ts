/*
 * The roaming hub as the stand-in every kind's page tests share - WWCP_Node's
 * test/node.ts: a stand-in for fetch, and a document of happy-dom to draw a
 * page into. What the pages' tests in src/pages/*.test.ts open their pages
 * with.
 *
 * Imported first, before a page: lit-html looks for the document as it is
 * loaded, and the pages load it.
 *
 *   import { open, ... } from '../../test/hub.ts';
 *   const { peersPage } = await import('./peers.ts');
 */

import { standIn } from '@node/../test/node.ts';
export * from '@node/../test/node.ts';

standIn({ name: 'Roaming Hub', icon: 'fa-circle-nodes' });
