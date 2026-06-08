#!/bin/bash
set -e

echo "=== RAID-Util: Removing ALL mdadm arrays ==="

# 1. Stop all active arrays
echo "[1/4] Stopping all arrays..."
for md in /dev/md*; do
    if [[ -e "$md" ]]; then
        echo "Stopping $md"
        sudo mdadm --stop "$md" || true
    fi
done

# 2. Remove array devices
echo "[2/4] Removing md devices..."
for md in /dev/md*; do
    if [[ -e "$md" ]]; then
        echo "Removing $md"
        sudo mdadm --remove "$md" || true
    fi
done

# 3. Zero superblocks on all member disks
echo "[3/4] Zeroing superblocks..."
for disk in $(sudo mdadm --examine --scan | awk '{print $4}' | sed 's/device=//'); do
    if [[ -e "$disk" ]]; then
        echo "Zeroing superblock on $disk"
        sudo mdadm --zero-superblock "$disk" || true
    fi
done

# 4. Clean mdadm.conf
echo "[4/4] Cleaning /etc/mdadm.conf..."
if [[ -f /etc/mdadm.conf ]]; then
    sudo sed -i '/^ARRAY/d' /etc/mdadm.conf
    echo "mdadm.conf cleaned."
else
    echo "No mdadm.conf found."
fi

echo "=== DONE ==="
echo "All mdadm arrays removed."
