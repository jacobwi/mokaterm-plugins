// The Developer Utilities tool's page side: a guard on pastes, and the regex tester's worker.
//
// Every keystroke in a Blazor text box sends the whole box to .NET, and on the web host a message over its receive limit
// ends the circuit, so a paste that would make a box larger than the tool takes is stopped here, before it reaches the
// box, and .NET says why. Nothing is cut: the box keeps what it had.
//
// The regex tester runs JavaScript's own engine, the one Mokaterm's terminal triggers use, in a dedicated worker. A
// JavaScript regex has no timeout, so the only way to stop a pattern that backtracks for hours is to terminate the
// worker it runs in; the page does that after the time .NET gives, and starts a fresh worker for the next run.
//
// Served as _content/Moka.DevTools/js/devtools.js. Imports are relative so a host under a path base resolves them.

import { createInstances, invoke, listen } from '../../Mokaterm.UI.Common/js/interop.js';

const instances = createInstances();

// How long a new worker gets to say it is ready before the tester reports that it cannot run here.
const StartupMs = 5000;

// undefined until the first run asks; null when the terminal's trigger checks are not there to ask.
let triggers;

/** Watches pastes and drops inside `root`. `limits` is `{ maxChars, maxBytes }`; a maxBytes of 0 means no byte limit. */
export function attach(root, dotNet, limits) {
	const state = { dotNet, disposables: [], limits, worker: null, ready: null, pending: null, ticket: 0, runs: 0 };
	const id = instances.add(state);
	state.disposables.push(listen(root, 'paste', event => guard(state, event, event.clipboardData), true));
	state.disposables.push(listen(root, 'drop', event => guard(state, event, event.dataTransfer), true));
	state.disposables.push({ dispose: () => stopWorker(state) });
	return id;
}

export function release(id) {
	instances.release(id);
}

/**
 * Runs `request` ({ pattern, flags, text, timeoutMs, maxMatches, maxGroups }) in the worker. Resolves with
 * { status, error, matches, total, truncated, elapsed, trigger }; status is ok, syntax, timeout, unavailable or
 * superseded. A newer run stops one still going instead of waiting behind it.
 */
export async function runRegex(id, request) {
	const state = instances.get(id);
	if (!state) {
		return unavailable('The tool has closed.');
	}

	const ticket = ++state.ticket;
	if (state.pending) {
		stopWorker(state);
	}

	const trigger = await checkWithTriggers(request.pattern, request.flags);
	if (!state.worker) {
		try {
			startWorker(state);
		}
		catch {
			return unavailable('The page would not start the worker the tester runs in, so nothing ran.');
		}
	}

	const ready = await state.ready;
	if (state.disposed || ticket !== state.ticket) {
		return superseded(trigger);
	}

	if (!ready) {
		stopWorker(state);
		return unavailable('The worker the tester runs in did not start, so nothing ran.');
	}

	const worker = state.worker;
	const runId = ++state.runs;
	return new Promise(resolve => {
		const finish = result => {
			clearTimeout(timer);
			worker.removeEventListener('message', onMessage);
			if (state.pending && state.pending.runId === runId) {
				state.pending = null;
			}

			resolve({ ...result, trigger });
		};

		const onMessage = event => {
			if (event.data && event.data.runId === runId) {
				finish(event.data.result);
			}
		};

		const timer = setTimeout(() => {
			// The pattern is still running; terminating the worker is the only way to stop it. The run is taken off
			// first, so stopping the worker does not report it as replaced.
			state.pending = null;
			stopWorker(state);
			finish({ status: 'timeout', error: null, matches: null, total: 0, truncated: false, elapsed: request.timeoutMs });
		}, request.timeoutMs);

		state.pending = { runId, finish };
		worker.addEventListener('message', onMessage);
		worker.postMessage({
			runId,
			pattern: request.pattern,
			flags: request.flags,
			text: request.text,
			maxMatches: request.maxMatches,
			maxGroups: request.maxGroups,
		});
	});
}

function startWorker(state) {
	const worker = new Worker(new URL('./regex-worker.js', import.meta.url));
	state.worker = worker;
	state.ready = new Promise(resolve => {
		const timer = setTimeout(() => resolve(false), StartupMs);
		const onReady = event => {
			if (event.data && event.data.ready === true) {
				clearTimeout(timer);
				worker.removeEventListener('message', onReady);
				resolve(true);
			}
		};

		worker.addEventListener('message', onReady);
		worker.addEventListener('error', () => {
			clearTimeout(timer);
			resolve(false);
		}, { once: true });
	});
}

function stopWorker(state) {
	const pending = state.pending;
	state.pending = null;
	if (state.worker) {
		state.worker.terminate();
		state.worker = null;
		state.ready = null;
	}

	if (pending) {
		pending.finish({ status: 'superseded', error: null, matches: null, total: 0, truncated: false, elapsed: 0 });
	}
}

// What the terminal's own trigger checks make of the pattern. triggers.js is not a published contract, so any part of
// this that is missing or throws means "not asked" rather than an error.
async function checkWithTriggers(pattern, flags) {
	if (triggers === undefined) {
		try {
			triggers = await import('../../Mokaterm.UI.Terminal/js/triggers.js');
		}
		catch {
			triggers = null;
		}
	}

	if (!triggers || typeof triggers.checkPattern !== 'function') {
		return null;
	}

	try {
		const result = triggers.checkPattern(pattern, typeof flags === 'string' && flags.includes('i'));
		if (result && typeof result.error === 'string') {
			return { refused: true, reason: result.error };
		}

		return result && result.regex ? { refused: false, reason: null } : null;
	}
	catch {
		return null;
	}
}

function guard(state, event, transfer) {
	const target = event.target;
	if (!isTextBox(target) || !transfer) {
		return;
	}

	const inserted = transfer.getData('text/plain') || '';
	const start = typeof target.selectionStart === 'number' ? target.selectionStart : target.value.length;
	const end = typeof target.selectionEnd === 'number' ? target.selectionEnd : start;
	const next = target.value.length - (end - start) + inserted.length;
	let refusal = next > state.limits.maxChars ? 'chars' : null;
	if (!refusal && state.limits.maxBytes > 0 && next * 6 > state.limits.maxBytes) {
		// Only now is the exact size worth working out: the box as the JSON message that would carry it.
		const text = target.value.slice(0, start) + inserted + target.value.slice(end);
		if (new TextEncoder().encode(JSON.stringify(text)).length > state.limits.maxBytes) {
			refusal = 'bytes';
		}
	}

	if (refusal) {
		event.preventDefault();
		event.stopPropagation();
		invoke(state, 'OnPasteRefused', next, refusal);
	}
}

function isTextBox(element) {
	if (!element || element.readOnly || element.disabled) {
		return false;
	}

	if (element.tagName === 'TEXTAREA') {
		return true;
	}

	return element.tagName === 'INPUT' && ['', 'text', 'password', 'search', 'url', 'email'].includes(element.type);
}

function unavailable(error) {
	return { status: 'unavailable', error, matches: null, total: 0, truncated: false, elapsed: 0, trigger: null };
}

function superseded(trigger) {
	return { status: 'superseded', error: null, matches: null, total: 0, truncated: false, elapsed: 0, trigger };
}
