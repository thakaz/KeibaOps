using KeibaOps.Models;

namespace KeibaOps.Services;

public class 財布サービス
{
    public 相場師 ユーザー { get; private set; } = new();
    public event Action? 残高変動時;

    public void 馬券購入(レース レース, 競走馬 馬, decimal 金額)
    {
        if (ユーザー.所持金 < 金額) return;

        ユーザー.所持金 -= 金額;
        
        // 簡易オッズ計算: 100 / スピード * 5
        decimal オッズ = Math.Round(100.0m / 馬.スピード * 5.0m, 1);

        ユーザー.購入馬券リスト.Add(new 馬券
        {
            レースId = レース.Id,
            馬Id = 馬.Id,
            馬名 = 馬.名前,
            購入額 = 金額,
            オッズ = オッズ
        });

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
            if (馬券.馬Id == 勝者.Id)
            {
                馬券.払戻金 = 馬券.購入額 * 馬券.オッズ;
                ユーザー.所持金 += 馬券.払戻金;
            }
            馬券.確定済み = true;
        }
        
        状態通知();
    }

    private void 状態通知() => 残高変動時?.Invoke();
}
