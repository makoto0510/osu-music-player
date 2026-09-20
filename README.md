# osu! music player

[![.NET](https://img.shields.io/badge/.NET-8.0-512bd4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Avalonia UI](https://img.shields.io/badge/Avalonia-11.x-9b59b6)](https://avaloniaui.net/)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)](#動作環境)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](#クレジット--免責事項)

ローカルにインストールされた **osu!stable** および **osu!lazer** の楽曲ライブラリをそのまま直接読み込んで楽しめる、高機能デスクトップ音楽プレイヤーです。  
楽曲の再生にとどまらず、譜面付属の**背景動画**、**ヒットサウンド**、**ストーリーボード**の完全再現再生や、オートプレイ風の**難易度プレビュー**にも対応しています。

> [!NOTE]
> **安全設計 (Read-Only):**  
> 本プレイヤーは osu! のデータフォルダーに対して**完全読み取り専用**でアクセスします。元のビートマップファイルやデータベースを変更・破損させることは一切ありません。プレイリストや設定情報は、本プレイヤー独自のデータフォルダーに独立して保存されます。

---

## 目次

- [主な機能](#主な機能)
- [動作環境](#動作環境)
- [ダウンロード & インストール](#ダウンロード--インストール)
- [クイックスタート](#クイックスタート)
- [機能ガイド](#機能ガイド)
  - [ライブラリ統合と検索](#ライブラリ統合と検索)
  - [UI モードとテーマ](#ui-モードとテーマ)
  - [ビジュアル（動画・ストーリーボード・プレビュー）](#ビジュアル動画ストーリーボードプレビュー)
  - [ヒットサウンド再現](#ヒットサウンド再現)
- [キーボードショートカット](#キーボードショートカット)
- [外部連携](#外部連携)
  - [Web リモート & OBS オーバーレイ](#web-リモート--obs-オーバーレイ)
  - [ヘッドレスサーバー](#ヘッドレスサーバー)
  - [Discord Rich Presence & osu! API](#discord-rich-presence--osu-api)
- [設定とデータの保存先](#設定とデータの保存先)
- [ビルドと開発](#ビルドと開発)
- [トラブルシューティング](#トラブルシューティング)
- [クレジット & 免責事項](#クレジット--免責事項)

---

## 主な機能

- **ライブラリのシームレスな統合**  
  stable と lazer のライブラリを自動検出し、重複楽曲をスマートに統合。ゲーム内コレクション（`collection.db` / Realm）もそのまま読み込み可能。
- **高機能オーディオエンジン**  
  BASS オーディオエンジンを採用。キュー管理、シャッフル、リピート、再生速度/ピッチ変更（DT / NC / HT / DC mod）、10 バンドイコライザーを搭載。
- **譜面演出の完全再現**  
  - 背景動画再生（libVLC 連携、音声同期、mod 速度追従）
  - ストーリーボード描画（加算合成、回転、アニメーション、ヒットサウンドトリガー対応）
  - ヒットサウンド同期再生（譜面付属音源 / 各種スキン音源への切り替え対応）
  - 4 モード（osu! / taiko / catch / mania）のオートプレイ難易度プレビュー
- **自由度の高い UI カスタマイズ**  
  現代的な **Studio モード** と、従来の音楽プレイヤーに近い **Classic モード** の 2 種類のレイアウト。6 種のテーマプリセットおよび自由なアクセントカラー設定。
- **充実した外部連携機能**  
  ブラウザからスマホや別端末で操作できる **Web リモート**、配信用の透過 **OBS オーバーレイ**、**Discord Rich Presence**、楽曲のジャンル・言語を補完する **osu! API v2** 連携。

---

## 動作環境

| OS | 対象アーキテクチャ | 対応 osu! ライブラリ | 背景動画の再生 |
| :--- | :--- | :--- | :--- |
| **Windows** | x64 | osu!stable / osu!lazer | 同梱または NuGet 経由で libVLC 自動配置 |
| **macOS** | Apple Silicon (arm64) / Intel (x64) | osu!lazer のみ | ビルド時に libVLC を配置 |
| **Linux** | x64 / arm64 | osu!lazer のみ | システムの libVLC を利用 (`apt install libvlc-dev` 等) |

- ローカルに楽曲データを含む osu! がインストールされている必要があります。
- BASS / BASS_FX のネイティブバイナリは各プラットフォーム向けに同梱されています。

---

## ダウンロード & インストール

### リリースバイナリを利用する場合 (推奨)

1. [GitHub Releases](https://github.com/makoto0510/osu-music-player/releases) より、お使いの OS・環境に合ったアーカイブをダウンロードします。
   - Windows: `OsuMusicPlayer-win-x64.zip`
   - macOS: `OsuMusicPlayer-osx-arm64.zip` (Apple Silicon) / `OsuMusicPlayer-osx-x64.zip` (Intel)
   - Linux: `OsuMusicPlayer-linux-x64.tar.gz` / `OsuMusicPlayer-linux-arm64.tar.gz`
2. アーカイブを任意のフォルダーに展開します。
3. アプリケーションを実行します:
   - **Windows:** `OsuMusicPlayer.App.exe` を起動。
   - **macOS / Linux:** `OsuMusicPlayer.App` に実行権限を付与して起動。
     ```sh
     chmod +x OsuMusicPlayer.App
     ./OsuMusicPlayer.App
     ```

> [!TIP]
> **Self-Contained (ランタイム同梱) 版**をご利用の場合、.NET ランタイムを別途インストールする必要はありません。  
> ランタイム非同梱版をご利用の場合は、**.NET 8 デスクトップランタイム**（および ASP.NET Core ランタイム）が必要です。

---

## クイックスタート

1. **起動と自動検出**  
   アプリを起動すると、PC 内の osu!stable / osu!lazer のインストール先を自動的に検出してライブラリの読み込みを開始します。
2. **手動でのフォルダー指定**  
   標準以外の場所へインストールしている場合や曲が表示されない場合は、ツールバーの **Sources** を開き、フォルダーを手動で追加してください。
   - **osu!stable (Windows のみ):** `osu!.db` と `Songs` フォルダーを含むディレクトリ。
   - **osu!lazer:** `client.realm` と `files` フォルダーを含むディレクトリ。
3. **再生する**  
   一覧から楽曲をダブルクリックすると即座に再生が始まります。
4. **ライブラリの再読み込み**  
   osu! 側で新しい曲をダウンロード・インポートした際は、ヘッダーの **Reload** をクリックすることで最新の状態に同期されます。

---

## 機能ガイド

### ライブラリ統合と検索

stable と lazer の両方がインストールされている場合、同一楽曲の重複をハッシュやメタデータから自動的にマージして 1 つのトラックとしてすっきり表示します。

#### 検索クエリチートシート

検索バー（`Ctrl+F` または `/`）では、多彩なフィルター構文が利用可能です。

| 入力例 | 絞り込み内容 |
| :--- | :--- |
| `artist:xi` | アーティスト名 |
| `title:freedom` | タイトル名 |
| `mapper:sotarks` | 譜面作成者（マッパー） |
| `mode:mania` | ゲームモード (`osu` / `taiko` / `catch` / `mania`) |
| `source:lazer` | ライブラリ元の指定 (`stable` / `lazer`) |
| `bpm:180-240` | BPM の範囲指定 |
| `stars:>6.5` | 難易度（星の数）での絞り込み |
| `length:<3:00` | 曲の長さ（3分未満など） |
| `genre:anime` | ジャンル指定（※osu! API メタデータ取得後） |
| `language:japanese` | 楽曲言語指定（※osu! API メタデータ取得後） |
| `-remix` | 特定キーワードの除外 |
| `camellia|t+pazolite` | OR 検索（いずれかに一致） |
| `"sweet love"` | 空白を含むフレーズの完全一致 |

#### プレイリスト & 整理機能
- **スマートプレイリスト:** 検索条件自体を保存し、条件に合う楽曲を常に自動収集。
- **レコメンド機能:** 再生履歴のタグ・アーティスト・マッパー・BPM を分析し、ライブラリ内の好みに合う未再生曲を提示。
- **コレクション書き出し:** プレイヤー上で作成・絞り込んだ曲リストを stable 形式の `collection.db` として書き出し可能（※安全のため osu! フォルダーへの直接上書きは制限されています）。

---

### UI モードとテーマ

**Settings → Appearance → Interface / UI** から、お好みの作業スタイルに合わせて 2 つの UI スタイルを切り替えられます。

- **Studio モード (既定):** 楽曲リストを広く活用し、サイドパネル（Settings / Sources / Equalizer / Browse / Playlists）を用途に応じて素早く呼び出せるモダンデザイン。
- **Classic モード:** 左サイドバー、中央トラック一覧、右側 Now Playing、下部プレイヤーバーで構成される、伝統的なメディアプレイヤーのレイアウト。

#### テーマ & アクセントカラー
- **プリセットテーマ:** osu! Pink / Lazer Purple / Midnight Blue / Forest / OLED Black / Daylight
- **カスタムアクセントカラー:** `#RRGGBB` 形式で自由なカラーコードを指定可能。

---

### ビジュアル（動画・ストーリーボード・プレビュー）

曲詳細ペインのトグルボタンから、いつでもビジュアル演出を切り替えられます。

- **背景動画:** 譜面付属の動画を BASS 音声にミリ秒単位で同期再生。DT / HT などの速度変更 mod にもリアルタイムで追従します。
- **ストーリーボード:** `.osb` および難易度別 `.osu` をパースし、SkiaSharp により高精度に描画。
- **ポップアウト & 全画面表示 (`Ctrl+P` / `F11`):** 動画やストーリーボードを独立した別ウィンドウに切り離し、マルチモニター環境やシアター環境で全画面表示できます。
- **難易度プレビュー (`Ctrl+Shift+P`):** 譜面の配置とリズムを 4 モード対応のミニマルなオートプレイ画面で確認できます。

---

### ヒットサウンド再現

**Hitsounds** 機能を有効にすると、譜面の打鍵音（ノーマル、ホイッスル、クラップ、フィニッシュ等）を再生中の音楽と完全同期して発音します。

- **Beatmap 音源:** 譜面固有のカスタムサンプルを優先再生。
- **Skin 音源:** 各種スキンや既定サウンドフォントの打鍵音で再生。
- デバイスのレイテンシに応じたオフセット微調整（Settings）も可能です。

---

## キーボードショートカット

いつでも快適に操作できるよう、各種グローバルショートカットが割り当てられています（**Settings → Shortcuts** でも確認可能）。

| キー | 操作内容 |
| :--- | :--- |
| `Space` / `Enter` | 再生 / 一時停止（選択中の曲を再生） |
| `Ctrl + ←` / `Ctrl + →` | 前の曲 / 次の曲 |
| `Shift + ←` / `Shift + →` | 5秒 巻き戻し / 早送り |
| `Ctrl + ↑` / `Ctrl + ↓` | 音量の上下 |
| `Ctrl + F` または `/` | 検索バーにフォーカス |
| `Ctrl + D` | お気に入りの切り替え |
| `Ctrl + E` | 再生キューに追加 |
| `Ctrl + H` | 選択中の曲を一時的に非表示 |
| `Ctrl + S` / `Ctrl + R` | シャッフル / リピートの切り替え |
| `Ctrl + Q` | キュー / Now Playing ペインの表示切替 |
| `Ctrl + T` | Theater モード |
| `Ctrl + P` / `F11` | ビジュアルの別ウィンドウ化 / 全画面表示 |
| `Ctrl + Shift + P` | 難易度プレビュー表示 |
| `Ctrl + ,` | 設定ウィンドウを開く |
| `Esc` | 検索クリア / 全画面・別ウィンドウ解除 |

※ テキスト入力中は誤爆を防ぐため `Space`、`Enter`、`/` などの単一キー操作は自動的に無効化されます。OS のメディアキーにも対応しています。

---

## 外部連携

### Web リモート & OBS オーバーレイ

内蔵サーバー（既定ポート: `5150`）を有効にすると、ブラウザ経由でのコントロールや配信画面への組み込みが可能になります。

| パス | 説明・用途 |
| :--- | :--- |
| `http://<IP>:5150/` | **Web リモート UI:** スマホやタブレットから再生操作、トラックブラウズ、および「Play here」機能による端末側への音声ストリーミング |
| `http://localhost:5150/overlay` | **OBS ブラウザーソース:** 背景透過の再生中楽曲オーバーレイ（推奨サイズ: 600 × 120） |
| `/api/tracks` | 楽曲リストの取得（検索・ページング対応） |
| `/api/tracks/{id}/audio` | HTTP Live Audio ストリーミング（HTTP Range 対応） |
| `/api/tracks/{id}/background` | 楽曲の背景画像取得 |
| `/api/state` | 現在の再生状態（再生中トラック、シーク位置、音量、キュー等） |

> [!CAUTION]
> 内蔵サーバーには認証機能がありません。必ず信頼できるローカルネットワーク（LAN）内でのみご利用いただき、ルーターのポート開放等でインターネットへ直接公開しないでください。

---

### ヘッドレスサーバー

GUI を起動せず、バックグラウンドの音楽ストリーミングサーバーとして常時起動するための `OsuMusicPlayer.ServerHost` も用意されています（Raspberry Pi などの Linux サーバー運用に最適です）。

```sh
dotnet run --project ./src/OsuMusicPlayer.ServerHost -- --port 5150 --lazer /path/to/osu-lazer-data
```
- `--local-only`: LAN からのアクセスを拒否し localhost のみに限定。
- `--stable <path>`: Windows 環境で stable フォルダーも指定する場合。

---

### Discord Rich Presence & osu! API

- **Discord Rich Presence:** 再生中の曲名、アーティスト名、難易度、経過時間を Discord のアクティビティステータスにリアルタイム表示します。
- **osu! API v2 連携:** osu! 公式の OAuth クライアント情報（Client ID / Secret）を設定することで、公式サーバーから楽曲の「ジャンル」や「言語」メタデータを自動取得・キャッシュします。

---

## 設定とデータの保存先

本プレイヤーの設定やキャッシュは、すべて OS 標準のアプリケーションデータ領域に格納されます。

- **設定ファイル:** `%AppData%\OsuMusicPlayer\settings.json`（macOS / Linux では各環境の `ApplicationData` 相当パス）
  - 登録フォルダー、音量、テーマ、カスタムプレイリスト、再生履歴等を保存。
- **オンラインメタデータキャッシュ:** `online-metadata.json`
  - osu! API から取得したジャンル・言語情報を保存。

---

## ビルドと開発

### 開発環境の前提要件
- **.NET 8 SDK** (C# 12)
- 対応プラットフォームの OS 環境

### リポジトリのクローンと実行

```sh
# 依存パッケージの復元
dotnet restore ./OsuMusicPlayer.sln --configfile ./NuGet.Config

# デスクトップアプリの実行
dotnet run --project ./src/OsuMusicPlayer.App/OsuMusicPlayer.App.csproj
```

### 配布用パッケージのビルド (`publish`)

各 OS 向けにランタイムを含めた自己完結型（Self-Contained）バイナリを出力する例：

```sh
# Windows x64
dotnet publish ./src/OsuMusicPlayer.App/OsuMusicPlayer.App.csproj -c Release -r win-x64 --self-contained true -o ./artifacts/publish/win-x64

# macOS Apple Silicon
dotnet publish ./src/OsuMusicPlayer.App/OsuMusicPlayer.App.csproj -c Release -r osx-arm64 --self-contained true -o ./artifacts/publish/osx-arm64

# Linux x64
dotnet publish ./src/OsuMusicPlayer.App/OsuMusicPlayer.App.csproj -c Release -r linux-x64 --self-contained true -o ./artifacts/publish/linux-x64
```

### テストの実行

```sh
# 単体テスト (xUnit)
dotnet test ./OsuMusicPlayer.sln

# 実ライブラリを用いた結合試験ハーネス
dotnet run --project ./tools/OsuMusicPlayer.IntegrationHarness -- all
```

#### プロジェクト構成

```
src/
 ├── OsuMusicPlayer.Core/       # ドメインモデル、osu!db / Realm パーサー、重複統合ロジック
 ├── OsuMusicPlayer.Audio/      # BASS / BASS_FX ラッパー、イコライザー、ヒットサウンド生成
 ├── OsuMusicPlayer.Server/     # Kestrel サーバー、ストリーミング API、Web リモート、OBS オーバーレイ
 ├── OsuMusicPlayer.ServerHost/ # ヘッドレスサーバー用実行ホスト
 └── OsuMusicPlayer.App/        # Avalonia UI アプリケーション本体、設定・テーマ管理
tests/                          # 単体テストプロジェクト
tools/                          # 結合試験・検証用ハーネス
third_party/                    # BASS 等のネイティブライブラリおよびライセンス情報
```

---

## トラブルシューティング

| 現象 | 主な原因と確認事項 |
| :--- | :--- |
| **楽曲が一覧に表示されない** | ツールバーの **Sources** を確認してください。osu!stable は `osu!.db` と `Songs`、lazer は `client.realm` と `files` が存在するフォルダーを指定し、**Reload** を実行してください。 |
| **音声ライブラリの読み込み失敗** | お使いの OS / CPU アーキテクチャに合致したパッケージか確認してください。また、展開時に `bass.dll` などのネイティブファイルが欠落していないか確認してください。 |
| **背景動画が再生されない** | 譜面自体に動画ファイルが存在するか、詳細ペインで **Video** がオンになっているか確認してください。Linux 環境では `libvlc-dev` パッケージがシステムにインストールされている必要があります。 |
| **macOS で起動がブロックされる** | 未署名アプリケーションの隔離（Gatekeeper）による場合があります。システム設定の「プライバシーとセキュリティ」から実行を許可してください。 |
| **Web リモートに接続できない** | Settings の Server でサーバーが有効になっているか、ファイアウォールでポート `5150` が許可されているか、また **Allow LAN** がオンになっているかを確認してください。 |

---

## クレジット & 免責事項

### 免責事項 (Disclaimer)
- 本ソフトウェアは非公式のファンメイドプロジェクトであり、ppy Pty Ltd または osu! 公式チームとは一切関係ありません。
- "osu!" は ppy Pty Ltd の登録商標です。

### オープンソース & サードパーティライブラリ
本プロジェクトは、以下の素晴らしいオープンソースライブラリおよびソフトウェアを活用して制作されています。

- **UI フレームワーク:** [Avalonia UI](https://avaloniaui.net/)
- **オーディオ再生:** [ManagedBass](https://github.com/fiload/ManagedBass) / [Un4seen BASS & BASS_FX](https://www.un4seen.com/)
  - ※ BASS および BASS_FX の非商用利用に関する規約は [third_party/README.md](third_party/README.md) をご覧ください。
- **osu! ファイル解析:** [OsuParsers](https://github.com/DuskyVanilla/OsuParsers)
- **データベース:** [Realm .NET SDK](https://github.com/realm/realm-dotnet)
- **動画再生:** [LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp) / [VideoLAN VLC](https://www.videolan.org/)
- **ストーリーボード再現:** [ReOsuStoryboardPlayer](https://github.com/MikiraSora/ReOsuStoryboardPlayer)
- **ヒットサウンド参照:** [KeyASIO.Net](https://github.com/Milkitic/KeyASIO.Net)
