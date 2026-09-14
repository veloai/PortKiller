namespace PortKiller.Core;

/// <summary>펫의 다섯 가지 수치. 항상 0~100 사이로 유지된다.</summary>
public sealed class PetStats
{
    private double _hunger = 80;
    private double _happiness = 80;
    private double _energy = 80;
    private double _health = 100;
    private double _cleanliness = 100;

    /// <summary>배부름. 높을수록 배가 부르다.</summary>
    public double Hunger { get => _hunger; set => _hunger = Clamp(value); }
    public double Happiness { get => _happiness; set => _happiness = Clamp(value); }
    public double Energy { get => _energy; set => _energy = Clamp(value); }
    public double Health { get => _health; set => _health = Clamp(value); }
    public double Cleanliness { get => _cleanliness; set => _cleanliness = Clamp(value); }

    public static double Clamp(double v) => v < 0 ? 0 : (v > 100 ? 100 : v);
}
