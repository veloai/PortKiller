namespace PortKiller.Core;

/// <summary>
/// 병아리의 설계도. 두 발로 서는 새라서 네발 동물과 <b>다른 몸 틀</b>(부품 이름도 다르다)을 쓴다.
/// 몸이 +X 쪽을 보고 Y 가 위, Z 가 좌우인 것은 같다.
/// </summary>
public static class VoxelChick
{
    public const string Body = "Body";
    public const string Head = "Head";
    public const string EyesOpen = "EyesOpen";
    public const string EyesClosed = "EyesClosed";
    public const string WingL = "WingL";
    public const string WingR = "WingR";
    public const string LegL = "LegL";
    public const string LegR = "LegR";

    /// <summary>브리핑 소품. 안경은 머리, 지시봉은 왼쪽 날개(WingL)에 붙는다.</summary>
    public const string Glasses = "Glasses";
    public const string Pointer = "Pointer";

    public static readonly IReadOnlyDictionary<string, (float X, float Y, float Z)> Pivots =
        new Dictionary<string, (float, float, float)>
        {
            [Body] = (0f, 3f, 0f),
            [Head] = (1.5f, 8.5f, 0f),
            [WingL] = (0f, 8f, 3.6f),
            [WingR] = (0f, 8f, -3.6f),
            [LegL] = (0f, 3.2f, 1.9f),
            [LegR] = (0f, 3.2f, -1.9f),
        };

    public const float MinX = -5.6f, MaxX = 8.6f, MaxY = 16f;

    /// <summary>낮은 노란 병아리, 저녁은 조금 더 주황빛이 도는 병아리.</summary>
    public static IReadOnlyList<VoxelBox> Boxes(string variant)
    {
        var evening = variant == "evening";
        string body = evening ? "#F2B134" : "#FFDD55";
        string soft = evening ? "#FFE3A0" : "#FFF0A8";
        string wing = evening ? "#E0982A" : "#F4C430";
        string crest = evening ? "#D88A22" : "#F2B707";
        const string beak = "#F08A24";
        const string leg = "#F08A24";
        const string cheek = "#FF9AA2";
        const string eye = "#120B0B";
        const string frame = "#1B1B22";

        return new List<VoxelBox>
        {
            // 몸통: 동글동글 + 배 + 꽁지 깃
            new(Body, body, -3.5f, 3f, -2.5f, 3.5f, 4.2f, 2.5f),
            new(Body, body, -4.5f, 4.2f, -3.5f, 4.5f, 9f, 3.5f),
            new(Body, soft, 4.5f, 4.8f, -2f, 4.8f, 8f, 2f),
            new(Body, wing, -5.6f, 6f, -1f, -4.5f, 8.2f, 1f),

            // 머리: 큰 동그란 머리, 정수리 깃, 부리, 볼
            new(Head, body, -1.5f, 8.5f, -3.8f, 6.5f, 14.5f, 3.8f),
            new(Head, crest, 0.5f, 14.5f, -0.8f, 2.5f, 16f, 0.8f),
            new(Head, beak, 6.5f, 10f, -1.2f, 8.6f, 11.8f, 1.2f),
            new(Head, beak, 6.5f, 9.4f, -0.9f, 8f, 10f, 0.9f),
            new(Head, cheek, 5.2f, 9.6f, 3.8f, 6.5f, 11f, 4.2f),
            new(Head, cheek, 5.2f, 9.6f, -4.2f, 6.5f, 11f, -3.8f),

            new(EyesOpen, eye, 6.5f, 11.8f, 1.9f, 7f, 13.2f, 3f),
            new(EyesOpen, eye, 6.5f, 11.8f, -3f, 7f, 13.2f, -1.9f),
            new(EyesOpen, "#FFFFFF", 7f, 12.6f, 2.2f, 7.2f, 13.2f, 2.6f),
            new(EyesOpen, "#FFFFFF", 7f, 12.6f, -2.6f, 7.2f, 13.2f, -2.2f),
            new(EyesClosed, eye, 6.5f, 12.2f, 1.9f, 6.9f, 12.6f, 3f),
            new(EyesClosed, eye, 6.5f, 12.2f, -3f, 6.9f, 12.6f, -1.9f),

            // 날개: 몸 옆에 늘어뜨린다(브리핑 때 왼쪽이 지시봉을 든다)
            new(WingL, wing, -2f, 4.5f, 3.5f, 2f, 8f, 4.6f),
            new(WingR, wing, -2f, 4.5f, -4.6f, 2f, 8f, -3.5f),

            // 다리: 가는 주황 다리 + 발
            new(LegL, leg, -1f, 0.8f, 1.3f, 0.8f, 3.2f, 2.5f),
            new(LegL, leg, -1f, 0f, 1.1f, 2.4f, 0.8f, 2.7f),
            new(LegR, leg, -1f, 0.8f, -2.5f, 0.8f, 3.2f, -1.3f),
            new(LegR, leg, -1f, 0f, -2.7f, 2.4f, 0.8f, -1.1f),

            // 안경(브리핑 소품)
            new(Glasses, frame, 7f, 13.2f, 1.6f, 7.4f, 13.5f, 3.4f),
            new(Glasses, frame, 7f, 11.5f, 1.6f, 7.4f, 11.8f, 3.4f),
            new(Glasses, frame, 7f, 11.5f, 1.6f, 7.4f, 13.5f, 1.9f),
            new(Glasses, frame, 7f, 11.5f, 3.1f, 7.4f, 13.5f, 3.4f),
            new(Glasses, frame, 7f, 13.2f, -3.4f, 7.4f, 13.5f, -1.6f),
            new(Glasses, frame, 7f, 11.5f, -3.4f, 7.4f, 11.8f, -1.6f),
            new(Glasses, frame, 7f, 11.5f, -1.9f, 7.4f, 13.5f, -1.6f),
            new(Glasses, frame, 7f, 11.5f, -3.4f, 7.4f, 13.5f, -3.1f),
            new(Glasses, frame, 7f, 12.3f, -1.6f, 7.4f, 12.6f, 1.6f),
            new(Glasses, frame, 1f, 12.4f, 3.8f, 7f, 12.7f, 4.1f),
            new(Glasses, frame, 1f, 12.4f, -4.1f, 7f, 12.7f, -3.8f),

            // 지시봉(브리핑 소품): 날개 끝에서 아래로 길게
            new(Pointer, "#E9D28B", -0.3f, -4.5f, 4.6f, 0.3f, 5f, 5.2f),
            new(Pointer, "#E5484D", -0.5f, -5.6f, 4.4f, 0.5f, -4.5f, 5.4f),
        };
    }
}
