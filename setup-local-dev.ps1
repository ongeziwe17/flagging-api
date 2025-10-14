# Setup Script for Feature Flags API with Database
Write-Host "Setting up Feature Flags API with Database" -ForegroundColor Green

# Navigate to the correct directory
Set-Location "FlaggingAPI\FeatureFlags.Api"

# Start only the database containers first
Write-Host "Starting database containers..." -ForegroundColor Yellow
docker-compose up -d sqlserver redis

# Wait for databases to be ready
Write-Host "Waiting for databases to start (30 seconds)..." -ForegroundColor Yellow
Start-Sleep -Seconds 30

# Check if containers are running
Write-Host "Checking container status..." -ForegroundColor Cyan
docker ps

Write-Host ""
Write-Host "Database setup complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "1. Run your API: dotnet run" -ForegroundColor White
Write-Host "2. The API will automatically create the database and tables" -ForegroundColor White
Write-Host ""
Write-Host "Access database containers (as per team instructions):" -ForegroundColor Cyan
Write-Host "SQL Server:" -ForegroundColor Yellow
Write-Host "docker run -it --rm mcr.microsoft.com/mssql-tools /opt/mssql-tools/bin/sqlcmd -S host.docker.internal,1433 -U sa -P `"CodeCrafters@SQL2025!`"" -ForegroundColor Gray
Write-Host ""
Write-Host "Redis:" -ForegroundColor Yellow
Write-Host "docker run -it --rm redis redis-cli -h host.docker.internal -a `"CodeCrafters@Redis2025!`"" -ForegroundColor Gray