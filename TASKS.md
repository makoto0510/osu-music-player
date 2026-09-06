# osu! music player 開発タスクリスト

最終更新: 2026-09-06(human-debug-list.md 対応後)

## 現在の開発段階

**段階: documents.md の機能構想(基本機能・特殊機能)、基盤整備(フェーズ 0)、human-debug-list.md の指摘(UI 作り直し・終了時の不具合・テーマ・ショートカット・ポップアップ / 全画面・難易度プレビュー)まで実装済み。残りは外部サービスや他 OS での実地確認と改善候補。**

- 対象: Windows / macOS / Linux。BASS ネイティブは 3 OS 分を同梱(実行確認は Windows のみ。macOS / Linux は手順を README に記載)
- 技術: .NET 8(ランタイムは `RollForward=Major` で新しい版も可)/ Avalonia 11.3 / ManagedBass / OsuParsers / Realm 20.1 / LibVLCSharp / ReOsuStoryboardPlayer.Core / ASP.NET Core(Kestrel)
- プロジェクト: Core(ドメイン・ローダー・検索・ヒットサウンド解析)、Audio(BASS 再生・EQ・ヒットサウンド)、App(Avalonia UI)、Server(HTTP API・Web リモート・オーバーレイ)、ServerHost(ヘッドレス実行ファイル)
- テスト: 149 件合格(Core 63 / Audio 18 / App 65 / Server 3)、警告ゼロ(TreatWarningsAsErrors)
- 実データ検証: この PC の stable(約 3,000 フォルダ)と lazer(約 3,950 セット)で読み込み・再生・動画・ヒットサウンド・ストーリーボード・サーバー API を確認済み
- Git: Codex が管理(コミットは Codex 側で実施)

## 実装済み機能(一覧)

| 領域 | 内容 |
| --- | --- |
| 検出・読み込み | stable(Windows)/ lazer(全 OS)の自動検出、手動フォルダ追加、Reload。osu!.db / .osu / client.realm(動的スキーマ)、lazer ハッシュストレージ、Protected セット除外、除外設定(長さ・検索条件) |
| 統合・メタデータ | stable / lazer 重複統合、音声ファイル単位のトラック化、Unicode 優先表示、最頻 BPM、星 / ルールセット / CS・AR・OD・HP / プレビュー地点、MD5 |
| 再生 | 再生・一時停止・シーク・音量、DT / NC / HT / DC、自動送り、前後移動、キュー、シャッフル(重複なし + 履歴)、リピート Off / All / One、ダブルクリック再生、10 バンド EQ とプリセット |
| ライブラリ | ビュー切替(All / Favourites / Recommended / プレイリスト / スマートプレイリスト / ゲーム内コレクション)、検索言語(`artist:` `mapper:` `tag:` `mode:` `bpm:` `stars:` `length:` `genre:` `language:` `-除外` `a\|b` 引用符)、Browse パネル、お気に入り、プレイリスト CRUD と並べ替え、collection.db 書き出し(osu! フォルダー外限定) |
| 状態復元 | 音量、mod、シャッフル、リピート、ソート、最後のトラックと位置、キュー、除外設定、各種設定を `%AppData%\OsuMusicPlayer\settings.json` に保存 |
| ビジュアル | 詳細ペイン(難易度一覧・星の色分け・プレビュー再生・osu! web リンク)、背景画像の上に重ねる背景動画(libVLC、音声同期、mod 速度追従)とストーリーボード(Skia 描画、加算合成、アニメーション、ヒットサウンドトリガー)、Theater モード、別ウィンドウ / 全画面表示(Ctrl+P / F11)、難易度プレビュー(osu! / taiko / catch / mania の簡略オートプレイ描画、Ctrl+Shift+P) |
| UI | サイドバー(ライブラリ / プレイリスト / コレクション / ツール)+ トラック一覧 + 詳細ペイン + プレイヤーバーの構成、Settings のタブ化、テーマプリセット 6 種とアクセント色指定(`Themes/PlayerTheme.cs`、DynamicResource で即時反映)、キーボードショートカット(`Input/ShortcutMap.cs`、Settings → Shortcuts に一覧) |
| 終了処理 | ウィンドウを閉じた瞬間に再生停止と設定保存、サーバー / Discord の非同期停止をワーカーで実行してから終了(UI スレッドを塞がない、DI コンテナは DisposeAsync)、8 秒のウォッチドッグで確実に終了 |
| ヒットサウンド | .osu からのイベント生成(サークル / スライダー各エッジ・ティック / スピナー)、譜面 → stable スキン → lazer スキン → lazer 既定サンプルの解決、デバイス遅延補正、音量 / オフセット設定、NC / DC ピッチ |
| 外部連携 | ミュージックサーバー(JSON API、Range 対応ストリーミング、Web リモート `/`、OBS オーバーレイ `/overlay`)、ヘッドレスホスト `osu-music-server`、Discord Rich Presence(IPC 直接)、osu! API v2 でのジャンル / 言語取得とキャッシュ、おすすめ(再生履歴ベース) |

---

## 残タスク

### A. フェーズ 0: 基盤整備(完了)

- [x] 結合試験ハーネスを `tools/OsuMusicPlayer.IntegrationHarness` に収録(load / audio / hitsounds / realm モード、README に手順)
- [x] macOS(universal)/ Linux x64・arm64 用 BASS ネイティブを `third_party/native` に配置し、`BassNatives.targets` で OS / RID 別にコピー。`third_party/README.md` にハッシュとライセンスを記載
- [x] libVLC: macOS は NuGet `VideoLAN.LibVLC.Mac` を条件付き参照、Linux は apt の libvlc を使用(README に記載)
- [x] 長さ 0 の WIP 譜面を BASS のデコードストリームで実測して補完(`BassAudioDurationProbe`、1 回の読み込みで最大 300 件)
- [x] Avalonia の統計タスク(`AvaloniaStats`、AppData へ書き込む build 時テレメトリ)を `Directory.Build.targets` で無効化
- [x] lazer の Realm スキーマ確認手順をハーネスの `realm` モードとして収録
- [ ] macOS / Linux 実機での起動確認(未署名ライブラリの隔離解除手順は README に記載)

### B. 実地確認が必要な項目(ユーザーの環境・認証情報が必要)

- [ ] Discord Rich Presence: Discord 起動中に Application ID を設定して表示を確認
- [ ] osu! API: OAuth クライアント(Client ID / Secret)で「Fetch metadata」を実行し、ジャンル / 言語の取得と `genre:` 検索を確認
- [ ] ヘッドレスサーバー `osu-music-server` を Linux(Raspberry Pi)で起動し、lazer ライブラリの読み込みと LAN からのストリーミングを確認
- [ ] Web リモートをスマートフォンの実機ブラウザーで確認(PC 操作、Play here のストリーミング、背景画像)
- [ ] OBS のブラウザーソースに `/overlay` を追加して表示を確認
- [ ] ヒットサウンドの同期精度を耳で確認し、必要なら Settings のオフセットで補正

### C. 改善候補(優先度低)

- [ ] lazer で 1 セット内に複数の音声ファイルがある場合、最後に走査した難易度の音声しか拾わない
- [ ] ストーリーボードの Fail レイヤー(意図的に非対応)と、Theater モードでの動画 + ストーリーボード合成表示
- [ ] サーバーの認証(現状は LAN 内無認証)と HTTPS
- [ ] Web リモートのプレイリスト / お気に入りビューへの対応(現状は全曲検索のみ)
- [ ] モバイルのネイティブアプリ(現状は Web リモートで代替)
- [x] 設定 UI の整理(Settings をタブ化、2026-09-06)
- [ ] パネル(Queue / Playlists / Browse / EQ / Sources / Settings)の開閉状態を保存する

### D. human-debug-list.md(2026-09-06 追加、完了)

- [x] UI 作り直し(サイドバー + 一覧 + 詳細 + プレイヤーバー、`MainWindow.axaml` 全面書き換え)
- [x] 閉じた後の不具合(DI コンテナの同期 Dispose が例外を投げて終了時にクラッシュしていた。`App.axaml.cs` で ShutdownRequested を一度キャンセルし、再生停止 → 非同期停止 → DisposeAsync → Shutdown の順に変更)
- [x] 動画 / ストーリーボードを背景画像の上に表示(詳細ペイン上部の層構造。動画なしのときはネイティブホストを非表示)
- [x] 詳細ペインの初期スクロール位置を先頭に(難易度 ListBox の `AutoScrollToSelectedItem="False"`)
- [x] 動画 / ストーリーボードのポップアップと全画面(`VisualsWindow`、libVLC の描画面を受け渡し)
- [x] 難易度プレビュー(`Core/Preview`、`Controls/PlayfieldPreviewView.cs`、`DifficultyPreviewWindow`)
- [x] テーマ変更(`Themes/`、Settings → Appearance)
- [x] キーボードショートカット(`Input/ShortcutMap.cs`、Settings → Shortcuts)
- [x] OBS オーバーレイ(`/overlay`、Settings → Server / OBS に案内)、おすすめ(Recommended ビュー)、プレイリストからコレクション生成(Export view as collection.db)は実装済みのためチェックのみ

## 守るべきルール(AGENT.md より)

- osu! のフォルダーは読み取り専用。書き込みは一切しない(コレクション書き出しも osu! フォルダー内は拒否)
- stable の処理は Windows 以外で実行しない
- Nullable 有効、警告はエラー、非同期 I/O で UI をブロックしない
- 新規クラスは xUnit + FluentAssertions でテストする
- 同じフォルダで複数のエージェントセッションを同時に動かさない(2026-09-06 に上書き事故あり)
- OsuParsers の `BeatmapDecoder` は静的状態を持ちスレッド安全ではない。`.osu` の解析は必ず `BeatmapDecoderGate.Sync` をロックして行う
- `human-debug-list.md` はチェックを付けるだけで本文は編集しない(ファイル内の指示)
