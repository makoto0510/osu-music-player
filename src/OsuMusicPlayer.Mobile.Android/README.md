# Android アプリ

既存のデスクトップ／ヘッドレスサーバーを操作する Avalonia 製 Android アプリです。Android 端末から osu! のデータファイルを直接読みません。

## 現在できること

- LAN 上のサーバーURLを手動入力して接続
- 楽曲検索と一覧表示（先頭100曲。検索はサーバーの全曲が対象）
- 選択曲のスマホ側再生と一時停止／再開
- 選択曲のダウンロードと保存済み曲の再生
- 選択曲のPC側再生、PC側の再生／一時停止、前曲／次曲
- PCで再生中の曲名表示

スマホで再生すると、PCサーバーから音声をストリーミングします。ダウンロードした曲はAndroidアプリ専用領域に保存され、「保存済み」タブから接続なしでも再生できます。端末の一般的な Downloads フォルダーには出力されません。背景画像、キュー／お気に入り、接続先の保存・自動発見、バックグラウンド再生は後続作業です。

## ビルドと接続

1. .NET 10 SDK、対応する Android ワークロード、Android SDK を用意します。共有の Mobile.App と Mobile.Core は引き続き .NET 8 を対象とします。
2. `dotnet restore src/OsuMusicPlayer.Mobile.Android/OsuMusicPlayer.Mobile.Android.csproj --configfile NuGet.Config` を実行します。
3. `dotnet build src/OsuMusicPlayer.Mobile.Android/OsuMusicPlayer.Mobile.Android.csproj -c Debug` を実行します。
4. PCアプリの Settings → Server / OBS でサーバーと Allow LAN を有効にするか、ServerHost を起動します。
5. Android端末とPCを同じLANに接続し、アプリに `http://<PCのLAN IP>:5150/` を入力します。Android端末から見た `localhost` はPCではありません。

既存サーバーはHTTPかつ認証なしです。AndroidのマニフェストはそのLAN接続のために平文HTTPを許可しています。信頼できるLAN内で使用し、ポートをインターネットに公開しないでください。

Android用プロジェクトは既存デスクトップのソリューション一括ビルドを変えないため、現時点では `OsuMusicPlayer.sln` に含めていません。Android用ビルドは上記のプロジェクトを直接指定してください。
