using BunnyU.Core;

namespace BunnyU.WinForms;

internal sealed class PagingTestApi:IBunnyApi
{
    public int CategoryCalls,ChapterCalls;
    public TaskCompletionSource<SearchPage> CategoryReply=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<ChapterPage> ChapterReply=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Func<int,Task<ChapterPage>>? ChapterResponder;
    public Task<SearchPage> CategoryAlbumsAsync(string c,int p,UserSession? s,CancellationToken ct=default){CategoryCalls++;return CategoryReply.Task;}
    public Task<ChapterPage> ChaptersAsync(long a,int p,UserSession? s,CancellationToken ct=default){ChapterCalls++;return ChapterResponder?.Invoke(p)??ChapterReply.Task;}
    public Task<UserSession> AccountAsync(UserSession s,CancellationToken ct=default)=>throw new NotSupportedException();
    public Task SendCodeAsync(string m,CancellationToken ct=default)=>throw new NotSupportedException();
    public Task<UserSession> LoginAsync(string m,string c,CancellationToken ct=default)=>throw new NotSupportedException();
    public Task<SearchPage> SearchAsync(string k,int p,UserSession? s,CancellationToken ct=default)=>throw new NotSupportedException();
    public Task<IReadOnlyList<RecommendationSection>> RecommendationsAsync(UserSession? s,CancellationToken ct=default)=>throw new NotSupportedException();
    public Task<IReadOnlyList<AlbumCategory>> CategoriesAsync(CancellationToken ct=default)=>throw new NotSupportedException();
}
