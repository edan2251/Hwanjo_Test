# 도철 보스전 완료 보고

Windows 실행파일: [Hwanjo_Docheol.exe](../Deliverables/Hwanjo_Docheol/Hwanjo_Docheol.exe)  
프로젝트: D:/2026_2_TeamProject-BossTest/BossTest_Unity  
Unity: 6000.3.23f1 · Windows x64 · Mono  
시작 씬: Assets/BossFight/Scenes/DocheolArena.unity  
브랜치: feature/docheol-boss-battle  
커밋: **39885ad334fae3bbacac8d33e991904abb4ecd8e**  
커밋 후 작업 트리 clean. 원격 push·공개 배포·추가 유료 API 결제는 하지 않았습니다. 지정 데스크탑 복제본만 사용했습니다.

## 구현한 전투

원본 Warrior 계층·스프라이트·Animator·이동·공격·점프·대시를 사용하며, 기존 배경을 가져오지 않은 바닥·벽·카메라의 최소 아레나입니다. 시작 화면, 일시정지, 승리, 사망, 재도전, 음소거, 창/전체 화면을 구현했습니다.

거대한 입과 이빨, 나무 양뿔, 두 앞발, 뒷다리 없는 뿌리 하체를 유지했습니다. 턱·구강·몸통·앞발 두 개·뿌리를 분리해 움직입니다. 핵심 DT05는 뿌리 확장 → 전신 상승 → 체공 → 전신 낙하 → 착지 회복입니다. 접지 그림자, 피격 정지, 제한된 화면 흔들림, 입자, 코드로 합성한 효과음을 포함합니다.

| 구현 패턴 | 예고 | 활성 | 회복 |
|---|---:|---:|---:|
| DT01 앞발 내려찍기 | 1.25초 | 0.22초 | 2.65초 |
| DT02 낮은 뿌리 쓸기 | 1.05초 | 0.66초 | 2.65초 |
| DT03 큰 물기 | 1.55초 | 0.22초 | 2.65초 |
| DT05 전신 낙하 | 1.90초 | 0.58초 | 3.00초 |

보스 HP 40, 플레이어 HP 8, 각 공격 피해 1. 플레이어 피격 무적 0.8초·경직 0.22초, 보스 연속 피격 제한 0.12초. 대시 속도 11·명목 지속 0.28초·시작 기준 쿨다운 0.4초이며 대시 무적은 없습니다. 실제 대시 측정은 50Hz 물리 스텝 기준 약 0.30초/3.23 유닛입니다. 타이머 경계의 한 물리 스텝 오차를 포함합니다.

체력 절반 이하에서는 현재 회복 후 2.1초 전환을 거쳐 2페이즈로 진행합니다. DT01 뒤에 별도의 1.2초 예고를 가진 DT02가 연결됩니다. 예고 목표와 방향은 고정되고 회복 중 위치·방향도 고정됩니다. 같은 공격은 낙하와 착지를 포함해 중복 피해를 주지 않습니다. 벽에서 물기가 강제로 불가피해지지 않도록 자연 선택 시 전방 공간 조건이 있습니다.

이 수치와 2페이즈 연결은 이번 구현의 조정값이며, Notion 원안의 확정 수치로 주장하지 않습니다. DT04 흡입은 미포함입니다. 원소·폼 체인지·퀵 스왑은 클리어 필수가 아닙니다.

## 실제 실행 검증

검증한 최종 Windows 빌드는 MCP job **build-d6b0f37c57**이며 2026-10-02 22:04:30 UTC에 성공했습니다. 빌드 오류 0, 경고 0. 약 111.5 MiB, 실행 폴더 186개 파일입니다.

| 최종 실행 보고서 | 통과 검사 | 측정 평균 FPS | 결과 |
|---|---:|---:|---|
| contracts30-final.json | 22/22 | 27.26 | 입력·점프·대시·피격·정지·재도전 |
| contracts60-final.json | 22/22 | 53.85 | 동일 계약 검사 |
| patterns30-final.json | 30/30 | 29.22 | 네 공격·중복 피해·양쪽 벽 회피 |
| patterns60-final.json | 30/30 | 58.30 | 동일 패턴 검사 |
| victory30-final.json | 7/7 | 29.62 | 자연 승리 및 실제 재도전 |
| victory60-final.json | 7/7 | 59.06 | 자연 승리 및 실제 재도전 |
| defeat60-final.json | 6/6 | 58.87 | 자연 사망 및 실제 재도전 |

**7회 실행, 124개 검사 전부 통과, 검사 중 런타임 오류 0.** FPS는 목표 설정과 별도로 실제 측정했으며, 평균에는 초기화·캡처·씬 재로딩 구간도 포함됩니다. 고정 30/60FPS를 보장하는 수치로 해석하지 않습니다.

30FPS 목표의 실제 전투는 63.88초에 남은 HP 5로 승리했습니다. 60FPS 목표에서는 58.32초에 HP 7로 승리했습니다. 체력·피해·패턴을 조작하지 않았습니다. 자연 사망은 50.73초, 적 공격 8회로 확인했습니다. 승리와 사망 모두 실제 Session.Retry 호출 후 새 플레이어 인스턴스, HP 8/보스 HP 40, 시간 배율 1, 초기 공격 상태를 확인했습니다.

계약 모드는 피격 API 계약 확인을 위해 합성 피해를 사용합니다. 패턴 모드는 각 동작을 강제로 실행하고 초기 배치·체력을 제어합니다. 각 보고서에 이를 구분했습니다. 양쪽 벽 회피 구간 중에는 위치·체력 변경이나 피해 주입 없이 걷기 또는 점프만 사용했습니다.

[release-summary.json](release-summary.json)에 집계와 각 보고서 해시, 83개 최종 캡처의 존재·크기·비검은화면 검사 결과를 보존했습니다. 83장 전체를 사람이 수동 검수한 것은 아닙니다. 주요 장면은 실제 이미지로 확인했고 별도 시각/전투 검수 에이전트가 도철 정체성, 관절 연결, 위험 표식 크기와 화면 구도를 독립 검토했습니다.

대표 최종 캡처:
- [DT05 전신 상승](captures-victory60-final/05-DT05-apex.png)
- [30FPS 승리](captures-victory30-final/16-victory.png)
- [자연 사망](captures-defeat60-final/12-natural-defeat.png)
- [재도전 복원](captures-defeat60-final/13-natural_defeat_retried.png)

## 실제 Unity MCP 사용 근거

설치 확인만으로 사용 성공이라 판단하지 않았습니다. 연결된 인스턴스 **BossTest_Unity@a625504df3266a23**에서 다음 호출을 실행했습니다.

- editor/state와 project/info: 지정 프로젝트, Unity 버전, 컴파일·재생 상태 확인.
- execute_menu_item: Build Docheol Arena로 실제 씬 생성. 콘솔의 Scene built with original warrior 메시지와 get_active의 DocheolArena 경로·buildIndex 0 확인.
- manage_scene/get_hierarchy: 원본 플레이어와 보스 구성 조회.
- manage_editor/play·stop 및 manage_camera/screenshot: 실제 실행과 화면 검수.
- manage_build/build·status: Windows 실행파일 빌드와 성공 결과 조회.
- read_console: 컴파일·런타임 진단 확인.
- Playtest 메뉴: 화면을 관찰하고 이동·공격·대시 입력을 직접 선택. DT01 예고에서 대시로 피한 뒤 회복 중 두 번 공격해 보스 HP 39→37, 플레이어 HP 8 유지를 확인.

근거: [mcp-live-evidence.json](mcp-live-evidence.json), [mcp-final-state.json](mcp-final-state.json), [mcp-directed-playtest.json](mcp-directed-playtest.json), [build-final-mcp.json](build-final-mcp.json), unity-editor-mcp2.log. Playtest는 입력 사이에 에디터를 일시정지하는 에이전트 직접 검수이며, 실제 키보드 수동 플레이나 인간 반응 시간 검증은 아닙니다.

MCP v10.0.0은 로컬 127.0.0.1:8080에서 동작하며 telemetry 차단, LAN·비보안 원격 연결 비허용 상태를 확인했습니다. 게임 실행에는 MCP·Python·uv가 필요하지 않습니다.

## 발견 후 수정한 사항

피격 순간 대시 관성이 넉백을 상쇄하던 문제, 연속 공격 애니메이션 재시작, 재도전 음소거 유지, 낮은 쓸기의 실제 범위보다 좁던 표식, 벽 바깥 목표로 가려던 검증 봇을 수정했습니다. 큰 디버그 사각형을 바닥 경계와 뿌리 파동으로 바꿨고, 구강 수평 이음선·가려진 앞발·얇은 뿌리·텍스처 혼선도 실제 캡처를 보며 보정했습니다.

초기 숨김 게임 창에서는 캡처가 실패하거나 검게 나왔습니다. 해당 결과를 시각 통과로 취급하지 않고 보이는 실제 게임 창에서 다시 검증했습니다. 초기 30FPS 대시 거리 검사 실패는 외부 방향/버튼 적용 시점과 요청 직후 측정의 문제였습니다. 외부 입력을 같은 Update에서 함께 적용하고, 수락된 대시의 실제 Rigidbody2D 물리 이동을 측정하도록 보강했습니다. 거리 허용 범위는 완화하지 않았습니다. 이전 실패 로그도 보존했습니다.

## 남은 한계와 알려진 사항

진행을 막는 기능 차단은 없습니다. 다만 물기 순간 입 오른쪽의 작은 붉은 연결 띠, 공중에서 몸통 아래쪽의 평평한 crop 경계가 일부 보입니다. 작은 보조 글씨는 주 정보보다 낮은 대비를 유지합니다. 손으로 그린 전체 프레임 애니메이션 수준의 연결 보정은 별도 미술 작업 여지입니다.

사람의 실제 키보드 플레이·오디오 청취 검수, 다른 PC/그래픽카드·모니터·Windows 버전, 장시간 반복 플레이는 이번 검증 범위 밖입니다. 현재 확인한 장치는 NVIDIA RTX 3070과 D3D12입니다. Steam 배포나 상업 성과 검증은 수행하지 않았습니다.

Unity Editor 도메인 재로딩 시 EditorTextSettings의 “Deleting invalid font reference” 진단, 실행 종료 시 URP/D3D12의 ComputeBuffer/4096-byte 자원 정리 진단이 원본 로그에 남습니다. 실행 중 C# 예외나 검사 실패로 이어지지는 않았지만 엔진 내부 원인까지 분리하지는 않았습니다.

일부 강제 패턴 검사에서 recovery라는 파일명으로 예약한 비동기 캡처가 다음 패턴 예고를 담을 수 있습니다. 상태 검증은 JSON의 실제 검사값, 시각 자료는 위 자연 전투 캡처를 우선합니다.

## 전달·재현·보존

실행 폴더의 파일들을 함께 보관하세요. 조작은 **A/D·방향키 이동, Space 점프, J 공격, Shift/K 대시, Enter 시작, Esc 일시정지, R 재도전, M 음소거, F11 창/전체 화면**입니다.

[release-manifest.json](release-manifest.json)에 실행 폴더 모든 파일의 SHA-256을 기록했습니다.

- EXE: 82EFD5A8C297A5046F6BB72C240DAB8972E64003851935EBA0A46AD121CE0305
- Assembly-CSharp.dll: 10CA73DF1A3AA15644CFD8B9DF4A51DEB1BB802212FBE84B30CE543A09FA5F07

최종 검사 뒤 두 C# 파일 끝의 빈 줄만 정리해 커밋했고, 검증한 실행파일과 어셈블리는 교체하지 않았습니다. 원본 복구점은 ../RestorePoints/BeforeDocheol_20261003(416파일 SHA-256 검증), 기준 커밋은 bac23a7affcf081f49777906d09b5c4e43ba6fd2입니다. 디버그 산출물은 삭제하지 않고 QA/BuildDebugInformationFinal에 보존했습니다.

추가 구현 문서: [DocheolEncounter.md](../BossTest_Unity/Docs/DocheolEncounter.md). 아트 원본은 ../Reference, 최종 아틀라스는 프로젝트 Assets/BossFight/Art에 보존했습니다.
