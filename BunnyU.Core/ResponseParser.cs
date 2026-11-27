using System.Text.Json;

namespace BunnyU.Core;

public static class ResponseParser
{
    private static IReadOnlyList<Album> BrowseAlbums(JsonElement? list)
    {
        if(list is not {ValueKind:JsonValueKind.Array})throw new BunnyApiException("作品列表格式无法识别。");
        return list.Value.EnumerateArray().Select(item=>new Album(Number(item,"album_id","albumId","id"),Text(item,"name","title"),Text(item,"author","cv_author"),Text(item,"cover"),Text(item,"coverTag")))
            .Where(a=>a.Id>0).DistinctBy(a=>a.Id).ToList();
    }
    public static IReadOnlyList<RecommendationSection> Recommendations(JsonElement root)
    {
        var list=Property(Data(root),"data");
        if(list is not {ValueKind:JsonValueKind.Array})throw new BunnyApiException("推荐栏目格式无法识别。");
        return list.Value.EnumerateArray().Select(item=>new RecommendationSection(Text(item,"title"),Text(item,"recommend_text"),BrowseAlbums(Property(item,"children")))).ToList();
    }
    public static IReadOnlyList<AlbumCategory> Categories(JsonElement root)
    {
        var conditions=Property(Data(root),"cond");
        if(conditions is {ValueKind:JsonValueKind.Array})
            foreach(var condition in conditions.Value.EnumerateArray())
                if(Text(condition,"field")=="album_sort_id"&&Property(condition,"data") is {ValueKind:JsonValueKind.Array} list)
                    return list.EnumerateArray().Select(item=>new AlbumCategory(Text(item,"value"),Text(item,"name"))).Where(c=>c.Id.Length>0).DistinctBy(c=>c.Id).ToList();
        throw new BunnyApiException("分类列表格式无法识别。");
    }
    public static SearchPage CategoryAlbums(JsonElement root,int page)
    {
        var data=Data(root);var albums=BrowseAlbums(Property(data,"data"));
        return new(albums,page,(int)Number(data,"total"));
    }
    public static JsonElement Data(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new BunnyApiException("服务器返回了无法识别的数据。");
        if (root.TryGetProperty("code", out var code) && code.ValueKind != JsonValueKind.Null)
        {
            var number = ReadLong(code);
            if (number is not (0 or 200))
                throw new BunnyApiException(Text(root, "msg", "message") is { Length: > 0 } msg ? msg : $"请求未成功（{number}）。", number is 401 or 403);
        }
        return root.TryGetProperty("data", out var data) ? data : root;
    }

    public static UserSession Login(JsonElement root, string mobile)
    {
        // CommonApi.mobileLogin validates response.data.code and reads
        // response.data.data into UserCredentials. Auth endpoints can wrap the
        // business result in an additional {code,msg,data} transport envelope.
        // Only unwrap the documented `data` chain, never arbitrary nested users.
        var current=root;
        for(var depth=0;depth<4&&current.ValueKind==JsonValueKind.Object;depth++)
        {
            var next=Data(current); // Validate every envelope before accepting its credentials.
            var token=Text(current,"token","access_token");
            var id=Number(current,"id","userId","user_id");
            if(!string.IsNullOrWhiteSpace(token)&&id>0)
                return new UserSession(id,token,Text(current,"mobile") is { Length: > 0 } phone?phone:mobile,Text(current,"nickname","nick_name"));
            if(!current.TryGetProperty("data",out _))break;
            current=next;
        }
        throw new BunnyApiException("服务器响应未包含可用的账号和登录凭据，客户端尚未完成登录。此提示不代表验证码错误。");
    }

    public static UserSession Account(JsonElement root,UserSession session)
    {
        var current=root;
        for(var depth=0;depth<4&&current.ValueKind==JsonValueKind.Object;depth++)
        {
            var next=Data(current);
            var id=Number(current,"user_id","userId","id");
            var nickname=Text(current,"nickname","nick_name");
            if(!string.IsNullOrWhiteSpace(nickname))
            {
                if(id>0&&id!=session.UserId)throw new BunnyApiException("账号资料与当前登录账号不一致。");
                return session with{Nickname=nickname};
            }
            if(!current.TryGetProperty("data",out _))break;
            current=next;
        }
        return session;
    }

    public static SearchPage Search(JsonElement root, int requestedPage)
    {
        var data = Data(root);
        var result = new List<Album>();
        if (Property(data, "albums") is { ValueKind: JsonValueKind.Array } albums)
            foreach (var item in albums.EnumerateArray()) AddAlbum(item);
        // 'album' is the highlighted exact match; recommendations are deliberately not search hits.
        if (requestedPage == 1 && Property(data, "album") is { ValueKind: JsonValueKind.Object } featured) AddAlbum(featured);
        return new SearchPage(result, requestedPage, (int)Number(data, "albumTotal", "total"));
        void AddAlbum(JsonElement item)
        {
            var id = Number(item, "album_id", "albumId", "id");
            if (id <= 0 || result.Any(x => x.Id == id)) return;
            result.Add(new Album(id, Text(item, "name", "title"), Text(item, "cv_author", "cvAuthor", "author"), Text(item, "cover", "cover_image"), Text(item, "tags")));
        }
    }

    public static ChapterPage Chapters(JsonElement root, int requestedPage)
    {
        var data = Data(root);
        var list = data.ValueKind == JsonValueKind.Array ? data : Property(data, "data", "list", "items");
        var result = new List<Chapter>();
        if (list is { ValueKind: JsonValueKind.Array })
        {
            foreach (var item in list.Value.EnumerateArray())
            {
                var sound = Property(item, "soundInfo") is { ValueKind: JsonValueKind.Object } info ? info : item;
                var id = Number(item, "album_sound_id", "albumSoundId", "id");
                if (id <= 0) id = Number(sound, "album_sound_id", "albumSoundId", "id");
                if (id <= 0 || result.Any(x => x.Id == id)) continue;
                // VIP/buyout metadata describes restrictions, not an entitlement grant.
                var restricted = Truth(item, "is_vip") || Truth(item, "is_svip") || Truth(item, "is_demand") || Truth(item, "is_free") || Truth(item, "is_buyout");
                var unlocked = Truth(item, "can_play_full") || Truth(item, "has_buy") || Truth(item, "is_purchased");
                var trial = (double)Number(item, "freeDuration", "free_duration");
                var duration = Number(sound, "duration");
                if (duration > 0) trial = Math.Min(trial, duration);
                result.Add(new Chapter(id, Text(sound, "full_name", "fullName", "name"), Text(sound, "cover"), duration,
                    MediaAddress.Resolve(item, sound), restricted, unlocked, Math.Max(0, trial),MediaAddress.ResolveDownload(item,sound)));
            }
        }
        return new ChapterPage(result, requestedPage, data.ValueKind == JsonValueKind.Object ? (int)Number(data, "total") : result.Count);
    }

    public static JsonElement? Property(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names) if (value.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null) return p;
        return null;
    }
    public static string Text(JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            var p = Property(value, name);
            if (p is { ValueKind: JsonValueKind.String } && p.Value.GetString() is { Length: > 0 } s) return s;
            if (p is { ValueKind: JsonValueKind.Number }) return p.Value.ToString();
        }
        return "";
    }
    public static long Number(JsonElement value, params string[] names)
    {
        foreach (var name in names) if (Property(value, name) is { } p && long.TryParse(p.ToString(), out var n)) return n;
        return 0;
    }
    private static long ReadLong(JsonElement value) => long.TryParse(value.ToString(), out var n) ? n : long.MinValue;
    private static bool Truth(JsonElement value, string name) => Property(value, name) is { } p && (p.ValueKind == JsonValueKind.True || p.ToString() == "1");
}
