namespace PortKiller.Core;

/// <summary>
/// 펫 말고 화면에 놓이는 것들(알·응아)의 복셀 설계도. <see cref="VoxelPanda"/> 와 같은 좌표계·같은 단위다.
/// 그림 파일이 아니라 숫자라서, 파일이 빠지거나 깨질 일이 없다.
/// </summary>
public static class VoxelProps
{
    public const string Part = "Prop";

    /// <summary>알이 화면에 차지하는 크기(가로 ±5.8, 세로 0~14.5).</summary>
    public const float EggHalfWidth = 5.8f, EggHeight = 14.5f;

    /// <summary>응아 크기(가로 ±4, 세로 0~6).</summary>
    public const float PoopHalfWidth = 4f, PoopHeight = 6f;

    /// <summary>
    /// 알. 층을 쌓아 달걀 모양을 만들고 얼룩을 얹는다. <paramref name="cracked"/> 가 참이면 금이 간 모습(부화 직전).
    /// </summary>
    /// <param name="cracked">금이 간 모습(부화 직전).</param>
    /// <param name="spot">얼룩 색. 종마다 다르다(<see cref="VoxelAnimals.EggSpot"/>).</param>
    public static IReadOnlyList<VoxelBox> Egg(bool cracked, string spot = "#C65A2A")
    {
        const string shell = "#F3E7CF", shade = "#E2D2B2", crack = "#4A2E22";
        var b = new List<VoxelBox>
        {
            new(Part, shade, -3f, 0f, -3f, 3f, 1.5f, 3f),
            new(Part, shell, -4.5f, 1.5f, -4.5f, 4.5f, 4f, 4.5f),
            new(Part, shell, -5.5f, 4f, -5.5f, 5.5f, 9f, 5.5f),
            new(Part, shell, -4.5f, 9f, -4.5f, 4.5f, 11.5f, 4.5f),
            new(Part, shell, -3f, 11.5f, -3f, 3f, 13.5f, 3f),
            new(Part, shell, -1.5f, 13.5f, -1.5f, 1.5f, 14.5f, 1.5f),

            // 얼룩(레서판다 색). 껍질 밖으로 살짝 튀어나와 도트 무늬처럼 보인다.
            new(Part, spot, -3.5f, 5f, 5.5f, -1.5f, 7f, 5.7f),
            new(Part, spot, 1f, 7f, 5.5f, 3.5f, 8.5f, 5.7f),
            new(Part, spot, 5.5f, 5.5f, -1f, 5.7f, 7.5f, 1.5f),
            new(Part, spot, -5.7f, 6.5f, -3f, -5.5f, 8.5f, -0.5f),
            new(Part, spot, -1.5f, 10f, 4.5f, 0.5f, 11.2f, 4.7f),
        };

        if (cracked)
        {
            // 앞면에 지그재그 금
            b.Add(new(Part, crack, -3.5f, 7.5f, 5.5f, -2f, 8.7f, 5.8f));
            b.Add(new(Part, crack, -2f, 6.4f, 5.5f, -0.5f, 7.6f, 5.8f));
            b.Add(new(Part, crack, -0.5f, 7.5f, 5.5f, 1f, 8.7f, 5.8f));
            b.Add(new(Part, crack, 1f, 6.4f, 5.5f, 2.5f, 7.6f, 5.8f));
            b.Add(new(Part, crack, -1f, 8.7f, 4.5f, 0.5f, 9.6f, 4.8f));
        }

        return b;
    }

    /// <summary>새끼 바구니가 화면에 차지하는 크기(가로 ±6.4, 세로 0~10.2).</summary>
    public const float CubHalfWidth = 6.4f, CubHeight = 10.2f;

    /// <summary>
    /// 새끼형 종의 갓 태어난 모습: 대나무 바구니 속에서 웅크려 자는 새끼.
    /// <paramref name="awake"/> 가 참이면 눈을 반쯤 뜨고 귀가 쫑긋한다(깨기 직전).
    /// 몸 색은 종마다 다르다(<see cref="VoxelAnimals.CubColors"/>).
    /// </summary>
    public static IReadOnlyList<VoxelBox> Cub(string kindId, bool awake)
    {
        var (body, belly, accent) = VoxelAnimals.CubColors(kindId);
        const string weaveA = "#B07A3C", weaveB = "#8E5E2B", rim = "#C9954F", blanket = "#F6E7C8", eye = "#120B0B";

        var b = new List<VoxelBox>
        {
            // 바구니: 바닥 + 엮은 벽 3층 + 윗테
            new(Part, weaveB, -6f, 0f, -6f, 6f, 1f, 6f),
            new(Part, weaveA, -6f, 1f, 5f, 6f, 2.4f, 6f),
            new(Part, weaveA, -6f, 1f, -6f, 6f, 2.4f, -5f),
            new(Part, weaveA, 5f, 1f, -5f, 6f, 2.4f, 5f),
            new(Part, weaveA, -6f, 1f, -5f, -5f, 2.4f, 5f),
            new(Part, weaveB, -6f, 2.4f, 5f, 6f, 3.8f, 6f),
            new(Part, weaveB, -6f, 2.4f, -6f, 6f, 3.8f, -5f),
            new(Part, weaveB, 5f, 2.4f, -5f, 6f, 3.8f, 5f),
            new(Part, weaveB, -6f, 2.4f, -5f, -5f, 3.8f, 5f),
            new(Part, rim, -6.2f, 3.8f, -6.2f, 6.2f, 4.6f, 6.2f),
            // 안쪽 담요
            new(Part, blanket, -5f, 1f, -5f, 5f, 3.4f, 5f),

            // 새끼: 웅크린 몸 + 머리 + 귀 + 꼬리
            new(Part, body, -3.6f, 3.4f, -3f, 3.2f, 7.2f, 3f),
            new(Part, belly, 3.2f, 3.8f, -1.6f, 3.5f, 6f, 1.6f),
            new(Part, body, 1.6f, 4f, -2.6f, 5.6f, 8f, 2.6f),
            new(Part, belly, 5.6f, 4.6f, -1.2f, 6.2f, 6.4f, 1.2f),
            new(Part, eye, 5.6f, 5.2f, -0.45f, 6.4f, 5.9f, 0.45f),
            new(Part, accent, -5f, 3.4f, -1.4f, -3.6f, 5.4f, 1.4f),
            new(Part, body, 2f, 8f, 1.2f, 3.4f, awake ? 10.2f : 9.2f, 2.6f),
            new(Part, body, 2f, 8f, -2.6f, 3.4f, awake ? 10.2f : 9.2f, -1.2f),
        };

        // 눈: 잘 땐 얇은 줄, 깨기 직전엔 반쯤 뜬 눈
        if (awake)
        {
            b.Add(new(Part, eye, 5.6f, 6.2f, 0.9f, 6.2f, 7.4f, 1.9f));
            b.Add(new(Part, eye, 5.6f, 6.2f, -1.9f, 6.2f, 7.4f, -0.9f));
        }
        else
        {
            b.Add(new(Part, eye, 5.6f, 6.6f, 0.9f, 6.2f, 7f, 1.9f));
            b.Add(new(Part, eye, 5.6f, 6.6f, -1.9f, 6.2f, 7f, -0.9f));
        }

        return b;
    }

    /// <summary>응아. 소용돌이 모양 3층, 앞면에 까만 눈 두 개(치우기 싫게 귀엽게).</summary>
    public static IReadOnlyList<VoxelBox> Poop() => new List<VoxelBox>
    {
        new(Part, "#7A4A25", -4f, 0f, -4f, 4f, 2f, 4f),
        new(Part, "#8D5A2E", -3f, 2f, -3f, 3f, 4f, 3f),
        new(Part, "#A06A38", -1.8f, 4f, -1.8f, 1.8f, 5.4f, 1.8f),
        new(Part, "#B77F49", -0.6f, 5.4f, -0.6f, 0.6f, 6f, 0.6f),
        new(Part, "#120B0B", -1.8f, 2.4f, 3f, -0.8f, 3.4f, 3.2f),
        new(Part, "#120B0B", 0.8f, 2.4f, 3f, 1.8f, 3.4f, 3.2f),
    };
}
