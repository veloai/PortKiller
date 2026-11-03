# PortKiller

Workspace Utilizer & Open Development Tools

## PortKiller Pet

바탕화면에서 같이 일하는 캐릭터. 윈도우 전용, 사내 배포용.

### 지금 되는 것

- 알에서 부화 → 아기 → 소년기 → 성체, 진화 2단계 (집중 시간·출근 일수로 조건 달성)
- 돌아다니기: 걷기·점프·집어 옮기기. 펫이 없는 자리는 클릭이 그대로 통과
- 돌보기: 우클릭 메뉴(먹이·간식·놀기·청소·약·재우기) + 말풍선 반응
- 응아: 시간이 지나면 생기고 클릭하면 치워진다
- 수첩: 오늘 할 일 3개, 리마인더(한 번/매일/평일), 뽀모도로 25분 집중
- **장기 기억**: 출근 시각·집중·연속 출근을 날짜별로 기억(90일, 이 기기만)해 첫 활동 때 한마디, 집중 신기록 축하, 브리핑 카드 주간 요약. 펫 우클릭 "🧠 기억 지우기"
- **MCP 서버**: `127.0.0.1:47420/mcp` (열쇠: `%APPDATA%\PortKillerPet\mcp.key`). 에이전트가 `pet_say`·`pet_status`·`dev_status`·`pet_briefing` 로 펫을 부른다
- **개발 환경 상태**: Docker·컨테이너(hermes, alfa-backend, alfa-pg)·Hermes 브리지(47411)를 1분마다 확인해 브리핑 카드에 표시하고, 멈추거나 되살아나면 펫이 말한다. 목록은 `%APPDATA%\PortKillerPet\devstatus.json`
- **브리핑(상태 보기)**: 레서판다가 안경을 쓰고 일어서서, 옆 대시보드 카드를 지시봉으로 짚으며 설명한다. 마우스를 카드 위로 움직이면 지시봉이 따라가고 그 항목에 대한 한마디를 한다
- 트레이 아이콘: 상태 보기 / 수첩 / 항상 위 / 자동 실행 / 초기화 / 종료
- 아이콘: 트레이·exe 둘 다 PKP 글자 아이콘 (16~256px 여러 크기를 담은 .ico)
- **AI 물어보기**: 프롬프트를 적고 엔터를 누르면 답한다. Gemini / OpenAI 중 골라 쓴다
  - 열쇠는 설정 창에서 등록한다. 윈도우 DPAPI 로 **암호화**해서 내 계정에만 저장된다
  - 질문과 답은 저장하지 않는다. 나가는 통신은 이것 하나뿐이다
  - 자세히: [docs/verification/ai-ask.md](docs/verification/ai-ask.md)

캐릭터: **3D 도트(복셀) 동물 4종** - 레서판다·고양이·여우(새끼로 태어남, 네발) + 병아리(알에서 태어남, 두 발).
처음 실행하거나 "🧺 새 펫 맞이하기"를 누르면 종을 직접 고르는 창이 뜬다. 알·새끼 바구니·응아·동물 모두
코드로 조립한 큐브 모델이라 그림 파일이 필요 없다.

| 종 | 태어나는 방식 | 몸 틀 |
|---|---|---|
| 레서판다 / 고양이 / 여우 | 🧺 바구니에서 자는 새끼를 쓰다듬어 깨운다 | 네발 (`VoxelPanda`, `VoxelQuadrupeds`) |
| 병아리 | 🥚 알을 쓰다듬어 깬다 | 두 발 (`VoxelChick`) |

새 종은 `PetKinds`(Core/PetKind.cs)에 한 줄을 더하고 `VoxelAnimals` 에 모델을 연결하면 된다.
검사용: `PortKillerPet.exe --render-sheet 파일.png`(모든 종·자세를 한 장으로), `--render-picker 파일.png`(종 고르는 창).
예전 2D 다람쥐 그림(`assets/sprites`)은 더 이상 쓰이지 않는다(기록용으로 남김).

### 구조

| 프로젝트 | 역할 | OS 의존 |
|---|---|---|
| `src/PortKiller.Core` | 두뇌 — 스탯·성장·진화·세이브 규칙 | 없음 |
| `src/PortKiller.Platform` | 윈도우 — 활동 측정, 자동시작, 저장 | 윈도우 |
| `src/PortKiller.App` | 화면 — 투명 창, 펫 | 윈도우 (WPF) |
| `tests/PortKiller.Core.Tests` | 두뇌 검사 | 없음 |

`Core` 에는 윈도우 코드가 한 줄도 없다. 나중에 다른 OS 로 갈 때 그대로 들고 간다.

### 빌드

.NET 9 SDK 가 필요하다.

```
winget install Microsoft.DotNet.SDK.9
dotnet build PortKiller.sln
dotnet test
dotnet run --project src/PortKiller.App
```

### 보안·개인정보

무엇을 읽고 무엇을 읽지 않는지는 [docs/privacy-and-security.md](docs/privacy-and-security.md) 참고.
사내 보안 검토에 이 문서를 함께 제출한다.

### 리소스

캐릭터 그림은 직접 만든다. 다른 앱의 캐릭터·스프라이트는 가져오지 않는다.

```
GEMINI_API_KEY=... node tools/gen-sprites.mjs       # 그림 뽑기 (돈이 나간다)
powershell -File scripts/matte-batch.ps1            # 배경 벗겨 투명 PNG 로
powershell -File scripts/sprite-sheet.ps1           # 전부 한 장에 모아 눈으로 확인
powershell -File scripts/make-pkp-icon.ps1 -Out assets/icons/pkp.png             # 아이콘(PKP) 그림 한 장
powershell -File scripts/make-ico.ps1 -In assets/icons/pkp.png -Out assets/icons/app.ico
powershell -File scripts/make-ico.ps1 -In assets/icons/pkp.png -Out assets/icons/tray.ico -Sizes 16,20,24,32,40,48,64
```

열쇠는 저장소에 두지 않는다 - 실행할 때 환경변수로만 준다.
주문 문구·규격·확인 항목: [docs/assets/image-brief.md](docs/assets/image-brief.md)
