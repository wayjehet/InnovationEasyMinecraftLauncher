namespace IEML.App;

public enum RState { Playing, Shop, Dead, Won }
public enum EKind { Zombie, Skeleton, Creeper, Boss }

public class Enemy
{
    public EKind Kind;
    public double X, Y, Hp, MaxHp, Speed, R;
    public double Cd, Touch, Fuse, Flash, Sum;
    public int Pattern;
    public bool Alive => Hp > 0;
}

public class Bullet
{
    public double X, Y, VX, VY, Dmg, Life, R = 4;
    public bool Friendly, Big;
    public int Pierce;
    public List<Enemy>? Hit;
}

public class Gem { public double X, Y; public int Value; public bool Magnet; }
public class SpawnMark { public double X, Y, T; public EKind Kind; }
public class Fx { public double X, Y, T, Max, Radius; }

public class ShopItem
{
    public string Name = "", Desc = "";
    public int Cost;
    public bool Sold;
    public Action<RoguelikeGame> Apply = _ => { };
}

/// 即時彈幕肉鴿（類 Brotato）：竞技場、自動瞄準射擊、一波一波打，波與波之間用材料買強化；第 10 波是凋零骷髏王。
/// 純模擬，不碰 UI；畫面在 RoguelikeView，操作在 RoguelikePage。座標單位是「邏輯像素」（場地 640×400）。
public class RoguelikeGame
{
    public const double W = 640, H = 400, PR = 11;      // 場地大小、玩家半徑
    public const int LastWave = 10;
    const double BulletSpeed = 460;

    /// 這次執行期間打到的最高波數（不存檔）。
    public static int Best { get; private set; }

    readonly Random _rng = new();
    public RState State { get; private set; }

    // 玩家能力值（商店會改）
    public double MaxHp, Hp, Regen, Dmg, FireRate, Range, Speed, Armor, Pickup;
    public int Pierce, Multi;
    public double PX, PY, Invuln, HurtFlash;
    public int Materials, Wave, Kills, RerollCost;
    public double WaveTime;

    public List<Enemy> Enemies { get; } = new();
    public List<Bullet> Bullets { get; } = new();
    public List<Gem> Gems { get; } = new();
    public List<SpawnMark> Marks { get; } = new();
    public List<Fx> Effects { get; } = new();
    public List<ShopItem> Offers { get; } = new();

    double _fire, _spawn;
    bool _bossSpawned;

    public RoguelikeGame() => Reset();

    public bool IsBossWave => Wave == LastWave;
    public double WaveDuration => Math.Min(45, 20 + (Wave - 1) * 2.5);
    public double TimeLeft => Math.Max(0, WaveDuration - WaveTime);
    public Enemy? Boss => Enemies.FirstOrDefault(e => e.Kind == EKind.Boss && e.Alive);

    public void Reset()
    {
        MaxHp = 20; Hp = 20; Regen = 0.2; Dmg = 6; FireRate = 2.0; Range = 230; Speed = 150; Armor = 0; Pickup = 60;
        Pierce = 0; Multi = 1;
        Materials = 0; Wave = 1; Kills = 0; RerollCost = 2;
        Offers.Clear();
        StartWave();
    }

    void StartWave()
    {
        Enemies.Clear(); Bullets.Clear(); Gems.Clear(); Marks.Clear(); Effects.Clear();
        PX = W / 2; PY = H / 2; Invuln = 0.6; HurtFlash = 0;
        WaveTime = 0; _fire = 0; _spawn = 0.4; _bossSpawned = false;
        State = RState.Playing;
    }

    // ───────────── 每幀更新 ─────────────
    /// ix/iy：方向輸入（-1～1，可不正規化）。
    public void Update(double dt, double ix, double iy)
    {
        if (State != RState.Playing) return;
        WaveTime += dt;

        double len = Math.Sqrt(ix * ix + iy * iy);
        if (len > 0) { PX += ix / len * Speed * dt; PY += iy / len * Speed * dt; }
        PX = Math.Clamp(PX, PR, W - PR); PY = Math.Clamp(PY, PR, H - PR);
        Invuln = Math.Max(0, Invuln - dt); HurtFlash = Math.Max(0, HurtFlash - dt);
        Hp = Math.Min(MaxHp, Hp + Regen * dt);

        Director(dt);
        UpdateMarks(dt);
        UpdateEnemies(dt);
        AutoFire(dt);
        UpdateBullets(dt);
        Reap();
        UpdateGems(dt);
        for (int i = Effects.Count - 1; i >= 0; i--) { Effects[i].T += dt; if (Effects[i].T >= Effects[i].Max) Effects.RemoveAt(i); }

        if (Hp <= 0) { Hp = 0; State = RState.Dead; Best = Math.Max(Best, Wave); return; }
        if (State == RState.Playing && !IsBossWave && WaveTime >= WaveDuration) EndWave();
    }

    // ───────────── 生怪 ─────────────
    void Director(double dt)
    {
        if (IsBossWave && !_bossSpawned)
        {
            _bossSpawned = true;
            Marks.Add(new SpawnMark { X = W / 2, Y = 70, T = 1.5, Kind = EKind.Boss });
        }
        _spawn -= dt;
        if (_spawn > 0) return;
        _spawn = IsBossWave ? 1.8 : Math.Max(0.4, 1.2 - Wave * 0.07);
        int cap = IsBossWave ? 8 : 8 + Wave * 3;
        if (Enemies.Count(e => e.Alive) + Marks.Count >= cap) return;
        var kind = RollKind();
        if (IsBossWave && kind == EKind.Creeper) kind = EKind.Zombie;
        for (int t = 0; t < 12; t++)
        {
            double x = 20 + _rng.NextDouble() * (W - 40), y = 20 + _rng.NextDouble() * (H - 40);
            if (Dist(x, y, PX, PY) < 150) continue;               // 不要生在玩家臉上
            Marks.Add(new SpawnMark { X = x, Y = y, T = 0.8, Kind = kind });
            return;
        }
    }

    EKind RollKind()
    {
        int r = _rng.Next(100);
        if (Wave < 3) return EKind.Zombie;
        if (Wave < 4) return r < 75 ? EKind.Zombie : EKind.Skeleton;
        if (Wave < 7) return r < 55 ? EKind.Zombie : r < 80 ? EKind.Skeleton : EKind.Creeper;
        return r < 40 ? EKind.Zombie : r < 70 ? EKind.Skeleton : EKind.Creeper;
    }

    void UpdateMarks(double dt)
    {
        for (int i = Marks.Count - 1; i >= 0; i--)
        {
            var m = Marks[i];
            m.T -= dt;
            if (m.T > 0) continue;
            Marks.RemoveAt(i);
            Enemies.Add(MakeEnemy(m.Kind, m.X, m.Y));
        }
    }

    Enemy MakeEnemy(EKind k, double x, double y)
    {
        double mul = 1 + 0.4 * (Wave - 1);
        return k switch
        {
            EKind.Zombie => new Enemy { Kind = k, X = x, Y = y, Hp = 14 * mul, MaxHp = 14 * mul, Speed = 50 + Wave * 2.5, R = 11 },
            EKind.Skeleton => new Enemy { Kind = k, X = x, Y = y, Hp = 9 * mul, MaxHp = 9 * mul, Speed = 42, R = 11, Cd = 1 + _rng.NextDouble() },
            EKind.Creeper => new Enemy { Kind = k, X = x, Y = y, Hp = 10 * mul, MaxHp = 10 * mul, Speed = 85, R = 11 },
            _ => new Enemy { Kind = k, X = x, Y = y, Hp = 450, MaxHp = 450, Speed = 30, R = 28, Cd = 1.5, Sum = 8 }
        };
    }

    // ───────────── 敵人 ─────────────
    void UpdateEnemies(double dt)
    {
        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            e.Flash = Math.Max(0, e.Flash - dt);
            e.Touch -= dt;
            double dx = PX - e.X, dy = PY - e.Y;
            double d = Math.Sqrt(dx * dx + dy * dy) + 1e-6;
            double ux = dx / d, uy = dy / d;

            switch (e.Kind)
            {
                case EKind.Zombie:
                    e.X += ux * e.Speed * dt; e.Y += uy * e.Speed * dt;
                    if (d < e.R + PR && e.Touch <= 0) { Damage(4 + Wave / 3); e.Touch = 0.8; }
                    break;

                case EKind.Skeleton:
                    if (d > 210) { e.X += ux * e.Speed * dt; e.Y += uy * e.Speed * dt; }        // 太遠就靠近
                    else if (d < 130) { e.X -= ux * e.Speed * dt; e.Y -= uy * e.Speed * dt; }   // 太近就退開
                    e.Cd -= dt;
                    if (e.Cd <= 0)
                    {
                        e.Cd = 1.6 + _rng.NextDouble();
                        double a = Math.Atan2(dy, dx);
                        int n = Wave >= 6 ? 3 : 1;
                        for (int i = 0; i < n; i++) Shoot(e.X, e.Y, a + (i - (n - 1) / 2.0) * 0.28, 175, 3 + Wave / 3, false);
                    }
                    break;

                case EKind.Creeper:
                    if (e.Fuse > 0)
                    {
                        e.Fuse -= dt;                                   // 蓄力時停住、閃白光
                        if (e.Fuse <= 0)
                        {
                            if (d < 62) Damage(9 + Wave / 2);
                            Effects.Add(new Fx { X = e.X, Y = e.Y, Max = 0.35, Radius = 62 });
                            e.Hp = 0;
                        }
                    }
                    else
                    {
                        e.X += ux * e.Speed * dt; e.Y += uy * e.Speed * dt;
                        if (d < 36) e.Fuse = 0.55;
                    }
                    break;

                case EKind.Boss:
                    BossAct(e, dt, ux, uy, d);
                    break;
            }
            e.X = Math.Clamp(e.X, e.R, W - e.R); e.Y = Math.Clamp(e.Y, e.R, H - e.R);
        }
    }

    void BossAct(Enemy b, double dt, double ux, double uy, double d)
    {
        if (d > 110) { b.X += ux * b.Speed * dt; b.Y += uy * b.Speed * dt; }
        if (d < b.R + PR && b.Touch <= 0) { Damage(7); b.Touch = 0.8; }
        bool rage = b.Hp < b.MaxHp * 0.5;
        b.Cd -= dt;
        if (b.Cd <= 0)
        {
            b.Cd = rage ? 1.1 : 1.7;
            int ring = rage ? 24 : 16;
            double aim = Math.Atan2(PY - b.Y, PX - b.X);
            switch (b.Pattern++ % 3)
            {
                case 0: Ring(b, ring, 0, 130); break;                                   // 一圈彈幕
                case 1: for (int i = -2; i <= 2; i++) Shoot(b.X, b.Y, aim + i * 0.26, 200, 4, true); break;   // 瞄準的五連發
                default: Ring(b, ring, Math.PI / ring, 110); for (int i = -1; i <= 1; i++) Shoot(b.X, b.Y, aim + i * 0.2, 190, 4, true); break;
            }
        }
        b.Sum -= dt;
        if (b.Sum <= 0)
        {
            b.Sum = 9;
            for (int i = 0; i < 3; i++)
                Marks.Add(new SpawnMark { X = Math.Clamp(b.X + _rng.Next(-80, 81), 20, W - 20), Y = Math.Clamp(b.Y + _rng.Next(-60, 61), 20, H - 20), T = 0.8, Kind = EKind.Zombie });
        }
    }

    void Ring(Enemy b, int n, double offset, double speed)
    {
        for (int i = 0; i < n; i++) Shoot(b.X, b.Y, offset + i * Math.PI * 2 / n, speed, 3, true);
    }

    void Shoot(double x, double y, double angle, double speed, double dmg, bool big) =>
        Bullets.Add(new Bullet { X = x, Y = y, VX = Math.Cos(angle) * speed, VY = Math.Sin(angle) * speed, Dmg = dmg, Life = 6, Friendly = false, Big = big, R = big ? 5 : 4 });

    void Damage(double raw)
    {
        if (Invuln > 0) return;
        double d = Math.Max(1, raw * 15 / (15 + Armor));        // 護甲：傷害 ×15/(15+護甲)，最少 1
        Hp -= d; Invuln = 0.4; HurtFlash = 0.18;
    }

    // ───────────── 玩家射擊與子彈 ─────────────
    void AutoFire(double dt)
    {
        _fire -= dt;
        if (_fire > 0) return;
        Enemy? target = null; double best = double.MaxValue;
        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            double d = Dist(e.X, e.Y, PX, PY) - e.R;
            if (d <= Range && d < best) { best = d; target = e; }
        }
        if (target is null) return;                              // 沒目標就待機，下一幀再找
        _fire = 1.0 / FireRate;
        double a = Math.Atan2(target.Y - PY, target.X - PX);
        for (int i = 0; i < Multi; i++)
        {
            double ang = a + (i - (Multi - 1) / 2.0) * 0.14;
            bool crit = _rng.NextDouble() < 0.1;
            Bullets.Add(new Bullet
            {
                X = PX, Y = PY, VX = Math.Cos(ang) * BulletSpeed, VY = Math.Sin(ang) * BulletSpeed,
                Dmg = Dmg * (crit ? 2 : 1), Life = (Range + 40) / BulletSpeed, Friendly = true, Pierce = Pierce, Big = crit, Hit = new()
            });
        }
    }

    void UpdateBullets(double dt)
    {
        for (int i = Bullets.Count - 1; i >= 0; i--)
        {
            var b = Bullets[i];
            b.X += b.VX * dt; b.Y += b.VY * dt; b.Life -= dt;
            bool remove = b.Life <= 0 || b.X < -20 || b.Y < -20 || b.X > W + 20 || b.Y > H + 20;

            if (!remove && b.Friendly)
            {
                foreach (var e in Enemies)
                {
                    if (!e.Alive || b.Hit!.Contains(e)) continue;
                    if (Dist(b.X, b.Y, e.X, e.Y) > e.R + b.R) continue;
                    e.Hp -= b.Dmg; e.Flash = 0.1; b.Hit.Add(e);
                    if (b.Pierce > 0) b.Pierce--; else { remove = true; break; }
                }
            }
            else if (!remove && Dist(b.X, b.Y, PX, PY) < PR + b.R)
            {
                Damage(b.Dmg); remove = true;
            }
            if (remove) Bullets.RemoveAt(i);
        }
    }

    void Reap()
    {
        for (int i = Enemies.Count - 1; i >= 0; i--)
        {
            var e = Enemies[i];
            if (e.Alive) continue;
            Enemies.RemoveAt(i);
            Kills++;
            int value = e.Kind switch { EKind.Zombie => 1, EKind.Skeleton => 2, EKind.Creeper => 2, _ => 40 };
            int pieces = e.Kind == EKind.Boss ? 8 : 1;
            for (int k = 0; k < pieces; k++)
                Gems.Add(new Gem { X = e.X + _rng.Next(-10, 11), Y = e.Y + _rng.Next(-10, 11), Value = value / pieces == 0 ? 1 : value / pieces });
            if (e.Kind == EKind.Boss)
            {
                State = RState.Won; Best = Math.Max(Best, LastWave);
                Bullets.Clear(); Marks.Clear();
                Enemies.Clear();
                return;                                          // 清單已被清空，不要再往下跑迴圈
            }
        }
    }

    void UpdateGems(double dt)
    {
        for (int i = Gems.Count - 1; i >= 0; i--)
        {
            var g = Gems[i];
            double d = Dist(g.X, g.Y, PX, PY);
            if (d < Pickup) g.Magnet = true;
            if (g.Magnet)
            {
                double dx = PX - g.X, dy = PY - g.Y, dd = d + 1e-6;
                g.X += dx / dd * 320 * dt; g.Y += dy / dd * 320 * dt;
            }
            if (d < 12) { Materials += g.Value; Gems.RemoveAt(i); }
        }
    }

    static double Dist(double x1, double y1, double x2, double y2)
    {
        double dx = x1 - x2, dy = y1 - y2;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    // ───────────── 波與波之間：商店 ─────────────
    void EndWave()
    {
        foreach (var g in Gems) Materials += g.Value;             // 沒撿的材料全部自動收走
        Gems.Clear(); Enemies.Clear(); Bullets.Clear(); Marks.Clear();
        Hp = Math.Min(MaxHp, Hp + MaxHp * 0.3);
        Best = Math.Max(Best, Wave);
        RerollCost = 2 + Wave / 2;
        BuildOffers();
        State = RState.Shop;
    }

    public void NextWave()
    {
        if (State != RState.Shop) return;
        Wave++;
        StartWave();
    }

    public bool Buy(int index)
    {
        if (State != RState.Shop || index < 0 || index >= Offers.Count) return false;
        var item = Offers[index];
        if (item.Sold || Materials < item.Cost) return false;
        Materials -= item.Cost;
        item.Apply(this);
        item.Sold = true;
        return true;
    }

    public bool Reroll()
    {
        if (State != RState.Shop || Materials < RerollCost) return false;
        Materials -= RerollCost; RerollCost++;
        BuildOffers();
        return true;
    }

    void BuildOffers()
    {
        var pool = new List<ShopItem>
        {
            Item("鑽石箭頭", "傷害 +2", 12, g => g.Dmg += 2),
            Item("快手", "射速 +0.35／秒", 14, g => g.FireRate += 0.35),
            Item("鷹眼", "射程 +40", 10, g => g.Range += 40),
            Item("金蘋果", "最大生命 +6，並回復 6", 12, g => { g.MaxHp += 6; g.Hp = Math.Min(g.MaxHp, g.Hp + 6); }),
            Item("皮革靴", "移動速度 +12", 10, g => g.Speed += 12),
            Item("鐵胸甲", "護甲 +2", 14, g => g.Armor += 2),
            Item("磁石", "拾取範圍 +35", 8, g => g.Pickup += 35),
            Item("再生藥水", "每秒回復 +0.5", 14, g => g.Regen += 0.5),
            Item("附魔重弩", "傷害 +5，射速 -0.25（最低 0.6）", 16, g => { g.Dmg += 5; g.FireRate = Math.Max(0.6, g.FireRate - 0.25); }),
            Item("疾風弓", "射速 +0.8，傷害 -1（最低 1）", 16, g => { g.FireRate += 0.8; g.Dmg = Math.Max(1, g.Dmg - 1); }),
            Item("龜殼", "護甲 +4，移動速度 -15（最低 80）", 14, g => { g.Armor += 4; g.Speed = Math.Max(80, g.Speed - 15); }),
        };
        if (Multi < 4) pool.Add(Item("雙重射擊", "每次多射 1 支箭，傷害 ×0.85", 30, g => { g.Multi++; g.Dmg = Math.Max(1, Math.Round(g.Dmg * 0.85, 1)); }));
        if (Pierce < 4) pool.Add(Item("穿透箭", "箭多穿透 1 個敵人", 18, g => g.Pierce++));

        Offers.Clear();
        double scale = 1 + 0.12 * (Wave - 1);
        foreach (var it in pool.OrderBy(_ => _rng.Next()).Take(4))
        {
            it.Cost = (int)Math.Round(it.Cost * scale);
            Offers.Add(it);
        }
    }

    static ShopItem Item(string name, string desc, int cost, Action<RoguelikeGame> apply) =>
        new() { Name = name, Desc = desc, Cost = cost, Apply = apply };
}
