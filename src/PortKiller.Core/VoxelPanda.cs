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

    /// <summary>부품이 돌 때의 축 위치(목, 엉덩이, 꼬리 밑동).</summary>
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
    public sealed record Palette(string Rust, string Ring, string Cream, string Dark, string Tip);

    public static Palette PaletteFor(string speciesId) => speciesId == "evening"
        ? new Palette("#A9452F", "#6E2A22", "#E9D6C6", "#1E1416", "#2C171A")
        : new Palette("#C65A2A", "#8E3A1B", "#F4E6D0", "#2A1A16", "#3A1F17");

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

            // 다리(검은색): 앞 둘, 뒤 둘
            new(LegFL, c.Dark, 1.5f, 0f, 0.8f, 3.8f, 3.4f, 2.8f),
            new(LegFR, c.Dark, 1.5f, 0f, -2.8f, 3.8f, 3.4f, -0.8f),
            new(LegBL, c.Dark, -3.8f, 0f, 0.8f, -1.5f, 3.4f, 2.8f),
            new(LegBR, c.Dark, -3.8f, 0f, -2.8f, -1.5f, 3.4f, -0.8f),

            // 꼬리: 굵은 줄무늬가 위로 말려 올라간다
            new(Tail, c.Rust, -7f, 4.3f, -1.6f, -3.8f, 7.5f, 1.6f),
            new(Tail, c.Ring, -9.6f, 5.2f, -1.8f, -7f, 8.6f, 1.8f),
            new(Tail, c.Rust, -11.8f, 6.4f, -1.9f, -9.6f, 9.9f, 1.9f),
            new(Tail, c.Ring, -13.6f, 7.6f, -1.8f, -11.8f, 11f, 1.8f),
            new(Tail, c.Tip, -15f, 8.8f, -1.5f, -13.6f, 12f, 1.5f),
        };

        return list;
    }
}
