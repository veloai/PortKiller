using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using PortKiller.Core;

namespace PortKiller.App;

/// <summary>펫이 취할 수 있는 그림 한 장. 파일 이름과 1:1이다.</summary>
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

/// <summary>
/// 스프라이트 파일을 찾아 주는 곳. <b>없으면 없다고 답한다</b> — 그림이 아직 안 온 포즈가 있어도
/// 앱이 죽지 않고 이모지로 돌아간다. 그림은 단계별로 한 벌씩 들어오므로, 중간 상태가 정상이다.
///
/// <para>왜 캐시가 필요한가: <c>RefreshFace()</c> 는 게임 루프에서 <b>초당 60번</b> 불린다.
/// 매번 디스크를 보거나 PNG 를 디코딩하면 그 자체로 프레임을 잡아먹는다. 경로별로 한 번만
/// 읽고, 없다는 사실도 함께 기억한다(없는 파일을 60번 다시 찾지 않도록).</para>
/// </summary>
public sealed class SpriteLibrary
{
    /// <summary>exe 옆의 assets/sprites. csproj 가 빌드할 때 복사해 둔다.</summary>
    private readonly string _root;

    /// <summary>경로 -> 그림. 값이 null 이면 "찾아봤는데 없더라"는 뜻이다.</summary>
    private readonly Dictionary<string, BitmapImage?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public SpriteLibrary(string? root = null)
    {
        _root = root ?? Path.Combine(AppContext.BaseDirectory, "assets", "sprites");
    }

    /// <summary>그림이 한 장이라도 깔려 있는가. 디버그 표시에만 쓴다.</summary>
    public bool AnyInstalled => Directory.Exists(_root);

    /// <summary>알 그림. 단계가 알일 때만 쓴다.</summary>
    public BitmapImage? Egg(string name) => Load(Path.Combine(_root, "chars", "egg", name + ".png"));

    /// <summary>응아 그림.</summary>
    public BitmapImage? Poop() => Load(Path.Combine(_root, "poop.png"));

    /// <summary>
    /// 아직 그림이 없는 종이 나왔을 때 대신 쓸 종.
    ///
    /// <para>종은 <b>부화한 시간대</b>로 정해진다(<see cref="SpeciesPicker"/>). 그래서 저녁에
    /// 부화시키면 <c>evening</c> 이 되는데, 그 종 그림이 아직 없으면 캐릭터가 통째로 사라지고
    /// 이모지가 검은 글자로 찍혀 나온다 — 실제로 그렇게 보였다. 그림이 덜 그려진 것과
    /// <b>고장 난 것</b>은 화면에서 구분이 안 되므로, 있는 그림으로 내려간다.</para>
    /// </summary>
    private const string FallbackSpecies = "day";

    /// <summary>
    /// 종·단계·포즈에 맞는 그림. 셋 단계로 물러선다.
    ///
    /// <list type="number">
    ///   <item>그 종·그 포즈</item>
    ///   <item>그 종의 같은 단계 idle — 8포즈 중 일부만 들어온 동안 이모지로 튀지 않게</item>
    ///   <item><see cref="FallbackSpecies"/> 의 같은 단계·같은 포즈 — 아직 안 그린 종을 위해</item>
    /// </list>
    /// </summary>
    public BitmapImage? Character(string speciesId, LifeStage stage, PetPose pose)
    {
        if (string.IsNullOrWhiteSpace(speciesId)) return null;

        var art = Find(speciesId, stage, pose);
        if (art is not null) return art;

        return Safe(speciesId) == FallbackSpecies ? null : Find(FallbackSpecies, stage, pose);
    }

    private BitmapImage? Find(string speciesId, LifeStage stage, PetPose pose)
    {
        var dir = Path.Combine(_root, "chars", Safe(speciesId), StageFolder(stage));
        return Load(Path.Combine(dir, PoseFile(pose)))
            ?? (pose == PetPose.Idle ? null : Load(Path.Combine(dir, "idle.png")));
    }

    /// <summary>
    /// 종 id 는 세이브 파일에서 온다. 파일이 손상되거나 손으로 고쳐졌을 때 그 값이 그대로
    /// 경로에 들어가면 바깥 폴더를 가리킬 수 있다. 영문·숫자·하이픈만 남긴다.
    /// </summary>
    private static string Safe(string value)
    {
        var buffer = new char[value.Length];
        var n = 0;
        foreach (var c in value)
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_') buffer[n++] = char.ToLowerInvariant(c);
        }
        return n == 0 ? "_" : new string(buffer, 0, n);
    }

    private static string StageFolder(LifeStage stage) => stage switch
    {
        LifeStage.Baby => "baby",
        LifeStage.Child => "child",
        LifeStage.Adult => "adult",
        _ => "egg",
    };

    private static string PoseFile(PetPose pose) => pose switch
    {
        PetPose.Walk1 => "walk1.png",
        PetPose.Walk2 => "walk2.png",
        PetPose.Eat => "eat.png",
        PetPose.Happy => "happy.png",
        PetPose.Sick => "sick.png",
        PetPose.Sleep => "sleep.png",
        PetPose.Sulk => "sulk.png",
        _ => "idle.png",
    };

    private BitmapImage? Load(string path)
    {
        if (_cache.TryGetValue(path, out var cached)) return cached;

        BitmapImage? image = null;
        try
        {
            if (File.Exists(path))
            {
                image = new BitmapImage();
                image.BeginInit();
                // 파일을 잡고 있으면 그림을 갈아끼울 때 저장이 막힌다. 통째로 읽고 놓는다.
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
            }
        }
        catch (Exception)
        {
            // 깨진 PNG 한 장 때문에 펫이 죽으면 안 된다. 없는 것으로 친다.
            image = null;
        }

        _cache[path] = image;
        return image;
    }
}
