# 도철 보스 테스트

이 폴더는 환조의 **도철 보스 테스트** 한 건입니다. 다른 보스나 시스템 실험은 `Hwanjo_Test` 바로 아래에 별도 폴더로 추가하면 됩니다.

| 폴더 | 용도 |
| --- | --- |
| `BossTest_Unity/` | Unity Hub에서 여는 작업 프로젝트. `Assets`, `Packages`, `ProjectSettings`와 `.meta` 파일이 포함됩니다. |
| `Reference/` | 도철 이미지 참고자료와 Blender 원본 1개. |
| `Planning/V6/` | V6 기획표, 흐름도, 이미지. |
| `QA/` | 최종 보고서와 V6 확인 문서. 이전 빌드와 대량 캡처는 포함하지 않습니다. |

## 노트북에서 작업하기

1. 이 **비공개** 저장소를 노트북에 복제합니다.
2. Unity Hub에서 `Hwanjo_Test/Docheol-BossTest/BossTest_Unity`를 프로젝트로 추가하고 Unity **6000.3.23f1**로 엽니다.
3. 최초 실행 시 Unity가 `Library` 캐시와 패키지를 다시 만듭니다. `Packages/manifest.json`에 GitHub에서 받는 `com.coplaydev.unity-mcp` 패키지가 있어 인터넷과 Git이 필요합니다.
4. 변경사항은 이 저장소의 `Docheol-BossTest` 아래에서 관리합니다. 생성된 `Library`, `Temp`, `Logs`, `UserSettings`, 빌드 파일은 올리지 않습니다.

실행만 할 때는 [도철 V6 b05 릴리스](https://github.com/edan2251/Hwanjo_Test/releases/tag/docheol-v6-b05)의 Windows ZIP을 받아 **폴더 전체를 압축 해제**한 뒤 `Hwanjo_Docheol_V6.exe`를 실행합니다. 이 빌드는 원본 Unity 커밋 `24bad62320e13f5dba2afd58e49c4c53f0957793`에서 만들어졌습니다.

## 원본과 외부 자료

원본은 `D:\2026_2_TeamProject-BossTest`에 그대로 있습니다. Unity 프로젝트의 기존 9개 Git 커밋도 이 저장소 이력에 연결해 보존했습니다. 과거 문서에 적힌 `D:\` 경로는 당시 작업 PC의 위치이며 노트북 경로가 아닙니다.

플레이어 스프라이트는 [Clembod의 Warrior-Free Animation set](https://clembod.itch.io/warrior-free-animation-set), 트윈 기능은 [Demigiant의 DOTween](https://dotween.demigiant.com/license.php)을 사용합니다. TextMesh Pro 샘플에는 EmojiOne 표기 자료가 포함되어 있습니다. 외부 에셋의 사용권은 각각 따르며, **저장소를 비공개로 설정한 것이 원본 에셋의 자유로운 재배포 허가를 뜻하지는 않습니다.** 공개 전환이나 파일 외부 공유 전에는 라이선스를 다시 확인하세요.
