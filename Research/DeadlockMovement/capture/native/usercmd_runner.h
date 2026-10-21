#pragma once
// Scripted user-command input: protobuf wire editing, the native schedule
// format and the per-command runner state machine. Pure logic, no engine calls,
// so the native tests exercise all of it without a game.
//
// Field numbers follow the pinned Deadworks protos (usercmd.proto and
// citadel_usercmd.proto). They are community reverse engineering of this build,
// not Valve's published schema; the diagnostic mode exists to confirm them.
#include <array>
#include <cstdint>
#include <cstring>
#include <istream>
#include <optional>
#include <sstream>
#include <string>
#include <string_view>
#include <vector>

namespace movement_capture::usercmd {

struct Field {
  uint32_t number = 0;
  uint32_t type = 0;  // 0 varint, 1 fixed64, 2 length-delimited, 5 fixed32
  uint64_t value = 0;
  std::string bytes;
};

inline bool ReadVarint(std::string_view data, size_t& at, uint64_t& out) {
  out = 0;
  for (int shift = 0; shift < 64; shift += 7) {
    if (at >= data.size()) return false;
    const auto byte = static_cast<uint8_t>(data[at++]);
    out |= static_cast<uint64_t>(byte & 0x7f) << shift;
    if (!(byte & 0x80)) return true;
  }
  return false;
}

inline void WriteVarint(std::string& out, uint64_t value) {
  while (value >= 0x80) { out.push_back(static_cast<char>(value | 0x80)); value >>= 7; }
  out.push_back(static_cast<char>(value));
}

inline bool Parse(std::string_view data, std::vector<Field>& fields) {
  fields.clear();
  size_t at = 0;
  while (at < data.size()) {
    uint64_t key = 0;
    if (!ReadVarint(data, at, key)) return false;
    Field f;
    f.number = static_cast<uint32_t>(key >> 3);
    f.type = static_cast<uint32_t>(key & 7);
    if (f.number == 0) return false;
    switch (f.type) {
      case 0: if (!ReadVarint(data, at, f.value)) return false; break;
      case 1:
        if (data.size() - at < 8) return false;
        std::memcpy(&f.value, data.data() + at, 8); at += 8; break;
      case 5:
        if (data.size() - at < 4) return false;
        { uint32_t v = 0; std::memcpy(&v, data.data() + at, 4); f.value = v; } at += 4; break;
      case 2: {
        uint64_t length = 0;
        if (!ReadVarint(data, at, length) || length > data.size() - at) return false;
        f.bytes.assign(data.substr(at, static_cast<size_t>(length)));
        at += static_cast<size_t>(length);
        break;
      }
      default: return false;  // groups are not used by these messages
    }
    fields.push_back(std::move(f));
  }
  return true;
}

inline std::string Serialize(const std::vector<Field>& fields) {
  std::string out;
  for (const auto& f : fields) {
    WriteVarint(out, (static_cast<uint64_t>(f.number) << 3) | f.type);
    switch (f.type) {
      case 0: WriteVarint(out, f.value); break;
      case 1: out.append(reinterpret_cast<const char*>(&f.value), 8); break;
      case 5: { const auto v = static_cast<uint32_t>(f.value); out.append(reinterpret_cast<const char*>(&v), 4); break; }
      case 2: WriteVarint(out, f.bytes.size()); out += f.bytes; break;
    }
  }
  return out;
}

inline Field* Find(std::vector<Field>& fields, uint32_t number) {
  for (auto& f : fields) if (f.number == number) return &f;
  return nullptr;
}

inline void Erase(std::vector<Field>& fields, uint32_t number) {
  std::erase_if(fields, [&](const Field& f) { return f.number == number; });
}

inline Field Float(uint32_t number, float value) {
  Field f; f.number = number; f.type = 5;
  uint32_t bits = 0; std::memcpy(&bits, &value, 4); f.value = bits;
  return f;
}

inline float AsFloat(const Field& f) {
  const auto bits = static_cast<uint32_t>(f.value);
  float v; std::memcpy(&v, &bits, 4); return v;
}

// CCitadelUserCmdPB.base = 1, CBaseUserCmdPB: legacy_command_number = 1,
// client_tick = 2, buttons_pb = 3 {held 1, changed 2, scroll 3}, viewangles = 4
// {x pitch 1, y yaw 2, z roll 3}, forwardmove = 5, leftmove = 6, upmove = 7,
// subtick_moves = 18.
struct CommandInput {
  uint64_t held = 0, changed = 0, scroll = 0;
  float pitch = 0, yaw = 0, roll = 0;
  float forward = 0, left = 0, up = 0;
  int32_t command_number = 0, client_tick = 0;
};

inline void SetOrAdd(std::vector<Field>& fields, Field value) {
  if (auto* existing = Find(fields, value.number)) *existing = std::move(value);
  else fields.push_back(std::move(value));
}

inline Field Varint(uint32_t number, uint64_t value) {
  Field f; f.number = number; f.type = 0; f.value = value; return f;
}

inline Field Message(uint32_t number, std::string bytes) {
  Field f; f.number = number; f.type = 2; f.bytes = std::move(bytes); return f;
}

// Reads the command's fields; absent fields keep proto defaults (zero). Fails
// when the bytes are not a CCitadelUserCmdPB wrapping a base command.
inline bool ReadCommand(std::string_view bytes, CommandInput& out) {
  out = {};
  std::vector<Field> top, base;
  if (!Parse(bytes, top)) return false;
  const auto* wrapped = Find(top, 1);
  if (!wrapped || wrapped->type != 2 || !Parse(wrapped->bytes, base)) return false;
  for (const auto& f : base) {
    if (f.number == 1 && f.type == 0) out.command_number = static_cast<int32_t>(f.value);
    else if (f.number == 2 && f.type == 0) out.client_tick = static_cast<int32_t>(f.value);
    else if (f.number == 3 && f.type == 2) {
      std::vector<Field> buttons;
      if (!Parse(f.bytes, buttons)) return false;
      for (const auto& b : buttons) {
        if (b.type != 0) continue;
        if (b.number == 1) out.held = b.value;
        else if (b.number == 2) out.changed = b.value;
        else if (b.number == 3) out.scroll = b.value;
      }
    } else if (f.number == 4 && f.type == 2) {
      std::vector<Field> angles;
      if (!Parse(f.bytes, angles)) return false;
      for (const auto& a : angles) {
        if (a.type != 5) continue;
        if (a.number == 1) out.pitch = AsFloat(a);
        else if (a.number == 2) out.yaw = AsFloat(a);
        else if (a.number == 3) out.roll = AsFloat(a);
      }
    } else if (f.type == 5) {
      if (f.number == 5) out.forward = AsFloat(f);
      else if (f.number == 6) out.left = AsFloat(f);
      else if (f.number == 7) out.up = AsFloat(f);
    }
  }
  return true;
}

// Rewrites buttons, view angles and the three axes. Every other field,
// including unknown ones, is preserved. Subtick moves are dropped: they carry
// the human's button edges and analog deltas and would contradict the script.
inline bool WriteCommand(std::string& bytes, const CommandInput& in) {
  std::vector<Field> top, base;
  if (!Parse(bytes, top)) return false;
  auto* wrapped = Find(top, 1);
  if (!wrapped || wrapped->type != 2 || !Parse(wrapped->bytes, base)) return false;
  std::string buttons;
  buttons += Serialize({Varint(1, in.held), Varint(2, in.changed), Varint(3, in.scroll)});
  SetOrAdd(base, Message(3, std::move(buttons)));
  SetOrAdd(base, Message(4, Serialize({Float(1, in.pitch), Float(2, in.yaw), Float(3, in.roll)})));
  SetOrAdd(base, Float(5, in.forward));
  SetOrAdd(base, Float(6, in.left));
  SetOrAdd(base, Float(7, in.up));
  Erase(base, 18);
  wrapped->bytes = Serialize(base);
  bytes = Serialize(top);
  return true;
}

// ---- schedule --------------------------------------------------------------

struct TickInput { uint64_t held = 0; float forward = 0, left = 0, pitch = 0, yaw = 0; };

struct Trial {
  std::string id;
  int slot = 0;
  std::optional<std::array<float, 6>> start;  // origin xyz then velocity xyz
  std::vector<TickInput> ticks;
};

// Line format written by runner_schedule.py:
//   GRS1
//   trial <id> <slot> <ticks> [start px py pz vx vy vz]
//   t <held> <forward> <left> <pitch> <yaw>     (ticks lines)
//   end
inline bool ParseSchedule(std::istream& in, std::vector<Trial>& trials, std::string& error) {
  trials.clear();
  std::string line;
  if (!std::getline(in, line) || line != "GRS1") { error = "missing GRS1 header"; return false; }
  while (std::getline(in, line)) {
    if (line.empty()) continue;
    std::istringstream header(line);
    std::string word;
    header >> word;
    if (word != "trial") { error = "expected trial line: " + line; return false; }
    Trial trial;
    size_t count = 0;
    if (!(header >> trial.id >> trial.slot >> count) || count == 0 || count > 100000 ||
        trial.slot < 0 || trial.slot > 63) { error = "bad trial header: " + line; return false; }
    std::string tag;
    if (header >> tag) {
      std::array<float, 6> s{};
      if (tag != "start") { error = "bad trial header: " + line; return false; }
      for (auto& v : s) if (!(header >> v)) { error = "bad start state: " + line; return false; }
      trial.start = s;
    }
    for (size_t i = 0; i < count; ++i) {
      if (!std::getline(in, line)) { error = "truncated trial " + trial.id; return false; }
      std::istringstream row(line);
      TickInput t;
      if (!(row >> tag >> t.held >> t.forward >> t.left >> t.pitch >> t.yaw) || tag != "t") {
        error = "bad tick line: " + line; return false;
      }
      trial.ticks.push_back(t);
    }
    if (!std::getline(in, line) || line != "end") { error = "missing end for " + trial.id; return false; }
    trials.push_back(std::move(trial));
  }
  if (trials.empty()) { error = "no trials"; return false; }
  return true;
}

// One scripted command per received user command of the target slot. The
// schedule is therefore command-exact; engine clock ticks are logged beside it.
class Runner {
 public:
  enum class Phase { kIdle, kNeedsStart, kRunning, kDone };
  struct Applied { size_t trial = 0, tick = 0; CommandInput input; };

  explicit Runner(std::vector<Trial> trials) : trials_(std::move(trials)) {}
  Phase phase() const { return phase_; }
  size_t trial_index() const { return trial_; }
  const Trial& trial() const { return trials_[trial_]; }
  int slot() const { return trials_.front().slot; }
  size_t trial_count() const { return trials_.size(); }

  void Arm() { if (phase_ == Phase::kIdle) { phase_ = Phase::kNeedsStart; trial_ = 0; tick_ = 0; } }
  // Called once the trial's start state is in place (or it has none).
  void BeginTrial(uint64_t previous_held) {
    if (phase_ != Phase::kNeedsStart) return;
    previous_held_ = previous_held;
    tick_ = 0;
    phase_ = Phase::kRunning;
  }
  // Consumes one command; returns what to write. Release happens through the
  // schedule's own trailing neutral ticks, then input returns to the client.
  std::optional<Applied> Next(const CommandInput& observed) {
    if (phase_ != Phase::kRunning) return std::nullopt;
    const auto& t = trials_[trial_].ticks[tick_];
    Applied a;
    a.trial = trial_;
    a.tick = tick_;
    a.input = observed;  // keeps command number, client tick, roll, up
    a.input.held = t.held;
    a.input.changed = t.held ^ previous_held_;
    a.input.scroll = 0;
    a.input.pitch = t.pitch;
    a.input.yaw = t.yaw;
    a.input.roll = 0;
    a.input.forward = t.forward;
    a.input.left = t.left;
    a.input.up = 0;
    previous_held_ = t.held;
    if (++tick_ >= trials_[trial_].ticks.size()) {
      phase_ = (++trial_ >= trials_.size()) ? Phase::kDone : Phase::kNeedsStart;
    }
    return a;
  }

 private:
  std::vector<Trial> trials_;
  Phase phase_ = Phase::kIdle;
  size_t trial_ = 0, tick_ = 0;
  uint64_t previous_held_ = 0;
};

}  // namespace movement_capture::usercmd
