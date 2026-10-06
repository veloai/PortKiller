namespace PortKiller.Core;

/// <summary>색이 있는 직육면체 하나. 좌표는 "복셀 한 칸 = 1" 단위, 몸이 +X 쪽을 보고 Y 가 위, Z 가 좌우다.</summary>
public sealed record VoxelBox(string Part, string Color, float X0, float Y0, float Z0, float X1, float Y1, float Z1);

/// <summary>
/// 3D 도트(복셀) 레서판다의 설계도. 화면 기술을 모르고 숫자와 이름만 안다.
///
/// <para>부품(<c>Part</c>)으로 나눠 둔 이유: 걷기·고개 끄덕임·꼬리 흔들기를 부품 단위로 움직이기 위해서다.
/// 통짜 3D 모델은 이게 안 돼서 "가만히 미끄러지는 인형"이 된다.</para>
/// </summary>
public static class VoxelPanda
{
    public const string Body = "Body";
    public const string Head = "Head";
    public const string EyesOpen = "EyesOpen";
    public const string EyesClosed = "EyesClosed";
    public const string Tail = "Tail";
    public const string LegFL = "LegFL";
    public const string LegFR = "LegFR";
    public const string LegBL = "LegBL";
    public const string LegBR = "LegBR";

    /// <summary>브리핑 때만 쓰는 소품. 안경은 머리에, 지시봉은 앞다리(LegFL)에 붙는다. 평소엔 화면에 없다.</summary>
    public const string Glasses = "Glasses";
    public const string Pointer = "Pointer";

    /// <summary>부품이 돌 때의 축 위치(목, 엉덩이, 꼬리 밑동).</summary>
    /// <summary>서서 브리핑할 때 몸이 기울어 서는 축(엉덩이). 앞다리는 몸과 같이 기운다.</summary>
    public static readonly (float X, float Y, float Z) Hip = (-2.6f, 3.2f, 0f);

    public static readonly IReadOnlyDictionary<string, (float X, float Y, float Z)> Pivots =
        new Dictionary<string, (float, float, float)>
        {
            [Body] = (-0.5f, 3f, 0f),
            [Head] = (2f, 7f, 0f),
            [Tail] = (-3.8f, 5f, 0f),
            [LegFL] = (2.6f, 3.2f, 1.8f),
            [LegFR] = (2.6f, 3.2f, -1.8f),
            [LegBL] = (-2.6f, 3.2f, 1.8f),
            [LegBR] = (-2.6f, 3.2f, -1.8f),
        };

    /// <summary>화면에 맞출 때 쓰는 대략의 크기(가로 -15.0 ~ 11.3, 세로 0 ~ 15.5).</summary>
    public const float MinX = -15f, MaxX = 11.3f, MaxY = 15.5f;

    /// <summary>종별 색. 낮 종은 붉은 갈색, 저녁 종은 더 어둡고 짙다.</summary>
    public sealed record Palette(string Rust, string Ring, string Cream, string Dark, string Tip, string Leg, string Toe);

    public static Palette PaletteFor(string speciesId) => speciesId == "evening"
        ? new Palette("#A9452F", "#6E2A22", "#E9D6C6", "#1E1416", "#2C171A", "#3C2226", "#4A2E32")
        : new Palette("#C65A2A", "#8E3A1B", "#F4E6D0", "#2A1A16", "#3A1F17", "#4A2A1E", "#5C3A2C");

    /// <summary>
    /// 다리 하나: 다리(위) + 발(아래, 앞으로 뻗음) + 발가락 셋. 발끝이 뭉개지지 않게 세 덩어리로 나눈다.
    /// 네발 동물들이 같이 쓴다.
    /// </summary>
    internal static IEnumerable<VoxelBox> Leg(string part, string upper, string foot, string toes,
        float x0, float x1, float z0, float z1)
    {
        // 발은 다리보다 앞쪽으로 0.7 더 나오고 옆으로 0.1 넓다.
        var fx0 = x0 - 0.2f;
        var fx1 = x1 + 0.7f;
        var fz0 = z0 - 0.1f;
        var fz1 = z1 + 0.1f;

        yield return new(part, upper, x0, 1.4f, z0, x1, 3.4f, z1);
        yield return new(part, foot, fx0, 0f, fz0, fx1, 1.4f, fz1);

        const float gap = 0.1f;
        var toe = (fz1 - fz0 - 2 * gap) / 3f;
        for (var i = 0; i < 3; i++)
        {
            var tz0 = fz0 + i * (toe + gap);
            yield return new(part, toes, fx1, 0f, tz0, fx1 + 0.6f, 0.9f, tz0 + toe);
        }
    }

    /// <summary>네발 동물 공통: 다리 넷. 앞 둘은 몸과 같이 기울어 서면 팔이 된다.</summary>
    internal static IEnumerable<VoxelBox> Legs(string upper, string foot, string toes)
    {
        foreach (var b in Leg(LegFL, upper, foot, toes, 1.6f, 3.7f, 0.8f, 2.8f)) yield return b;
        foreach (var b in Leg(LegFR, upper, foot, toes, 1.6f, 3.7f, -2.8f, -0.8f)) yield return b;
        foreach (var b in Leg(LegBL, upper, foot, toes, -3.7f, -1.6f, 0.8f, 2.8f)) yield return b;
        foreach (var b in Leg(LegBR, upper, foot, toes, -3.7f, -1.6f, -2.8f, -0.8f)) yield return b;
    }

    /// <summary>
    /// 브리핑 소품(안경·지시봉). 네발 동물은 머리 크기·눈 위치·앞다리 위치가 같아서 그대로 쓴다.
    /// </summary>
    internal static IEnumerable<VoxelBox> BriefingProps()
    {
        const string frame = "#1B1B22";
        return new VoxelBox[]
        {
            // 안경: 검은 테 둘 + 코다리 + 다리
            new(Glasses, frame, 9.6f, 11.7f, 1.3f, 10f, 12.05f, 3.5f),
            new(Glasses, frame, 9.6f, 9.5f, 1.3f, 10f, 9.85f, 3.5f),
            new(Glasses, frame, 9.6f, 9.5f, 1.3f, 10f, 12.05f, 1.6f),
            new(Glasses, frame, 9.6f, 9.5f, 3.2f, 10f, 12.05f, 3.5f),
            new(Glasses, frame, 9.6f, 11.7f, -3.5f, 10f, 12.05f, -1.3f),
            new(Glasses, frame, 9.6f, 9.5f, -3.5f, 10f, 9.85f, -1.3f),
            new(Glasses, frame, 9.6f, 9.5f, -1.6f, 10f, 12.05f, -1.3f),
            new(Glasses, frame, 9.6f, 9.5f, -3.5f, 10f, 12.05f, -3.2f),
            new(Glasses, frame, 9.6f, 10.6f, -1.3f, 10f, 10.95f, 1.3f),
            new(Glasses, frame, 5.5f, 11.2f, 4f, 9.6f, 11.55f, 4.3f),
            new(Glasses, frame, 5.5f, 11.2f, -4.3f, 9.6f, 11.55f, -4f),

            // 지시봉: 손에서 아래로 길게 뻗은 막대. 앞다리와 같이 회전해서 가리킨다.
            new(Pointer, "#E9D28B", 2.35f, -6.4f, 1.5f, 2.95f, 0.6f, 2.1f),
            new(Pointer, "#E5484D", 2.15f, -7.4f, 1.3f, 3.15f, -6.4f, 2.3f),
        };
    }

    /// <summary>네발 동물 공통: 몸통 + 머리 바탕 + 눈(뜬/감은). 종마다 색과 눈 색만 다르다.</summary>
    internal static IEnumerable<VoxelBox> Skeleton(string body, string belly, string eye)
    {
        yield return new(Body, body, -4f, 3f, -2.8f, 3f, 8f, 2.8f);
        yield return new(Body, belly, 3f, 3.6f, -1.5f, 3.3f, 6.6f, 1.5f);
        yield return new(Head, body, 1f, 6.5f, -4f, 9f, 13.5f, 4f);

        yield return new(EyesOpen, eye, 9f, 10.2f, 1.8f, 9.5f, 11.6f, 3f);
        yield return new(EyesOpen, eye, 9f, 10.2f, -3f, 9.5f, 11.6f, -1.8f);
        yield return new(EyesOpen, "#FFFFFF", 9.5f, 11f, 2.1f, 9.7f, 11.6f, 2.5f);
        yield return new(EyesOpen, "#FFFFFF", 9.5f, 11f, -2.5f, 9.7f, 11.6f, -2.1f);
        yield return new(EyesClosed, eye, 9f, 10.6f, 1.8f, 9.4f, 11f, 3f);
        yield return new(EyesClosed, eye, 9f, 10.6f, -3f, 9.4f, 11f, -1.8f);
    }

    public static IReadOnlyList<VoxelBox> Boxes(string speciesId)
    {
        var c = PaletteFor(speciesId);
        var eye = "#120B0B";
        var list = new List<VoxelBox>
        {
            // 몸통 + 가슴 흰 무늬
            new(Body, c.Rust, -4f, 3f, -2.8f, 3f, 8f, 2.8f),
            new(Body, c.Cream, 3f, 3.6f, -1.5f, 3.3f, 6.6f, 1.5f),

            // 머리: 통통한 네모 + 주둥이 + 코 + 볼 털
            new(Head, c.Rust, 1f, 6.5f, -4f, 9f, 13.5f, 4f),
            new(Head, c.Cream, 9f, 7.5f, -2f, 10.5f, 10.5f, 2f),
            new(Head, c.Dark, 10.5f, 9.3f, -0.7f, 11.3f, 10.5f, 0.7f),
            new(Head, c.Cream, 6f, 7f, 4f, 9f, 9.5f, 4.8f),
            new(Head, c.Cream, 6f, 7f, -4.8f, 9f, 9.5f, -4f),
            // 눈물 자국(레서판다 특징)
            new(Head, c.Ring, 9f, 8.2f, 2.2f, 9.3f, 10.2f, 3f),
            new(Head, c.Ring, 9f, 8.2f, -3f, 9.3f, 10.2f, -2.2f),
            // 귀
            new(Head, c.Rust, 3.5f, 13.5f, 2.4f, 5.5f, 15.5f, 4.4f),
            new(Head, c.Rust, 3.5f, 13.5f, -4.4f, 5.5f, 15.5f, -2.4f),
            new(Head, c.Cream, 5.5f, 13.7f, 2.9f, 5.8f, 15f, 3.9f),
            new(Head, c.Cream, 5.5f, 13.7f, -3.9f, 5.8f, 15f, -2.9f),

            // 눈: 뜬 눈 / 감은 눈 (둘 중 하나만 보인다)
            new(EyesOpen, eye, 9f, 10.2f, 1.8f, 9.5f, 11.6f, 3f),
            new(EyesOpen, eye, 9f, 10.2f, -3f, 9.5f, 11.6f, -1.8f),
            new(EyesOpen, "#FFFFFF", 9.5f, 11f, 2.1f, 9.7f, 11.6f, 2.5f),
            new(EyesOpen, "#FFFFFF", 9.5f, 11f, -2.5f, 9.7f, 11.6f, -2.1f),
            new(EyesClosed, eye, 9f, 10.6f, 1.8f, 9.4f, 11f, 3f),
            new(EyesClosed, eye, 9f, 10.6f, -3f, 9.4f, 11f, -1.8f),

            // 다리: 위쪽은 짙은 갈색, 발은 검고 앞으로 뻗으며, 발가락 셋이 톡 튀어나온다.
            // (예전엔 검은 통짜라 발끝이 뭉개져 보였다)

            // 꼬리: 굵은 줄무늬가 위로 말려 올라간다
            new(Tail, c.Rust, -7f, 4.3f, -1.6f, -3.8f, 7.5f, 1.6f),
            new(Tail, c.Ring, -9.6f, 5.2f, -1.8f, -7f, 8.6f, 1.8f),
            new(Tail, c.Rust, -11.8f, 6.4f, -1.9f, -9.6f, 9.9f, 1.9f),
            new(Tail, c.Ring, -13.6f, 7.6f, -1.8f, -11.8f, 11f, 1.8f),
            new(Tail, c.Tip, -15f, 8.8f, -1.5f, -13.6f, 12f, 1.5f),
        };

        list.AddRange(Legs(c.Leg, c.Dark, c.Toe));
        list.AddRange(BriefingProps());

        return list;
    }
}
