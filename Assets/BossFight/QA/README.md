# Docheol V6 QA

Desktop project: `D:\2026_2_TeamProject-BossTest\BossTest_Unity`. Unity **6000.3.23f1**, Windows x64, `Assets/BossFight/Scenes/DocheolArenaV6.unity`. Build with `FourGuardians.BossFight.Editor.BossFightBuilder.BuildWindows`. The V5 b06 commit `158a4263d0e00241b290f177275d68473c53633e` and the complete V5/V4 delivery folders are preserved. Do not mix old candidate assertion counts into V6 results.

## Reproduce

Keep the V6 executable, Data directory and DLLs together. Run one visible 1280x720 window at a time; use unique output/log/capture paths. Requested FPS is a cap, not a sustained-performance certificate.

```text
Hwanjo_Docheol_V6.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -bossqa v6burrow -bossqa-fps 60 -bossqa-output <absolute unique.json> -bossqa-capture <unique directory> -logFile <unique.log>
```

| Mode | What it verifies / disclosed setup |
| --- | --- |
| `v6burrow` | P1/P2 C at center and both walls; P2 A/B; ordinary evasion, locked destinations, no tell/flight contact damage, A resubmerge and cap, B single underground first tell, five IDs and stationary recovery. Initial phase/branch setup is synthetic/forced. A separate disclosed relocation probe measures the C package's one-hit policy. |
| `v6impact` | Real sword root/body contacts, HP5, contact feedback, full attached white material including armor, outside/debris exclusion and exact color restoration; simultaneous stop uses max; a disclosed direct stop probe observes actual frozen combat clock/physics, cap/budget; settings/reset. White still is a presentation override, not natural-damage timing evidence. |
| `v6tempo` | Seeded 24-wait replay, bounded nonconstant sampling and no adjacent upper-band waits; 26 scaled seconds per phase of actual scheduler choices, contacts, recovery opportunities, underground time and mean waits. Synthetic steady-P2 fixture and damage-disabled player are disclosed. |
| `v6roots` | Ordinary swords at root/body/mixed boundaries, both directions and whole poses; no synthetic body damage. |
| `v6whole` | Ground five-hit timing/IDs, walk/dash/wall evasion, airborne overlap, premature fourth-hit greed and real fifth-hit recovery counters. Synthetic initial P2; ordinary measured input. |
| `v6advance` / `v6core` | Locked bite/sweep direction/target, bounded root advance and warning geometry, ordinary counters/dash, close camping, bounded preparation/turning/walls. Initial pattern/position controlled. |
| `v6phase` | Real protected phase transition and exactly two rock windows; ordinary route during rocks; B/whole exclusions, queued late event after promised recovery, death cancels pending event. Initial threshold HP is synthetic. |
| `v6scenarios` | Center/front/rear/cross/flee/wall scheduler measurements. Player damage disabled to isolate cadence; not a difficulty test. |
| `v6natural` | Production scheduler with ordinary input until actual victory/defeat/cap. No forced patterns, HP injection/reset, teleport or modified immunity during combat. Bot reads current geometry and internal timed rock escape routes; not a human reaction or balance test. |
| `v6death` | Actual boss damage to HP0, dead input blocked, API retry resets HP5/40, mask15, phase1 and rock count. Physical R is separately checked. |
| `v6observe` | Passive physical-keyboard observer; preserves intro and rebinds after real R. No input submission, health mutation or teleport. External foreground-checked SendInput/client captures supply the keyboard evidence. |
| `armorvisual` | Fixed camera/root/idle pose comparisons at masks15/14/12/8/0 (4/3/2/1/0 plates), unchanged hooves/horns/trees, live+queued debris/reconfiguration reset. The one-plate image is an override of a transient production state. V6 uses the whole white material; historical localized colored-hit evidence applies to its original binary only. |

Native PNG writing is asynchronous: the V6 driver waits for requested files before asserting completion. Every final PNG is also checked externally for dimensions/hash. Selected stills are opened for agent visual review. A screenshot request alone is not an image or a visual judgment.

## Initial tuning and damage policies

| Item | V6 value |
| --- | --- |
| Once-sampled action wait | P1 minimum1.15 plus0..0.18s; P2 minimum0.32 plus0..0.12s, biased short. Following an upper-band draw, next draw is reduced. Tell, active and promised recovery are not randomized. |
| P2 movement / burrow | Reposition4.8 units/s; burrow cooldown6.8s. P1 retains the heavy approach. P2 A/B/C weights40/28/32%; at two consecutive A choices, choose B/C. These are initial tuning choices. |
| A | 0.62s branch tell after underground travel; one fast side contact; 0.26s resubmerge. Select the next behavior or briefly emerge into action wait. No long recovery is advertised then cancelled. |
| B | First1.80s warning is spent underground, with late low emergence. Immediately execute world L/R/L/R/delayed L5; separate contact IDs. Preserve1.05s final hesitation and1.90s recovery. |
| C | Fixed departure and landing separated by5.6 units where arena limits permit;4.2-unit parabola, flight0.98/0.82s in P1/P2, departure0.12s, landing, delayed wave,1.15s recovery. Departure, landing and both waves share one attack ID, so at most one accepted damage event per package. No constant airborne contact damage. |
| White flash / controls | Whole attached sprite pixels turn white through a shader; alpha is preserved and renderer colors restore exactly. Unscaled fade lasts about0.11s. F7 cycles100/off/35/65/100%; F6 retains shake control. Retry retains both settings. Root-invalid damage never requests body white/strong stop. |
| Hitstop / shake | Boss armored/skin stops0.028/0.038s; first four whole player hits0.030s, final0.045s. Same-frame requests use maximum; per-request cap0.055s, budget0.16s per unscaled second. Camera amplitude cap0.18 and duration0.22s; active rocks use a smaller amplitude. Combat physics/AI/action/immunity clocks remain scaled together. |

Reference images opened: `Reference/reference_bite_forward.png` and `Reference/reference_burrow_patterns.png`. Horizontal bite extension/retraction uses the attached head/body pose and existing limited root advance, separate from a vertical slam. Reference sources informed anticipation/impact/recovery, without copying numeric tuning: [Jotun / William Dube](https://www.gamedeveloper.com/design/giant-norse-elementals-the-80-20-rule), [Motion Twin / Dead Cells](https://80.lv/articles/interview-with-the-developers-of-dead-cells), [Mariel Cartwright / effective animation](https://media.gdcvault.com/gdcchina14/presentations/833784_MarielCartwright_PowerfulAndEffective_EN.pdf).

## Evidence and limits

Final selected candidate, run table, actual FPS, hashes, preservation audit and selected image links are generated in `../../../../QA/V6/verification.md`. Previous failed candidates/logs remain separate. Finite structured QA and agent visual review do not certify human fun/balance, subjective audio quality, long-duration performance or all possible input sequences. Unity shutdown ComputeBuffer/D3D12 cleanup messages are classified separately from play-time Error/Exception/Assert events and retained in original logs.

Tool verification: local AGENTS/.agents/skills inspected; no applicable Unity skill found. Actual MCP active-scene/console reads succeeded using the already installed bridge. Codex CLI0.160.0 binary/version/MCP listing was verified only; no CLI model/agent run, new installation or paid API was used. Source and build provenance are recorded in the final audit. Local commit only; no push.

## Final b05 result and armor comparisons

Same final b05: 24 regression runs at30/60FPS, 654checks; passive OS observer3checks and external physical audit16checks. Native Windows scan codes48actions confirmed HP5 death/R, both walls, root deflection, attack/jump/dash, F6/F7/pause/mute and retry settings. Natural180s timeout and two foreground failures remain disclosed. b03 failures were corrected by QA clock/input observation only; [diagnosis](../../../../QA/V6/qa-diagnosis-b05.json) proves gameplay source unchanged.

Fixed camera/root/pose comparisons; mask8 is a transient presentation override, not natural threshold evidence. Same horns/hooves/two trees/anatomy and live+queued debris/reset are asserted separately.

| Attached plates | Mask | Native30FPS | Native60FPS |
|---|---:|---|---|
| 4 | 15 | [PNG30](../../../../QA/V6/captures-armorvisual30-b05/03-armor-compare-4-mask15.png) | [PNG60](../../../../QA/V6/captures-armorvisual60-b05/03-armor-compare-4-mask15.png) |
| 3 | 14 | [PNG30](../../../../QA/V6/captures-armorvisual30-b05/04-armor-compare-3-mask14.png) | [PNG60](../../../../QA/V6/captures-armorvisual60-b05/04-armor-compare-3-mask14.png) |
| 2 | 12 | [PNG30](../../../../QA/V6/captures-armorvisual30-b05/05-armor-compare-2-mask12.png) | [PNG60](../../../../QA/V6/captures-armorvisual60-b05/05-armor-compare-2-mask12.png) |
| 1 | 8 | [PNG30](../../../../QA/V6/captures-armorvisual30-b05/06-armor-compare-1-mask8.png) | [PNG60](../../../../QA/V6/captures-armorvisual60-b05/06-armor-compare-1-mask8.png) |
| 0 | 0 | [PNG30](../../../../QA/V6/captures-armorvisual30-b05/07-armor-compare-0-mask0.png) | [PNG60](../../../../QA/V6/captures-armorvisual60-b05/07-armor-compare-0-mask0.png) |
