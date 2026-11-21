# Lumine v2 製品競争力・創作ワークフロー・技術構成 再監査（初期ベースライン）

- 記録日: **2026-11-21（JST）**
- Tracking: [Issue #816](https://github.com/Kataage/Lumine/issues/816)
- 調査対象: `Kataage/Lumine`、v2 `develop`、旧v1.1.0は製品要件の参照のみ
- 調査開始時の `develop` HEAD: `eae45e0177eb1edd21d5df3f9eb0469518975735`（PR #815 を含む）
- **Status: initial evidence inventory / NOT final adoption decision**
- 原則: 実装確認、公式の機能説明、第三者の実測、推測、要検証を混ぜない。CI green ≠ 完成品の品質。

## 0. 監査の理由と結論の前提

2026年9月の監査はデスクトップフレームワーク、画像デコード、SQLite、Windows監視、AI推論基盤について深く調査した。一方、2026-10のv2製品再監査 [v1-product-gap-audit](../v1-product-gap-audit.md) は「旧Lumineの製品機能に戻す」ことが主要な比較軸であり、**外部製品とAI画像制作者のタスク完遂を同条件で比較する証拠**が不足していた。

2026-11-21に公開されたTsumikiをきっかけに、作り直し自体ではなく**何を最適化する製品なのか**を再検討する。現在の.NET 10/Avalonia/NativeAOTを捨てる判断、または絶対に残す判断を先取りしない。

## 1. エビデンスの付け方

| コード | 意味 | 採用判断に使えるか |
|---|---|---|
| CODE | 対象コミットのソースで確認 | 実装の存在・境界は判定可、使用感は別 |
| REPO-DOC | 自製品の仕様・テスト文書 | 意図/試験設計。実装済みの保証ではない |
| OFFICIAL | 競合の公式サイト・公開記事の説明 | 競合が「主張している」事実 |
| MEASURED | 同一条件・再現可能な実機計測 | 条件一致後に性能比較可能 |
| UNKNOWN | 一次資料／動作確認が不足 | 未実装・劣っていると断定しない |

※以下で「競合が先行」と書く場合も、主に **OFFICIAL 対 CODE/REPO-DOC** の比較であり、第三者による品質優劣の断定ではない。

## 2. 競合一次資料（2026-11-21確認）

| 対象 | 公式ソース | この段階で確認できること | 未確認・留保 |
|---|---|---|---|
| **Tsumiki v1.0** | https://studiomasakaki.com/articles/12959649/ | 生成画像の取り込み、Forge/ComfyUI/NovelAIメタデータ、プロンプト/モデル/LoRA/Seed検索、SFW/NSFW切替、日本語タグ連携、WD14付きLoRAデータセット作成を作者が紹介 | 実アプリの再現テスト、全機能の制約、RAM/VRAM/速度の独立測定、ソース構成は未検証 |
| **Diffusion Toolkit** | https://github.com/RupertAvery/DiffusionToolkit | AI生成画像のメタデータ検索・閲覧。リリース文書にはComfyUI workflow/raw metadata検索の記述あり | 形式別の適合率・取り込み速度は未測定 |
| **Hydrus Network** | https://hydrusnetwork.github.io/hydrus/getting_started_searching.html | タグとシステム属性のpredicateを組み合わせる検索 | AI生成画像メタデータへの対応範囲やUXは別調査 |
| **Eagle** | https://en.eagle.cool/ | スマートフォルダ、タグ、重複整理、各種制作連携・MCPを公式サイトで案内 | 画像生成固有のプロンプト検索、原画像保持方式、Tsumikiとの性能差は不明 |
| **Breadboard** | https://github.com/cocktailpeanut/breadboard/releases | 公式リリース記録で画像カード・フィルタ等を確認 | 現行の製品方向、画像管理の現行機能と安定性は要再検証 |

**重要:** Tsumikiの「数十万枚・メモリ負荷」などの公開値をLumineと直接比較しない。ライブラリ内画像形式、画素数、キャッシュ状態、測定機材、操作負荷が一致しない。WebView等の内部採用技術も一次証拠がなければ断定しない。

## 3. Lumine v2 の実コード/文書の初期点検

| 確認対象 | 証拠 | 初期判断 |
|---|---|---|
| 基盤 | [README](../../README.md) | .NET 10、Avalonia 12、SQLite、libvips、Windows NativeAOT（REPO-DOC） |
| ユーザータグ・評価・ステータス・検索 | [LibraryModels](../../src/Lumine.Library/LibraryModels.cs) `AssetUserMetadata`, `AssetQuery`; [search-core](../search-core.md) | 実装されている検索APIの対象を確認（CODE/REPO-DOC） |
| 日本語2文字の通常テキスト検索 | [search-core](../search-core.md) | CJK bigram+FTS5による filename/notes/tags等の検索。「Danbooru辞典同義語検索」とは別（REPO-DOC） |
| 画像EXIF | [LibraryRepository.Exif](../../src/Lumine.Library/LibraryRepository.Exif.cs) | 撮影機材/ISO/GPS等の保持。PNGの生成ワークフロー抽出を示す証拠ではない（CODE） |
| 制作履歴のデータモデル | [CreativeArchiveModels](../../src/Lumine.Library/CreativeArchiveModels.cs) | GenerationGroupには Prompt/NegativePrompt/ModelName/Sampler/Scheduler/Steps/CFG/WorkflowJson の項目がある（CODE）。**自動抽出したことの証拠ではない** |
| 生成画像の自動解析→個々の画像へ紐付け→検索 | 上記Repository/検索APIの点検範囲 | 一貫した経路をまだ確認できない（UNKNOWN）。全ソース追跡、データベースDDL、UIとfixture検証が必要 |
| 大量画像Viewer | [product-acceptance](../product-acceptance.md)、[PR #815](https://github.com/Kataage/Lumine/pull/815) | NativeAOTでの自動受入と方向優先lookaheadの修正実績。ただし画面外先読みの自然スクロール実機承認は未済（REPO-DOC） |
| Tsumiki型の学習データセット編集/外部制作アプリ連携 | 現時点のLibrary/App点検範囲 | この段階では確認できず（UNKNOWN）。未実装と断定しない |

### 現時点での最も重要な設計上の疑問
`GenerationGroup`がプロンプト情報を**保存できる**ことと、生成結果ファイルの埋め込み情報を**自動で解析・検索できる**ことは別能力。将来の「意味検索」より先に、画像生成環境で既に得られる決定的なメタデータを構造化し検索できるかを優先検証する。

## 4. ユーザー作業タスク — 10シナリオの実測仕様

| ID | 実際に達成する操作 | 合格の根拠（スクリーンキャプチャ＋手順＋時間） |
|---|---|---|
| U01 | ComfyUI生成PNGを登録→プロンプト/モデル/LoRA/Seedを確認 | parse成否、原データ復元性、UI操作数 |
| U02 | Forge/A1111画像のパラメータで絞る | 形式混在、AND/NOT、未知値 |
| U03 | NovelAI画像も同じ検索画面で探す | タグ/パラメータ正規化と原文保持 |
| U04 | 日本語「金髪」「ツインテール」とDanbooru英語タグで検索 | 辞書と形態の違い、false positive |
| U05 | LoRA名+Checkpoint名+Seedの複合条件で特定画像を探す | 正確な一致/不存在時no-match |
| U06 | 生成情報を失った画像へ手動/自動タグを付ける | userとmachine tagを明確に区別 |
| U07 | 同一画像・近似画像・アップスケール元を追跡 | 重複の定義と可逆操作 |
| U08 | ComfyUI再生成・CLIP STUDIO編集へ戻る | 保存済みworkflowの復元可否と失敗時の導線 |
| U09 | 選別画像→WD14→キャプション修正→LoRA学習用エクスポート | 一括/個別編集、元画像非破壊、kohya等の形式 |
| U10 | 生成からWork/GenerationGroup/Publicationまで追跡 | 差分/公開版の並び・履歴の正確さ |

各シナリオは**5点満点の主観評価だけ**にしない。検証に使った画像セット、作業成功率、操作数、所要時間、誤検索率、失敗からの復帰、RAM/VRAM/ディスク増加、初回/再起動を記録する。

## 5. 性能/品質の比較条件

1. 全製品を同じWindows PC / 同じライブラリコピー / 同じディスク条件で計測する。NVIDIA GPUの型番とドライバ、CPU/RAM、解像度、画像の形式・画素分布を記録。
2. ライブラリ規模: 4k現実データ、10k/100k代表セット、可能なら400k超。大量件数は実画像と合成fixtureを混ぜた場合の境界を記録。
3. 起動・初画面有用表示・自然ホイール前後・遠距離ジャンプ・詳細表示・検索 first useful resultをCold/Warm別に測定。
4. メモリはidle/ピーク、VRAM、ファイルI/O、キャッシュ容量、バックグラウンド終了時間を別々に記録。
5. 画像品質、向き、ICC、透過、アニメ、メタデータ改変、原本の不変性を検証する。
6. Tsumikiの実行バイナリ/実機スクリーンが未入手なら **benchmark UNKNOWN** のままにする。宣伝文の数値を採用しない。

## 6. 初期の優先順位（仮説）

- **P0 / 調査**: 生成メタデータの入力フォーマットとSQLite schema/index/UI検索の実装状況をコード・テストで確定する。旧機能の見逃しを防ぐ。
- **P0 / 調査**: 画像管理→制作再利用のU01–U10を現v2で試し、競合の公式説明に対するギャップを証拠化する。
- **P0 / 継続**: #634/#805/#803/#635等、既存の実機承認待ちIssueは残し、CI成功のみで閉じない。
- **P1 / 候補**: 生成メタデータ抽出・構造化検索 / Danbooru・日本語同義語 / 保存検索（スマートフォルダ）/外部アプリ再編集導線。
- **P1 / 候補**: 重複・派生関係 / タグ支援 / 学習データセット出力。ただし優先度は実タスクの効果測定後に確定。
- **P2 / 候補**: semantic/vision/reranker/agentなど重いAI機能。既知のメタデータで正確に解ける問題をAIで置換しない。
- **決定保留**: .NET/Avaloniaの維持・部分再設計・全面再構築。同条件benchmarkで必要性を示してからADRにする。

## 7. まだ完了していないこと / 次の具体的調査

- `src/Lumine.Library`全DDL、生成メタデータのreader/parser、UI導線、テストfixtureの横断監査。
- Tsumikiの利用条件・配布物を入手できる場合に実際の操作比較。利用規約を遵守し、コードや資産の流用はしない。
- Tsumiki/他競合の実機検証。公式記事は機能主張の根拠であり、第三者の独立ベンチ結果ではない。
- 各Uシナリオの実測シート・採否基準・既存Issueとの依存を確定する。
- 項目ごとの `implemented/partial/missing/unverified` 判定表を作り、独立した実装Issueへ分解する。
- 全面改修決定の前に、既存v2の設計資産とユーザーデータ互換・移行/ロールバックを評価する。

## 8. 調査と報告のルール

- 1 Issue = 原則1 PR。#816は「競争力再監査」であり、既存ViewerやUIの変更を混ぜない。
- コメントやIssue説明の更新は **PR / CIを待たず** に報告する。
- 所見には「確認日時」「取得元」「証拠レベル」を付ける。断定不可能ならUNKNOWN。
- 既存の製品受け入れを迂回しない。新たなIssueはギャップの確認後に作成する。
- AIなしでも高速で使えるlocal-firstを維持しつつ、AI制作者の作業全体の完成度を問う。

## 9. 画像メタデータ抽出の第1次コード追跡（2026-11-21）

- [ImageExifMetadataProbe](../../src/Lumine.Image/ImageExifMetadataProbe.cs): NetVips `GetFields()` から実際に読んでいるのは camera model、lens、focal length、aperture、shutter speed、ISO、撮影日時、GPS。**ComfyUI workflow JSON / Forge parameters / NovelAI の画像生成情報を抽出するコードは、このクラスには含まれない**（CODE）。
- [LibraryRepository.Exif](../../src/Lumine.Library/LibraryRepository.Exif.cs): `asset_exif_metadata` への保存項目も上記撮影EXIFに限定（CODE）。
- [CreativeArchiveModels](../../src/Lumine.Library/CreativeArchiveModels.cs): `GenerationGroupInfo` / `GenerationGroupCreate` にPrompt、モデル名、Sampler、Scheduler、Steps、CFG、WorkflowJson等の保存フィールドは存在（CODE）。これは画像個別の原ファイルからのメタデータ自動抽出やLoRA・Seed検索を保証しない。
- [LibraryModels](../../src/Lumine.Library/LibraryModels.cs): `AssetQuery` の条件はSearchText、RequiredTags、Rating、Favorite、Status、Color、Folder等で、LoRAやSeedの専用predicateをこの型では公開していない（CODE）。
- `src/Lumine.Image` の現在のソース構成と `src/Lumine.Library` のファイル一覧では、明示的なForge/ComfyUI/NovelAI parser moduleは確認できていない。ただし複数ファイルに埋め込まれた処理や別プロジェクトまで排除できず、**リポジトリ全体としての実装不存在はまだUNKNOWN**。

**暫定的な事実:** Coreが「画像の撮影EXIFを扱える」ことと「画像生成情報を構造的に取り込み、検索できる」ことの間には、確認できる実装上の差分がある。これを修正Issueへ昇格する前に、production/testコード全体で既存機能の有無を確定する。
