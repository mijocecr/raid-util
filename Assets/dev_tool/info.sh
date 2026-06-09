#!/bin/bash

echo "====================================="
echo " RAID-UTIL STATUS (CLI VERSION)"
echo "====================================="

# -------------------------------
# RAID ARRAYS
# -------------------------------
echo
echo "[RAID] Arrays detectados:"
cat /proc/mdstat | grep -E "^md| active| blocks" || echo "No RAID detected"

arrays=$(grep -E "^md" /proc/mdstat | awk '{print $1}')

if [ -z "$arrays" ]; then
    echo
    echo "[RAID] No hay arrays RAID."
else
    echo
    echo "[RAID] Detalles de arrays:"
    for a in $arrays; do
        echo
        echo "---- /dev/$a ----"
        sudo mdadm --detail /dev/$a 2>&1
    done
fi

# -------------------------------
# RAID HEALTH (REAL)
# -------------------------------
echo
echo "[RAID] Estado general:"
health="Healthy"

for a in $arrays; do
    detail=$(sudo mdadm --detail /dev/$a 2>/dev/null)

    d=$(echo "$detail" | tr 'A-Z' 'a-z')

    if echo "$d" | grep -q "degraded"; then
        health="Critical"
    fi

    if echo "$d" | grep -q "faulty"; then
        health="Critical"
    fi

    # Rebuild REAL (no confundir con "Consistency Policy : resync")
    if echo "$d" | grep -qE "resync =|rebuild =|finish=|speed="; then
        health="Warning"
    fi
done

echo "Estado: $health"

# -------------------------------
# REBUILD STATUS (REAL)
# -------------------------------
echo
echo "[RAID] Rebuild:"
found=0
for a in $arrays; do
    detail=$(sudo mdadm --detail /dev/$a 2>/dev/null)
    line=$(echo "$detail" | grep -Ei "resync =|rebuild =|finish=|speed=")

    if [ ! -z "$line" ]; then
        echo "/dev/$a → $line"
        found=1
    fi
done

if [ $found -eq 0 ]; then
    echo "No rebuild en progreso"
fi

# -------------------------------
# ARRAYS AT RISK
# -------------------------------
echo
echo "[RAID] Arrays en riesgo:"
risk=0
for a in $arrays; do
    detail=$(sudo mdadm --detail /dev/$a 2>/dev/null)
    d=$(echo "$detail" | tr 'A-Z' 'a-z')

    if echo "$d" | grep -q "degraded"; then
        echo "/dev/$a → DEGRADED"
        risk=1
    fi

    if echo "$d" | grep -q "faulty"; then
        echo "/dev/$a → FAILED DISK"
        risk=1
    fi
done

if [ $risk -eq 0 ]; then
    echo "Ningún array en riesgo"
fi

# -------------------------------
# DISK ALERTS
# -------------------------------
echo
echo "[RAID] Alertas de discos:"
alerts=0
for a in $arrays; do
    detail=$(sudo mdadm --detail /dev/$a 2>/dev/null)
    d=$(echo "$detail" | tr 'A-Z' 'a-z')

    if echo "$d" | grep -q "faulty"; then
        echo "/dev/$a → Faulty disk detected"
        alerts=1
    fi
done

if [ $alerts -eq 0 ]; then
    echo "Sin alertas"
fi

# -------------------------------
# RECENT EVENTS (REAL)
# -------------------------------
echo
echo "[RAID] Eventos recientes:"
events=0
for a in $arrays; do
    detail=$(sudo mdadm --detail /dev/$a 2>/dev/null)
    d=$(echo "$detail" | tr 'A-Z' 'a-z')

    if echo "$d" | grep -q "faulty"; then
        echo "/dev/$a → Faulty disk detected"
        events=1
    fi

    if echo "$d" | grep -q "degraded"; then
        echo "/dev/$a → Array degraded"
        events=1
    fi

    if echo "$d" | grep -qE "resync =|rebuild =|finish=|speed="; then
        echo "/dev/$a → Rebuild in progress"
        events=1
    fi
done

if [ $events -eq 0 ]; then
    echo "Sin eventos recientes"
fi

# -------------------------------
# SYSTEM INFO
# -------------------------------
echo
echo "[SYSTEM] Información del sistema:"

echo "Uptime sesión: $(uptime -p)"

CPU=$(grep 'cpu ' /proc/stat | awk '{usage=($2+$3+$4)*100/($2+$3+$4+$5)} END {print int(usage)}')
echo "CPU: ${CPU}%"

RAM=$(free | awk '/Mem:/ {printf "%d", ($3*100/$2)}')
echo "RAM: ${RAM}%"

DISK=$(df -h / | awk 'NR==2 {print $4}')
echo "Disk Free: ${DISK}"

echo
echo "====================================="
echo " FIN DEL STATUS"
echo "====================================="
