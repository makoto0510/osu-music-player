# osu! music player

Avalonia と ManagedBass で作る、ローカルの osu!stable / osu!lazer ライブラリ向け音楽プレイヤーです。

## 開発実行

```powershell
dotnet restore .\OsuMusicPlayer.sln --configfile .\NuGet.Config
dotnet run --project .\src\OsuMusicPlayer.App\OsuMusicPlayer.App.csproj
```

公式の BASS / BASS_FX ネイティブライブラリは `third_party/native/` に Windows x64、macOS(universal)、Linux x64 / arm64 分を配置済みです。ビルド時に `src/OsuMusicPlayer.App/BassNatives.targets` が実行 OS(`dotnet publish -r osx-arm64` などの RuntimeIdentifier 指定時はその RID)に合うものを出力フォルダーへコピーします。配布元、バージョン、ライセンスと検証用ハッシュは `third_party/README.md` を参照してください。

### macOS で動かす

1. .NET SDK 8 以降をインストールします(https://dotnet.microsoft.com/download)。ASP.NET Core ランタイムは SDK に含まれます。
2. リポジトリ直下で次を実行します。

   ```bash
   dotnet restore ./OsuMusicPlayer.sln --configfile ./NuGet.Config
   dotnet run --project ./src/OsuMusicPlayer.App/OsuMusicPlayer.App.csproj
   ```

3. osu!lazer は `~/Library/Application Support/osu` から自動検出されます(macOS に osu!stable はありません)。見つからない場合は「Sources」から `client.realm` と `files` を含むフォルダーを追加してください。
4. 背景動画は NuGet の `VideoLAN.LibVLC.Mac` が自動で参照されます。初回起動時に macOS が未署名ライブラリ(`libbass.dylib`、libVLC)をブロックした場合は、「システム設定 → プライバシーとセキュリティ」で許可するか、次で隔離属性を外してください。

   ```bash
   xattr -dr com.apple.quarantine ./src/OsuMusicPlayer.App/bin/Debug/net8.0/
   ```

5. 配布用ビルドは `dotnet publish ./src/OsuMusicPlayer.App -c Release -r osx-arm64 --self-contained false`(Intel Mac は `osx-x64`)です。
6. 動作確認は `dotnet run --project ./tools/OsuMusicPlayer.IntegrationHarness -- all` で、ライブラリ読み込み・再生・ヒットサウンド解決・Realm スキーマをまとめて検証できます。

### Linux で動かす

- BASS は `third_party/native/linux-x64` / `linux-arm64` から自動コピーされます。
- 背景動画には `libvlc` が必要です(Debian / Ubuntu: `sudo apt install libvlc-dev vlc-plugin-base`)。無い場合は動画なしで動作します。
- osu!lazer は `~/.local/share/osu` または Flatpak の `~/.var/app/sh.ppy.osu/data/osu` を自動検出します。
- 常時起動のサーバーだけが必要なら、UI のない `src/OsuMusicPlayer.ServerHost` を使ってください(後述)。

### 結合試験ハーネス

`tools/OsuMusicPlayer.IntegrationHarness` は、この PC にある実際の osu! ライブラリで読み込み・再生・ヒットサウンド解決・Realm スキーマ確認を行うコンソールツールです(osu! のフォルダーは読み取りのみ)。

```powershell
dotnet run --project .\tools\OsuMusicPlayer.IntegrationHarness -- load        # 読み込みと統合の統計
dotnet run --project .\tools\OsuMusicPlayer.IntegrationHarness -- audio       # BASS で数秒再生(小音量)
dotnet run --project .\tools\OsuMusicPlayer.IntegrationHarness -- hitsounds   # サンプル解決の欠落数
dotnet run --project .\tools\OsuMusicPlayer.IntegrationHarness -- collections # コレクションの楽曲がいくつ解決できるか
dotnet run --project .\tools\OsuMusicPlayer.IntegrationHarness -- realm       # lazer 更新後の client.realm スキーマ確認
```

`--lazer <folder>` / `--stable <folder>` で自動検出以外のフォルダーも指定できます。

## 画面構成とテーマ

画面は「上部ヘッダー(ロゴ / 検索 / ゲームモードフィルター / ツールボタン / ウィンドウボタン)」「左サイドバー(ライブラリ / クイックソート / プレイリスト / osu! コレクション)」「中央のトラック表(# / タイトル / アーティスト / BPM / 長さ / ♥ / …メニュー。上部にツールドロワー)」「右の Now Playing ペイン(カバー画像の上に動画とストーリーボードを重ねて描画、曲情報、タグ、操作、Up Next キュー、難易度一覧)」「下部のプレイヤーバー」の 5 つです。Browse / Equalizer / Sources / Settings はヘッダー右のアイコンから開き、中央上部のドロワーに表示されます。キューは右ペインの Up Next に常時表示され、Ctrl+Q で右ペインごと隠せます。各行の「…」(または右クリック)から再生 / プレビュー / キュー追加 / プレイリスト追加 / osu! web を呼び出せます。Settings はタブ(Appearance / Hit sounds / Library / Server・OBS / Discord / osu! API / Shortcuts)に分かれています。

テーマは Settings → Appearance で切り替えます。プリセット(osu! Pink / Lazer Purple / Midnight Blue / Forest / OLED Black / Daylight)に加えて、アクセント色を `#RRGGBB` で自由に指定できます。変更は即座に反映され、`settings.json` の `Appearance` に保存されます。

## キーボードショートカット

Settings → Shortcuts に一覧があります。主なもの: Space(再生 / 一時停止)、Enter(選択曲を再生)、Ctrl+← / →(前 / 次)、Shift+← / →(5 秒シーク)、Ctrl+↑ / ↓(音量)、Ctrl+F または /(検索)、Ctrl+D(お気に入り)、Ctrl+E(キューに追加)、Ctrl+S(シャッフル)、Ctrl+R(リピート)、Ctrl+Q(Now Playing ペインの表示切替)、Ctrl+,(設定)、Ctrl+T(Theater)、Ctrl+P(動画 / ストーリーボードを別ウィンドウへ)、F11(全画面)、Ctrl+Shift+P(難易度プレビュー)、Esc(検索クリア / 全画面解除)。検索ボックス入力中は Space や Enter などの単独キーは無効で、Ctrl などの組み合わせとメディアキーだけが効きます。

## 動画 / ストーリーボードのポップアップと全画面

詳細ペイン上部の「Pop out」(Ctrl+P)で動画とストーリーボードを独立したウィンドウに移し、「Fullscreen」(F11)で全画面にします。Esc で全画面を解除、もう一度 Esc で元のペインに戻ります(ダブルクリックでも全画面を切り替え)。libVLC の描画面は 1 つだけなので、ポップアウト中は詳細ペイン側の動画は止まり、戻すと再び詳細ペインに表示されます。

## 難易度プレビュー

詳細ペインの「Preview play」(Ctrl+Shift+P)で、選択中の難易度をオートプレイ風に描画するウィンドウを開きます。再生中の音楽をクロックにするため、別の曲を選んでいた場合はその曲の再生を開始します。osu!(サークル / スライダー / スピナー / カーソル)、taiko、catch、mania を簡略描画で対応しています。譜面の解析は `OsuMusicPlayer.Core.Preview.PlayfieldPreviewBuilder`(スライダーのパス計算は `SliderPathCalculator`)が行い、`.osu` を読むだけで osu! のフォルダーには書き込みません。

## osu! フォルダーを手動で指定する

自動検出に失敗した場合は、左サイドバーの「Sources」を開き、「Add osu!stable folder…」または「Add osu!lazer folder…」から osu! のデータフォルダーを選択してください。stable は `osu!.db` と `Songs` を、lazer は `client.realm` と `files` を含むフォルダーが必要です。stable の追加ボタンは Windows でのみ表示されます。

手動で追加したフォルダーはアプリ自身の設定ファイル `%AppData%\OsuMusicPlayer\settings.json`（macOS/Linux では `~/.config/OsuMusicPlayer/settings.json`）に保存されます。osu! のフォルダーには一切書き込みません。「Reload」で osu! 側の変更を再読み込みできます。

## 背景動画

譜面に背景動画がある場合、右側の詳細ペイン上部の背景画像の位置でミュート再生します(詳細ペイン上部の「Video」トグルで切り替え)。動画は libVLC(LibVLCSharp)で再生し、Windows x64 向けのネイティブライブラリは NuGet パッケージ `VideoLAN.LibVLC.Windows` から自動で配置されます。macOS / Linux では各 OS 向けの libVLC を別途用意する必要があり、無い場合は動画なしで動作します。

Avalonia のネイティブコントロールをホストするため `src/OsuMusicPlayer.App/app.manifest` に対応 OS を宣言しています。削除すると起動時に例外になります。

## ヒットサウンドとストーリーボード

詳細ペイン上部の「Hitsounds」を有効にすると、詳細ペインで選択中の難易度のヒットサウンド(サークル、スライダーの頭・折り返し・終点・ティック、スピナー終了)を音楽に同期して再生します。サンプルは次の順で探します。

1. 譜面フォルダー(カスタムインデックスやファイル名指定のサンプル)
2. osu!stable の設定ファイルで選択中のスキン(Windows のみ)
3. osu!lazer に同梱の既定サンプル(`osu.Game.Resources.dll` を PE 形式として読み取るだけで、コードは実行しません)

「Storyboard」を有効にすると、`.osb` と選択中難易度の `.osu` にあるストーリーボードを詳細ペイン上部(背景画像の上)に描画します。解析には MIT ライセンスの ReOsuStoryboardPlayer.Core(NuGet)を使い、描画は Avalonia の Skia で行います(加算合成、回転、反転、アニメーション対応。Fail レイヤーとトリガーは未対応)。

## ライブラリ機能

- **ビュー**: 検索欄の隣のドロップダウンで「All tracks / Favourites / Recommended / プレイリスト / スマートプレイリスト / ゲーム内コレクション」を切り替えます。コレクションは stable の `collection.db` と lazer の Realm から読み込みます(読み取りのみ)。
- **検索言語**: 単語は全項目に一致。`artist:` `title:` `mapper:` `tag:` `diff:` `source:stable|lazer` `mode:osu|taiko|catch|mania` `bpm:120-180` `stars:>5` `length:<3:00` `genre:` `language:` で絞り込み、`-語` で除外、`a|b` でいずれか、`"..."` で空白を含む語を指定できます。
- **プレイリスト / お気に入り**: 「Playlists」パネルで作成・改名・削除、詳細ペインで追加。プレイリスト表示中は並べ替え(▲▼)と削除ができます。お気に入りは詳細ペインと下部バーの ♥ で切り替えます。
- **スマートプレイリスト**: 検索条件をそのまま保存する動的プレイリストです(「Save search as smart playlist」)。
- **おすすめ**: 再生履歴(タグ / アーティスト / マッパー / BPM)から好みを学習し、未再生曲を並べます(3 曲以上再生すると表示)。
- **コレクション書き出し**: 表示中のトラックを osu!stable 形式の `collection.db` として任意の場所に保存します。osu! フォルダーへの書き込みは拒否されます。
- **イコライザー**: 10 バンドの BASS_FX ピーキング EQ とプリセット。設定は自動保存されます。
- **除外設定**: 「Settings」の Library exclusions で、短すぎる / 長すぎるセットや、検索言語に一致するセット(例 `mode:mania`)をライブラリから隠せます。一覧の行はダブルクリックで再生できます。
- **状態の復元**: 音量、mod、シャッフル、リピート、最後のトラックと位置、キューを次回起動時に復元します。

## ミュージックサーバー / Web リモート / OBS オーバーレイ

「Settings」パネルでサーバーを有効にすると、Kestrel が `http://<PC の IP>:5150/` で待ち受けます(「Allow LAN」を外すと localhost のみ)。

- `/` はスマホ向けの Web リモートです。PC 側の再生操作に加え、「Play here」でブラウザーが直接音声をストリーミング再生します。
- `/overlay` は OBS のブラウザーソース用オーバーレイ(600×120 目安、背景透過)です。
- `/api/tracks?q=&offset=&limit=`、`/api/tracks/{id}/audio`(Range 対応)、`/api/tracks/{id}/background`、`/api/state`、`POST /api/play/{id}` `toggle` `pause` `resume` `next` `previous` `seek?seconds=` `volume?value=` `queue/{id}` `favourite/{id}` が使えます。
- 認証はありません。LAN 内での利用を想定しています。

`Microsoft.AspNetCore.App` が必要です。この PC のように .NET 8 の ASP.NET Core ランタイムが無い場合でも、`RollForward=Major` により .NET 10 などの新しいランタイムで動作します。

### ヘッドレスサーバー(Raspberry Pi など)

`src/OsuMusicPlayer.ServerHost` はデスクトップ UI なしで同じ API と Web リモートを提供します(ローカル再生は無く、クライアント側でストリーミング再生します)。

```powershell
dotnet run --project .\src\OsuMusicPlayer.ServerHost -- --port 5150 --lazer /home/pi/.local/share/osu
```

## Discord Rich Presence と osu! API

- Rich Presence: discord.com/developers でアプリケーションを作り、その Application ID を「Settings」に入力して有効化します。Discord のローカル IPC に直接接続します(外部ライブラリなし)。
- ジャンル / 言語: osu! の OAuth クライアント(Client ID / Secret)を入力し「Fetch metadata」を押すと、オンライン ID のあるセットのジャンルと言語を取得してキャッシュします(`%AppData%\OsuMusicPlayer\online-metadata.json`)。取得後は `genre:anime` `language:japanese` で検索でき、詳細ペインにも表示されます。

## テスト

```powershell
dotnet test .\OsuMusicPlayer.sln
```
