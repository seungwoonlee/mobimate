namespace MobiMate.Web.Hosting;

/// <summary>favorites.json (FR-DT-15·21): 가방 아이템과 채집물의 즐겨찾기. 이름 기준, 모든 캐릭터·기기가 함께 쓴다.</summary>
public sealed class FavoritesData
{
    public List<string> Items { get; set; } = new();
    public List<string> Gather { get; set; } = new();
}

public enum FavoriteKind { Items, Gather }

public sealed class FavoritesStore
{
    public const string FileName = "favorites.json";
    public const int MaxPerKind = 500;
    private readonly JsonFileStore _store = new();
    private readonly string _path;
    private readonly object _lock = new();
    private FavoritesData _current;
    private HashSet<string> _items;
    private HashSet<string> _gather;

    public FavoritesStore(string storageDir)
    {
        _path = Path.Combine(storageDir, FileName);
        _current = _store.Load<FavoritesData>(_path) ?? new FavoritesData();
        (_items, _gather) = (new(_current.Items, StringComparer.Ordinal), new(_current.Gather, StringComparer.Ordinal));
    }

    public bool IsItem(string? name) { lock (_lock) return name != null && _items.Contains(name); }
    public bool IsGather(string? name) { lock (_lock) return name != null && _gather.Contains(name); }

    public (IReadOnlyList<string> Items, IReadOnlyList<string> Gather) Snapshot()
    {
        lock (_lock) return (_current.Items.ToList(), _current.Gather.ToList());
    }

    /// <summary>즐겨찾기를 켜거나 끈다. 저장에 실패하면 false이고 메모리 값도 바꾸지 않는다. 한도를 넘으면 <paramref name="tooMany"/>가 true.</summary>
    public bool Set(FavoriteKind kind, string name, bool favorite, out bool tooMany)
    {
        tooMany = false;
        lock (_lock)
        {
            var next = new FavoritesData { Items = _current.Items.ToList(), Gather = _current.Gather.ToList() };
            var list = kind == FavoriteKind.Items ? next.Items : next.Gather;
            if (favorite)
            {
                if (list.Contains(name, StringComparer.Ordinal)) return true;
                if (list.Count >= MaxPerKind) { tooMany = true; return false; }
                list.Add(name);
            }
            else if (list.RemoveAll(x => x == name) == 0) return true;

            if (!_store.Save(_path, next)) return false;
            _current = next;
            (_items, _gather) = (new(next.Items, StringComparer.Ordinal), new(next.Gather, StringComparer.Ordinal));
            return true;
        }
    }
}
