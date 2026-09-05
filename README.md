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

## テスト

```powershell
dotnet test .\OsuMusicPlayer.sln
```
