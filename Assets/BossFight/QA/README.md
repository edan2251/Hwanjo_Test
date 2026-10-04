# Docheol V3 contract revision QA

The executable driver reports `combo-rockfall-v3-contract-r1`. Run reports, screenshots, logs, build identity and retained failures live outside the Unity project in `../../../../QA/V3/r1/`. The final selected results are in `verification.md`; the earlier V3 report is preserved in `../../../../QA/V3/baseline-963ed8e/verification.md`. Earlier counts do not certify this revision.

## Running

Build the Windows x64 executable from `Assets/BossFight/Scenes/DocheolArena.unity`. Each run needs a unique absolute output/capture/log path. Repeat combat cases at 30 and 60 FPS; a requested cap is not a sustained performance claim.

```text
-bossqa v3matrix -bossqa-fps 30 -bossqa-output <report.json> -bossqa-capture <directory> -logFile <log.txt>
-bossqa v3matrix -bossqa-case 3-2 -bossqa-fps 60 ...
-bossqa v3matrix -bossqa-case B -bossqa-phase 2 -bossqa-fps 60 ...
-bossqa v3revision -bossqa-fps 60 ...
-bossqa v3ai -bossqa-seconds 120 -bossqa-fps 60 ...
```

Exit 0 means all recorded assertions passed; exit 2 preserves failures. Never overwrite a failed run with a rerun. Capture requests are limited to forty per run and one per rendered frame; inspect the actual PNGs separately.

| Mode | Evidence and limits |
| --- | --- |
| `v3contracts` / `contracts` | Original physical walk/dash travel and duration, jump, pause/resume, no queued input, hurt/death blocking, damage-ID deduplication, retry and mute. Boss stopped and synthetic damage/reset disclosed. |
| `v3phases` | Shared HP40 thresholds at30/24/20, four different hit regions, deterministic armor order, protected transition and unarmored P2. Controlled setup/synthetic damage disclosed. |
| `v3matrix` | Both phases' allowed whole plans, rejection of retired attacks, safe versus greedy and attack/dash-cancel 3-to-2, airborne-to-inside 2-to-1, A with ordinary attacks and physical escape, B rest and linked-basic exits at center and both walls. The measured input also continues into a natural next attack. |
| `v3revision` | Actual attack locks during A's roar/basic tell at left/right corners, overlapping rock warnings, unavailable-preview waiting, no new rock/basic choice while locked, ordinary safe paths and retained rock/basic activity. Seeded production size draws are compared across two positions/phases and another seed. Separate controlled camera cue measurements compare slam/bite and the rockfall shake cap. A separate initial B fixture checks that the QA selection hook also rejects A inside B's package. |
| `v3ai` | Production scheduler with ordinary input, phase/plan counts and preselected B exits. Only the first actual telegraph of a plan counts as an AI choice; waiting states and intermediate combo IDs are excluded. Up to the requested interval, but normal victory/defeat can end it earlier. Finite observations do not prove randomness or balance. |
| `victory` / `survival` | Ordinary input with no forced attacks, synthetic damage, HP/position resets or altered invulnerability during combat. Victory includes production retry. Survival requires alive at the end. |
| `defeat` / `mash` | Actual normal boss damage and death; defeat also checks retry/input blocking. Mash requires engagement and death, not merely failing to reach the boss. |
| `intro` | Intro protection/shared StartFight entry/retry. `-bossqa-intro-wait 8` observes full natural introduction. API-driven checks do not certify physical Enter input. |
| `armorvisual` | Presentation-only fixed camera/root/idle masks15/14/12/0, unchanged body/two horns/two trees/two hooves, independent local hits, fade and rig reset. Explicit override label. Does not prove natural HP progression. |
| `observe` | Passive observer preserves real keyboard and introduction. No SubmitInput, forced pattern, synthetic damage, teleport or retry API. Its one error assertion is separate from external Windows key logs and visual judgments. |

## Combat and input measurements

`usesSyntheticDamage`, `usesForcedPatterns`, `usesTestRepositioning`, `usesModifiedInvulnerability`, and `usesPresentationOverrides` disclose setup. Controlled cases reset actors only between cases; P2 and late-P1 A use disclosed initial HP changes. Normal invulnerability remains .8 seconds. Once a selected plan starts, evasion uses ordinary `SubmitInput`, with no timer advance or damage immunity. The camera portion of `v3revision` temporarily disables the session's feedback updates and uses existing cue methods; this is a presentation override, not gameplay evidence.

The input strategy waits .2 scaled seconds after a current locked warning becomes observable. The selected next basic's footprint is now visible from Link, before its tell. Low-paw jump input waits until the current tell has at most .22 seconds remaining; this prevents jumping and landing too early during the explicit .90-second B followup tell. It uses observed current tell progress, never a future combo/route API. Requested and actually accepted jumps are recorded separately; discarded ordinary button requests can be retried.

The matched 3-to-2 fixtures use the same initial positions. Safe input jumps; greed requests a normal attack at Link; the separate cancellation probe requests the ordinary outward dash at least .12 scaled seconds after an accepted attack, preserving the original cancellation behavior. The cancellation probe now requires an accepted attack/dash and actual incoming damage before the chosen combo's final recovery. It can still deal one hit while taking one: the corrected contract is risk, not a claim that all mid-combo hits are impossible. Damage after unrelated random followups is excluded from the risk comparison.

Production 3-to-2 commits to a 3.5-unit body advance (clipped by body arena bounds) during the followup sweep tell. Its low damage rectangle stays 3.8 by .60; it is not enlarged to catch the dash. Link is .08 seconds and the sweep tell is .30. Facing, endpoint and footprint are fixed before the new Link warning. No reactive extra attack, late tracking or damage increase is used. The 2-to-1 test requires actual JumpUp, player displacement inward, presence inside the active slam and no incoming damage.

B chooses rest or a phase-valid basic plan before submerging. Rest gives genuine1.15-second Recovery. A linked plan uses .24 seconds of bracing and a first tell of at least .90 seconds, then its normal final recovery. Tests force each initial branch explicitly at center and walls and verify phase1 has no wave, phase2 has a separate delayed warning and both physical wave sides. No A is permitted before a real recovery; after a completed real recovery the ordinary scheduler can choose A again.

A placement and selection fail closed. With a locked player action or no verified candidate, the boss stays grounded/hittable in a .12-second waiting state and reassesses; it does not invent a safe combo. While waiting with no committed attack, a separation below3.2 units makes it regain spacing at the existing idle speed2.4, using a3.6-unit destination clipped to body bounds and choosing the inward side if the wall prevents spacing. This movement deals no damage. New rocks are deferred during action locks, and rejected placements are skipped. Existing committed attacks/hazards still resolve. The path preview is an approximation, not a universal safety proof, and sustained action locks can reduce density substantially. Revision reports retain actual spawn/basic/wait counts so reduced activity is visible.

Rock size draws use a local seeded generator independent of global particle/audio randomness. Base weights are small45/medium35/large20; no size repeats more than twice and large never repeats immediately. Identical seeds reproduce the size draw prefix, not an entire battle replay or identical accepted placements. Geometry/route rejection can change which drawn sizes are spawned. The production default seed is20261003; setup-only seed overrides are disclosed.

Raw physical evidence checks locked target stability, positive active steps for completed attacks, the correct stage's warning enclosing actual damage, B landing and phase-specific waves, and exact accepted attack-ID attribution. The controller's passive `LastCompletedAttackId` preserves completion evidence when the next ID begins at Link. Late phase transitions and unfinished final tells remain raw observations, not invented completions. Zero incoming hits does not stress deduplication.

## Aligned armor and local-hit captures

Run `-bossqa armorvisual -bossqa-fps 60 -bossqa-output <report.json> -bossqa-capture <directory> -logFile <log.txt>` and repeat at 30 FPS. This presentation-only fixture freezes scaled time, disables combat/session/HUD updates and labels its images explicitly. The four snapshots use masks15,14,12,0, corresponding to four, three, two and zero attached plates. The transient one-plate transition is tested by `v3phases`, not included as a settled comparison pose. `armorCaptures` records each actual mask/count, frame, camera position/size, root position and screenshot request. Both hooves, both horns, body and the existing two-tree sprite must keep their identities and transforms.

The same mode checks independent simultaneous forehead/dorsal flashes, an untouched white shoulder, marks following rig translation, bare-skin color/fade, and `ConfigureV2` clearing hit/death state and queued debris with exactly one active rig before deferred destruction. Seven native images cover the two-region hit, four armor levels, the skin hit and its cleared state. Native capture requests wait across rendering boundaries; missing capture output/batch mode fails request checks. Actual PNG contents and existence require separate inspection. No image-generation or replacement artwork is used.

These forced visual masks cannot certify natural armor thresholds. `v3phases` applies disclosed synthetic HP changes through the real controller and checks HP30/24/20, the protected transition, fixed piece order independent of hit location, and zero armor in P2.

## Preserved failures and limitations

Candidate `r1b03` passed the related combat/player/presentation runs but failed both stationary-mash runs: playerHP1, bossHP0 after seven incoming hits. Its fail-closed A wait could leave a close boss stationary throughout consecutive attack locks. The next candidate regained normal grounded spacing during waiting only, without damage or invulnerability changes; both mash cases then ended in actual defeat.

The `r1b04` revision30 audit reported one locked telegraph. Static inspection found that its counter included already committed combo followups as new selections; the old report did not retain the offending step ID. `r1b05` distinguishes `ComboIndex == 0` new choices from committed followups, retains the zero-new-choice assertion, records `committedFollowupsWhileLocked`, and logs every audited ID/plan/step/action. Existing committed attacks are supposed to continue; silently canceling them would change the contract. The final safety activity count is whole basic plans rather than all intermediate telegraph cues. Preserve the earlier failed report and use final audit events when reviewing this correction.

The original V3's failures/fixes remain in its archived report. In revision candidate `r1b01`, both 3-to-2 FPS probes and both A/camera probes passed, but B's P2 linked2-to-1 cases each took one sweep hit at center and both walls: the old bot jumped immediately after recognition and landed before the longer .90-second tell ended. The later input fix times the jump against the current tell, keeps .2-second recognition, and does not relax zero-hit criteria or change boss timing/damage. Candidate `r1b02` passed both B reruns and the expanded safety probes; the later candidate also tightens the QA hook's B/A exclusion and excludes wait-state artifacts from natural AI-choice counting. Preserve each candidate's exact report and assembly hash.

Build report warning counts, runtime errors and shutdown messages are different evidence. The existing Unity/D3D12 cleanup messages and Unity's URP-asset inclusion warning are retained when present. Short capture-heavy averages and a few-minute combat run are not a long-duration FPS benchmark. Scripted OS keys and agent screenshot inspection are not a human fun, difficulty or subjective audio evaluation. Original player sources, original artwork and older executable folders are preserved; no new paid API or image generation is involved.
