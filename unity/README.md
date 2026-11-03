# 렌즈 스트라이크 AR (Unity)

두 폰이 **AR 좌표를 공유**해서 대전하는 렌즈 스트라이크 본편입니다. 웹 버전이 "화면에 보이는 사람"을 맞히는 것과 달리, 이 버전은 **상대 폰의 실제 위치·방향**으로 상대의 몸(머리·몸통·다리)을 세우고 내 조준선이 거기에 닿는지로 판정합니다.

- 지나가던 사람을 맞혀도 판정되지 않습니다. 상대가 등 뒤에 있으면 화면 가장자리 화살표로 방향을 알려 줍니다.
- 파이어볼·워터볼은 AR 공간을 실제로 날아가므로, **몸을 움직여 피할 수 있습니다.** 상대가 쏜 마법탄이 내 쪽으로 날아오는 모습도 보입니다.
- 세로·가로 화면을 모두 지원합니다.

## 대전 방법

1. 두 사람 모두 앱을 열고 닉네임과 캐릭터를 고릅니다.
2. 한 명이 **방 만들기** → 화면의 참가 코드를 다른 사람이 입력해 **참가**합니다.
3. **좌표 맞추기** (한 번만 하면 됩니다)
   1. 방장이 기준 자리(바닥 타일 한 칸 등)에 서서, **멀리 있는 물체**(창틀·간판 등)를 조준점 가운데 두고 **"여기가 기준"**을 누릅니다.
   2. 방장이 비켜 주면, 참가자가 **같은 자리**에 서서 **같은 물체**를 겨누고 **"여기가 기준"**을 누릅니다.
   3. 서로의 머리 위에 이름이 제자리에 보이면 성공입니다.
4. 서로 떨어진 뒤 방장이 **시작**을 누릅니다. 먼저 5킬하면 승리합니다.

**혼자 연습**은 내 앞 3m에 훈련 표적을 세웁니다. ◎ 버튼을 누르면 지금 보는 방향 3m 앞으로 다시 세웁니다.

## 요구 사항

- ARCore를 지원하는 안드로이드 8.0 이상 폰([지원 기기 목록](https://developers.google.com/ar/devices)). 대전에는 두 대가 필요합니다.
- 인터넷 연결: 두 폰은 Unity Relay로 연결되므로 서로 다른 네트워크(LTE·와이파이)여도 됩니다.
- iOS(ARKit)는 코드상 지원하도록 구성했지만, 빌드에는 Mac이 필요해 아직 배포하지 않습니다.

## 개발

- Unity **6.3 LTS (6000.3.27f2)**, Android Build Support
- 프로젝트 구성(씬·머티리얼·플레이어·XR 설정)은 모두 [`Assets/LensStrike/Editor/LensStrikeBuilder.cs`](Assets/LensStrike/Editor/LensStrikeBuilder.cs)가 코드로 만듭니다. 씬 파일을 손으로 고치지 말고 이 스크립트를 고치세요.

```bash
# 구성 + APK 빌드 (Builds/Android/LensStrikeAR.apk)
"D:/Unity/Editors/6000.3.27f2/Editor/Unity.exe" -batchmode -nographics -projectPath unity -executeMethod LensStrike.EditorTools.LensStrikeBuilder.SetupAndBuildAndroid -logFile -
```

```bash
# 경기 규칙·판정 단위 테스트 (Unity 없이 dotnet으로)
dotnet test unity/CoreTests
```

### 구조

```
Assets/LensStrike/
  Core/      순수 C#(Unity 의존 없음): 무기 수치, 경기 규칙, 방아쇠, AR 명중 판정, 좌표 맞추기
  App/       Unity 런타임: 화면(uGUI를 코드로 생성), 게임 진행, Relay 연결, 효과음·진동
  Editor/    프로젝트 구성·빌드 스크립트
CoreTests/   Core를 검증하는 xUnit 테스트
```

## 개인정보

카메라 영상은 이 기기 안에서 AR 위치 추적에만 쓰고 저장·전송하지 않습니다. 상대 기기로는 내 폰의 위치·방향(기준점 기준 좌표)과 닉네임·캐릭터·HP 같은 게임 정보만 보냅니다. 연결에는 Unity Relay(익명 로그인)를 씁니다.
