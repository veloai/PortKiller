// 스프라이트 뽑기. Node 24, 바깥 라이브러리 없음.
//
// 왜 이 저장소가 직접 부르나: 그림은 PortKiller 것이고 다른 제품의 스튜디오와 상관이 없다.
// 남의 DB 에 캐릭터를 등록해야 참조를 쓸 수 있는 통로를 빌리면, 우리 그림이 남의 자료에
// 섞이고 그쪽 규칙이 바뀔 때마다 우리가 멈춘다. 부르는 곳은 구글 API 한 군데뿐이다.
//
// 열쇠는 환경변수로만 받는다 — 저장소에 열쇠를 두지 않는다.
//   GEMINI_API_KEY=... node tools/gen-sprites.mjs [묶음이름 ...]
//
// 중요: 이 모델은 알파 채널을 못 낸다(docs/assets/image-brief.md §8). 여기서 나온 파일은
// 배경이 칠해진 JPEG 다. 반드시 scripts/matte-batch.ps1 로 배경을 벗겨야 쓸 수 있다.
//
// 기준 그림(anchor): 포즈마다 따로 뽑으면 매번 다른 캐릭터가 나온다. 그래서 각 묶음은
// idle 을 먼저 확정하고, 나머지 포즈는 그 idle 을 참조로 붙여 뽑는다. 참조는 배경을 벗긴
// PNG 를 쓴다 — 체크무늬가 든 원본을 참조로 주면 그 무늬까지 따라 그린다.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const RAW = path.join(ROOT, 'assets', 'sprites', '_raw');
const OUT = path.join(ROOT, 'assets', 'sprites');
const MODEL = 'gemini-3-pro-image';
const SPECIES = 'day';

const KEY = process.env.GEMINI_API_KEY;
if (!KEY) {
  console.error('GEMINI_API_KEY 가 없다. 열쇠를 환경변수로 주고 다시 실행하라.');
  process.exit(2);
}

// ---------------------------------------------------------------- 주문 문구

const STYLE = `A tiny desktop-pet mascot character, chibi proportions with an oversized round head
and a very small body. Thick clean outline, flat cel shading with only two tones, no gradients,
no texture, no fur detail. Bold simple silhouette that stays readable when shrunk to 96 pixels.
Warm friendly cartoon style, soft rounded shapes.

Facing to the RIGHT in three-quarter view. Feet planted at the very bottom of the frame.
Character fills about 90% of a square canvas, centered horizontally.

Plain flat background. Hard clean edges with no glow, no bloom, no soft drop shadow,
no blurred halo. No ground plate, no shadow on the floor.

No text, no letters, no numbers, no logos, no asymmetric markings - the sprite is mirrored
horizontally at runtime and must still read correctly.

Do NOT include: glow, bloom, drop shadow, soft shadow, photorealism, 3d render, text,
watermark, border frame, multiple characters, speech bubble, ground plate, reflection,
sticker outline, white die-cut border.`;

const SAME = `The attached image is the reference for this character. Keep EXACTLY the same
character: same body shape, same proportions, same colours, same outline weight, same art style.
Only the pose and expression change. Draw it at the same size with the feet on the same baseline.`;

const POSES = {
  idle: `standing relaxed, weight on both feet, calm content expression, eyes open looking
slightly toward the viewer's right. This is the neutral resting pose.`,
  walk1: `mid-stride, right foot forward and body lifted very slightly, arms swinging gently,
cheerful expression. Feet must stay at the same baseline as the reference.`,
  walk2: `mid-stride, left foot forward and body settled slightly lower, opposite arm swing.
Same baseline. This differs from walk1 only in the legs, arms and a tiny vertical bob -
head size and position unchanged.`,
  eat: `mouth wide open in a happy round shape, eyes squeezed into upward curves, both hands
raised near the mouth as if holding food. Pure delight. Do NOT draw the food itself.`,
  happy: `eyes closed in joyful upward arcs, big open smile, both arms raised, body slightly
airborne with feet just barely off the ground. Also used for jumping, so the pose must read
as "lifting up".`,
  sick: `drooping posture, half-closed tired eyes, small flushed pink patches on the cheeks,
mouth a small wavy line, shoulders sagging, head tilted down. Sad but still cute - never
grotesque, never distressing.`,
  sleep: `curled up sitting on the ground, eyes closed as simple curved lines, head resting to
one side, peaceful small smile, body compact and rounded. Do NOT draw sleep symbols like
"Z" letters - no text allowed.`,
  sulk: `head and body turned away to the left while the feet still point right, cheeks puffed
out, eyes shut tight in a pouting frown, arms crossed or held stiffly down. Clearly
"offended but not sad".`,
};

/** 단계마다 몸이 달라진다. idle 을 뽑을 때만 붙는 설명이다. */
const STAGES = {
  baby: `CHARACTER: a small round creature with a focused, hard-working personality - a simple
rounded body, two small tufts on top of the head, big friendly eyes, short stubby arms and legs.
Warm orange body with a cream face and belly. Two flat colours only. This is the BABY stage:
very round, very small body, head much bigger than the body.`,
  child: `This is the CHILD stage of the SAME creature shown in the reference: a little taller
and slightly slimmer than the baby, legs a bit longer, the head still large but less dominant.
The tufts on the head are slightly bigger. Same colours, same face, same outline weight.`,
  adult: `This is the ADULT stage of the SAME creature shown in the reference: taller again,
a clearly defined body under the head, longer limbs, a calm confident posture. The head tufts
are fully grown. Same colours, same face, same outline weight.`,
};

const EGG = {
  idle: `A smooth speckled egg resting upright, perfectly still, calm and inviting. Pastel cream
shell with a few soft freckles. Slightly wider at the bottom so it looks stable and sits flat.
No character, just the egg.`,
  tilt1: `The same egg tipped about 12 degrees to the LEFT, as if it just wobbled.`,
  tilt2: `The same egg tipped about 12 degrees to the RIGHT, a mirrored wobble.`,
  crack: `The same egg upright again but with two or three thin jagged cracks spreading from the
top, and a faint warm light escaping from inside the cracks. The light must stay INSIDE the
shell outline and must not spill outside the egg.`,
};

const POOP = `A small cartoon droppings pile, three soft rounded swirls stacked into a cone,
warm brown with a lighter highlight on the upper left. Cute and clean, absolutely not gross or
realistic. Simple bold outline. Sits flat on its base. No character.`;

// ---------------------------------------------------------------- 호출

async function callGemini(prompt, referenceFile) {
  const parts = [];
  if (referenceFile && fs.existsSync(referenceFile)) {
    const bytes = fs.readFileSync(referenceFile);
    parts.push({
      inline_data: {
        mime_type: bytes[0] === 0xff ? 'image/jpeg' : 'image/png',
        data: bytes.toString('base64'),
      },
    });
  } else if (referenceFile) {
    // 참조가 있어야 하는데 없으면 멈춘다. 참조 없이 뽑으면 다른 캐릭터가 나오는데
    // 돈은 그대로 나간다 — 조용히 넘어가면 나중에 전부 다시 뽑아야 한다.
    throw new Error(`reference image missing: ${referenceFile}`);
  }
  parts.push({ text: prompt });

  const res = await fetch(
    `https://generativelanguage.googleapis.com/v1beta/models/${MODEL}:generateContent`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json', 'x-goog-api-key': KEY },
      body: JSON.stringify({
        contents: [{ role: 'user', parts }],
        generationConfig: {
          responseModalities: ['IMAGE'],
          imageConfig: { aspectRatio: '1:1' },
        },
      }),
    },
  );

  const text = await res.text();
  if (!res.ok) throw new Error(`HTTP ${res.status} ${text.slice(0, 300)}`);

  const json = JSON.parse(text);
  for (const part of json?.candidates?.[0]?.content?.parts ?? []) {
    const inline = part.inlineData ?? part.inline_data;
    if (inline?.data) {
      const mime = inline.mimeType ?? inline.mime_type ?? 'image/png';
      return { bytes: Buffer.from(inline.data, 'base64'), ext: mime.includes('png') ? 'png' : 'jpg' };
    }
  }
  throw new Error(`no image in response: ${text.slice(0, 300)}`);
}

/** 이미 뽑아 둔 것은 건너뛴다 — 중간에 끊겨도 다시 돌리면 남은 것만 뽑는다(돈이 나가므로). */
function rawPath(name) {
  for (const ext of ['jpg', 'png']) {
    const p = path.join(RAW, `${name}.${ext}`);
    if (fs.existsSync(p)) return p;
  }
  return null;
}

async function make(name, prompt, referenceFile) {
  const done = rawPath(name);
  if (done) {
    console.log(`skip  ${name}  (already there)`);
    return done;
  }
  const started = Date.now();
  const { bytes, ext } = await callGemini(prompt, referenceFile);
  const file = path.join(RAW, `${name}.${ext}`);
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, bytes);
  console.log(`made  ${name}  ${(bytes.length / 1024) | 0}KB  ${((Date.now() - started) / 1000).toFixed(1)}s`);
  return file;
}

/** 벗겨 낸 PNG 자리. 참조로 쓸 때는 이쪽을 본다. */
const matted = name => path.join(OUT, 'chars', ...name.split('/')) + '.png';

// ---------------------------------------------------------------- 묶음

async function runStage(stage, anchorRef) {
  const base = `${SPECIES}/${stage}`;
  const idlePrompt = [STYLE, '', STAGES[stage], '', anchorRef ? SAME : '', '',
    `POSE (idle): ${POSES.idle}`].join('\n');
  await make(`${base}/idle`, idlePrompt, anchorRef);

  const ref = matted(`${base}/idle`);
  if (!fs.existsSync(ref)) {
    console.log(`HOLD  ${base}/idle must be matted before the other poses can be drawn.`);
    console.log('      run scripts/matte-batch.ps1, then run this again.');
    return false;
  }
  for (const [pose, body] of Object.entries(POSES)) {
    if (pose === 'idle') continue;
    await make(`${base}/${pose}`, [STYLE, '', SAME, '', `POSE (${pose}): ${body}`].join('\n'), ref);
  }
  return true;
}

async function runEgg() {
  await make('egg/idle', [STYLE, '', EGG.idle].join('\n'), null);
  const ref = matted('egg/idle');
  if (!fs.existsSync(ref)) {
    console.log('HOLD  egg/idle must be matted before the other eggs can be drawn.');
    return false;
  }
  for (const name of ['tilt1', 'tilt2', 'crack']) {
    await make(`egg/${name}`, [STYLE, '', SAME, '', EGG[name]].join('\n'), ref);
  }
  return true;
}

const GROUPS = {
  egg: runEgg,
  baby: () => runStage('baby', matted(`${SPECIES}/baby/idle`)),
  child: () => runStage('child', matted(`${SPECIES}/baby/idle`)),
  adult: () => runStage('adult', matted(`${SPECIES}/child/idle`)),
  poop: async () => { await make('poop', [STYLE, '', POOP].join('\n'), null); return true; },
};

const wanted = process.argv.slice(2).filter(a => a in GROUPS);
const groups = wanted.length ? wanted : Object.keys(GROUPS);

let failed = 0;
for (const g of groups) {
  console.log(`--- ${g} ---`);
  try {
    if (!(await GROUPS[g]())) failed++;
  } catch (e) {
    console.error(`FAIL  ${g}: ${e.message}`);
    failed++;
  }
}
console.log(`done. ${failed} group(s) failed or on hold. raw files in assets/sprites/_raw`);
process.exit(failed ? 1 : 0);
