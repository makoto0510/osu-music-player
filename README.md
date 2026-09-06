# osu! music player

Avalonia と ManagedBass で作る、ローカルの osu!stable / osu!lazer ライブラリ向け音楽プレイヤーです。

## 開発実行

```powershell
dotnet restore .\OsuMusicPlayer.sln --configfile .\NuGet.Config
dotnet run --project .\src\OsuMusicPlayer.App\OsuMusicPlayer.App.csproj
```

Windows x64向けの公式 BASS と BASS_FX ネイティブライブラリは `third_party/native/win-x64` に配置済みです。Windows上でのビルド・発行時に `bass.dll` と `bass_fx.dll` がアプリ出力フォルダーへ自動コピーされます。配布元、バージョン、ライセンスと検証用ハッシュは `third_party/README.md` を参照してください。

macOS/Linuxで実音声を再生する場合は、各OS向けの公式ネイティブライブラリを同様に用意する必要があります。ライブラリがない環境でもアプリは起動し、再生時にエラーを表示します。

## osu! フォルダーを手動で指定する

自動検出に失敗した場合は、ウィンドウ右上の「Sources」を開き、「Add osu!stable folder…」または「Add osu!lazer folder…」から osu! のデータフォルダーを選択してください。stable は `osu!.db` と `Songs` を、lazer は `client.realm` と `files` を含むフォルダーが必要です。stable の追加ボタンは Windows でのみ表示されます。

手動で追加したフォルダーはアプリ自身の設定ファイル `%AppData%\OsuMusicPlayer\settings.json`（macOS/Linux では `~/.config/OsuMusicPlayer/settings.json`）に保存されます。osu! のフォルダーには一切書き込みません。「Reload」で osu! 側の変更を再読み込みできます。

## 背景動画

譜面に背景動画がある場合、右側の詳細ペインでミュート再生します(右上の「Video」で切り替え)。動画は libVLC(LibVLCSharp)で再生し、Windows x64 向けのネイティブライブラリは NuGet パッケージ `VideoLAN.LibVLC.Windows` から自動で配置されます。macOS / Linux では各 OS 向けの libVLC を別途用意する必要があり、無い場合は動画なしで動作します。

Avalonia のネイティブコントロールをホストするため `src/OsuMusicPlayer.App/app.manifest` に対応 OS を宣言しています。削除すると起動時に例外になります。

## ヒットサウンドとストーリーボード

右上の「Hitsounds」を有効にすると、詳細ペインで選択中の難易度のヒットサウンド(サークル、スライダーの頭・折り返し・終点・ティック、スピナー終了)を音楽に同期して再生します。サンプルは次の順で探します。

1. 譜面フォルダー(カスタムインデックスやファイル名指定のサンプル)
2. osu!stable の設定ファイルで選択中のスキン(Windows のみ)
3. osu!lazer に同梱の既定サンプル(`osu.Game.Resources.dll` を PE 形式として読み取るだけで、コードは実行しません)

「Storyboard」を有効にすると、`.osb` と選択中難易度の `.osu` にあるストーリーボードを詳細ペインに描画します。解析には MIT ライセンスの ReOsuStoryboardPlayer.Core(NuGet)を使い、描画は Avalonia の Skia で行います(加算合成、回転、反転、アニメーション対応。Fail レイヤーとトリガーは未対応)。

## ライブラリ機能

- **ビュー**: 検索欄の隣のドロップダウンで「All tracks / Favourites / Recommended / プレイリスト / スマートプレイリスト / ゲーム内コレクション」を切り替えます。コレクションは stable の `collection.db` と lazer の Realm から読み込みます(読み取りのみ)。
- **検索言語**: 単語は全項目に一致。`artist:` `title:` `mapper:` `tag:` `diff:` `source:stable|lazer` `mode:osu|taiko|catch|mania` `bpm:120-180` `stars:>5` `length:<3:00` `genre:` `language:` で絞り込み、`-語` で除外、`a|b` でいずれか、`"..."` で空白を含む語を指定できます。
- **プレイリスト / お気に入り**: 「Playlists」パネルで作成・改名・削除、詳細ペインで追加。プレイリスト表示中は並べ替え(▲▼)と削除ができます。お気に入りは詳細ペインと下部バーの ♥ で切り替えます。
- **スマートプレイリスト**: 検索条件をそのまま保存する動的プレイリストです(「Save search as smart playlist」)。
- **おすすめ**: 再生履歴(タグ / アーティスト / マッパー / BPM)から好みを学習し、未再生曲を並べます(3 曲以上再生すると表示)。
- **コレクション書き出し**: 表示中のトラックを osu!stable 形式の `collection.db` として任意の場所に保存します。osu! フォルダーへの書き込みは拒否されます。
- **イコライザー**: 10 バンドの BASS_FX ピーキング EQ とプリセット。設定は自動保存されます。
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
