// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_ARRAY_VIEW_HPP
#define NAXP_ARRAY_VIEW_HPP

#include <array>
#include <cstddef>
#include <vector>

namespace logmu::detail
{
	/// A read-only view of a contiguous run of values that somebody else owns.
	///
	/// This is what `std::span<const Value>` does in C++20. The library stops at C++17 so that
	/// the toolchains R has shipped since 4.1 can build it, and this is the one piece of the
	/// C++20 library it could not do without: the factories take a run of parts that is
	/// sometimes a vector and sometimes a two element array on the stack, and a view lets both
	/// callers pass what they have without copying.
	///
	/// The converting constructors are deliberately implicit, which is the whole point of a
	/// view type: a caller writes `factory.concat(parts)` whatever `parts` is.
	template <typename Value>
	class array_view
	{
	public:
		using value_type = Value;
		using size_type = std::size_t;
		using const_iterator = const Value*;
		using iterator = const_iterator;

		constexpr array_view() noexcept = default;

		constexpr array_view(const Value* data, std::size_t size) noexcept
			: first(data),
			count(size)
		{
		}

		template <std::size_t Count>
		constexpr array_view(const Value (&values)[Count]) noexcept
			: first(values),
			count(Count)
		{
		}

		template <std::size_t Count>
		constexpr array_view(const std::array<Value, Count>& values) noexcept
			: first(values.data()),
			count(Count)
		{
		}

		array_view(const std::vector<Value>& values) noexcept
			: first(values.data()),
			count(values.size())
		{
		}

		constexpr const Value* data() const noexcept
		{
			return this->first;
		}

		constexpr std::size_t size() const noexcept
		{
			return this->count;
		}

		constexpr bool empty() const noexcept
		{
			return this->count == 0;
		}

		constexpr const Value& operator[](std::size_t index) const noexcept
		{
			return this->first[index];
		}

		constexpr const Value& front() const noexcept
		{
			return this->first[0];
		}

		constexpr const Value& back() const noexcept
		{
			return this->first[this->count - 1];
		}

		constexpr const_iterator begin() const noexcept
		{
			return this->first;
		}

		constexpr const_iterator end() const noexcept
		{
			return this->first + this->count;
		}

	private:
		const Value* first = nullptr;
		std::size_t count = 0;
	};
}

#endif
