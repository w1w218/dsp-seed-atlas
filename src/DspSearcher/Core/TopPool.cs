namespace DspSearcher.Core;

/// <summary>全局 top 池（线程安全，容量 PoolSize，按 ScoreMinutes 降序）。</summary>
public class TopPool
{
    private readonly object _lock = new();
    private readonly List<Candidate> _items = new();
    private readonly HashSet<int> _seeds = new();   // 查重（resume 重扫与旧池重叠时防重复）
    public int Capacity { get; }

    public TopPool(int capacity) { Capacity = capacity; }

    public void Add(Candidate c)
    {
        lock (_lock)
        {
            if (!_seeds.Add(c.Seed)) return;
            _items.Add(c);
            _items.Sort((a, b) => Compare(a, b));
            if (_items.Count > Capacity) { _items.RemoveRange(Capacity, _items.Count - Capacity); RebuildSeeds(); }
        }
    }

    /// <summary>批量合并（快筛 worker 批末调用）：锁一次 + 一次排序，显著降低 32 线程锁竞争。</summary>
    public void AddRange(IEnumerable<Candidate> items)
    {
        lock (_lock)
        {
            foreach (var c in items)
                if (_seeds.Add(c.Seed)) _items.Add(c);
            _items.Sort((a, b) => Compare(a, b));
            if (_items.Count > Capacity) { _items.RemoveRange(Capacity, _items.Count - Capacity); RebuildSeeds(); }
        }
    }

    private void RebuildSeeds()
    {
        _seeds.Clear();
        foreach (var c in _items) _seeds.Add(c.Seed);
    }

    /// <summary>排序：仅按支撑产量降序（4.2/4.3b 不参与排序，作为输出列展示）。</summary>
    public static int Compare(Candidate a, Candidate b)
    {
        return b.ScoreMinutes.CompareTo(a.ScoreMinutes);
    }

    public List<Candidate> Snapshot()
    {
        lock (_lock) return new List<Candidate>(_items);
    }

    /// <summary>按 seed 替换单个候选（精算结果回写：理论分 → 精算分），并重排。无该 seed 则忽略。</summary>
    public void Update(Candidate c)
    {
        lock (_lock)
        {
            int i = _items.FindIndex(x => x.Seed == c.Seed);
            if (i < 0) return;
            _items[i] = c;
            _items.Sort((a, b) => Compare(a, b));
        }
    }

    public int Count { get { lock (_lock) return _items.Count; } }

    public void ReplaceAll(List<Candidate> items)
    {
        lock (_lock)
        {
            _items.Clear();
            _items.AddRange(items);
            _items.Sort((a, b) => Compare(a, b));
            if (_items.Count > Capacity) _items.RemoveRange(Capacity, _items.Count - Capacity);
            RebuildSeeds();
        }
    }
}
