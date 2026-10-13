// The regex tester's worker: one run at a time, JavaScript's own engine, offsets back. The page terminates this worker
// when a run takes longer than it allows, which is the only way to stop a JavaScript regex, so nothing here needs a
// time check of its own. A classic worker with no imports, so it starts the same in every host.
'use strict';

let hasIndices = true;
try {
	new RegExp('', 'd');
}
catch {
	hasIndices = false;
}

self.onmessage = event => {
	const request = event.data || {};
	let result;
	try {
		result = run(String(request.pattern ?? ''), String(request.flags ?? ''), String(request.text ?? ''), request.maxMatches | 0, request.maxGroups | 0);
	}
	catch (error) {
		result = failed('syntax', describe(error));
	}

	self.postMessage({ runId: request.runId, result });
};

self.postMessage({ ready: true });

function run(pattern, flags, text, maxMatches, maxGroups) {
	if (!hasIndices) {
		return failed('unavailable', 'This JavaScript engine has no match indices (the d flag), which the tester needs.');
	}

	// Compiled first with the flags as given, so an error names those and not the d added for the offsets.
	try {
		new RegExp(pattern, flags);
	}
	catch (error) {
		return failed('syntax', describe(error));
	}

	const regex = new RegExp(pattern, flags.includes('d') ? flags : flags + 'd');

	const names = groupNames(pattern, flags.includes('v'));
	const global = regex.global;
	const fullUnicode = regex.unicode || regex.unicodeSets === true;
	const matches = [];
	let total = 0;
	let truncated = false;
	let groupsLeft = maxGroups;
	const started = performance.now();
	regex.lastIndex = 0;
	for (;;) {
		const match = regex.exec(text);
		if (!match) {
			break;
		}

		if (matches.length >= maxMatches) {
			truncated = true;
			break;
		}

		total++;
		const shaped = shape(match, names, groupsLeft);
		groupsLeft -= shaped.groups.length;
		matches.push(shaped);
		if (!global) {
			break;
		}

		// An empty match leaves lastIndex where it was, so step past it the way matchAll does.
		if (match[0].length === 0) {
			regex.lastIndex = advance(text, regex.lastIndex, fullUnicode);
		}
	}

	return { status: 'ok', error: null, matches, total, truncated, elapsed: performance.now() - started };
}

function shape(match, names, groupsLeft) {
	const indices = match.indices;
	const groups = [];
	const known = names.length === match.length;
	for (let number = 1; number < match.length && groups.length < groupsLeft; number++) {
		const span = indices[number];
		groups.push({
			number,
			name: known ? names[number] : null,
			start: span ? span[0] : -1,
			end: span ? span[1] : -1,
		});
	}

	return { index: indices[0][0], end: indices[0][1], groups };
}

function advance(text, index, fullUnicode) {
	if (!fullUnicode || index + 1 >= text.length) {
		return index + 1;
	}

	const code = text.charCodeAt(index);
	const next = text.charCodeAt(index + 1);
	return code >= 0xd800 && code <= 0xdbff && next >= 0xdc00 && next <= 0xdfff ? index + 2 : index + 1;
}

// Each capturing group's name by its number (null for an unnamed one), read from the pattern in the order the groups
// open. Escapes and character classes are stepped over; with the v flag classes can nest.
function groupNames(source, nestedClasses) {
	const names = [null];
	let classDepth = 0;
	for (let i = 0; i < source.length; i++) {
		const c = source[i];
		if (c === '\\') {
			i++;
			continue;
		}

		if (classDepth > 0) {
			if (c === '[' && nestedClasses) {
				classDepth++;
			}
			else if (c === ']') {
				classDepth--;
			}

			continue;
		}

		if (c === '[') {
			classDepth = 1;
			continue;
		}

		if (c !== '(') {
			continue;
		}

		if (source[i + 1] !== '?') {
			names.push(null);
		}
		else if (source[i + 2] === '<' && source[i + 3] !== '=' && source[i + 3] !== '!') {
			const close = source.indexOf('>', i + 3);
			names.push(close > 0 ? source.slice(i + 3, close) : null);
		}
	}

	return names;
}

function failed(status, error) {
	return { status, error, matches: null, total: 0, truncated: false, elapsed: 0 };
}

// The engine's message without its "Invalid regular expression: /.../flags: " opening, which repeats the pattern.
function describe(error) {
	const message = String((error && error.message) || error || '').replace(/^Invalid regular expression: \/.*\/[a-z]*: /s, '');
	return message.length === 0 ? 'This is not a valid pattern.' : message[0].toUpperCase() + message.slice(1);
}
