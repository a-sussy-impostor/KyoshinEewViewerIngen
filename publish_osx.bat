dotnet restore -r osx-x64 src/KyoshinEewViewer/KyoshinEewViewer.csproj
dotnet msbuild -t:BundleApp -p:RuntimeIdentifier=%2  -p:Configuration=Release src/KyoshinEewViewer/KyoshinEewViewer.csproj -p:PublishDir=tmp/%2_%3 -p:PublishReadyToRun=false -p:PublishSingleFile=true -p:PublishTrimmed=true -p:UseAppHost=true

powershell -c "Compress-Archive -Path tmp/%2_%3/* -DestinationPath tmp/KyoshinEewViewer_ingen_%2_%3.zip"
