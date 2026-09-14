# 필요한 그림 목록과 주문서

지금 펫은 이모지다. 이 문서는 그 자리를 채울 그림의 **목록·규격·주문 문구**다.

---

## 0. 먼저 읽을 것 — 코드가 이미 정해 놓은 제약

아래는 취향이 아니라 **코드가 요구하는 조건**이다. 어기면 기능이 깨진다.

### (1) 반투명 테두리·빛번짐 금지 ★가장 중요

클릭 통과가 **알파값으로만** 동작한다. 완전히 투명한 픽셀만 뒤 창으로 클릭이 넘어간다.
캐릭터 주변에 은은한 빛(글로우)이나 흐릿한 그림자를 깔면, **눈에는 안 보이는데 클릭은 막히는 영역**이 생긴다.

- 외곽은 **딱 떨어지게**. 안티에일리어싱 정도(1~2px)만 허용
- 그림자를 넣으려면 **캐릭터에 붙은 단단한 그림자**로. 바닥에 퍼지는 반투명 원 금지
- 배경은 **완전 투명(알파 0)**

### (2) 오른쪽을 보게 그린다

왼쪽으로 걸을 때 코드가 **좌우를 뒤집는다**(`ScaleTransform(-1,1)`).

- 기본 방향: **오른쪽**
- 뒤집혀도 어색하지 않아야 한다 → 한쪽에만 있는 **글자·숫자·비대칭 로고 금지**
- 눈·입은 뒤집어도 말이 되게

### (3) 발이 바닥에 닿아야 한다

펫은 화면 맨 아래 바닥선 위에 선다. 캔버스 안에서 **발바닥이 아래 가장자리에 거의 닿게** 그린다.
포즈마다 발 높이가 들쭉날쭉하면 걸을 때 **위아래로 덜덜 떨린다.**

### (4) 크기

화면 표시 크기는 96×96이지만, 배율 150% 노트북에서는 실제 144px로 늘어난다.

- 원본: **256×256 PNG, 투명**
- 캐릭터가 캔버스의 **85~95%**를 채우게 (여백이 크면 화면에서 작아 보인다)
- 모든 포즈가 **같은 캔버스 크기·같은 기준선**

### (5) 작은 화면에서 알아볼 수 있어야 한다

96px는 손톱만 하다. 게다가 배경이 **밝은 문서일 수도, 어두운 IDE일 수도** 있다.

- 실루엣이 단순하고 뚜렷할 것
- 밝은 배경·어두운 배경 양쪽에서 보이도록 **캐릭터에 자기 테두리선**을 둘 것
- 잔털·가는 선·작은 무늬는 96px에서 뭉개진다 → 넣지 말 것

### (6) 화풍은 글로 안 된다 — 기준 그림을 먼저 확정한다

말로만 주문하면 장마다 그림체와 인물이 바뀐다. 순서를 지켜야 한다.

1. **`idle` 한 장**을 먼저 여러 번 뽑아 마음에 드는 것 하나를 고른다 → 이게 **기준 그림**
2. 나머지 포즈는 **반드시 그 기준 그림을 참조로 첨부**해서 뽑는다
3. 참조 없이 뽑은 장은 **다른 캐릭터가 된다.** 같은 묶음 안이라도 그렇다

---

## 1. 공통 화풍 문구 (모든 주문에 앞에 붙인다)

```
A tiny desktop-pet mascot character, chibi proportions with an oversized round head
and a very small body. Thick clean outline, flat cel shading with only two tones,
no gradients, no texture, no fur detail. Bold simple silhouette that stays readable
when shrunk to 96 pixels. Warm friendly cartoon style, soft rounded shapes.

Facing to the RIGHT in three-quarter view. Feet planted at the very bottom of the frame.
Character fills about 90% of a square canvas, centered horizontally.

Fully transparent background, alpha 0. Hard clean edges with no glow, no bloom,
no soft drop shadow, no blurred halo, no background plate of any kind.

No text, no letters, no numbers, no logos, no asymmetric markings — the sprite is
mirrored horizontally at runtime and must still read correctly.

Output: single character on transparent background, 256x256, PNG.
```

**금지어(뒤에 붙인다)**

```
--no background, glow, bloom, drop shadow, soft shadow, gradient background,
photorealism, 3d render, text, watermark, border frame, multiple characters,
speech bubble, ground plate, reflection
```

> 말풍선은 앱이 직접 그린다. 그림에 말풍선이 들어가면 두 개가 겹친다.

---

## 2. 알 (부화 전) — 4장

종과 무관하게 공용. 처음 켰을 때 제일 먼저 보이는 그림이다.

| 파일 | 언제 보이나 | 필요한 장면 |
|---|---|---|
| `egg/idle.png` | 앱을 처음 켰을 때 | 가만히 놓인 알 |
| `egg/tilt1.png` | 알을 클릭할 때 | 왼쪽으로 살짝 기운 알 |
| `egg/tilt2.png` | 알을 클릭할 때 | 오른쪽으로 살짝 기운 알 |
| `egg/crack.png` | 부화 직전 | 금이 간 알 |

```
[공통 화풍 문구]

A smooth speckled egg resting upright. Pastel cream shell with a few soft freckles.
Slightly wider at the bottom so it looks stable and sits flat.

(idle)  perfectly upright and still, calm and inviting.
(tilt1) tipped about 12 degrees to the left, as if it just wobbled.
(tilt2) tipped about 12 degrees to the right, mirrored wobble.
(crack) upright again but with two or three thin jagged cracks spreading from the top,
        a faint warm light escaping from inside the cracks — the light must stay INSIDE
        the shell outline and must not spill onto the transparent background.
```

**주의**: `crack`의 빛이 알 바깥으로 새면 클릭 통과가 깨진다. 빛은 껍질 안쪽에만.

---

## 3. 캐릭터 포즈 — 1종당 8장

한 종(species)마다 아래 8장이 필요하다. **성장 단계마다 한 벌씩** 필요하다.

| 파일 | 언제 보이나 | 필요한 표정·동작 |
|---|---|---|
| `idle.png` | 평소 (제일 오래 보임) | 편안히 서서 정면 살짝 오른쪽을 봄 |
| `walk1.png` | 걸을 때 A | 한쪽 발 앞으로, 몸이 살짝 올라감 |
| `walk2.png` | 걸을 때 B | 반대 발 앞으로, 몸이 살짝 내려감 |
| `eat.png` | 먹이·간식을 줬을 때 | 입을 크게 벌리고 행복하게 먹는 중 |
| `happy.png` | 놀기·쓰다듬기·할 일 완료 | 눈을 감고 웃으며 폴짝 (점프에도 씀) |
| `sick.png` | 체력이 25 아래 | 축 처지고 볼이 붉고 힘없는 눈 |
| `sleep.png` | 재웠을 때 | 눈 감고 웅크려 앉음 |
| `sulk.png` | 행복이 20 아래 | 고개 돌리고 볼 부풀림, 눈을 안 마주침 |

각 포즈 주문에 붙일 문구:

```
[공통 화풍 문구]
[기준 그림 첨부 — 같은 캐릭터, 같은 색, 같은 화풍을 유지할 것]

(idle)  standing relaxed, weight on both feet, calm content expression, eyes open
        looking slightly toward the viewer's right. This is the neutral resting pose.

(walk1) mid-stride, right foot forward and body lifted very slightly, arms swinging
        gently, cheerful expression. Feet must stay at the same baseline as idle.

(walk2) mid-stride, left foot forward and body settled slightly lower, opposite arm
        swing. Same baseline. walk1 and walk2 must differ only in the legs, arms and
        a tiny vertical bob — head size and position unchanged.

(eat)   mouth wide open in a happy round shape, eyes squeezed into upward curves,
        both hands raised near the mouth as if holding food. Pure delight.
        Do NOT draw the food itself.

(happy) eyes closed in joyful upward arcs, big open smile, both arms raised,
        body slightly airborne with feet just barely off the ground.
        Also used for jumping, so the pose must read as "lifting up".

(sick)  drooping posture, half-closed tired eyes, small flushed pink patches on the
        cheeks, mouth a small wavy line, shoulders sagging, head tilted down.
        Sad but still cute — never grotesque, never distressing.

(sleep) curled up sitting on the ground, eyes closed as simple curved lines,
        head resting to one side, peaceful small smile, body compact and rounded.
        Do NOT draw sleep symbols like "Z" letters — no text allowed.

(sulk)  head and body turned away to the left while feet still point right,
        cheeks puffed out, eyes shut tight in a pouting frown, arms crossed
        or held stiffly down. Clearly "offended but not sad".
```

---

## 4. 몇 벌이 필요한가 — 단계별로 끊어서 간다

전부 한 번에 만들려 하면 **6종 × 5단계 × 8포즈 = 240장**이 된다. 그렇게 하지 말 것.

### 1단계 — 굴러가게 만드는 최소 (29장)

| 묶음 | 장수 |
|---|---|
| 알 | 4 |
| 기본 종 1개 × 아기 | 8 |
| 기본 종 1개 × 소년기 | 8 |
| 기본 종 1개 × 성체 | 8 |
| 응아 | 1 |

이것만 있으면 게임이 처음부터 끝까지 돈다. **먼저 이걸 끝내고 화면에 붙여 본 뒤** 다음으로 간다.
작게 붙여 봐야 "96px에서 안 보인다" 같은 문제가 드러난다.

### 2단계 — 진화 (16장)

성체의 진화형 2단계 × 8포즈.

### 3단계 — 나머지 종 (종당 40장)

현재 종 구분은 **부화한 시간대**로 갈린다 (`SpeciesPicker`).

| id | 언제 부화하면 나오나 | 성격 방향 제안 |
|---|---|---|
| `dawn` | 00~06시 | 밤을 새운, 눈 밑이 어두운 |
| `morning` | 06~11시 | 부지런하고 생기 있는 |
| `lunch` | 11~14시 | 배부르고 느긋한 |
| `day` | 14~18시 | 집중하는, 일하는 |
| `evening` | 18~24시 | 나른하고 편안한 |
| `friday` | 금요일 저녁 | 들뜬, 신난 |

---

## 5. 그 밖에 필요한 그림

| 파일 | 크기 | 목적 | 주문 |
|---|---|---|---|
| `poop.png` | 128×128 | 바닥에 생기는 응아. 클릭하면 치워짐 | 아래 |
| `tray.ico` | 16·32·48 합본 | 트레이 아이콘. 지금은 코드로 그린 파란 원 | 아래 |
| `app.ico` | 16~256 합본 | exe 아이콘. 파일 탐색기·작업표시줄 | 아래 |

```
(poop)
[공통 화풍 문구]
A small cartoon droppings pile, three soft rounded swirls stacked into a cone,
warm brown with a lighter highlight on the upper left. Cute and clean, absolutely
not gross or realistic. Simple bold outline. Sits flat on its base.

(tray.ico / app.ico)
[공통 화풍 문구]
An extremely simplified icon version of the pet's head only — no body.
Must stay readable at 16x16 pixels: at most three colors, one thick outline,
no small details, no facial detail beyond two dots for eyes.
Centered in a square with a small even margin.
```

> 트레이 아이콘은 **16px에서 알아볼 수 있는지**가 전부다. 얼굴 전체를 넣으려 하면 뭉개진다.
> 머리 실루엣 하나로 승부해야 한다.

---

## 6. 받은 그림을 넣는 법

```
assets/sprites/chars/<species>/<stage>/idle.png
assets/sprites/chars/egg/idle.png
assets/sprites/poop.png
```

지금 코드는 이모지를 쓰고 있다. 그림이 준비되면 `PetWindow.xaml` 의 `PetFace` 를
`Image` 로 바꾸고, `RefreshFace()` 가 단계·상태에 맞는 파일을 고르게 하면 된다.
**파일 이름만 위 규칙대로 오면 코드는 내가 바꾼다.**

## 7. 받자마자 확인할 것

붙이기 전에 이것부터 본다. 나중에 발견하면 전부 다시 뽑아야 한다.

1. **투명 배경인가** — 포토샵 아닌 뷰어에서 열어 체크무늬가 보이는지
2. **가장자리에 흐린 번짐이 없는가** — 있으면 클릭이 막힌다
3. **8장의 발 높이가 같은가** — 다르면 걸을 때 떨린다
4. **96px로 줄여도 알아보이는가** — 줄여서 보는 게 유일한 검사다
5. **좌우로 뒤집어도 말이 되는가**
6. **8장이 같은 캐릭터로 보이는가** — 아니면 기준 그림을 다시 첨부해 뽑는다

---

## 8. 실제로 뽑아 보고 알게 된 것 (2026-09-14, IVS Gemini 3장 실측)

### 그림 모델은 투명 배경을 못 만든다 — 체크무늬를 **그려서** 준다

주문서대로 "fully transparent background, alpha 0" 를 넣고 불렀더니 돌아온 것은
**JPEG** 였고, 배경 자리에는 투명을 뜻하는 **회색·흰색 체크무늬가 그림으로 칠해져** 있었다.
알파 채널 자체가 없다. 그대로 쓰면 펫 주위가 체크무늬 판때기가 되고 클릭 통과도 전부 막힌다.

문구를 고쳐서 될 일이 아니다. **뽑은 뒤에 배경을 벗겨내는 단계가 반드시 필요하다.**

### 배경 벗기기: `scripts/matte-sprite.ps1`

```
powershell -File scripts/matte-sprite.ps1 -In 받은그림.jpg -Out sprite.png
```

하는 일:

1. 테두리에서 안쪽으로 번지며 **밝고 색기 없는** 픽셀(체크무늬)을 지운다.
   캐릭터에 두꺼운 검은 외곽선이 있어서 번짐이 거기서 멈춘다. 크림색 배는 색기가 있어 살아남는다.
2. JPEG 가 남긴 흐린 테 한 겹을 한 번 더 벗긴다.
3. 캐릭터만 남은 상자를 잘라 **256×256 가운데·발바닥 아래 기준선**에 맞춘다.
4. 알파를 **0 아니면 255** 로 못박는다. 반투명을 남기면 §0-(1) 대로
   **안 보이는데 클릭은 막는 픽셀**이 생긴다.

3장 모두 통과했다 (불투명 42~45%).

### 그래서 주문 순서가 이렇게 된다

| 단계 | 하는 곳 |
|---|---|
| 1. 뽑기 | `POST /ivs/lab/image` (provider=GEMINI, aspectRatio=1:1) |
| 2. 받기 | `<fortune 실행 폴더>/dummy/lab-works/<uuid>.jpg` |
| 3. 배경 벗기기 | `scripts/matte-sprite.ps1` |
| 4. 96px 로 줄여 보기 | §7 확인표 |

> IVS 실험실은 **부를 때마다 돈이 나간다.** 한 번에 29장을 지르지 말 것.
> §0-(6) 대로 `idle` 기준 그림부터 확정하고, 나머지는 그 그림을 `referencePath` 로 첨부해 뽑는다.

### 지금 상태

기준 그림 후보 3장이 `assets/sprites/_candidates/` 에 있다 (`_compare.png` 로 나란히 비교).
**하나를 고르면** 그것을 참조로 붙여 나머지 7포즈 + 단계별 벌을 뽑는다.

### 덧 — "제미나이는 투명 배경 되지 않나" 에 대한 확인 (2026-09-14)

**안 된다.** 주문 문구를 바꿔 두 번 더 뽑아 봤다("실제 알파 채널로 내라 · 체크무늬를
그리지 마라 · 다이컷 스티커다"). 결과는 똑같이 **체크무늬가 칠해진 JPEG** 였고,
이번에는 스티커 흰 테두리까지 덤으로 그려 넣었다.

원인은 문구가 아니라 모델이다. 구글 이미지 모델 계열(나노바나나 = Gemini 2.5 Flash Image,
나노바나나 프로 = Gemini 3 Pro Image)은 **RGB 3채널만 낸다 — 알파 채널이 없다.**
"투명 배경"을 주문하면 흰 판, 검은 판, 아니면 투명을 **뜻하는 그림**(체크무늬)이 나온다.
`imageConfig` 에도 형식을 정하는 칸이 없다(우리 어댑터가 쓰는 것은 `aspectRatio` 뿐).

사람들이 "제미나이는 투명 배경 된다"고 말하는 것은 **앱·외부 서비스가 뒤에서 배경을
벗겨 주는 것**이지 API 가 알파를 내주는 것이 아니다. 그래서 벗기는 단계는 우리가 갖는다.

**나중에 품질이 아쉬우면 쓸 더 좋은 방법**: 같은 주문을 **흰 배경 한 장·검은 배경 한 장**으로
뽑아 두 장을 견주면 알파를 수식으로 정확히 복원할 수 있다(반투명 경계까지). 모델이 같은
그림을 꽤 일관되게 다시 그려 주기 때문에 성립한다. 지금은 외곽선이 두꺼워
`matte-sprite.ps1` 의 번짐 방식으로 충분해서 쓰지 않았다. 장당 비용이 두 배가 된다.

출처: [Nano Banana PNG Fix](https://transparify.app/blog/gemini-transparent-background) ·
[Why Google Gemini Fails at Transparent Backgrounds](https://discover.oreateai.com/discover/why-google-gemini-fails-at-transparent-backgrounds-and-how-to-fix-it) ·
[Gemini API forum — Unable to create Transparent PNGs](https://discuss.ai.google.dev/t/unable-to-create-transparent-pngs/92868)

---

## 9. 실제로 만들어 넣었다 (2026-09-14) — 29장

3번 후보(`day/baby/idle`)를 기준 그림으로 확정하고 나머지를 전부 뽑았다.

| 묶음 | 장수 | 참조로 쓴 그림 |
|---|---|---|
| 알 | 4 | `egg/idle` (tilt·crack 이 이걸 참조) |
| 아기 | 8 | 기준 그림 = 후보 3번 |
| 소년기 | 8 | 아기 idle |
| 성체 | 8 | 소년기 idle |
| 응아 | 1 | 없음 |

같은 캐릭터로 이어졌다. 단계마다 idle 을 먼저 확정하고 그 idle 을 참조로 나머지 7포즈를
뽑는 순서를 지켰기 때문이다. **참조를 앞 단계 것으로 이어 붙이면 몸이 자라도 얼굴이 안 바뀐다.**

### 통로: fortune 이 아니라 이 저장소가 직접 부른다

처음에는 IVS 실험실(`POST /ivs/lab/image`)로 불렀는데, **참조 그림은 fortune DB 에 "확정 기준"
으로 등록된 것만 쓸 수 있다.** 우리 펫을 남의 캐릭터 표에 등록해야 참조가 걸리는 구조다.
그림은 PortKiller 것이고 그쪽 자료와 상관이 없으므로, 통로를 이 저장소로 가져왔다:
`tools/gen-sprites.mjs`. 부르는 곳은 구글 API 한 군데뿐이고, 열쇠는 환경변수로만 받는다.

### 배경 벗기기에서 걸린 것 세 가지

1. **체크무늬만 가정한 규칙은 반만 맞았다.** 모델은 체크무늬 말고 **단색 판**(황갈색·청록색·자주색)
   위에 그려 주기도 한다. "밝고 색기 없는 픽셀"을 지우는 첫 규칙은 그때 아무 일도 안 했고,
   네 장이 색 네모를 두른 채로 통과했다. → 테두리에 **실제로 있는 색을 읽어서** 그 색을 지운다.
2. **판 안에 판이 또 있었다.** 알 3장은 흰 종이 위에 크림색 액자, 그 안에 황갈색 판, 그 안에 알이었다.
   한 번 지우고 멈추면 액자가 그림이 된다. → 남은 그림의 **테두리가 거의 한 색이면 그것도 판**으로 보고
   한 번 더 지운다(최대 2번 — 캐릭터 외곽선은 균일한 사각형이 될 수 없으므로 저절로 멈춘다).
3. **다시 지울 때 시작점이 이미 지워져 있어서 한 발도 못 나갔다.** "지워진 칸은 건너뛴다"로 짜면
   두 번째 홍수가 자기 시작점에서 바로 죽는다. → "가 봤다"를 따로 기록한다.

> 덤으로 하나 더: **PowerShell 변수 이름은 대소문자를 구분하지 않는다.** `$bx` 와 `$bX` 가 같은
> 변수라서 최소/최대를 그렇게 이름 지으면 상자가 한 점으로 찌그러진다. 한참 헤맸다.

### 확인한 것

- 29장 전부 **알파 0 아니면 255** (반투명 0픽셀 — 안 보이는데 클릭 막는 자리가 없다)
- 전부 256×256, 발바닥이 아래 기준선에 맞음 (`scripts/sprite-sheet.ps1` 의 빨간 줄로 확인)
- 밝은 배경·어두운 배경 양쪽에서 96px 로 알아볼 수 있음
- 실제 바탕화면에 떠서 그려지는 것 실측 (`scripts/verify-sprites.ps1`)

### 크기 맞추기는 알고리즘으로 풀면 안 된다 (실패 기록)

포즈마다 캔버스에 꽉 채워 넣으면, 웅크린 `sleep` 은 납작해서 머리가 커지고 알 `tilt2` 는 작게 나온다.
그래서 **원본에서 캐릭터가 차지한 크기 비율대로 줄이는** 단계를 만들었다. 결과는 더 나빴다.

원본에서 모델이 그리는 크기는 **아무 뜻이 없다.** 매번 다르게 그린다. 그 비율을 가져오니
멀쩡하던 줄까지 흔들렸다 — 성체가 `idle` 만 100%, 걷기는 92% 가 되어 **걷기 시작하는 순간
펫이 작아진다.** 원래 결함보다 큰 결함을 만든 것이다.

- **화면에 보이는 크기를 일정하게** 하는 것이 목적이다. 원본 크기를 보존하는 것이 아니다.
- 캔버스에 맞춰 넣는 원래 방식이 맞다. 그 단계는 되돌렸고 `normalize-sizes.ps1` 은 지웠다.
- 크기가 튀는 장이 있으면 **그 장만 다시 뽑는다.** 주문에 "참조와 화면에서 차지하는 크기가
  픽셀 단위로 같아야 한다 · 확대도 축소도 다시 자르기도 하지 마라" 를 넣으면 잡힌다.
  장당 20초, 한 번 호출이면 끝난다. 코드로 풀 문제가 아니었다.

**그래서 두 장만 다시 뽑았다.** `egg/tilt2` 와 `day/adult/sleep`. 주문에 넣은 문구:

```
SIZE IS CRITICAL. The attached reference and this new drawing are two frames of the same
animation, so the subject must occupy EXACTLY the same amount of the frame as it does in the
reference: same height in pixels, same width in pixels, same position, same distance from each
edge. Do not zoom in, do not zoom out, do not re-crop. Only the pose changes.

(sleep 에는 추가) Because it is curled up it is shorter than the standing reference - that is
correct, do NOT enlarge it to fill the frame. THE HEAD MUST BE THE SAME SIZE IN PIXELS AS THE
HEAD IN THE REFERENCE. Leave the empty space above it empty.
```

두 번 호출, 40초, 둘 다 한 번에 맞았다. 크기가 안 맞는 장이 또 나오면 이 문구를 쓴다.

---

## 10. 아이콘 (2026-09-14) — 트레이·exe

성체 `idle` 을 참조로 **머리만** 그린 그림 한 장(`assets/icons/head.png`)에서 두 개를 만든다.

- `assets/icons/tray.ico` — 16·20·24·32·40·48·64
- `assets/icons/app.ico` — 위 + 128·256 (exe 에 박힌다)

### 왜 .ico 를 손으로 쓰나

`System.Drawing` 은 아이콘을 **한 크기만** 저장할 수 있다. 그런데 윈도우는 자리마다 다른
크기를 골라 간다 — 트레이·제목줄 16, Alt+Tab 32, 탐색기 목록 48, 큰 아이콘 보기 256.
한 크기만 주면 윈도우가 직접 줄이는데, 그 결과가 우리가 미리 줄여 둔 것보다 나쁘다.
그래서 `scripts/make-ico.ps1` 이 ICO 구조를 직접 쓴다. 64px 이하는 32비트 DIB,
256px 은 PNG(비스타 이후 큰 아이콘의 표준 저장 방식 — 안 그러면 그 한 장으로 256KB 가 는다).

**스프라이트와 달리 아이콘은 알파를 부드럽게 둔다.** 아이콘은 클릭 통과와 상관이 없고,
16px 에서 딱딱한 가장자리는 지저분해 보인다.

### 주문 문구에서 중요한 줄

```
IT MUST STAY READABLE AT 16x16 PIXELS. That is the only thing that matters.
Strip every small detail: no nose shading, no cheek blush, no individual hairs,
no inner ear lines, no whiskers. Eyes are two simple dark dots.
```

두 장 뽑아 16px 로 줄여 놓고 골랐다. 더 단순한 쪽(`head-b`)이 이긴다 — **얼굴 전체를
넣으려 하면 뭉개진다. 머리 실루엣 하나로 승부해야 한다.**

### 손으로 ICO 를 쓸 때 밟은 함정 3개

1. **BITMAPINFOHEADER 의 높이는 두 배로 적는다.** 색 줄과 그 아래 마스크 줄을 함께 세기 때문이다.
2. **PowerShell 의 `[int]` 는 내림이 아니라 반올림이다.** `[int](7/8)` 이 1 이라
   마스크 바이트 자리가 밀려 배열 밖을 짚었다. `[Math]::Floor` 를 써야 한다.
3. **`powershell -File` 은 인자를 전부 문자열로 넘긴다.** `[int[]]$Sizes` 는 그 순간 깨진다 —
   쉼표로 이은 문자열로 받아 직접 쪼갠다.

> 그리고 또 대소문자 함정: `$out`(MemoryStream)이 파라미터 `$Out`(경로)과 같은 변수였다.
> 이 저장소에서 **세 번째** 같은 사고다. PowerShell 에서는 이름이 같으면 같은 변수다.

### 확인

`System.Drawing.Icon` 으로 다시 열어 보고 끝낸다 — 잘못 쓴 파일이 트레이가 아니라
**만드는 자리에서** 터지게 하려는 것이다. exe 아이콘은 `ExtractAssociatedIcon` 으로 꺼내 봤다.

---

## 11. 종이 늘었다 — `evening` 24장, 그리고 없는 종을 대비하는 법 (2026-09-14)

**사고부터**: 저녁에 알을 깼더니 캐릭터가 **검은 윤곽**으로 나왔다. 종은 부화 시간대로 갈리는데
(`SpeciesPicker`) 그림이 있는 종은 `day` 하나뿐이었다. 그림을 못 찾아 이모지로 내려갔고,
`PetFace` 에 글자색이 없어서 이모지가 검은 글자로 찍힌 것이다.

**그림이 덜 그려진 것과 고장 난 것은 화면에서 구분이 안 된다.** 그래서 둘을 고쳤다.

1. `SpriteLibrary` 가 **세 단계로 물러선다** — 그 종·그 포즈 → 그 종의 idle → `day` 의 같은 포즈.
   앞으로 어떤 종이 나와도 캐릭터가 사라지지 않는다.
2. `evening` 24장을 실제로 뽑았다.

### 종은 다른 생물이 아니다 — 색조 변주다

종마다 다른 동물을 그리면 6종 × 40장이 전부 **새 캐릭터 만들기**가 된다. 그러면 기준 그림도
6벌이 필요하고 화풍이 갈라진다. 그래서 몸·비율·화풍은 그대로 두고 **색조와 분위기만** 바꾼다.
`tools/gen-sprites.mjs` 의 `FLAVOURS` 가 그 한 문단씩을 들고 있다.

```
node tools/gen-sprites.mjs --species evening baby
```

**아기의 기준 그림은 언제나 `day/baby/idle`** 이다. 그걸 참조로 주고 색조 문단을 붙이면
같은 아이의 다른 색이 나온다. 소년기·성체는 제 종의 앞 단계를 참조로 잇는다.

| 종 | 색조 | 상태 |
|---|---|---|
| `day` | 주황 (기준) | 24장 |
| `evening` | 장밋빛 갈색 | 24장 |
| `dawn` | 흐린 라벤더 회색 | 문구만 |
| `morning` | 부드러운 황금빛 | 문구만 |
| `lunch` | 복숭앗빛 | 문구만 |
| `friday` | 선명한 산호빛 | 문구만 |

### 또 하나 — API 가 가끔 빈손으로 온다

`evening` 을 뽑는 동안 두 번, 응답에 그림 없이 `usageMetadata` 만 왔다. 원인은 안 밝혔다.
생성기가 **이미 있는 파일은 건너뛰므로 같은 명령을 다시 돌리면 빠진 것만 채운다.**
빠진 채로 넘어가지 않게 개수를 세서 확인한다.
