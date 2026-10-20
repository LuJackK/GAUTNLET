#include "recorder_core.h"
#include "frame_writer.h"
#include "frame_observation.h"

#include <filesystem>
#include <iostream>
#include <memory>
#include <map>
#include <string>
#include <string_view>
#include <tuple>
#include <set>
#include <sstream>
#include <algorithm>

#include "modlock/engine_host.h"
#include "modlock/gameinterop/entity_abi.h"
#include "modlock/gameinterop/bot_creation.h"
#include "modlock/gameinterop/hero_definitions.h"
#include "modlock/gameinterop/mapped_module_image.h"
#include "modlock/gameinterop/pawn_observer.h"
#include "modlock/gameinterop/engine_server.h"
#include "modlock/gameinterop/movement_hook.h"
#include "modlock/gameinterop/server_clock.h"
#include "modlock/plugin_library.h"

namespace {
class ProbeConnections final : public modlock::gameinterop::ConnectionEventSink {
 public:
  void OnConnected(int32_t, uint64_t, bool, const char*) override {}
  void OnDisconnecting(int32_t, uint64_t) override {}
};
using modlock::gameinterop::MovementCall;
using modlock::gameinterop::MovementHook;

movement_capture::State StateOf(const MovementCall& call) {
  movement_capture::State state;
  state.position = call.origin;
  state.velocity = call.velocity;
  state.dash_vector = movement_capture::DashVectorOf(call.dash_vector);
  if (call.pawn) {
    state.ground_entity = call.ground_entity;
    state.move_type = call.move_type;
  }
  return state;
}

class Recorder final : public modlock::Plugin, public modlock::TickContextPlugin {
 public:
  explicit Recorder(const modlock::PluginContext& context)
      : engine_(context.engine), check_only_(context.check_only) {
    for (int i = 1; i < context.argc; ++i) {
      const std::string_view arg(context.argv[i]);
      if (arg == "--capture-output" && i + 1 < context.argc) output_ = context.argv[++i];
      if (arg == "--solo-probe") solo_probe_ = true;
      if (arg == "--solo-drop") solo_drop_ = true;
    }
  }
  uint32_t InterfaceVersion() const override { return modlock::PluginInterfaceVersion; }
  const char* Name() const override { return "gauntlet-movement-capture"; }
  bool Load() override {
    // A private, new run directory is created by capture_session.py. Refuse an
    // existing output even when somebody invokes the plugin manually.
    if (output_.empty() || !output_.is_absolute() || std::filesystem::exists(output_)) {
      std::cerr << "capture: --capture-output must be a new absolute file path\n";
      return false;
    }
    stream_.open(output_, std::ios::out | std::ios::binary);
    if (!stream_) return false;
    const auto frame_path = output_.parent_path() / "frames.jsonl";
    if (std::filesystem::exists(frame_path)) return false;
    frame_stream_.open(frame_path, std::ios::out | std::ios::binary);
    if (!frame_stream_) return false;
    frame_stream_.imbue(std::locale::classic());
    frames_ = std::make_unique<movement_capture::FrameWriter>(frame_stream_);
    frames_->Start(check_only_);
    writer_ = std::make_unique<movement_capture::Writer>(stream_);
    writer_->Start(check_only_);
    return static_cast<bool>(stream_);
  }
  bool Start() override {
    if (check_only_) {
      world_ = 1;
      movement_capture::Step s;
      s.world = 1;
      s.pawn_handle = 1;
      s.ticks = {10, 11};
      s.before.position = {0, 0, 0};
      s.after.position = {1, 0, 0};
      s.before.velocity = s.after.velocity = {64, 0, 0};
      s.clock_tick = 11;
      s.clock_time_s = 11.0 / 64.0;
      writer_->Push(s);
      latest_movement_ = 0;
      modlock::gameinterop::PawnObserver::Sample pawn;
      pawn.slot = 0; pawn.pawn_handle = 1; pawn.session_generation = 1;
      std::ostringstream frame;
      frame << "\"type\":\"frame\",\"world\":1,\"clock_tick\":11,\"clock_time_s\":0.171875,\"movement_sequence\":0";
      movement_capture::WriteFrameFields(frame, pawn, nullptr, frame_caps_, nullptr);
      frames_->Push(frame.str());
      return true;
    }
    if (!engine_) return false;
    auto connections = engine_->OnConnection(std::make_shared<ProbeConnections>());
    if (!connections) return false;
    subscription_.Add(std::move(*connections));
    if (solo_probe_) {
      if (!engine_->Precache({"hero_wraith"}, {})) return false;
    }
    // Installing after the world is ready ensures schema registration has
    // finished. Removing on world ending prevents stale cached schema offsets.
    auto worlds = engine_->OnWorld([this](std::string_view) { ++world_; identities_.clear(); pawn_observer_.Invalidate(); frame_caps_.Reset(); },
        [this](std::string_view) {
          if (world_ == 0) ++world_;
          if (hook_) return;
          auto observed = MovementHook::Observe(MovementHook::Module::kServer,
              [this](const MovementCall& before, const MovementCall& after) {
                Record(before, after);
              });
          if (!observed) {
            failed_ = true;
            std::cerr << "capture: observer unavailable: " << observed.error() << '\n';
            return;
          }
          hook_ = std::move(*observed);
          std::cout << "capture: native server observer installed; layout/timing still unverified\n";
          auto server = modlock::gameinterop::EngineServer::Resolve();
          if (!server || !server->ServerCommand("sv_hibernate_when_empty 0\n"))
            ProbeError("cannot request continued simulation on empty server");
        });
    if (!worlds) return false;
    subscription_.Add(std::move(*worlds));
    auto ending = engine_->OnWorldEnding([this] { hook_.reset(); pawn_observer_.Invalidate(); });
    if (!ending) return false;
    subscription_.Add(std::move(*ending));
    return true;
  }
  // Identity is sampled by the host after GameFrame, not guessed from the
  // movement command number. Emit only changes, never account identifiers.
  // The session wrapper saves stdout alongside the trace for pilot selection.
  void Tick(const modlock::TickContext&) override {
    StopIfRequested();
    if (check_only_ || !hook_) return;
    PollMarkers();
    frame_caps_.Resolve();
    if (solo_probe_ && !probe_started_) StartProbe();
    for (int32_t slot = 0; slot < 64; ++slot) {
      if (!modlock::gameinterop::ConnectionTracker::StateForSlot(slot).occupied) continue;
      auto sample = pawn_observer_.Observe(slot);
      if (!sample) continue;
      const auto& pawn = *sample;
      // Snapshot before any optional probe intervention later in this frame.
      auto abilities = pawn_observer_.CurrentAbilitiesForSlot(slot);
      auto frame = FramePrefix("frame");
      movement_capture::WriteFrameFields(frame, pawn, abilities ? &*abilities : nullptr,
                                         frame_caps_, pawn_observer_.PawnForSlot(slot));
      frames_->Push(frame.str());
      if (solo_drop_ && !drop_done_ && slot == probe_slot_ && pawn.hero_id != 0 &&
          std::chrono::steady_clock::now() >= probe_deadline_ - std::chrono::seconds(15)) {
        drop_done_ = true;
        auto module = modlock::gameinterop::MappedModuleImage::ForModule(L"server.dll");
        auto native_pawn = pawn_observer_.PawnForSlot(slot);
        if (module && native_pawn) {
          auto motion = modlock::gameinterop::ResolvePawnMotion(*module);
          if (motion) {
            const std::array<float, 3> position{static_cast<float>(pawn.x), static_cast<float>(pawn.y), static_cast<float>(pawn.z + 256)};
            const std::array<float, 3> zero{};
            motion->set_origin(native_pawn, position.data());
            motion->set_velocity(native_pawn, zero.data());
            std::cout << "capture-intervention {\"kind\":\"one_time_raise\",\"slot\":" << slot
                      << ",\"pawn_handle\":" << pawn.pawn_handle << ",\"raise_source_units\":256,\"position\":";
            movement_capture::Array(std::cout, position);
            std::cout << ",\"initial_velocity\":[0,0,0]}" << std::endl;
          } else ProbeError(motion.error());
        } else ProbeError("cannot resolve live pawn for drop");
      }
      const auto identity = std::make_tuple(pawn.pawn_handle, pawn.session_generation, pawn.hero_id);
      auto it = identities_.find(slot);
      if (it != identities_.end() && it->second == identity) continue;
      identities_[slot] = identity;
      std::cout << "capture-pawn {\"world\":" << world_ << ",\"slot\":" << slot
                << ",\"pawn_handle\":" << pawn.pawn_handle << ",\"hero_id\":" << pawn.hero_id
                << ",\"session_generation\":" << pawn.session_generation
                << ",\"observation\":\"after_frame\"}" << std::endl;
    }
  }
  void Tick() override { StopIfRequested(); }
  void Stop() override {
    hook_.reset();
    subscription_.Reset();
    if (writer_) {
      writer_->Finish(world_);
      failed_ = failed_ || writer_->Failed() || writer_->Dropped() != 0 || writer_->Written() == 0;
      std::cout << "capture: " << writer_->Written() << " records, " << writer_->Dropped()
                << " dropped; run the offline validator\n";
    }
    if (frames_) { frames_->Finish(); failed_ = failed_ || frames_->Failed(); }
    frame_stream_.close();
    if (frame_stream_.fail()) failed_ = true;
    stream_.close();
    if (stream_.fail()) failed_ = true;
  }
  std::optional<int> ExitCode() const override { return failed_ ? 1 : 0; }
 private:
  std::ostringstream FramePrefix(const char* type) {
    std::ostringstream out;
    out.imbue(std::locale::classic());
    out << std::setprecision(std::numeric_limits<double>::max_digits10);
    out << "\"type\":\"" << type << "\",\"world\":" << world_ << ",\"clock_tick\":";
    std::optional<uint64_t> tick;
    std::optional<double> time;
    if (auto clock = modlock::gameinterop::ServerClock::Resolve()) {
      if (auto observed = clock->Observe()) { tick = observed->tick; time = observed->time_seconds; }
    }
    movement_capture::Optional(out, tick);
    out << ",\"clock_time_s\":"; movement_capture::Optional(out, time);
    out << ",\"movement_sequence\":"; movement_capture::Optional(out, latest_movement_);
    return out;
  }
  static bool MarkerToken(const std::string& value) {
    if (value.empty() || value.size() > 64) return false;
    for (const auto c : value)
      if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
            (c >= '0' && c <= '9') || c == '_' || c == '-')) return false;
    return true;
  }
  void PollMarkers() {
    const auto now = std::chrono::steady_clock::now();
    if (now < next_marker_probe_) return;
    next_marker_probe_ = now + std::chrono::milliseconds(100);
    const auto directory = output_.parent_path() / "marker-requests";
    std::error_code error;
    if (!std::filesystem::is_directory(directory, error)) return;
    std::vector<std::filesystem::directory_entry> requests;
    for (const auto& entry : std::filesystem::directory_iterator(directory, error)) requests.push_back(entry);
    if (error) { ProbeError("cannot enumerate marker requests"); return; }
    std::sort(requests.begin(), requests.end(), [](const auto& a, const auto& b) {
      return a.path().filename() < b.path().filename();
    });
    for (const auto& entry : requests) {
      const auto path = entry.path();
      if (path.extension() != ".request" || processed_markers_.contains(path.filename().string())) continue;
      processed_markers_.insert(path.filename().string());
      if (entry.is_symlink(error) || !entry.is_regular_file(error) || entry.file_size(error) > 256) {
        ProbeError("invalid marker request file"); continue;
      }
      std::ifstream input(path);
      std::string id, trial, event, extra;
      std::getline(input, id); std::getline(input, trial); std::getline(input, event);
      if (!MarkerToken(id) || !MarkerToken(trial) || id != path.stem().string() ||
          !(event == "arm" || event == "start" || event == "end" || event == "abort") ||
          std::getline(input, extra)) { ProbeError("invalid marker request payload"); continue; }
      auto marker = FramePrefix("marker");
      marker << ",\"request_id\":\"" << id << "\",\"trial_id\":\"" << trial
             << "\",\"event\":\"" << event << "\"";
      frames_->Push(marker.str());
      std::cout << "capture-marker " << id << ' ' << trial << ' ' << event << std::endl;
    }
  }
  void StartProbe() {
    if (probe_started_) return;
    probe_started_ = true;
    probe_deadline_ = std::chrono::steady_clock::now() + std::chrono::seconds(20);
    auto module = modlock::gameinterop::MappedModuleImage::ForModule(L"server.dll");
    if (!module) { ProbeError(module.error()); return; }
    auto heroes = modlock::gameinterop::HeroDefinitions::Resolve(*module);
    auto bots = modlock::gameinterop::BotCreation::Resolve(*module);
    if (!heroes) { ProbeError(heroes.error()); return; }
    if (!bots) { ProbeError(bots.error()); return; }
    auto hero = heroes->Find("hero_wraith");
    if (!hero) { ProbeError(hero.error()); return; }
    auto slot = bots->Create("CaptureProbe", 2, static_cast<uint32_t>(hero->id), {0, 0, 2048});
    if (!slot) {
      failed_ = true;
      std::cerr << "capture: solo bot creation failed: " << slot.error() << std::endl;
      return;
    }
    std::cout << "capture-probe {\"kind\":\"native_bot_spawn_idle\",\"hero\":\"hero_wraith\","
                 "\"slot\":" << *slot << ",\"requested_position\":[0,0,2048],\"duration_wall_s\":20}"
              << std::endl;
    probe_slot_ = *slot;
  }
  void ProbeError(const std::string& message) {
    failed_ = true;
    std::cerr << "capture: solo probe failed: " << message << std::endl;
  }
  void StopIfRequested() {
    if (check_only_ || quit_requested_) return;
    const auto now = std::chrono::steady_clock::now();
    if (now < next_stop_probe_) return;
    next_stop_probe_ = now + std::chrono::milliseconds(250);
    std::error_code error;
    const bool probe_done = probe_started_ && now >= probe_deadline_;
    if (!probe_done && !std::filesystem::is_regular_file(output_.parent_path() / "stop.request", error)) return;
    auto server = modlock::gameinterop::EngineServer::Resolve();
    if (server && server->ServerCommand("quit\n")) {
      quit_requested_ = true;
      std::cout << "capture: graceful server shutdown requested" << std::endl;
    }
  }
  void Record(const MovementCall& before, const MovementCall& after) noexcept {
    movement_capture::Step s;
    s.world = world_;
    s.pawn_handle = modlock::gameinterop::ReferenceHandleOf(before.pawn);
    s.command_raw = before.command;
    s.ticks = before.ticks;
    s.fractions = before.fractions;
    s.buttons = before.buttons;
    s.angles = before.angles;
    s.axes = {before.forward, before.left, before.up};
    s.before = StateOf(before);
    s.after = StateOf(after);
    s.wall_ns = static_cast<uint64_t>(std::chrono::duration_cast<std::chrono::nanoseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count());
    // This stamp is the callback's engine clock, not proof of the command's
    // integration interval. Raw native ticks/fractions are retained separately.
    auto clock = modlock::gameinterop::ServerClock::Resolve();
    if (clock) {
      auto observed = clock->Observe();
      if (observed) { s.clock_tick = observed->tick; s.clock_time_s = observed->time_seconds; }
    }
    writer_->Push(s);
    latest_movement_ = movement_count_++;
  }
  modlock::EngineHost* engine_;
  bool check_only_, failed_ = false;
  bool quit_requested_ = false;
  bool solo_probe_ = false, probe_started_ = false;
  bool solo_drop_ = false, drop_done_ = false;
  int32_t probe_slot_ = -1;
  std::chrono::steady_clock::time_point probe_deadline_{};
  std::chrono::steady_clock::time_point next_stop_probe_{};
  uint32_t world_ = 0;
  std::filesystem::path output_;
  std::ofstream stream_;
  std::ofstream frame_stream_;
  std::unique_ptr<movement_capture::FrameWriter> frames_;
  movement_capture::FrameCapabilities frame_caps_;
  std::optional<uint64_t> latest_movement_;
  uint64_t movement_count_ = 0;
  std::chrono::steady_clock::time_point next_marker_probe_{};
  std::set<std::string> processed_markers_;
  std::unique_ptr<movement_capture::Writer> writer_;
  modlock::Subscription subscription_;
  std::optional<MovementHook> hook_;
  std::map<int32_t, std::tuple<uint32_t, uint32_t, uint32_t>> identities_;
  modlock::gameinterop::PawnObserver pawn_observer_{modlock::gameinterop::PawnObserver::LiveSeams(true), true};
};
}  // namespace

MODLOCK_PLUGIN_EXPORT modlock::PluginManifest ModlockPluginManifest_v1() {
  return {modlock::PluginInterfaceVersion, MODLOCK_SDK_ABI};
}
MODLOCK_PLUGIN_EXPORT modlock::Plugin* ModlockPluginCreate_v1(const modlock::PluginContext* c) {
  return c ? new Recorder(*c) : nullptr;
}
MODLOCK_PLUGIN_EXPORT void ModlockPluginDestroy_v1(modlock::Plugin* p) { delete p; }
