#!/usr/bin/env bash
# Smoke reproducible de la CLI: menús numerados y Deep Clean con dispositivos
# simulados. No toca discos reales ni necesita contraseña de sudo.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
fake="$tmp/bin"
mkdir -p "$fake"

cat > "$fake/sudo" <<'EOF'
#!/bin/sh
if [ "$1" = "-n" ]; then
  shift
  if [ "$1" = "-v" ]; then exit 0; fi
  exec "$@"
fi
if [ "$1" = "-S" ]; then
  IFS= read -r _password || true
  exit 0
fi
exit 1
EOF

cat > "$fake/lsblk" <<'EOF'
#!/bin/sh
cat <<'JSON'
{"blockdevices":[{"name":"sdb","type":"disk","size":"1T","model":"Fake Disk","serial":"FAKE1","rota":"0","fstype":null,"mountpoint":null,"pkname":null,"pttype":"gpt","tran":"sata","children":null}]}
JSON
EOF

cat > "$fake/smartctl" <<'EOF'
#!/bin/sh
echo "Temperature_Celsius: 30"
EOF

cat > "$fake/mdadm" <<'EOF'
#!/bin/sh
exit 0
EOF

for tool in wipefs sgdisk dd blockdev partprobe udevadm; do
  printf '#!/bin/sh\nexit 0\n' > "$fake/$tool"
done
chmod +x "$fake"/*

cd "$repo"
dotnet build RAID-Util.sln -c Release --no-incremental > "$tmp/build.log"
dll="bin/Release/net10.0/RAID-Util.dll"

dotnet "$dll" --help | grep -q -- '--cli'

menus="$(printf 'x\n9\n0\n' | PATH="$fake:$PATH" dotnet "$dll" --cli)"
grep -q 'Please enter a number between 0 and 5.' <<<"$menus"

deep_clean="$(printf '3\n1\n1\n3\n1\n0\n0\n' | PATH="$fake:$PATH" timeout 30 dotnet "$dll" --cli)"
grep -q 'Disk /dev/sdb has been thoroughly wiped and cleaned.' <<<"$deep_clean"

echo 'smoke-cli: OK'
