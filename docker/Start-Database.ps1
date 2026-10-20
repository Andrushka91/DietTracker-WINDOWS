Set-Location -LiteralPath $PSScriptRoot
 docker compose up -d --wait
if ($LASTEXITCODE -ne 0) { throw 'Docker nu a putut porni PostgreSQL.' }
 docker compose exec -T postgres psql -U diettracker -d diettracker -c 'SELECT count(*) AS table_count FROM information_schema.tables WHERE table_schema = ''public'' AND table_type = ''BASE TABLE'';'
if ($LASTEXITCODE -ne 0) { throw 'Verificarea importului a esuat.' }
