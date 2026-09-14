namespace PortKiller.Core;

/// <summary>
/// 부화한 시각으로 어떤 종이 나올지 정한다.
/// "언제 알을 깼느냐"가 캐릭터를 가르는 것이 이 게임의 재미 축이다.
///
/// 주의: 종 이름과 그림은 직접 만들어 채워야 한다.
/// 참고한 앱의 캐릭터 이름·스프라이트는 그쪽 저작물이므로 가져오지 않는다.
/// </summary>
public static class SpeciesPicker
{
    /// <summary>부화 시간대 구분표. id 는 스프라이트 폴더 이름과 1:1로 맞춘다.</summary>
    public static readonly IReadOnlyList<SpeciesRule> Rules = new[]
    {
        new SpeciesRule("dawn",    "새벽에 깨어난 아이", h => h >= 0 && h < 6),
        new SpeciesRule("morning", "아침에 깨어난 아이", h => h >= 6 && h < 11),
        new SpeciesRule("lunch",   "점심에 깨어난 아이", h => h >= 11 && h < 14),
        new SpeciesRule("day",     "한낮에 깨어난 아이", h => h >= 14 && h < 18),
        new SpeciesRule("evening", "저녁에 깨어난 아이", h => h >= 18 && h < 24),
    };

    public static string Pick(DateTimeOffset hatchedAt)
    {
        var hour = hatchedAt.Hour;

        // 금요일 저녁은 별도 종으로 빠진다. 조건이 겹칠 때 먼저 확인한다.
        if (hatchedAt.DayOfWeek == DayOfWeek.Friday && hour >= 18) return "friday";

        foreach (var rule in Rules)
            if (rule.Matches(hour))
                return rule.Id;

        return Rules[0].Id;
    }

    public static string DisplayName(string speciesId)
        => Rules.FirstOrDefault(r => r.Id == speciesId)?.DisplayName ?? speciesId;
}

public sealed record SpeciesRule(string Id, string DisplayName, Func<int, bool> HourMatch)
{
    public bool Matches(int hour) => HourMatch(hour);
}
