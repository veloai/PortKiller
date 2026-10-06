using System.Windows.Media;
using System.Windows.Media.Media3D;
using PortKiller.Core;

namespace PortKiller.App;

/// <summary>
/// 두 발 동물(병아리)을 그리고 움직인다. 네발 동물과 몸 틀이 달라서 따로 만들었지만 약속은 같다.
///
/// <para>이미 서 있는 몸이라 브리핑 때 일어서는 동작은 없고, 안경을 쓰고 왼쪽 날개로 지시봉을 든다.</para>
/// </summary>
public sealed class BipedView : AnimalViewBase
{
    private const double ViewUnits = 30;

    private readonly Dictionary<string, Model3DGroup> _parts = new();
    private readonly Dictionary<string, AxisAngleRotation3D> _swing = new();   // Z 축(앞뒤): 다리 걸음, 고개 끄덕임, 날개 짚기
    private readonly Dictionary<string, AxisAngleRotation3D> _flap = new();    // X 축(옆으로): 날개 퍼덕임
    private readonly ScaleTransform3D _stageScale = new(1, 1, 1, 0, 0, 0);
    private readonly ScaleTransform3D _squash = new(1, 1, 1, 0, 0, 0);
    private readonly AxisAngleRotation3D _roll = new(new Vector3D(1, 0, 0), 0);
    private readonly TranslateTransform3D _rigMove = new();
    private readonly Model3DGroup _eyesOpen = new();
    private readonly Model3DGroup _eyesClosed = new();
    private readonly Model3DGroup _glasses = new();
    private readonly Model3DGroup _pointer = new();
    private readonly ModelVisual3D _visual = new();

    private IReadOnlyList<VoxelBox> _boxes = Array.Empty<VoxelBox>();
    private string? _builtFor;
    private double _centerX = (VoxelChick.MinX + VoxelChick.MaxX) / 2;

    private bool _briefing;
    private double? _pointAngle;
    private double _blend;
    private double _armAngle;
    private double _lastSeconds;
    private bool _propsOn;
    private bool? _lastEyesClosed;

    public BipedView()
    {
        Camera = new OrthographicCamera(new Point3D(_centerX, 13.5, 40), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), ViewUnits);
        ClipToBounds = true;
        IsHitTestVisible = false;
        Children.Add(_visual);
    }

    public override double BriefingBlend => _blend;

    public override void Configure(string kindId, string speciesId, LifeStage stage)
    {
        var variant = VoxelAnimals.VariantOf(speciesId);
        if (_builtFor != variant) Build(variant);

        var s = stage switch { LifeStage.Baby => 0.62, LifeStage.Child => 0.82, _ => 1.0 };
        _stageScale.ScaleX = _stageScale.ScaleY = _stageScale.ScaleZ = s;
    }

    private void Build(string variant)
    {
        _builtFor = variant;
        _boxes = VoxelChick.Boxes(variant);
        _parts.Clear();
        _swing.Clear();
        _flap.Clear();

        _centerX = (VoxelChick.MinX + VoxelChick.MaxX) / 2;
        _stageScale.CenterX = _squash.CenterX = _centerX;
        Camera = new OrthographicCamera(new Point3D(_centerX, 13.5, 40), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), ViewUnits);

        var rig = new Model3DGroup();
        foreach (var light in VoxelMeshBuilder.Lights()) rig.Children.Add(light);

        var body = Group(VoxelChick.Body);
        _parts[VoxelChick.Body] = body;

        var head = Group(VoxelChick.Head, rotates: true);
        FillGroup(_eyesOpen, VoxelChick.EyesOpen);
        FillGroup(_eyesClosed, VoxelChick.EyesClosed);
        FillGroup(_glasses, VoxelChick.Glasses);
        FillGroup(_pointer, VoxelChick.Pointer);
        head.Children.Add(_eyesOpen);
        _lastEyesClosed = false;
        _propsOn = false;
        body.Children.Add(head);

        rig.Children.Add(body);
        foreach (var part in new[] { VoxelChick.WingL, VoxelChick.WingR })
            rig.Children.Add(Group(part, rotates: true, flaps: true));
        foreach (var part in new[] { VoxelChick.LegL, VoxelChick.LegR })
            rig.Children.Add(Group(part, rotates: true));

        var all = new Transform3DGroup();
        all.Children.Add(_stageScale);
        all.Children.Add(_squash);
        all.Children.Add(new RotateTransform3D(_roll, _centerX, 0, 0));   // 뒤뚱거림(좌우로 기우뚱)
        all.Children.Add(_rigMove);
        all.Children.Add(VoxelMeshBuilder.ThreeQuarterView(_centerX, 6));
        rig.Transform = all;

        _visual.Content = rig;
    }

    private Model3DGroup Group(string part, bool rotates = false, bool flaps = false)
    {
        var g = new Model3DGroup();
        foreach (var byColor in _boxes.Where(b => b.Part == part).GroupBy(b => b.Color))
            g.Children.Add(VoxelMeshBuilder.BuildMesh(byColor, byColor.Key));

        if (rotates)
        {
            var pivot = VoxelChick.Pivots[part];
            var swing = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
            var t = new Transform3DGroup();
            t.Children.Add(new RotateTransform3D(swing, pivot.X, pivot.Y, pivot.Z));
            if (flaps)
            {
                var flap = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
                t.Children.Add(new RotateTransform3D(flap, pivot.X, pivot.Y, pivot.Z));
                _flap[part] = flap;
            }

            g.Transform = t;
            _swing[part] = swing;
        }

        _parts[part] = g;
        return g;
    }

    private void FillGroup(Model3DGroup target, string part)
    {
        target.Children.Clear();
        foreach (var byColor in _boxes.Where(b => b.Part == part).GroupBy(b => b.Color))
            target.Children.Add(VoxelMeshBuilder.BuildMesh(byColor, byColor.Key));
    }

    public override void SetBriefing(bool on)
    {
        _briefing = on;
        if (!on) _pointAngle = null;
    }

    public override void PointAt(double? angleDegrees) => _pointAngle = angleDegrees;

    public override void Animate(PetPose pose, bool walking, double seconds)
    {
        if (_builtFor is null) return;

        var t = seconds;
        var dt = Math.Clamp(t - _lastSeconds, 0, 0.1);
        _lastSeconds = t;
        _blend += ((_briefing ? 1 : 0) - _blend) * (1 - Math.Exp(-dt * 7));
        var w = _blend;
        if (_briefing) pose = PetPose.Idle;

        double legSwing = 0, bob = 0, headNod = 0, roll = 0, flap = 0;
        var squash = 1.0;
        var eyesClosed = false;

        if (walking)
        {
            // 뒤뚱뒤뚱: 짧은 다리가 빠르게 번갈아 움직이고 몸이 좌우로 기우뚱한다.
            legSwing = Math.Sin(t * 14) * 32;
            bob = Math.Abs(Math.Sin(t * 14)) * 0.7;
            roll = Math.Sin(t * 7) * 6;
            flap = 8 + Math.Sin(t * 14) * 6;
        }

        switch (pose)
        {
            case PetPose.Happy:
                bob = Math.Abs(Math.Sin(t * 10)) * 1.8;
                flap = 22 + Math.Sin(t * 22) * 20;   // 신나서 날개를 퍼덕인다
                headNod = Math.Sin(t * 10) * 5;
                break;
            case PetPose.Eat:
                headNod = -18 + Math.Sin(t * 14) * 16;   // 모이를 쫀다
                break;
            case PetPose.Sick:
                headNod = -22;
                flap = 14;
                bob = Math.Sin(t * 3) * 0.15;
                legSwing = 0;
                break;
            case PetPose.Sleep:
                headNod = -30;
                squash = 0.8 + Math.Sin(t * 1.6) * 0.02;
                eyesClosed = true;
                legSwing = 0;
                bob = 0;
                roll = 0;
                break;
            case PetPose.Sulk:
                headNod = -10;
                flap = 6;
                break;
            default:
                if (!walking)
                {
                    headNod = Math.Sin(t * 2.2) * 2.5;
                    bob = Math.Sin(t * 2.2) * 0.12;
                    flap = 3 + Math.Sin(t * 2) * 3;
                }

                break;
        }

        // 다리
        _swing[VoxelChick.LegL].Angle = legSwing * (1 - w);
        _swing[VoxelChick.LegR].Angle = -legSwing * (1 - w);

        // 날개: 평소엔 옆으로 퍼덕이고, 브리핑 때는 왼쪽 날개가 지시봉을 들어 가리킨다(회전각 = 방향 + 90).
        _armAngle += (((_pointAngle ?? 25) + 90) - _armAngle) * (1 - Math.Exp(-dt * 10));
        _flap[VoxelChick.WingL].Angle = -flap * (1 - w);
        _flap[VoxelChick.WingR].Angle = flap * (1 - w);
        _swing[VoxelChick.WingL].Angle = _armAngle * w;
        _swing[VoxelChick.WingR].Angle = 8 * w;

        var pointing = _pointAngle is not null;
        var talk = Math.Sin(t * (pointing ? 6 : 2.4)) * (pointing ? 3 : 1.5);
        _swing[VoxelChick.Head].Angle = headNod * (1 - w) + (2 + talk) * w;

        _roll.Angle = roll * (1 - w);
        _rigMove.OffsetY = bob;
        _squash.ScaleY = squash;

        SetProps();
        SetEyes(eyesClosed);
    }

    private void SetProps()
    {
        var want = _briefing || _blend > 0.05;
        if (want == _propsOn) return;
        _propsOn = want;

        var head = _parts[VoxelChick.Head];
        var wing = _parts[VoxelChick.WingL];
        head.Children.Remove(_glasses);
        wing.Children.Remove(_pointer);
        if (!want) return;
        head.Children.Add(_glasses);
        wing.Children.Add(_pointer);
    }

    private void SetEyes(bool closed)
    {
        if (_lastEyesClosed == closed) return;
        _lastEyesClosed = closed;

        var head = _parts[VoxelChick.Head];
        head.Children.Remove(_eyesOpen);
        head.Children.Remove(_eyesClosed);
        head.Children.Add(closed ? _eyesClosed : _eyesOpen);
    }
}
