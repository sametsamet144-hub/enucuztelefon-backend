# PowerShell Database Backup Script for EnUcuzTelefon PostgreSQL
param (
    [string]$ContainerName = "enucuztelefon_postgres",
    [string]$DbUser = "postgres",
    [string]$DbName = "enucuztelefon",
    [string]$BackupDir = "./backups"
)

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
if (!(Test-Path -Path $BackupDir)) {
    New-Item -ItemType Directory -Path $BackupDir -Force
}

$backupFile = "$BackupDir/${DbName}_backup_$timestamp.sql.gz"
Write-Host "Creating backup for $DbName to $backupFile..."

docker exec -t $ContainerName pg_dump -U $DbUser -d $DbName | gzip > $backupFile

if ($LASTEXITCODE -eq 0) {
    Write-Host "Backup completed successfully: $backupFile" -ForegroundColor Green
} else {
    Write-Host "Backup failed with exit code $LASTEXITCODE" -ForegroundColor Red
}
