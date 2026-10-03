# Docheol V3 executable QA

The opt-in driver measures `specVersion: combo-rockfall-v3`. V2 reports and their success counts do not certify V3. Final executable results and preserved failure history are recorded in `../../../../QA/V3/verification.md` outside the Unity project. Preserve failed reports, logs, build identity and strategy revisions alongside successful reruns.

## Execution

Only the integration owner builds, compiles or launches Unity and the Windows executable. Run the final build at both requested frame caps and retain measured average FPS:

```text
-bossqa v3contracts -bossqa-fps 60 -bossqa-output <absolute-report.json>
-bossqa v3phases -bossqa-fps 60 -bossqa-output <absolute-report.json>
-bossqa v3matrix -bossqa-fps 60 -bossqa-output <absolute-report.json>
-bossqa v3ai -bossqa-fps 60 -bossqa-seconds 120 -bossqa-output <absolute-report.json>
-bossqa victory -bossqa-fps 60 -bossqa-output <absolute-report.json>
-bossqa defeat -bossqa-fps 60 -bossqa-output <absolute-report.json>
```

Repeat at 30 FPS. Use distinct output paths for every build, frame rate and attempt. Exit 0 means all recorded assertions passed; exit 2 preserves at least one failure. A requested cap is not sustained performance evidence. Add `-logFile <absolute-log.txt>` and optionally `-bossqa-capture <directory>`.

`v3matrix -bossqa-phase 2 -bossqa-case B` narrows diagnosis to phase 2 B center/left-wall/right-wall cases. Case values: `all`, `3`, `2`, `1`, `3-2`, `2-1`, `3-2-1`, `A`, `B`. Phase values: `all`, `1`, `2`. A filtered run does not certify the omitted matrix.

## Modes and scope

| Mode | Expected runtime | Measurement |
| --- | --- | --- |
| `v3contracts` / `contracts` | 15 -> 25 seconds | Existing player movement and damage contract: physical walk/dash, jump, pause input rejection, no queued resume action, knockback, attack-ID deduplication, invulnerability, death/input blocking, scene retry and mute retention. Boss stopped; synthetic player damage and reset disclosed. |
| `v3phases` | 15 -> 25 seconds | Controlled shared HP 40 -> 30 -> 24 -> 20, repeated with four different hit locations. Requires fixed armor order 0 then 1, protected transition and zero phase-2 armor. Synthetic damage during transition must be rejected for both actors. |
| `v3matrix` | approximately 3 -> 5 minutes | All allowed singles/combo plans, P1 rejection of 1-containing plans, rejection of retired DT04/05/06, late-P1 A gate, safe versus greedy 3 -> 2 input, 2 -> 1 jump/inside route, B in both phases at center and both walls, A with concurrent basic attacks and actual movement. Each case continues into the next naturally selected attack or through A expiration. |
| `v3ai` | up to 120 seconds; `-bossqa-seconds` override | Production scheduler with ordinary input. Records chronological phase/whole-plan choices and empirical counts, requires at least three distinct phase-plan keys and six completed attacks, and reports early defeat honestly. Repeat runs to inspect distribution; a finite sample does not prove randomness. |
| `victory` | up to 180 seconds | Ordinary movement/jump/attack bot with recognition delay. Requires living-player victory, then production retry. No forced patterns, synthetic damage, position resets or invulnerability changes during combat. |
| `survival` | up to 180 seconds; override allowed | Same natural strategy with explicit alive-at-end assertion. |
| `defeat` | up to 120 seconds | Neutral input, actual boss damage, real defeat state and blocked input, then production retry. |
| `mash` | up to 120 seconds | Stationary normal attacks. Engagement and death are both required; failure to reach the boss is not evidence that mashing loses. |
| `intro` | 1 -> 3 seconds plus retry | Observe protected intro, call shared StartFight entry point and check retry skips intro. This is not synthetic keyboard coverage. |
| `intro -bossqa-intro-wait 8` | 9 -> 12 seconds | Allow natural intro completion before retry checks. |

Retired V2 `phases`, `patterns`, `punish` and `punishsafe` names explicitly fail as unsupported. Their V2 pattern lists, grounded-P1 premise, recovery durations and target retaliation counts are not reused as V3 pass criteria.

## Controlled measurement boundary

`usesSyntheticDamage`, `usesForcedPatterns`, `usesTestRepositioning`, and `usesModifiedInvulnerability` disclose setup. Matrix/phases reset the boss and player only between cases. P2 uses synthetic HP reduction followed by the real protected transition; A in P1 uses initial HP 28. Normal player invulnerability remains 0.8 seconds. After one forced whole plan or special starts, movement and attacks use `SubmitInput`; no teleport, HP restoration, timer advance or successful forced selection is allowed during the measurement. Production retry is separately identified.

The safe input strategy waits at least 0.2 scaled seconds after the current locked warning becomes observable. Delayed B wave warning and new rock warnings receive their own recognition delay. B lock occurs after underground travel. The bot walks out of bite/slam warning bounds, jumps the local front-paw sweep, and jumps the delayed B wave. Rockfall steering samples reachable floor points against the union of recognized visible warnings, then walks there through actual player physics. Geometric corridor existence alone is not an evasion pass.

The greedy 3 -> 2 fixture shares the safe fixture's initial state but advances and requests normal attacks during Link/followup tell. `greed_vs_safe` requires more actual incoming hits than the safe fixture. A concealed recovery UI by itself cannot satisfy this check. Natural followups are unforced, so compare timelines as well as totals when diagnosing a difference.

`v3Cases` records the requested plan, setup disclosure, complete stage/attack-ID/position timeline, accepted player attacks, accepted recovery damage-event count, incoming hits, requested jumps, peak physical feet height, fixed-step count and accumulated `Rigidbody2D.position` travel. `recoveryHits` has no target minimum and is not a fun score. `jump_then_inside` requires an observed airborne sweep response and actual inward movement during the subsequent 1. Every safe case includes zero incoming hits through the observed natural followup; failures remain failures even if the bot survives.

Combo checks require the selected whole plan to remain stable through its final recovery, a visible Link for multi-step plans, no intermediate Recovery, and no facing flip. B requires observed elevation and landing; phase 1 must have no wave, phase 2 must have a wave. A requires observed rockfall, concurrent basics, no B, enabled boss damage, and actual physical movement. Hazard records retain locked X, announced size/width, active samples and fixed-position/coverage checks. `attacks` retain raw target/warning samples; B changes its warning footprint for its separately telegraphed wave, so the old single-footprint V2 assumption is not used to certify B.

## Preserved player measurements

The walk probe is unchanged: after actual movement acceptance, measure 25 commanded physics steps / 0.500 seconds at the standard fixed timestep from `Rigidbody2D.position`. Count stopped steps as failures rather than filtering them. Report request-to-acceptance delay separately and keep the original 2.15 -> 2.9-unit bounds. Dash uses accepted actual physics-step travel (2.5 -> 4.1 units), and duration tolerance is one physics step around the player's preserved dash duration. These checks verify the preserved player, not V3 combat tuning. No original player controller, damage, movement or attack code is modified by QA.

If production parameters change, rerun the scenarios. Change a test fixture only to correct a documented instrumentation error or to exercise the revised mechanic with a legitimate input strategy. Preserve previous failures and explain the changed measurement or route; do not relax pass ranges to match a failed result.

## Screenshots and human review

At most 40 native screenshot requests occur per run, with one request per rendered frame. Each request records elapsed time, frame and visible combat stage; captures target locked tells, active moments, Link, B apex/travel, rockfall, transition and result. The screenshot list records requests, not file/content verification. Inspect actual images for black frames, missing UI, foreground overlap and stage readability. Use filtered cases to reserve capture budget for P2 or a particular special.

Run the visible executable for direct keyboard checks of intro/Enter, movement/jump/dash/attack, pause/resume, retry and mute. API-driven QA is distinct from real keyboard input. Separately assess readable 3 -> 2 and 2 -> 1 decisions, A breathing/roar/rubble size and safe corridors, B high eruption/forward descent/delayed wave, armor order, hit feedback and audio, camera shake, arena edges and reasonable retaliation opportunities. A no-hit bot run is not proof of fun, fairness for all players, or satisfying presentation.

## Observation-only keyboard capture

`-bossqa observe -bossqa-seconds 120 -bossqa-fps 60 -bossqa-output <report.json> -bossqa-capture <directory>` preserves the real keyboard and normal introduction. It does not call StartFight, SetExternalInput, SubmitInput, ForcePattern, synthetic damage or reset/teleport APIs. It records stage/health/timescale observations, rebinds observation references after a user retry, requests timed and stage screenshots, and exits after the real-time interval. Its pass flag certifies only absence of recorded runtime errors; keyboard actions and judgments must be documented in the integration owner's external input timeline and visual review.

The greedy fixture deliberately omits dash cancellation. Its pass demonstrates a penalty for that particular greedy input sequence; it does not prove that an attack followed by dash cancel is unsafe or that every aggressive tactic loses. The comparison uses only hits through the forced plan's final recovery boundary, excluding random natural-followup damage. The 2-to-1 check additionally requires an observed JumpUp action, player displacement toward the boss (not boss travel), physical presence inside the slam during Active, and zero total incoming hits.

## Aligned armor and local-hit presentation snapshots

`-bossqa armorvisual -bossqa-fps 60 -bossqa-output <report.json> -bossqa-capture <capture-directory> -logFile <log.txt>` runs the merged presentation-only fixture. Repeat at 30 FPS. `usesPresentationOverrides` is true and the input strategy explicitly excludes combat/HP progression evidence. The fixture disables session/HUD and combat input, freezes scaled time, and applies V3 visual masks 15, 14, 12 and 0. The four, three, two and zero plate screenshots share root position, camera and idle pose. `armorCaptures` records the masks, visible plate count, frame, camera, root position and requested file. Anatomy identities and transforms must remain unchanged, including both hooves, both horns and the two-tree sprite. Captures have a visible presentation-only label. These visual overrides do not prove HP thresholds or natural armor loss; run `v3phases` separately.

The same fixture retains the verified local-hit regression implementation: simultaneous independent region flashes, marks following rig translation, untouched armor remaining white, bare-skin hit color/fade, and ConfigureV2 clearing tint/death/deferred debris while leaving one live rig. It uses existing artwork. `CaptureVisualFrame` waits across native rendering boundaries and shares the run-wide maximum of 40 screenshots/one per rendered frame. Missing capture directory or batch mode fails capture-request verification. Inspect actual PNG content separately.

The retained historical build `build-424877c945` passed 42/42 assertions for the V2 visual follow-up only: armorvisual 12 each and V2 phases 9 each at requested 60/30 FPS. Those reports live in `C:/Users/molph/Documents/Codex/2026-10-03/task/approved-visual-qa`. They do not certify the merged V3 code. V3 mask order is intentionally 15/14/12/0 rather than the V2 snapshot's 15/13/5/0, and requires fresh rendering and test results. The later final-build runs in the verification report validate the merged V3 presentation separately.
## Separate ordinary attack-to-dash cancellation probe

The `3-2` matrix also records a `_dashcancel` case independently from `_greed` and `_safe`. After evading 3, it requests the ordinary attack during Link/followup tell and an ordinary outward dash at least .12 scaled seconds after the attack sequence is accepted. `cancelAttackAccepted`, `cancelDashRequested`, `cancelDashAccepted`, `cancelDelay`, `comboRetaliationHits` and `planIncomingHits` retain the actual outcome. The assertion requires observation of the requested input actions; it does not require cancellation to be safe or unsafe. A successful attack-and-cancel is a dash-resource/timing exception and must be reported as such. The original player's cancellation behavior is unchanged. Neither the no-cancel greed check nor a hidden recovery indicator proves all mid-combo retaliation impossible.
## V3 physical warning and exact damage attribution correction

Build01 is an initial smoke run, not complete V3 validation. The corrected observer separates B's landing warning (captured at target lock) from the new full-floor warning observed during `wave_delay`. It preserves the first wave-warning time, width and physical-step count, checks warning stability, and verifies both `ActiveBounds` and `SecondaryActiveBounds` against that previously observed warning on every active physics sample. Core wave damage/rendering volumes now end at the arena boundaries; QA still compares raw physical extents without clipping or widening its allowed warning. The landing target remains fixed through both stages.

Final reports now assert each completed damaging attack had positive active physics samples, each observed damaging active attack had a prior telegraph/lock and stable target, and every sampled damage extent was contained in the correct stage's warning. Completed B attacks require sampled landing; P1 requires zero wave samples and P2 requires a prior stable delayed warning plus positive samples on both wave sides. An unfinished final telegraph remains raw evidence and is not silently counted as a completed attack. Passive `observe` and presentation `armorvisual` modes do not receive these combat pass claims.

`DamageInfo.Source` is the same boss object for rocks and basic attacks and has no attack ID. Therefore `OnDamage` now records only total player damage and contextual state. Exact attribution comes from the controller's passive `PlayerDamagedByAttack(int attackId)` event, emitted only after `ReceiveAttack` reports accepted damage. `acceptedDamage` retains every exact ID and its known category; an unresolved ID is labeled explicitly instead of guessed from the current boss pattern. Attack and hazard counters use that ID. The driver unsubscribes from the previous controller and subscribes to a new one after scene retry, passive observer rebinding and initialization, and unsubscribes on destruction. Combat-mode assertions require the accepted-ID event count to equal total player damage and no observed ID to produce more than one accepted hit. Zero incoming hits is not a stress test of deduplication. Preserve Build01 observations and rerun the corrected final build; earlier reports do not establish these new assertions.
## Build03 input and timing correction

Build02 specialA failed one real rock hit because the input strategy chose a safe endpoint but crossed the rock during impact. The route test now considers ordinary walking during already recognized visible impact intervals and observed remaining attack lock. It can withhold a new attack that would make that route unsafe. It does not query hidden future combos, spawn plans or boss route APIs. Position, HP, time, ordinary movement, invulnerability, 0.2-second recognition and zero-hit assertions remain unchanged.

Build02 matrix30 failed 11 assertions with 13 actual sweep hits; nominal B failures were subsequent natural sweep hits. Feet at .330/.474 show two/three successful rising physics steps, short of the .60-high sweep. The .30 followup tell did not cover recognition plus rendered-frame input delivery and four required rising physics steps. Production followup tell is now .40. QA removes an extra .55-progress wait, records the requested and actually accepted JumpUp times separately, and retries ordinary grounded jump requests discarded during a locked action. It never turns a requested jump into a fake state change.

Requested jumps and acceptedJumps are separate counts. Rock-input traces record observed action, attack lock estimate, endpoint and actual requested movement every .1 scaled seconds. These strategies are test inputs, not a global fairness proof. The earlier failed reports remain retained.

The production followup sweep tell is now .40 seconds; standalone .50, strike .16 and Link .08 are unchanged. The final build passed its recorded 30/60 FPS matrix, filtered cases and natural battle checks. Details and limits are in the final verification report; previous failures remain retained.

Build04 also fixes an actual input mismatch found in Build03 matrix60 A: reaching a validated safe endpoint no longer leaves the separate approach-to-boss axis active. At that endpoint the bot holds position and only requests an in-range ordinary attack when grounded and stationary. The tiny facing axis is submitted only with that allowed ground attack. No combat rule or assertion changed. Build03 matrix60 remains a failed run in the evidence.
