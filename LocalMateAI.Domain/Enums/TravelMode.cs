namespace LocalMateAI.Domain.Enums;

public enum TravelMode
{
    /// <summary>Tự chọn theo từng chặng: đường đi ≤ 700 m tính đi bộ, xa hơn tính xe máy.</summary>
    Auto = 0,
    Walking = 1,
    Motorbike = 2
}
