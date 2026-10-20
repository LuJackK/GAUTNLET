#pragma once
#include <cstddef>
#include <cstdint>
#include <optional>

namespace movement_capture {
struct ModifierMaskField { size_t offset, size; };

// Resolve the enum by name at runtime, and bound every read by BOTH schema
// spans. Either mask can activate a state. A missing/failed read is unknown.
template <typename Reader>
std::optional<bool> ReadModifierState(
    const void* pawn, std::optional<size_t> property_offset,
    std::optional<ModifierMaskField> ordinary, std::optional<ModifierMaskField> predicted,
    std::optional<int64_t> flag, Reader read) {
  if (!pawn || !property_offset || !ordinary || !predicted || !flag || *flag < 0) return std::nullopt;
  const uint64_t word = static_cast<uint64_t>(*flag) / 32;
  if (word >= ordinary->size / sizeof(uint32_t) || word >= predicted->size / sizeof(uint32_t))
    return std::nullopt;
  const unsigned char* property = nullptr;
  if (!read(static_cast<const unsigned char*>(pawn) + *property_offset, &property, sizeof(property)) ||
      !property) return std::nullopt;
  uint32_t a = 0, b = 0;
  if (!read(property + ordinary->offset + word * 4, &a, sizeof(a)) ||
      !read(property + predicted->offset + word * 4, &b, sizeof(b))) return std::nullopt;
  return ((a | b) & (uint32_t{1} << (*flag % 32))) != 0;
}
}  // namespace movement_capture
