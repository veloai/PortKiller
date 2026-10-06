namespace PortKiller.Core;

/// <summary>
/// 종 id 로 모델 설계도를 찾아 주는 곳. 화면은 종 이름을 몰라도 여기서 상자 목록과 크기를 받는다.
/// </summary>
public static class VoxelAnimals
{
    /// <summary>다 자란(걷는) 모습의 상자 목록. 모르는 종은 레서판다.</summary>
    public static IReadOnlyList<VoxelBox> Boxes(string kindId, string variant) => kindId switch
    {
        PetKinds.Cat => VoxelQuadrupeds.Cat(variant),
        PetKinds.Fox => VoxelQuadrupeds.Fox(variant),
        PetKinds.Chick => VoxelChick.Boxes(variant),
        _ => VoxelPanda.Boxes(variant),
    };

    /// <summary>모델이 차지하는 가로 범위와 높이. 카메라를 맞출 때 쓴다.</summary>
    public static (float MinX, float MaxX, float MaxY) Bounds(string kindId) => kindId switch
    {
        PetKinds.Chick => (VoxelChick.MinX, VoxelChick.MaxX, VoxelChick.MaxY),
        PetKinds.Cat => (-12.2f, 11.3f, 15.5f),
        _ => (VoxelPanda.MinX, VoxelPanda.MaxX, VoxelPanda.MaxY),
    };

    /// <summary>색 변형 이름. 같은 종 안에서 "언제 깨어났나"로 갈린다(저녁에 깨면 evening, 나머지는 day 모습).</summary>
    public static string VariantOf(string speciesId) => speciesId == "evening" ? "evening" : "day";

    /// <summary>새끼형 종의 모습이 되는 몸 색(갓 태어난 새끼를 칠할 때 쓴다): 몸, 배, 포인트.</summary>
    public static (string Body, string Belly, string Accent) CubColors(string kindId) => kindId switch
    {
        PetKinds.Cat => ("#E0883A", "#FFF1DC", "#A85A1E"),
        PetKinds.Fox => ("#E8721E", "#FFF3E6", "#2A1A14"),
        _ => ("#C65A2A", "#F4E6D0", "#8E3A1B"),
    };

    /// <summary>알형 종의 알 얼룩 색.</summary>
    public static string EggSpot(string kindId) => kindId == PetKinds.Chick ? "#E0A53A" : "#C65A2A";
}
