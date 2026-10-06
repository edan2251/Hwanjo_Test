# 도철 V6 최종 검증 — b05

지정 데스크탑 `D:\2026_2_TeamProject-BossTest\BossTest_Unity`, Unity6000.3.23f1. 최종 Windows 빌드 **b05**, 빌드 오류0/경고0. V6 전체 개편을 같은 바이너리에서 검수했다. 30/60FPS 회귀24개 실행 **654검사**, 수동 변경 없는 실제 입력 관찰3검사, 별도 물리 입력 감사16검사 통과. 검사 수를 과거 후보와 합산하지 않았다. Git 소스 커밋 식별자는 봉인 단계에서 이 문서와 VERSION.json에 기록한다.

## 구현 및 피해 정책

P1의 묵직한 C, P2의 A 빠른 한쪽 타격→즉시 재잠복(초기 연속2 상한), B 지하 첫1.8초 예고→낮은 등장→L/R/L/R/지연L5→1.9초 확정 회복, C 별도 출발/착지 예고→출발 짧은 타격→전방 포물선→착지→지연 지면 파동→회복을 포함한다. C 출발·착지·양쪽 파동은 **같은 공격 ID**, 최대1회 수용 피해다. 공중 전체 접촉 피해는 없고 목적지는 tell start에 고정한다. 지상5연타·뿌리 무효타·낙석 정확히2회·낙석 중 땅굴/전신 공격 제외를 유지했다.

정면 물기 머리/상체 수평 확장·수축, root 전진.28/앞발.22 제한, P2 재배치4.8, 행동 결정마다 한 번만 샘플링하는 좁은 가변 대기(P1 최소1.15/P2.32, seed 재현), HP5 초기화/HUD/재시작을 구현했다. 예고·active·약속 회복 시간은 무작위로 줄이지 않았다. 실제 타격에 부착 몸 전체 white shader·소리·접촉 입자·짧은 히트스톱을 동기화하며, 배경/플레이어/탈락 갑주에는 white 값이 전달되지 않는다. max-not-add, 요청.055초 cap/초당.16 budget, 같은 전투 시계 정지, 흔들림.18/.22 상한, F6/F7 설정과 R 유지가 검수 대상이다.

## 동일 최종 후보 회귀

| 실행 | 검사 | 평균 FPS | 실제초 | PNG | 결과 |
|---|---:|---:|---:|---:|---|
| [whole30-b05](whole30-b05.json) | 53 | 29.54 | 65.74 | 13 | PASS |
| [whole60-b05](whole60-b05.json) | 53 | 59.03 | 65.41 | 13 | PASS |
| [impact30-b05](impact30-b05.json) | 16 | 25.02 | 5.76 | 7 | PASS |
| [impact60-b05](impact60-b05.json) | 16 | 41.61 | 5.98 | 7 | PASS |
| [burrow30-b05](burrow30-b05.json) | 76 | 29.44 | 59.85 | 26 | PASS |
| [burrow60-b05](burrow60-b05.json) | 76 | 58.87 | 58.70 | 26 | PASS |
| [tempo30-b05](tempo30-b05.json) | 6 | 29.44 | 53.51 | 2 | PASS |
| [tempo60-b05](tempo60-b05.json) | 6 | 57.52 | 53.25 | 2 | PASS |
| [advance30-b05](advance30-b05.json) | 50 | 27.52 | 20.89 | 3 | PASS |
| [advance60-b05](advance60-b05.json) | 50 | 56.92 | 19.91 | 3 | PASS |
| [core30-b05](core30-b05.json) | 31 | 28.83 | 25.53 | 30 | PASS |
| [core60-b05](core60-b05.json) | 31 | 55.71 | 25.42 | 30 | PASS |
| [roots30-b05](roots30-b05.json) | 20 | 28.55 | 22.56 | 22 | PASS |
| [roots60-b05](roots60-b05.json) | 20 | 57.17 | 20.81 | 22 | PASS |
| [phase30-b05](phase30-b05.json) | 16 | 29.01 | 33.64 | 20 | PASS |
| [phase60-b05](phase60-b05.json) | 16 | 56.52 | 33.76 | 21 | PASS |
| [scenarios30-b05](scenarios30-b05.json) | 34 | 29.48 | 101.13 | 24 | PASS |
| [scenarios60-b05](scenarios60-b05.json) | 34 | 58.75 | 101.03 | 24 | PASS |
| [death30-b05](death30-b05.json) | 5 | 28.47 | 28.62 | 20 | PASS |
| [death60-b05](death60-b05.json) | 5 | 56.34 | 28.49 | 20 | PASS |
| [natural30-b05-extended](natural30-b05-extended.json) | 4 | 28.67 | 205.35 | 20 | PASS |
| [natural60-b05-extended](natural60-b05-extended.json) | 4 | 56.48 | 184.48 | 20 | PASS |
| [armorvisual30-b05](armorvisual30-b05.json) | 16 | 11.51 | 2.78 | 11 | PASS |
| [armorvisual60-b05](armorvisual60-b05.json) | 16 | 12.86 | 2.96 | 11 | PASS |
| [keyboard-b05-input3](keyboard-b05-input3.json) | 3 | 57.19 | 122.01 | 4 | PASS |

모든 실행의 Assembly/level0/sharedassets SHA256이 아래 최종 파일과 일치한다. 요청30/60은 cap이며 평균 FPS는 해당 검사 구간의 실제 측정이다. 특히 짧은 presentation 검사에는 초기화 비용이 포함되므로 지속 성능 증명으로 읽지 않는다. 최초 자연30/180초는 보스HP6에서 승리 항목이 실패했고 [원 결과](natural30-b05.json)를 보존했다. 새270초 상한으로 **게임 바이너리/봇 규칙을 변경하지 않고** 자연30 205.35초, 자연60 184.48초 실행에서 실제 승리와 피격0을 확인했다. 각 JSON의 실제 시간을 기준으로 한다.

자연 모드는 강제 패턴/합성HP/teleport/변경 무적 없이 production scheduler를 사용하지만 봇이 현재 geometry와 내부 낙석 경로 API를 읽는다. 통제 검사는 JSON에 초기 phase/position/branch·합성 피해·presentation override·피해 비활성 fixture 여부를 밝힌다. 가까운 템포26초 P2 표본에는 자연 땅굴 선택0회가 있으므로 A/B/C 자연 빈도 증명으로 사용하지 않는다.

## b03 30FPS 두 실패와 기준 유지

[상태·시간 및 소스 비교](qa-diagnosis-b05.json): b03→b05 gameplay C#15개는 내용이 동일하다. 게임·이동·무적·피해·시계를 수정한 해결이 아니다.

벽5연타는 QA가 첫 렌더 회복 관찰+명목1.9초까지 기다리면서 FixedUpdate 경계를 놓쳐 다음 `whole_tell` 공격의 root/facing을 검사한 문제였다. b05는 실제 EncounterSeconds/RecoveryRemaining으로 고정 회복 구간을 계산한다. 벽 왼쪽 시작4.839996/종료6.739994/elapsed1.899998, 상태Recovery/root=-6.3을 유지했고 최소1.90초(부동소수 허용.002) 검사도 추가했다.

공중J는 합성 phase 전환 타격 히트스톱(TimeScale0/remaining.004668713) 중 첫 J가 실제 입력 가드에서 거부된 문제였다. grounded=False여서 과거 땅 판정 추측과 구별했다. 실제 히트스톱 종료 후 첫J를 주며 강제 시계/ground/action 상태를 쓰지 않는다. 30FPS 실제 JumpAttack14프레임, 상승 없음, 실제 바닥 착지. 30/60 각각53검사 통과. 정상 회피0피격, 공중캠핑 불가, 5개 서로 다른 ID/좌우 순서/4빠른+지연5, 1.9초 확정 회복/실제 검2회 이상 반격을 유지했다. b04의 중간 실패도 보존했다.

## 실제 Windows 입력

[keyboard-b05-input3](keyboard-b05-input3.json), [16항목 감사](physical-audit.json). 실제 scan-code48동작이며 전경 창/PID/path를 검사했고 SubmitInput/강제 패턴/HP mutation/teleport/API retry를 사용하지 않았다. 양쪽 벽 실제x=-9.65..9.65, 뿌리 무효타 count1/bossHP40, 일반 공격 bossHP39, 자연 HP0 Defeat t38.58, 실제 R 후 t40.45에 HP5/보스40/원위치-5.8/phase1. F7 1→0→.35와 F6 .65→1→0 및 재시작 유지, Escape pause/resume, M toggle을 확인했다. 사망 화면에 D/Space/J/Shift 입력 후 위치가 같음을 paired PNG로 보완했다.

앞선 `keyboard-b05` 전경 창 실패(키0회)와 `keyboard-b05-input2` 도중 전경 변경(9동작 후 중단)을 유지한다. 성공 실행은 대상 창 복구 후 각 key-down 직전 전경 검사에서만 키를 보낸다. 자동화된 OS 입력이며 사람이 직접 플레이했다는 주장이 아니다. F11 전환은 이 입력 시퀀스에서 시험하지 않았다.

## 갑주 비교 및 실제 이미지 확인

고정 camera/root/idle pose에서 갑주만4/3/2/1/0으로 바꾼 native PNG다. 뿔/발굽/두 나무와 몸체 transform/sprite가 같음을 각각 검사했다. 1개 mask8은 production transition의 짧은 상태를 보기 위한 presentation override다. 자연 HP 임계값/탈락 순서/보호 phase transition은 별도 phase 검사가 담당한다. live+queued debris, 피격·사망·턴/공격 geometry transient, old rig 즉시 숨김과 복구를 검사했다.

| 갑주 개수 | mask | 실제 PNG30 |
|---|---:|---|
| 4 | 15 | [armor-compare-4-mask15](captures-armorvisual30-b05/03-armor-compare-4-mask15.png) |
| 3 | 14 | [armor-compare-3-mask14](captures-armorvisual30-b05/04-armor-compare-3-mask14.png) |
| 2 | 12 | [armor-compare-2-mask12](captures-armorvisual30-b05/05-armor-compare-2-mask12.png) |
| 1 | 8 | [armor-compare-1-mask8](captures-armorvisual30-b05/06-armor-compare-1-mask8.png) |
| 0 | 0 | [armor-compare-0-mask0](captures-armorvisual30-b05/07-armor-compare-0-mask0.png) |

최종 선택 PNG 425개를 외부에서 CRC/IDAT 복호/해상도/SHA256으로 검사했다. 실제로 열어본 같은 b05 still은 27개이며 [개별 기록](visual-review-b05.json)에 있다. 이미지 한 장으로 시간/ID/피해 횟수를 주장하지 않고 JSON 시계 검사를 함께 사용한다.

- [03-root--1-idle-body-contact.png](captures-impact30-b05/03-root--1-idle-body-contact.png): Actual ordinary sword contact: attached whole body visibly flashes white; player/background retain colors. Timing/accepted event is certified by JSON rather than still.
- [05-whole-attached-white-flash.png](captures-impact30-b05/05-whole-attached-white-flash.png): Disclosed manual full-white shader setting: attached body including remaining armor is white; detached forehead plate stays brown; player/background unchanged.
- [06-white-flash-restored.png](captures-impact30-b05/06-white-flash-restored.png): Attached original colors restored; detached plate remains brown. Exact renderer/MPB restoration separately asserted by QA.
- [01-bite-weight-before.png](captures-advance30-b05/01-bite-weight-before.png): Front warning and prepared horizontal bite posture visible.
- [02-bite-weight-strike.png](captures-advance30-b05/02-bite-weight-strike.png): Forward head/body extension and jaw closure; not a vertical slam. Root movement limit is checked in JSON.
- [09-P2-ArcLeap--2.2-arc_warning.png](captures-burrow30-b05/09-P2-ArcLeap--2.2-arc_warning.png): Underground C: separate cyan departure and landing ground markers.
- [11-P2-ArcLeap--2.2-flight.png](captures-burrow30-b05/11-P2-ArcLeap--2.2-flight.png): High forward arc, tucked lower body, player remains grounded outside landing marker. Full trajectory/damage policy asserted by JSON.
- [12-P2-ArcLeap--2.2-land.png](captures-burrow30-b05/12-P2-ArcLeap--2.2-land.png): Landing at the disclosed marked destination; player outside contact.
- [17-P2-QuickSlam--2.2-quick_slam_hit.png](captures-burrow30-b05/17-P2-QuickSlam--2.2-quick_slam_hit.png): A low one-sided slam contact and ground trace; ordinary evasion player outside. This is the active still, not the first warning frame.
- [22-P2-FiveBite--2.2-burrow_five_tell.png](captures-burrow30-b05/22-P2-FiveBite--2.2-burrow_five_tell.png): B first warning remains underground with both amber side bounds visible; ordinary movement leaving union.
- [03-armor-compare-4-mask15.png](captures-armorvisual30-b05/03-armor-compare-4-mask15.png): Fixed pose/camera: 4 armor plates, mask15; underlying anatomy/horns/hooves/two trees remain. Presentation override, not a natural HP-threshold timing certificate.
- [04-armor-compare-3-mask14.png](captures-armorvisual30-b05/04-armor-compare-3-mask14.png): Fixed pose/camera: 3 armor plates, mask14; underlying anatomy/horns/hooves/two trees remain. Presentation override, not a natural HP-threshold timing certificate.
- [05-armor-compare-2-mask12.png](captures-armorvisual30-b05/05-armor-compare-2-mask12.png): Fixed pose/camera: 2 armor plates, mask12; underlying anatomy/horns/hooves/two trees remain. Presentation override, not a natural HP-threshold timing certificate.
- [06-armor-compare-1-mask8.png](captures-armorvisual30-b05/06-armor-compare-1-mask8.png): Fixed pose/camera: 1 armor plates, mask8; underlying anatomy/horns/hooves/two trees remain. Presentation override, not a natural HP-threshold timing certificate.
- [07-armor-compare-0-mask0.png](captures-armorvisual30-b05/07-armor-compare-0-mask0.png): Fixed pose/camera: 0 armor plates, mask0; underlying anatomy/horns/hooves/two trees remain. Presentation override, not a natural HP-threshold timing certificate.
- [03-five-long-tell.png](captures-whole30-b05/03-five-long-tell.png): Ground-five first warning; initial synthetic P2 damage flash still visible in this controlled fixture.
- [04-five-final-reprepare.png](captures-whole30-b05/04-five-final-reprepare.png): Distinct raised posture and last-left hesitation warning.
- [05-five-last-left.png](captures-whole30-b05/05-five-last-left.png): Final left contact posture, player outside range.
- [06-five-guaranteed-recovery.png](captures-whole30-b05/06-five-guaranteed-recovery.png): Low sustained recovery posture after fifth. Full1.9-second fixed-clock boundary separately asserted by QA.
- [00-root-deflection.png](keyboard-b05-input3/00-root-deflection.png): Actual keyboard sword at roots: root-invalid HUD confirmation, boss bar unchanged; report proves root count increase at bossHP40.
- [04-jump.png](keyboard-b05-input3/04-jump.png): Space accepted: player visibly airborne.
- [05-dash.png](keyboard-b05-input3/05-dash.png): Shift accepted: dash trail and forward displacement.
- [12-flash-off.png](keyboard-b05-input3/12-flash-off.png): Real F7 while paused: HUD/button flash0%.
- [13-flash-reduced.png](keyboard-b05-input3/13-flash-reduced.png): Second real F7: HUD/button flash35%.
- [15-defeat.png](keyboard-b05-input3/15-defeat.png): Real HP0 defeat, shake0/flash35 HUD.
- [16-dead-input-blocked.png](keyboard-b05-input3/16-dead-input-blocked.png): After actual D/Space/J/Shift dead-input attempt: player location matches defeat still.
- [17-retry.png](keyboard-b05-input3/17-retry.png): Actual R: fresh HP5 and four armor plates; flash35/shake0 preserved.

## 도구·원본·소스 증거

로컬 AGENTS/.agents/.codex 및 관련 skills 목록을 확인했고 적용할 Unity 전용 skill은 없었다. 새 설치는 필요하지 않았다. 기존 Unity MCP10.0.0 패키지와 서버로 최종 `DocheolArenaV6.unity` load/get_active 및 read_console을 **실제 호출**했다([MCP 결과](mcp-read-v6-final-evidence.json)); 캐시 준비 상태만으로 성공을 판단하지 않았다. 필터 조회는 Error0, Warning1: 기존 MCP WebSocket 시작 연결 경고이며 원문/스택을 보존했다. active V6 load/get_active는 성공했고 isDirty=false였다. 본 작업의 숨김 batch probe Editor만 PID/path/명령줄 확인 후 종료했다. [CLI 실제 실행](cli-evidence-final.json)은0.160.0 버전과 mcp list이며 모델/에이전트 실행은 하지 않았다. 유료 API/새 설치/3D/다른 노트북 사용 없음.

V4 전체189파일, V5 전체192파일은 최초 SHA256과 모두 동일하다. 원래 CourseContent 이동/점프/대시/공격/Health 스크립트는 V5 기준으로 변경하지 않았다. [artifact-audit](artifact-audit.json)은 build input raw SHA256, 실행별 report hash, 모든 PNG, 원본 보존, 소스 동일성에 대한 근거를 포함한다. 새 로컬 커밋만 만들며 push하지 않는다.

Assembly SHA256 `3b50c9416624bc48dc0f060b62730afaedea00e13f1ce354815167e325fc8f46`

level0 SHA256 `2eb7b717dfba082d860a98e2f11500b2014e410b6fa74837d05c6c3011b88051`

sharedassets SHA256 `ee92c975dacade0157887d97faf1f13956384b7454dea11a2fdd94da4affe6e2`

## 남는 한계

유한 자동 입력·에이전트 이미지 검수다. 사람의 재미/밸런스·음향 취향·장시간 성능이나 모든 조합을 보증하지 않는다. Unity 종료 시 ComputeBuffer/D3D12 cleanup 메시지는 원 로그에 보존했고 플레이 중 Error/Exception/Assert0과 구별했다. 새 기능이나 임의의 면역으로 해당 메시지를 숨기지 않았다.

최종 소스 커밋 `24bad62320e13f5dba2afd58e49c4c53f0957793` (`feature/docheol-burrow-impact-v6`), 작업 트리 clean, push 없음. 테스트한155개 input의 raw SHA256은 변경되지 않았고 각 Git blob과 내용이 일치한다. CRLF/LF만 다른 파일은 artifact-audit에 별도 표시했다. 전체 V6 납품 파일 manifest는 `../../Deliverables/Hwanjo_Docheol_V6/SHA256SUMS.txt`다.
