# IVAN プラグインセンター · Rhino 7/10 プラグイン集

[简体中文](README.md) | [English](README.en.md) | **日本語** | [繁體中文](README.zh-TW.md)

**Rhino 7 / Rhino 8** 向けの .NET プラグイン集です：**7 つのパラメトリックモデリングプラグイン** + グラスモーフィズム調の**プラグインセンター**（インストーラー / ランチャー）。
各プラグインは「1 コマンド + パラメータパネル + リアルタイムプレビュー + ヘッドレス自己テスト」で構成され、**サードパーティ依存はありません**（RhinoCommon / WinForms のみ）。

> UI と操作はリポジトリ内の『デザイン一貫性仕様』に準拠：統一されたガラス調カード、セマンティックコントロール
> （スライダー + 数値ボックスの連動）、ピックボタンは**有効な対象を選ぶと緑 / 未選択・誤選択は赤**、
> 150/200/300 ms の 3 段階モーション、共通のリニアアイコンファミリー。

## ダウンロード / インストール

| バージョン | ダウンロード | 備考 |
|---|---|---|
| **v1.2.0** | [**IVAN-CENTER.exe**](https://github.com/IvanXxxxyyuffff/ivan-rhino-plugins/releases/download/v1.2.0/IVAN-CENTER.exe) | Windows x64 インストーラー（3.3 MB、md5 `ddf66a708a37a431e7ba337c1f2dad9f`）
| v1.1.0 | [IVAN-CENTER.exe](https://github.com/IvanXxxxyyuffff/ivan-rhino-plugins/releases/download/v1.1.0/IVAN-CENTER.exe) | 前バージョン（8 プラグイン、2.9 MB、md5 `4a7932421e68dfc91d64618c63f9f648`） | |

1. **Rhino を終了してから**インストーラーを実行 → `%LOCALAPPDATA%\IVAN\plugins` にインストール（10 プラグインを登録し、ツールバーにボタンを追加）
2. Rhino を起動：ツールバーに 7 つのボタンが表示され、クリックでパネルが開きます
3. ソースからビルドすることもできます（下記）

> Rhino 本体は含まれません（Rhino 7 または 8 が必要）。**プラグインの UI は現在 簡体字中国語のみ**です。

## プラグイン一覧

| # | プラグイン | コマンド | 機能 |
|---|-----------|---------|------|
| 1 | **StripeOnSurface** | `StripeOnSurface` | サーフェス/ポリサーフェス上にストライプを生成。端部は面取り / フラット / 境界に完全追従（エッジ距離で内側にオフセットし、端部は境界形状でトリム）。ハードエッジはフィレット可能。幅・間隔・角度・エッジ距離を実寸 mm でリアルタイム調整 |
| 2 | **VapeVolume** | `VapeVolume`, `VapeVolumeWatch` | ボトル/タンクの容量と注入量を計算し、リアルタイム表示 |
| 3 | **HalftoneDots**（パラメトリックテクスチャ） | `ParametricTexture` | 6 配列（グリッド / 千鳥 / 六角 / 同心円 / スパイラル / ジッター）× 4 形状（円 / 三角 / 四角 / 六角）。同心円・スパイラル・ジッターは中心を指定して外側へ拡散。曲面でも図形が歪まず、面全体に自動で敷き詰め |
| 4 | **VoronoiTexture** | `VoronoiTexture` | 平面境界内にボロノイ（Voronoi）セルの凹凸テクスチャを生成：セル中心点、セル壁の厚み、グラデーションオブジェクトで密度制御。出力は「ワイヤーフレーム / メッシュ」の二択で、「ワンクリックスムーズ」で**SubD（サブディビジョンサーフェス）**に変換 |
| 5 | **RadialDots** | `RadialDots` | 半径方向にグラデーションするドットパターン：4 配列 × 5 形状。サイズはピーク位置と減衰で変化。重なった図形は自動でブーリアン結合 |
| 6 | **MeshFix** | `MeshFix` | 「シェーディング/レンダリング表示で複雑なトリムサーフェスがエッジ線だけになる」問題をワンクリック修復：オブジェクトのレンダーメッシュ「最大アスペクト比」を 0 → 6 に変更して再構築。**パネルなし**（選択があれば選択分、なければファイル全体をスキャン） |
| 7 | **DiamondFacet** | `DiamondFacet` | 平面/閉曲線境界内に凹凸のあるダイヤモンドカット面を生成：ランダム三角分割 + 頂点のランダム高低、境界固定オプション。出力は「ワイヤーフレームのみ / 面」の二択で、面モードでは**各三角ファセットが 1 枚のメッシュパッチ**になり細分化できます |
| 8 | **WaterRipple** | `WaterRipple` | サーフェス / ポリサーフェス（1 枚の面として扱う）/ 閉平面境界に水面の波紋を生成：3 つの波形（**オーガニック / 方向性バンド / 同心円リップル**）を切替、波長・波高・波数・主方向・広がり・波頭形状を調整可；**境界固定**と境界ブレンド（幅 / 滑らかさ）；メッシュ出力、ワンクリックスムーズで **SubD**（境界に crease を入れて角を保つ） |
| 9 | **SurfaceUnify** | `SurfaceUnify` | 複雑なポリサーフェス（またはサーフェス / 押し出し / メッシュ）を**1 枚の開いた NURBS サーフェス**へ変換：境界は元の裸エッジに完全追従、内部はベース面法線に沿ったレイキャストでフィット（分割数 / フィット強度 / スムーズ度 / 最大スナップ距離を調整可）、内側の穴は投影してトリム。ライブプレビューと最大 / 平均 / 境界偏差の表示 |
| 10 | **PatchFill** | `PatchFill` | 境界曲線/サーフェス エッジのループ（N≥2）を選び**1 枚の滑らかな NURBS サーフェス**で補填：境界連続性 **G0 / G1 / G2**（G2=曲率）、隣接面の微分サンプリング、内部曲線/点拘束、**面積圧力エネルギー項**、**残差駆動の局所適応ノット挿入**、辺ごとギャップ+ビットマスク診断；ライブプレビュー |

各プラグインには自己テストコマンド（例：`VoronoiSelfTest`、`DiamondFacetSelfTest`）があり、ヘッドレスで幾何アサーションを実行できます。

## リポジトリ構成

```
implementation/
  MODIFIED_FILE/                  プラグインのソース
    stripe/                       StripeOnSurface + プラグインセンター（center：インストーラー / ランチャー）
    voronoi/  halftone/  radialdots/  meshfix/  diamondfacet/
                                  残り 5 プラグイン（それぞれ Rhino7 + Rhino8 のデュアル TFM プロジェクト）
    src/VapeVolume/               VapeVolume
    shared/PanelTheme.cs          全パネル共通のカスタムコントロール + モーション + アイコン描画（各プラグイン内はバイト単位で同一のコピー）
    iconmake/                     アイコンレンダラー（リニアアイコン一式をコードから生成、ビットマップ素材なし）
    PROJECT-STATE.md              プロジェクト状態 / ラウンドごとの変更 / ハマりどころ（正本）
    DESIGN-CONSISTENCY.md         デザイン一貫性仕様（プラグイン追加時に必読）
    UI-REFACTOR-HANDOFF.md        UI リファクタ引き継ぎ
  *.ps1 / *.py                    ビルド・インストール・自己テスト・監査のワンショットスクリプト
index.html / app.js / styles.css / icons/ …   HTML UI プロトタイプ（グラスモーフィズムのモック + スクリーンショット）
```

## ビルド

要件：Windows + .NET SDK 7/8 + Rhino 7 または 8（RhinoCommon は NuGet から取得するため、コンパイルに Rhino 本体は不要）。

```bash
# 単一プラグイン（デュアル TFM：Rhino 7 = net48、Rhino 8 = net7.0-windows）
dotnet build implementation/MODIFIED_FILE/voronoi/Rhino8/VoronoiTexture.csproj -c Release
dotnet build implementation/MODIFIED_FILE/voronoi/Rhino7/VoronoiTexture.csproj -c Release

# 全プラグイン + プラグインセンター
python -X utf8 implementation/native-gate.py build-panels
```

成果物は各プラグインの `out/rhino7|rhino8/` に出力されます（`.rhp` は Rhino にドラッグするか、プラグインセンターからインストールできます）。

## インストール / 自己テスト

```bash
pwsh -File implementation/refresh-payload-and-center.ps1   # payload を集約 + プラグインセンター exe をビルド
pwsh -File implementation/install-and-verify.ps1           # サイレントインストール + 検証（レジストリ / ツールバー / payload がバイト一致）
pwsh -File implementation/run-native-selftests.ps1         # 10 プラグインの自己テストを一括実行（非表示ウィンドウ）
pwsh -File implementation/run-native-selftests.ps1 -Only Voronoi   # 単一プラグイン
```

インストール先：`%LOCALAPPDATA%\IVAN\plugins\<プラグイン>\rh8\<プラグイン>.rhp`（Rhino 8）/ `rh7\`（Rhino 7）。

## ドキュメント

- [`PROJECT-STATE.md`](implementation/MODIFIED_FILE/PROJECT-STATE.md) — プロジェクト状態、各プラグインの仕様、ラウンドごとの変更と**ハマりどころ**（幾何 API、RhinoCommon の罠、検証フロー）
- [`DESIGN-CONSISTENCY.md`](implementation/MODIFIED_FILE/DESIGN-CONSISTENCY.md) — ピクセル単位のレイアウト、セマンティックコントロール、ピックボタンの赤/緑ルール、アイコンファミリー、自己テスト形式
- [`UI-REFACTOR-HANDOFF.md`](implementation/MODIFIED_FILE/UI-REFACTOR-HANDOFF.md) — UI リファクタ引き継ぎ

## ライセンス

MIT — [LICENSE](LICENSE) を参照。
