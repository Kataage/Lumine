# 実機のGrid/Listスクロールを確認する5項目。評価データのキーは変更しない。
# CIの成功だけで実画面の滑らかさや使用者の承認が得られたとはみなさない。
function New-ViewerScrollReviewChecks {
    @(
        [ordered]@{ id='gridSmallForward'; title='グリッド：ホイールを1目盛り動かす'; instruction='画像の読み込みが落ち着いたら下に1目盛り動かす。新しく見えた部分に空白が残らないか確認。'; status='pending' },
        [ordered]@{ id='gridBurstReverse'; title='グリッド：下4・上4・下2目盛り'; instruction='下へ4目盛り、上へ4目盛り、再び下へ2目盛り。空白や引っかかりが発生しないか確認。'; status='pending' },
        [ordered]@{ id='listSmallForward'; title='リスト：ホイールを1目盛り動かす'; instruction='通常密度のリスト表示に切り替え、読み込み後に下へ1目盛り。空白行が残らないか確認。'; status='pending' },
        [ordered]@{ id='listBurstReverse'; title='リスト：下4・上4・下2目盛り'; instruction='途中で待たず下へ4目盛り、上へ4目盛り、下へ2目盛り。特に4目盛り目の空白・停止を確認。'; status='pending' },
        [ordered]@{ id='jumpRecovery'; title='遠くへ移動した後の通常スクロール'; instruction='離れた位置に移動して画像表示を待ち、通常スクロールと逆方向のスクロールを実行。表示が自然に回復するか確認。'; status='pending' }
    )
}
