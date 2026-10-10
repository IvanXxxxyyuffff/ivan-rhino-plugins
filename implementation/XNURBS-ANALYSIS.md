# XNURBS 逆向分析报告（多重曲面 → 单一光滑 NURBS 面）

- 分析日期：2026-10-10
- 目标（**只读，未修改任何文件**）：
  - `C:\Program Files\Rhino 7\Plug-ins\XNurbsRhino 5\XNurbsRhino.rhp`（173,568 B，2020-12-10）
  - `C:\Program Files\Rhino 7\Plug-ins\XNurbsRhino 5\xnkernel.dll`（53,431,808 B，2020-12-11，版本 2021.0.1.0）
  - 同目录 `XNurbsRhino.rui` / `License.rtf` / 演示 gif·mp4
- 原始产物：`E:\IVAN-LiquidGlass-preview\implementation\xnurbs-re\`（清单见文末）
- 授权：用户自有机器上的已授权资产，仅做静态反编译分析。

> **重要更正**：任务书假设 `.rhp` 是「.NET 托管程序集 = 插件外壳」。**实测不成立**。
> `XNurbsRhino.rhp` 的 CLR 数据目录为 0（`COM_DESCRIPTOR RVA=0x0 size=0`），导入 `mfc140u.dll`(159)/`RhinoCore.dll`(310)/`opennurbs.dll`(117)/`MSVCP140.dll`，
> 导出 `RhinoPlugInMfcVersion` / `RhinoPlugInUsesMfc` 等 MFC 插件标志 → 它是 **原生 x64 C++/MFC DLL**。
> 因此**不存在 P/Invoke 声明**；等价信息 = 它的**原生导入表**（= 内核 C API 的真实使用面），本报告第 1 节给出。
> 同时本机 `xNURBS License Manager.exe` 才是授权工具（TurboActivate）。故本次未使用 ilspycmd（已装但无托管目标可反编译）。

---

## 1. `.rhp` 插件外壳：命令、参数体系、内核 API 使用面

### 1.1 命令

`XNurbsRhino.rui` 里只有一个宏 `XNurbs`（外加 `XNurbsHelp`），即 Rhino 命令 **`_XNurbs`**（另含 `_XNurbsHelp` 打开 `XNurbsHelp.chm`）。
该 `.rui` 已被第三方汉化（`XNurbs 帮助[远水遥岑汉化]`），但**内核与命令集未被改动**。
`.rhp` 另有 4 个自有导出：`AddOneConstraint` / `CreateOrSetVSurf` / `GenerateNewVSurf` / `FreeXNSurf`。

### 1.2 参数体系（从 `RT_DIALOG id=2000` 的 31 个控件 + `RT_STRING` 完整还原）

对话框为 MFC `CXNurbsDialog`（类名在 `.rdata` 0x13668）。控件与语义如下（class 序数：`#128`=Button, `#130`=Static, `#131`=ListBox, `#133`=ComboBox）：

| id | 控件 | 原始文本（本机为汉化版） | 语义 |
|----|------|--------------------------|------|
| 2001 | ListBox 125×84 | — | **约束列表**（每行一条约束） |
| 2002/2003/2004 | Radio ×3 | `G0` / `G1` / `G2` | **连续性等级**（只有 3 级，**没有 G3**） |
| 2005 | CheckBox | `边缘约束` | 拓扑：边缘约束 |
| 2006 | CheckBox | `将连续性应用于所有边界` | 把所选连续性套用到所有边界 |
| 2007 | CheckBox | `优化四边面` | Optimize quad-sided surface |
| 2008 | CheckBox | `修剪生成的曲面` | 结果按边界修剪 |
| 2022 | CheckBox | `显示预览` | 实时预览 |
| 2023 | CheckBox | `满足精度要求` | Meet accuracy requirement |
| 2024 | CheckBox | `斑马纹预览` | 斑马纹检查 |
| 2026 | ComboBox | — | **UV 方向** |
| 2009 | Trackbar | `G0 精确度:` | G0 位置精度 |
| 2025 | Trackbar | `G1 精确度:` | G1 切线精度 |
| 2010 | Trackbar | `质量控制:` | 质量（能量）权重 |
| 2011 | Trackbar | `平坦控制:` | 平坦/光顺权重 |
| 2015/2016/2019 | Button | `创建` / `取消` / `帮助` | — |

`RT_STRING`（资源 126/127）给出约束类型与全部告警文案，是理解其失败模式的钥匙：

- 约束类型：`选择曲线和点` / `边界` / `内部` / `点` / `曲线`（strid 2007–2011）
  → **XNurbs 的约束分「边界」与「内部」两类，内部可以是曲线，也可以是点集**。
- 法向/对齐模式：`正常` / `曲线法线` / `对其至相邻曲线`（strid 2013–2015）
- strid 2012（求解失败）：`无法从输入约束创建曲面。修复冲突约束、指定附加约束、取消选中修剪选项或放宽精度可能会解决问题`
- strid 2016：`由于边界边之间的重叠/间隙以及相邻几何图形上不一致的切线方向，生成的曲面可能无法严格满足指定的切线要求`
- strid 2017：`由于边界边之间的重叠/间隙，生成的曲面可能无法严格满足指定的位置要求`
- strid 2018/2019：编辑/不能编辑所选曲面

**结论：XNurbs 的参数体系只有 4 类量 —— 连续性等级(G0/G1/G2)、精度容差(G0/G1 两个滑杆)、能量权重(质量/平坦两个滑杆)、以及布尔开关(边缘约束/套用全部边界/优化四边面/修剪/预览/斑马纹)。**
它**不暴露**次数、控制点数、节点向量、span 数、周期性、对称性 —— 这些都归内核内部决定。

### 1.3 内核 C API（`xnkernel.dll` 导出 47 个，`.rhp` 实际使用 26 个）

`.rhp` 的导入表（= 内核 API 使用面，按调用点统计自 `rhp_text_full.txt`）：

| 内核函数 | `.rhp` 调用次数 | 作用 |
|---|---|---|
| `VK_initialize_kernel` / `VK_terminate_kernel` | 1 / 6 | 生命周期 |
| `VK_get_version` | 6 | 版本 |
| `VK_reset_vsurf_advanced` / `VK_reset_vsurf` | 5 / 1 | 复位求解器 |
| **`VK_create_vsurf_without_init_para`** | **4** | **创建「虚拟曲面」并让内核自算初始参数化** |
| `VK_create_multi_pt_cst` | 1 | 建点集约束（实参 200 / 200） |
| `VK_add_pt_position_set_cst` | 1 | 加点-位置约束 |
| `VK_add_pt_position_normal_set_cst` | 1 | 加点-位置+法向约束 |
| `VK_add_curve_cst` / `VK_modify_curve_cst` | 2 / 2 | 加/改曲线约束（边界与内部共用） |
| `VK_remove_constraint` / `VK_remove_pt_from_multi_pt_cst` / `VK_remove_all_from_multi_pt_cst` | 5 / 2 / 1 | 删约束 |
| `VK_remove_physical_force` | 1 | **移除「物理力」** |
| `VK_generate_solution` | 3 | **求解** |
| `VK_get_bsurf` | 1 | 取结果 NURBS 面 |
| `VK_get_outcome_status` | 1 | 结果状态 |
| `VK_get_warning_message` | 1 | **告警位掩码** |
| `VK_get_auto_enforce_tol_flavor` | 1 | 自动容差策略开关 |
| `VK_get_outer_boundary_loop` | 1 | 取外环 |
| `VK_get_uv_bcurve_from_curve_cst` | 1 | 把曲线约束映射到 UV 域 |
| `VK_check_open_boundary_difference` | 1 | **检查开放边界缝隙** |
| `VK_Calc_perpendicular_curvature` | 1 | 法向曲率 |
| `VK_create_bspline` | 1 | 造 B 样条（喂给内核） |
| `VK_free_v_object` | 5 | 释放 |

内核**未导出但存在于导出表**、供其它宿主（SolidWorks 等）使用的还有：
`VK_Calc_C1_dd` / `VK_Calc_C2_dd` / `VK_Calc_C3_dd`（G1/G2/G3 导数计算）、`VK_add_area_pressure`、`VK_add_pt_cst`、`VK_create_vsurf` / `_advanced` / `_advanced2` / `_without_init_para2`、`VK_get_bcurve`、`VK_get_interval_from_curve_cst`、`VK_get_num_of_pts_from_multi_pt_cst`、`VK_get_uv_from_point_cst`、`VK_get_uvs_from_multi_pt_cst`、`VK_check_quad_sided_possibility`、`VK_reset_bsurf`、`VK_reset_pt_xyz`、`VK_free_vsurf`。

**从 API 形状可直接读出的事实：**

1. **「vsurf」（virtual surface）是内核的核心对象**；宿主只给约束，不给曲面结构。
2. **参数化由内核自算**：`.rhp` 用的是 `VK_create_vsurf_without_init_para`（"without initial parameterization"），**从未**调用 `VK_create_vsurf_advanced*` → 宿主不提供初始参数化。
3. **约束是「求值型」而非「解析型」**：见 1.4 的回调机制。
4. **存在物理/能量语言**：`VK_add_area_pressure`（面积压力）、`VK_remove_physical_force`（移除物理力）→ 求解器带"力"这一层概念，不是纯线性插值。
5. **输出是 3 次（bicubic）NURBS**：`VK_get_bsurf` 调用点把输出结构体的次数域写成常量 `3`（`mov dword ptr [r11-0x44], 3` @0x5a3c），无其它次数可选。
6. **容差由宿主按文档绝对容差派生**：调用点用 `ON_3dmUnitsAndTolerances::Scale()` × `0.01`（下限）与 × `0.001`（另一路）计算 G0 容差 —— 说明**精度滑杆是相对文档容差的倍数**，不是绝对长度。

### 1.4 关键机制：几何通过**回调**喂给内核（G1/G2 的实现方式）

曲线约束的提交路径（`.rhp` 0x6127–0x6230）把 3 个**函数指针**写进一个描述块再交给 `VK_add_curve_cst` / `VK_modify_curve_cst`：

- 回调 @`0x4ca0`（G1 路）、@`0x4fd0`、@`0x50d0`（G2 路），被指向 `0x4ca0` / `0x4fd0` / `0x50d0`
- 回调体特征：`cmp ecx, 2`（请求阶数）、`lea r9d, [rbp+3]` / `lea r8d, [rdi+1]`（请求 3 阶与 1 阶导）、
  `mov rax,[rcx]; call qword ptr [rax+0x1e0]` → **对 openNURBS 曲线对象做虚调用**（vtable +0x1e0）
- 描述块里另写入常量 `2` 与 `4`（连续性阶与采样数）

**结论（证据支持）：XNurbs 不要求输入几何解析化。它通过宿主回调按参数 t 采样源曲线/曲面的点与 1~3 阶导数，把这些采样值变成约束方程。**
这就是它能对任意修剪边、任意多重曲面接缝做 G1/G2 的原因，也是 `VK_get_uv_bcurve_from_curve_cst` / `VK_get_uv_from_point_cst`（把 3D 约束映射到 UV 域）存在的理由。

### 1.5 告警与边界缝隙检查

- `VK_check_open_boundary_difference(vsurf, bool* out)` @0xaba9：内核直接回答"开放边界是否有缝隙/重叠"，宿主据此弹 strid 2017 位置告警。
- `VK_get_warning_message(vsurf, int* flags)` @0xa8d1：返回**位掩码**；bit0 置位时宿主加载字符串资源 `0x7e1`(=2017 位置告警)，对应 bit1 走 strid 2016（切线告警）。

→ **边界缝隙检测是内核的一等公民**，这也反证"多张面拼合处必然存在微小缝隙/重叠"是它的主要失败模式。

---

## 2. `xnkernel.dll` 外部特征

### 2.1 PE 概览

| 项 | 值 |
|---|---|
| 机器/子系统 | x64 / GUI |
| ImageBase / SizeOfImage | `0x180000000` / `0x3340000` |
| 入口 RVA | `0x2f86944` |
| 时间戳 | 2020-12-11 07:43:56 UTC |
| DllCharacteristics | `0x160` = HIGH_ENTROPY_VA + DYNAMIC_BASE + NX_COMPAT（**无 CFG**） |
| 版本资源 | `XNKernel DLL` / `2021.0.1.0` / `Copyright (C) 2020 XN. All Rights Reserved.` |
| Debug 目录 | type=13 (POGO, 940 B) + type=14 → **LTCG + PGO 优化构建** |
| TLS | 存在 |
| Overlay | 0（无附加数据） |

**节区与熵（无壳）**

| 节 | VA | VSize | RawSize | 熵 | 判读 |
|---|---|---|---|---|---|
| `.text` | 0x1000 | 0x2feb978 | 0x2feba00 | 6.6537 | 47.9 MB 代码，**未加壳**（正常 C++ 熵） |
| `.rdata` | 0x2fed000 | 0x223834 | 0x223a00 | 5.9241 | 字符串/常量 |
| `.data` | 0x3211000 | 0x61ff8 | 0x1be00 | 2.2423 | 低熵=大量零 |
| `.pdata` | 0x3273000 | 0xc5b50 | 0xc5c00 | 7.1251 | **67,484 个函数**（异常表） |
| `.gfids` | 0x3339000 | 0x2c | 0x200 | 0.224 | CFG 函数表，**仅 11 项** |
| `.tls` | 0x333a000 | 0x9 | 0x200 | 0.0204 | 线程局部 |
| `.reloc` | 0x333c000 | 0x314c | 0x3200 | 5.457 | 重定位 |

**导出：47 个，全部有名字**（清单见 §1.3）。无序号导出。

### 2.2 导入：**关键发现 —— 没有任何外部数学库**

完整依赖（仅系统库 + MSVC 运行库）：

- `KERNEL32` (95)：线程池原语齐备 —— `CreateIoCompletionPort` / `GetQueuedCompletionStatus` / `QueueUserAPC` / `SetThreadAffinityMask` / `GetProcessAffinityMask` / `_beginthreadex` / `WaitForMultipleObjects`
  → **自建线程池做并行**（不是 OpenMP）。
- `ADVAPI32` (26) + `CRYPT32` (16) + `bcrypt` (3)：`PFXImportCertStore` / `CryptStringToBinaryW` / `CertGetCertificateChain` → 授权证书链
- `WS2_32` (31) + `WINHTTP` (5) + `SHLWAPI`：联网激活
- `ole32` / `OLEAUT32`：COM（WMI 硬件指纹）
- `IPHLPAPI!GetAdaptersInfo` + `DeviceIoControl` + `GetVolumeNameForVolumeMountPointW` + `AllocateAndInitializeSid`
  → **硬件指纹**（网卡 MAC + 卷序列号 + 用户 SID）
- `MSVCP140` (85) / `VCRUNTIME140` (24) / `api-ms-win-crt-*`：C++ STL 静态/动态运行库
- `_aligned_malloc` / `_aligned_realloc` → SIMD 对齐分配（热点循环向量化）
- 数学只导入 `acos cos exp expf fabs log logf pow sin sqrt`（连 `atan2` 都没有 → 绝大部分数学是内联/SIMD 实现）

**没有** `mkl_rt.dll` / `libiomp5md.dll` / `vcomp140.dll` / `openblas.dll` / `lapack.dll` / `suitesparse` / `Eigen` 动态库。
→ **所有线性代数与网格库都是静态链接进这个 53 MB DLL 的。**

### 2.3 静态链接的第三方库（字符串证据 + 引用证据）

| 库 | 证据字符串 | 是否被活代码引用 |
|---|---|---|
| **Intel MKL** | `MKL_VERBOSE Intel(R) MKL %d.%d`、`MKL_NUM_THREADS`、`MKL_DOMAIN_PARDISO`、`mkl_malloc`、`Intel MKL FATAL ERROR: ...`、`MKL_DCSRSYMV` | **是**（LAPACK 函数名字符串块 0x2fed400–0x2fed800 有 **1,229 处**引用，来自 `.text` 起始 0x5867/0x587d/0x5919 → MKL 代码位于 `.text` 前部且活跃） |
| MKL·LAPACK | `DGETRF` `DGETRS` `DPBTRF` `DPBTRS` `DGEQP3` `DORMQR` `DSYEVR` `DDOTI` `DSBMV` `DTRTRI` | 是（同上） |
| MKL·PARDISO | `*** error PARDISO: iterative refinement`、`< Hybrid Solver PARDISO with CGS/CG Iteration >`、`Minimum degree algorithm at reorder step is turned ON`、`Task does not fit RAM, OOC LU factorization algorithm is turned ON` | 字符串块仅 2 处引用 → PARDISO 主体可能被 LTCG 裁剪 |
| MKL·FGMRES/CG | `Intel MKL RCI FGMRES ERROR:`、`DFGMRES`、`The tolerance parameter DPAR(1)=...` | **0 处引用 → 死代码** |
| METIS（MKL 内） | `RefineKWay: graph->vwgt`、`AllocateWorkSpace: edegrees`、`< Preprocessing with state of the art partitioning metis>` | **0 处引用 → 死代码** |
| **TetGen** | 完整 `-h` 帮助文本：`A Quality Tetrahedral Mesh Generator and 3D Delaunay`、`-p Tetrahedralizes a piecewise linear complex (PLC)`、`-Y Preserves the input surface mesh`、`Error: Invalid number of tetrahedra.`、`Creating surface mesh ...`、`Surface mesh seconds: %g` | **是 —— 0x308d000–0x3093000 字符串块有 1,181 处引用，引用点落在 0x2e80000–0x2ffffff（即内核自有代码所在区间）** |
| **TurboActivate**（WyDay 授权库） | `TurboActivate.dat`、`The verified trial has expired...`、`The product key has been used with the maximum number of computers` 等 30+ 条 | 是 |
| **libcurl + schannel** | `schannel: failed to send initial handshake data`、`WinHttpGetIEProxyConfigForCurrentUser` | 是 |
| MKL Poisson / Trig | `Intel MKL POISSON SOLVER ERROR:`、`MKL_Poisson_Library_Log.txt`、`Intel MKL TRIG TRANSFORMS` | 块内仅个别引用，**不足以断定被调用** |

> 一个实测到的取数陷阱记录在此，避免后人重复踩：库字符串并非被逐条 RIP 相对寻址，而是整块被引用；
> 因此"对单条字符串做 xref = 0"**不能**证明该库未使用。判定"活/死"必须**按 64KB 块统计引用数**（本文档即用此法）。

### 2.4 内核自有代码的定位

用 `.pdata` 函数表 + 从 47 个导出做**完整** BFS（无预算截断，仅 1,552 个可达函数）：

- 可达代码分布：`46 MB 桶 : 372`、`47 MB 桶 : 1101`（即 **0x2e00000–0x2fffff，最后约 2 MB**），另有 68 个函数在 `0 MB 桶`（MKL）
- 跨桶边：`46MB→0MB : 9`、`0MB→4MB : 5`、`0MB→44MB : 1`、`0MB→27MB : 1`（MKL 内部互调）
- 内核自有区间对外部代码的**直接** call/jmp 共 160 处，落在 `0MB:101`、`4MB:36`、`7MB:6`、`8MB:6`、`23MB:2`、`1MB:2`

**结论：47.9 MB 的 `.text` 中，前约 46 MB 是静态链接的第三方库（MKL 主体在前部），XNurbs 自己的求解器只有最后约 2 MB；**
这 2 MB 与 TetGen 代码在同一区段（TetGen 字符串的引用点就在 0x2e8xxxx），即 **TetGen 是"最后 2 MB"的组成部分之一**。
调用点极少（160 处直接外部调用）说明自有代码是**高度内联/模板化的 C++**（热点已被 PGO 内联），并非靠薄封装调用库。

### 2.5 常量与默认值（从自有代码区 RIP 相对取数提取，1,167 个不同常量）

自有代码区引用的浮点常量里，可读出的语义值（`.rdata` 0x3093xxx–0x3095xxx 常量池）：

| 值 | 站点 | 判读 |
|---|---|---|
| `0.3` | 0x3094xxx，被点约束构造块引用 | **点约束默认权重 0.3**（`VK_add_pt_position_normal_set_cst` 描述块偏移 0x00 与 0x38 都写它） |
| `1e-12` | 0x3093c70 | 数值零阈值 |
| `0.015` `0.02` `0.06` `0.07` `0.1` `0.5` `1` `1.1` `2` `3` `6` `10` `180` `1000` | — | 尺度/容差/角度（`180` 与 `3.14159265359` 成对 → 度↔弧度） |
| `0.0017453283659` | 0x3093d40 | ≈ 0.1°（弧度） |
| 一串倒数式数列 `1/190, 1/171, 1/153, 1/136, 1/120, 1/105, 1/91, 1/78, 1/66, 1/55, 1/45 ...` | 0x30949xx–0x3095xxx（`movaps` 16 字节批量装载） | **按索引递减的权重/采样表**（分母二阶差分恒定 → 二次型索引），疑为基函数/求积权重表 |

（`movaps` 装载的是 16 字节，报告按 double 解析；`as_float` 列对这类批量装载无意义，已在原始文件中保留以便复核。）

---

## 3. 算法结论

### 3.1 证据支持的结论（Proven）

1. **整体形态**：单进程原生 C++ 求解器（MSVC x64，LTCG+PGO），53 MB 中约 46 MB 是静态链接的第三方库；自有求解器约 2 MB。无壳、无 .NET。
2. **它不用外部 BLAS/LAPACK 动态库，而是静态链 MKL**；MKL 的 LAPACK 名字符串被 `.text` 前部活代码引用，**MKL 确实在用**。
3. **静态链了 TetGen**（完整帮助文本 + 运行期消息），且该字符串块**被自有代码区引用 1,181 次** → **TetGen 在求解路径上是活的**。
4. **求解器对象是 "vsurf"（virtual surface）**，宿主只交约束；**初始参数化由内核自己算**（宿主调 `VK_create_vsurf_without_init_para`）。
5. **约束分"边界 / 内部"两类，内部可为曲线或点集**；曲线约束支持"位置 / 位置+法向 / 法线对齐相邻曲线"三种模式。
6. **连续性只有 G0 / G1 / G2 三档**（对话框与命令行长选项都只有这 3 个；G3 只在未使用的 `VK_Calc_C3_dd` 里）。
7. **G1/G2 通过"按参数采样源几何的点与 1~3 阶导数"实现**，宿主以函数指针回调 openNURBS 虚函数；**不要求解析输入**。
8. **输出恒为 3 次（bicubic）NURBS**，次数不可调。
9. **边界缝隙/重叠被显式检测**（`VK_check_open_boundary_difference`）并作为告警位返回（`VK_get_warning_message`）。
10. **存在"力/压力"层概念**（`VK_add_area_pressure`、`VK_remove_physical_force`）→ 求解含能量/力的松弛项，不是纯插值。
11. **精度参数是相对文档绝对容差的倍数**（宿主侧 ×0.01 / ×0.001 派生）。
12. **点约束默认权重 0.3**；点集约束容量 200。

### 3.2 高置信度推断（Inference，标注依据）

1. **路线 = 体参数化 + 约束能量最小化，而非"直接对采样点插值"。**
   依据：TetGen 活的 + `VK_create_vsurf_without_init_para`（内核自算参数化）+ `VK_check_quad_sided_possibility`（四边可行性判定）
   + `VK_get_uv_from_point_cst` / `VK_get_uv_bcurve_from_curve_cst`（把 3D 约束映射进 UV 域）。
   最自然的解释：**把输入曲面壳当作 PLC 做四面体剖分 → 在体网格上解一个（类调和/拉普拉斯）标量或参数场得到全局 UV → 再在 UV 域内用有约束的（最小二乘/能量）拟合出双三次 NURBS**。
   这与它"能处理任意拓扑多重曲面、内外环、非四边区域"的能力完全吻合。
2. **拟合是最小二乘/法方程型，不是插值型。**
   依据：静态链了 `DPBTRF` / `DPBTRS`（**带状对称正定 Cholesky**）—— 这正是 B 样条最小二乘**法方程**的经典解法（法方程带宽 = 基函数支撑数）；
   另有 `DGEQP3`（带列主元 QR，用于秩亏最小二乘/秩检测）与 `DGETRF/DGETRS`（一般 LU）。
   若只是插值，用不到带状 Cholesky 这一族。
3. **能量项 = 薄板/曲率型 + 约束惩罚 + 压力项**。
   依据：`VK_add_area_pressure`（面积压力）+ `VK_remove_physical_force`（物理力）+ 两个滑杆 `质量控制` / `平坦控制`。
   "力"这一命名习惯来自物理松弛法（force-based / dynamic relaxation），"平坦控制"对应薄板（bi-harmonic）惩罚。
4. **并行方式是自建线程池**（`CreateIoCompletionPort` + `QueueUserAPC` + `SetThreadAffinityMask` + `_aligned_malloc`），不是 OpenMP。
5. **它的"局部细节"手段是加内部约束，不是局部插结。**
   依据：对话框约束类型明确列出 `内部` / `点` / `曲线`；API 有 `VK_create_multi_pt_cst`（点集）与内部曲线约束；
   而**全部 47 个导出里没有任何** knot / span / insert / refine / degree / subdivide 相关函数，对话框也没有"控制点数/次数"控件。

### 3.3 推测（Speculation，明确标注）

- 体的具体用途（是用于参数化、还是只用于内外判定与拓扑修复）无法从静态证据定论。倾向"用于参数化"，但**未证实**。
- 能量泛函的精确形式（各项权重、是否含扭率项）未知。已知 `质量控制` / `平坦控制` 两个滑杆 + 0.3 默认点权重，不足以反推公式。
- MKL 的 FGMRES / CG / ILUT / METIS / Poisson / Trig 字符串**存在但零引用**，说明这些**路径未被使用**（或被 LTCG 裁剩数据）。
  因此**不能**声称它用迭代法解大稀疏系统；证据更偏向"带状/稠密直接法"。
- 是否做自适应局部细化（局部插结）**无证据**；从"内部约束 + 精度滑杆"的组合看，它的自适应是**在约束层面**而非**在节点向量层面**。

### 3.4 未能验证 / 卡点

- **无法给出任何内核函数的 C 签名**：无 PDB、无导出参数信息、C++ 名字被 `extern "C"` 导出抹掉。参数结构体是**从调用点的栈布局反推**的（如点约束块 = `{double w; pad; Point3d pos@0x18; int flags[2]@0x30; double w2@0x38; Vector3d nrm@0x40}`），**字段语义属推断**。
- 内核自有代码仅约 2 MB 但高度内联，逐函数还原求解器核心（能量装配、参数化求解）工作量远超本次范围，未做。
- 未做动态调试（不启动 Rhino 加载 XNurbs，避免干扰用户环境）。

---

## 4. 对我们插件（SurfaceUnify）的可执行优化清单

**现状**（`E:\IVAN-LiquidGlass-preview\implementation\MODIFIED_FILE\surfaceunify\src\SurfaceUnifyCore.cs`）：
Coons 基面（`CoonsGrid` L897）→ 射线贴合（`SnapToSheet` L996）+ `SnapBoundary`(L1120) → 拉普拉斯光顺（`SmoothGrid` L968）→
**`NurbsSurface.CreateThroughPoints` 插值**（`BuildSurface` L1276）→ 内孔 `BrepFace.Split` → 25×25 采样测偏差（`Measure` L1514）。
另有爬行网格 `BuildPeelGrid`(L1142) 用**单一全局步长** `step = march*1.35/(n-1)`(L1166)。

对照 XNurbs 的 12 条证据，给出 6 条改动（按对用户 4 项诉求的收益排序）。

---

### 改动 1（诉求①：比 `_Patch` 更精准）把 `BuildSurface` 从"插值"改成"带硬约束的加权最小二乘"

- **改哪里**：`SurfaceUnifyCore.BuildSurface`(L1276) 的 `CreateThroughPoints` 调用；调用方 `GenerateOnce`(L184) 传样点的方式。
- **怎么做**：
  1. 选**夹持均匀节点向量**（clamped uniform）作为双三次 NURBS 的固定节点：u 方向 `nu` 个控制点 → 节点向量 `{0,0,0,0, 1/(nu-3), ..., 1,1,1,1}`（v 同理）。夹持节点的好处：`S(u_min,v)` 与 `S(u_max,v)` 恰好是边界曲线 → 边界约束只需落在控制网首末行。
  2. 组装线性系统 `A·P = b`，未知量 = 控制点 `(nu+2)×(nv+2)` 的三坐标。三类行：
     - **数据行**（权重 `w_d`）：对每个射线贴合成功的样点 `p_jk`，行 = `[N_i(u_j,v_k)]`，右端 = `p_jk`。
     - **硬边界行**（权重 `w_b = 1e3·w_d`，G0）：`SampleArc` 得到的 4 条边样点，行只含首/末行基函数。
     - **G1 行**（权重 `w_t`，可选）：边界的 `∂S/∂n` 应等于相邻面的法向分量 → 行 = 首/末行基函数的一阶导（`NurbsSurface` 无解析导数时用中心差分，步长 `1e-6`）。
  3. 正则化行（权重 `λ`，对应 XNurbs 的"平坦控制"）：控制点二阶差分 `P(i+1)+P(i-1)-2P(i)`（u、v 各一组）。
  4. 解 `(AᵀA + λ·RᵀR) P = Aᵀb`。`nu=nv=16` 时未知量 18×18=324 个标量/坐标，**稠密 Cholesky 毫秒级**：用 `Rhino.Geometry.Matrix` 组 `AᵀA`，`Rhino.Geometry.LinearSystem.Solve` 求解；不要自己写迭代法。
  5. 把 `nu,nv` 从"插值必须点数"解耦：**样点数可以远多于控制点数**（这正是插值做不到、而最小二乘能做的），于是射线采样可以加密而控制点数不必跟着涨。
- **为什么更准**：`_Patch` 与 XNurbs 都用最小二乘；插值会被噪声/离群射线命中"过拟合"（一个坏点把整张面拉出去）。改成最小二乘 + 硬边界行后，边界仍然**精确**（硬约束），内部则取最小残差 —— 两个目标不再互相妥协。
- **怎么验证**：在 `SurfaceUnifySelfTest.cs` 新增用例：对同一输入，人为把 5% 的样点沿法向偏移 `0.05·diag`。
  断言：插值版边界偏差 > 容差（失败）；最小二乘版**边界偏差 ≤ 1e-6·diag** 且**内部最大偏差比插值版下降**。保留现有 114 项全绿。

---

### 改动 2（诉求③：边界精确贴齐、拐角节点一致）

- **改哪里**：`Corners`(L682 附近)、`SnapBoundary`(L1120)、`BuildSurface`(L1276)、`MeasureResultBoundary`(L1308)。
- **怎么做**：
  1. **拐角取真实 Brep 顶点**：现在外环 4 角取"对角极值"（网格点）。改成：取该角附近 `Brep.Vertices` 中距网格角点最近的真实顶点，再 `Brep.ClosestPoint` 精修 → 角点变成**原几何的精确角**，四角天然与相邻面共享同一坐标。
  2. **夹持节点 + 边界行只落在控制网首末行**（改动 1 已给）：这样"节点一致"是结构性保证，不靠事后吸附。
  3. **加每边缝隙检查**（对应 `VK_check_open_boundary_difference`）：新函数 `MeasureEdgeGaps(result, loops, out double[4] gaps)` —— 对 4 条边各采样 64 点，测 `S(edge)` 到输入边界折线的最大距离。当前 `MeasureResultBoundary` 只给一个总偏差，**无法定位是哪条边缝了**。
  4. **加角点一致性检查**：断言 `S(u_min,v_min)`、`S(u_max,v_min)`、`S(u_min,v_max)`、`S(u_max,v_max)` 与 4 个真实顶点距离 ≤ `1e-9·diag`（现有 `CornersMatch` L1291 只校验点序，容差 1e-6 且比的是网格点，需升级为比真实顶点）。
- **怎么验证**：新增"4 张面拼的盒体"用例（相邻面在角点处故意留 `1e-4` 偏差），断言 4 条边 gap 均 < `1e-6·diag` 且 4 角一致；再跑 `2234.3dm` 目标 1 确认边界偏差 ≤ 0.07（不退化）。

---

### 改动 3（诉求②：局部复杂处局部加结构线）用"内部约束注入 + 残差驱动迭代"代替全局抬控制点数

> XNurbs 的答案就是这一条：**它的局部细节靠内部曲线/点集约束，而不是靠局部插结**（§3.2 第 5 条，有 API 与对话框双重证据）。

- **改哪里**：新增 `AddInteriorConstraints(...)`，在 `GenerateOnce`(L184) 的"贴合→光顺→建面"之间插入迭代环；复用 `SnapToSheet`(L996)、`Measure`(L1514)、`MeasureTrimmedDeviation`(L1729)。
- **怎么做**（残差驱动，4 轮上限）：
  1. 用改动 1 的 LS 建面，然后在**贴合网格分辨率**上（不是现在的 25×25）算残差场 `r(i,j) = |S(u_i,v_j) − p_ij|`。
  2. 找出 `r > 0.5·maxTol` 的连通单元簇（`maxTol` = 面板"最大贴合距离"）。
  3. 对每个簇：把簇内样点作为**一个约束组**追加（对应 `VK_create_multi_pt_cst` —— 一组点共用一个权重，`w = 1.0`，与已有数据行区分）。
     若要"结构线"效果（用户说的等参线）：取该簇的主方向，跨簇采一条点列，作为一条**内部曲线约束**追加（对应 `VK_add_curve_cst`），权重高于数据行。
  4. 重解 LS（只增行，矩阵复用），循环 2–4 轮或残差不再下降为止。
  5. **参数化仍固定**（节点向量不变）→ 不会因为加约束而改变整体形状，只把局部拉准。
- **为什么优于现状**：现状是"抬 `nu/nv`"（用户已实测 12→16 才让面积比到 0.996）——全局加点是**全局代价、且会重新引入振荡**。按残差加约束只动坏区域，控制点数可以保持 16。
- **怎么验证**：`2234.3dm` 目标 1（袋口卷边，现残余最大偏差 8.74、折叠单元 16）：
  断言在 `nu=nv=16` 不变的前提下，卷边区域的最大偏差下降 ≥ 30%，且面积比 ≥ 0.996 不退化；日志输出每轮残差。

---

### 改动 4（诉求②的几何前提：让网格能覆盖住细节）`BuildPeelGrid` 的全局步长改曲率自适应

- **改哪里**：`BuildPeelGrid`(L1142) 第 1166 行 `double step = Math.Max(march * 1.35 / (n - 1), 1e-9);`
- **怎么做**：
  1. 步长逐点化：`step_i = step_base · clamp(κ_ref / κ_i, 0.5, 2.0)`，`κ_i` 取该点最大主曲率（`Brep.CurvatureAt` 的 `MaximumPrincipalCurvature`；Brep 为空时用网格 `Mesh.Curvature` 或邻域法向夹角近似）。
  2. 每行生成后**按弧长重新等分**（复用 `SampleArc` 的思路），保证行内点均匀、且行与行在弧长上对齐 —— 这是"等参线均匀"的前提。
  3. 走不动的点（`stalled`）冻结，但**只冻结该点**，允许邻居继续（现在 `stalled` 只是计数，行是否继续由整行 `maxMove` 决定）。
- **为什么**：卷边/高曲率处需要更密的行，平坦处不需要 —— 全局步长必然两头不讨好（要么卷边欠采样，要么平坦区浪费行数并让 LS 病态）。
- **怎么验证**：碗形兜袋用例断言 `stalled` 计数下降；新增圆柱用例断言相邻行间距的变异系数 < 5%；回归断言面积比 ≥ 0.99。

---

### 改动 5（诉求④：别卡）把"光顺"折进 LS，并去掉每次迭代的数组克隆

- **改哪里**：`SmoothGrid`(L968)、改动 1 的 LS 装配、`GenerateOnce`(L184) 的整体循环。
- **怎么做**：
  1. **`SmoothGrid` 的每次迭代 `(Point3d[,])g.Clone()` 是一次全数组分配**（L972）—— 改成双缓冲（两个预分配数组互换）。iters 大时这是纯浪费。
  2. **把拉普拉斯/薄板正则项作为 LS 的正则化行**（改动 1 第 3 步），而不是"先光顺再建面"。现在的流程里光顺与建面是**串行两步、互相破坏**（光顺把点挪开，`CreateThroughPoints` 又强制穿过挪开后的点）；折进一个系统后是**一次求解同时满足拟合与光顺**，且**不需要多次迭代**。
  3. 正则项从一阶拉普拉斯升级为**二阶（薄板）差分**：`Δ²P = P(i+2,j)+P(i-2,j)+P(i,j+2)+P(i,j-2) − 4P(i+1,j) − 4P(i−1,j) − 4P(i,j+1) − 4P(i,j−1) + 8P(i,j)`，边界行用单侧差分。一阶拉普拉斯会**收缩**面（曲率不惩罚），二阶才对应"平坦控制"的语义。
  4. 射线贴合结果**缓存**：改动 3 的迭代只重解 LS，**不重新打射线**（射线 + `Brep.ClosestPoint` 是当前最贵的部分）。只在控制点数变化或边界变化时重采样。
- **怎么验证**：加计时断言（`2234.3dm` 目标 1 端到端 < 现状耗时）；加"平坦区不加曲"断言（数据平坦处的二阶差分最大值不高于输入）；L 形折板用例跑曲率梳/斑马纹等价检查（比较二阶差分分布）。

---

### 改动 6（可诊断性）照搬 `VK_get_warning_message` 的**位掩码告警**

- **改哪里**：`SurfaceUnifyResult` 加 `[Flags] int WarnFlags`；面板（`SurfaceUnifyUi.cs`）与自检（`SurfaceUnifySelfTest.cs`）消费它。
- **怎么做**（位定义照 XNurbs 的告警语义）：
  - bit0 边界存在重叠/间隙且位置要求可能无法严格满足 → `MeasureEdgeGaps` 任一 gap > `docTol`
  - bit1 相邻几何切线方向不一致，切线要求可能无法严格满足 → G1 行残差 > `2·w_t` 目标
  - bit2 结果被修剪/存在折叠单元 → 现有"折叠单元数" > 0
  - bit3 有样点未能贴合 → 现有"未贴合点数" > 0
- **为什么**：XNurbs 的失败是**可解释**的（strid 2012/2016/2017 直接告诉用户"改哪里"）。我们现在的失败信息散在报告文本里，用户看不出该动哪个滑杆。
- **怎么验证**：为每个 bit 构造一个退化输入的单测；断言 bit 正确置位、面板显示对应文案。

---

### 不做的事（避免走偏）

- **不要引入第三方数值库**：改动 1 的规模（16–24 控制点）稠密 Cholesky 足够；`nu=nv=16` 时 324 未知量、`nu=nv=32` 时 1156 未知量，`LinearSystem.Solve` 都是毫秒级。**不要**去链 MKL/LAPACK。
- **不要抄 XNurbs 的 TetGen 路线**（体剖分 + 体参数化）。那是 2 MB 级求解器 + 53 MB 库的工程量；我们的场景（片状壳、已有一圈裸边）用"Coons 初值 + 曲率自适应爬行 + 约束最小二乘"能拿到 80% 的效果。
- **不要加"局部插结"**：XNurbs 自己都没做（47 个导出里没有任何 knot/insert/refine）。用户要的"局部结构线"用改动 3 的**内部曲线约束**实现，语义相同、实现成本低一个数量级。

---

## 原始产物清单

目录：`E:\IVAN-LiquidGlass-preview\implementation\xnurbs-re\`

**分析脚本（可复跑，只读目标文件）**

| 文件 | 用途 |
|---|---|
| `recon_pe.py` | PE 头/节区+熵/导出/导入/延迟导入/TLS/资源/overlay 全量 dump |
| `extract_strings.py` | ASCII + UTF-16LE 字符串抽取（带偏移、去重） |
| `filter_ui.py` | 从字符串堆里筛出人类 UI 文本（滤掉 C++ 修饰名/路径） |
| `dump_resources.py` | PE 资源枚举 + 落盘 |
| `parse_dialog.py` | **完整 RT_DIALOGEX / RT_STRING 解析**（控件 id + class + 文本）→ 还原参数体系 |
| `disas.py` | 导出函数反汇编 + 跳转链追踪（`--follow <name>`） |
| `full_disas.py` | 小 PE 全 `.text` 反汇编 + 内联标注（字符串/导入/常量），带重同步 |
| `funcs.py` | `.pdata` 函数边界表 + 从导出做 BFS 调用图探测 |
| `bfs_deep.py` | 深 BFS + 跨桶边统计（判定库可达性） |
| `region_calls.py` | 指定代码区间的**全部**外部直接 call/jmp 目标分桶 |
| `consts.py` | 自有代码区 RIP 相对浮点常量提取（容差/权重/尺度） |
| `xref_imports.py` | PE 内对指定 DLL 导入函数的调用点定位 |
| `xref_addr.py` | 对指定 VA 的 RIP 相对引用定位（找"谁引用了这个字符串"） |
| `find_ptr.py` | 全镜像 8 字节指针追踪 + 对指针槽的代码引用（追消息表） |

**产物**

| 文件 | 内容 |
|---|---|
| `XNurbsRhino.recon.txt` | `.rhp` PE 全量 recon（含完整导入表 310+117 条） |
| `xnkernel.recon.txt` | `xnkernel.dll` PE 全量 recon |
| `kernel_recon_stdout.txt` | 内核 recon 运行日志 |
| `XNurbsRhino.rhp.strings.txt` | `.rhp` 字符串（1,373 条去重） |
| `XNurbsRhino.rhp.ui.txt` | `.rhp` UI 类字符串（213 条） |
| `rhp_dialog_dump.txt` | **对话框 31 控件 + RT_STRING 全文**（参数体系证据） |
| `rhp_text_full.txt` | **`.rhp` 全 `.text` 反汇编（17,203 行）**，含 `IMPORT xnkernel.dll!VK_*` 与 `WSTR` 标注 |
| `rhp_kernel_callsites.txt` | 内核导入调用点统计 + 邻域反汇编窗口 |
| `xnkernel.dll.strings.txt` | 内核字符串（23,690 条去重） |
| `api_VK_*.txt` / `api_calc_c1.txt` | 12 个关键导出函数的反汇编（含跳转链） |
| `consts_xnurbs_region.txt` | 自有代码区 1,167 个浮点常量（值 + 站点 + 出现次数） |
| `funcs_report.txt` | 67,484 函数边界、40 个最大函数、2MB 桶直方图、BFS 结果 |
| `bfs_deep.txt` | 深 BFS 分桶与跨桶边（库可达性判定） |
| `region_calls_xnurbs.txt` | 自有代码区 160 处外部直接调用的目标分桶 |
| `rhp_res/` | `.rhp` 全部 12 个资源 + 清单（含对话框原始模板 `RT_DIALOG_2000_2052.bin`） |

**未执行**：动态调试（不加载 XNurbs 到 Rhino 进程）；内核求解器核心（参数化装配 / 能量装配）的逐函数还原。
