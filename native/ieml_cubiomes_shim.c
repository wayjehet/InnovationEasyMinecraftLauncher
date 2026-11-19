/* IEML Cubiomes shim：把 Cubiomes 包成簡單的 C ABI，讓 C# 用 P/Invoke 呼叫。
 * 未編譯、未實測；Cubiomes 函式名稱依 https://github.com/Cubitect/cubiomes 記憶撰寫，若版本不同請對照其 finders.h / generator.h。
 * 建置方式見 native/README.md */
#include <stdint.h>
#include <string.h>
#include <stdlib.h>
#include "finders.h"
#include "generator.h"

#ifdef _WIN32
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

static int structure_from_name(const char *n)
{
    static const struct { const char *name; int id; } T[] = {
        {"Desert_Pyramid", Desert_Pyramid}, {"Jungle_Temple", Jungle_Temple}, {"Swamp_Hut", Swamp_Hut},
        {"Igloo", Igloo}, {"Village", Village}, {"Ocean_Ruin", Ocean_Ruin}, {"Shipwreck", Shipwreck},
        {"Monument", Monument}, {"Mansion", Mansion}, {"Outpost", Outpost}, {"Treasure", Treasure},
        {"Fortress", Fortress}, {"Bastion", Bastion}, {"End_City", End_City}, {"Ruined_Portal", Ruined_Portal},
        {"Ancient_City", Ancient_City}, {"Trail_Ruins", Trail_Ruins}, {"Trial_Chambers", Trial_Chambers},
    };
    for (size_t i = 0; i < sizeof(T) / sizeof(T[0]); i++)
        if (strcmp(T[i].name, n) == 0) return T[i].id;
    return -1;
}

/* 回傳 Minecraft 版本代碼（Cubiomes 的 MC_*），不認得回 -1 */
EXPORT int ieml_mc_version(const char *name)
{
    int v = str2mc(name);
    return v;
}

/* 在以 (cx,cz) 為中心、半徑 radius（方塊）的方形範圍內找結構。
 * out 以 [x0,z0,x1,z1,...] 排列，最多 maxOut 個結構；回傳實際數量，錯誤回負數。 */
EXPORT int ieml_find_structures(const char *mcName, const char *structName, int64_t seed,
                                int cx, int cz, int radius, int *out, int maxOut)
{
    int mc = str2mc(mcName);
    if (mc < 0) return -1;
    int st = structure_from_name(structName);
    if (st < 0) return -2;

    StructureConfig sc;
    if (!getStructureConfig(st, mc, &sc)) return -3;

    Generator g;
    setupGenerator(&g, mc, 0);
    int dim = (st == Fortress || st == Bastion) ? DIM_NETHER : (st == End_City ? DIM_END : DIM_OVERWORLD);
    applySeed(&g, dim, (uint64_t)seed);

    int regBlocks = sc.regionSize * 16;
    int rx0 = (int)((cx - radius) / (double)regBlocks) - 1, rx1 = (int)((cx + radius) / (double)regBlocks) + 1;
    int rz0 = (int)((cz - radius) / (double)regBlocks) - 1, rz1 = (int)((cz + radius) / (double)regBlocks) + 1;

    int n = 0;
    for (int rz = rz0; rz <= rz1; rz++)
        for (int rx = rx0; rx <= rx1; rx++)
        {
            Pos p;
            if (!getStructurePos(st, mc, (uint64_t)seed, rx, rz, &p)) continue;
            if (abs(p.x - cx) > radius || abs(p.z - cz) > radius) continue;
            if (!isViableStructurePos(st, &g, p.x, p.z, 0)) continue;
            if (n >= maxOut) return n;
            out[n * 2] = p.x; out[n * 2 + 1] = p.z; n++;
        }
    return n;
}

/* 取得 (x,z) 地表（y=63）的群系名稱，寫入 buf；回傳 0 成功，負數失敗 */
EXPORT int ieml_biome_at(const char *mcName, int64_t seed, int x, int z, char *buf, int bufLen)
{
    int mc = str2mc(mcName);
    if (mc < 0) return -1;
    Generator g;
    setupGenerator(&g, mc, 0);
    applySeed(&g, DIM_OVERWORLD, (uint64_t)seed);
    int id = getBiomeAt(&g, 1, x, 63, z);
    if (id < 0) return -2;
    const char *name = biome2str(mc, id);
    if (!name) return -3;
    strncpy(buf, name, bufLen - 1); buf[bufLen - 1] = 0;
    return 0;
}
