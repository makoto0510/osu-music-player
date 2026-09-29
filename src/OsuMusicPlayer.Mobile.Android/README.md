# Android アプリ（TASK4 初期版）

既存のデスクトップ／ヘッドレスサーバーを操作する Avalonia 製 Android アプリです。Android 端末から osu! のデータファイルを直接読みません。

## 現在できること

- LAN 上のサーバーURLを手動入力して接続
- 楽曲検索と一覧表示（先頭100曲。検索はサーバーの全曲が対象）
- 選択曲のPC側再生、再生／一時停止、前曲／次曲
- 再生中の曲名表示

端末内での音声再生、背景画像、キュー／お気に入り、接続先の保存・自動発見、バックグラウンド再生は後続作業です。サーバーの音声・背景URLは `MobileServerClient` に用意しています。

## ビルドと接続

1. .NET 8 SDK、Android ワークロード、Android SDK を用意します。
2. `dotnet restore src/OsuMusicPlayer.Mobile.Android/OsuMusicPlayer.Mobile.Android.csproj --configfile NuGet.Config` を実行します。
3. `dotnet build src/OsuMusicPlayer.Mobile.Android/OsuMusicPlayer.Mobile.Android.csproj -c Debug` を実行します。
4. PCアプリの Settings → Server / OBS でサーバーと Allow LAN を有効にするか、ServerHost を起動します。
5. Android端末とPCを同じLANに接続し、アプリに `http://<PCのLAN IP>:5150/` を入力します。Android端末から見た `localhost` はPCではありません。

既存サーバーはHTTPかつ認証なしです。AndroidのマニフェストはそのLAN接続のために平文HTTPを許可しています。信頼できるLAN内で使用し、ポートをインターネットに公開しないでください。

Android用プロジェクトは既存デスクトップのソリューション一括ビルドを変えないため、現時点では `OsuMusicPlayer.sln` に含めていません。Android用ビルドは上記のプロジェクトを直接指定してください。
