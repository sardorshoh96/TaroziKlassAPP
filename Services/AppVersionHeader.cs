namespace TaroziAPP.Services;

/// <summary>
/// Serverga boradigan har so'rovga ilova versiyasini qo'shadi (`X-App-Version: TaroziKlass 1.0.6 (7)`).
/// Server uni qurilma yozuviga saqlaydi: admin panelda "Versiya" ustuni va "Yangilanishlar" bo'limidagi
/// "Qurilmalarda" soni shundan olinadi (yangilanish tarqalishini kuzatish uchun).
/// </summary>
public static class AppVersionHeader
{
    public const string Name = "X-App-Version";

    static string? _value;

    public static string Value
    {
        get
        {
            if (_value != null) return _value;
            try { _value = $"TaroziKlass {AppInfo.VersionString} ({AppInfo.BuildString})"; }
            catch { _value = "TaroziKlass"; }
            return _value;
        }
    }

    public static void AddTo(HttpRequestMessage request) => request.Headers.TryAddWithoutValidation(Name, Value);
}
