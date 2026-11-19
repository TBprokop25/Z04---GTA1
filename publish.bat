@echo off
REM Release build Chaos City - vystup v .\release\<platforma>\ a zipy v .\release\
for %%R in (win-x64 linux-x64 osx-arm64) do (
  dotnet publish -c Release -r %%R --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o release\ChaosCity-%%R
  copy README.md release\ChaosCity-%%R\ >nul
  powershell -Command "Compress-Archive -Force release\ChaosCity-%%R\* release\ChaosCity-%%R.zip"
)
echo Hotovo.
