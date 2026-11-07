# PowerShell Database Restore Script for EnUcuzTelefon PostgreSQL
param (
    [Parameter(Mandatory=$true)]
    [string]$BackupFile,
    [string]$ContainerName = "enucuztelefon_postgres",
    [string]$DbUser = "postgres",
    [string]$DbName = "enucuztelefon"
)

if (!(Test-Path -Path $BackupFile)) {
    Write-Host "Error: Backup file '$BackupFile' does not exist." -ForegroundColor Red
    exit 1
}

Write-Host "Restoring database $DbName from $BackupFile..."

# Decompress and stream to docker exec psql
gunzip -c $BackupFile | docker exec -i $ContainerName psql -U $DbUser -d $DbName

if ($LASTEXITCODE -eq 0) {
    Write-Host "Database restore completed successfully." -ForegroundColor Green
} else {
    Write-Host "Restore failed with exit code $LASTEXITCODE" -ForegroundColor Red
}
