param(
  [string]$Config = "Release"
)

Write-Host "Restaurando pacotes..."
dotnet restore

Write-Host "Publicando win-x64..."
dotnet publish -c $Config -r win-x64 -p:PublishSingleFile=false -o dist/win

Write-Host "Publicando linux-x64..."
dotnet publish -c $Config -r linux-x64 -p:PublishSingleFile=false -o dist/linux

Write-Host "Concluído. Saídas em dist/win e dist/linux"
