# Lumine v2 — #634 Windows実機スクロール検証

対象: [Issue #634](https://github.com/Kataage/Lumine/issues/634)。**CIの合格だけではIssueを閉じない。**

## できること

製品版 `Lumine.App.exe` を通常起動し、実際の画像ライブラリでGridとListの小スクロール・連続ホイール・方向転換・遠方ジャンプ後の復帰を確認する。終了後、5項目の **y / n / s（未確認）** 回答を受け付け、`viewer-scroll-owner-report.json` と `VIEWER-SCROLL-CHECKLIST.md` を保存する。未実行の準備モードでは**すべてpending**、失敗項目はfail、未回答はpendingとなる。GPU名称は参考情報であり、物理GPU presentを計測したことにはならない。**自動的にIssueを完了扱いにしない。**

## 実行方法（Windows x64）

[greenfield-core Windows CI](https://github.com/Kataage/Lumine/actions/workflows/v2-foundation.yml) の**成功したdevelop実行**から `lumine-product-acceptance-win-x64-<run-id>` を展開する。展開先でPowerShellを開き、以下を実行する。

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\build\Run-ViewerScrollOwnerReview.ps1
```

既に使用しているLumine実行ファイルを指定する場合:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\build\Run-ViewerScrollOwnerReview.ps1 -Exe "D:\Lumine\Lumine.App.exe" -OutputDirectory "D:\Lumine-review"
```

通常、Lumineは同梱のportableデータ領域を使用する。既存の**別の**データ保存先を使用したい場合のみ、明示的に `-DataDirectory "D:\path\to\data"` を指定する。レビュー用スクリプトはユーザーの画像ライブラリを自動で追加・変更しない。初回利用ならアプリの画面で代表的な実画像ライブラリを登録すること。デフォルトは`MemoryOnly`。

### 確認ポイント

1. Grid: 初期表示を落ち着かせ、通常のホイール1回。画像未表示の帯が現れないか。
2. Grid: 下4回→上4回→下2回を連続操作。空白・停止・異常な描画遅延がないか。
3. List: 標準密度でホイール1回。未表示行がないか。
4. List: 下4回→上4回→下2回を連続操作。特に前進4回目の境界行が空白にならないか。
5. 遠方ジャンプ後: 初めて表示する画像の読み込みは許容し、表示が落ち着いてから小スクロールと方向転換が自然か。

アプリを閉じてから5件の回答を入力する。**合格を宣言するには5件すべての実際の確認が必要。**

## 結果と限界

出力先の `viewer-scroll-owner-report.json` をIssue #634の実機受け入れ資料として使用する。画像・ライブラリのフルパス・ファイル名は保存しない。GPU名やWindowsバージョンは記録するが、リフレッシュレート、実際のGPU合成presentフェンス、GPUフレーム遅延等は計測していない。スクリーンキャプチャは自動取得しない（プライバシー保護）。実機の体感と欠損有無の**人間による観察結果**であり、CIのSkia/合成フレームの証明ではない。

`-PrepareOnly` はCI用の**非実行**モード。pendingのJSON生成をテストするもので、受け入れ合格に使用してはならない。起動失敗、ソフトウェアレンダラーへの明示上書き、未回答がある場合も物理GPU実機合格とは扱わない。

CI成功・PRマージは[PR #659](https://github.com/Kataage/Lumine/pull/659)で確認済み。Issue #634のクローズは、製品オーナーが実機の実画像環境での改善を認めた場合に限定する。
