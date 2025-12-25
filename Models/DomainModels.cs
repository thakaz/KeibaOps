namespace KeibaOps.Models;

public enum 競走馬状態 { 現役, 引退, 繁殖 }
public enum 性別 { 牡, 牝 }
public enum レース状態 { 投票受付中, 出走中, 終了 }
public enum 競走馬クラス { 新馬, 未勝利, 一勝クラス, 二勝クラス, 三勝クラス, オープン }
public enum レースグレード { 一般, G3, G2, G1 }
public enum 脚質 { 逃げ, 先行, 差し, 追込 }
public enum コース種別 { 芝, ダート, 障害, サイバー空間 }
public enum 券種 { 単勝, 複勝, ワイド, 馬連, 馬単, 三連複, 三連単 }

public class 競走馬
{
    public string Id { get; set; } = Guid.NewGuid().ToString().Substring(0, 8);
    public string 名前 { get; set; } = "名無しの馬";
    public 性別 性別 { get; set; } = 性別.牡;
    public string 毛色 { get; set; } = "#8B4513"; // Hex color
    
    // ステータス (0-100)
    public int スピード { get; set; }
    public int スタミナ { get; set; }
    public 脚質 脚質 { get; set; } = 脚質.先行;
    public int ゲート { get; set; }
    public int 調子 { get; set; } = 100;
    public int 疲労 { get; set; } = 0; // 0-100, 100 is exhausted
    public string 父名 { get; set; } = "不明";
    public string 母名 { get; set; } = "不明";

    // 市場価値
    public decimal 価格 { get; set; } = 1000m;
    public int 年齢 { get; set; } = 2; // 2歳新馬

    // キャリア
    public DateTime 最終出走時刻 { get; set; } = DateTime.MinValue;
    public 競走馬状態 状態 { get; set; } = 競走馬状態.現役;
    public 競走馬クラス クラス { get; set; } = 競走馬クラス.新馬;
    public int 出走数 { get; set; }
    public int 勝利数 { get; set; }
    public decimal 獲得賞金 { get; set; }
}

public class レース
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string レース名 { get; set; } = "未勝利戦";
    public レースグレード グレード { get; set; } = レースグレード.一般;
    public 競走馬クラス クラス { get; set; } = 競走馬クラス.未勝利; 
    public コース種別 コース { get; set; } = コース種別.芝;
    public int 距離 { get; set; } = 1200; // Meters
    public List<競走馬> 出走馬リスト { get; set; } = new();
    public レース状態 状態 { get; set; } = レース状態.投票受付中;
    
    // シミュレーション用
    public Dictionary<string, double> 各馬の進捗 { get; set; } = new();
    public List<string> 着順 { get; set; } = new();
}

public class 相場師
{
    public decimal 所持金 { get; set; } = 1000000000000m;
    public List<競走馬> 所有馬リスト { get; set; } = new();
    public List<馬券> 購入馬券リスト { get; set; } = new();
}

public class 馬券
{
    public string レースId { get; set; } = "";
    public string 馬Id { get; set; } = "";
    public string 馬名 { get; set; } = ""; // 表示用スナップショット
    public 券種 種別 { get; set; } = 券種.単勝;
    public List<string> 対象馬Idリスト { get; set; } = new();
    public decimal 購入額 { get; set; }
    public decimal オッズ { get; set; }
    public bool 確定済み { get; set; }
    public decimal 払戻金 { get; set; }
}

public class レース収支
{
    public string レースId { get; set; } = "";
    public string レース名 { get; set; } = "";
    public DateTime 確定時刻 { get; set; } = DateTime.Now;
    public decimal 単勝総売上 { get; set; }
    public decimal 複勝総売上 { get; set; }
    public decimal 単勝払戻合計 { get; set; }
    public decimal 複勝払戻合計 { get; set; }
    public decimal 総売上 => 単勝総売上 + 複勝総売上;
    public decimal 総払戻 => 単勝払戻合計 + 複勝払戻合計;
    public decimal 利益 => 総売上 - 総払戻;
}
