import './styles/app.scss';

// FontAwesome: the CSS ends up in the extracted stylesheet, the referenced
// font files become hashed assets below /assets/.
import '@fortawesome/fontawesome-free/css/fontawesome.css';
import '@fortawesome/fontawesome-free/css/solid.css';

import { auth } from './auth';
import { html, must, render } from './html';
import { logs } from './logs/store';
import { Router } from './router';

import { configurationPage }  from './pages/configuration';
import { dnsPage }            from './pages/dns';
import { homePage }           from './pages/home';
import { ntsPage }            from './pages/nts';
import { ocpiPage }           from './pages/ocpi';
import { peersPage }          from './pages/peers';
import { trafficPage }        from './pages/traffic';
import { loginPage }          from './pages/login';
import { logsPage }           from './pages/logs';
import { notFoundPage }       from './pages/notFound';
import { fromURL } from './basePath';


const root = document.getElementById('app');

if (root === null)
    throw new Error("The '#app' element is missing!");

render(root, html`<div id="page" class="page"></div>`);

const router = new Router({
    routes: [
        // "/" is a route of its own rather than nothing: the sign-in remembers
        // where somebody was going, and for the first visit that is "/". Where
        // it leads depends on who arrived - whoever may read the traffic to
        // the traffic, everybody else to the configuration.
        { path: '/',                              page: homePage,           guard: auth.requireSignIn },

        { path: '/traffic',                       page: trafficPage,        guard: auth.requireSignIn },

        { path: '/configuration',                 page: configurationPage,  guard: auth.requireSignIn },
        { path: '/configuration/dns',             page: dnsPage,            guard: auth.requireSignIn },
        { path: '/configuration/nts',             page: ntsPage,            guard: auth.requireSignIn },
        { path: '/configuration/ocpi',            page: ocpiPage,           guard: auth.requireSignIn },
        // The path stays "partners" - it is what the JSON API calls them, and
        // what the other four components call the same page. Only the word on
        // the screen is "peers", because that is what they are to a hub.
        { path: '/configuration/ocpi/partners',   page: peersPage,          guard: auth.requireSignIn },

        { path: '/logs',                          page: logsPage,           guard: auth.requireSignIn },
        { path: '/login',                         page: loginPage }
    ],
    outlet:       must<HTMLElement>(root, '#page'),
    notFound:     notFoundPage,
    titleSuffix:  ' · RoamingHub'
});

// Signed in: follow the hub's log from now on, whichever page is open - so
// that opening the Logs page shows what happened while somebody was reading
// the configuration, and not an empty list. Only for whoever may read the
// log: the stream would answer 403 otherwise.
// Signed out - by the button, or because the session expired and a request
// came back with 401: close the stream, forget the log, show the sign-in.
//
// The traffic has a stream of its own and is not followed here: it is behind
// its own permission, it can run at a rate the event log never does, and
// nothing outside its page shows it. It opens and closes with that page.
auth.onChange(user => {

    if (user !== null) {

        if (auth.can('readConfiguration'))
            logs.start();

        return;

    }

    logs.stop();

    if (fromURL(location.pathname) !== '/login')
        router.navigate(auth.requireSignIn(new URL(location.href)) ?? '/login', true);

});

// Find out who is signed in before the first page renders, so that a reload on
// a deep URL does not flash the sign-in page on its way back to where it was.
void (async () => {
    await auth.refresh();
    router.start();
})();
