using System.Timers;
using KeibaOps.Models;

namespace KeibaOps.Services;

public class オッズ市場サービス : IDisposable
{
    private readonly レース運用サービス _運用;
    private readonly Random _乱数 = new();
    private readonly object _lock = new();
    private System.Timers.Timer _更新タイマー;

    private string _現在レースId = "";
    private Dictionary<string, decimal> _単勝賭け総額 = new();
    private Dictionary<string, decimal> _複勝賭け総額 = new();
    private Dictionary<string, decimal> _単勝オッズ = new();
    private Dictionary<string, decimal> _複勝オッズ = new();
    private Dictionary<string, Dictionary<string, decimal>> _確定単勝オッズByRace = new();
    private Dictionary<string, Dictionary<string, decimal>> _確定複勝オッズByRace = new();
    private Dictionary<string, Dictionary<string, decimal>> _確定単勝投票ByRace = new();
    private Dictionary<string, Dictionary<string, decimal>> _確定複勝投票ByRace = new();
    private Dictionary<string, decimal> _確定単勝プールByRace = new();
    private Dictionary<string, decimal> _確定複勝プールByRace = new();
    private Dictionary<string, int> _複勝枠数ByRace = new();
    private decimal _単勝総プール;
    private decimal _複勝総プール;
    private int _平均毎秒ベット数;
    private decimal _平均賭け額;
    private bool _締切済み;

    private const decimal 控除率 = 0.25m;
    private const decimal 最低オッズ = 1.1m;
    private const decimal 最大オッズ = 200.0m;
    private const decimal 単勝比率 = 0.7m;

    public event Action? オッズ変動時;
    public event Action<string>? 投票締切時;
    public event Action? 収支更新時;

    public オッズ市場サービス(レース運用サービス 運用)
    {
        _運用 = 運用;
        _運用.レース切替時 += レース切替;
        _運用.レース状態変更時 += 状態変化;
        _運用.レース終了時 += レース終了;
        初期化(_運用.現在のレース);

        _更新タイマー = new System.Timers.Timer(1000);
        _更新タイマー.Elapsed += 毎秒更新;
        _更新タイマー.Start();
    }

    private void レース切替(レース 新レース)
    {
        lock (_lock)
        {
            初期化(新レース);
            _締切済み = false;
        }
        オッズ変動時?.Invoke();
    }

    private void 状態変化()
    {
        if (_運用.現在のレース.状態 != レース状態.出走中) return;

        string レースId = _運用.現在のレース.Id;
        bool 締切確定 = false;

        lock (_lock)
        {
            if (_締切済み) return;
            if (_現在レースId != レースId) return;

            _確定単勝オッズByRace[レースId] = _単勝オッズ.ToDictionary(kv => kv.Key, kv => kv.Value);
            _確定複勝オッズByRace[レースId] = _複勝オッズ.ToDictionary(kv => kv.Key, kv => kv.Value);
            _確定単勝投票ByRace[レースId] = _単勝賭け総額.ToDictionary(kv => kv.Key, kv => kv.Value);
            _確定複勝投票ByRace[レースId] = _複勝賭け総額.ToDictionary(kv => kv.Key, kv => kv.Value);
            _確定単勝プールByRace[レースId] = _単勝総プール;
            _確定複勝プールByRace[レースId] = _複勝総プール;
            _締切済み = true;
            締切確定 = true;
        }

        if (締切確定)
        {
            投票締切時?.Invoke(レースId);
        }
    }

    private void 初期化(レース レース)
    {
        _現在レースId = レース.Id;
        _単勝賭け総額 = レース.出走馬リスト.ToDictionary(h => h.Id, _ => 0m);
        _複勝賭け総額 = レース.出走馬リスト.ToDictionary(h => h.Id, _ => 0m);
        _単勝オッズ = レース.出走馬リスト.ToDictionary(h => h.Id, _ => 0m);
        _複勝オッズ = レース.出走馬リスト.ToDictionary(h => h.Id, _ => 0m);
        _複勝枠数ByRace[_現在レースId] = 複勝枠数算定(レース.出走馬リスト.Count);

        var 規模 = 市場規模算定(レース);
        _平均毎秒ベット数 = 規模.毎秒ベット数;
        _平均賭け額 = 規模.平均賭け額;

        decimal 初期プール = 規模.想定賭け人 * _平均賭け額 * 0.2m;
        decimal 初期単勝プール = 初期プール * 単勝比率;
        decimal 初期複勝プール = 初期プール - 初期単勝プール;

        var 重み = レース.出走馬リスト.ToDictionary(h => h.Id, h => 強さスコア(h));
        var 合計重み = 重み.Values.Sum();
        if (合計重み <= 0) 合計重み = 1;

        foreach (var 馬 in レース.出走馬リスト)
        {
            var 取り分 = 初期単勝プール * 重み[馬.Id] / 合計重み;
            _単勝賭け総額[馬.Id] += Math.Max(1m, 取り分);
        }

        foreach (var 馬 in レース.出走馬リスト)
        {
            var 取り分 = 初期複勝プール * 重み[馬.Id] / 合計重み;
            _複勝賭け総額[馬.Id] += Math.Max(1m, 取り分);
        }

        _単勝総プール = _単勝賭け総額.Values.Sum();
        _複勝総プール = _複勝賭け総額.Values.Sum();
        オッズ再計算();
    }

    private void レース終了(レース 終了レース)
    {
        レース収支? 収支 = null;
        lock (_lock)
        {
            var raceId = 終了レース.Id;
            var 単勝投票 = _確定単勝投票ByRace.TryGetValue(raceId, out var t1) ? t1 : _単勝賭け総額;
            var 複勝投票 = _確定複勝投票ByRace.TryGetValue(raceId, out var t2) ? t2 : _複勝賭け総額;
            var 単勝オッズ = _確定単勝オッズByRace.TryGetValue(raceId, out var o1) ? o1 : _単勝オッズ;
            var 複勝オッズ = _確定複勝オッズByRace.TryGetValue(raceId, out var o2) ? o2 : _複勝オッズ;
            var 単勝総 = _確定単勝プールByRace.TryGetValue(raceId, out var p1) ? p1 : _単勝総プール;
            var 複勝総 = _確定複勝プールByRace.TryGetValue(raceId, out var p2) ? p2 : _複勝総プール;

            decimal 単勝払戻 = 0m;
            decimal 複勝払戻 = 0m;

            var 勝者Id = 終了レース.着順.FirstOrDefault();
            if (!string.IsNullOrEmpty(勝者Id)
                && 単勝投票.TryGetValue(勝者Id, out var 勝者投票)
                && 単勝オッズ.TryGetValue(勝者Id, out var 勝者オッズ))
            {
                単勝払戻 = 勝者投票 * 勝者オッズ;
            }

            int 枠数 = _複勝枠数ByRace.TryGetValue(raceId, out var n) ? n : 3;
            var 複勝対象 = 終了レース.着順.Take(枠数);
            foreach (var id in 複勝対象)
            {
                if (複勝投票.TryGetValue(id, out var 投票)
                    && 複勝オッズ.TryGetValue(id, out var odds))
                {
                    複勝払戻 += 投票 * odds;
                }
            }

            収支 = new レース収支
            {
                レースId = raceId,
                レース名 = 終了レース.レース名,
                確定時刻 = DateTime.Now,
                単勝総売上 = 単勝総,
                複勝総売上 = 複勝総,
                単勝払戻合計 = 単勝払戻,
                複勝払戻合計 = 複勝払戻
            };
        }

        if (収支 != null)
        {
            _収支履歴.Insert(0, 収支);
            収支更新時?.Invoke();
        }
    }

    private (int 想定賭け人, int 毎秒ベット数, decimal 平均賭け額) 市場規模算定(レース レース)
    {
        if (レース.グレード == レースグレード.G1) return (100000, 500, 2000m);
        if (レース.グレード == レースグレード.G2) return (60000, 300, 1500m);
        if (レース.グレード == レースグレード.G3) return (40000, 200, 1200m);

        return レース.クラス switch
        {
            競走馬クラス.オープン => (20000, 120, 1000m),
            競走馬クラス.三勝クラス => (10000, 80, 800m),
            競走馬クラス.二勝クラス => (5000, 40, 600m),
            競走馬クラス.一勝クラス => (2000, 20, 400m),
            競走馬クラス.未勝利 => (500, 6, 300m),
            競走馬クラス.新馬 => (200, 3, 300m),
            _ => (1000, 10, 400m)
        };
    }

    private decimal 強さスコア(競走馬 馬)
    {
        decimal score = 馬.スピード * 0.6m + 馬.スタミナ * 0.2m + 馬.調子 * 0.25m - 馬.疲労 * 0.25m;
        return Math.Max(1m, score);
    }

    private void 毎秒更新(object? sender, ElapsedEventArgs e)
    {
        if (_運用.現在のレース.状態 != レース状態.投票受付中) return;

        bool 変化あり = false;
        lock (_lock)
        {
            if (_運用.現在のレース.Id != _現在レースId)
            {
                初期化(_運用.現在のレース);
                変化あり = true;
            }

            if (_平均毎秒ベット数 <= 0) return;

            int min = Math.Max(1, _平均毎秒ベット数 / 2);
            int max = _平均毎秒ベット数 + (_平均毎秒ベット数 / 2);
            int 今回ベット数 = _乱数.Next(min, max + 1);

            for (int i = 0; i < 今回ベット数; i++)
            {
                var 馬Id = ランダムに馬を選ぶ(_運用.現在のレース);
                if (馬Id == null) continue;
                var 金額 = ランダム賭け額();

                bool 単勝ベット = _乱数.NextDouble() < (double)単勝比率;
                if (単勝ベット)
                {
                    _単勝賭け総額[馬Id] += 金額;
                    _単勝総プール += 金額;
                }
                else
                {
                    _複勝賭け総額[馬Id] += 金額;
                    _複勝総プール += 金額;
                }

                変化あり = true;
            }

            if (変化あり)
            {
                オッズ再計算();
            }
        }

        if (変化あり)
        {
            オッズ変動時?.Invoke();
        }
    }

    private string? ランダムに馬を選ぶ(レース レース)
    {
        if (レース.出走馬リスト.Count == 0) return null;

        var 重み = レース.出走馬リスト
            .Select(h => 強さスコア(h) + (decimal)_乱数.NextDouble() * 20m)
            .ToList();
        var 合計 = 重み.Sum();
        var 抽選 = (decimal)_乱数.NextDouble() * 合計;

        decimal 累積 = 0m;
        for (int i = 0; i < レース.出走馬リスト.Count; i++)
        {
            累積 += 重み[i];
            if (抽選 <= 累積) return レース.出走馬リスト[i].Id;
        }

        return レース.出走馬リスト.Last().Id;
    }

    private decimal ランダム賭け額()
    {
        decimal min = Math.Max(100m, _平均賭け額 * 0.4m);
        decimal max = _平均賭け額 * 2.5m;
        var amount = (decimal)_乱数.NextDouble() * (max - min) + min;
        return Math.Round(amount / 100m, 0) * 100m;
    }

    private void オッズ再計算()
    {
        単勝オッズ再計算();
        複勝オッズ再計算();
    }

    private void 単勝オッズ再計算()
    {
        var 配当プール = _単勝総プール * (1m - 控除率);
        if (配当プール <= 0) 配当プール = 1m;

        foreach (var 馬Id in _単勝賭け総額.Keys.ToList())
        {
            var 投票額 = _単勝賭け総額[馬Id];
            decimal odds = 投票額 <= 0 ? 最大オッズ : 配当プール / 投票額;
            odds = Math.Clamp(odds, 最低オッズ, 最大オッズ);
            _単勝オッズ[馬Id] = Math.Round(odds, 1);
        }
    }

    private void 複勝オッズ再計算()
    {
        var 配当プール = _複勝総プール * (1m - 控除率);
        int 枠数 = _複勝枠数ByRace.TryGetValue(_現在レースId, out var n) ? n : 3;
        var 枠割プール = 配当プール / Math.Max(1, 枠数);
        if (枠割プール <= 0) 枠割プール = 1m;

        foreach (var 馬Id in _複勝賭け総額.Keys.ToList())
        {
            var 投票額 = _複勝賭け総額[馬Id];
            decimal odds = 投票額 <= 0 ? 最大オッズ : 枠割プール / 投票額;
            odds = Math.Clamp(odds, 最低オッズ, 最大オッズ);
            _複勝オッズ[馬Id] = Math.Round(odds, 1);
        }
    }

    public decimal 現在オッズ(string 馬Id)
    {
        lock (_lock)
        {
            if (_確定単勝オッズByRace.TryGetValue(_現在レースId, out var 確定)
                && 確定.TryGetValue(馬Id, out var 確定オッズ))
            {
                return 確定オッズ;
            }

            return _単勝オッズ.TryGetValue(馬Id, out var odds) ? odds : 0m;
        }
    }

    public decimal 現在複勝オッズ(string 馬Id)
    {
        lock (_lock)
        {
            if (_確定複勝オッズByRace.TryGetValue(_現在レースId, out var 確定)
                && 確定.TryGetValue(馬Id, out var 確定オッズ))
            {
                return 確定オッズ;
            }

            return _複勝オッズ.TryGetValue(馬Id, out var odds) ? odds : 0m;
        }
    }

    public decimal 確定単勝オッズ取得(string レースId, string 馬Id)
    {
        lock (_lock)
        {
            if (_確定単勝オッズByRace.TryGetValue(レースId, out var 確定)
                && 確定.TryGetValue(馬Id, out var 確定オッズ))
            {
                return 確定オッズ;
            }

            return _単勝オッズ.TryGetValue(馬Id, out var odds) ? odds : 0m;
        }
    }

    public decimal 確定複勝オッズ取得(string レースId, string 馬Id)
    {
        lock (_lock)
        {
            if (_確定複勝オッズByRace.TryGetValue(レースId, out var 確定)
                && 確定.TryGetValue(馬Id, out var 確定オッズ))
            {
                return 確定オッズ;
            }

            return _複勝オッズ.TryGetValue(馬Id, out var odds) ? odds : 0m;
        }
    }

    public void プレイヤー投票(string 馬Id, decimal 金額, 券種 種別)
    {
        lock (_lock)
        {
            if (!_単勝賭け総額.ContainsKey(馬Id)) return;

            switch (種別)
            {
                case 券種.複勝:
                    _複勝賭け総額[馬Id] += 金額;
                    _複勝総プール += 金額;
                    break;
                default:
                    _単勝賭け総額[馬Id] += 金額;
                    _単勝総プール += 金額;
                    break;
            }

            オッズ再計算();
        }
        オッズ変動時?.Invoke();
    }

    public void Dispose()
    {
        _更新タイマー?.Stop();
        _更新タイマー?.Dispose();
        _運用.レース切替時 -= レース切替;
        _運用.レース状態変更時 -= 状態変化;
        _運用.レース終了時 -= レース終了;
    }

    private static int 複勝枠数算定(int 出走頭数)
    {
        if (出走頭数 >= 8) return 3;
        if (出走頭数 >= 5) return 2;
        return 1;
    }

    private readonly List<レース収支> _収支履歴 = new();
    public IReadOnlyList<レース収支> 収支履歴 => _収支履歴;
}
