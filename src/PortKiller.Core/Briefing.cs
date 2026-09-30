namespace PortKiller.Core;

/// <summary>브리핑에서 짚을 수 있는 항목.</summary>
public enum BriefingTopic
{
    Hunger,
    Happiness,
    Energy,
    Health,
    Cleanliness,
}

/// <summary>
/// 브리핑(상태 보기) 때 레서판다가 항목을 짚으며 하는 한마디. 값의 높낮이에 따라 세 갈래다.
/// 화면을 모르는 순수한 글 규칙이라 시험할 수 있다.
/// </summary>
public static class Briefing
{
    /// <summary>50 이상은 좋음, 25 이상은 주의, 그 아래는 위험 - 상태 카드의 막대 색과 같은 기준이다.</summary>
    public static string Line(BriefingTopic topic, double value)
    {
        var tier = value >= 50 ? 2 : value >= 25 ? 1 : 0;
        return (topic, tier) switch
        {
            (BriefingTopic.Hunger, 2) => "배는 든든해요.",
            (BriefingTopic.Hunger, 1) => "슬슬 출출해지고 있어요.",
            (BriefingTopic.Hunger, _) => "배가 너무 고파요! 먹이를 주세요.",

            (BriefingTopic.Happiness, 2) => "기분이 아주 좋아요.",
            (BriefingTopic.Happiness, 1) => "조금 심심해요. 놀아 주세요.",
            (BriefingTopic.Happiness, _) => "많이 시무룩해요. 같이 놀아요.",

            (BriefingTopic.Energy, 2) => "기운이 넘쳐요.",
            (BriefingTopic.Energy, 1) => "슬슬 졸려요.",
            (BriefingTopic.Energy, _) => "기운이 바닥이에요. 재워 주세요.",

            (BriefingTopic.Health, 2) => "건강해요.",
            (BriefingTopic.Health, 1) => "컨디션이 별로예요. 잘 챙겨 주세요.",
            (BriefingTopic.Health, _) => "많이 아파요! 약이 필요해요.",

            (BriefingTopic.Cleanliness, 2) => "깨끗해요.",
            (BriefingTopic.Cleanliness, 1) => "슬슬 지저분해져요.",
            (BriefingTopic.Cleanliness, _) => "너무 지저분해요. 청소해 주세요!",
            _ => string.Empty,
        };
    }

    /// <summary>항목을 가리키지 않을 때 하는 전체 요약. 가장 급한 것 하나를 말한다.</summary>
    public static string Summary(PetStats s)
    {
        var worst = new[]
        {
            (Topic: BriefingTopic.Hunger, Value: s.Hunger),
            (Topic: BriefingTopic.Happiness, Value: s.Happiness),
            (Topic: BriefingTopic.Energy, Value: s.Energy),
            (Topic: BriefingTopic.Health, Value: s.Health),
            (Topic: BriefingTopic.Cleanliness, Value: s.Cleanliness),
        }.OrderBy(x => x.Value).First();

        return worst.Value >= 50
            ? "전부 좋아요! 항목을 마우스로 짚어 보세요."
            : Line(worst.Topic, worst.Value);
    }
}
