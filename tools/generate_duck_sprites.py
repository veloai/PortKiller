import os
from PIL import Image

palette = {
    '.': (0, 0, 0, 0),             # 투명
    '#': (50, 32, 12, 255),        # 도트 아웃라인 (따뜻한 진갈색)
    'Y': (255, 215, 0, 255),       # 러버덕 골드 옐로우
    'H': (255, 245, 140, 255),     # 하이라이트 (고무 광택)
    'S': (230, 165, 0, 255),       # 음영 (진한 노랑)
    'O': (255, 110, 0, 255),       # 주황 부리
    'D': (210, 70, 0, 255),        # 부리 그림자
    'K': (20, 20, 20, 255),        # 눈 (블랙)
    'W': (255, 255, 255, 255),     # 눈 하이라이트 (화이트)
    'F': (245, 130, 15, 255),      # 발 (주황)
    'P': (255, 140, 160, 255),     # 볼터치 (핑크)
    'B': (90, 190, 255, 255),      # 물방울 / 땀방울 (하늘색)
}

sprites = {}

# 1. IDLE (평화로운 기본 자세: 둥글고 귀여운 황금비 18x18 도트 러버덕)
sprites['idle'] = [
    '..................',
    '......######......',
    '....##HHHHHH##....',
    '...#HHHHHHHHHH#...',
    '...#HYYWKKYYYY#...',
    '...#YYYYYYYYYY#OO#',
    '...#YYYYYYP###OOO#',
    '....#SSSS#DDDD##..',
    '....#YYYY#####....',
    '...#YYYYYYYYYY#...',
    '..#YYYYYYYYYYYY#..',
    '.#YYYY##YYYYYYY#..',
    '.#YYYY#HYYYYYYY#..',
    '.#YYYY#YYYYYYY##..',
    '..#YYYYYYYYYY#O#..',
    '...#SSSSSSSSS#....',
    '....##########....',
    '....#FF#..#FF#....',
]

# 2. WALK1 (뒤뚱뒤뚱 1: 앞발 내딛기)
sprites['walk1'] = [
    '..................',
    '......######......',
    '....##HHHHHH##....',
    '...#HHHHHHHHHH#...',
    '...#HYYWKKYYYY#...',
    '...#YYYYYYYYYY#OO#',
    '...#YYYYYYP###OOO#',
    '....#SSSS#DDDD##..',
    '....#YYYY#####....',
    '...#YYYYYYYYYY#...',
    '..#YYYYYYYYYYYY#..',
    '.#YYYY##YYYYYYY#..',
    '.#YYYY#HYYYYYYY#..',
    '.#YYYY#YYYYYYY##..',
    '..#YYYYYYYYYY#O#..',
    '...#SSSSSSSSS#....',
    '....##########....',
    '...#FFF#....#F#...',
]

# 3. WALK2 (뒤뚱뒤뚱 2: 반대발 내딛기)
sprites['walk2'] = [
    '..................',
    '......######......',
    '....##HHHHHH##....',
    '...#HHHHHHHHHH#...',
    '...#HYYWKKYYYY#...',
    '...#YYYYYYYYYY#OO#',
    '...#YYYYYYP###OOO#',
    '....#SSSS#DDDD##..',
    '....#YYYY#####....',
    '...#YYYYYYYYYY#...',
    '..#YYYYYYYYYYYY#..',
    '.#YYYY##YYYYYYY#..',
    '.#YYYY#HYYYYYYY#..',
    '.#YYYY#YYYYYYY##..',
    '..#YYYYYYYYYY#O#..',
    '...#SSSSSSSSS#....',
    '....##########....',
    '.....#F#....#FFF#.',
]

# 4. HAPPY (기분 좋을 때 / 빌드 성공: 활짝 웃는 눈 ^^)
sprites['happy'] = [
    '......######......',
    '....##HHHHHH##....',
    '...#HHHHHHHHHH#...',
    '...#HY#KK##KK#....',
    '...#YY#KK##KK#....',
    '...#YYYYYYYYYY#OO#',
    '...#YYYYYYP###OOO#',
    '....#SSSS#DDDD##..',
    '....#YYYY#####....',
    '...#YYYYYYYYYY#...',
    '..#YYYYYYYYYYYY#..',
    '.#YYYY##YYYYYYY#..',
    '.#YYYY#HYYYYYYY#..',
    '.#YYYY#YYYYYYY##..',
    '..#YYYYYYYYYY#O#..',
    '...#SSSSSSSSS#....',
    '....##########....',
    '....#FF#..#FF#....',
]

# 5. SLEEP (낮잠 / 평온: 눈 감고 발 쏙 모으기)
sprites['sleep'] = [
    '..................',
    '..................',
    '......######......',
    '....##HHHHHH##....',
    '...#HHHHHHHHHH#...',
    '...#HY######YY#...',
    '...#YY......YY#...',
    '...#YYYYYYYYYY#OO#',
    '...#YYYYYYP###OOO#',
    '....#SSSS#DDDD##..',
    '....#YYYY#####....',
    '...#YYYYYYYYYY#...',
    '..#YYYYYYYYYYYY#..',
    '.#YYYY##YYYYYYY#..',
    '.#YYYY#HYYYYYYY#..',
    '...#SSSSSSSSSS#...',
    '....##########....',
    '....#FFFFFFFF#....',
]

# 6. SHOCK (에러 감지 / 깜놀: 똥그란 눈과 땀방울)
sprites['shock'] = [
    '..............B...',
    '......######.BBB..',
    '....##HHHHHH##B...',
    '...#HHHHHHHHHH#...',
    '...#HY#KK##KK#....',
    '...#YY#WW##WW#OO#.',
    '...#YYYYYYP###OOO#',
    '....#SSSS#DDDD##..',
    '....#YYYY#####....',
    '...#YYYYYYYYYY#...',
    '..#YYYYYYYYYYYY#..',
    '.#YYYY##YYYYYYY#..',
    '.#YYYY#HYYYYYYY#..',
    '.#YYYY#YYYYYYY##..',
    '..#YYYYYYYYYY#O#..',
    '...#SSSSSSSSS#....',
    '....##########....',
    '....#FF#..#FF#....',
]

# 7. RUN1 (고부하 전력질주 1: 몸 숙이고 속도감)
sprites['run1'] = [
    '..................',
    '........######....',
    '......##HHHHHH##..',
    '.....#HHHHHHHHHH#.',
    '.....#HYYWKKYYYY#.',
    '.....#YYYYYYYYYY#O',
    '.....#YYYYYYP#OOOO',
    '......#SSSS#DDDDD#',
    '....##YYYYYYYY##..',
    '...#YYYYYYYYYY#...',
    '..#YYYY##YYYYY#...',
    '.#YYYYY#HYYYYY#...',
    '##YYYYY#YYYYYY#...',
    '.###SSSSSSSSSS#...',
    '....##########....',
    '....#FFF#.........',
    '....#####...#FFF#.',
    '............#####.',
]

# 8. RUN2 (고부하 전력질주 2)
sprites['run2'] = [
    '..................',
    '........######....',
    '......##HHHHHH##..',
    '.....#HHHHHHHHHH#.',
    '.....#HYYWKKYYYY#.',
    '.....#YYYYYYYYYY#O',
    '.....#YYYYYYP#OOOO',
    '......#SSSS#DDDDD#',
    '....##YYYYYYYY##..',
    '...#YYYYYYYYYY#...',
    '..#YYYY##YYYYY#...',
    '.#YYYYY#HYYYYY#...',
    '##YYYYY#YYYYYY#...',
    '.###SSSSSSSSSS#...',
    '....##########....',
    '............#FFF#.',
    '....#FFF#...#####.',
    '....#####.........',
]

os.makedirs('assets/sprites/duck', exist_ok=True)

for name, rows in sprites.items():
    img = Image.new('RGBA', (18, 18))
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            img.putpixel((x, y), palette.get(ch, (0, 0, 0, 0)))
    path = f'assets/sprites/duck/{name}.png'
    img.save(path)
    print(f'Saved: {path} (18x18)')
