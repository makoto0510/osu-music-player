# osu! music player

ローカルの osu!stable / osu!lazer ライブラリをそのまま楽しめる、デスクトップ音楽プレイヤーです。楽曲だけでなく、譜面の背景動画・ヒットサウンド・ストーリーボードも再生できます。

osu! のデータフォルダーは読み取り専用で扱います。プレイリストや設定は、プレイヤー自身のデータフォルダーに保存します。

## 主な機能

- **ライブラリの統合** — stable / lazer の楽曲を読み込み、重複を統合。ゲーム内コレクションにも対応。
- **音楽再生** — キュー、シャッフル、リピート、DT / NC / HT / DC、10 バンドイコライザー。
- **楽曲の整理** — お気に入り、プレイリスト、検索条件を保存するスマートプレイリスト、再生履歴に基づくおすすめ。
- **譜面の演出** — 背景動画、ヒットサウンド、ストーリーボード、4 モードの簡易難易度プレビュー。
- **画面のカスタマイズ** — Studio / Classic の画面切り替え、配色テーマ、アクセントカラー。
- **外部連携** — Web リモート、音声ストリーミング、OBS オーバーレイ、Discord Rich Presence、osu! API によるメタデータ取得。

## 目次

- [動作環境](#動作環境)
- [はじめに](#はじめに)
- [ライブラリと検索](#ライブラリと検索)
- [画面と再生機能](#画面と再生機能)
- [キーボードショートカット](#キーボードショートカット)
- [外部連携](#外部連携)
- [設定の保存先](#設定の保存先)
- [ビルドと開発](#ビルドと開発)
- [トラブルシューティング](#トラブルシューティング)
- [使用ライブラリ](#使用ライブラリ)

## 動作環境

以下はプロジェクトが対象とする構成です。各 OS・アーキテクチャでの動作検証状況を保証する一覧ではありません。

| OS | 対象アーキテクチャ | 読み込める osu! ライブラリ | 背景動画 |
| --- | --- | --- | --- |
| Windows | x64 | stable / lazer | ビルド時に libVLC を配置 |
| macOS | Apple Silicon / Intel | lazer | macOS 上のビルド時に libVLC を配置 |
| Linux | x64 / arm64 | lazer | libVLC を別途インストール |

- 楽曲を含むローカルの osu! インストールが必要です。
- ソースから実行する場合は **.NET 8 SDK** が必要です。
- ランタイムを含まない配布物では、対応する .NET / ASP.NET Core ランタイムが必要です。プロジェクトのターゲットは .NET 8 で、新しいメジャー版へのロールフォワードも有効です。
- BASS / BASS_FX は対象プラットフォーム向けのネイティブライブラリを同梱しています。

## はじめに

1. 配布物を使う場合はフォルダー全体を展開し、`OsuMusicPlayer.App`（Windows では `OsuMusicPlayer.App.exe`）を起動します。ソースからの起動は[ビルドと開発](#ビルドと開発)を参照してください。
2. 起動すると osu! のライブラリを自動検出します。曲が見つからない場合は **Sources** を開き、データフォルダーを追加します。
3. 一覧の曲をダブルクリックして再生します。検索、お気に入り、プレイリストで曲を絞り込めます。
4. osu! 側で曲を追加・削除した後は **Reload** で読み直します。

### osu! フォルダーの指定

| 種類・OS | 自動検出する主な場所 |
| --- | --- |
| stable / Windows | レジストリ `HKCU\Software\osu!` または `%LocalAppData%\osu!` |
| lazer / Windows | `%AppData%\osu` |
| lazer / macOS | `~/Library/Application Support/osu` |
| lazer / Linux | `~/.local/share/osu` または `~/.var/app/sh.ppy.osu/data/osu`（Flatpak） |

手動指定では、Sources の **Add osu!stable folder…** / **Add osu!lazer folder…** から次のフォルダーを選びます。

- **stable:** `osu!.db` と `Songs` を含むフォルダー。Windows でのみ追加できます。
- **lazer:** `client.realm` と `files` を含むフォルダー。

## ライブラリと検索

お気に入り、プレイリスト、スマートプレイリスト、ゲーム内コレクションを切り替えて閲覧できます。コレクションは stable の `collection.db` と lazer の Realm から読み込みます。

### 検索の例

| 入力 | 絞り込み |
| --- | --- |
| `artist:xi` | アーティスト |
| `mode:mania` | ゲームモード（`osu` / `taiko` / `catch` / `mania`） |
| `source:lazer` | 読み込み元（`stable` / `lazer`） |
| `bpm:120-180` | BPM の範囲 |
| `stars:>5` | 難易度 |
| `length:<3:00` | 曲の長さ |
| `genre:anime language:japanese` | ジャンルと言語（osu! API からの取得後） |

`title:`、`mapper:`、`tag:`、`diff:` も使えます。通常の単語は全項目を検索し、`-語` で除外、`a|b` でいずれか、`"..."` で空白を含む語を指定できます。

### 整理と保存

- **プレイリスト:** Playlists で作成・改名・削除。曲のメニューから追加できます。
- **スマートプレイリスト:** **Save search as smart playlist** で検索条件を保存します。
- **おすすめ:** 再生履歴のタグ・アーティスト・マッパー・BPM をもとに未再生曲を提示します。
- **除外設定:** Settings の Library で曲の長さや検索条件に基づいて非表示にできます。
- **コレクション書き出し:** 表示中の曲を stable 形式の `collection.db` に保存できます。保存先に osu! フォルダーは指定できません。
- **状態の復元:** 音量、mod、シャッフル、リピート、最後の曲と再生位置、キューを次回起動時に復元します。

## 画面と再生機能

### Studio / Classic とテーマ

初期画面は **Studio** です。**Settings → Appearance → Interface / UI** で Classic に切り替えられます。再生状態を保ったまま即時反映し、選択を保存します。

- **Studio:** Settings / Sources / Equalizer / Browse / Playlists を共通のツール領域に表示します。キューは独立して表示を切り替えられます。
- **Classic:** 左のサイドバー、中央のトラック表、右の Now Playing ペイン、下部のプレイヤーバーを中心に操作します。ツールはヘッダーから開きます。

Appearance では osu! Pink / Lazer Purple / Midnight Blue / Forest / OLED Black / Daylight のテーマと、`#RRGGBB` 形式のアクセントカラーを選べます。

### 背景動画・ストーリーボード

曲の詳細にある **Video** / **Storyboard** で表示を切り替えます。背景動画はミュートで再生します。ストーリーボードは `.osb` と選択中の難易度の `.osu` を読み込みます。加算合成、回転、反転、アニメーション、再生したヒットサウンドに連動する HitSound トリガーに対応しています。Fail レイヤーは表示しません。

**Pop out**（Ctrl+P）で独立したウィンドウに移し、F11 またはダブルクリックで全画面表示にできます。Esc で全画面を解除し、もう一度 Esc で元のペインに戻ります。ポップアウト中は移動先のウィンドウで動画を表示します。

### ヒットサウンド

**Hitsounds** を有効にすると、選択中の難易度のヒットサウンドを音楽に同期して再生します。Settings の **Hit sounds → Source** で音源を切り替えます。

- **Beatmap:** 譜面付属のサンプルを優先し、選択中のスキン、stable の使用中スキン（Windows のみ）、lazer の既定サンプルへフォールバックします。
- **Skin:** 譜面付属のカスタム音をスキップし、スキンや既定サンプルの標準サウンドを使います。

### 難易度プレビュー

**Preview play**（Ctrl+Shift+P）で選択中の難易度をオートプレイ風に表示します。osu! / taiko / catch / mania の簡略描画に対応しています。音楽の再生位置に同期し、別の曲を選んでいた場合は対象の曲の再生を開始します。

## キーボードショートカット

一覧は **Settings → Shortcuts** でも確認できます。

| キー | 操作 |
| --- | --- |
| Space / Enter | 再生・一時停止 / 選択曲を再生 |
| Ctrl+← / Ctrl+→ | 前の曲 / 次の曲 |
| Shift+← / Shift+→ | 5 秒戻る / 進む |
| Ctrl+↑ / Ctrl+↓ | 音量を上げる / 下げる |
| Ctrl+F または / | 検索 |
| Ctrl+D / Ctrl+E | お気に入り切り替え / キューに追加 |
| Ctrl+H | 選択曲を非表示 |
| Ctrl+S / Ctrl+R | シャッフル / リピート切り替え |
| Ctrl+Q | Studio のキュー / Classic の Now Playing ペインの表示切り替え |
| Ctrl+, | 設定 |
| Ctrl+T | Theater モード |
| Ctrl+P / F11 | 動画・ストーリーボードの別ウィンドウ表示 / 全画面 |
| Ctrl+Shift+P | 難易度プレビュー |
| Esc | 検索クリア / 全画面解除 |

テキスト入力中は Space、Enter、/ による操作を無効にします。メディアキーにも対応しています。

## 外部連携

### Web リモートと OBS

**Settings → Server・OBS** でサーバーを有効にします。既定のポートは `5150` です。**Allow LAN** を外すと localhost のみで待ち受けます。

| パス | 用途 |
| --- | --- |
| `/` | Web リモート。PC の再生操作と **Play here** によるブラウザーでの音声再生 |
| `/overlay` | OBS ブラウザーソース用オーバーレイ（背景透過、600 × 120 目安） |
| `/api/tracks?q=&offset=&limit=` | トラック一覧 |
| `/api/tracks/{id}/audio` | 音声ストリーミング（Range 対応） |
| `/api/tracks/{id}/background` | 背景画像 |
| `/api/state` | 再生状態 |

再生操作には `POST /api/play/{id}`、`/api/toggle`、`/api/pause`、`/api/resume`、`/api/next`、`/api/previous`、`/api/seek?seconds=`、`/api/volume?value=`、`/api/queue/{id}`、`/api/favourite/{id}` を使えます。

同じ PC では `http://localhost:5150/`、LAN 内の端末では `http://<PC の IP>:5150/` に接続します。**認証機能はありません。信頼できる LAN 内で使用し、インターネットへ直接公開しないでください。**

### ヘッドレスサーバー

`OsuMusicPlayer.ServerHost` は UI なしで Web リモートと API を提供します。サーバー側のローカル再生は行わず、クライアントのブラウザーでストリーミング再生します。

```sh
dotnet run --project ./src/OsuMusicPlayer.ServerHost -- --port 5150 --lazer /home/pi/.local/share/osu
```

既定では LAN 接続を許可します。ローカル接続だけにする場合は `--local-only` を追加してください。Windows では `--stable <folder>` も指定できます。

### Discord / osu! API

- **Discord Rich Presence:** Settings → Discord の Enabled で切り替えます。ローカルの Discord に接続します。
- **ジャンル・言語の取得:** Settings の osu! API に OAuth クライアントの Client ID / Secret を入力し、**Fetch metadata** を実行します。オンライン ID のあるセットの情報をキャッシュし、検索と詳細表示に使用します。

## 設定の保存先

設定は .NET の `Environment.SpecialFolder.ApplicationData` 配下の `OsuMusicPlayer/settings.json` に保存します。Windows では通常 `%AppData%\OsuMusicPlayer\settings.json` です。macOS / Linux の場所は実行環境の ApplicationData に従います。

手動登録したフォルダー、テーマ、プレイリスト、再生状態などを保存します。オンラインメタデータはプレイヤーのデータフォルダーの `online-metadata.json` にキャッシュします。

## ビルドと開発

技術スタックは .NET 8 / C# 12、Avalonia UI、ManagedBass、ASP.NET Core（Kestrel）です。以下のコマンドはリポジトリ直下で実行します。

### 開発実行

```sh
dotnet restore ./OsuMusicPlayer.sln --configfile ./NuGet.Config
dotnet run --project ./src/OsuMusicPlayer.App/OsuMusicPlayer.App.csproj
```

BASS / BASS_FX は `third_party/native/` からビルド先にコピーされます。Windows / macOS の libVLC は各 OS 上で NuGet パッケージから配置されます。Linux で背景動画を使用する場合はシステムに libVLC を用意してください。

### 配布用ビルド

対象 OS 上で実行し、`-r` に対象アーキテクチャを指定します。例えば Windows x64 のランタイム同梱ビルドは次のとおりです。

```sh
dotnet publish ./src/OsuMusicPlayer.App/OsuMusicPlayer.App.csproj -c Release -r win-x64 --self-contained true -o ./artifacts/publish/win-x64
```

| 対象 | RuntimeIdentifier（`-r`） |
| --- | --- |
| Windows x64 | `win-x64` |
| macOS Apple Silicon / Intel | `osx-arm64` / `osx-x64` |
| Linux x64 / arm64 | `linux-x64` / `linux-arm64` |

出力フォルダー全体を配布してください。`--self-contained false` に変更すると .NET ランタイムを含まないビルドになります。libVLC の NuGet 参照はビルドを実行する OS に依存するため、RID の変更だけで別 OS 向けの動画依存関係が揃う構成ではありません。インストーラー作成や macOS の署名・公証は、このコマンドには含まれません。

### テスト

```sh
dotnet test ./OsuMusicPlayer.sln
```

実際の osu! ライブラリを使う結合試験には、次のハーネスを使用します。osu! フォルダーは読み取りのみです。`audio` と `all` は実際に音を再生します。

```sh
dotnet run --project ./tools/OsuMusicPlayer.IntegrationHarness -- load
dotnet run --project ./tools/OsuMusicPlayer.IntegrationHarness -- audio
dotnet run --project ./tools/OsuMusicPlayer.IntegrationHarness -- hitsounds
dotnet run --project ./tools/OsuMusicPlayer.IntegrationHarness -- collections
dotnet run --project ./tools/OsuMusicPlayer.IntegrationHarness -- realm
dotnet run --project ./tools/OsuMusicPlayer.IntegrationHarness -- all
```

順に、ライブラリ読み込み、音声再生、ヒットサウンド解決、コレクション解決、Realm スキーマ、全項目を確認します。自動検出以外の場所には `--lazer <folder>` / `--stable <folder>`（Windows のみ）を指定できます。

### プロジェクト構成

| ディレクトリ | 役割 |
| --- | --- |
| `src/OsuMusicPlayer.Core` | osu! データの読み込み、モデル、重複統合 |
| `src/OsuMusicPlayer.Audio` | 音声再生、ヒットサウンド、mod、EQ |
| `src/OsuMusicPlayer.App` | Avalonia UI と設定・外部連携 |
| `src/OsuMusicPlayer.Server` | Web リモート、ストリーミング API、OBS |
| `src/OsuMusicPlayer.ServerHost` | ヘッドレスサーバー |
| `tests` | ユニットテスト |
| `tools/OsuMusicPlayer.IntegrationHarness` | 実ライブラリを使う結合試験 |

## トラブルシューティング

| 症状 | 確認すること |
| --- | --- |
| 曲が表示されない | Sources でデータフォルダーを指定し、必要な DB とフォルダーがあるか確認して Reload |
| 音声ライブラリを読み込めない | OS・アーキテクチャに合う配布物か、BASS / BASS_FX を含むフォルダー全体を展開したか確認 |
| 背景動画が表示されない | 譜面に動画があるか、Video が有効か、libVLC が配置されているか確認。libVLC がない場合は動画なしで動作 |
| macOS がライブラリをブロックする | 配布元を確認したうえで、システム設定の「プライバシーとセキュリティ」で許可 |
| Web リモートにつながらない | サーバーが有効か、ポートが一致するか、LAN 接続が許可されているか、ファイアウォールを確認 |
| ランタイム不足で起動しない | 配布物に応じた .NET / ASP.NET Core ランタイムがあるか確認。開発時は SDK を使用 |

## 使用ライブラリ

Avalonia、ManagedBass / BASS / BASS_FX、OsuParsers、Realm、LibVLCSharp / libVLC、ReOsuStoryboardPlayer.Core などを使用しています。

同梱する BASS / BASS_FX の配布元、バージョン、検証用ハッシュ、利用条件の所在は [third_party/README.md](third_party/README.md) を参照してください。
