# Docheol V2 QA

The opt-in runtime driver records tested observations, not a guarantee of fun or human playability. V1 results do not certify V2. Reports identify `specVersion: armor-burrow-v2` and preserve failed assertions and runtime errors.

## Running

The integration owner builds and runs the Windows executable. Do not start a second Unity editor or build process against this project.

```
-bossqa contracts -bossqa-fps 60 -bossqa-output <absolute-report.json>
```

Repeat functional checks at 30 FPS. Record both requested and measured average FPS; a requested cap is not proof of sustained performance. Exit code 0 means the assertions passed; exit code 2 means a failure.

Use `-logFile <absolute-log-path>` for Unity logs. Add `-bossqa-capture <directory>` for at most 40 native rendered screenshots per run. Run the interactive game visibly for visual inspection: previous hidden-window captures failed or returned black frames. A file existing or an error-free capture is not proof of a valid image; inspect its actual content. These are engine captures of automated input, not manual keyboard play.

## Test modes

| Mode | Conditions and purpose |
| --- | --- |
| `contracts` | Stops the boss. Exercises existing movement, jump, accepted dash distance/duration, pause/resume input, dash hit knockback, damage deduplication, invulnerability, death, reset, scene retry and mute retention. Synthetic damage is explicitly flagged. |
| `phases` | Synthetic boss damage crosses 40 to 30 to 24 to 20 HP. Checks four initial armor pieces, local piece loss at 75%/60%, immediate protection of both sides at 50%, no damage/active hitboxes throughout transition, zero armor in phase 2, and return through a first telegraph. Calls the phase gate with forbidden P1 patterns and requires rejection. |
| `patterns` | Controlled setup/health changes and forced patterns. Covers P1 DT01/02/03 and P2 DT01/02/03/05/06. Disables player hit invulnerability for one-hit-per-attack tests. Checks every observed physics-step target lock and horizontal warning coverage, whole-body DT05 rise/landing, fixed recovery facing/position and V2 recovery timings. |
| `punish` | Controlled P1 DT01/02/03 and P2 DT05/06 setups. After a 0.2-second reaction interval, ordinary walking/timed jump evades, then ordinary walking and attacks use the recovery. Records accepted hits, damage, first-hit scaled latency, recovery duration, initial gap, movement distances and accepted attack count. No arbitrary hit-count threshold or fun claim. Separate front/body/rear probes each require one ordinary attack, one accepted damage event and exactly one boss hurt receiver. |
| `punishsafe` | Same controlled initial five attack setups as `punish`, but stops attacking after one accepted hit on P1 attacks or two on DT05/06. Then observes and evades the next production-AI telegraph and active attack with ordinary walking/jump input after a 0.2-second recognition interval. Requires intended accepted attacks/hits, initial evasion and damage-free natural follow-up. No forced selection, HP/position reset or timer/AI changes from the first forced attack through follow-up completion. |
| `intro` | Suppresses automatic QA start. Observes intro protection for 0.6 seconds, calls the same `StartFight` entry point as Enter/button, checks immediate skip, then verifies real retry bypasses the intro. It does not inject a keyboard Enter event. |
| `intro -bossqa-intro-wait 8` | Lets the approximately seven-second intro complete automatically before verifying combat and retry. The wait is an additional CLI option, not part of the mode name. |
| `mash` | Ordinary attack input only: no direction, jump or dash. Requires real engagement (at least six attack attempts and three damage dealt) before accepting player death with the boss alive as evidence that stationary mashing fails. A bot that never reaches the boss is not a valid comparison. |
| `victory` | Ordinary movement/jump/dash/attack input bot. No injected damage, health edits, forced patterns or teleports. Requires victory with the player alive, then captures the result and performs the actual scene retry. Includes phase/armor/lock/duplicate-hit observations. |
| `defeat` | Neutral input, natural boss damage only, up to 120 real seconds. Checks player death, blocked post-death input, result screen, real scene retry, full health and initial AI state. |
| `survival` | Ordinary evasion/attack bot for 75 seconds. Diagnoses survivability; failure alone does not establish unfairness. |

`usesSyntheticDamage`, `usesForcedPatterns`, `usesTestRepositioning` and `usesModifiedInvulnerability` disclose controlled test conditions. `punish` and `punishsafe` use synthetic damage only to prepare phase 2, forced selection and controlled placement before measurement, and normal invulnerability throughout. From forced pattern start through recovery end it uses only ordinary input. The final body probes in `punish` are separately marked controlled placements; `punishsafe` does not repeat them. The natural modes do not alter game health, damage, positions or attack selection. Normal retry restores game state through the production session method.

## V2 assertions

- P1 has four initial plates and stays grounded; DT05/DT06 must not occur in P1.
- Armor loses one local piece at HP 30 (75%) and another at HP 24 (60%). HP 20 starts the protected transition. Phase 2 must have no armor.
- Each attack record contains phase, pattern, armor at start, locked target, locked warning footprint, number of active physics steps and accepted damage count. `testCase` separates controlled setups; the controller's static increasing attack IDs already remain unique across resets.
- After `TargetLocked`, target X cannot change. Every active horizontal damage extent must lie inside the previously displayed locked warning footprint. This is a horizontal footprint test, not an assertion that a floor marker represents the entire vertical volume.
- Controlled standing-in-danger tests use zero hit invulnerability and require exactly one accepted damage event per attack ID, including DT05 fall/landing and DT06 eruption.
- Recovery nominal values are DT01/02 0.85 seconds, DT03 0.95, DT05/06 1.25. The test accounts for the first rendered observation occurring shortly after recovery starts. These are implementation tuning values, not Notion-confirmed constants.
- Wall tests allow 0.2 seconds after target lock for recognition. DT01/03/05 walk to a reachable edge. DT02 uses a timed stationary jump. DT06 uses ordinary walking from a dangerous near-wall position, and separately checks that the clamped extreme-wall safe zone remains safe. During evasion there is no teleport, HP reset, dash or synthetic damage.
- Forced wall bite tests intentionally exceed the natural scheduler's forward-space restrictions; reports must not imply those forced cases are naturally selected.
- DT05/DT06 tests deliberately move the target after lock and check tracking does not resume.

Dash distance is sampled from actual `Rigidbody2D.position` deltas for accepted dash physics steps. Request-to-acceptance real time is recorded separately. The distance range remains 2.5–4.1 units; physical duration must be approximately 0.28 seconds (one physics-step tolerance). Neither test changes keyboard behavior.

## Final evidence and limits

Run contracts, phases and patterns at both 30 and 60 FPS. Run natural victory/retry, natural defeat/retry, stationary mash and both intro paths. Keep the executable/build identifier, JSON, full Unity logs and inspected screenshots together. Do not summarize a partially failed run as a pass.

Human or agent-directed visual review must separately assess readable armor loss, transition, DT05 compression/leap/landing, underground travel and eruption tell, foreground overlap, hit feedback/audio, pace, recovery opportunity and frustration. Passing automated assertions does not certify those qualities. Agent-directed paused input steps are also distinct from real-time manual keyboard play.

## Recovery measurement details

The `punishWindows` array contains one record per selected attack. `firstHitLatency` uses scaled game seconds from recovery start, reconstructed from the controller's nominal duration minus `RecoveryRemaining`; `-1` means no recovery hit. Damage is counted from accepted `Health2D.DamageTaken` events while the same boss attack ID is in Recovery. Late hits during the next telegraph are excluded. The 0.2-second reaction interval, movement route and walk-only return are this repeatable input strategy, not an optimal-play claim. Zero, one, two or three observed hits are all preserved as tuning evidence. Functional setup/completion/avoidance assertions remain separate from those counts.

Only one native screenshot request is issued per rendered frame. Start, case reset and completion boundaries allow a rendered frame before further scene mutations; intro captures additionally sample stage progress 45–65%. The recorded screenshot list contains issued requests, not filesystem/content verification; inspect the output files independently.
The revised natural bot (`inputStrategy: reactive_walk_jump_nearest_body_v2`) walks to the nearest safe warning edge instead of overshooting it with a full dash. It attacks as soon as actual body hurt range is reachable. For DT02 it predicts the leading edge's arrival using wave speed, both half widths and phase tell progress, and requests a grounded jump about 0.16 seconds before contact; the previous constant 1.4-unit center-distance trigger afforded only about 0.073 seconds at full wave speed. These are input strategy changes only. The preview defeat is retained as a failed run; revised strategy success still requires a new executable run.
`punishsafe` distinguishes limited safe retaliation from the unlimited recovery measurement. Each `punishWindows` record includes `limitedHits`, `intendedHits`, the naturally chosen follow-up ID/pattern, observed telegraph/active/completion flags and accepted incoming hit count through that follow-up. It makes no claim about every possible later attack sequence. Controlled P1 starts at 40 HP and P2 at 20 after completing its real transition, so the intended one/two damage does not itself trigger another phase transition.

The QA reach calculation uses the preserved serialized player `attackOffset` of 0.8 (confirmed in DocheolArena, BasicMovement and SampleScene), plus the actual collider half width. It no longer reads an uninitialized sword transform before the first attack. Build02's accepted damage counts remain valid, but its first DT01 return-distance/latency should not be treated as an equal-strategy comparison with later cases; those timing measurements need rerunning with the corrected helper. Player scripts and attack behavior are unchanged.
## Armor comparison captures

The controlled `phases` mode captures four attached armor plates, three at HP 30, two at HP 24, and zero after transition. The camera stays fixed; normal boss approach can cause small position changes. These are synthetic threshold checks, not natural-play victory evidence. Native capture requests are separated by rendered frames. Inspect the images to verify ivory skin, the same two brown cloven hooves, and the same two dorsal trees through armor loss.
The retained `contracts30-final` failure (2.129 units) measured the request window through an interpolated transform. The corrected `walk_speed` probe records 25 actual commanded physics steps after movement acceptance using Rigidbody2D.position. It reports acceptance latency separately, counts unexpected stopped steps, and keeps the original 2.15 to 2.9-unit bounds. `patterns30-final` retained a late-jump failure because its wall fixture alone still used the old 1.4-unit trigger. Wall evasion now shares the same 0.16-second arrival prediction as the natural and punish bots. These are QA instrumentation/input corrections; production player movement, boss damage, hitboxes and timing are unchanged. Preserve the failed reports and require new executable runs after the corrections.
## Verified Windows rerun (2026-10-03)

Unity 6000.3.23f1 Windows64 build `build-d897cc32dc` succeeded with zero errors and warnings. The new executable passed `contracts30-verified` (22), `contracts60-verified` (22), `patterns30-verified` (62), `patterns60-verified` (62), and `victory60-verified` (10): 178 of 178 assertions, no recorded runtime errors. Both walk probes measured 2.382 units over exactly 25 commanded physics steps / 0.500 seconds. The earlier failures remain in `contracts30-final.json` and `patterns30-final.json` for comparison.

Reports and native screenshots are in the sibling `QA/V2` directory outside this Unity repository. `captures-phases60-final` includes `02-armor-4-full-controlled.png`, `03-armor-3-hp75-controlled.png`, `04-armor-2-hp60-controlled.png`, and `06-armor-0-phase2-controlled.png`. Their Art02 production visuals are unchanged in the final QA-only rebuild. These images were opened and inspected, including partial-armor debris; they are not precisely pixel-aligned poses. The final QA rerun changed measurement/input strategy only and is not a new art or gameplay revision. Full selected-case provenance is retained in `QA/V2/validation-summary.json`.
