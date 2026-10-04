# Docheol V4 QA

V4 reports use `docheol-root-reach-v4`. The approved specification and retained candidate history are in `../../../../QA/V4/APPROVED_SPEC.md` and `CHECKPOINT.md`. Selected final evidence is [verification](../../../../QA/V4/verification.md) and [artifact audit](../../../../QA/V4/artifact-audit.json). V3 and VisualQa results certify their original binaries; do not combine their counts with V4. The previous QA README is preserved in `../../../Documentation/Docheol_V3_QA_Archived.md`.

## Build and run

Unity6000.3.23f1, Windows x64, scene `Assets/BossFight/Scenes/DocheolArenaV4.unity`. `FourGuardians.BossFight.Editor.BossFightBuilder.BuildWindows` builds `../Deliverables/Hwanjo_Docheol_V4/Hwanjo_Docheol_V4.exe`. Keep the executable, Data folder and Unity DLLs together. Editor batch builds can be hidden; capture runs require a visible window.

```text
-screen-fullscreen 0 -screen-width 1280 -screen-height 720
-bossqa v4core -bossqa-fps 60 -bossqa-output <unique-absolute.json> -bossqa-capture <unique-directory> -logFile <unique-log>
-bossqa v4burrow -bossqa-phase 1 -bossqa-fps 30 ...
-bossqa v4whole -bossqa-fps 60 ...
-bossqa v4phase -bossqa-fps 30 ...
-bossqa v4natural -bossqa-seconds 150 -bossqa-fps 30 ...
-bossqa v4death -bossqa-fps 60 ...
-bossqa v4observe -bossqa-seconds 120 -bossqa-fps 60 ...
-bossqa armorvisual -bossqa-fps 60 ...
```

Exit0 means all recorded assertions passed; exit2 preserves a failure. Never overwrite failed runs. Requested FPS is a cap, not a benchmark. V4 captures at most80 unique labels and one request per rendered frame. Verify actual PNGs independently; a request is not a file or a visual judgment.

| Mode | Checks and limits |
| --- | --- |
| `v4core` | Retired1/2→1/3→2→1 rejected; root overlap camping leads to spacing and incoming damage; grounded preparation stays hittable; committed turn despite crossing; wall spacing without crossing; matched bite/sweep/3→2 evasion and greedy inputs; actual ordinary counter damage in stationary recovery; actual dash travels outside sweep and has no extra immunity. Initial pose/plan is controlled. |
| `v4burrow` | Each phase at center/left/right wall, rest and3→2 exit. First-tell target/facing locked, landing and delayed wave observed, physical normal jump/walk evasion, wall target opens space, linked normal tell followed by final recovery. P2 is seeded through disclosed synthetic HP and the real protected entry/rock window before the measured B case. |
| `v4whole` | P2 whole-body left/right two distinct contacts and preselected targets; no retargeting; ≥1.65s observed promised recovery; safe0 incoming, greed positive incoming; ordinary attacks deal≥2 damage in long recovery. Initial P2 fixture is synthetic; measured input is ordinary movement/attack. |
| `v4phase` | All armor off and controls/health locked through stagger/wail, fixed armor order independent of initial hit region, control returns≥.35s before first rock warning, both corners have a physical route, late HP10 queue waits for whole3→2 and final recovery, exactly2 windows with B/whole excluded, actual HP0 skips pending late event. HP changes and between-case positions are disclosed. |
| `v4natural` | Production scheduler and ordinary input until victory/defeat/cap. No forced plans, teleport, HP reset/damage injection or modified invulnerability during combat. First tells record distinct AI choices. **Bot reads current geometry and the internal timed rock route API**; it is not a human reaction or difficulty test. |
| `v4death` | Real normal boss damage to defeat, ordinary dead input blocked, retry API resets HP8/40, mask15, phase1 and rock count0. Explicit API retry; does not certify physical R. |
| `v4observe` | Passive real keyboard observer, preserved intro. No SubmitInput, forced patterns, HP mutation, teleport or retry API. Rebinds scene actors after real R without changing gameplay. External Windows scan-code logs and still review are required to certify the physical action. |
| `armorvisual` | Presentation-only masks15/14/12/8/0 with fixed camera/root/idle rig; independent local hits, exposed/bare skin tint/fade and ConfigureV2 reset including queued/live debris. Override label is shown. Does not prove natural armor thresholds or combat. |

`usesControlledSetup`, `usesSyntheticDamage`, `usesForcedPatterns`, `usesTestRepositioning`, `usesModifiedInvulnerability`, `usesApiRetry` and `inputStrategy` disclose the V4 driver setup. Original normal player immunity remains.8s. The input bot recognizes current attacks after.2 scaled seconds. Sweep jump requests are timed to≤.22s remaining in the current tell. Accepted JumpUp/attack counters are separate from requests. It does not advance timers or change damage rules.

## Presentation regression and still review

The retained `armorvisual` driver reports `local-hit-armor-capture-r2`. Compare attached armor4/3/2/1/0 using actual masks15/14/12/8/0; one remaining plate is a fixed override of a transient production state. Inspect the simultaneous forehead/dorsal local flash, untouched plates/skin, exposed skin with remaining plates, bare-skin fade, and complete rig/reset assertions. Final selected links and build identity are in verification.md.

Core stills include turn retract / root crossing / extend and complete, close camping/backdash, wall spacing, locked ground warnings and actual evasion/counters. Whole stills include both contact sides and mid-cross body. Physical input uses Windows SendInput to the verified foreground V4 process, releases held keys in finally and records actions/timing. It captures only the target client area. Passive observer state/action/attack-sequence logs support screenshots; individual scripted screenshots alone cannot prove an accepted input.

## Retained candidates and interpretation

Core-b01 had a C# QA variable-name compile error. Core-b02 had zero damage in the matched greedy3→2 case; b03 kept the chain at a fixed root and covered the paw's actual near-root region, then passed 30/60 FPS core probes. Special-b04 had3 missing PNGs because Unity kept the final screenshot request in the same frame; b05 limits requests to one per frame and passed the full functional matrix. B05 is preserved with its exact reports/hash. B06 changes observer scene rebinding and adds intermediate native poses; production controller, player, Visual and timing code remain identical to b05. The first OS run sent early keys during the splash; a later run failed foreground focus before sending keys. Both are retained. Ready2 verified controls/death/R but revealed magenta dash dust. B07 assigns the existing compatible material to the copied arena's particle renderer; original player/FX assets and gameplay remain unchanged. Final run wrappers record Assembly, scene and shared-assets hashes, since a scene-only fix can keep the same Assembly hash.

Actual OS input and agent still inspection are structured QA, not human fun, balance, difficulty or subjective audio validation. One bot win with HP7 is not a balance judgment. Continuous 2D contraction has a thin center silhouette; high leap can bring horns close to the HUD. Safe-route planning is an approximation and may reduce hazard density during sustained locked actions. Shutdown ComputeBuffer/D3D12 cleanup messages are retained separately from play-time Error/Exception/Assert counts. Long duration performance and arbitrary player input sequences remain outside this finite validation.

B07 phase60 failed the strict late-rock route assertion with one incoming hit. B08 stopped the independent sweep-jump override during a timed rock route and logged accepted source IDs, but the same hit still occurred: rock200017 at14.84s, playerX-3.832 on the ground while a basic was in tell. These failures are retained; the override fix alone is not claimed as sufficient. B09 adds `MoveSpeed*(2*fixedDeltaTime + 1/30)` horizontal planning clearance for rendered-input/fixed-step command travel. Actual damage geometry and original player remain unchanged. This conservative clearance can reject more placements/plans, so actual rock counts and selection events remain visible. The bot keeps its.2s recognition and ordinary physics, without HP/immunity/timer cheats.

Final selected candidate: b09, 16 runs / 269 recorded checks / 478 native PNGs, all passed. Separate physical-input audit: 10 checks from 38 scan-code actions; dust-color audit: 1 check. See linked verification for actual FPS, controlled setups and retained failures.
