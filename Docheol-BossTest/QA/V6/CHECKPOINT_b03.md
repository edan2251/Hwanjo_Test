# V6 중간 체크포인트 — 2026-10-05T13:08:10.968415+00:00

부모 요청에 따라 안전한 실행 경계에서 저장한 **미완료 중간 결과**다. 지정 데스크탑 프로젝트만 사용했다. 새 로컬 커밋과 push는 아직 없다.

## 최신 상태

- 최신 Windows 후보 **b03**, Unity6000.3.23f1, 빌드 성공 / 오류0 / 경고0. 경로 `Deliverables/Hwanjo_Docheol_V6/Hwanjo_Docheol_V6.exe`.
- b01과 b02 실행 폴더 전체는 `QA/V6/candidates/b01`, `b02`에 보존했다. 실패 JSON/로그/PNG도 유지했다.
- V5 전체 192개와 V4 전체 189개 파일의 SHA256을 기준값과 재대조해 모두 변경 없음.
- 현재 Git HEAD `158a4263d0e00241b290f177275d68473c53633e`, 브랜치 `feature/docheol-burrow-impact-v6`; V6 변경 미커밋.

## 실제 확인

참조 `reference_bite_forward.png`, `reference_burrow_patterns.png` 두 장을 실제 열람했다. Unity MCP는 기존 설치 브리지로 V5 장면의 get_active와 콘솔 읽기가 성공했다. **V6 장면 MCP 검수 완료를 의미하지 않는다.** CLI 실제 바이너리 버전0.160.0과 MCP 목록을 확인했으며 CLI 모델/에이전트 실행은 하지 않았다. 로컬 AGENTS/.agents/skills와 관련 스킬 목록을 점검했으며 적용할 Unity 전용 스킬은 없었다. 새 설치·유료 API 추가 결제 없음.

## 구현

D 프로젝트에 A 빠른 한쪽 타격 후 즉시 재잠복(연속A 초기 제한2), B 땅속1.8초 예고 후 L/R/L/R/지연L5와1.9초 회복, C 고정 출발/착지·전진 포물선·착지·지연 파동·회복을 적용했다. 출발/착지/파동은 같은 ID로 패키지 최대1회 피해다. P1의 무거운 동작과 P2 이동4.8, 행동 대기 최소1.15/.32 및 좁은 단발 샘플 변동, HP5, 전진 물기 자세, 갑주 포함 전체 흰색 셰이더, 실제 타격 이벤​​트 기반 히트스톱/방향 흔들림, F7 플래시 설정을 구현했다. 기존 roots 무효 피해, 5연타, 두 낙석 이벤트와 회복 규칙을 유지했다.

기존 피격·초기화와 갑주4/3/2/1/0 비교 기능은 보존했다. V6 전용 전체 흰색 플래시와 재초기화 검수로 확장 중이다. `README_DRAFT.md`는 준비본이며 검수 완료 문서가 아니다.

## 실행 증거

| 실행 | 결과 | 검사 | 런타임 오류 | 실제 PNG 요청 |
| --- | --- | --- | --- | --- |
| burrow30-b02 | PASS | 76 | 0 | 26 |
| burrow60-b01 | FAIL | 76 | 0 | 26 |
| burrow60-b02 | PASS | 76 | 0 | 26 |
| impact30-b02 | PASS | 16 | 0 | 7 |
| impact60-b01 | PASS | 16 | 0 | 7 |
| impact60-b02 | PASS | 16 | 0 | 7 |
| tempo30-b02 | PASS | 6 | 0 | 2 |
| tempo60-b02 | PASS | 6 | 0 | 2 |
| whole30-b02 | FAIL | 42 | 0 | 13 |
| whole30-b03 | FAIL | 42 | 0 | 12 |
| whole60-b02 | PASS | 42 | 0 | 13 |

b02 땅굴30/60 각76개, 타격감30/60 각16개, 템포30/60 각6개, 지상5연타60의42개 검사는 통과했다. 템포 동일시드26초 표본: 평균 행동대기 P1=1.2219 / P2=.3614초, 타격관찰6/17, 반격 회복창6/7. 근거리P2 표본에서 땅굴은0회라서 새 분기 자연 출현의 증거로 쓰지 않는다. 모든 A/B/C 분기는 공개한 강제 초기 설정 후 보통 입력으로 검수했다. 수치는 지속 FPS/밸런스 인증이 아니다.

## 실패와 수정 준비

- `burrow60-b01`: P2-QuickSlam--2.2_ordinary_evasion — complete=True incoming=1
- `burrow60-b01`: P2-FiveBite--2.2_ordinary_evasion — complete=True incoming=2
- `burrow60-b01`: P2-FiveBite-8.2_ordinary_evasion — complete=True incoming=3
- `burrow60-b01`: native_capture_files — files=26
- `whole30-b02`: wall-left_five_world_left_right_contacts — sides=-1,1,-1,1,-1 times=1.974918,2.442848,2.946764,3.414871,4.696714 target/rootFixed=False
- `whole30-b03`: air_J_does_not_lift_or_sustain_top — airAttackFrames=0 upwardMovement=False landedOnActualFloor=True
- `whole30-b03`: wall-left_five_world_left_right_contacts — sides=-1,1,-1,1,-1 times=1.968967,2.435638,2.935642,3.402426,4.669729 target/rootFixed=False stage=whole_tell state=Telegraph facing=1/-1 root=-4.2/-6.3 side=-1 target=-6.42649 expected=-7.888157 locked=True

b01 빌드 직렬화 중 MaterialPropertyBlock 생성 예외는 b02에서 메인스레드 지연 생성으로 수정했고 빌드 오류0을 확인했다. A 예고의 전체 타격 궤적 포함, B 양쪽 예고/회피 인식, PNG 비동기 완료 대기를 보완해 b02 땅굴30/60이 통과했다.

b03 whole30 진단 실행은 종료코드2, 검사42개 중2개 실패, 런타임 오류0이었다. 왼쪽 벽 기록은 첫5타 후 새 `whole_tell`에서 root=-4.2(처음-6.3), facing=1(처음-1)을 비교했다. 기존 QA가 최초 **렌더 프레임**의 회복 시점부터 명목1.9초를 세어 실제 FixedUpdate 회복 종료를 놓치고 다음 공격을 관찰한 정황과 일치한다. 이를 실제 `RecoveryRemaining`으로 종료하게 고친 파일은 `pending-source/DocheolV5QaWhole.cs`와 C `v6/current`에만 저장했고 **D 및 b03에는 아직 적용하지 않았다**. 다음 빌드 재검증 전에는 해결 완료로 단정하지 않는다.

또한 b03 공중J 검사는 `airAttackFrames=0`으로 실패했다(부유 상승은 없고 실제 바닥 착지는 관찰). 초기 순간이동 후 오래된 grounded 표본이 남은 프레임에 J를 넣는 QA 조건이 원인일 수 있다. 실제 비접지 표본을 기다린 뒤 J를 제출하도록 한 준비 수정도 같은 C/`pending-source` 파일에 저장했다. 미적용·미검증 상태이며 공격이 정상이라고 단정하지 않는다.

b03에는 실제 분리 갑주 플래시 제외·실제 피격 무적 상태의 정지 검사와 최초 목표/위치 실패값 기록을 추가했다. 이 검사를 포함한 전체 행렬은 미실시다.

마지막 성공 실행은 **whole60-b02**, 검사42개 / 오류0 / 종료코드0이다. 최신 b03과 같은 후보의 검수 결과로 합산하지 않는다. 모든 V6 게임과 Editor 실행은 종료 경계에서 멈추며, 기존 MCP 서버는 대기 상태로 남겨 후속 연결에 사용할 수 있다.

## 후속 작업

진단 종료 결과 확인 → C의 준비된 회복 QA 수정 적용 및 필요한 전투 원인 수정 → 새 고유 후보 빌드 → 동일 최종 후보30/60 전체 회귀와 갑주 비교 실제 PNG 확인 → 실제 Windows 키보드 Enter/이동/점프/대시/공격/F6/F7/벽/사망/R 검수 → 문서·해시·V5/V4 보존 재확인 → 로컬 커밋(no push).

승인 사양 `APPROVED_SPEC.md`, 실제 b03 소스 `b03-source-snapshot`, 다음 수정 준비 `pending-source`, 실행 보고/원본 로그, 상세 해시 `checkpoint-evidence.json`을 저장했다. C `v6` 준비 스크립트는 그대로 있다. `bootstrap.py`, `prepare_controller.py`, `prepare_presentation.py`, `wire_qa.py`는 일회 적용 스크립트이므로 재실행하지 말고 현재 파일을 이어 수정한다. QA 종료 후 새 게임/Editor 검수를 시작하지 않는다. 이전 사용자 V5 게임은 건드리지 않는다.
