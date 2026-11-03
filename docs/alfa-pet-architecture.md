# ALFA 펫 — 흐름·관계·이어서 할 일

> 작성 2026-11-03 (Windows 사무실 PC). mac-air 에서 이어서 작업하기 위한 정본.
> 위키 허브: `Alfa/Obsidian/wiki/projects/portkiller-desktop-pet.md`

## 1. 한 줄 정의

**ALFA 펫 = ALFA 가 보조로 쓰는 바탕화면 비서 캐릭터.** 말로 시키면 서버·개발 환경을 보고, 관리하고, 보고한다.
앱 이름은 PortKiller Pet(PKP). 펫은 **얼굴과 입구**, ALFA 백엔드는 **머리와 규칙**이다.

## 2. 결정 사항 (사용자 확정)

| 결정 | 내용 | 날짜 |
|---|---|---|
| 머리는 ALFA 백엔드 | 펫의 명령 해석·정책·기록은 `alfa-backend`(127.0.0.1:7701)가 맡는다. **Hermes 는 ALFA 가 아니다**(별도 에이전트 컨테이너) | 2026-11-03 |
| 펫은 One Mind 에 넣지 않는다 | ALFA One Mind(여러 기기 = 논리적 한 ALFA)와 달리, **펫은 기기마다 따로** 둔다. 세이브·기억·상태는 기기별이고 동기화하지 않는다 | 2026-11-03 |
| 관리 설정은 ALFA 쪽 | "무엇을 볼지/할 수 있는지/누가 했는지"는 펫이 아니라 ALFA 의 정책 엔진·감사 원장에 둔다 | 2026-11-03 |
| 컴퓨터 사용은 샌드박스에서만 | 실제 업무 화면이 아니라 Docker 안의 격리 리눅스 데스크톱에서만(제안 단계, 미착수) | 2026-11-03 |
| 개인용 | 사내 배포가 아니라 본인 PC 개인용(NAC 자동 로그인 포함) | 2026-09-30 |

## 3. 관계도

```
                 ┌──────────────────── 이 PC 안 (127.0.0.1) ─────────────────────┐
  나 ──대화──▶  ALFA 펫 (PKP, WPF)                                                 │
   ▲            │  ├ 물어보기 창 ──────────────▶ ALFA 백엔드 :7701                 │
   │            │  │                              ├ /api/alorys/chat (비서·사용 중)  │
   │            │  │                              ├ 정책 엔진(허용/거절)             │
   │            │  │                              ├ 감사 원장(기록)                  │
   │            │  │                              └ MCP 허브 ──┐                     │
   │            │  ├ MCP 서버 :47420 ◀───────────────────────────┘ (pet_say 등)      │
   │            │  │      ▲ Claude Code·Codex 도 같은 통로로 부른다                  │
   │            │  ├ 개발 환경 감시(1분) ──▶ docker ps / 로컬 대기 포트 목록          │
   │            │  │      (Docker Desktop, alfa-backend, alfa-pg, hermes, 브리지 47411)│
   │            │  ├ 장기 기억 memory.json (기기별)                                   │
   │            │  └ NAC 자동 로그인 ──▶ Genian 로그인 창 (관리자 권한이라 막힘)       │
   └── 말풍선·표정·브리핑 카드                                                        │
                 └───────────────────────────────────────────────────────────────────┘
  (제안) 샌드박스: Docker 리눅스 데스크톱 ◀── 펫이 Claude 컴퓨터 사용으로 조작
```

## 4. 흐름

### 4.1 대화로 명령 (완료: 1단계, 2026-11-03)
1. 처음 한 번: 트레이 → AI 설정 → **ALFA 로그인**(주인 계정 아이디·비밀번호, 사용자가 직접 입력)
   → `POST /api/auth/login` → 돌려받은 **7일 세션 열쇠만** DPAPI 로 `keys.dat` 에 보관(비밀번호는 저장 안 함).
   패스키 전용 계정은 웹에서 받은 `alfa_token` 을 붙여 넣는다.
2. 나: "alfa-backend 상태 봐 줘" → 펫 물어보기 창(제공자 = ALFA)
3. 펫 → ALFA `POST /api/alorys/chat` `{text, history: []}` + `Authorization: Bearer`
   (Bearer 가 있으면 ALFA 의 Origin 검사를 통과한다 — 컨트롤러가 그렇게 설계됨)
4. ALFA: Alorys 가 직접 처리하거나 에이전트 루프(최대 4단계, 90초)로 답
5. 펫: 답을 창에 보여 줌. `status: approval_required` 면 **"ALFA 에서 승인해야 실행"이라고만** 알리고 펫이 승인하지 않는다
- 주소는 **127.0.0.1/localhost 만** 받는다(열쇠가 주인 권한이라 밖으로 새면 안 됨). 기다림 100초(서버 90초보다 길게)
- 펫 MCP 도구로 ALFA 대화를 중계하지 **않는다** — 그러면 `mcp.key` 만 가진 쪽이 ALFA 주인 권한을 얻는다
- 코드: `Core/AlfaChatClient.cs`, 시험 `AlfaChatClientTests.cs`
- **왜 cli-ask 가 아닌가**: ALFA 의 cli-ask 는 꺼져 있고(`ALFA_CLI_ASK_ENABLED` 없음), 호출자 열쇠도 없고,
  컨테이너에 claude·gemini·codex CLI 도 없다. 켜는 것만으로는 동작하지 않는다 → 나중 선택지(승인 필요)
- **알아 둘 점**: 펫 대화는 ALFA 의 `web:<내 userId>` 방에 들어가 웹 Alorys 대화와 같은 방을 쓴다.
  웹에서 답을 기다리는 중이면 펫은 "다른 질문에 답하는 중"(429)을 받는다

### 4.2 ALFA → 펫 (완료: MCP 서버)
- ALFA MCP 허브 / Claude Code 가 `pet_say`·`pet_status`·`dev_status`·`pet_briefing` 호출
- 인증: `%APPDATA%\PortKillerPet\mcp.key` Bearer. 외부 Origin 403. 검사 `scripts/mcp-smoke.ps1`

### 4.3 감시·보고 (완료)
- 1분마다 개발 환경 확인 → 멈추면 "hermes 이(가) 멈췄어요!" + 시무룩, 살아나면 기쁨
- 브리핑 카드(📊)에 수치·개발 환경·멘트. 지시봉이 마우스 높이를 따라 짚음

## 5. 지금 상태 (2026-11-03)

| 영역 | 상태 | 비고 |
|---|---|---|
| 캐릭터 4종(레서판다·고양이·여우·병아리), 종 선택 | ✅ | 복셀, 코드로 조립 |
| 브리핑(안경·지시봉), 상태 카드 | ✅ | |
| 개발 환경 감시(E) | ✅ | `devstatus.json` 으로 목록 변경 |
| MCP 서버(C) | ✅ | 10/10 시험 통과 |
| 장기 기억(B) | ✅ | 첫 출근 한마디·신기록·브리핑 카드 주간 요약·MCP `pet_memory`·"기억 지우기". 11/03 실기 확인 |
| 자동 실행·시작 기록·중복 방지 | ✅ | `startup.log` |
| NAC 자동 로그인 | 🔴 | 창 찾기·입력 방식은 됨. Genian `GNAUTH` 가 관리자 권한이라 막힘 → 관리자 도우미(작업 스케줄러) **승인 대기** |
| ALFA 백엔드 연결(물어보기 → Alorys) | ✅ | 11/03. ALFA 설정 변경 없음. 실 ALFA 로 잘못된 로그인 거절·외부 주소 차단 확인. **실제 로그인·질문은 사용자 몫** |
| ALFA `/mcp` 무인증 | ⚠️ | ALFA 의 `/mcp/*` 는 인증 필터(`/api/*` 만 검사) 밖이라 열쇠 없이 `tools/list` 가 된다(읽기 4종). 공개 도메인 노출 여부는 허브(mac-intel) 터널 설정 확인 필요. 펫은 여기에 기대지 않는다 |
| 샌드박스 컴퓨터 사용 | ⚪ | 제안. Docker 리눅스 데스크톱 + Anthropic 열쇠 필요 |
| 게임(PS5 패드·음성·WoW HUD) | 🟡 | mac 쪽에서 들어온 기능(veloai, 10/11). 입력 흉내·마이크 사용 → **개인정보 문서 미반영** |

## 6. mac-air 에서 이어서 하기

- **WPF 앱은 맥에서 빌드되지 않는다.** `PortKiller.App`·`PortKiller.Platform`·`PortKiller.Game` 은 `net9.0-windows`.
  맥에서 되는 것: `PortKiller.Core` 와 `tests/PortKiller.Core.Tests`(net9.0) → `dotnet test tests/PortKiller.Core.Tests`.
  그래서 맥에서는 **Core 규칙·시험·문서·ALFA 쪽 작업**을 하고, 화면 확인은 Windows 에서 한다.
- 맥 전용 앱: `Game/`(SwiftUI, `swift build`).
- 저장소: `git pull` 후 작업. 로컬 데이터(`%APPDATA%\PortKillerPet\*`)는 기기별이라 옮기지 않는다(펫은 기기마다 따로).

### 다음 할 일 (순서)
1. ~~B 마무리~~ ✅ 2026-11-03
2. ~~ALFA 연결~~ ✅ 2026-11-03 (Alorys 비서). 다음: ALFA `/mcp` 인증 막기(승인 후), 펫 전용 방 분리 여부 결정
3. NAC 관리자 도우미(승인 시)
4. 게임 기능의 개인정보 문서 반영
5. 샌드박스 컴퓨터 사용(결정 시)

## 7. 참고 파일

- 설계 규칙: `src/PortKiller.Core/` (`DevStatus.cs`, `McpRouter.cs`, `PetMemory.cs`, `PetKind.cs`, `Voxel*.cs`)
- 윈도우 부분: `src/PortKiller.Platform/` (`DevStatusProbe.cs`, `McpHttpHost.cs`, `MemoryStore.cs`, `NacAutoLogin.cs`)
- 개인정보·보안: `docs/privacy-and-security.md`
- ALFA 쪽: `Alfa/backend/docs/alfa-multi-llm-gateway-policy.md`, `alfa-cli-ask-external-api-spec.md`, `system-architecture.md`
