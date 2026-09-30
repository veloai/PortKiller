using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using PortKiller.Core;

namespace PortKiller.App;

/// <summary>
/// 3D 도트 레서판다를 그리고 움직인다. 게임 규칙은 모르고 "지금 어떤 포즈인가"만 받는다.
///
/// <para>부품마다 <see cref="Model3DGroup"/> 을 하나씩 두고, 매 프레임 회전·이동 값만 바꾼다.
/// 모양(상자)은 한 번만 만든다 — 초당 60번 불리므로 새로 만들 이유가 없다.</para>
/// </summary>
public sealed class PandaView : Viewport3D
{
    /// <summary>화면에 잡히는 세로·가로 길이(복셀 단위). 몸 크기(-15~10.5)와 회전 여유를 합친 값이다.</summary>
    private const double ViewUnits = 30;

    private const double CenterX = (VoxelPanda.MinX + VoxelPanda.MaxX) / 2;

    private readonly Dictionary<string, Model3DGroup> _parts = new();
    private readonly Dictionary<string, AxisAngleRotation3D> _rot = new();
    private readonly Dictionary<string, TranslateTransform3D> _move = new();
    private readonly ScaleTransform3D _stageScale = new(1, 1, 1, CenterX, 0, 0);
    private readonly ScaleTransform3D _squash = new(1, 1, 1, CenterX, 0, 0);
    private readonly TranslateTransform3D _rigMove = new();
    private readonly Model3DGroup _eyesOpen = new();
    private readonly Model3DGroup _eyesClosed = new();
    private readonly ModelVisual3D _visual = new();

    private string? _builtFor;

    public PandaView()
    {
        // 몸이 화면 한가운데 오도록 직교 카메라(원근 없음 - 도트 느낌이 산다).
        Camera = new OrthographicCamera(new Point3D(CenterX, 13.5, 40), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), ViewUnits);
        ClipToBounds = true;
        IsHitTestVisible = false;   // 눌림은 바깥 PetBody 가 받는다
        Children.Add(_visual);
    }

    /// <summary>종·성장 단계가 바뀌었을 때만 다시 만든다.</summary>
    public void Configure(string speciesId, LifeStage stage)
    {
        var key = speciesId == "evening" ? "evening" : "day";
        if (_builtFor != key) Build(key);

        var s = stage switch { LifeStage.Baby => 0.62, LifeStage.Child => 0.82, _ => 1.0 };
        _stageScale.ScaleX = _stageScale.ScaleY = _stageScale.ScaleZ = s;
    }

    private void Build(string species)
    {
        _builtFor = species;
        _parts.Clear();
        _rot.Clear();
        _move.Clear();

        var rig = new Model3DGroup();
        foreach (var light in VoxelMeshBuilder.Lights()) rig.Children.Add(light);

        var body = new Model3DGroup();
        foreach (var part in new[] { VoxelPanda.Body })
            AddPart(body, part, species);

        // 몸 위에 얹히는 부품들은 몸 그룹 안에 넣어, 몸이 출렁이면 같이 움직이게 한다.
        var head = MakeGroup(VoxelPanda.Head, species);
        FillEyes(species);
        head.Children.Add(_eyesOpen);       // 처음엔 뜬 눈. 감은 눈은 Animate 가 바꿔 끼운다
        _lastEyesClosed = false;
        body.Children.Add(head);
        body.Children.Add(MakeGroup(VoxelPanda.Tail, species));
        foreach (var leg in new[] { VoxelPanda.LegFL, VoxelPanda.LegFR, VoxelPanda.LegBL, VoxelPanda.LegBR })
            rig.Children.Add(MakeGroup(leg, species));

        rig.Children.Add(body);
        _parts[VoxelPanda.Body] = body;

        // 몸 전체: 단계 크기 -> 눌림 -> 위아래 -> 보는 각도(3/4 시점)
        var all = new Transform3DGroup();
        all.Children.Add(_stageScale);
        all.Children.Add(_squash);
        all.Children.Add(_rigMove);
        all.Children.Add(VoxelMeshBuilder.ThreeQuarterView(CenterX, 6));
        rig.Transform = all;

        _visual.Content = rig;
    }

    private Model3DGroup MakeGroup(string part, string species)
    {
        var g = new Model3DGroup();
        AddPart(g, part, species);

        var pivot = VoxelPanda.Pivots[part];
        var rot = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
        var t = new Transform3DGroup();
        t.Children.Add(new RotateTransform3D(rot, pivot.X, pivot.Y, pivot.Z));
        var move = new TranslateTransform3D();
        t.Children.Add(move);
        g.Transform = t;

        _parts[part] = g;
        _rot[part] = rot;
        _move[part] = move;
        return g;
    }

    private static void AddPart(Model3DGroup group, string part, string species)
    {
        // 같은 색 상자끼리 한 메시로 묶는다(재질 수 = 색 수).
        foreach (var byColor in VoxelPanda.Boxes(species).Where(b => b.Part == part).GroupBy(b => b.Color))
            group.Children.Add(VoxelMeshBuilder.BuildMesh(byColor, byColor.Key));
    }

    private void FillEyes(string species)
    {
        _eyesOpen.Children.Clear();
        _eyesClosed.Children.Clear();
        foreach (var g in VoxelPanda.Boxes(species).Where(b => b.Part == VoxelPanda.EyesOpen).GroupBy(b => b.Color))
            _eyesOpen.Children.Add(VoxelMeshBuilder.BuildMesh(g, g.Key));
        foreach (var g in VoxelPanda.Boxes(species).Where(b => b.Part == VoxelPanda.EyesClosed).GroupBy(b => b.Color))
            _eyesClosed.Children.Add(VoxelMeshBuilder.BuildMesh(g, g.Key));
    }

    /// <summary>
    /// 지금 포즈에 맞춰 부품을 움직인다. <paramref name="seconds"/> 는 앱이 켜진 뒤 흐른 시간이다.
    /// 걸을 때는 <paramref name="walking"/> 이 참이고, 그때 다리가 번갈아 앞뒤로 흔들린다.
    /// </summary>
    public void Animate(PetPose pose, bool walking, double seconds)
    {
        if (_builtFor is null) return;

        var t = seconds;
        double legSwing = 0, bob = 0, headNod = 0, headTurn = 0, tailSway = Math.Sin(t * 2.0) * 8, tailDroop = 0;
        var squash = 1.0;
        var eyesClosed = false;

        if (walking)
        {
            legSwing = Math.Sin(t * 12) * 30;
            bob = Math.Abs(Math.Sin(t * 12)) * 0.5;
            tailSway = Math.Sin(t * 6) * 12;
        }

        switch (pose)
        {
            case PetPose.Happy:
                bob = Math.Abs(Math.Sin(t * 10)) * 1.6;
                tailSway = Math.Sin(t * 16) * 26;
                headNod = Math.Sin(t * 10) * 6;
                break;
            case PetPose.Eat:
                headNod = -14 + Math.Sin(t * 14) * 12;
                break;
            case PetPose.Sick:
                headNod = -22;
                tailDroop = -22;
                bob = Math.Sin(t * 3) * 0.15;
                legSwing = 0;
                break;
            case PetPose.Sleep:
                headNod = -28;
                tailDroop = -12;
                squash = 0.78 + Math.Sin(t * 1.6) * 0.02;   // 숨쉬는 것처럼 살짝 부푼다
                eyesClosed = true;
                legSwing = 0;
                bob = 0;
                break;
            case PetPose.Sulk:
                headTurn = 42;
                headNod = -8;
                tailDroop = -18;
                break;
            default:
                headNod = Math.Sin(t * 2.2) * 2.5;   // 가만히 있어도 살아 있는 느낌
                bob = Math.Sin(t * 2.2) * 0.12;
                break;
        }

        // 다리: 앞다리 왼쪽·뒷다리 오른쪽이 한 쌍으로 움직인다(네 발 걸음)
        _rot[VoxelPanda.LegFL].Angle = legSwing;
        _rot[VoxelPanda.LegBR].Angle = legSwing;
        _rot[VoxelPanda.LegFR].Angle = -legSwing;
        _rot[VoxelPanda.LegBL].Angle = -legSwing;

        // 머리: 끄덕임은 Z 축, 고개 돌리기는 Y 축. 두 회전을 한 축으로 못 합치므로 끄덕임만 회전에 넣고
        // 돌리기는 몸통 자체를 살짝 틀어서 흉내 낸다.
        _rot[VoxelPanda.Head].Angle = headNod;

        // 꼬리는 Z 축 회전으로 위아래, 좌우 흔들림은 Z 이동으로 흉내 낸다.
        _rot[VoxelPanda.Tail].Angle = tailDroop + tailSway * 0.4;
        _move[VoxelPanda.Tail].OffsetZ = tailSway * 0.12;
        _move[VoxelPanda.Head].OffsetZ = headTurn * 0.05;

        _rigMove.OffsetY = bob;
        _squash.ScaleY = squash;

        SetEyes(eyesClosed);
    }

    private bool? _lastEyesClosed;

    private void SetEyes(bool closed)
    {
        if (_lastEyesClosed == closed) return;
        _lastEyesClosed = closed;

        // 그룹을 통째로 넣고 빼서 눈 상태를 바꾼다 - 투명도 조절보다 싸고 확실하다.
        var head = _parts[VoxelPanda.Head];
        head.Children.Remove(_eyesOpen);
        head.Children.Remove(_eyesClosed);
        head.Children.Add(closed ? _eyesClosed : _eyesOpen);
    }
}
