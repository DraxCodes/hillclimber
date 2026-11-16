# Blind shuffle for the council field test.
#
# Copies out\<brief>-<model>.md to blind\<brief>-<n>.md in random order and records the
# mapping in _map<brief>.json. Score every blind file before opening its map.
#
#   .\shuffle.ps1                 # all briefs a, b, c
#   .\shuffle.ps1 -Briefs c       # one brief

param([string[]]$Briefs = @("a", "b", "c"))

$root = "C:\Users\joelp\code\HillClimberUI\.scratch\council-field-test"
$out = Join-Path $root "out"
$blind = Join-Path $root "blind"

New-Item -ItemType Directory -Force -Path $blind | Out-Null
Get-ChildItem $blind -File -ErrorAction SilentlyContinue | Remove-Item -Force

foreach ($prefix in $Briefs) {
  $files = Get-ChildItem $out -Filter "$prefix-*.md" -File | Sort-Object { Get-Random }
  if (-not $files) { "no outputs for '$prefix'"; continue }

  $map = @()
  $i = 0
  foreach ($f in $files) {
    $i++
    $name = "$prefix-$i.md"
    Copy-Item $f.FullName (Join-Path $blind $name) -Force
    $map += [pscustomobject]@{ blind = $name; source = $f.Name }
  }

  $map | ConvertTo-Json | Set-Content (Join-Path $root "_map$prefix.json") -Encoding UTF8
  "shuffled $($map.Count) '$prefix' files"
}