# PortKiller

Workspace Utilizer & Open Development Tools

## PortKiller Pet

바탕화면에서 같이 일하는 캐릭터. 윈도우 전용, 사내 배포용.

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
