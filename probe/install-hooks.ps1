param([switch]$Remove)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot
$codexRoot=if($env:CODEX_HOME){$env:CODEX_HOME}else{Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex'}
$target=Join-Path $codexRoot 'hooks.json'
$relay=Join-Path $projectRoot 'bin\CodexMonitor.HookRelay.exe'
$events=Join-Path $PSScriptRoot 'results\hook-events'
$command='powershell.exe -NoProfile -NonInteractive -Command "& '''+$relay.Replace("'","''")+''' '''+$events.Replace("'","''")+'''"'
$config=if(Test-Path -LiteralPath $target){Get-Content -LiteralPath $target -Raw | ConvertFrom-Json -AsHashtable}else{@{hooks=@{}}}
if(!$config.ContainsKey('hooks')){$config.hooks=@{}}
foreach($event in @('UserPromptSubmit','PermissionRequest','Stop','Interrupt')){
    $existing=@($config.hooks[$event] | Where-Object {$null -ne $_})
    # Only remove groups owned by this probe; preserve all unrelated hooks.
    $existing=@($existing | Where-Object {!(($_.hooks | ForEach-Object command) -contains $command)})
    if(!$Remove){$existing+=@{hooks=@(@{type='command';command=$command;timeout=3})}}
    if($existing.Count){$config.hooks[$event]=$existing}else{$config.hooks.Remove($event)}
}
if(Test-Path -LiteralPath $target){Copy-Item -LiteralPath $target -Destination ($target+'.monitor-backup-'+[DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff'))}
$config | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $target -Encoding utf8
Write-Output "Probe hook definitions updated: $target"
Write-Output 'Codex must trust these definitions before they execute. No trust state was changed.'
