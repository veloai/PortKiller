namespace PortKiller.Core;

/// <summary>
/// 고양이·여우의 설계도. 레서판다와 <b>같은 몸 틀</b>(몸통·머리 크기, 눈·다리 자리, 꼬리 밑동)을 쓴다 -
/// 그래서 걷기·일어서기·브리핑 동작과 안경·지시봉을 그대로 공유하고, 다른 건 색·귀·주둥이·꼬리 모양뿐이다.
/// </summary>
public static class VoxelQuadrupeds
{
    // ---------- 고양이 ----------

    /// <summary>고양이: 낮은 주황 얼룩, 저녁은 회색. 뾰족한 귀, 수염, 이마 줄무늬, 가늘고 긴 꼬리, 하얀 양말.</summary>
    public static IReadOnlyList<VoxelBox> Cat(string variant)
    {
        var evening = variant == "evening";
        string body = evening ? "#8C95A3" : "#E0883A";
        string belly = evening ? "#E8ECF2" : "#FFF1DC";
        string stripe = evening ? "#5E6672" : "#A85A1E";
        string legUpper = evening ? "#7A8391" : "#CF7A2E";
        string sock = evening ? "#EEF1F6" : "#FFF1DC";
        string toes = evening ? "#D5DBE4" : "#F0DCC0";
        string tip = evening ? "#4A515C" : "#8A4A1A";
        const string pink = "#E8908A";
        const string dark = "#2A1E18";

        var list = new List<VoxelBox>(VoxelPanda.Skeleton(body, belly, "#1A120C"));

        // 얼굴: 작고 하얀 주둥이, 분홍 코, 수염
        list.Add(new(VoxelPanda.Head, belly, 9f, 7.6f, -1.6f, 9.9f, 10.2f, 1.6f));
        list.Add(new(VoxelPanda.Head, pink, 9.9f, 9.2f, -0.6f, 10.5f, 10.1f, 0.6f));
        list.Add(new(VoxelPanda.Head, dark, 9.5f, 8.2f, -0.25f, 9.95f, 8.6f, 0.25f));
        foreach (var zSign in new[] { 1f, -1f })
        {
            list.Add(new(VoxelPanda.Head, belly, 8.4f, 8.9f, zSign > 0 ? 4f : -5.6f, 9.1f, 9.2f, zSign > 0 ? 5.6f : -4f));
            list.Add(new(VoxelPanda.Head, belly, 8.4f, 8.3f, zSign > 0 ? 4f : -5.6f, 9.1f, 8.6f, zSign > 0 ? 5.6f : -4f));
        }

        // 귀: 뾰족하고 안쪽이 분홍
        list.Add(new(VoxelPanda.Head, body, 3.8f, 13.5f, 2.2f, 5.4f, 15.5f, 4f));
        list.Add(new(VoxelPanda.Head, body, 3.8f, 13.5f, -4f, 5.4f, 15.5f, -2.2f));
        list.Add(new(VoxelPanda.Head, pink, 5.4f, 13.7f, 2.7f, 5.7f, 15f, 3.5f));
        list.Add(new(VoxelPanda.Head, pink, 5.4f, 13.7f, -3.5f, 5.7f, 15f, -2.7f));

        // 이마와 몸 줄무늬(얼룩 고양이)
        list.Add(new(VoxelPanda.Head, stripe, 4f, 13.5f, -0.5f, 7f, 13.8f, 0.5f));
        list.Add(new(VoxelPanda.Head, stripe, 4.5f, 13.5f, 1.2f, 6.5f, 13.8f, 2f));
        list.Add(new(VoxelPanda.Head, stripe, 4.5f, 13.5f, -2f, 6.5f, 13.8f, -1.2f));
        foreach (var x in new[] { -3f, -1f, 1f })
        {
            list.Add(new(VoxelPanda.Body, stripe, x, 4f, 2.8f, x + 0.7f, 7.4f, 3f));
            list.Add(new(VoxelPanda.Body, stripe, x, 4f, -3f, x + 0.7f, 7.4f, -2.8f));
        }

        list.AddRange(VoxelPanda.Legs(legUpper, sock, toes));
        list.AddRange(VoxelPanda.BriefingProps());

        // 꼬리: 가늘고 길게 위로 휘어 오른다
        list.Add(new(VoxelPanda.Tail, body, -7f, 4f, -0.7f, -3.8f, 5.6f, 0.7f));
        list.Add(new(VoxelPanda.Tail, stripe, -9.4f, 4.6f, -0.7f, -7f, 6.4f, 0.7f));
        list.Add(new(VoxelPanda.Tail, body, -11f, 6f, -0.7f, -9.4f, 7.8f, 0.7f));
        list.Add(new(VoxelPanda.Tail, stripe, -12.2f, 7.8f, -0.7f, -10.6f, 10f, 0.7f));
        list.Add(new(VoxelPanda.Tail, tip, -12.2f, 10f, -0.7f, -10.6f, 12.4f, 0.7f));
        return list;
    }

    // ---------- 여우 ----------

    /// <summary>여우: 주황 털에 하얀 볼과 가슴, 검은 양말, 뾰족한 긴 주둥이, 끝이 하얀 풍성한 꼬리. 저녁은 더 붉다.</summary>
    public static IReadOnlyList<VoxelBox> Fox(string variant)
    {
        var evening = variant == "evening";
        string body = evening ? "#B8481B" : "#E8721E";
        string belly = evening ? "#F0E0D0" : "#FFF3E6";
        string legUpper = evening ? "#A33F18" : "#D9651A";
        const string dark = "#1E1512";
        const string earBack = "#2A1A14";
        const string toes = "#2C1D18";
        string tailTip = "#FFF6EC";

        var list = new List<VoxelBox>(VoxelPanda.Skeleton(body, belly, "#120B0B"));

        // 얼굴: 하얀 볼과 길쭉하게 뾰족한 주둥이
        list.Add(new(VoxelPanda.Head, belly, 6.5f, 6.8f, 4f, 9f, 10f, 4.8f));
        list.Add(new(VoxelPanda.Head, belly, 6.5f, 6.8f, -4.8f, 9f, 10f, -4f));
        list.Add(new(VoxelPanda.Head, belly, 9f, 7.4f, -2f, 10.2f, 10.2f, 2f));
        list.Add(new(VoxelPanda.Head, belly, 10.2f, 8.2f, -1.2f, 11f, 9.9f, 1.2f));
        list.Add(new(VoxelPanda.Head, dark, 11f, 8.9f, -0.6f, 11.3f, 9.9f, 0.6f));

        // 귀: 크고 뾰족하며 뒤와 끝이 검다
        list.Add(new(VoxelPanda.Head, body, 3.5f, 13.5f, 2f, 5.3f, 15.5f, 4f));
        list.Add(new(VoxelPanda.Head, body, 3.5f, 13.5f, -4f, 5.3f, 15.5f, -2f));
        list.Add(new(VoxelPanda.Head, earBack, 3.8f, 14.6f, 2.4f, 5f, 15.5f, 3.6f));
        list.Add(new(VoxelPanda.Head, earBack, 3.8f, 14.6f, -3.6f, 5f, 15.5f, -2.4f));
        list.Add(new(VoxelPanda.Head, belly, 5.3f, 13.8f, 2.6f, 5.6f, 14.8f, 3.4f));
        list.Add(new(VoxelPanda.Head, belly, 5.3f, 13.8f, -3.4f, 5.6f, 14.8f, -2.6f));

        // 가슴 하얀 털을 조금 더 넓게
        list.Add(new(VoxelPanda.Body, belly, 3f, 3.6f, -1.8f, 3.4f, 6.8f, 1.8f));

        list.AddRange(VoxelPanda.Legs(legUpper, dark, toes));
        list.AddRange(VoxelPanda.BriefingProps());

        // 꼬리: 풍성하게 뒤로 쭉, 끝은 하얗다
        list.Add(new(VoxelPanda.Tail, body, -7.5f, 3.8f, -2f, -3.8f, 7.4f, 2f));
        list.Add(new(VoxelPanda.Tail, body, -10.5f, 4.2f, -2.4f, -7.5f, 8.2f, 2.4f));
        list.Add(new(VoxelPanda.Tail, body, -13.3f, 4.4f, -2.4f, -10.5f, 8.4f, 2.4f));
        list.Add(new(VoxelPanda.Tail, tailTip, -15f, 4.4f, -2f, -13.3f, 8.2f, 2f));
        return list;
    }
}
