# Lumine 開発指示書（Windows画像管理アプリ / Tauri 2 + React + Rust）

## 0. この文書の役割

この文書は、コーディングAIにそのまま渡して実装を進めさせるための**開発仕様書兼実装指示書**である。  
対象アプリは **Lumine** という名称の**Windows向けローカル画像管理アプリ**であり、以下を最重要視する。

- **UI/UXが見やすく使いやすく、デザイン性が高いこと**
- **大量の画像・大容量フォルダでも固まらないこと**
- **ローカルフォルダを扱うWindowsアプリとして実用的であること**
- **将来的な機能拡張（投稿管理、便利機能、配布対応）を見据えた設計であること**
- **自動テスト、GitHub Actions、ビルドの仕組みを最初から整えること**

本プロジェクトでは、単なるプロトタイプではなく、**継続開発可能な本格的な土台**を作ることを目的とする。

---

## 1. 前提条件

### 1.1 固定方針

- アプリは**Windowsデスクトップアプリ**として作る
- 技術スタックの第一候補を採用する
- 開発は主に**WSL2上のコーディングAI**にやらせることを前提とする
- ただし、**Windowsアプリとしての実行確認・最終ビルドはWindows側で行う前提**で設計する
- 現時点では配布は必須ではないが、**将来的な配布を見据えて構成する**
- データは原則**ローカルファースト**で扱う
- 初期段階では**クラウド同期必須ではない**
- オンライン前提ではなく、**オフラインでも主要機能が使える**こと


### 1.2 アプリ名・命名方針（固定）

本アプリの正式名称は **Lumine** とする。  
実装、設定、ビルド、CI、ドキュメント上でも原則としてこの名称を用いること。

命名の基本方針:

- **アプリ表示名**: `Lumine`
- **プロジェクト名 / リポジトリ名 推奨**: `lumine` または `lumine-app`
- **フロントエンド package 名 推奨**: `lumine`
- **Tauri productName**: `Lumine`
- **実行ファイル名 推奨**: `Lumine.exe`
- **Rust crate 名 推奨**: `lumine`（snake_caseを守る）
- **バンドル識別子（仮）**: `com.example.lumine`
- 将来的に公開や配布を行う場合は、識別子を所有ドメイン等に基づく正式な値へ変更すること
- DBファイル名、ログディレクトリ名、キャッシュディレクトリ名も `lumine` を基準に統一すること

Tauri設定での推奨例:

```json
{
  "productName": "Lumine",
  "identifier": "com.example.lumine"
}
```

Windows上の保存先やアプリ固有ディレクトリ名も、原則として `Lumine` または `lumine` に統一すること。  
コード中で仮称の `image-manager` や `app-name` などを残さないこと。

### 1.3 採用技術スタック（固定）

以下を基本採用とする。

- **Tauri 2**
- **React + TypeScript**
- **Vite**
- **Tailwind CSS**
- **shadcn/ui**
- **TanStack Virtual**
- **Rust**（重い処理・ファイル操作・DB処理・サムネイル生成・インデックス処理）
- **SQLite**（ローカルDB）
- **Vitest + React Testing Library**（フロントエンド単体テスト）
- **Rust test / integration test**（バックエンドテスト）
- **Playwright**（フロントエンドUI/E2Eテスト。必要に応じてTauri WebDriver系スモークテストを追加）
- **GitHub Actions**（CI/CD）

### 1.4 採用理由（設計意図）

- UIはReact系で作ることで、**コーディングAIが最も扱いやすい構成**にする
- 見た目はTailwind + shadcn/uiにより、**洗練されたUIを高速に作る**
- 大量描画はTanStack Virtualで仮想化し、**一覧表示のDOM肥大化を防ぐ**
- 重い処理はRustへ逃がし、**フロントエンドのメインスレッドを詰まらせない**
- ローカルファイル操作・サムネイル生成・メタデータ抽出・フォルダ走査・移動処理などはRust側で担う
- SQLiteにより、起動時は生ファイル走査ではなく**DBから即座に一覧を返せる構成**にする

---

## 2. 絶対に守るべき設計原則

以下は必須ルールであり、実装中も逸脱してはならない。

### 2.1 UIを固めない

- **UIスレッドで重いファイル走査をしないこと**
- **UIスレッドで画像デコードをしないこと**
- **巨大リストを全件描画しないこと**
- **初回表示時に全フォルダを同期走査しないこと**
- フロントエンドは「状態表示」と「ユーザー操作の受付」に集中し、重い処理はRustへ委譲すること

### 2.2 すべてを一気に読まない

- 画像一覧では**原寸画像を読まない**
- 一覧には**サムネイルのみ**を使う
- サムネイルは**必要時に生成・キャッシュ**する
- 検索結果・一覧・メタデータ・投稿一覧は**ページングまたはウィンドウ表示**を前提とする

### 2.3 起動を速くする

- 起動時はまずSQLite上のインデックスから画面を出す
- フォルダの再走査はバックグラウンドで行う
- 初回起動でも「殻のUI → 最近使ったライブラリ → キャッシュ済み一覧」の順で即座に表示する

### 2.4 失敗に強くする

- 大量画像の中に破損ファイル、特殊ファイル、アクセス拒否、長いパス、ロック中ファイルが存在してもアプリ全体は止めない
- 個別ファイルのエラーはジョブログへ落とし、UIでは非致命エラーとして扱う
- 途中キャンセル可能なジョブ設計にする

### 2.5 将来拡張を壊さない

- 投稿管理は**複数の投稿先 / 複数アカウント / 複数画像**に対応できる抽象設計にする
- 画像管理機能と投稿管理機能は密結合にせず、**Asset中心のドメイン設計**にする
- 将来、プラグイン的に新しい投稿先を追加できる構造を意識する

---

## 3. 想定する主要ユースケース

このアプリは以下の使い方を想定する。

1. ユーザーがローカルの画像フォルダを登録する
2. アプリがバックグラウンドでフォルダを走査し、画像一覧を生成する
3. 画像を軽快に一覧・検索・絞り込みできる
4. 画像を選択して詳細を見る
5. 画像にメモ、タグ、評価、状態などの情報を紐づける
6. 画像を別フォルダへ移動する
7. 複数画像をまとめて整理する
8. 画像を投稿候補として管理する
9. 複数の投稿先やアカウントに対して、投稿下書き・予約・済み管理を行う
10. アプリを長時間起動していても重くなりにくい

---

## 4. MVPで必須の機能

以下は**初期バージョンで必須**とする。

### 4.1 ライブラリ管理

- 監視対象フォルダの登録 / 解除
- 複数フォルダ登録
- サブフォルダ再帰走査
- 除外フォルダ設定
- 対応拡張子設定
- 手動再スキャン
- バックグラウンド再スキャン
- フォルダ監視（変更検知）

### 4.2 画像一覧

- グリッド表示
- リスト表示
- サムネイルサイズ変更
- 仮想スクロール
- 複数選択
- キーボード操作
- ソート（更新日 / 作成日 / 名前 / サイズ / 評価 / 投稿状態）
- フィルタ（フォルダ / タグ / 評価 / メモ有無 / 投稿状態 / 拡張子）
- 検索（ファイル名 / メモ / タグ / 任意の簡易全文検索）

### 4.3 画像詳細

- 拡大プレビュー
- 左右移動
- 基本メタデータ表示
  - パス
  - ファイル名
  - 更新日時
  - サイズ
  - 画像サイズ（width/height）
  - 拡張子
  - 任意でEXIFの一部
- メモ閲覧/編集
- タグ設定
- 評価設定（星など）
- 状態ラベル（未整理 / 選別済み / 投稿候補 / 投稿済み など）

### 4.4 ファイル操作

- 画像の別フォルダへの移動
- 複数画像の一括移動
- 失敗時のロールバックまたは安全な中断
- 同名ファイル衝突時のポリシー
  - スキップ
  - リネーム
  - 上書き禁止（初期値）
- 操作後にDBインデックスを正しく更新

### 4.5 メモ機能

- 各画像に対するリッチすぎないメモ
- プレーンテキストベースで十分
- メモの更新日時記録
- メモ有無で検索可能

### 4.6 投稿管理

「実際にSNSへ自動投稿」までをMVP必須にはしない。まずは**投稿管理の土台**を作る。

必須要件:

- 投稿先マスタ管理
  - 例: X, Pixiv, Patreon, Fanbox, Misskey, Bluesky, その他カスタム
- 投稿アカウント管理
  - 1投稿先に対して複数アカウント登録可能
- 投稿下書き管理
  - タイトル
  - 本文
  - ハッシュタグ
  - 添付画像複数
  - 対象投稿先
  - 対象アカウント
  - 状態（下書き / 予約 / 投稿済み / 失敗 / 保留）
- 画像と投稿下書きの紐付け
- ある画像がどの投稿に使われたかの逆引き
- 投稿日時の記録
- 予約予定日時の管理
- 投稿済み管理
- 投稿漏れ確認しやすい一覧

### 4.7 便利機能（MVPに入れてよい）

以下は優先度中程度だが、実装コストが高くなければ入れてよい。

- お気に入り
- 色ラベル
- 最近開いた画像
- 最近使った移動先フォルダ
- クイックフィルタ保存
- 一括タグ付け
- 一括評価
- 一括状態更新
- 画像のコピー / パスコピー / フォルダを開く
- 画像使用履歴の簡易表示

---

## 5. 今後拡張しやすいように考慮しておく機能

以下はMVP必須ではないが、**将来追加しやすい設計**にしておくこと。

- 重複画像検出（hashベース）
- 類似画像検出（将来AI埋め込み等）
- 自動タグ候補
- 投稿テンプレート
- 予約投稿実行
- 外部サービスAPI連携
- フォルダルールに基づく自動移動
- 一括リネーム
- 比較ビュー
- フルスクリーン閲覧
- ショートカットカスタマイズ
- 設定のエクスポート/インポート
- 将来的な自動更新対応

---

## 6. UI/UX 要件

### 6.1 全体方針

UIは「高機能だが難解」ではなく、**視認性・操作の明快さ・情報整理のしやすさ**を優先する。

方向性:

- ミニマルで整ったデザイン
- ダークモードを基本にしつつライトにも対応可能な構成
- 余白・階層・タイポグラフィを明確にする
- 情報密度を上げすぎない
- クリックしやすく、キーボードでも効率よく使える

### 6.2 レイアウト案

基本レイアウトは以下を想定する。

- **左サイドバー**
  - ライブラリ
  - 保存フィルタ
  - タグ
  - 投稿管理
  - 設定
- **上部ツールバー**
  - 検索
  - フィルタ
  - ソート
  - 表示切替
  - サムネイルサイズ
  - 一括操作
- **中央メイン領域**
  - 画像グリッド / リスト
- **右詳細パネル**（任意で開閉）
  - メタ情報
  - メモ
  - タグ
  - 投稿状況

### 6.3 UX要件

- 一覧操作は軽く、スクロール中に引っかかりを感じにくいこと
- サムネイル未生成時はプレースホルダ / skeleton を出すこと
- 長時間処理は進捗表示すること
- 走査中・移動中・投稿更新中など、状態が常にわかること
- 破壊的操作には確認ダイアログを出すこと
- 直前操作を可能な範囲でUndoできる余地を残すこと
- エラーは技術者向けスタックトレースではなく、ユーザーが理解できる表現で出すこと

### 6.4 必須UIコンポーネント

- Command palette / quick action
- Dialog / sheet / drawer
- Dropdown menu / context menu
- Tabs
- Resizable panels
- Toast / inline alert
- Table / list / virtual grid
- Breadcrumb or folder path navigator
- Status badge
- Skeleton loader

### 6.5 アクセシビリティ

- キーボード操作可能
- フォーカスリングを消さない
- ラベルを適切に付与
- コントラスト不足を避ける
- ショートカットはツールチップや設定に表示する

---

## 7. 非機能要件（性能・安定性）

以下は必須の性能目標である。厳密なベンチマーク値は環境差があるため目安とするが、実装はこれを目指すこと。

### 7.1 起動性能

- アプリシェル表示: できるだけ**2秒以内目標**
- 起動時に全フォルダをフルスキャンしない
- 起動直後でも前回DBキャッシュを元に一覧が見られること

### 7.2 一覧性能

- 数万件規模のAssetでも一覧画面が固まらないこと
- DOM全件描画禁止
- 表示中の要素だけ描画すること
- 画像スクロール中の再レンダリング回数を抑えること
- 1セルごとに重い処理をしないこと

### 7.3 サムネイル性能

- サムネイルはディスクキャッシュを前提とする
- 同じサムネイルを毎回再生成しないこと
- 表示サイズに応じた複数サイズ戦略を許容する
- 原寸画像からの毎回縮小を避けること

### 7.4 バックグラウンド処理

- 走査・サムネイル生成・メタデータ取得・ハッシュ計算はジョブキュー化
- 並列度は固定値ではなく適切に制御すること
- CPU/IOを食い潰してUIを悪化させないこと
- キャンセル可能であること

### 7.5 メモリ使用

- 原寸画像を一覧用に保持しない
- 見えていない範囲の大きい画像データは解放する
- 使い終わったバッファを保持し続けない
- 大量スクロール後にメモリリーク的な挙動がないこと

### 7.6 安定性

- 破損画像・巨大画像・非対応画像が混在しても落ちにくいこと
- ファイル移動中のエラー時に中途半端なDB状態にならないこと
- DBトランザクションとファイル操作結果を整合させること

---

## 8. アーキテクチャ方針

### 8.1 全体像

採用するアーキテクチャは以下。

- **Frontend (React)**
  - 画面描画
  - ユーザー操作
  - 画面状態
  - 軽量なバリデーション
- **Application Core (Rust/Tauri)**
  - ファイル走査
  - ファイル移動
  - サムネイル生成
  - メタデータ抽出
  - SQLiteアクセス
  - バックグラウンドジョブ管理
  - 設定永続化

### 8.2 層分離

Rust側は少なくとも以下の層に分離すること。

- `commands` : Tauri command境界
- `application` : use case / service層
- `domain` : エンティティ・値オブジェクト・ドメインルール
- `infrastructure` : SQLite, file system, thumbnail cache, watcher実装
- `jobs` : background queue, scanner, thumbnail worker

React側は少なくとも以下に分けること。

- `app` : エントリ、router、providers
- `pages` : 画面単位
- `features` : 機能別UIとhooks
- `entities` : Asset / Post / Folder / Tagなどの型とview model
- `shared` : UI部品、utils、hooks、api client

### 8.3 フロントエンドの状態管理

推奨:

- サーバー状態相当（Tauri command経由データ）は **TanStack Query** または同等のquery管理
- UI状態は **Zustand** またはReact state
- 巨大データの全件グローバル保持は禁止
- 一覧は必要範囲だけ取得または描画すること

### 8.4 データ取得原則

- 画像一覧は**ページング/カーソル/範囲取得**を基本とする
- フロントへ巨大配列を毎回IPCしない
- 必要なら`list_assets(query, offset, limit)`のようなAPIにする
- ソート・フィルタはなるべくSQLite側で処理する

---

## 9. 推奨ディレクトリ構成

```text
project-root/
  src/
    app/
      providers/
      router/
    pages/
      library/
      posts/
      settings/
    features/
      asset-grid/
      asset-detail/
      search-filter/
      move-assets/
      notes/
      tags/
      posts/
      libraries/
    entities/
      asset/
      post/
      folder/
      tag/
    shared/
      api/
      components/
      hooks/
      lib/
      styles/
      types/
  src-tauri/
    src/
      commands/
      application/
      domain/
      infrastructure/
      jobs/
      db/
      tests/
    migrations/
    capabilities/
    icons/
  tests/
    e2e/
    fixtures/
  .github/
    workflows/
  docs/
```

---

## 10. データモデル（初期案）

SQLiteスキーマは最低でも以下を持つこと。

### 10.1 libraries

- `id`
- `name`
- `root_path`
- `is_enabled`
- `created_at`
- `updated_at`
- `last_scanned_at`

### 10.2 folders

- `id`
- `library_id`
- `path`
- `parent_path`
- `is_excluded`
- `created_at`
- `updated_at`

### 10.3 assets

- `id`
- `library_id`
- `folder_path`
- `file_name`
- `file_path`（unique）
- `extension`
- `file_size`
- `created_at_fs`
- `modified_at_fs`
- `width`
- `height`
- `mime_type`
- `hash_blake3`（任意 / 将来重複判定用）
- `thumb_status`（none / queued / ready / failed）
- `rating`
- `status_label`
- `is_favorite`
- `color_label`
- `indexed_at`
- `updated_at`

### 10.4 asset_notes

- `id`
- `asset_id`
- `content`
- `created_at`
- `updated_at`

### 10.5 tags

- `id`
- `name`
- `color`
- `created_at`

### 10.6 asset_tags

- `asset_id`
- `tag_id`
- unique(asset_id, tag_id)

### 10.7 post_targets

- `id`
- `name`
- `kind`（x / pixiv / patreon / fanbox / custom 等）
- `created_at`

### 10.8 post_accounts

- `id`
- `post_target_id`
- `display_name`
- `account_identifier`
- `is_active`
- `created_at`
- `updated_at`

### 10.9 posts

- `id`
- `title`
- `body`
- `hashtags`
- `status`（draft / scheduled / published / failed / on_hold）
- `scheduled_at`
- `published_at`
- `created_at`
- `updated_at`

### 10.10 post_destinations

- `id`
- `post_id`
- `post_target_id`
- `post_account_id`
- `status`
- `scheduled_at`
- `published_at`
- `external_post_id`（将来連携用）

### 10.11 post_assets

- `post_id`
- `asset_id`
- `sort_order`
- unique(post_id, asset_id)

### 10.12 job_logs

- `id`
- `job_type`
- `status`
- `message`
- `payload_json`
- `started_at`
- `finished_at`

### 10.13 app_settings

- `key`
- `value_json`
- `updated_at`

### 10.14 必須インデックス

最低でも以下を張ること。

- `assets(file_path)` unique
- `assets(file_name)`
- `assets(folder_path)`
- `assets(modified_at_fs)`
- `assets(status_label)`
- `assets(rating)`
- `assets(is_favorite)`
- `asset_notes(asset_id)`
- `tags(name)` unique
- `post_assets(asset_id)`
- `post_destinations(post_account_id, status)`

必要に応じてFTS5導入を検討してよい。

---

## 11. パフォーマンス設計の詳細

### 11.1 起動時処理

起動直後にやること:

1. 設定読み込み
2. DB接続
3. 前回状態の復元
4. 直近ライブラリの一覧表示
5. バックグラウンドで差分スキャン開始

**禁止:** 起動前にすべてのフォルダを生走査してからUIを出すこと。

### 11.2 一覧描画

- TanStack Virtualを用いて仮想化する
- グリッド表示ではセルサイズの推定を安定させる
- セルコンポーネントはメモ化する
- スクロール中に重い状態更新をしない
- 一覧セルで毎回日付整形や重い派生計算をしない

### 11.3 サムネイル生成

- Rust側でサムネイルを生成し、アプリ管理下のキャッシュディレクトリへ保存
- キーは`asset_id + modified_at_fs + desired_size`などで一意化する
- 元ファイルが更新されたらキャッシュ無効化
- 一覧はキャッシュ済みなら即表示、未生成ならプレースホルダ
- 表示領域近傍の先読みはしてよいが、過剰な先読みは避ける

### 11.4 フォルダ走査

- 初回は再帰走査
- 以後は`path + size + modified time`ベースの差分更新を優先
- `notify`等による変更監視を導入してよい
- 巨大フォルダでも一定件数ごとに進捗を報告
- キャンセル・再開に配慮

### 11.5 ファイル移動

- 同一ボリュームならrename優先
- 異なるボリュームならcopy + verify + delete
- DB更新はファイル操作成功後に整合的に反映
- 一括移動はトランザクション単位と失敗時扱いを明示
- 移動結果のサマリをUIに返す

### 11.6 検索

- フロントエンドの配列フィルタではなく、基本はSQLiteで検索
- メモ・タグ・投稿状態などの組み合わせ検索をサポート
- 大量件数でも全件返さずlimit/offsetまたはcursorで返す

---

## 12. Rust側の実装要件

### 12.1 推奨クレート

必要に応じて以下を検討すること。

- `rusqlite` または `sqlx`（SQLite）
- `walkdir`（再帰走査）
- `notify`（ファイル監視）
- `image`（画像読み込み/縮小）
- `kamadak-exif`（EXIF読み取り）
- `blake3`（ハッシュ）
- `rayon` または適切な非同期/並列実装
- `serde`, `serde_json`
- `anyhow`, `thiserror`
- `tracing`, `tracing-subscriber`
- `trash`（ごみ箱対応を将来考えるなら）

### 12.2 コマンド設計例

最低限、以下に相当するcommand APIを用意すること。

- `get_app_bootstrap()`
- `list_libraries()`
- `add_library(root_path)`
- `remove_library(library_id)`
- `scan_library(library_id)`
- `cancel_job(job_id)`
- `list_assets(query, offset, limit)`
- `get_asset_detail(asset_id)`
- `update_asset_note(asset_id, content)`
- `set_asset_tags(asset_id, tag_ids)`
- `bulk_update_assets(...)`
- `move_assets(asset_ids, destination_folder, conflict_policy)`
- `list_posts(query, offset, limit)`
- `create_post_draft(payload)`
- `update_post(payload)`
- `attach_assets_to_post(post_id, asset_ids)`
- `list_post_targets()`
- `list_post_accounts()`
- `create_post_target()`
- `create_post_account()`
- `get_job_status(job_id)`

### 12.3 イベント設計

Tauri eventまたは同等手段で以下をUIへ通知してよい。

- scan progress
- thumbnail generation progress
- move progress
- job completed
- job failed
- file system changed

### 12.4 エラーハンドリング

- Rust内部では詳細なエラーを保持
- フロントへは理解可能なメッセージへ変換
- ログには技術的詳細を残す
- 既知エラーは分類する
  - permission denied
  - path too long
  - unsupported image
  - file disappeared
  - destination already exists
  - database locked

---

## 13. React側の実装要件

### 13.1 UIライブラリ

- shadcn/uiベースで統一感あるUIを作る
- コンポーネントの見た目を場当たりで乱立させない
- 独自デザインシステムの入口を作る
  - spacing
  - radius
  - typography
  - color tokens
  - elevation

### 13.2 表示設計

- 画像グリッドは`AssetGrid`コンポーネントとして独立
- 右詳細パネルは再利用可能な`AssetDetailPanel`
- フィルタはURLクエリまたは単一状態オブジェクトに集約
- 大量アイテム時に不要なprops drillingを避ける

### 13.3 禁止事項

- 一覧全件を一度にstateに展開して重くすること
- base64の巨大画像を状態に持つこと
- 画面遷移ごとに不要な再フェッチを乱発すること
- レンダー内で重い整形処理を毎回すること

### 13.4 UXの細部

- 選択状態がわかりやすいこと
- 複数選択時の一括操作バーを表示すること
- 詳細パネルは開閉状態を記憶してよい
- キーボードショートカット例:
  - `←` `→` 次/前画像
  - `Delete` 削除または保留アクション
  - `M` メモ編集
  - `T` タグ編集
  - `P` 投稿下書き作成
  - `Ctrl/Cmd + F` 検索フォーカス

---

## 14. セキュリティとTauri設定方針

- Tauriの権限は**最小権限**で構成する
- `@tauri-apps/plugin-fs`を使う場合も、アプリが触れてよい範囲を安易に広げない
- ダイアログ経由で取得したパスのみ扱う設計を基本とする
- 投稿先連携を将来実装する場合も、秘密情報は安全に保存できるよう抽象化しておく
- ローカルDBとキャッシュの保存場所はWindowsユーザープロファイル配下の適切なアプリ領域を利用する

---

## 15. テスト戦略（必須）

**テストは必須。テストを書かない実装は禁止。**  
特に「固まらないこと」「ファイル操作で壊れないこと」「一覧・検索・投稿管理が破綻しないこと」を担保する。

### 15.1 テストの層

#### A. Rust unit test

対象:

- パス処理
- クエリ生成
- 状態遷移
- conflict policy
- post domain model
- asset filtering logic
- thumbnail cache key generation

#### B. Rust integration test

対象:

- SQLite migration
- repository CRUD
- ライブラリ走査
- 差分更新
- ファイル移動
- 失敗時整合性
- 投稿下書きと画像紐付け

`tempfile`やfixture directoryを使い、**テストごとに独立した一時環境**を使うこと。

#### C. Frontend unit/component test

対象:

- 検索UI
- フィルタUI
- 詳細パネル
- メモ編集UI
- 投稿フォーム
- 一括操作バー
- エラートースト

Vitest + React Testing Libraryで行うこと。

#### D. Frontend integration test

対象:

- 一覧表示 → 詳細 → メモ更新 → 反映
- 複数選択 → 一括操作バー表示
- 投稿下書き作成 → 画像紐付け → 一覧反映

#### E. E2E / smoke test

最低限:

- アプリ起動
- フォルダ追加
- 一覧表示
- 画像選択
- メモ保存
- 投稿下書き作成
- 一括移動実行の成功ケース

初期段階ではPlaywright中心でもよい。  
将来的にはTauriのWebDriverベースのdesktop E2Eも追加し、**実際のTauri実行環境でのスモークテスト**を行う。

### 15.2 テストデータ

`tests/fixtures` に以下を用意すること。

- small_images/
- mixed_formats/
- broken_files/
- huge_dimension_images/
- nested_directories/
- duplicate_names/
- move_conflict_cases/

### 15.3 テストで必ず確認すること

- 破損画像があっても一覧生成が継続する
- 画像が数百件〜数千件あるfixtureでもUIロジックが破綻しない
- 移動後にDBパスが更新される
- 同名衝突時ポリシーが正しく動作する
- 投稿下書きに複数画像を紐づけられる
- 1画像が複数投稿に使える
- ライブラリ再スキャン時に重複登録しない

### 15.4 パフォーマンステスト（軽量版でよい）

最低限の簡易性能チェックを作ること。

- `list_assets` が一定件数で極端に遅くならない
- サムネイルキャッシュヒット時の取得が十分高速
- 大量データでシリアライズ過多にならない

厳密なベンチでも簡易ベンチでもよいが、**性能退行を検知できる仕組み**を入れること。

---

## 16. GitHub Actions 要件（必須）

GitHub Actionsを最初から整備すること。  
最低限、以下のworkflowを作る。

### 16.1 `ci.yml`

トリガー:

- push
- pull_request

実行内容:

- frontend dependencies install
- rust toolchain setup
- node setup
- cache restore
- `npm run lint`
- `npm run typecheck`
- `npm run test`
- `cargo fmt --check`
- `cargo clippy --all-targets --all-features -- -D warnings`
- `cargo test --all`
- フロントエンドbuild確認

### 16.2 `e2e.yml`

トリガー:

- pull_request
- workflow_dispatch

実行内容:

- Playwrightセットアップ
- フロントエンドE2E
- 必要に応じてfixtureを使った統合シナリオ確認

### 16.3 `build-windows.yml`

トリガー:

- workflow_dispatch
- push tag
- release branch push（任意）

実行内容:

- Windows runnerでTauriアプリをビルド
- artifactをアップロード
- 将来的に署名やreleaseを追加可能な構成

### 16.4 将来の`release.yml`

- 公式の`tauri-action`利用を前提に設計してよい
- tag pushでWindowsバイナリ作成
- 将来的に更新ファイル配布に対応できる形にする

---

## 17. GitHub Actions 実装サンプル方針

以下はあくまで指針。実際のバージョン番号はその時点の安定版に合わせて更新すること。

### 17.1 package scripts の想定

```json
{
  "scripts": {
    "dev": "vite",
    "build": "tsc -b && vite build",
    "preview": "vite preview",
    "lint": "eslint .",
    "typecheck": "tsc --noEmit",
    "test": "vitest run",
    "test:watch": "vitest",
    "e2e": "playwright test",
    "tauri:dev": "tauri dev",
    "tauri:build": "tauri build"
  }
}
```

### 17.2 `ci.yml` のイメージ

```yaml
name: ci

on:
  push:
  pull_request:

jobs:
  quality:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-node@v4
        with:
          node-version: 22
          cache: npm

      - name: Install frontend deps
        run: npm ci

      - name: Setup Rust
        uses: dtolnay/rust-toolchain@stable

      - name: Rust cache
        uses: Swatinem/rust-cache@v2

      - name: Lint
        run: npm run lint

      - name: Typecheck
        run: npm run typecheck

      - name: Frontend tests
        run: npm run test

      - name: Rust fmt
        run: cargo fmt --manifest-path src-tauri/Cargo.toml --all -- --check

      - name: Rust clippy
        run: cargo clippy --manifest-path src-tauri/Cargo.toml --all-targets --all-features -- -D warnings

      - name: Rust tests
        run: cargo test --manifest-path src-tauri/Cargo.toml --all

      - name: Frontend build
        run: npm run build
```

### 17.3 `e2e.yml` のイメージ

```yaml
name: e2e

on:
  pull_request:
  workflow_dispatch:

jobs:
  e2e:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-node@v4
        with:
          node-version: 22
          cache: npm

      - name: Install deps
        run: npm ci

      - name: Install Playwright
        run: npx playwright install --with-deps

      - name: Run e2e
        run: npm run e2e
```

### 17.4 `build-windows.yml` のイメージ

```yaml
name: build-windows

on:
  workflow_dispatch:
  push:
    tags:
      - 'v*'

jobs:
  build:
    runs-on: windows-latest
    permissions:
      contents: write

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-node@v4
        with:
          node-version: 22
          cache: npm

      - name: Install frontend deps
        run: npm ci

      - uses: dtolnay/rust-toolchain@stable

      - name: Rust cache
        uses: Swatinem/rust-cache@v2

      - name: Build Tauri app
        uses: tauri-apps/tauri-action@v0
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        with:
          projectPath: .

      - name: Upload bundle artifacts
        uses: actions/upload-artifact@v4
        with:
          name: app-windows-bundles
          path: src-tauri/target/release/bundle/**
```

---

## 18. コーディング規約

### 18.1 共通

- 型を丁寧に付ける
- 命名を雑にしない
- 1ファイル1責務を意識する
- コメントは「何をしているか」より「なぜそうしているか」を優先する
- magic numberを避ける
- 一時しのぎの実装を放置しない

### 18.2 React

- コンポーネントの責務を分ける
- propsが肥大化する場合はview modelやhookへ寄せる
- memo化と依存配列を雑に扱わない
- 見た目のためだけの複雑なstateは増やしすぎない

### 18.3 Rust

- エラー型を整理する
- 例外的ケースを無視しない
- IO, DB, domain logic を分離する
- `unwrap()`乱用禁止（テスト以外）

---

## 19. ログとデバッグ

- Rust側は`tracing`でログ出力
- 開発時はdebugログを見やすく
- ユーザー向けにはログファイル保存場所を用意してよい
- 長時間ジョブにはjob idを付与
- 失敗理由を追跡できるようにする

---

## 20. 実装フェーズ

### Phase 1: 土台構築

- Tauri + React + TS + Tailwind + shadcn/ui セットアップ
- SQLite初期化
- migrations
- 基本レイアウト
- GitHub Actions
- テスト基盤

### Phase 2: ライブラリ / 一覧 / 詳細

- フォルダ登録
- 走査
- DB登録
- 一覧表示
- 詳細表示
- メモ / タグ / 評価 / 状態

### Phase 3: ファイル移動

- 単体移動
- 一括移動
- 衝突処理
- ジョブ進捗
- テスト強化

### Phase 4: 投稿管理

- 投稿先・アカウント管理
- 投稿下書き
- 複数画像紐付け
- 投稿一覧 / 状態管理
- 画像から投稿逆引き

### Phase 5: 便利機能と磨き込み

- クイックフィルタ保存
- 最近使った移動先
- 一括編集改善
- UX改善
- 性能改善

---

## 21. 受け入れ基準（Acceptance Criteria）

以下を満たした時点で、初期バージョンとして合格とする。

1. Windowsで起動し、主要画面が崩れず表示される
2. 複数フォルダを登録できる
3. バックグラウンドで画像を走査し、一覧が出る
4. 数千件規模のfixtureでもUIが致命的に固まらない
5. 仮想スクロールが効いている
6. 画像詳細を開き、メモを保存できる
7. 複数画像を選択して別フォルダへ移動できる
8. 移動後に一覧とDBが整合する
9. 投稿下書きを作成し、複数画像を紐づけられる
10. 1画像が複数投稿に紐づけ可能
11. 単体テスト / 統合テスト / E2Eが最低限存在する
12. GitHub Actionsでlint / test / buildが回る
13. エラー時にアプリ全体が落ちにくい

---

## 22. AIへの最終指示

以下を厳守して実装すること。

- まず**土台 → テスト基盤 → CI → DB → 画面骨組み**の順に作ること
- いきなり全機能を詰め込まず、段階的にコミットしやすい構造で作ること
- すべての機能は**テストを書ける設計**にすること
- UIが綺麗でも固まる設計は不合格
- 速くても使いにくいUIは不合格
- 投稿管理は単なる文字列管理ではなく、将来的な複数投稿先対応を見据えた設計にすること
- ファイル移動は特に壊れやすいため、**安全性を最優先**すること
- 技術的負債を増やす実装は避けること
- 実装後はREADME / セットアップ手順 / テスト実行手順 / CI説明も整備すること

---

## 23. 追加の実装メモ

- フロントの一覧表示は`AssetGrid`と`AssetList`を切り替え可能にする
- 右詳細パネルは選択中の複数画像にも対応できる余地を残す
- 投稿作成画面では、画像選択 → 投稿文作成 → 対象投稿先選択 の流れを簡潔にする
- 最近使ったタグ、最近使った移動先などはUX向上に効果が大きい
- 設定画面は最低限でも以下を持つ
  - テーマ
  - サムネイルサイズ既定値
  - 走査対象拡張子
  - キャッシュ保存先 / サイズ上限
  - 同名衝突時初期ポリシー
  - ログ出力レベル

---

## 24. 実装開始時に最初に作るべきもの（優先順）

1. repository初期化
2. Tauri + React + TS + Tailwind + shadcn/ui セットアップ
3. SQLite migration基盤
4. `libraries`, `assets`, `asset_notes`, `posts` 周辺の最小スキーマ
5. GitHub Actions `ci.yml`
6. Rust / Frontend テスト基盤
7. ライブラリ追加UI
8. 走査 command
9. 一覧画面（仮想化）
10. 詳細パネル
11. メモ機能
12. ファイル移動機能
13. 投稿下書き機能

---

## 25. 完成物に求める品質

このアプリは、単なる技術検証ではなく、**日常的に使いたくなる品質**を目指すこと。  
以下のバランスが取れていることが重要である。

- 見た目がよい
- 使いやすい
- 速い
- 壊れにくい
- 拡張しやすい
- テストしやすい
- 継続開発しやすい

この条件を満たすよう、設計・実装・テスト・CI・ビルドを一貫して整備すること。


---

## 26. Lumine 固有の設定値と初期化ルール

実装開始時点で、最低限以下を揃えること。

### 26.1 初期値

- アプリ表示名: `Lumine`
- リポジトリ名: `lumine` または `lumine-app`
- Rust crate: `lumine`
- package name: `lumine`
- Tauri productName: `Lumine`
- Tauri identifier: `com.example.lumine`（後で正式値へ変更）
- 設定ファイルやDB名に使う短縮名: `lumine`

### 26.2 推奨ファイル名・識別子

- SQLite DB: `lumine.db`
- ログディレクトリ: `lumine/logs`
- サムネイルキャッシュ: `lumine/thumb-cache`
- 一時作業領域: `lumine/tmp`

### 26.3 README や package metadata に必ず含めること

- アプリ名が **Lumine** であること
- 何をするアプリか
- Windows 向けローカル画像管理アプリであること
- 主な機能:
  - 画像一覧・検索
  - フォルダ移動
  - メモ
  - 投稿管理
- セットアップ方法
- テスト方法
- GitHub Actions の説明

### 26.4 AIへの命名上の注意

- UI文言、ウィンドウタイトル、通知文言、README、Actions artifact名に仮称を残さないこと
- `Lumine` と `lumine` の使い分けを明確にすること
  - ユーザー向け表示: `Lumine`
  - ファイル名・識別子・crate・package: `lumine`
- 既存テンプレート由来の名称が混入していないか最初に確認すること
