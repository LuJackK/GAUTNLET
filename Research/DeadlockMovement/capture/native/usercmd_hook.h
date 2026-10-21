#pragma once
// Inline detour of the server's ProcessUsercmds, the batch ingress for player
// commands. Hand-built because the SDK exposes no hook for it: the function's
// first 15 bytes are four position-independent instructions, so a verified
// copy can be replayed from a trampoline without an instruction decoder.
//
//   void* ProcessUsercmds(void* controller, void* cmds, int numcmds,
//                         unsigned char paused, float margin)
//
// The pattern and prologue are from COMMUNITY_TOOLS_RESEARCH-pinned Deadworks
// and were found exactly once in the installed server.dll on 19 October 2026.
// Anything that does not match refuses to install; nothing is guessed.
#include <cstdint>
#include <cstring>
#include <expected>
#include <functional>
#include <string>

#include "modlock/gameinterop/game_symbols.h"
#include "modlock/gameinterop/signature.h"

#if defined(_WIN32)
#include <windows.h>
#endif

namespace movement_capture {

class UsercmdHook {
 public:
  // controller, first command, command count, paused. Runs before the original.
  using Handler = std::function<void(void*, void*, int, bool)>;
  static constexpr size_t kCommandStride = 0xA8;
  static constexpr size_t kMessageOffset = 0x10;

  static std::expected<UsercmdHook*, std::string> Install(
      const modlock::gameinterop::ModuleImage& server, Handler handler) {
#if defined(_WIN32)
    if (instance_) return std::unexpected("usercmd hook: already installed");
    auto signature = modlock::gameinterop::ParseSignature(
        "usercmd.process", "48 8B C4 44 88 48 ?? 44 89 40 ?? 48 89 50 ?? 53");
    if (!signature) return std::unexpected(signature.error());
    const auto hits = modlock::gameinterop::SignatureScan(server.image_bytes(), *signature);
    if (hits.size() != 1)
      return std::unexpected("usercmd hook: pattern matched " + std::to_string(hits.size()) + " times");
    auto* target = reinterpret_cast<uint8_t*>(server.base() + hits[0]);
    static constexpr uint8_t kPrologue[kStolen] = {
        0x48, 0x8B, 0xC4, 0x44, 0x88, 0x48, 0x20, 0x44, 0x89, 0x40, 0x18, 0x48, 0x89, 0x50, 0x10};
    if (std::memcmp(target, kPrologue, kStolen) != 0)
      return std::unexpected("usercmd hook: prologue differs from the verified build");
    auto* trampoline = static_cast<uint8_t*>(
        VirtualAlloc(nullptr, 64, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
    if (!trampoline) return std::unexpected("usercmd hook: cannot allocate trampoline");
    std::memcpy(trampoline, target, kStolen);
    WriteJump(trampoline + kStolen, target + kStolen);
    DWORD old = 0;
    if (!VirtualProtect(target, kStolen, PAGE_EXECUTE_READWRITE, &old)) {
      VirtualFree(trampoline, 0, MEM_RELEASE);
      return std::unexpected("usercmd hook: cannot unprotect target");
    }
    auto* hook = new UsercmdHook;
    hook->handler_ = std::move(handler);
    hook->target_ = target;
    hook->trampoline_ = trampoline;
    std::memcpy(hook->original_, target, kStolen);
    instance_ = hook;
    WriteJump(target, reinterpret_cast<void*>(&Detour));
    target[14] = 0x90;
    FlushInstructionCache(GetCurrentProcess(), target, kStolen);
    VirtualProtect(target, kStolen, old, &old);
    return hook;
#else
    (void)server; (void)handler;
    return std::unexpected("usercmd hook requires the Windows host");
#endif
  }

  // Restores the original bytes. The trampoline is leaked deliberately: a
  // command batch already inside the detour may still return through it.
  static void Remove() {
#if defined(_WIN32)
    if (!instance_) return;
    DWORD old = 0;
    if (VirtualProtect(instance_->target_, kStolen, PAGE_EXECUTE_READWRITE, &old)) {
      std::memcpy(instance_->target_, instance_->original_, kStolen);
      FlushInstructionCache(GetCurrentProcess(), instance_->target_, kStolen);
      VirtualProtect(instance_->target_, kStolen, old, &old);
    }
    instance_->handler_ = nullptr;  // late callers fall straight through
#endif
  }

 private:
  static constexpr size_t kStolen = 15;
  static void WriteJump(uint8_t* at, void* destination) {
    at[0] = 0xFF; at[1] = 0x25; std::memset(at + 2, 0, 4);
    std::memcpy(at + 6, &destination, 8);
  }
#if defined(_WIN32)
  using Original = void* (__fastcall*)(void*, void*, int, unsigned char, float);
  static void* __fastcall Detour(void* controller, void* cmds, int count, unsigned char paused,
                                 float margin) {
    auto* self = instance_;
    if (self && self->handler_ && cmds && count > 0 && count < 64) {
      try { self->handler_(controller, cmds, count, paused != 0); } catch (...) {}
    }
    return reinterpret_cast<Original>(self->trampoline_)(controller, cmds, count, paused, margin);
  }
  static inline UsercmdHook* instance_ = nullptr;
#endif
  Handler handler_;
  uint8_t* target_ = nullptr;
  uint8_t* trampoline_ = nullptr;
  uint8_t original_[kStolen]{};
};

}  // namespace movement_capture
