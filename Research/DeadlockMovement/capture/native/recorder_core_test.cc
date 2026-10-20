#include "recorder_core.h"
#include "frame_writer.h"
#include "modifier_reader.h"
#include <cstring>
#include "observe_step.h"
#include <cstdlib>
#include <iostream>
#include <memory>
#include <sstream>

void Require(bool condition, const char* message) {
  if (!condition) { std::cerr << message << '\n'; std::exit(1); }
}
int main(int argc, char** argv) {
  int engine_state = 4, native_calls = 0, observations = 0;
  movement_capture::ObserveStep(engine_state,
      [&] { ++native_calls; engine_state += 3; },
      [&](int& after) { after = engine_state; },
      [&](const int& before, const int& after) {
        ++observations;
        Require(before == 4 && after == 7, "paired native before/after state");
      });
  Require(native_calls == 1 && observations == 1 && engine_state == 7,
          "observation calls native once without altering result");
  movement_capture::Queue<2> queue;
  // The old reader silently missed predicted-only states and assumed stale
  // numeric enum values. Exercise both masks, shifted IDs and missing data.
  std::array<uint32_t, 22> mask_words{};
  const void* property = mask_words.data();
  int reads = 0;
  auto read = [&](const void* source, void* destination, size_t size) {
    ++reads; std::memcpy(destination, source, size); return true;
  };
  const movement_capture::ModifierMaskField ordinary{0, 44}, predicted{44, 44};
  auto flag = [&](std::optional<int64_t> value) {
    return movement_capture::ReadModifierState(&property, 0, ordinary, predicted, value, read);
  };
  Require(flag(46) == false, "resolved inactive state is false");
  mask_words[11 + 46 / 32] = uint32_t{1} << (46 % 32);
  Require(flag(46) == true, "predicted-only state must be active");
  Require(flag(41) == false, "different runtime enum must not alias");
  mask_words.fill(0); mask_words[10] = uint32_t{1} << 31;
  Require(flag(351) == true, "last schema mask bit supported");
  const auto before_reads = reads;
  Require(!flag(352) && !flag(-1) && !flag(std::nullopt), "out-of-range or missing enum unknown");
  Require(reads == before_reads, "invalid enum must not read memory");
  Require(!movement_capture::ReadModifierState(&property, 0, ordinary,
      std::optional<movement_capture::ModifierMaskField>{}, 41, read), "missing predicted mask unknown");
  Require(!movement_capture::ReadModifierState(&property, 0, ordinary, predicted, 41,
      [](const void*, void*, size_t) { return false; }), "unreadable property unknown");
  property = nullptr;
  Require(!flag(41), "null modifier property unknown");
  movement_capture::Step a; a.sequence = 7;
  Require(queue.Push(a), "first enqueue");
  a.sequence = 9; Require(queue.Push(a), "second enqueue");
  Require(!queue.Push(a), "overflow must be reported");
  Require(queue.Dropped() == 1, "overflow count");
  std::array<movement_capture::Step, 2> records;
  Require(queue.Drain(records.data(), 1) == 1 && records[0].sequence == 7, "first drain");
  a.sequence = 12; Require(queue.Push(a), "wraparound enqueue");
  Require(queue.Drain(records.data(), 2) == 2 && records[0].sequence == 9 &&
          records[1].sequence == 12, "FIFO wraparound preserves sequence gaps");
  movement_capture::Queue<32> concurrent;
  std::thread producer([&] {
    for (uint64_t i = 0; i < 10000; ++i) {
      movement_capture::Step s; s.sequence = i;
      while (!concurrent.Push(s)) std::this_thread::yield();
    }
  });
  uint64_t received = 0;
  while (received < 10000) {
    const auto n = concurrent.Drain(records.data(), records.size());
    for (size_t i = 0; i < n; ++i)
      Require(records[i].sequence == received++, "concurrent transfer preserves order and contents");
    if (n == 0) std::this_thread::yield();
  }
  producer.join();
  a.before.velocity[0] = std::numeric_limits<float>::infinity();
  a.buttons[0] = std::numeric_limits<uint64_t>::max();
  std::ostringstream invalid;
  movement_capture::WriteStep(invalid, a);
  Require(invalid.str().find("null") != std::string::npos, "nonfinite is explicit null");
  Require(invalid.str().find("\"18446744073709551615\"") != std::string::npos,
          "button mask must not lose precision");
  std::ostringstream out;
  auto writer = std::make_unique<movement_capture::Writer>(out);
  writer->Start(true);
  movement_capture::Step sample;
  sample.world = 1; sample.pawn_handle = 1; sample.ticks = {0, 1};
  sample.clock_tick = 1; sample.clock_time_s = 0.015625;
  sample.after.position = {1, 0, 0};
  writer->Push(sample);
  writer->Finish(1);
  Require(!writer->Failed() && writer->Written() == 1 && writer->Dropped() == 0, "writer drain");
  Require(out.str().find("\"sequence\":0") != std::string::npos, "sequence starts at zero");
  Require(out.str().find("\"attempted\":1,\"written\":1,\"dropped\":0") != std::string::npos,
          "footer reconciliation");
  std::ostream broken(nullptr);
  auto failing = std::make_unique<movement_capture::Writer>(broken);
  failing->Start(true);
  failing->Push(sample);
  failing->Finish(1);
  Require(failing->Failed(), "disk/stream errors must not report success");
  std::ostringstream frame_out;
  movement_capture::FrameWriter frames(frame_out);
  frames.Start(true);
  frames.Push("\"type\":\"frame\"");
  frames.Push("\"type\":\"marker\"");
  frames.Finish();
  Require(!frames.Failed(), "frame stream flush succeeds");
  Require(frame_out.str().find("\"sequence\":0,\"type\":\"frame\"") != std::string::npos,
          "frame ordering begins at zero");
  Require(frame_out.str().find("\"sequence\":1,\"type\":\"marker\"") != std::string::npos,
          "markers and frames share sidecar sequence");
  Require(frame_out.str().find("\"attempted\":2,\"written\":2,\"dropped\":0") != std::string::npos,
          "frame footer reconciles");
  movement_capture::FrameWriter bad_frames(broken);
  bad_frames.Start(false);
  bad_frames.Push("\"type\":\"frame\"");
  bad_frames.Finish();
  Require(bad_frames.Failed(), "frame write errors are visible");
  std::ostringstream overflow_out;
  movement_capture::FrameWriter overflow_frames(overflow_out);
  for (int i = 0; i < 8193; ++i) overflow_frames.Push("\"type\":\"frame\"");
  overflow_frames.Start(true);
  overflow_frames.Finish();
  Require(overflow_frames.Failed(), "frame overflow makes capture fail");
  Require(overflow_out.str().find("\"attempted\":8193,\"written\":8192,\"dropped\":1") != std::string::npos,
          "frame overflow reconciles without disguising loss");
  if (argc == 2) {
    std::ofstream fixture(argv[1], std::ios::binary);
    fixture << out.str();
    Require(static_cast<bool>(fixture), "fixture output");
  }
  constexpr float kUnset = std::numeric_limits<float>::max();
  Require(!movement_capture::DashVectorOf({kUnset, kUnset, kUnset}), "unset dash vector is null");
  Require(movement_capture::DashVectorOf({kUnset, 0, 0}).has_value(), "partial dash vector is kept");
  movement_capture::State dashing;
  dashing.dash_vector = std::array<float, 3>{9.5f, 0, 0};
  std::ostringstream state_out;
  movement_capture::WriteState(state_out, dashing);
  Require(state_out.str().find("\"dash_vector\":[9.5,0,0]") != std::string::npos,
          "dash vector serialised");
  std::ostringstream idle_out;
  movement_capture::WriteState(idle_out, movement_capture::State{});
  Require(idle_out.str().find("\"dash_vector\":null") != std::string::npos,
          "inactive dash serialised as null");
  std::cout << "capture core tests passed\n";
}
