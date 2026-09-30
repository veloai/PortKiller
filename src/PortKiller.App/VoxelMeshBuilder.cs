using System.Windows.Media;
using System.Windows.Media.Media3D;
using PortKiller.Core;

namespace PortKiller.App;

/// <summary>복셀 상자들을 WPF 3D 메시로 바꾸는 도구. 펫·알·응아가 같이 쓴다.</summary>
internal static class VoxelMeshBuilder
{
    /// <summary>같은 색 상자끼리 한 메시로 묶는다(재질 수 = 색 수).</summary>
    public static IEnumerable<GeometryModel3D> BuildByColor(IEnumerable<VoxelBox> boxes)
    {
        foreach (var byColor in boxes.GroupBy(b => b.Color))
            yield return BuildMesh(byColor, byColor.Key);
    }

    public static GeometryModel3D BuildMesh(IEnumerable<VoxelBox> boxes, string hex)
    {
        var mesh = new MeshGeometry3D();
        foreach (var b in boxes) AddBox(mesh, b);
        mesh.Freeze();

        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return new GeometryModel3D(mesh, new DiffuseMaterial(brush));
    }

    /// <summary>면마다 꼭짓점을 따로 둔다 - 법선이 면마다 달라야 큐브가 각져 보인다(부드럽게 섞이면 도트가 아니다).</summary>
    private static void AddBox(MeshGeometry3D m, VoxelBox b)
    {
        void Face(Point3D a, Point3D bb, Point3D c, Point3D d, Vector3D n)
        {
            var i = m.Positions.Count;
            m.Positions.Add(a); m.Positions.Add(bb); m.Positions.Add(c); m.Positions.Add(d);
            for (var k = 0; k < 4; k++) m.Normals.Add(n);
            foreach (var idx in new[] { 0, 1, 2, 0, 2, 3 }) m.TriangleIndices.Add(i + idx);
        }

        double x0 = b.X0, y0 = b.Y0, z0 = b.Z0, x1 = b.X1, y1 = b.Y1, z1 = b.Z1;
        Face(new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1), new(0, 0, 1));    // +Z
        Face(new(x1, y0, z0), new(x0, y0, z0), new(x0, y1, z0), new(x1, y1, z0), new(0, 0, -1));   // -Z
        Face(new(x1, y0, z1), new(x1, y0, z0), new(x1, y1, z0), new(x1, y1, z1), new(1, 0, 0));    // +X
        Face(new(x0, y0, z0), new(x0, y0, z1), new(x0, y1, z1), new(x0, y1, z0), new(-1, 0, 0));   // -X
        Face(new(x0, y1, z1), new(x1, y1, z1), new(x1, y1, z0), new(x0, y1, z0), new(0, 1, 0));    // +Y
        Face(new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1), new(x0, y0, z1), new(0, -1, 0));   // -Y
    }

    /// <summary>모든 복셀 물체가 같은 조명을 쓴다 - 알·응아·펫이 한 세계에 있는 것처럼 보이게.</summary>
    public static IEnumerable<Light> Lights()
    {
        yield return new AmbientLight(Color.FromRgb(0xA8, 0xA8, 0xA8));
        yield return new DirectionalLight(Color.FromRgb(0xA0, 0xA0, 0xA0), new Vector3D(-0.5, -0.8, -0.6));
    }

    /// <summary>펫과 같은 3/4 시점(살짝 옆에서, 살짝 위에서).</summary>
    public static Transform3D ThreeQuarterView(double centerX, double centerY)
    {
        var g = new Transform3DGroup();
        g.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), -32), centerX, 0, 0));
        g.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 12), centerX, centerY, 0));
        return g;
    }
}
