using System.Windows.Controls;
using PortKiller.Core;

namespace PortKiller.App;

/// <summary>
/// 움직이는 동물 모델을 그리는 화면 부품의 공통 약속. 네발(<see cref="QuadrupedView"/>)과
/// 두 발(<see cref="BipedView"/>)이 같은 약속을 따르고, 펫 창은 종의 몸 틀에 맞는 쪽을 골라 쓴다.
/// </summary>
public abstract class AnimalViewBase : Viewport3D
{
    /// <summary>종·색 변형·성장 단계에 맞는 모델로 맞춘다. 같은 값이면 아무것도 다시 만들지 않는다.</summary>
    public abstract void Configure(string kindId, string speciesId, LifeStage stage);

    /// <summary>지금 포즈에 맞춰 부품을 움직인다. <paramref name="seconds"/> 는 앱이 켜진 뒤 흐른 시간이다.</summary>
    public abstract void Animate(PetPose pose, bool walking, double seconds);

    /// <summary>브리핑 자세(안경을 쓰고 서서 지시봉을 든다)를 켜고 끈다.</summary>
    public abstract void SetBriefing(bool on);

    /// <summary>
    /// 지시봉이 가리킬 방향(도). 몸이 보는 쪽이 0°, 위가 90°, 아래가 -90° 다. null 이면 가리키는 곳이 없다.
    /// </summary>
    public abstract void PointAt(double? angleDegrees);

    /// <summary>0(평소 자세)~1(브리핑 자세 완료). 브리핑을 끝내고 다 돌아왔는지 알 때 쓴다.</summary>
    public abstract double BriefingBlend { get; }
}
