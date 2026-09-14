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
- 트레이 아이콘: 상태 보기 / 수첩 / 항상 위 / 자동 실행 / 초기화 / 종료

아직 없는 것: 캐릭터 그림 (지금은 이모지)

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

캐릭터 이름과 그림은 직접 만들어 채운다. 다른 앱의 캐릭터·스프라이트는 가져오지 않는다.
