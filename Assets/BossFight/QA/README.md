# Boss encounter QA

The executable includes an opt-in QA driver. Ordinary launches do not create it.

## Invocation

Run the Windows executable with:

```
-bossqa contracts -bossqa-fps 60 -bossqa-output <absolute-path-to-report.json>
```

Repeat at `-bossqa-fps 30`. Exit code 0 means all checks passed; 2 means at least one failed. The report records requested and measured average frame rates, assertions, damage events, runtime errors, and outcome. Non-batch execution also captures a PNG next to the JSON before exiting. Use `-logFile <absolute-path>` for the full Unity log. Add `-bossqa-capture <directory>` to save up to 20 actual rendered frames from start, each pattern telegraph/active/recovery, DT05 apex, phase 2, and outcome. Victory/defeat outcome capture waits at least 0.95 seconds for the result overlay. Each screenshot path is listed in the JSON. These are engine screenshots of automated input; they are not evidence of manual play.

Modes:

- `contracts`: stops the boss and exercises real player movement/jump/dash (dash measurement starts after Action.Dash acceptance and sums actual Rigidbody2D position deltas over dash physics steps; request-to-acceptance delay is reported separately), pause/resume rejection of queued gameplay input and accepted damage overriding forward dash momentum with the requested backward knockback, then synthetic damage to verify attack-ID deduplication after invulnerability expires, invulnerability across different attacks, lethal damage, blocked input while dead, reset of health/attack ledger, and the actual Session.Retry scene reload with a new player instance, full health, zero horizontal velocity, timeScale 1, preserved mute state and a dormant boss. The mute check uses the normal ToggleMute action and restores the initial preference after verification. These are API integration checks, not a claim that a human can beat the boss.
- `patterns`: explicitly forces each implemented pattern and places the test player in its warning zone. With hit invulnerability set to zero, verifies exactly one damage event per attack; also checks locked target/attack ID, DT05 whole-body rise and landing, fixed recovery position/facing, and minimum recovery window. Additional left/right wall cases begin inside the warning range and allow 0.2 seconds of recognition time. DT01/DT03/DT05 use ordinary inward walking; DT02 stays at the wall and performs a timed jump as the traveling wave approaches. No teleport, health reset, dash, or damage injection occurs during these evasion windows. DT03 wall cases deliberately force a stress scenario that natural AI normally excludes through its forward-space requirement. The report identifies these controlled conditions with `usesForcedPatterns: true`. These are controlled integration tests, not an unassisted playthrough.
- `victory`: uses the same input submission path as player controls, without changing HP or damage, for up to 180 seconds. Recovery approach targets are clamped to reachable arena space and the real mouth hurtbox plus sword reach, preventing wall-side approach deadlock. Checks target lock, observed attack cycles and a living-player victory. After a 0.95-second result-screen wait and victory capture, invokes the real session Retry and verifies a new player instance, HP 8/boss HP 40, timeScale 1 and initial attack state; successful completion reports `victory_retried`.
- `defeat`: submits neutral input and waits up to 120 real seconds for natural boss attacks to kill the player. Does not change HP, damage, position or patterns. Verifies Defeat/HP 0 and blocked movement/attack, captures the result after one second, invokes the real session Retry, then checks a new player instance, HP 8/boss HP 40, timeScale 1 and initial attack state.
- `survival`: the same ordinary-input bot for up to 75 seconds; checks survival and observed attacks. A bot failure requires diagnosis: it does not alone establish that an attack is unfair.

`contracts` intentionally uses synthetic lethal damage. The JSON identifies this with `usesSyntheticDamage: true`. The `patterns` mode resets health between controlled scenarios and changes invulnerability for the deduplication check. The `survival`, `victory` and `defeat` modes do not inject damage or alter health. Defeat mode uses only the normal game retry to restore health after natural death.

## Required independent play verification

- Run both 30 and 60 FPS contract checks in the actual Windows build.
- Confirm the complete loop from introduction through player death and retry, and separately boss death and victory/retry. Scene reload must reset HP, movement, attack state, camera, VFX, UI, and timeScale.
- Try each attack at both arena walls. Escape using movement and jump; dash has no invulnerability.
- Confirm the warning target freezes before release and remains visible throughout DT05 whole-body rise, hover and landing.
- Confirm DT05 falling body and landing cannot damage the player twice in one attack instance.
- During recovery, verify the boss holds position/facing and the player can approach and connect 2–3 ordinary attacks.
- Confirm the low traveling sweep is avoidable by a timed jump at near, mid and far distances. Jump timing depends on wave arrival, not just release.
- Inspect real rendered screenshots, readable health/control text, hit feedback/audio, and absence of runtime errors.

## Initial baseline audit

The pre-change Editor log records Unity 6000.3.23f1 opening this clone and shutting down normally, with no matched C# compiler errors or NullReferenceException. This is historical evidence, not a fresh compilation test. Unity Personal licensing recovered after an initial IPC handshake retry. Test Framework 1.6.0 was present; no project test assembly existed. Physics is 50 Hz (fixedDeltaTime 0.02), input uses the new Input System only. GUID scanning against Assets and PackageCache found unresolved references only in existing DefaultVolumeProfile/PC_Renderer settings, not the existing scenes/prefabs/animations.

No automated run has passed merely by this file existing. Cite the generated JSON and Unity log from the tested build as evidence.
