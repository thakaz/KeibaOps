using System.Timers;
using KeibaOps.Models;

namespace KeibaOps.Services;

public class レース運用サービス : IDisposable
{
    private System.Timers.Timer _ループタイマー;
    public レース 現在のレース { get; private set; } = new();
    private readonly List<レース> _予定レースリスト = new();
    public event Action? レース状態変更時;
    public event Action<レース>? レース切替時;
    public event Action<レース>? レース終了時;

    private Random _乱数 = new Random();
    private 馬個体管理サービス _個体管理;
    private const int 予定レース数 = 3;
    private const double 横位置最小 = 0.08;
    private const double 横位置最大 = 0.92;
    private const double 体幅 = 0.08;
    private const double 横移動速度 = 0.012;
    private const double ブロック距離 = 6.0;
    private const double 最低車間距離 = 3.0;
    private const double 側方チェック距離 = 5.0;
    private const double 後方チェック距離 = 2.0;
    private const double 加速係数最小 = 0.6;
    private const double 加速係数最大 = 1.4;
    private const double 基本加速mps2 = 4.0;
    private const double スタート加速補正 = 1.3;
    private const double 巡航加速度mps2 = 6.0;
    private const double スタミナ消費基準 = 0.0012;
    private const double スタミナスパート消費 = 0.006;
    private const double スタート初速割合 = 0.6;

    public レース運用サービス(馬個体管理サービス 個体管理)
    {
        _個体管理 = 個体管理;
        現在のレース = レース生成();
        予定レース補充();
        状態通知();
        レース切替時?.Invoke(現在のレース);
        _ループタイマー = new System.Timers.Timer(100); // 10 ticks per second
        _ループタイマー.Elapsed += ゲームループ;
        _ループタイマー.Start();
    }

    public IReadOnlyList<レース> 予定レース一覧 => _予定レースリスト;
    public レース? 次のレース => _予定レースリスト.FirstOrDefault();

    private void 予定レース補充()
    {
        while (_予定レースリスト.Count < 予定レース数)
        {
            _予定レースリスト.Add(レース生成());
        }
    }

    private レース レース生成()
    {
        // ランダムにレースクラスを決定
        var r = _乱数.NextDouble();
        競走馬クラス クラス;
        string レース名サフィックス = "";
        レースグレード グレード = レースグレード.一般;
        
        if (r < 0.4) { クラス = 競走馬クラス.未勝利; レース名サフィックス = "未勝利戦"; }
        else if (r < 0.6) { クラス = 競走馬クラス.一勝クラス; レース名サフィックス = "1勝クラス"; }
        else if (r < 0.8) { クラス = 競走馬クラス.二勝クラス; レース名サフィックス = "2勝クラス"; }
        else if (r < 0.9) { クラス = 競走馬クラス.三勝クラス; レース名サフィックス = "3勝クラス"; }
        else 
        { 
            クラス = 競走馬クラス.オープン; 
            if(_乱数.NextDouble() > 0.5) 
            {
                レース名サフィックス = "G1 天皇賞"; // 仮
                グレード = レースグレード.G1;
            }
            else 
            {
                レース名サフィックス = "オープン特別";
            }
        }

        // コース設定
        var rCourse = _乱数.NextDouble();
        コース種別 コース;
        if (rCourse < 0.5) コース = コース種別.芝;
        else if (rCourse < 0.9) コース = コース種別.ダート;
        else if (rCourse < 0.98) コース = コース種別.障害;
        else コース = コース種別.サイバー空間;
        
        // サイバー空間なら名前を変える
        if (コース == コース種別.サイバー空間) レース名サフィックス = "電脳賞";

        var 新レース = new レース
        {
            レース名 = $"第{DateTime.Now.Ticks % 1000}回 {レース名サフィックス}",
            グレード = グレード,
            クラス = クラス,
            コース = コース,
            距離 = _乱数.Next(3, 8) * 400, // 1200m to 2800m
            状態 = レース状態.投票受付中
        };

        // 個体管理サービスから出走馬を選出 (8頭)
        // プレイヤー枠を空けるため、最初は7頭にする？
        // いや、とりあえず8頭埋めて、プレイヤーが入るときに1頭弾き出す方式にしよう。
        var 候補馬 = _個体管理.出走馬選出(クラス, 8);
        
        foreach(var 馬 in 候補馬)
        {
            新レース.出走馬リスト.Add(馬);
            新レース.各馬の進捗[馬.Id] = 0;
            新レース.各馬の横位置[馬.Id] = 初期横位置(馬);
            新レース.各馬の不発フラグ[馬.Id] = 不発抽選(馬);
            新レース.各馬の現在速度[馬.Id] = 初期巡航速度(馬) * スタート初速割合;
            新レース.各馬のスタート遅延ティック[馬.Id] = スタート遅延抽選(馬);
            新レース.各馬のスタミナ残量[馬.Id] = 1.0;
        }

        return 新レース;
    }

    public bool 出走登録(競走馬 自分の馬)
    {
        var 対象レース = 次のレース;
        if (対象レース == null) return false;
        if (対象レース.状態 != レース状態.投票受付中) return false;
        if (対象レース.出走馬リスト.Any(h => h.Id == 自分の馬.Id)) return false; // 既に登録済み
        
        // クラスチェック
        bool クラスOK = false;
        
        // 1. 完全一致
        if (自分の馬.クラス == 対象レース.クラス) クラスOK = true;
        // 2. 新馬 -> 未勝利 はOK
        if (自分の馬.クラス == 競走馬クラス.新馬 && 対象レース.クラス == 競走馬クラス.未勝利) クラスOK = true;
        // 3. オープン -> G1 はOK
        if (自分の馬.クラス == 競走馬クラス.オープン && 対象レース.グレード == レースグレード.G1) クラスOK = true;

        if (!クラスOK) return false;

        // 枠があるか？なければNPCを1頭除外
        if (対象レース.出走馬リスト.Count >= 8)
        {
            var 除外馬 = 対象レース.出走馬リスト.Last(); // 末尾（ランダム選出なので誰でもいい）
            対象レース.出走馬リスト.Remove(除外馬);
            対象レース.各馬の進捗.Remove(除外馬.Id);
        }

        対象レース.出走馬リスト.Add(自分の馬);
        対象レース.各馬の進捗[自分の馬.Id] = 0;
        対象レース.各馬の横位置[自分の馬.Id] = 初期横位置(自分の馬);
        対象レース.各馬の不発フラグ[自分の馬.Id] = 不発抽選(自分の馬);
        対象レース.各馬の現在速度[自分の馬.Id] = 初期巡航速度(自分の馬) * スタート初速割合;
        対象レース.各馬のスタート遅延ティック[自分の馬.Id] = スタート遅延抽選(自分の馬);
        対象レース.各馬のスタミナ残量[自分の馬.Id] = 1.0;
        
        状態通知();
        return true;
    }
    
    private int _投票待機ティック = 0; 
    private const int 投票期間ティック = 200;
    private int _回復ティック = 0;
    
    public int 投票残り秒
    {
        get
        {
            if (現在のレース.状態 != レース状態.投票受付中) return 0;
            int 残りティック = Math.Max(0, 投票期間ティック - _投票待機ティック);
            return (int)Math.Ceiling(残りティック / 10.0);
        }
    }

    private void ゲームループ(object? sender, ElapsedEventArgs e)
    {
        // 10秒に1回、全頭の疲労回復
        if (++_回復ティック >= 100) 
        {
            _個体管理.全頭疲労回復();
            _回復ティック = 0;
        }

        if (現在のレース.状態 == レース状態.投票受付中)
        {
            _投票待機ティック++;
            if (_投票待機ティック > 投票期間ティック)
            {
                現在のレース.状態 = レース状態.出走中;
                _投票待機ティック = 0;
                状態通知();
            }
            else if (_投票待機ティック % 10 == 0)
            {
                状態通知();
            }
        }
        else if (現在のレース.状態 == レース状態.出走中)
        {
            bool 全馬ゴール = true;
            var 出走馬 = 現在のレース.出走馬リスト;
            var 距離 = 現在のレース.距離;
            var 現在距離ById = 出走馬.ToDictionary(
                h => h.Id,
                h => Math.Max(0, Math.Min(1.0, 現在のレース.各馬の進捗[h.Id])) * 距離
            );
            var 順位リスト = 出走馬
                .OrderByDescending(h => 現在距離ById[h.Id])
                .ToList();
            var 順位ById = 順位リスト
                .Select((h, i) => new { h.Id, Index = i })
                .ToDictionary(x => x.Id, x => x.Index);
            var 横位置ById = 出走馬.ToDictionary(
                h => h.Id,
                h => 現在のレース.各馬の横位置.TryGetValue(h.Id, out var y) ? y : 0.5
            );

            foreach (var 馬 in 現在のレース.出走馬リスト)
            {
                double 現在位置 = 現在のレース.各馬の進捗[馬.Id];
                if (現在位置 < 1.0)
                {
                    全馬ゴール = false;

                    if (現在のレース.各馬のスタート遅延ティック.TryGetValue(馬.Id, out var delay) && delay > 0)
                    {
                        現在のレース.各馬のスタート遅延ティック[馬.Id] = delay - 1;
                        double 遅延減速 = Math.Clamp(1.0 - (delay * 0.03), 0.5, 1.0);
                        double 初速 = 初期巡航速度(馬) * スタート初速割合 * 遅延減速;
                        現在のレース.各馬の現在速度[馬.Id] = 初速;
                        continue;
                    }
                    
                    // 速度計算 (m/s)
                    // 基本スピード(15m/s) + 能力値補正(0-100 -> 0-5m/s) + 乱数
                    double リアル秒速 = 15.0 + (馬.スピード / 20.0) + ((_乱数.NextDouble() - 0.5) * 2.0);

                    double p = 現在位置;

                    // 巡航・位置取り・隊列 (脚質が活きるように調整)
                    int 順位 = 順位ById[馬.Id];
                    int 参加頭数 = Math.Max(1, 出走馬.Count - 1);
                    double 現在順位率 = 順位 / (double)参加頭数; // 0:先頭, 1:最後方
                    double 目標順位率 = 馬.脚質 switch
                    {
                        脚質.逃げ => 0.1,
                        脚質.先行 => 0.3,
                        脚質.差し => 0.65,
                        _ => 0.85
                    };
                    double 位置取り補正 = 1.0 + Math.Clamp(目標順位率 - 現在順位率, -0.5, 0.5) * 0.08;

                    bool 追込不発 = 現在のレース.各馬の不発フラグ.TryGetValue(馬.Id, out var flag) && flag;
                    double staminaRemain = 現在のレース.各馬のスタミナ残量.TryGetValue(馬.Id, out var stamina) ? stamina : 1.0;
                    double accelNorm = 加速正規化(馬);
                    double 脚質補正 = 脚質速度カーブ補正(馬, p, 追込不発, accelNorm, staminaRemain, out var 仕掛け負荷, out var スパート中);
                    double スタミナ補正 = スタミナ消耗補正(馬, p, 仕掛け負荷, staminaRemain);

                    // スリップストリーム：前を走る馬が近いほど微加速
                    double スリップストリーム補正 = 1.0;
                    if (順位 > 0)
                    {
                        var 直前馬 = 順位リスト[順位 - 1];
                        double ギャップ = 現在距離ById[直前馬.Id] - 現在距離ById[馬.Id];
                        if (ギャップ > 0 && ギャップ < 7.0)
                        {
                            スリップストリーム補正 = 1.0 + ((7.0 - ギャップ) / 7.0) * 0.03;
                        }
                    }

                    // ソラ：大きく抜け出すと気を抜きがち（終盤は解除）
                    double ソラ補正 = 1.0;
                    if (順位 == 0 && 順位リスト.Count > 1)
                    {
                        var 二番手 = 順位リスト[1];
                        double 先頭差 = 現在距離ById[順位リスト[0].Id] - 現在距離ById[二番手.Id];
                        if (先頭差 > 8.0 && p < 0.85)
                        {
                            ソラ補正 = 0.97;
                        }
                    }

                    // コーナー区間はわずかに減速
                    double コーナー補正 = (p > 0.42 && p < 0.52) || (p > 0.72 && p < 0.82) ? 0.97 : 1.0;

                    // 進路取りと横移動
                    double 現在横位置 = 横位置ById[馬.Id];
                    double 目標横位置 = 目標横位置算定(馬, p, 現在順位率);
                    bool ブロック = ブロック判定(馬, 現在距離ById, 横位置ById);
                    double ブロック補正 = 1.0;
                    double 車間補正 = 車間補正算定(馬, 現在距離ById, 横位置ById);

                    if (ブロック)
                    {
                        var 回避先 = 回避横位置(馬, 現在距離ById, 横位置ById);
                        if (回避先.HasValue)
                        {
                            目標横位置 = 回避先.Value;
                            ブロック補正 = 0.97;
                        }
                        else
                        {
                            ブロック補正 = 0.90;
                        }
                    }

                    double 横差 = Math.Clamp(目標横位置 - 現在横位置, -横移動速度, 横移動速度);
                    double 次横位置 = Math.Clamp(現在横位置 + 横差, 横位置最小, 横位置最大);
                    現在のレース.各馬の横位置[馬.Id] = 次横位置;

                    // 外を回るほど距離ロス
                    double 外回し補正 = 1.0 - ((次横位置 - 横位置最小) / (横位置最大 - 横位置最小)) * 0.05;
                    外回し補正 = Math.Clamp(外回し補正, 0.92, 1.0);

                    // ゲーム的倍速 (20秒で2000m走る -> 100m/s)
                    double 基準秒速 = リアル秒速 * 5.5
                        * 位置取り補正
                        * スタミナ補正
                        * スリップストリーム補正
                        * ソラ補正
                        * コーナー補正
                        * ブロック補正
                        * 車間補正
                        * 外回し補正;
                    double 巡航秒速 = 基準秒速;
                    double 目標秒速 = 基準秒速 * 脚質補正;

                    double 現在秒速 = 現在のレース.各馬の現在速度.TryGetValue(馬.Id, out var v) ? v : 0.0;
                    double 加速係数 = 加速係数最小 + (加速係数最大 - 加速係数最小) * accelNorm;
                    double startBoost = p < 0.08 ? スタート加速補正 : 1.0;
                    double 巡航加速度 = 巡航加速度mps2 * startBoost;
                    double スパート加速度 = 基本加速mps2 * 加速係数 * startBoost;
                    double 加速度 = 現在秒速 < (巡航秒速 - 0.5) ? 巡航加速度 : スパート加速度;
                    double tick最大増減 = 加速度 * 0.1;
                    double 速度差 = Math.Clamp(目標秒速 - 現在秒速, -tick最大増減, tick最大増減);
                    double 次秒速 = Math.Max(0.0, 現在秒速 + 速度差);
                    現在のレース.各馬の現在速度[馬.Id] = 次秒速;

                    // スタミナ消費: 巡航は低消費、スパートで消費増
                    double staminaCost = スタミナ消費基準 + (スパート中 ? スタミナスパート消費 : 0.0);
                    double staminaQuality = Math.Clamp(馬.スタミナ / 100.0, 0.4, 1.2);
                    staminaCost *= Math.Clamp(1.2 - staminaQuality * 0.6, 0.6, 1.2);
                    staminaRemain = Math.Max(0.0, staminaRemain - staminaCost);
                    現在のレース.各馬のスタミナ残量[馬.Id] = staminaRemain;

                    // 1フレーム(0.1s)の移動距離
                    double 移動距離m = 次秒速 * 0.1;

                    double 進捗加算 = 移動距離m / 現在のレース.距離;

                    現在のレース.各馬の進捗[馬.Id] += 進捗加算;
                    
                    if (現在のレース.各馬の進捗[馬.Id] >= 1.0)
                    {
                        現在のレース.各馬の進捗[馬.Id] = 1.0; // 表示用にクランプ
                        
                        // ゴール判定
                        if (!現在のレース.着順.Contains(馬.Id))
                        {
                            現在のレース.着順.Add(馬.Id);
                        }
                    }
                }
            }

            if (全馬ゴール)
            {
                現在のレース.状態 = レース状態.終了;

                // 勝者判定 (着順の1番目)
                var 勝者Id = 現在のレース.着順.FirstOrDefault();
                var 勝者 = 現在のレース.出走馬リスト.FirstOrDefault(h => h.Id == 勝者Id);

                // 疲労蓄積と最終出走
                foreach(var 馬 in 現在のレース.出走馬リスト)
                {
                    馬.疲労 = Math.Min(100, 馬.疲労 + 30);
                    馬.最終出走時刻 = DateTime.Now;
                    馬.出走数++;
                    
                    if (勝者 != null && 馬.Id == 勝者.Id)
                    {
                        馬.勝利数++;
                        馬.獲得賞金 += 1000000; // 仮の賞金
                        
                        // 昇級ロジック
                        if (馬.クラス == 競走馬クラス.新馬 || 馬.クラス == 競走馬クラス.未勝利) 馬.クラス = 競走馬クラス.一勝クラス;
                        else if (馬.クラス == 競走馬クラス.一勝クラス) 馬.クラス = 競走馬クラス.二勝クラス;
                        else if (馬.クラス == 競走馬クラス.二勝クラス) 馬.クラス = 競走馬クラス.三勝クラス;
                        else if (馬.クラス == 競走馬クラス.三勝クラス) 馬.クラス = 競走馬クラス.オープン;
                    }
                }

                レース終了時?.Invoke(現在のレース);
                
                // 次のレースへ
                Task.Delay(5000).ContinueWith(_ => 次のレースへ());
            }
            状態通知();
        }
    }

    private void 次のレースへ()
    {
        if (_予定レースリスト.Count == 0)
        {
            _予定レースリスト.Add(レース生成());
        }

        現在のレース = _予定レースリスト[0];
        _予定レースリスト.RemoveAt(0);
        _投票待機ティック = 0;
        予定レース補充();

        状態通知();
        レース切替時?.Invoke(現在のレース);
    }



    private void 状態通知() => レース状態変更時?.Invoke();

    public void Dispose()
    {
        _ループタイマー?.Stop();
        _ループタイマー?.Dispose();
    }

    private double 初期横位置(競走馬 馬)
    {
        double basePos = 馬.脚質 switch
        {
            脚質.逃げ => 0.22,
            脚質.先行 => 0.30,
            脚質.差し => 0.52,
            _ => 0.70
        };
        double noise = (_乱数.NextDouble() - 0.5) * 0.12;
        return Math.Clamp(basePos + noise, 横位置最小, 横位置最大);
    }

    private bool 不発抽選(競走馬 馬)
    {
        if (馬.脚質 != 脚質.追込) return false;
        return _乱数.NextDouble() < 0.35;
    }

    private int スタート遅延抽選(競走馬 馬)
    {
        double gateNorm = Math.Clamp(馬.ゲート / 100.0, 0.0, 1.0);
        double 出遅れ率 = 0.20 - gateNorm * 0.15;
        int baseDelay = _乱数.Next(0, 6); // 0.0s - 0.5s
        if (_乱数.NextDouble() < 出遅れ率)
        {
            return baseDelay + _乱数.Next(6, 16); // 0.6s - 1.5s 追加
        }
        return baseDelay;
    }

    private double 加速正規化(競走馬 馬)
    {
        return Math.Clamp(馬.加速力 / 100.0, 0.0, 1.0);
    }

    private double 初期巡航速度(競走馬 馬)
    {
        double リアル秒速 = 15.0 + (馬.スピード / 20.0);
        return リアル秒速 * 5.5;
    }

    private double 目標横位置算定(競走馬 馬, double 進捗, double 現在順位率)
    {
        double basePos = 馬.脚質 switch
        {
            脚質.逃げ => 0.20,
            脚質.先行 => 0.28,
            脚質.差し => 0.50,
            _ => 0.65
        };

        if (進捗 > 0.7)
        {
            if (馬.脚質 == 脚質.追込) basePos += 0.08;
            else if (馬.脚質 == 脚質.逃げ) basePos -= 0.04;
        }

        if (現在順位率 > 0.6) basePos += 0.05; // 後方は外へ
        if (現在順位率 < 0.2) basePos -= 0.03; // 先頭付近は内へ

        return Math.Clamp(basePos, 横位置最小, 横位置最大);
    }

    private double 脚質速度カーブ補正(
        競走馬 馬,
        double 進捗,
        bool 追込不発,
        double accelNorm,
        double staminaRemain,
        out double 仕掛け負荷,
        out bool スパート中)
    {
        double p = Math.Clamp(進捗, 0.0, 1.0);
        double mult = 1.0;
        仕掛け負荷 = 0.0;
        スパート中 = false;

        switch (馬.脚質)
        {
            case 脚質.逃げ:
                if (p < 0.2) mult = 1.08; // 2F目が速い
                else if (p < 0.5) mult = 1.02;
                else if (p < 0.7) mult = 0.98;
                else mult = 0.92 - ((p - 0.7) * 0.20);
                break;
            case 脚質.先行:
                if (p < 0.2) mult = 1.02;
                else if (p < 0.7) mult = 1.00;
                else mult = 0.97 - ((p - 0.7) * 0.10);
                break;
            case 脚質.差し:
                {
                    double baseKick = 0.60;
                    double kick = Math.Clamp(baseKick - (1.0 - accelNorm) * 0.08 - ((staminaRemain - 0.5) * 0.08), 0.45, baseKick);
                    if (p < 0.35) mult = 0.95;
                    else if (p < kick) mult = 1.00;
                    else if (p < 0.82) mult = 1.08;
                    else mult = 1.03;

                    if (p < baseKick && p >= kick)
                    {
                        仕掛け負荷 = (baseKick - p) * (1.0 - accelNorm);
                    }

                    スパート中 = p >= kick;
                }
                break;
            case 脚質.追込:
                {
                    double baseKick = 0.70;
                    double kick = Math.Clamp(baseKick - (1.0 - accelNorm) * 0.10 - ((staminaRemain - 0.5) * 0.08), 0.55, baseKick);
                    if (追込不発)
                    {
                        if (p < 0.6) mult = 0.90;
                        else if (p < 0.85) mult = 0.98;
                        else mult = 0.94;
                    }
                    else
                    {
                        if (p < 0.5) mult = 0.88;
                        else if (p < kick) mult = 0.98;
                        else if (p < 0.85) mult = 1.14; // 8F目付近でピーク
                        else mult = 1.06;
                    }

                    if (!追込不発 && p < baseKick && p >= kick)
                    {
                        仕掛け負荷 = (baseKick - p) * (1.0 - accelNorm);
                    }

                    スパート中 = !追込不発 && p >= kick;
                }
                break;
        }

        return Math.Clamp(mult, 0.75, 1.20);
    }

    private double スタミナ消耗補正(競走馬 馬, double 進捗, double 仕掛け負荷, double staminaRemain)
    {
        double staminaQuality = Math.Clamp(馬.スタミナ / 100.0, 0.4, 1.2);
        double fatigue = Math.Pow(Math.Clamp(進捗, 0.0, 1.0), 1.6);
        double earlyCost = 1.0 + Math.Clamp(仕掛け負荷, 0.0, 0.25);
        double baseFactor = 1.0 - (1.0 - staminaQuality) * fatigue * 0.35 * earlyCost;
        double staminaCap = 0.75 + (staminaRemain * 0.25);
        return Math.Clamp(baseFactor * staminaCap, 0.70, 1.05);
    }

    private bool ブロック判定(
        競走馬 馬,
        Dictionary<string, double> 現在距離ById,
        Dictionary<string, double> 横位置ById)
    {
        double 自分距離 = 現在距離ById[馬.Id];
        double 自分横 = 横位置ById[馬.Id];

        foreach (var kv in 現在距離ById)
        {
            if (kv.Key == 馬.Id) continue;
            double 相手距離 = kv.Value;
            double ahead = 相手距離 - 自分距離;
            if (ahead <= 0 || ahead > ブロック距離) continue;

            double 相手横 = 横位置ById[kv.Key];
            if (Math.Abs(相手横 - 自分横) < 体幅)
            {
                return true;
            }
        }

        return false;
    }

    private double 車間補正算定(
        競走馬 馬,
        Dictionary<string, double> 現在距離ById,
        Dictionary<string, double> 横位置ById)
    {
        double 自分距離 = 現在距離ById[馬.Id];
        double 自分横 = 横位置ById[馬.Id];
        double 最短ギャップ = double.MaxValue;

        foreach (var kv in 現在距離ById)
        {
            if (kv.Key == 馬.Id) continue;
            double 相手距離 = kv.Value;
            double ahead = 相手距離 - 自分距離;
            if (ahead <= 0 || ahead > ブロック距離) continue;

            double 相手横 = 横位置ById[kv.Key];
            if (Math.Abs(相手横 - 自分横) < 体幅 * 1.35)
            {
                if (ahead < 最短ギャップ) 最短ギャップ = ahead;
            }
        }

        if (最短ギャップ == double.MaxValue) return 1.0;
        if (最短ギャップ <= 0.5) return 0.80;
        if (最短ギャップ < 最低車間距離)
        {
            double t = (最低車間距離 - 最短ギャップ) / 最低車間距離;
            return Math.Clamp(1.0 - (0.2 * t), 0.8, 1.0);
        }

        return 1.0;
    }

    private double? 回避横位置(
        競走馬 馬,
        Dictionary<string, double> 現在距離ById,
        Dictionary<string, double> 横位置ById)
    {
        double 自分距離 = 現在距離ById[馬.Id];
        double 自分横 = 横位置ById[馬.Id];
        double 左候補 = Math.Clamp(自分横 - 0.10, 横位置最小, 横位置最大);
        double 右候補 = Math.Clamp(自分横 + 0.10, 横位置最小, 横位置最大);

        bool 左空き = 進路空き判定(馬.Id, 左候補, 自分距離, 現在距離ById, 横位置ById);
        bool 右空き = 進路空き判定(馬.Id, 右候補, 自分距離, 現在距離ById, 横位置ById);

        if (左空き && 右空き)
        {
            return (右候補 - 横位置最小) > (横位置最大 - 左候補) ? 右候補 : 左候補;
        }

        if (右空き) return 右候補;
        if (左空き) return 左候補;
        return null;
    }

    private bool 進路空き判定(
        string 馬Id,
        double 候補横,
        double 自分距離,
        Dictionary<string, double> 現在距離ById,
        Dictionary<string, double> 横位置ById)
    {
        foreach (var kv in 現在距離ById)
        {
            if (kv.Key == 馬Id) continue;
            double 相手距離 = kv.Value;
            double delta = 相手距離 - 自分距離;
            if (delta < -後方チェック距離 || delta > 側方チェック距離) continue;

            double 相手横 = 横位置ById[kv.Key];
            if (Math.Abs(相手横 - 候補横) < 体幅)
            {
                return false;
            }
        }

        return true;
    }
}
