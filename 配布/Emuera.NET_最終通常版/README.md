# Emuera.NET 最終通常版

- 基礎: BugFix_Test `7b7dd3bf240eff4fdfc7094f4175de0e014532b7`
- 対象OS: Windows x64
- 必要ランタイム: .NET 9
- 配布形態: framework-dependent single-file
- `Emuera.exe` SHA-256: `C701F961D07B0A4E6D317B25EE542262F51CC4E87EE516F40B4F7E4699B4F088`

採用機能:

- 起動時の並列ERB解析に起因する不定期な偽警告を抑止
- 起動高速化
- 複数入力マクロの描画集約による高速化
- マウスXButton1/XButton2による前ページ・次ページ選択
  - 日本語の「前のページ」「次のページ」系
  - 調教画面の `1007` / `1009`
  - ショップ画面の `PREV` / `NEXT`

表示されている文字は部分一致で探す。調教画面のように `div` の内側へ入れ子になったHTMLボタンも再帰的に探し、ゲームへ渡す内部入力値が `1007` / `1009` と完全一致すれば選べる。

ゲーム別の候補は `Data/setting_user.json` で変更できる。`|` は「いずれか」の区切りで、JSONは1行でも複数行でもよい。

```json
"MouseXButton1ButtonText": "前のページ|前ページ|1007|PREV",
"MouseXButton2ButtonText": "次のページ|次ページ|後ろのページ|1009|NEXT"
```

実際の設定ファイルで日本語が `\u524D...` のように見える場合も正常である。Shift-JIS誤判定による文字化けを避けるJSON表記で、Emuera内では同じ日本語として扱われる。

ゲームへ導入するときは、既存の `Emuera.exe` を別名で退避してから置き換える。ゲームのERB、ERH、CSV、セーブは変更しない。
