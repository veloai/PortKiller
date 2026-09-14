namespace PortKiller.Core;

/// <summary>
/// 게임 수치를 한곳에 모은다. 코드 여기저기에 숫자를 흩지 않는다.
/// 시간당 변화량은 모두 "1시간 기준"이며 Tick 에서 경과 시간에 비례 적용된다.
/// </summary>
public static class Balance
{
    // --- 스탯 자연 감소 (시간당) ---
    public const double HungerDecayPerHour = 4.0;
    public const double HappinessDecayPerHour = 3.0;
    public const double EnergyDecayPerHour = 5.0;
    public const double CleanlinessDecayPerHour = 2.0;

    // --- 잠자는 동안 ---
    public const double SleepEnergyRecoveryPerHour = 25.0;

    // --- 체력 ---
    public const double HealthDrainHungryPerHour = 3.0;
    public const double HealthDrainDirtyPerHour = 3.0;
    public const double HealthRegenPerHour = 2.0;

    // --- 상태 판정 기준 ---
    public const double SickThreshold = 25.0;        // 체력이 이 아래면 아프다
    public const double SickRecoverThreshold = 50.0; // 이 위로 올라와야 낫는다
    public const double HungerWarnThreshold = 20.0;
    public const double SulkThreshold = 20.0;        // 행복이 이 아래면 삐진다
    public const double SulkRecoverThreshold = 40.0;

    // --- 성장 ---
    public const double HatchHoursMax = 4.0;         // 방치해도 4시간이면 부화
    public const int HatchClicksRequired = 30;       // 쓰다듬어서 앞당기기
    public const int StageChildDays = 3;
    public const int StageAdultDays = 7;

    // --- 자리 비움(오프라인) 보정 ---
    /// <summary>앱이 꺼져 있던 시간은 절반만 인정한다.</summary>
    public const double OfflineDecayRate = 0.5;
    /// <summary>아무리 오래 꺼놔도 8시간치까지만 깎는다. 죽지 않게 하는 장치.</summary>
    public const double OfflineCapHours = 8.0;

    // --- 응아 ---
    /// <summary>먹은 뒤 소화에 걸리는 시간(분). 이 범위에서 무작위로 정해진다.</summary>
    public const double DigestMinutesMin = 90.0;
    public const double DigestMinutesMax = 180.0;
    /// <summary>아무것도 안 먹어도 이 간격(분)으로는 눈다.</summary>
    public const double PoopIntervalMinutesMin = 180.0;
    public const double PoopIntervalMinutesMax = 300.0;
    /// <summary>응아 하나가 깎는 청결도.</summary>
    public const double PoopDirtiness = 12.0;
    /// <summary>꺼둔 사이에 쌓이는 응아 최대 개수. 돌아왔을 때 화면이 뒤덮이지 않게.</summary>
    public const int MaxOfflinePoops = 3;
    /// <summary>바닥에 동시에 존재할 수 있는 최대 개수.</summary>
    public const int MaxPoopsOnGround = 6;

    // --- 움직임 (초당 픽셀) ---
    public const double WalkSpeed = 34.0;
    public const double Gravity = 900.0;
    public const double JumpVelocity = -420.0;
    /// <summary>가만히 있는 시간(초) 범위.</summary>
    public const double IdleSecondsMin = 2.0;
    public const double IdleSecondsMax = 6.0;
    /// <summary>한 번 걷는 시간(초) 범위.</summary>
    public const double WalkSecondsMin = 1.5;
    public const double WalkSecondsMax = 4.0;
    /// <summary>가만히 있다가 다음에 점프를 고를 확률.</summary>
    public const double JumpChance = 0.2;

    // --- 뽀모도로 ---
    public const double PomodoroFocusMinutes = 25.0;
    public const double PomodoroBreakMinutes = 5.0;

    // --- 진화 조건 ---
    /// <summary>
    /// 키 입력 "횟수"가 아니라 "집중한 시간"을 쓴다.
    /// 모든 키를 훑는 방식은 백신이 키로거로 보기 때문에 처음부터 쓰지 않는다.
    /// </summary>
    public const double EvolutionFocusHours = 20.0;
    public const int EvolutionDistinctDays = 5;
    public const int EvolutionCareCount = 40;
}
