// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "well_formedness.hpp"

#include "ast.hpp"
#include "fault.hpp"
#include "naxp_message.hpp"
#include "tree_walker.hpp"

#include <optional>
#include <string>
#include <vector>

namespace logmu::detail::well_formedness
{
	namespace
	{
		std::vector<const ast*> children(const ast& node)
		{
			switch (node.kind())
			{
				case ast_kind::sequence:
				{
					std::vector<const ast*> result;

					for (const auto& child : as<ast_sequence>(node).children)
					{
						result.push_back(child.get());
					}

					return result;
				}

				case ast_kind::alternation:
				{
					std::vector<const ast*> result;

					for (const auto& child : as<ast_alternation>(node).children)
					{
						result.push_back(child.get());
					}

					return result;
				}

				case ast_kind::optional:
					return {as<ast_optional>(node).child.get()};

				case ast_kind::interval:
					return {as<ast_interval>(node).child.get()};

				case ast_kind::unified:
					return {as<ast_unified>(node).subject.get(), as<ast_unified>(node).rendering.get()};

				default:
					return {};
			}
		}

		// W2: '!' may not nest

		bool try_check_w2(const ast& node, std::optional<fault>& error)
		{
			if (node.kind() == ast_kind::unified)
			{
				const ast_unified& unified = as<ast_unified>(node);

				if (ast::contains_unified(*unified.subject) || ast::contains_unified(*unified.rendering))
				{
					error = fault(naxp_message::unified_nested);

					return false;
				}
			}

			for (const ast* child : children(node))
			{
				if (!try_check_w2(*child, error))
				{
					return false;
				}
			}

			error.reset();

			return true;
		}

		// W1: a rendering must be one of the strings it replaces

		bool try_check_w1(const ast& node, std::optional<fault>& error)
		{
			if (node.kind() == ast_kind::unified)
			{
				const ast_unified& unified = as<ast_unified>(node);
				std::string rendering;
				const tree_walker::single_string_outcome outcome = tree_walker::try_get_single_string(*unified.rendering, rendering);

				if (outcome == tree_walker::single_string_outcome::too_long)
				{
					error = fault(naxp_message::element_too_long);

					return false;
				}

				if (outcome == tree_walker::single_string_outcome::multiple)
				{
					error = fault(unified.form == unified_form::reproduced
						? naxp_message::reproduced_subject_not_single
						: naxp_message::rendering_not_single);

					return false;
				}

				bool too_long = false;

				if (!tree_walker::generates(*unified.subject, rendering, too_long))
				{
					if (too_long)
					{
						error = fault(naxp_message::element_too_long);

						return false;
					}

					error = rendering.empty()
						? fault(naxp_message::element_not_deletable)
						: fault(naxp_message::rendering_not_generated, rendering);

					return false;
				}
			}

			for (const ast* child : children(node))
			{
				if (!try_check_w1(*child, error))
				{
					return false;
				}
			}

			error.reset();

			return true;
		}
	}

	bool try_check(const ast& tree, std::optional<fault>& error)
	{
		// W2 first, because W1 reads inside both operands of a '!' and the answer is only
		// meaningful once nothing is hidden in there.
		if (!try_check_w2(tree, error))
		{
			return false;
		}

		return try_check_w1(tree, error);
	}
}
