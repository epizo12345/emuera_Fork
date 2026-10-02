# Packager Releaseのライセンス資料

Release ZIPはrepoの`license.md`、このdirectory内のnotice、runtime-template内の`fonts/DotGothic16-OFL.txt`を同梱します。
`LICENSE.md`と`licenses/`はsealed runtime-templateにも収録し、Packagerで生成するWeb出力へそのまま引き継ぎます。Web出力のフォント通知は`fonts/DotGothic16-OFL.txt`です。

- `DOTNET-LICENSE.txt`と`DOTNET-THIRD-PARTY-NOTICES.txt`は、Release buildに使った.NET SDK 10.0.112の配布資料から無改変で保存したものです。
- `AngleSharp-MIT.txt`はRuntimeのAngleSharp 1.6.0（NuGet nuspecのlicense expression: MIT）のnoticeです。
- `System.IO.Hashing-MIT.txt`はRuntimeのSystem.IO.Hashing 10.0.10（NuGet nuspecのlicense expression: MIT）のnoticeです。
- SkiaSharp、Enums.NET、Emuera等のnoticeはrepo rootの`license.md`にあります。
- `SkiaSharp-THIRD-PARTY-NOTICES.txt`は実リンクしたSkiaSharp.NativeAssets.WebAssembly 4.150.1から無改変で保存した第三者通知です。SkiaSharpのMIT表示だけで代用しません。
- `DOTNET-RUNTIME-THIRD-PARTY-NOTICES.txt`はbrowser-wasm Runtime 10.0.12、`DOTNET-WINX64-RUNTIME-THIRD-PARTY-NOTICES.txt`はself-contained win-x64 Runtime 10.0.12の通知です。
- `DOTNET-WINDOWSDESKTOP-LICENSE.txt`はPackager同梱WindowsDesktop Runtime 10.0.12のライセンスです。
- DotGothic16のSIL Open Font Licenseはruntime-templateのフォントファイルとともに保持されます。

これらはエンジン・依存・フォントの通知であり、入力するゲーム・画像・口上の再配布許諾を与えるものではありません。
