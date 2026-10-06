using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PortKiller.Core;

namespace PortKiller.App;

/// <summary>
/// 검사용: 모든 종의 모습을 한 장의 PNG 로 그려 낸다(`--render-sheet 파일.png`).
///
/// <para>화면에 띄워 캡처하지 않고 메모리에서 바로 그리므로, 화면이 잠겨 있거나 다른 창에 가려져 있어도 된다.
/// 종을 늘릴 때 "모든 종이 모든 자세에서 제대로 나오는지"를 한눈에 보려는 도구다.
/// 인자를 주지 않으면 아무 일도 하지 않는다.</para>
/// </summary>
internal static class ContactSheet
{
    private const double Cell = 240;

    public static void Render(string path)
    {
        var rows = new List<List<FrameworkElement>>();

        foreach (var kind in PetKinds.All)
        {
            var row = new List<FrameworkElement>
            {
                Animal(kind, PetPose.Idle, false, null, 1.0),
                Animal(kind, PetPose.Walk1, true, null, 1.0),
                Animal(kind, PetPose.Idle, false, 30, 1.0, briefing: true),
                Animal(kind, PetPose.Idle, false, -20, 1.0, briefing: true),
                Animal(kind, PetPose.Sleep, false, null, 1.0),
                Animal(kind, PetPose.Eat, false, null, 0.62),
            };
            rows.Add(row);
        }

        // 갓 태어난 모습: 알형은 알(평상/금), 새끼형은 바구니 속 새끼(잠/깨기 직전)
        var born = new List<FrameworkElement>();
        foreach (var kind in PetKinds.All)
        {
            foreach (var nearly in new[] { false, true })
            {
                born.Add(kind.Birth == BirthKind.Egg
                    ? Prop(VoxelProps.Egg(nearly, VoxelAnimals.EggSpot(kind.Id)), 28, VoxelProps.EggHeight)
                    : Prop(VoxelProps.Cub(kind.Id, nearly), 20, VoxelProps.CubHeight));
            }
        }

        rows.Add(born);

        var cols = rows.Max(r => r.Count);
        var grid = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2F, 0x3A)) };
        for (var c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Cell) });
        for (var r = 0; r < rows.Count; r++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Cell) });

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < rows[r].Count; c++)
            {
                var cell = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x45, 0x55)),
                    BorderThickness = new Thickness(1),
                    Child = rows[r][c],
                };
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }

        var w = cols * Cell;
        var h = rows.Count * Cell;
        grid.Measure(new Size(w, h));
        grid.Arrange(new Rect(0, 0, w, h));
        grid.UpdateLayout();

        var bmp = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(grid);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        encoder.Save(file);
    }

    /// <summary>종 고르는 창을 화면에 띄우지 않고 PNG 로 그린다(`--render-picker 파일.png`).</summary>
    public static void RenderPicker(string path)
    {
        var window = new SpeciesPickerWindow(firstTime: true);
        window.Show();
        window.UpdateLayout();

        var root = (FrameworkElement)window.Content;
        var w = (int)window.ActualWidth;
        var h = (int)window.ActualHeight;
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using (var file = File.Create(path)) encoder.Save(file);
        window.Close();
    }

    private static FrameworkElement Animal(PetKind kind, PetPose pose, bool walking, double? point, double scale, bool briefing = false)
    {
        AnimalViewBase view = kind.Rig == RigKind.Biped ? new BipedView() : new QuadrupedView();
        view.Width = view.Height = Cell;
        view.Configure(kind.Id, "day", scale < 1 ? LifeStage.Baby : LifeStage.Adult);
        view.SetBriefing(briefing);
        view.PointAt(point);

        // 부드럽게 바뀌는 값(서기·팔)이 자리 잡도록 몇 초 분량을 미리 돌린다.
        for (var i = 0; i <= 90; i++) view.Animate(pose, walking, 1.0 + i / 30.0);
        return view;
    }

    private static FrameworkElement Prop(IReadOnlyList<VoxelBox> boxes, double units, double height)
    {
        var view = new VoxelPropView(units, height) { Width = Cell, Height = Cell };
        view.Show(boxes);
        return view;
    }
}
