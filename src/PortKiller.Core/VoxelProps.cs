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
    public static IReadOnlyList<VoxelBox> Egg(bool cracked)
    {
        const string shell = "#F3E7CF", spot = "#C65A2A", shade = "#E2D2B2", crack = "#4A2E22";
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
