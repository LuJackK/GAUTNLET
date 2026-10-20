#pragma once

#include <atomic>
#include <chrono>
#include <deque>
#include <mutex>
#include <ostream>
#include <string>
#include <thread>

namespace movement_capture {
// Frame observations have their own ordering and observation phase. Never join
// them to a movement sample as if the two reads were simultaneous.
class FrameWriter {
 public:
  explicit FrameWriter(std::ostream& stream) : stream_(stream) {}
  ~FrameWriter() { Finish(); }
  void Start(bool synthetic) {
    stream_ << "{\"type\":\"frame_session\",\"schema_version\":1,\"phase\":\"after_game_frame\",\"synthetic\":"
            << (synthetic ? "true" : "false") << "}\n";
    stream_.flush();
    thread_ = std::thread([this] {
      while (!stop_) { Drain(); std::this_thread::sleep_for(std::chrono::milliseconds(20)); }
      Drain();
    });
  }
  // Engine thread only. Payload contains JSON members, without outer braces.
  void Push(std::string payload) {
    const auto sequence = attempted_++;
    std::lock_guard lock(mutex_);
    if (queue_.size() >= 8192) { ++dropped_; return; }
    queue_.push_back("{\"sequence\":" + std::to_string(sequence) + "," + payload + "}\n");
  }
  void Finish() {
    if (!thread_.joinable()) return;
    stop_ = true;
    thread_.join();
    stream_ << "{\"type\":\"frame_end\",\"attempted\":" << attempted_
            << ",\"written\":" << written_ << ",\"dropped\":" << dropped_ << "}\n";
    stream_.flush();
    failed_ = failed_ || !stream_;
  }
  bool Failed() const { return failed_ || dropped_ != 0; }
 private:
  void Drain() {
    std::deque<std::string> batch;
    { std::lock_guard lock(mutex_); batch.swap(queue_); }
    for (const auto& line : batch) { stream_ << line; ++written_; }
    if (!batch.empty()) stream_.flush();
    if (!stream_) failed_ = true;
  }
  std::ostream& stream_;
  std::deque<std::string> queue_;
  std::mutex mutex_;
  std::thread thread_;
  std::atomic<bool> stop_{false}, failed_{false};
  uint64_t attempted_ = 0, written_ = 0, dropped_ = 0;
};
}  // namespace movement_capture
