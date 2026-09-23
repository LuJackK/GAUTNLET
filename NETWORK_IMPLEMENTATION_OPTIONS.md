# Networking implementation options for GAUTNLET

Date: 2026-09-16. Status: design comparison; no networking migration selected.

## The requirement driving this decision

Neither player's local simulation should have privileged authority over contested outcomes. Preserve surf movement feel and make melee exchanges understandable under latency. Fairness means equivalent rules and an acceptable competitive experience; it does not mean both players always see the same frame or experience identical corrections.

This document compares four approaches from the discussion: player-hosted authority, independent dedicated-server authority, symmetric deterministic rollback, and rotating authority. The primary candidates for the stated fairness requirement are the middle two. Player hosting is a useful baseline; rotation is an experimental alternative.

Player count, launch platforms, ranked requirements, and optional world physics remain undecided. A two-player LAN diagnostic experiment is a test scope, not a commitment to a 1v1-only release.

## Current project baseline

Static inspection on this date found Unity 6000.3.2f1, FishNet prediction/reconciliation, per-character replay, and diagnostics. The reconcile service resolves received or predicted inputs before simulation and keeps a separate spectator presentation path. Client proxies do not run the same simulation path as owners. These distinctions matter when comparing traces.

The repository also contains newer shared-world foundations. The [world rollback status](C:/Users/LukaJack/Desktop/GAUTNLET/WORLD_ROLLBACK_IMPLEMENTATION.md) describes snapshots, restorable RNG, history, and simultaneous combat resolution, but explicitly says this is not an enabled connected world-rollback mode. Live geometry, health/pickup integration, lifecycle, and presentation still require work. Its recorded test results are historical documentation, not tests rerun for this comparison.

Consequently, switching transport or declaring the movement deterministic would not complete a peer rollback migration. Existing foundations should be extended and validated before considering a rewrite.

## Concepts that apply to all four options

Authority chooses which state or decision is accepted. Prediction advances before remote information arrives. Rollback restores an earlier state and replays subsequent ticks. These are separate choices: a server-authoritative game can use rollback too.

Matching inputs imply matching results only when starting state and simulation behavior also match. A wrong guess about an opponent's input is an expected prediction error. Different outcomes from identical state and inputs are simulation divergence; replay alone cannot guarantee agreement. GGPO requires deterministic stepping and complete save/restore support. [GGPO integration requirements](https://github.com/pond3r/ggpo/blob/master/doc/DeveloperGuide.md)

One peer can predict correctly and need no correction while the other must replay. Even when both replay, their visible correction sizes may differ. Equal rules cannot guarantee equal disruption in each exchange.

## 1. Player-hosted authoritative simulation

### How it works here

One player's process runs the server and their local player. The guest predicts movement, sends inputs, and reconciles to the host's accepted state. Server-side combat decides hits, health, and momentum changes. This is the nearest deployment model to the existing authority structure.

For an attack versus dodge, the host's authoritative simulation resolves the exchange. That is not necessarily identical to every rendered frame on the host's screen, but the local player's input path can avoid the guest's network trip.

### Best situations

- Casual sessions with friends where host privilege is an accepted tradeoff.
- Early gameplay testing and a low-infrastructure comparison baseline.
- Games where player hosting matters more than an independent referee.

### Fit and tradeoffs

It reuses the current stack most directly, but does not satisfy the stated requirement that neither player owns gameplay authority. Artificially delaying the local player's authoritative inputs and matching prediction paths can reduce timing asymmetry. It does not remove the machine owner's control over authority.

Internet hosting may still need connection traversal or relays; low server-simulation cost does not imply zero service cost. Host disconnection and migration need explicit policies.

### Work and evidence needed

Harden current reconciliation and combat behavior, measure host/guest correction differences, and repeat identical scenarios with host roles swapped. This is valuable diagnostic work even if this mode is never shipped competitively.

## 2. Independent dedicated server with client prediction

### How it works here

A separate trusted process owns shared gameplay. Both players predict locally and reconcile to it. FishNet already supports the basic pattern of immediate client prediction with server authority. [FishNet prediction documentation](https://fish-networking.gitbook.io/docs/guides/features/prediction)

For attack versus dodge, the server uses a defined input/tick and hit-validation policy. Historical collision queries or other compensation may be needed, but the exact combat policy must be designed and tested; moving authority off a player's machine does not automatically fix delayed hits.

### Best situations

- Ranked competition requiring an independent referee during a match.
- Larger matches or interacting world systems that make complete peer replay expensive.
- Preserving the current Unity simulation while improving prediction incrementally.

### Fit and tradeoffs

Neither player controls the referee. Different routes, ping, jitter, and buffering still produce unequal conditions. Region selection and explicit late-input and combat policies matter.

Cross-machine bitwise determinism is not a prerequisite for a server to arbitrate state, although divergence can still cause unacceptable corrections. Server authority also does not prevent all cheating, such as automation or misuse of information already sent to clients.

Recurring hosting, deployment, monitoring, and capacity costs are the main operational tradeoffs. The server must be independently controlled for competitive trust. A headless server process running on a player's PC is a useful test topology, not an independent referee.

### Work and evidence needed

Verify server-only startup and gameplay, measure both clients' correction and combat timelines, and establish server tick cost. Improve the existing prediction gap before assuming a full architectural migration is necessary.

## 3. Symmetric deterministic peer rollback

### How it works here

Both peers simulate the complete gameplay world from shared initial state and tick-indexed inputs. Missing remote inputs are predicted. Corrections replay all affected participants and interactions. With identical stepping and input history, neither peer's prediction wins by authority. This follows GGPO's input-driven model. [GGPO developer guide](https://github.com/pond3r/ggpo/blob/master/doc/DeveloperGuide.md)

For attack versus dodge, each peer may initially display a different prediction. Once the required inputs are known and replayed, both resolve the same outcome. If identical replay still disagrees, the session has a desync requiring a defined failure/recovery policy.

### Best situations

- Small, tightly scoped competitive matches where symmetric local simulation is a product priority.
- A team willing to own complete state capture and reproducible world simulation.
- Gameplay whose full replay cost fits the frame budget on the slowest supported machine.

### Fit and tradeoffs

This directly addresses player-authority privilege but is the largest simulation change. Both players' movement, melee, health, momentum, pickups, RNG, respawns, and future dynamic interactions must share one coherent timeline. The existing simultaneous-hit/trade rule in the world-rollback plan must survive that integration.

Unity collision queries are a particular investigation area. Matching builds and fixed ticks alone do not prove equivalent query results across machines. Preserve surf feel as a measured acceptance criterion while investigating the required simulation changes.

Equivalent delay, prediction limits, and late-input rules are essential. Peers still need disconnect and desync handling. Agreement does not prove honest input generation, and two conflicting state hashes cannot identify an honest peer. Matchmaking and result services can coexist with P2P; independent verification requires its own trustworthy evidence and simulation design.

### Work and evidence needed

First prove local save/restore replay, then common-input replay on both machines, then complete world replay, then delayed connected play. Do not allow per-character FishNet replay and a new world driver to advance the same entities concurrently. A limited successful LAN test is evidence for that build and scenario, not proof of all-platform determinism.

## 4. Rotating or randomly selected authority

### How it could work here

An agreed schedule selects the authoritative peer for each interval. The transfer must specify a boundary tick, accepted input history, state snapshot, authority generation, and acknowledgement. The receiver adopts the accepted state before becoming the next authority. Old-generation decisions are rejected. A timeout needs an explicit pause/abort policy so both peers cannot independently claim authority.

Start experiments with a deterministic alternating schedule; random selection adds another variable. If randomness is later tested, its selection must be jointly agreed and resistant to unilateral manipulation. A shared seed alone does not make adversarial participants honest.

### Best situations

- A bounded research prototype testing whether authority distribution changes perceived fairness.
- Casual games that explicitly accept temporary player authority.
- Potentially round-boundary changes, where transfer is simpler and easier to explain than mid-combat changes.

### Fit and tradeoffs

Rotation can spread time in control without making an individual exchange impartial. One interval may contain movement only; another may contain the deciding kill. Accepted damage and momentum persist after handoff. Frequent switching introduces state transfer, correction, and synchronization costs; infrequent switching leaves longer periods of privilege. A modified client gets authority during its intervals.

Known systems support distributed ownership and authority transfer, such as [Unity's ownership model](https://mp-docs.dl.it.unity3d.com/netcode/current/basics/ownership/). That establishes related machinery, not the fairness of randomly switching whole-match authority. No documented example of that exact competitive-fairness strategy was established in this review.

### Work and evidence needed

Treat this as a new protocol, not a setting on the current rollback manager. Define transfer correctness before implementation. Compare it against fixed-host and symmetric alternatives using identical input traces and impairment schedules. Measure authority during decisive events, not just percentage of time assigned to each peer.

## Decision guide

| Requirement | Strongest candidate | Main condition |
|---|---|---|
| Independent live referee | Dedicated server | Hosting and combat compensation are acceptable |
| Neither peer's simulation has privileged authority | Symmetric rollback | Full reproducible world simulation is feasible |
| Fastest casual deployment using current architecture | Player host | Host privilege is acceptable |
| Research distributing temporary privilege | Rotating authority | Fairness remains experimental |
| Preserve current movement with least migration risk | Investigate dedicated-server refinement first | Measure existing corrections before deciding |

Recommendation: use the [LAN diagnostics development plan](C:/Users/LukaJack/Desktop/GAUTNLET/LAN_NETCODE_DIAGNOSTICS_PLAN.md) to gather evidence before choosing between dedicated-server refinement and symmetric world rollback. Keep rotating authority as a separately gated experiment. Revisit the choice when player count, platform scope, trust requirements, and measured replay cost are known.
