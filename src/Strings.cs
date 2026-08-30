namespace ZesilovacAplikaci;

internal enum Lang { English, Czech }

/// <summary>
/// Preklady rozhrani. Oba jazyky se pisou primo na miste pouziti - pri dvou
/// jazycich je to citelnejsi nez tabulka klicu nekde stranou a nemuze se stat,
/// ze se prida text a preklad se zapomene.
/// </summary>
internal static class S
{
    /// <summary>Vychozi je anglictina; cestina se prepina v nastaveni.</summary>
    public static Lang Current { get; set; } = Lang.English;

    public static string T(string en, string cs) => Current == Lang.Czech ? cs : en;
}
