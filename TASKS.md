# osu! music player 開発タスクリスト

最終更新: 2026-09-06(フェーズ 1〜4 実装後)

## 現在の開発段階

**段階: フェーズ 1〜4 の実装完了。残りはフェーズ 0(基盤整備)と、外部サービスの認証情報が必要な項目の実地確認。**

- 対象: Windows / macOS / Linux(実行確認は Windows のみ)
- 技術: .NET 8 / Avalonia 11.3 / ManagedBass / OsuParsers / Realm 20.1
- テスト: 120 件合格(Core 53 / Audio 18 / App 46 / Server 3)、警告ゼロ(TreatWarningsAsErrors)
- 実データ検証: この PC の stable(約 3000 フォルダ)と lazer(約 3950 セット)で読み込み・再生を確認済み

### 実装済み

| 領域 | 内容 |
| --- | --- |
| 検出 | Windows の stable / 全 OS の lazer 自動検出、手動フォルダ追加(設定 JSON に永続化)、Reload |
| 読み込み | osu!.db / .osu(背景) / client.realm(動的スキーマ)、lazer ハッシュストレージ解決、Protected 除外 |
| 統合 | stable / lazer の重複統合(OnlineId → 正規化メタデータ、辞書索引)、音声ファイル単位のトラック化 |
| メタデータ | タイトル / アーティスト(Unicode 優先)、マッパー、タグ、最頻 BPM、長さ、背景画像、難易度ごとの星 / ルールセット / CS・AR・OD・HP / プレビュー地点、.osu パス |
| 再生 | 再生 / 一時停止 / 停止 / シーク / 音量、DT / NC / HT / DC、自動送り |
| ナビゲーション | 前後移動、キュー、シャッフル(1 周重複なし + 履歴で戻る)、リピート Off / All / One |
| 一覧 | 検索(複数語 AND、タイトル / アーティスト / マッパー / タグ)、ソート(タイトル / アーティスト / BPM / 長さ) |
| 詳細 | 難易度一覧(星評価と色、CS/AR/OD/HP、ルールセット)、プレビュー地点からの再生、出典バッジ |
| 動画 | .osu の Video イベント解析、libVLC(LibVLCSharp)によるミュート背景動画、音声クロックへの同期と mod に応じた速度 |
| ヒットサウンド | .osu 解析からのイベント列生成(サークル / スライダー各エッジとティック / スピナー)、譜面 → スキン → lazer 既定サンプルの解決、BASS サンプルを音声クロックに同期(デバイス遅延を先読み補正) |
| ストーリーボード | ReOsuStoryboardPlayer.Core で .osb + .osu を解析、Skia で描画(加算合成・回転・反転・アニメーション)、選択難易度に追従 |
| 配布 | Windows x64 用 bass.dll / bass_fx.dll の自動コピー |

### 既知の制限・技術的負債

- [ ] macOS / Linux 用 BASS ネイティブライブラリが未配置(起動はするが再生不可)
- [x] Git リポジトリ化(Codex が初期コミット済み)
- [ ] 結合試験ハーネスがセッション一時フォルダにしかない(リポジトリ未収録)
- [ ] lazer で 1 セット内に複数音声がある場合、最後に走査した難易度の音声しか拾わない
- [ ] osu!.db の TotalTime が 0 の WIP 譜面は長さが 0:00 と表示される(音声から実測していない)
- [ ] 音量・mod・ソート・パネル開閉状態が再起動で失われる
- [ ] 制限環境で Avalonia の統計タスクが AppData へ書き込めず失敗することがある(要再現)
- [ ] lazer の Realm ファイル形式が更新された際の互換性確認手順がない

---

## フェーズ 0: 基盤整備(短期・小)

- [ ] `git init` と初回コミット(.gitignore は用意済み)
- [ ] 結合試験ハーネスを `tools/OsuMusicPlayer.IntegrationHarness` として収録し、README に実行方法を記載
- [ ] 設定の永続化を拡張(音量、mod、ソート、シャッフル、リピート)
- [ ] 音声ファイルから実際の長さを取得し、DB の長さが 0 の場合に補完
- [ ] Avalonia 統計タスクのログ書き込み失敗を再現し、無効化または無害化
- [ ] macOS / Linux 用 BASS の配置手順と `third_party/README.md` の更新(ライセンス・ハッシュ含む)

## フェーズ 1: 基本機能の完成(documents.md「基本機能」)

- [x] **プレイリスト**: 作成 / 改名 / 削除、追加・削除・並べ替え、settings.json に保存
- [x] **お気に入り**: 詳細ペイン / 下部バーの ♥ と Favourites ビュー
- [x] **イコライザー**: BASS_FX PeakEQ 10 バンド、プリセット、自動保存
- [x] **アーティスト / マッパー**: Browse パネル(アーティスト / マッパー / タグのチップ)と `artist:` `mapper:` 検索
- [x] **ゲーム内コレクション読み込み**: stable collection.db + lazer Realm BeatmapCollection をビューとして表示
- [x] **タグ検索の強化**: 検索言語(フィールド指定、範囲、除外、OR、引用符)
- [x] **現在再生中の状態復元**: 最後のトラック / 位置 / キュー、音量、mod、シャッフル、リピート、ソート
- [x] 一覧の仮想化: VirtualizingStackPanel + 遅延画像読み込みで約 4,000 件を確認済み
- [ ] 曲ダブルクリックで再生
- [ ] ファイル読み込みの除外設定（曲の長さなど）

## フェーズ 2: ビジュアル機能(documents.md「特殊機能」前半)

- [x] **譜面プレビュー**(2026-09-06): 右側の詳細ペインに大きい背景、バッジ(ソース / 最高星 / 難易度数 / BPM / 長さ)、難易度一覧(ルールセット、星の色分け、CS/AR/OD/HP、mania はキー数)、「Preview from m:ss」でプレビュー地点から再生
- [x] **動画・ストーリーボードの検出**(2026-09-06): .osu の [Events] を再生時に読み、Video / .osb を解決して下部バーに VIDEO / STORYBOARD バッジを表示(`BeatmapMediaResolver`)
- [ ] **背景動画再生**: 描画方式の決定が必要。候補は LibVLCSharp.Avalonia 3.10 + VideoLAN.LibVLC.Windows 3.0.23(NuGet 約 90MB、コーデック網羅、コールバック描画で合成も可)か FFMediaToolkit 4.8(FFmpeg ネイティブを別途配置、フレームを WriteableBitmap に描画)
- [x] **ストーリーボード再生**: ReOsuStoryboardPlayer.Core(NuGet, MIT)+ Avalonia Skia 描画で実装
- [x] ストーリーボード: ヒットサウンド連動トリガー、Theater モード(全幅表示)、テクスチャの並列デコード。Fail レイヤーは意図的に非対応
- [x] **ヒットサウンド再生**: 自前実装(OsuParsers + BASS サンプル)。KeyASIO.Net はアプリケーションでありライブラリではないため不採用
- [x] ヒットサウンド: Settings パネルの音量 / オフセット、NC / DC のピッチ、lazer スキン(Realm)のサンプル解決
- [ ] ヒットサウンドの同期精度の聴感確認(オフセットで補正可能)

## フェーズ 3: 外部連携(documents.md「特殊機能」後半)

- [x] **Rich Presence**: Discord IPC 直接実装。Application ID の入力が必要(未検証: Discord 未起動環境)
- [x] **マップサイトへのリンク**: 詳細ペインの「osu! web」
- [x] **OBS オーバーレイ**: `/overlay`
- [x] **ミュージックサーバー**: `OsuMusicPlayer.Server`(Kestrel 最小 API、Range 対応ストリーミング、実機確認済み)
- [x] **モバイルからの接続**: `/` の Web リモート(PC 操作 + ブラウザー内再生)。ネイティブアプリは未着手

## フェーズ 4: 発展機能

- [x] **ジャンル / 言語の取得**: osu! API v2(client credentials)+ ディスクキャッシュ。ユーザーの OAuth クライアントが必要(未検証: 認証情報なし)
- [x] **条件指定プレイリスト**: 検索条件を保存するスマートプレイリスト(genre / language は API 取得後に有効)
- [x] **おすすめ機能**: 再生履歴からの内容ベース推薦(Recommended ビュー)
- [x] **プレイリストからコレクション生成**: 表示中ビューを collection.db に書き出し(osu! フォルダー内は拒否)
- [x] **Linux 常時起動サーバー**: `OsuMusicPlayer.ServerHost`(ヘッドレス、lazer 対応)。Linux 実機では未検証

---

## 直近の推奨着手順

1. フェーズ 0 の `git init` とハーネス収録(作業の安全網)
2. フェーズ 1 のプレイリストとお気に入り(保存形式を先に決める)
3. フェーズ 1 のゲーム内コレクション読み込み(既存のローダー構造で実装しやすい)
4. イコライザー
5. フェーズ 2 の譜面プレビュー

## 守るべきルール(AGENT.md より)

- osu! のフォルダーは読み取り専用。書き込みは一切しない
- stable の処理は Windows 以外で実行しない
- Nullable 有効、警告はエラー、非同期 I/O で UI をブロックしない
- 新規クラスは xUnit + FluentAssertions でテストする
