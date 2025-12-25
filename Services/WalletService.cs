using KeibaOps.Models;

namespace KeibaOps.Services;

public class 財布サービス
{
    private readonly オッズ市場サービス _オッズ市場;
    public 相場師 ユーザー { get; private set; } = new();
    public event Action? 残高変動時;

    public 財布サービス(オッズ市場サービス オッズ市場)
    {
        _オッズ市場 = オッズ市場;
        _オッズ市場.投票締切時 += 投票締切処理;
    }

    public void 馬券購入(レース レース, 競走馬 馬, decimal 金額)
    {
        馬券購入(レース, 馬, 金額, 券種.単勝);
    }

    public void 馬券購入(レース レース, 競走馬 馬, decimal 金額, 券種 種別)
    {
        if (ユーザー.所持金 < 金額) return;

        ユーザー.所持金 -= 金額;
        decimal オッズ = 種別 switch
        {
            券種.複勝 => _オッズ市場.現在複勝オッズ(馬.Id),
            _ => _オッズ市場.現在オッズ(馬.Id)
        };
        if (オッズ <= 0) オッズ = Math.Round(100.0m / 馬.スピード * 5.0m, 1);

        ユーザー.購入馬券リスト.Add(new 馬券
        {
            レースId = レース.Id,
            馬Id = 馬.Id,
            馬名 = 馬.名前,
            種別 = 種別,
            対象馬Idリスト = new List<string> { 馬.Id },
            購入額 = 金額,
            オッズ = オッズ
        });

        _オッズ市場.プレイヤー投票(馬.Id, 金額, 種別);
        状態通知();
    }

    public void 支払い(decimal 金額)
    {
        if (ユーザー.所持金 < 金額) return;
        ユーザー.所持金 -= 金額;
        状態通知();
    }

    public void レース結果処理(レース 終了レース)
    {
        // 勝者を判定
        var 勝者Id = 終了レース.着順.FirstOrDefault();
        if (勝者Id == null) return;
        
        var 勝者 = 終了レース.出走馬リスト.FirstOrDefault(h => h.Id == 勝者Id);
        if (勝者 == null) return;

        if (勝者 == null) return;

        var 対象馬券リスト = ユーザー.購入馬券リスト
            .Where(b => b.レースId == 終了レース.Id && !b.確定済み)
            .ToList();

        if (!対象馬券リスト.Any()) return;

        foreach (var 馬券 in 対象馬券リスト)
        {
            var 対象Id = 馬券.対象馬Idリスト.Any() ? 馬券.対象馬Idリスト : new List<string> { 馬券.馬Id };
            if (馬券.種別 == 券種.単勝 && 対象Id.Contains(勝者.Id))
            {
                馬券.払戻金 = 馬券.購入額 * 馬券.オッズ;
                ユーザー.所持金 += 馬券.払戻金;
            }
            else if (馬券.種別 == 券種.複勝)
            {
                int 枠数 = 複勝枠数算定(終了レース.出走馬リスト.Count);
                var 複勝対象 = 終了レース.着順.Take(枠数).ToHashSet();
                if (対象Id.Any(id => 複勝対象.Contains(id)))
                {
                    馬券.払戻金 = 馬券.購入額 * 馬券.オッズ;
                    ユーザー.所持金 += 馬券.払戻金;
                }
            }
            馬券.確定済み = true;
        }
        
        状態通知();
    }

    private void 状態通知() => 残高変動時?.Invoke();

    private void 投票締切処理(string レースId)
    {
        var 対象馬券リスト = ユーザー.購入馬券リスト
            .Where(b => b.レースId == レースId && !b.確定済み)
            .ToList();

        if (!対象馬券リスト.Any()) return;

        foreach (var 馬券 in 対象馬券リスト)
        {
            馬券.オッズ = 馬券.種別 switch
            {
                券種.複勝 => _オッズ市場.確定複勝オッズ取得(レースId, 馬券.馬Id),
                _ => _オッズ市場.確定単勝オッズ取得(レースId, 馬券.馬Id)
            };
        }

        状態通知();
    }

    private static int 複勝枠数算定(int 出走頭数)
    {
        if (出走頭数 >= 8) return 3;
        if (出走頭数 >= 5) return 2;
        return 1;
    }
}
