using System.Windows.Controls;
using System.Windows.Media.Media3D;
using PortKiller.Core;

namespace PortKiller.App;

/// <summary>
/// 알·응아처럼 움직이는 부품이 없는 복셀 물체를 그리는 창. 바닥 가운데를 기준으로 서 있고,
/// <see cref="Tilt"/> 로 좌우로 기울일 수 있다(알이 흔들릴 때).
/// </summary>
public sealed class VoxelPropView : Viewport3D
{
    private readonly double _height;
    private readonly AxisAngleRotation3D _tilt = new(new Vector3D(0, 0, 1), 0);
    private readonly ModelVisual3D _visual = new();
    private IReadOnlyList<VoxelBox>? _shown;

    /// <param name="viewUnits">화면 한 변이 몇 복셀인가. 클수록 물체가 작게 그려진다.</param>
    /// <param name="height">물체의 세로 길이(복셀). 바닥에 맞춰 가운데에 놓는 데 쓴다.</param>
    public VoxelPropView(double viewUnits, double height)
    {
        _height = height;
        Camera = new OrthographicCamera(new Point3D(0, viewUnits / 2 - 1.2, 40), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), viewUnits);
        ClipToBounds = true;
        IsHitTestVisible = false;
        Children.Add(_visual);
    }

    /// <summary>기울기(도). 바닥 가운데를 축으로 좌우로 기운다.</summary>
    public double Tilt
    {
        get => _tilt.Angle;
        set => _tilt.Angle = value;
    }

    /// <summary>모양을 바꾼다. 같은 설계도를 다시 주면 아무것도 하지 않는다(초당 60번 불려도 싸다).</summary>
    public void Show(IReadOnlyList<VoxelBox> boxes)
    {
        if (ReferenceEquals(_shown, boxes)) return;
        _shown = boxes;

        var group = new Model3DGroup();
        foreach (var light in VoxelMeshBuilder.Lights()) group.Children.Add(light);

        var body = new Model3DGroup();
        foreach (var m in VoxelMeshBuilder.BuildByColor(boxes)) body.Children.Add(m);
        var t = new Transform3DGroup();
        t.Children.Add(new RotateTransform3D(_tilt, 0, 0, 0));
        body.Transform = t;
        group.Children.Add(body);

        group.Transform = VoxelMeshBuilder.ThreeQuarterView(0, _height / 2);
        _visual.Content = group;
    }
}
