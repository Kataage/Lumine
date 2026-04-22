# Lumine

Windows向けローカル画像管理アプリケーション

## 概要

Lumineは、Windows上で動作する高性能なローカル画像管理アプリです。大量の画像ファイルを素早く一覧表示・検索・整理ができ、 投稿下書きの管理機能も備えています。

## 主な機能

- **ライブラリ管理**: 複数のフォルダを登録して画像を一括管理
- **高速一覧表示**: 仮想スクロールにより、数万件規模の画像でも軽快に動作
- **強力な検索**: ファイル名、メモ、タグでの検索に対応
- **画像メタデータ**: 評価、メモ、タグ、状態ラベルを管理
- **ファイル整理**: 画像の移動、名前変更、一括操作
- **投稿管理**: 投稿下書きの作成、複数画像紐付け、投稿先管理

## 技術スタック

- **Frontend**: React + TypeScript + Vite + Tailwind CSS + shadcn/ui
- **Backend**: Rust (Tauri 2)
- **Database**: SQLite
- **State Management**: TanStack Query + Zustand
- **Virtualization**: TanStack Virtual

## セットアップ

### 前提条件

- Node.js 22+
- Rust 1.70+
- Windows 10/11

### 開発環境のセットアップ

```bash
# 依存関係のインストール
npm install

# 開発サーバーの起動
npm run tauri dev
```

### ビルド

```bash
# 本番ビルド
npm run tauri build
```

## テスト

```bash
# フロントエンドテスト
npm test

# E2Eテスト
npm run e2e
```

## プロジェクト構造

```
src/
├── app/               # アプリ全体設定、プロバイダー、レイアウト
│   ├── providers/     # React Provider
│   └── layout/        # メインレイアウトコンポーネント
├── components/        # 再利用可能なUIコンポーネント
├── entities/          # 型定義
├── features/          # 機能別のUIとhooks
│   ├── asset-grid/    # 画像グリッド表示
│   ├── asset-detail/  # 画像詳細パネル
│   └── libraries/     # ライブラリ管理
├── pages/             # 画面単位のコンポーネント
└── shared/            # 共有ユーティリティ、hooks、APIクライアント
    ├── api/           # Tauri APIクライアント
    ├── components/    # 共有UIコンポーネント
    ├── hooks/         # 共有hooks
    └── lib/           # ユーティリティ関数

src-tauri/
├── src/
│   ├── commands/      # Tauriコマンド
│   ├── application/   # アプリケーションサービス
│   ├── domain/        # ドメインエンティティ
│   ├── infrastructure/# ファイル走査等のインフラ
│   ├── jobs/          # バックグラウンドジョブ
│   └── db/            # データベース接続・マイグレーション
├── migrations/        # SQLマイグレーションファイル
└── tests/             # Rust統合テスト
```

## ライセンス

MIT