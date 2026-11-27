namespace BunnyU.Core;

public sealed record UserSession(long UserId, string Token, string Mobile, string? Nickname = null)
{
    public string MaskedMobile => Mobile.Length >= 7 ? $"{Mobile[..3]}****{Mobile[^4..]}" : "未提供";
    public string DisplayName => !string.IsNullOrWhiteSpace(Nickname)
        ? new string(Nickname.Where(c=>!char.IsControl(c)).Take(80).ToArray()).Trim()
        : Mobile.Length >= 7 ? MaskedMobile : $"用户 {UserId}";
}

public sealed record Album(long Id, string Name, string Author, string Cover, string Tags)
{
    public string Subtitle => string.IsNullOrWhiteSpace(Author) ? "有声作品" : Author;
}

public sealed record SearchPage(IReadOnlyList<Album> Items, int Page, int Total);
public sealed record RecommendationSection(string Name,string Description,IReadOnlyList<Album> Albums);
public sealed record AlbumCategory(string Id,string Name);

public sealed record Chapter(long Id, string Name, string Cover, double DurationSeconds,
    string MediaUrl, bool RequiresEntitlement, bool ExplicitlyUnlocked, double TrialSeconds, string? DownloadUrl = null, PlaybackAddresses? Playback = null, PlaybackAddresses? DownloadSources = null)
{
    public bool IsTrial => RequiresEntitlement && !ExplicitlyUnlocked && TrialSeconds > 0;
    public bool IsLocked => RequiresEntitlement && !ExplicitlyUnlocked && TrialSeconds <= 0;
    public string AccessLabel => IsLocked ? "需购买 / 会员权限" : IsTrial ? $"试听 {FormatTime(TrialSeconds)}" : "可播放";
    public string DurationLabel => FormatTime(DurationSeconds);
    public double? PlaybackLimit => IsTrial ? TrialSeconds : null;
    public static string FormatTime(double seconds) => seconds >= 3600
        ? TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"h\:mm\:ss")
        : TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"m\:ss");
}

public sealed record ChapterPage(IReadOnlyList<Chapter> Items, int Page, int Total);
public sealed class BunnyApiException(string message, bool requiresLogin = false) : Exception(message)
{
    public bool RequiresLogin { get; } = requiresLogin;
}

public interface IBunnyApi
{
    Task<IReadOnlyList<RecommendationSection>> RecommendationsAsync(UserSession? session,CancellationToken cancellationToken=default);
    Task<IReadOnlyList<AlbumCategory>> CategoriesAsync(CancellationToken cancellationToken=default);
    Task<SearchPage> CategoryAlbumsAsync(string category,int page,UserSession? session,CancellationToken cancellationToken=default);
    Task<UserSession> AccountAsync(UserSession session,CancellationToken cancellationToken=default);
    Task SendCodeAsync(string mobile, CancellationToken cancellationToken = default);
    Task<UserSession> LoginAsync(string mobile, string code, CancellationToken cancellationToken = default);
    Task<SearchPage> SearchAsync(string keyword, int page, UserSession? session, CancellationToken cancellationToken = default);
    Task<ChapterPage> ChaptersAsync(long albumId, int page, UserSession? session, CancellationToken cancellationToken = default);
}
