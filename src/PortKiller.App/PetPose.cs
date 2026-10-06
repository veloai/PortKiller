namespace PortKiller.App;

/// <summary>펫이 취할 수 있는 포즈. <see cref="QuadrupedView"/>·<see cref="BipedView"/> 가 포즈마다 다른 몸짓을 한다.</summary>
public enum PetPose
{
    Idle,
    Walk1,
    Walk2,
    Eat,
    Happy,
    Sick,
    Sleep,
    Sulk,
}
