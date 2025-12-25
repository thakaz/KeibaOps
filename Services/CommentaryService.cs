using KeibaOps.Models;

namespace KeibaOps.Services;

public class 実況サービス
{
    private Random _乱数 = new Random();

    public string 実況生成(レース レース)
    {
        // レース開始前
        if (レース.状態 == レース状態.投票受付中) return "投票受付中... まもなく発走です！";
        
        // 勝者が決まった瞬間（最初のゴール）
        if (レース.着順.Any())
        {
            var 勝者Id = レース.着順.FirstOrDefault();
            var 勝者 = レース.出走馬リスト.FirstOrDefault(h => h.Id == 勝者Id);
            return $"ゴールイン！！ 勝ったのは... {勝者?.名前} だぁぁぁ！！！";
        }

        // レース中
        var 先頭 = レース.出走馬リスト.OrderByDescending(h => レース.各馬の進捗[h.Id]).FirstOrDefault();
        if (先頭 == null) return "";

        var 進捗 = レース.各馬の進捗[先頭.Id];

        // サイバー空間用パロディ実況
        if (レース.コース == コース種別.サイバー空間)
        {
            if (進捗 < 0.2) return $"各馬一斉にログイン！ {先頭.名前} が通信速度を上げていきます！";
            if (進捗 < 0.5) return $"第3セクターを通過！ {先頭.名前} のping値が良好！";
            if (進捗 < 0.8) return $"ファイアウォールを突破して {先頭.名前} が先頭！";
            return $"残り僅か！ {先頭.名前} がダウンロード完了目前！！";
        }

        // 通常実況
        if (進捗 < 0.1) return "各馬一斉にスタート！ きれいなスタートを切りました！";
        if (進捗 < 0.3) return $"先頭は {先頭.名前}、リードを広げにかかります！";
        if (進捗 < 0.6) return $"第3コーナーを回って、依然として {先頭.名前} が先頭！";
        if (進捗 < 0.8) return $"第4コーナーを回った！ さあ最後の直線だ！";
        if (進捗 < 0.9) return $"残り200！ {先頭.名前} 粘る！ 後続も来ているぞ！";
        
        return $"{先頭.名前} 先頭！ {先頭.名前} 先頭！ そのまま押し切れるか！！";
    }
}
