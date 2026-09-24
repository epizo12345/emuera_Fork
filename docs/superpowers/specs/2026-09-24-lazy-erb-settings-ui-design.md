# LazyERB設定UIとユーザー設定分離 — 設計

**状態:** 実装完了（未コミット）
**日付:** 2026-09-24

## 目的

`Data/setting.json`をゲーム作者が配布する推奨値、`Data/setting_user.json`をプレイヤーが設定画面で保存する個人上書き値として分離する。設定画面から安全なERBフォルダを選べるようにし、現在のLazyERB判定・危険ファイルのeager fallback・Debug/Analysisのeager動作を保つ。

## 現行構造

- `Program`が`ExeDir`と`ErbDir`を確定した後、`JSONConfig.Load()`がJSON設定を読む。
- `JSONConfig`はgame/user両方の元`JsonObject`を保持し、通常保存ではtop-levelおよびLazyERB object内の未知propertyを残す。
- LazyERB設定は`JSONGameConfigData.LazyErb`をゲーム推奨値として読み、user blockが存在する場合はそのblock全体を実効設定にする。`LazyErbPolicy`は`EffectiveLazyErb`を参照する。
- `LazyErbPolicy`は相対パスの正規化、ERB配下の境界、ERB root除外を行う。Debug / Analysis、`IgnoreUncalledFunction`等の既存ゲートもここで判定する。対象ファイルの危険構造によるfallbackは`ErbLoader`側にある。
- `ConfigDialog`はWinFormsのタブ画面で、保存と保存して再起動のときに`JSONConfig.Save()`を呼び、キャンセル時はJSON保存しない。
- `Emuera以外(開発補助ツールなど)/ConfigRegressionTests/`で、JSON・path safety・WinForms設定UIを検証する。

## JSONの責務と優先順位

### `setting.json`

- 新規ファイル、または日本語LazyERBキーと旧`LazyErb`キーが両方ない場合だけ、次の無効な空ブロックを作る。

  ```json
  "起動時に読み込まないERBフォルダ": {
      "有効": false,
      "フォルダ": []
  }
  ```

- 既存の日本語ブロックは有効な値と未知propertyを保持する。旧`LazyErb`形式の既存migrationは維持する。
- 現行の日本語キーが`null`、object以外、または既知propertyの型が不正な場合は設定エラーにする。旧`LazyErb`キーの`null`だけは変更前のmigration互換を維持し、欠落時と同じ既定値として扱って旧キーを削除する。日本語ブロックが既にあればそれを優先し、日本語ブロックも旧キーもない場合とは区別して旧migration既定値を使う。旧キーが非nullのobject以外、またはobject内の既知propertyの型が不正な場合は設定エラーにし、読み込み前のJSONファイルを書き換えない。object内の既知propertyが明示的に`null`でも不正値として扱う。
- 現行migrationが扱う「object内の既知property欠落」は、既存の既定値補完を維持する。新規ブロックの無効・空の既定値とは区別する。
- ゲーム設定の元`JsonObject`を基に既知値だけを更新する。通常保存ではtop-levelとLazyERB object内の未知propertyを残す。

### `setting_user.json`

- LazyERBキーの不在は「ゲーム推奨設定を使用する」を意味する。新規生成と通常保存ではこのキーを追加しない。
- キーがobjectとして存在すれば、そのobjectのみを実効設定に使う。`有効=false`も明示的な上書きであり、ゲーム値とのマージはしない。
- キーが存在して値が`null`、object以外、または既知propertyの型が不正なら、ゲーム側へ黙ってfallbackせず設定エラーにする。元ファイルは維持する。既知propertyの省略は型不正とは区別し、共通の既定値を補う。
- user JSONの元`JsonObject`を保持し、通常保存ではDTO既知propertyだけ更新する。未知top-level propertyとuser LazyERB object内の未知propertyを残す。
- 「ゲームの推奨設定に戻す」はuser LazyERBキーをobjectごと削除する。object内の未知propertyも同時に削除する。

## 実効設定と適用時期

`JSONConfig`が`EffectiveLazyErb`を公開し、user objectがあるときはuser値、ないときはgame値を返す。`LazyErbPolicy`はこの値だけを読み、既存の有効化条件を維持する。設定変更は次回起動から適用し、稼働中のloaderやERB graphを再構成しない。

## 設定画面

既存`ConfigDialog`へ「起動」タブを加え、一般向け表示では日本語の説明を使う。

- 「ゲームの推奨設定を使用する」「このPC用の設定を使用する」を選べる。
- 推奨設定選択中はgame側実効値を表示し、保存確定までuser JSONへ書かない。
- PC用設定へ切り替えると、現在の実効値を編集用の初期値にする。必要なときまで一部のゲーム処理を読み込まない設定、対象数、対象選択ボタンを表示する。
- 説明には起動時メモリが減る場合があること、対象処理は初回利用時に読み込まれること、変更は再起動後に反映されることを書く。
- 画面内の編集値は保存・保存して再起動で確定し、キャンセルでは変更しない。
- 既存のFormLocalizationリソースを使い、英語・日本語・簡体字の既存構成に追加する。

## 対象フォルダダイアログとpath safety

- 別のWinFormsダイアログに、現在の`Program.ErbDir`以下のディレクトリだけをチェック付きTreeViewで表示する。ファイルは表示しない。ERB rootは表示上の親ノードで選択不可。
- 選択結果は`ERB/...`のDataDir相対表記とし、複数選択を許可する。選択中の親が含む子パスは保存前に除く。
- 親をチェックすると全子孫をチェック表示し、親自身を解除すると全子孫も解除する。親ONから子だけを解除した場合は親だけを解除し、他の兄弟の状態を維持する。子を個別にすべてチェックしても親は自動チェックしない。
- 親がチェック中なら保存対象は親パスのみとし、親が未チェックなら選択された子パスだけを保存する。
- checkbox上のnative `WM_LBUTTONDBLCLK`だけを抑止する。フォルダ名上のdouble-click通知はTreeViewへ渡し、展開・折りたたみを維持する。
- ダイアログの列挙と保存直前の検証でreparse pointを拒否する。Lazy policyもERB root自身を含め、rootから対象までの既存パス要素にreparse pointがある対象を拒否し、手編集したJSONからのsymlink/junction経由のERB外指定を通さない。
- 既存policyの絶対パス、ERB root、ERB外、`..`、個別ERBファイルの禁止を保つ。既存の危険ERB fallbackとDebug / Analysis eagerは変更しない。
- キャンセルは編集中の対象を変更せず、OK時だけ選択内容を設定画面へ返す。

## 検証

依存パッケージのない小さなWindows console regression harnessを追加し、実際の設定JSON処理とpath safety helperを一時ディレクトリ上で検証する。最低限、欠落時の無効・空ブロックが実際のsetting.jsonへ保存され未知propertyも残ること、正常な既存blockが不要なmigrationなしに書き換わらないこと、既存game値、user不在/true上書き/false上書き/リセット、game/user双方の未知property、旧形式migration、不正値での無変更、通常pathと拒否path、reparse point拒否を確認する。Windows権限によりreparse fixtureを作れない場合は`SKIP/UNAVAILABLE`と明記するが、製品コードの拒否判定は必須とする。UIはビルド後に起動し、複数選択・キャンセル・再起動反映表示を手動確認する。

## 対象外

- LazyERBのERB parse/hydrationアルゴリズム、危険構造のfallback分類、Debug/Analysisの動作変更
- 設定変更時の実行中loader再初期化
- 他の設定やゲームERBの修正
- 新規UI framework、JSON library、テストframeworkの追加
- 配布EXE発行、Git push
