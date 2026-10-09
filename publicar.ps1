$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    # Se ejecuta en el equipo del desarrollador, que sí necesita el SDK.
    # --self-contained true incluye .NET y Windows Forms para que el jugador no los instale.
    dotnet publish src/NebulaInvaders.Desktop -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -o portatil
    if ($LASTEXITCODE -ne 0) { throw 'La publicación no se completó.' }
    Write-Host 'Listo: portatil/NebulaInvaders.exe. Copia la carpeta completa; no necesita instalar .NET.'
}
finally { Pop-Location }
