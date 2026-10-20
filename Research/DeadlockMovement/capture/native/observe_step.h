#pragma once

namespace movement_capture {
// Used by the patched native dispatch AND the offline test. The pre-state is a
// value copy. The observer receives const views and cannot replace native motion.
template <typename State, typename Native, typename Read, typename Observer>
void ObserveStep(State before, Native native, Read read_after, Observer observer) {
  native();
  State after = before;
  read_after(after);
  observer(static_cast<const State&>(before), static_cast<const State&>(after));
}
}
