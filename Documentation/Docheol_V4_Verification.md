# 도철 V4 검증 기록

Unity6000.3.23f1 Windows x64 최종 후보 **b09**. 빌드 오류0·경고0, 선택된16실행의269검사 통과, 플레이 중 런타임 오류0. 실제 OS 키보드38행동과 별도10검사, 대시 색상1검사를 분리해 기록했다. 네이티브 PNG478장의 크기·CRC·SHA256을 확인했다.

- [전체 검증 및 갑주4→3→2→1→0 비교](../../QA/V4/verification.md)
- [실행·소스·캡처·실행 파일 감사](../../QA/V4/artifact-audit.json)
- [실제 입력 기록](../../QA/V4/physical-audit.json)
- [로컬 커밋 식별](../../QA/V4/commit.json)
- [Windows 실행 파일](../../Deliverables/Hwanjo_Docheol_V4/Hwanjo_Docheol_V4.exe)
- [조작 및 초기 패턴 수치](../README_Docheol_V4.md)

Assembly SHA256: `ccd96f89080839b3ca12c65ce9ab8a592f29ee2e8d9fcf1d0658c68752fe9c82`

자동 봇은 내부 낙석 경로를 읽으며 일부 단위 검사에는 제어된 초기 위치·패턴·HP가 사용된다. 실제 OS 입력 검사는 수동 사람 플레이가 아니다. 재미·적정 난이도·음향 취향 및 장시간 성능은 미검증이다. 2D 회전 중 얇은 실루엣과 높은 도약의 HUD 근접, 근사 경로 검사로 변하는 낙석 밀도를 알려진 한계로 남긴다. 이전 실패와 후보 실행 파일을 보존했다.
