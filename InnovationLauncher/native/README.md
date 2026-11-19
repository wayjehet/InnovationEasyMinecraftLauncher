# Cubiomes 原生 DLL（種子地圖用）

啟動器的「種子地圖」需要 `ieml_cubiomes.dll`（放在啟動器 exe 同一個資料夾，或放在 `native\` 讓專案建置時自動複製）。

## 建置（Windows，x64）
1. `git clone https://github.com/Cubitect/cubiomes` 到 `native\cubiomes\`
2. 在「x64 Native Tools Command Prompt for VS」執行：

       cd native
       cl /O2 /LD /I cubiomes ieml_cubiomes_shim.c cubiomes\biomenoise.c cubiomes\biomes.c cubiomes\finders.c cubiomes\generator.c cubiomes\layers.c cubiomes\noise.c cubiomes\quadbase.c cubiomes\util.c /Fe:ieml_cubiomes.dll

   用 MinGW 時：

       gcc -O2 -shared -I cubiomes -o ieml_cubiomes.dll ieml_cubiomes_shim.c cubiomes/*.c -lm

   （cubiomes 的 .c 檔清單依版本略有不同，全部編進去即可；如果 `cubiomes\` 裡有 `tests.c` / `xmapview.c` 之類的範例，要排除。）
3. 位元數要和啟動器一致（x64 啟動器 → x64 DLL）。

沒有這個 DLL 時，種子地圖視窗會顯示提示，其他功能不受影響。
