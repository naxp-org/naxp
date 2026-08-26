// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { formatNaxpMessage } from './naxp-message.js';

/**
 * A refusal: which message, where in the source, and what the message needs to say it.
 *
 * The text is not held. A refusal names a member of `NaxpMessage` and, where that message
 * interpolates something, supplies one string; the words are looked up only when somebody asks for
 * them. So nothing between the point of refusal and the public surface handles prose.
 *
 * An `offset` and a `length` of zero together mean the whole naxp, which is what most refusals
 * want and none of them have to say. Only the parser knows a position, and only the public surface
 * knows how long the source is, so the substitution happens there. Every refusal that does name a
 * position uses a length of at least one, or it would read as this.
 */
export class NaxpError {
	/**
	 * @param {string} message Which refusal this is, a member of `NaxpMessage`.
	 * @param {string | null} [argument] What the message interpolates, or null where it takes none.
	 * @param {number} [offset] Where the fault starts, or zero for the naxp as a whole.
	 * @param {number} [length] How much is at fault, or zero for the naxp as a whole.
	 */
	constructor(message, argument = null, offset = 0, length = 0) {
		this.message = message;
		this.argument = argument;
		this.offset = offset;
		this.length = length;

		Object.freeze(this);
	}

	/** Whether this refusal belongs to the naxp as a whole rather than to a place in it. */
	get isWholeNaxp() {
		return this.offset === 0 && this.length === 0;
	}

	/**
	 * The stable identifier for this refusal, such as `NAXP1002`.
	 *
	 * The number alone. `NaxpMessage` spells each member `NAXP1002_IntervalHyphen` so that somebody
	 * reading the library can see at a glance which refusal a line is about, but that half is a
	 * note to ourselves: it is not part of the identifier, it would read as a promise about
	 * wording we have not made, and it must never reach a caller.
	 */
	get code() {
		const hint = this.message.indexOf('_');

		return hint < 0 ? this.message : this.message.slice(0, hint);
	}

	/** What is wrong, and where practical what to write instead. */
	get text() {
		return formatNaxpMessage(this.message, this.argument);
	}

	/** @returns {string} The code, the span where there is one, and the text. */
	toString() {
		return this.isWholeNaxp
			? `${this.code}: ${this.text}`
			: `${this.code} at ${this.offset}..${this.offset + this.length}: ${this.text}`;
	}
}
