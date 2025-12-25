using KeibaOps.Models;

namespace KeibaOps.Services;

public class 繁殖サービス
{
    private 財布サービス _財布;
    private Random _乱数 = new Random();

    public event Action? 牧場更新時;

    public 繁殖サービス(財布サービス 財布)
    {
        _財布 = 財布;
    }

    public void 引退させる(競走馬 対象馬)
    {
        if (対象馬.状態 != 競走馬状態.現役) return;
        
        対象馬.状態 = 競走馬状態.引退; // 引退して繁殖入り
        牧場更新時?.Invoke();
    }

    // 繁殖 (配合)
    public 競走馬? 繁殖させる(競走馬 父, 競走馬 母)
    {
        // 基本的なバリデーション
        if (父.状態 != 競走馬状態.引退 || 母.状態 != 競走馬状態.引退) return null;
        if (父.性別 == 母.性別) return null; // 同性配合不可

        // ステータス遺伝ロジック
        // 両親の平均 + ランダム変異 (-10 to +10)
        var スピード遺伝 = (父.スピード + 母.スピード) / 2;
        var スタミナ遺伝 = (父.スタミナ + 母.スタミナ) / 2;

        var 新馬 = new 競走馬
        {
            名前 = 幼名生成(父.名前, 母.名前),
            性別 = (性別)_乱数.Next(2),
            スピード = Math.Clamp(スピード遺伝 + _乱数.Next(-10, 15), 1, 100), // 少し上振れしやすくする
            スタミナ = Math.Clamp(スタミナ遺伝 + _乱数.Next(-10, 15), 1, 100),
            毛色 = _乱数.Next(2) == 0 ? 父.毛色 : 母.毛色, // メンデルの法則無視で50%
            年齢 = 2,
            状態 = 競走馬状態.現役, // 即デビュー
            価格 = 0, // 自家生産なのでタダ
            父名 = 父.名前,
            母名 = 母.名前
        };

        // 親として登録できれば良いが、今はLineageデータ構造がないので省略
        // ユーザーの所有馬に追加
        _財布.ユーザー.所有馬リスト.Add(新馬);
        
        牧場更新時?.Invoke();
        return 新馬;
    }

    private string 幼名生成(string 父名, string 母名)
    {
        // 簡易的な名前合成
        var 父部 = 父名.Length > 2 ? 父名.Substring(0, 2) : 父名;
        var 母部 = 母名.Length > 2 ? 母名.Substring(母名.Length - 2) : 母名;
        return $"{父部}{母部}{_乱数.Next(100)}";
    }
}
