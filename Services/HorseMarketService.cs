using KeibaOps.Models;

namespace KeibaOps.Services;

public class 馬市場サービス
{
    public List<競走馬> 販売馬リスト { get; private set; } = new();
    public event Action? 市場更新時;
    
    private Random _乱数 = new Random();
    private 財布サービス _財布;
    private 馬名生成サービス _命名;

    public 馬市場サービス(財布サービス 財布, 馬名生成サービス 命名)
    {
        _財布 = 財布;
        _命名 = 命名;
        市場更新();
    }

    public void 市場更新()
    {
        販売馬リスト.Clear();
        // ランダムに5頭生成
        for (int i = 0; i < 5; i++)
        {
            var スピード = _乱数.Next(50, 200);
            var スタミナ = _乱数.Next(50, 200);
            // 能力が高いほど高い
            var 価格 = (スピード + スタミナ) * 10; 
            
            販売馬リスト.Add(new 競走馬
            {
                名前 = _命名.生成(),
                性別 = (性別)_乱数.Next(2),
                スピード = スピード,
                スタミナ = スタミナ,
                脚質 = (脚質)_乱数.Next(4),
                毛色 = ランダムな色を取得(),
                価格 = 価格,
                年齢 = 2,
                父名 = _命名.生成(),
                母名 = _命名.生成()
            });
        }
        市場更新時?.Invoke();
    }

    public bool 馬購入(競走馬 対象馬)
    {
        if (_財布.ユーザー.所持金 < 対象馬.価格) return false;
        if (!販売馬リスト.Contains(対象馬)) return false;

        _財布.支払い(対象馬.価格);
        _財布.ユーザー.所有馬リスト.Add(対象馬);
        販売馬リスト.Remove(対象馬);
        
        市場更新時?.Invoke();
        
        return true;
    }



    private string ランダムな色を取得()
    {
        var 色リスト = new[] { "#E74C3C", "#8E44AD", "#3498DB", "#1ABC9C", "#F1C40F", "#E67E22", "#95A5A6", "#34495E" };
        return 色リスト[_乱数.Next(色リスト.Length)];
    }
}
