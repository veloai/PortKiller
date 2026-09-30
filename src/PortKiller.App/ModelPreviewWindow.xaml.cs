using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using PortKiller.Core;

namespace PortKiller.App;

/// <summary>
/// 3D 모델 시험 창. 투명 창 위에 GLB 를 배경 없이 그리고 천천히 돌린다.
/// 펫 본 화면에 3D 를 넣기 전에 "이 방식으로 그려지는가"를 확인하려는 발판이다.
/// </summary>
public partial class ModelPreviewWindow : Window
{
    public ModelPreviewWindow(string glbPath)
    {
        InitializeComponent();

        var model = GlbModel.Load(File.ReadAllBytes(glbPath));
        var visual = new ModelVisual3D { Content = BuildGeometry(model) };
        View.Children.Add(visual);

        // 세로 축을 중심으로 천천히 돈다. 모든 각도가 예쁜지 눈으로 보려는 것이다.
        var spin = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
        visual.Transform = new RotateTransform3D(spin);
        spin.BeginAnimation(AxisAngleRotation3D.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(10)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    private static GeometryModel3D BuildGeometry(GlbModel m)
    {
        var mesh = new MeshGeometry3D();
        for (var i = 0; i < m.Positions.Length; i += 3)
            mesh.Positions.Add(new Point3D(m.Positions[i], m.Positions[i + 1], m.Positions[i + 2]));
        for (var i = 0; i < m.Normals.Length; i += 3)
            mesh.Normals.Add(new Vector3D(m.Normals[i], m.Normals[i + 1], m.Normals[i + 2]));
        for (var i = 0; i < m.Uvs.Length; i += 2)
            mesh.TextureCoordinates.Add(new Point(m.Uvs[i], m.Uvs[i + 1]));
        foreach (var idx in m.Indices) mesh.TriangleIndices.Add(idx);
        mesh.Freeze();

        Material material;
        if (m.TextureBytes is { } bytes)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            var brush = new ImageBrush(bmp) { ViewportUnits = BrushMappingMode.Absolute };
            brush.Freeze();
            material = new DiffuseMaterial(brush);
        }
        else
        {
            material = new DiffuseMaterial(Brushes.SandyBrown);
        }

        // 뒷면도 그린다. 얇은 꼬리 끝이 사라져 보이지 않게.
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private void View_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Close();
}
