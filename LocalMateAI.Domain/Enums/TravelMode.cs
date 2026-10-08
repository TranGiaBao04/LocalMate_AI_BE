namespace LocalMateAI.Domain.Enums;

public enum TravelMode
{
    /// <summary>Tự chọn theo từng chặng: đường đi ≤ 700 m tính đi bộ, xa hơn tính xe máy.</summary>
    Auto = 0,
    Walking = 1,
    Motorbike = 2,

    /// <summary>
    /// Đi tàu Metro số 1 giữa các ga. Đoạn cùng ga hoặc đường đi ≤ 700 m thì đi thẳng, không lên tàu;
    /// hết tàu thì đoạn đó tính xe máy.
    /// </summary>
    Metro = 3
}
