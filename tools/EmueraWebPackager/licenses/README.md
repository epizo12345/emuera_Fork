# Packager Releaseのライセンス資料

Release ZIPはrepoの`license.md`、このdirectory内のnotice、runtime-template内の`fonts/DotGothic16-OFL.txt`を同梱します。

- `DOTNET-LICENSE.txt`と`DOTNET-THIRD-PARTY-NOTICES.txt`は、Release buildに使った.NET SDK 10.0.303の配布資料から無改変で保存したものです。
- `AngleSharp-MIT.txt`はRuntimeのAngleSharp 1.6.0（NuGet nuspecのlicense expression: MIT）のnoticeです。
- `System.IO.Hashing-MIT.txt`はRuntimeのSystem.IO.Hashing 10.0.10（NuGet nuspecのlicense expression: MIT）のnoticeです。
- SkiaSharp、Enums.NET、Emuera等のnoticeはrepo rootの`license.md`にあります。
- DotGothic16のSIL Open Font Licenseはruntime-templateのフォントファイルとともに保持されます。
