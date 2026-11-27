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
# Los diagnósticos de servicios deben ir al archivo de log, no a la CLI.
if grep -qE '\[(CMD|STATUS|RAID|AA|ICONHELPER|NOTIFICACIÓN)\]' <<<"$menus"; then
  echo 'smoke-cli: FAIL — la CLI imprimió logs de diagnóstico' >&2
  exit 1
fi

deep_clean="$(printf '3\n1\n1\n3\n1\n0\n0\n' | PATH="$fake:$PATH" timeout 30 dotnet "$dll" --cli)"
grep -q 'Disk /dev/sdb has been thoroughly wiped and cleaned.' <<<"$deep_clean"

# "0) Back" en el selector del nivel de logs debe cancelar TODA la pantalla
# de ajustes: no guardar, no aplicar y no crear el directorio escrito.
settings_home="$tmp/settings-home"
mkdir -p "$settings_home"
printf '0\n' | HOME="$settings_home" PATH="$fake:$PATH" timeout 30 dotnet "$dll" --cli >/dev/null
config_file="$settings_home/.config/raid-util/config.json"
cp "$config_file" "$config_file.before"
cancelled_logs="$tmp/cancelled-logs"
settings_out="$(printf '5\n%s\n0\n0\n' "$cancelled_logs" |
    HOME="$settings_home" PATH="$fake:$PATH" timeout 30 dotnet "$dll" --cli)"
grep -q 'Settings unchanged.' <<<"$settings_out"
if grep -q 'Settings saved.' <<<"$settings_out"; then
  echo 'smoke-cli: FAIL — cancelar ajustes guardó la configuración' >&2
  exit 1
fi
if ! cmp -s "$config_file" "$config_file.before"; then
  echo 'smoke-cli: FAIL — cancelar ajustes modificó config.json' >&2
  exit 1
fi
if [[ -e "$cancelled_logs" ]]; then
  echo 'smoke-cli: FAIL — cancelar ajustes creó el directorio de logs' >&2
  exit 1
fi

echo 'smoke-cli: OK'
