// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_TX_MACHINE_HPP
#define NAXP_TX_MACHINE_HPP

#include "ascii_char_set.hpp"
#include "fault.hpp"
#include "naxp_limits.hpp"
#include "tx.hpp"

#include <cstddef>
#include <memory>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace logmu::detail
{
	class tx_state;

	/// A transition of the canonicalisation machine.
	///
	/// The sets of a state are disjoint, because they come from the minterms, so a walk can stop
	/// at the first one that holds the character.
	struct tx_transition
	{
		ascii_char_set set;

		/// What reading a character of `set` emits. A `copy_marker` in it stands for the
		/// character just read, which is what lets a whole set share one transition rather than
		/// needing one per character.
		std::string output;

		const tx_state* next;
	};

	/// A state of the canonicalisation machine.
	///
	/// A state stands for a set of parses that agree on everything emitted so far, each carrying
	/// whatever it has emitted beyond their common prefix. Two states are the same object when
	/// those sets are equal.
	///
	/// That is sharing on the construction, not on behaviour, so it is weaker than what the state
	/// map gives. The acceptor is the minimal machine because it is acyclic and hash-consed on
	/// what a state does; this one can hold two states that behave alike because their branch
	/// sets differ. `A(Q|q)!QX(B|C)|B(Q|q)!Q(XB|XC)` builds eight states where five would do.
	class tx_state
	{
	public:
		explicit tx_state(int id) noexcept
			: id(id)
		{
		}

		tx_state(const tx_state&) = delete;
		tx_state& operator=(const tx_state&) = delete;

		int id;

		/// The transitions, sorted by the set order.
		///
		/// Filled after every state object exists, because a transition names its target and a
		/// target may have been discovered before the state that reaches it.
		std::vector<tx_transition> transitions;

		/// What is emitted where the input ends here, or nothing where it may not.
		///
		/// This is never empty of meaning: a unified element that has consumed its subject
		/// emits its whole rendering at this point, so the machine can emit more after the last
		/// character than it did on any transition.
		std::optional<std::string> end_output;

		/// Whether the input may end here.
		bool accepts_end_of_text() const noexcept
		{
			return this->end_output.has_value();
		}
	};

	/// The canonicalisation rho as a machine, so that it can be walked rather than recursed over
	/// the tree, and emitted as a table or a switch in another language.
	///
	/// The construction is the classical determinisation of a transducer: a state is a set of
	/// live parses, each with the output it owes beyond what the others have already emitted,
	/// and a transition emits the longest common prefix of what they all owe. That delay is
	/// needed because a unified element emits nothing until it completes, so two branches can
	/// disagree about what has been emitted for as long as the input has not yet told them apart.
	///
	/// It terminates by acyclicity. Every transition consumes a character and so strictly
	/// decreases the longest string any live parse can still read, which bounds the depth.
	///
	/// The state count can be exponential in the length of the naxp, even where both language
	/// machines are small. `[ab]{k}c|([ab]!a){k}d` builds exactly 2^(k+1) states, because
	/// nothing before the final character says which branch was taken, so the machine has to
	/// remember every character it has read in order to emit them later. The lower bound in
	/// `encoding/w3-functionality.md` holds for any finite-state machine that emits rho as it
	/// reads.
	///
	/// The machine is built only where a naxp holds a unified element. Without one rho is the
	/// identity, which needs no machine at all.
	class tx_machine
	{
	public:
		tx_machine(const tx_state* start, std::vector<std::unique_ptr<tx_state>> states, int register_depth)
			: start(start)
			, states(std::move(states))
			, register_depth(register_depth)
		{
		}

		tx_machine(const tx_machine&) = delete;
		tx_machine& operator=(const tx_machine&) = delete;

		const tx_state* start;

		std::vector<std::unique_ptr<tx_state>> states;

		/// How many characters a run has to keep, which is how far back the deepest reference
		/// in any output reaches. Zero where no output holds one.
		int register_depth;

		/// The canonical form of a string, which is the string with each unified element
		/// replaced by its rendering.
		///
		/// @param text The string.
		/// @param canonical The canonical form. Untouched where the string is invalid.
		/// @returns Whether the string is accepted.
		bool try_canonicalise(std::string_view text, std::string& canonical) const;
	};

	/// A character read but not yet placed, held in a pending output as how far back it was read
	/// rather than as its value.
	///
	/// A reference is `copy_marker` followed by one character whose code is `depth_base` plus
	/// the number of steps back, so a pending stays an ordinary string and the prefix, key and
	/// ordering all work on it unchanged. Depth zero is the character just read. Holding the
	/// character this way is what keeps two parses that hold different characters in the same
	/// shape to one state.
	namespace tx_reference
	{
		/// The code a depth of zero is written as, chosen so a pending stays printable.
		inline constexpr int depth_base = 33;

		/// Turns each copy marker into a reference to the character read at this step.
		std::string symbolise(std::string_view emitted);

		/// Every reference is one character older once another character has been read.
		std::string age(std::string_view pending);

		/// How far back a pending reaches, which is what a run has to keep.
		int depth_of(std::string_view pending) noexcept;

		/// How many characters a unit occupies: two for a reference, one for a literal.
		std::size_t unit_length(std::string_view pending, std::size_t at) noexcept;

		/// Whether fixing the character being read could make two pendings agree.
		///
		/// A reference to the character just read is the only thing a narrower block can turn
		/// into something else, so it is the only thing that can close a difference. `0!!|\9`
		/// needs this: one branch owes the literal `0` and the other owes the character read,
		/// and on `0` those are the same output rather than two.
		bool could_reconcile(std::string_view left, std::string_view right) noexcept;
	}

	/// Builds a `tx_machine` from a `tx` by determinisation.
	///
	/// The single-valuedness faults here duplicate the W3 checker, which decides the same
	/// question over the same derivatives, so on an expression the checker has passed they are
	/// unreachable. They are kept as defence in depth, because the two walk different shapes,
	/// the checker walks pairs and this walks sets, and a machine built from an unchecked
	/// expression would otherwise be silently wrong rather than invalid.
	///
	/// The state cap is not a duplicate, and it is reachable on a naxp that is entirely legal.
	/// `[ab]{16}c|([ab]!a){16}d` passes every rule, compiles, and then has no machine.
	namespace tx_machine_builder
	{
		/// Builds the machine for a transduction.
		///
		/// @param root The transduction.
		/// @param factory The factory that made it, whose derivative cache is reused.
		/// @param machine The machine, or null on failure.
		/// @param error The failure, or nothing on success.
		/// @param max_states The budget, lowered by tests so the cap can be reached cheaply.
		/// @returns Whether the machine was built.
		bool try_build(const tx* root, tx_factory& factory, std::unique_ptr<tx_machine>& machine, std::optional<fault>& error, int max_states = limits::max_states);
	}

	/// Merges states of a built `tx_machine` that behave alike.
	///
	/// The builder shares a state only where two branch sets are equal, which is a property of
	/// the construction rather than of behaviour, so it can leave two states that do the same
	/// thing. `A(Q|q)!QX(B|C)|B(Q|q)!Q(XB|XC)` is the smallest witness found: eight states
	/// built, five after this pass.
	///
	/// The machine is acyclic, so a post-order walk reaches every successor before the state
	/// that reaches it, and one bottom-up sweep suffices. A state is keyed on what it emits at
	/// end of text and on its transitions once their targets have been replaced by the
	/// representatives already chosen, which is the usual hash-consing. Merging targets can
	/// leave two transitions agreeing on output and target, and those are unioned, which is safe
	/// because their sets were disjoint.
	///
	/// This makes the machine smaller. It does not make it canonical the way the state map is:
	/// that would need an onward normalisation of where output is emitted, which nothing
	/// downstream asks for.
	namespace tx_machine_merger
	{
		std::unique_ptr<tx_machine> merge(const tx_machine& machine);
	}
}

#endif
