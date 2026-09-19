# Lumine

Lumine は、大量のローカル画像を軽快に閲覧・整理するための Windows 向け画像ライブラリアプリです。

## 現在の基本方針

- **画像を見ることを最優先**にした日本語 UI
- 大量画像でも画面に必要な範囲だけを読み込む仮想スクロール
- グリッド／リスト／全画面表示
- 検索、フォルダー、状態、評価による絞り込み
- 評価、お気に入り、カラーラベル、タグ、メモによる整理
- EXIF 情報は必要になった時だけ遅延読み込み
- SQLite + WAL によるローカル完結のインデックス

### サムネイル画像をディスクへ保存しません

Lumine は、表示専用のサムネイル画像や縮小コピーを独自ファイルとして生成・保存しません。
一覧・詳細プレビューは、元画像から必要な表示サイズだけを**メモリ上**でデコードし、上限付きのメモリキャッシュで管理します。

そのため、画像コレクションとは別に巨大なサムネイルキャッシュが増え続ける設計にはしていません。

## 主な操作

- **画像フォルダーを追加**: 左の「ライブラリ」から追加します。登録後、自動で画像を一覧化します。
- **画像を選択**: クリック。`Ctrl` で複数選択、`Shift` で範囲選択できます。
- **全画面表示**: 画像をダブルクリックします。
- **全画面で前後移動**: `←` / `→`
- **全画面を閉じる**: `Esc`
- **拡大・縮小**: 全画面表示中にマウスホイール。拡大時はドラッグで移動できます。
- **検索・絞り込み**: 画面上部から操作します。適用中の条件はチップで表示され、個別または一括で解除できます。

## 開発

バックエンドは Go + Wails v2、フロントエンドは React + TypeScript です。

### フロントエンド

```bash
cd frontend
npm ci
npm run build
```

### Go

```bash
go vet ./...
go test -race ./...
go build ./...
```

### Windows アプリ

Installed build:

```bash
wails build -platform windows/amd64
```

Portable build:

```bash
wails build -platform windows/amd64 -tags portable -ldflags "-H windowsgui" -o Lumine-portable.exe
```

Installed版の新規データはWindowsのLocalAppData配下の `Lumine` に保存します。旧 `%USERPROFILE%\\lumine` に既存DBがある場合は、データ消失を避けるため明示的に移行するまで旧保存先を継続利用します。

Portable版はexe配置フォルダー配下に `data/lumine.db`、`data/logs`、`data/semantic-index`、`data/webview2`、`models`、`runtimes` を保存します。旧データを検出した場合はAI設定のストレージ欄から「次回起動時にコピー」を予約できます。コピー元は自動削除しません。

GitHub Actions ではフロントエンドの lint / typecheck / unit test / build、Go の vet / build / race test、および Windows の Wails 通常版・portable 版ビルドを検証します。
