using KeibaOps.Models;

namespace KeibaOps.Services;

public class 馬個体管理サービス
{
    public List<競走馬> 全競走馬リスト { get; private set; } = new();
    private 馬名生成サービス _命名;
    private Random _乱数 = new Random();

    public 馬個体管理サービス(馬名生成サービス 命名)
    {
        _命名 = 命名;
        初期化(1000);
    }

    private void 初期化(int 頭数)
    {
        全競走馬リスト.Clear();
        for (int i = 0; i < 頭数; i++)
        {
            var 馬 = 生成(i);
            全競走馬リスト.Add(馬);
        }
    }

    private 競走馬 生成(int seed)
    {
        // クラス分布をシミュレート
        // 新馬/未勝利: 50%, 1勝: 20%, 2勝: 15%, 3勝: 10%, OP: 5%
        var r = _乱数.NextDouble();
        競走馬クラス クラス;
        int スピード補正 = 0;

        if (r < 0.5) { クラス = 競走馬クラス.未勝利; スピード補正 = 0; }
        else if (r < 0.7) { クラス = 競走馬クラス.一勝クラス; スピード補正 = 10; }
        else if (r < 0.85) { クラス = 競走馬クラス.二勝クラス; スピード補正 = 20; }
        else if (r < 0.95) { クラス = 競走馬クラス.三勝クラス; スピード補正 = 30; }
        else { クラス = 競走馬クラス.オープン; スピード補正 = 40; }

        return new 競走馬
        {
            名前 = _命名.生成(),
            性別 = (性別)_乱数.Next(2),
            毛色 = ランダムな色を取得(),
            スピード = Math.Clamp(_乱数.Next(40, 60) + スピード補正, 1, 100),
            スタミナ = _乱数.Next(40, 100),
            加速力 = _乱数.Next(30, 100),
            脚質 = (脚質)_乱数.Next(4),
            年齢 = _乱数.Next(2, 8),
            クラス = クラス,
            調子 = _乱数.Next(80, 100),
            父名 = _命名.生成(),
            母名 = _命名.生成()
        };
    }

    // レース条件に合う馬をランダムに選出
    // 連闘防止ロジック: 疲労が高い、または直近に出走している馬は除外
    public List<競走馬> 出走馬選出(競走馬クラス 条件クラス, int 頭数)
    {
        return 全競走馬リスト
            .Where(h => h.クラス == 条件クラス && h.状態 == 競走馬状態.現役)
            .Where(h => h.疲労 < 80) // 疲労困憊なら出ない
            .Where(h => (DateTime.Now - h.最終出走時刻).TotalSeconds > 20) // 20秒（擬似的な数週間）は間隔を空ける
            .OrderBy(_ => _乱数.Next()) // シャッフル
            .Take(頭数)
            .ToList();
    }

    public void 全頭疲労回復()
    {
        foreach (var 馬 in 全競走馬リスト)
        {
            if (馬.疲労 > 0)
            {
                馬.疲労 = Math.Max(0, 馬.疲労 - 5);
            }
        }
    }

    private string ランダムな色を取得()
    {
        var 色リスト = new[] { "#E74C3C", "#8E44AD", "#3498DB", "#1ABC9C", "#F1C40F", "#E67E22", "#95A5A6", "#34495E" };
        return 色リスト[_乱数.Next(色リスト.Length)];
    }
}
