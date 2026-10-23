#pragma once

#include "recorder_core.h"
#include "modifier_reader.h"
#include <iostream>
#include <cmath>
#include <cstring>
#include <sstream>
#include "modlock/gameinterop/pawn_observer.h"
#include "modlock/gameinterop/entity_abi.h"

namespace movement_capture {
// Class and schema field names are identifier characters; escape defensively anyway.
inline void JsonString(std::ostream& out, const std::string& text) {
  out << '"';
  for (const char c : text) {
    if (c == '"' || c == '\\') out << '\\' << c;
    else if (static_cast<unsigned char>(c) < 0x20) out << '?';
    else out << c;
  }
  out << '"';
}
inline void NullableBool(std::ostream& out, std::optional<bool> value) {
  if (!value) out << "null";
  else out << (*value ? "true" : "false");
}

// Upstream MovementState contains default-false booleans even when a field is
// unavailable. Guard them with schema/ownership evidence instead of exporting
// unavailable as false. Modifier flags are read only from a non-null property.
class FrameCapabilities {
 public:
  void Reset() { *this = FrameCapabilities{}; }
  void Resolve() {
    if (resolved_) return;
    auto schema = modlock::gameinterop::ResolveSchemaSystem();
    if (!schema) return;
    resolved_ = true;
    auto field = [&](const char* type, const char* name, size_t size) -> std::optional<size_t> {
      auto f = modlock::gameinterop::SchemaFieldOf(*schema, "server.dll", type, name);
      if (!f || f->size < size) return std::nullopt;
      return f->offset;
    };
    property_ = field("CBaseEntity", "m_pModifierProp", sizeof(void*));
    auto mask = [&](const char* name) -> std::optional<ModifierMaskField> {
      auto f = modlock::gameinterop::SchemaFieldOf(*schema, "server.dll", "CModifierProperty", name);
      if (!f || f->size < sizeof(uint32_t)) return std::nullopt;
      return ModifierMaskField{f->offset, f->size};
    };
    ordinary_ = mask("m_bvEnabledStateMask");
    predicted_ = mask("m_bvEnabledPredictedStateMask");
    auto state = [&](const char* name) -> std::optional<int64_t> {
      auto value = modlock::gameinterop::SchemaEnumValueOf(*schema, "server.dll", "EModifierState", name);
      if (!value || *value < 0) return std::nullopt;
      return *value;
    };
    ground_dash_ = state("MODIFIER_STATE_GROUND_DASHING");
    air_dash_ = state("MODIFIER_STATE_AIR_DASHING");
    climbing_ = state("MODIFIER_STATE_IS_CLIMBING_ROPE");
    wall_slide_ = state("MODIFIER_STATE_WALLSLIDE");
    std::cout << "capture-modifier-layout {\"reader_revision\":2,\"ground_dash\":";
    Optional(std::cout, ground_dash_);
    std::cout << ",\"air_dash\":"; Optional(std::cout, air_dash_);
    std::cout << ",\"climbing\":"; Optional(std::cout, climbing_);
    std::cout << ",\"wall_slide\":"; Optional(std::cout, wall_slide_);
    std::cout << ",\"ordinary_mask_bytes\":" << (ordinary_ ? ordinary_->size : 0)
              << ",\"predicted_mask_bytes\":" << (predicted_ ? predicted_->size : 0) << "}" << std::endl;
    sliding_ = field("CCitadel_Ability_Slide", "m_bIsSliding", 1).has_value();
    friction_ = field("CBaseEntity", "m_flFriction", sizeof(float));
    services_ = field("CBasePlayerPawn", "m_pMovementServices", sizeof(void*));
    surface_friction_ = field("CPlayer_MovementServices_Humanoid", "m_flSurfaceFriction", sizeof(float));
    ground_normal_ = field("CPlayer_MovementServices_Humanoid", "m_groundNormal", sizeof(float) * 3);
    fall_velocity_ = field("CPlayer_MovementServices_Humanoid", "m_flFallVelocity", sizeof(float));
    colliding_ = field("CCitadelPlayer_MovementServices", "m_bColliding", 1);
    landed_ = field("CCitadelPlayer_MovementServices", "m_bLandedOnGround", 1);
    channeling_ = field("CCitadelBaseAbility", "m_bChanneling", 1).has_value();
  }
  std::optional<bool> Modifier(void* pawn, std::optional<int64_t> flag) const {
    return ReadModifierState(pawn, property_, ordinary_, predicted_, flag,
                             modlock::gameinterop::ReadNative);
  }
  std::optional<bool> GroundDash(void* pawn) const { return Modifier(pawn, ground_dash_); }
  std::optional<bool> AirDash(void* pawn) const { return Modifier(pawn, air_dash_); }
  std::optional<bool> Climbing(void* pawn) const { return Modifier(pawn, climbing_); }
  std::optional<bool> WallSliding(void* pawn) const { return Modifier(pawn, wall_slide_); }
  // Pawn friction coefficient (the slide writes it; 1 otherwise) and the
  // movement services' surface friction. Unresolved or unreadable is null.
  std::optional<float> Friction(const void* pawn) const { return ReadFloat(pawn, friction_); }
  std::optional<float> SurfaceFriction(const void* pawn) const {
    if (!pawn || !services_ || !surface_friction_) return std::nullopt;
    const unsigned char* services = nullptr;
    if (!modlock::gameinterop::ReadNative(static_cast<const unsigned char*>(pawn) + *services_,
                                          &services, sizeof(services)) || !services)
      return std::nullopt;
    return ReadFloat(services, surface_friction_);
  }
  // Movement-services state for walls, slopes and landings. The ground normal
  // is the last ground plane the services saw; it is stale while airborne.
  std::optional<std::array<double, 3>> GroundNormal(const void* pawn) const {
    const unsigned char* services = Services(pawn);
    if (!services || !ground_normal_) return std::nullopt;
    float v[3] = {};
    if (!modlock::gameinterop::ReadNative(services + *ground_normal_, v, sizeof(v)) ||
        !std::isfinite(v[0]) || !std::isfinite(v[1]) || !std::isfinite(v[2]))
      return std::nullopt;
    return std::array<double, 3>{v[0], v[1], v[2]};
  }
  std::optional<float> FallVelocity(const void* pawn) const { return ReadFloat(Services(pawn), fall_velocity_); }
  std::optional<bool> Colliding(const void* pawn) const { return ReadBool(Services(pawn), colliding_); }
  std::optional<bool> LandedOnGround(const void* pawn) const { return ReadBool(Services(pawn), landed_); }
  bool sliding() const { return sliding_; }
  bool channeling() const { return channeling_; }
 private:
  const unsigned char* Services(const void* pawn) const {
    if (!pawn || !services_) return nullptr;
    const unsigned char* services = nullptr;
    if (!modlock::gameinterop::ReadNative(static_cast<const unsigned char*>(pawn) + *services_,
                                          &services, sizeof(services)))
      return nullptr;
    return services;
  }
  static std::optional<bool> ReadBool(const void* base, std::optional<size_t> offset) {
    if (!base || !offset) return std::nullopt;
    unsigned char value = 0;
    if (!modlock::gameinterop::ReadNative(static_cast<const unsigned char*>(base) + *offset,
                                          &value, sizeof(value)) || value > 1)
      return std::nullopt;
    return value == 1;
  }
  static std::optional<float> ReadFloat(const void* base, std::optional<size_t> offset) {
    if (!base || !offset) return std::nullopt;
    float value = 0;
    if (!modlock::gameinterop::ReadNative(static_cast<const unsigned char*>(base) + *offset,
                                          &value, sizeof(value)) || !std::isfinite(value))
      return std::nullopt;
    return value;
  }
  std::optional<size_t> friction_, services_, surface_friction_;
  std::optional<size_t> ground_normal_, fall_velocity_, colliding_, landed_;
  bool resolved_ = false, sliding_ = false, channeling_ = false;
  std::optional<size_t> property_;
  std::optional<ModifierMaskField> ordinary_, predicted_;
  std::optional<int64_t> ground_dash_, air_dash_, climbing_, wall_slide_;
};

inline void WriteFrameFields(std::ostream& out,
    const modlock::gameinterop::PawnObserver::Sample& pawn,
    const std::vector<modlock::gameinterop::PawnObserver::Ability>* abilities,
    const FrameCapabilities& caps, void* native_pawn) {
  out << ",\"slot\":" << pawn.slot << ",\"pawn_handle\":" << pawn.pawn_handle
      << ",\"hero_id\":" << pawn.hero_id << ",\"session_generation\":" << pawn.session_generation
      << ",\"position\":";
  Array(out, std::array<double, 3>{pawn.x, pawn.y, pawn.z});
  out << ",\"velocity\":";
  if (pawn.movement && pawn.movement->abs_velocity) Array(out, *pawn.movement->abs_velocity);
  else out << "null";
  out << ",\"ground_entity\":";
  Optional(out, pawn.movement ? pawn.movement->ground_entity_handle : std::optional<uint32_t>{});
  out << ",\"grounded\":";
  NullableBool(out, pawn.movement ? pawn.movement->grounded_by_handle : std::optional<bool>{});
  std::optional<bool> sliding, mantling;
  unsigned slide_count = 0, mantle_count = 0;
  if (abilities) for (const auto& a : *abilities) {
    if (a.slot == 0xD && caps.sliding()) { sliding = a.sliding; ++slide_count; }
    if (a.slot == 0xA && caps.channeling()) { mantling = a.channeling; ++mantle_count; }
  }
  if (slide_count != 1) sliding.reset();
  if (mantle_count != 1) mantling.reset();
  out << ",\"sliding\":"; NullableBool(out, sliding);
  auto ground_dash = caps.GroundDash(native_pawn), air_dash = caps.AirDash(native_pawn);
  out << ",\"dashing\":";
  NullableBool(out, ground_dash && air_dash ? std::optional<bool>(*ground_dash || *air_dash) : std::nullopt);
  out << ",\"climbing\":"; NullableBool(out, caps.Climbing(native_pawn));
  out << ",\"mantling\":"; NullableBool(out, mantling);
  out << ",\"friction\":"; Optional(out, caps.Friction(native_pawn));
  out << ",\"surface_friction\":"; Optional(out, caps.SurfaceFriction(native_pawn));
  out << ",\"wall_sliding\":"; NullableBool(out, caps.WallSliding(native_pawn));
  out << ",\"ground_normal\":";
  if (auto normal = caps.GroundNormal(native_pawn)) Array(out, *normal);
  else out << "null";
  out << ",\"fall_velocity\":"; Optional(out, caps.FallVelocity(native_pawn));
  out << ",\"colliding\":"; NullableBool(out, caps.Colliding(native_pawn));
  out << ",\"landed_on_ground\":"; NullableBool(out, caps.LandedOnGround(native_pawn));
  out << ",\"stamina\":";
  if (pawn.stamina) {
    out << "{\"current\":"; Number(out, pawn.stamina->current);
    out << ",\"max\":"; Number(out, pawn.stamina->max); out << '}';
  } else out << "null";
  out << ",\"abilities\":";
  if (!abilities) { out << "null"; return; }
  out << '[';
  bool first = true;
  for (const auto& a : *abilities) {
    if (!first) out << ',';
    first = false;
    out << "{\"handle\":" << a.handle << ",\"slot\":" << a.slot << ",\"charges\":" << a.charges;
    out << ",\"cooldown_start\":"; Number(out, a.cooldown_start);
    out << ",\"cooldown_end\":"; Number(out, a.cooldown_end);
    out << ",\"recharge_start\":"; Number(out, a.charge_recharge_start);
    out << ",\"recharge_end\":"; Number(out, a.charge_recharge_end);
    out << ",\"class\":";
    if (a.rtti_class) JsonString(out, *a.rtti_class);
    else out << "null";
    out << ",\"fields\":{";
    bool first_field = true;
    for (const auto& [name, value] : a.fields) {
      if (!first_field) out << ',';
      first_field = false;
      JsonString(out, name);
      out << ':'; Number(out, value);
    }
    out << "}}";
  }
  out << ']';
}
}  // namespace movement_capture
