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
