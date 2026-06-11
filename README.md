# RAID‑Util

RAID -> (Redundant Array of Independent Disks)

RAID‑Util is a graphical application designed to simplify the management, monitoring, and maintenance of Linux software RAID arrays (`mdadm`).  
It provides a clean, modern interface that allows administrators to work with RAID arrays, JBOD groups, and individual disks without relying on complex terminal commands.

The goal of the project is to offer a **safe, intuitive, and reliable** utility suitable for homelabs, servers, and advanced desktop users who want full control over their storage without risking system integrity.

---

## Purpose

Managing RAID arrays with `mdadm` can be powerful but also error‑prone, especially when dealing with multiple disks, degraded arrays, metadata cleanup, or system‑level operations.  
RAID‑Util presents these capabilities through a structured interface that emphasizes **clarity, safety, and consistency**, helping users avoid destructive mistakes and streamline their workflow.

---

## Key Features

### RAID Array Management
- Create, assemble, stop, and delete RAID arrays  
- Support for RAID levels: **0, 1, 4, 5, 6, 10, Linear, JBOD**  
- Add, remove, re‑add, and repair disks  
- Trigger resync, reshape, and consistency checks  
- Real‑time monitoring of array state, progress, and events  
- Safe handling of degraded or recovering arrays  


### Disk Management
- View all disks grouped by array  
- Initialize, wipe, or clean RAID metadata  
- View SMART data (temperature, health, attributes)  
- Detect system disks and protect them from destructive actions  

### System Integration
- Universal detection of `mdadm` path  
- All operations executed through controlled `sudo` helpers  
- Pre‑UI system checks to ensure safe startup  
- Automatic updates to `mdadm.conf` when arrays change  
- Optional initramfs regeneration when needed  

### Modern Interface
- Built with Avalonia for a consistent cross‑platform experience  
- Clean, organized layout with dedicated tabs: **Status**,**Arrays RAID**, **Disks**, **Logs**  
- Real‑time progress indicators for RAID operations  
- Unified HDD icon theme for all disk types  
- Designed for readability and operational safety  

---

## Installation

Pre‑built packages and AppImage builds are available on the project’s GitHub page:

**https://github.com/mijocecr/raid-util/releases**

AUR (Arch Linux / Manjaro / EndeavourOS)
RAID‑Util is available in the Arch User Repository.

Install with yay:
```bash
yay -S raid-util-bin
```
Install with paru:
```bash
paru -S raid-util-bin
```
Manual AUR clone:

```bash
git clone https://aur.archlinux.org/raid-util-bin.git
cd raid-util-bin
makepkg -si
```

---

## Running RAID‑Util

### From source:
```bash
git clone https://github.com/mijocecr/raid-util.git
cd raid-util
dotnet run
```

---

## Requirements

To function correctly, RAID‑Util requires:

- A Linux system with `mdadm` installed  
- `systemd` for managing RAID‑related services  
- `smartmontools` for SMART disk monitoring  
- Administrative privileges for operations such as creating arrays, wiping metadata, or managing disks  
- Access to `/etc/mdadm/mdadm.conf` or `/etc/mdadm.conf` depending on the distribution  

---

## Usage Overview

RAID‑Util is organized into several sections, each focused on a specific aspect of RAID and disk management:

- **Status** — Review system‑level information, RAID service state, and overall health.
- **Arrays RAID** — Create, assemble, stop, delete, and repair RAID arrays.  
- **Disks** — Inspect disks, view SMART data, wipe RAID metadata, mount/unmount partitions, and safely eject USB devices.    
- **Logs** — Inspect relevant system messages and RAID events in real time.

Each section is designed with clear labels, structured layouts, and informative messages to guide the user through safe and predictable workflows.

---

## Contributing

Contributions, suggestions, and issue reports are welcome.  
Please use the GitHub issue tracker or submit a pull request to participate in the project’s development.

---

## License

RAID‑Util is distributed under the **MIT License**, allowing free use, modification, and distribution.

---

## Project Status

RAID‑Util is stable, safe, and suitable for production use in home servers, homelabs, and small office environments.  
It focuses on reliability and does not modify any RAID configuration outside the user’s explicit actions.

---

<img width="1536" height="1024" alt="raid-util-poster" src="https://github.com/user-attachments/assets/d7b84f4c-3b78-43f4-9d83-57ffc7e6b880" />

---


