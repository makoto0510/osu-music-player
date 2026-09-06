# Native BASS dependencies

Windows x64 / macOS(universal)/ Linux x64・arm64 用の公式バイナリを、配布 ZIP と実行用ライブラリに分けて保管しています。ビルド時に `src/OsuMusicPlayer.App/BassNatives.targets` が実行 OS(または RuntimeIdentifier)に合うものを出力フォルダーへコピーします。

| ファイル | 配布元 | SHA-256 |
| --- | --- | --- |
| `downloads/bass24.zip` | `https://www.un4seen.com/files/bass24.zip` | `3A03EC9A33D0F4F9D167660DA51C8BB1432E8977496995455AB137277D69636E` |
| `downloads/bass_fx24.zip` | `https://www.un4seen.com/files/z/0/bass_fx24.zip` | `A4BAF602865941963127ACB15ED12627D108189F99C2757970432AE7DA0366CD` |
| `native/win-x64/bass.dll` | `bass24.zip/x64/bass.dll` | `FEBB2CF1882D554C3A958280777DA0B69F07DE6E262DF271DE11C56E4A54AFD4` |
| `native/win-x64/bass_fx.dll` | `bass_fx24.zip/x64/bass_fx.dll` | `A6E1847EEF52D882B4137AF514D834C2E220DACEB417C821D1E502FB7A34C84A` |
| `downloads/bass24-osx.zip` | `https://www.un4seen.com/files/bass24-osx.zip` | `DFADD6238896B02B144B2870655FB9EE2445FC84D5C00F7E1C56FAF9343CE59C` |
| `native/osx/libbass.dylib` | `bass24-osx.zip/libbass.dylib` | `E81FB7B4D0009BA6343FBFCD840620704DCB840686D405B9734CD37150D31974` |
| `downloads/bass_fx24-osx.zip` | `https://www.un4seen.com/files/z/0/bass_fx24-osx.zip` | `EB9E496DA229CDD73DC51D16C7E3FD17C7400A8125C2BE2FEB88EEE1AC68DC68` |
| `native/osx/libbass_fx.dylib` | `bass_fx24-osx.zip/libbass_fx.dylib` | `82AA99A433D9E86DE7ECBBD65599B2FFE9BEFEBCD420D6368EF3BD1D33E418FE` |
| `downloads/bass24-linux.zip` | `https://www.un4seen.com/files/bass24-linux.zip` | `9BF723DBF750D665C14CFC099F96A056483DC35DE6850D2DF76AF8B640435CA9` |
| `native/linux-x64/libbass.so` | `bass24-linux.zip/libs/x86_64/libbass.so` | `BD0841D0C14F25065A16192BE07C20B045E384199A86A00892CC3864474DBC51` |
| `native/linux-arm64/libbass.so` | `bass24-linux.zip/libs/aarch64/libbass.so` | `A05BA3AFC880B03F36DB8DEA50FD1D912CACF2A2C85D785FBAEF020F5A93ADB2` |
| `downloads/bass_fx24-linux.zip` | `https://www.un4seen.com/files/z/0/bass_fx24-linux.zip` | `1EE97610BC2768357C4C344C0D7A058AC95EDF51804C391FDCF7644762BD413B` |
| `native/linux-x64/libbass_fx.so` | `bass_fx24-linux.zip/libs/x86_64/libbass_fx.so` | `63A41BA9627D4577874FD8F241789834AD058F4EF3BAC930BF9BBCE54B4CEDBA` |
| `native/linux-arm64/libbass_fx.so` | `bass_fx24-linux.zip/libs/aarch64/libbass_fx.so` | `7BC424672048E6CB8E44A3E0B5DA984AD7996CF77FA4D3E47808BCA3C7E793E8` |

- BASS: 2.4.18.3
- BASS_FX: 2.4.12.6
- 対象: Windows x64、macOS(universal: x64 + arm64)、Linux x64 / arm64(Raspberry Pi 4 以降の 64bit OS)

公式の利用条件は各 `downloads/*.zip` 配布物内の `bass.txt` と `bass_fx.txt` に含まれています。展開済みSDK一式はGit管理対象外です。BASSは非商用利用では無料ですが、商用利用には用途に応じたライセンスが必要です。
