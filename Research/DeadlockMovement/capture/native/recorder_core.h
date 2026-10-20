#pragma once

#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <fstream>
#include <iomanip>
#include <limits>
#include <locale>
#include <optional>
#include <ostream>
#include <thread>
#include <type_traits>

namespace movement_capture {

struct State {
  std::array<float, 3> position{}, velocity{};
  std::optional<uint32_t> ground_entity;
  std::optional<uint8_t> move_type;
};

struct Step {
  uint64_t sequence = 0, wall_ns = 0;
  uint32_t world = 0;
  std::optional<uint32_t> pawn_handle;
  int32_t command_raw = 0;
  std::array<int32_t, 2> ticks{};
  std::array<float, 2> fractions{};
  std::array<uint64_t, 3> buttons{};
  std::array<float, 3> angles{}, axes{};
  State before, after;
  std::optional<uint64_t> clock_tick;
  std::optional<double> clock_time_s;
};

// Null preserves an invalid observation without producing invalid JSON NaN/Inf.
// The offline validator rejects null where an observed float is required.
inline void Number(std::ostream& out, double value) {
  if (std::isfinite(value)) out << value;
  else out << "null";
}
template <typename T> void Optional(std::ostream& out, std::optional<T> value) {
  if (!value) out << "null";
  else if constexpr (std::is_floating_point_v<T>) Number(out, *value);
  else out << +*value;
}
template <typename T, size_t N> void Array(std::ostream& out, const std::array<T, N>& values) {
  out << '[';
  for (size_t i = 0; i < N; ++i) {
    if (i) out << ',';
    if constexpr (std::is_floating_point_v<T>) Number(out, values[i]);
    else out << values[i];
  }
  out << ']';
}
inline void WriteState(std::ostream& out, const State& s) {
  out << "{\"position\":"; Array(out, s.position);
  out << ",\"velocity\":"; Array(out, s.velocity);
  out << ",\"ground_entity\":"; Optional(out, s.ground_entity);
  out << ",\"move_type\":"; Optional(out, s.move_type);
  out << '}';
}
inline void WriteStep(std::ostream& out, const Step& s) {
  out << std::setprecision(std::numeric_limits<double>::max_digits10);
  out << "{\"type\":\"step\",\"sequence\":" << s.sequence
      << ",\"world\":" << s.world << ",\"pawn_handle\":";
  Optional(out, s.pawn_handle);
  out << ",\"command_raw\":" << s.command_raw << ",\"ticks\":"; Array(out, s.ticks);
  out << ",\"fractions\":"; Array(out, s.fractions);
  out << ",\"buttons\":[\"" << s.buttons[0] << "\",\"" << s.buttons[1] << "\",\""
      << s.buttons[2] << "\"],\"angles\":"; Array(out, s.angles);
  out << ",\"axes\":"; Array(out, s.axes);
  out << ",\"before\":"; WriteState(out, s.before);
  out << ",\"after\":"; WriteState(out, s.after);
  out << ",\"wall_ns\":" << s.wall_ns << ",\"clock_tick\":"; Optional(out, s.clock_tick);
  out << ",\"clock_time_s\":"; Optional(out, s.clock_time_s);
  out << "}\n";
}

// Modlock dispatches on one engine thread: one producer, one writer consumer.
// Neither side waits on the other; only a genuinely full ring drops samples.
// Acquire/release transfers ownership of slots without allocating in the hook.
template <size_t Capacity> class Queue {
 public:
  bool Push(Step step) noexcept {
    const auto tail = tail_.load(std::memory_order_relaxed);
    if (tail - head_.load(std::memory_order_acquire) == Capacity) {
      dropped_.fetch_add(1);
      return false;
    }
    records_[tail % Capacity] = step;
    tail_.store(tail + 1, std::memory_order_release);
    return true;
  }
  size_t Drain(Step* output, size_t limit) {
    auto head = head_.load(std::memory_order_relaxed);
    const auto tail = tail_.load(std::memory_order_acquire);
    size_t n = 0;
    while (head != tail && n < limit) {
      output[n++] = records_[head % Capacity];
      ++head;
    }
    head_.store(head, std::memory_order_release);
    return n;
  }
  uint64_t Dropped() const { return dropped_.load(); }
 private:
  std::array<Step, Capacity> records_{};
  alignas(64) std::atomic<uint64_t> head_{0};
  alignas(64) std::atomic<uint64_t> tail_{0};
  std::atomic<uint64_t> dropped_{0};
};

// Thread lifetime is strictly inside the plugin lifetime. Call Finish only
// after removing the native hook, so producers cannot race the final drain.
class Writer {
 public:
  explicit Writer(std::ostream& stream) : stream_(stream) {
    stream_.imbue(std::locale::classic());
  }
  ~Writer() { if (thread_.joinable()) Finish(0); }
  void Start(bool synthetic) {
    stream_ << "{\"type\":\"session\",\"schema_version\":1,\"module\":\"server\","
               "\"units\":\"source\",\"timing_status\":\"unverified\",\"synthetic\":"
            << (synthetic ? "true" : "false")
            << ",\"modlock_revision\":\"67478b9bbc54ff1f94ccfdb5f9ce0e08dc6f6418\","
               "\"observer_revision\":1}\n";
    stream_.flush();
    thread_ = std::thread([this] {
      while (!stop_.load()) {
        Drain();
        std::this_thread::sleep_for(std::chrono::milliseconds(20));
      }
      while (Drain()) {}
    });
  }
  void Push(Step step) noexcept {
    // Native callbacks are on the engine thread. This counter includes losses;
    // gaps retain the true callback order instead of disguising a missing step.
    step.sequence = attempted_.fetch_add(1);
    queue_.Push(step);
  }
  void Finish(uint32_t worlds) {
    if (!thread_.joinable()) return;
    stop_ = true;
    thread_.join();
    stream_ << "{\"type\":\"end\",\"attempted\":" << attempted_.load()
            << ",\"written\":" << written_ << ",\"dropped\":" << queue_.Dropped()
            << ",\"worlds\":" << worlds << "}\n";
    stream_.flush();
    failed_ = failed_ || !stream_;
  }
  bool Failed() const { return failed_.load(); }
  uint64_t Written() const { return written_; }  // After Finish only.
  uint64_t Dropped() const { return queue_.Dropped(); }
 private:
  size_t Drain() {
    std::array<Step, 256> batch;
    const auto count = queue_.Drain(batch.data(), batch.size());
    for (size_t i = 0; i < count; ++i) { WriteStep(stream_, batch[i]); ++written_; }
    if (count) stream_.flush();
    if (!stream_) failed_ = true;
    return count;
  }
  std::ostream& stream_;
  Queue<16384> queue_;
  std::thread thread_;
  std::atomic<bool> stop_{false}, failed_{false};
  std::atomic<uint64_t> attempted_{0};
  uint64_t written_ = 0;
};
}  // namespace movement_capture
