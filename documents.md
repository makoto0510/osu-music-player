# osu! music player資料

これはAIに渡す資料です。

# 概要

これはローカルPCにインストールされているosu!(stable/lazer)から音声ファイル、データベースを抽出、解析し、再生などできるサービスです。

提供プラットフォームは、Windows,MacOS,Linux,Android,iPhoneです。

なおMacOS,Linux,Android,iPhoneはStableの提供がないのでLazer環境での利用のみです。

開発段階のためデスクトップ環境（Windows,MacOS,Linux）の環境のみ利用可能です。

---

**参考**

[https://github.com/Milkitic/Osu-Player](https://github.com/Milkitic/Osu-Player)

## PCソフトの特殊機能

- osu!(stable/lazer)のローカルファイルから情報、音声ファイルを取得し、再生する。
- stableとlazerで曲が重複するなど考慮しながら読み込める
- https://github.com/Milkitic/KeyASIO.Netを利用してヒットサウンドを再生可能（参考に書いたソフトウェアはこのライブラリを使っています）
- https://github.com/MikiraSora/ReOsuStoryboardPlayerを利用してストーリーボードを再生可能
- バックグラウンド動画を再生可能
- 譜面プレビュー機能
- ビートマップ難易度プレビュー機能
- DT/NC/HT/DC modを適用可能
- OBSオーバーレイ
- ミュージックサーバー
- ゲーム内コレクション
- ビートマップタグで検索
- BPM、長さなどでソート
- プレイリスト作成（ビートマップタグ、ジャンル、言語などで指定可能）
- おすすめ機能（ビートマップタグ、BPM、ジャンルなどでユーザーの好みを学習）
- プレイリストからコレクションを生成
- Rich Presence
- マップサイトに直接アクセス
- マルチプラットフォーム(mac、linuxはlazerのみ)
- モバイルからはpcで立ち上げているサーバーにアクセスでき再生できる(モバイルでもローカル再生可能)

**基本機能**

- お気に入り
- プレイリスト
- キュー
- イコライザー
- リピート、シャッフル
- アーティスト、マッパー

### 将来的に実装したい内容

- Linuxで常時起動できるスタンドアロンミュージックサーバークライアントをリリース(ラズパイでの利用を想定)
