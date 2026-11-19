# 🖨️ Ricoh MFP Self-Service Setup Tool (1-Click Enterprise Installer)

An enterprise-ready, standalone Windows desktop application developed in native C# (.NET Framework) that enables office staff and clients to install, configure, and manage Ricoh Multifunction Printers (MFPs) in **1 click**—without requiring IT staff to remote in, and without touching PowerShell or command-line interfaces.

---

## 💼 Executive Summary & Value Proposition (For Management & HOD)

| Metric | Traditional Method | With Ricoh Setup Tool |
|---|---|---|
| **Deployment Time** | 15 – 30 mins per PC | **< 10 seconds (1 Click)** |
| **IT Staff Involvement** | Required (Remote desktop / manual visit) | **Zero (Self-service by staff)** |
| **Prerequisites / Runtimes** | Often requires runtimes or admin tools | **Zero dependencies (Native Windows 10 & 11)** |
| **Network Dependency** | Must be online on LAN | **Supports 100% Offline Pre-Deployment** |
| **Driver Scalability** | Manual driver download per machine | **Dynamic auto-discovery of 110+ Ricoh models** |

### Key Business Benefits:
1. **Reduces IT Support Tickets:** Non-technical staff can install their own office printer queue immediately without waiting for an IT technician.
2. **Accelerates Client Onboarding:** For MFP sales and rental deployments, technicians can pre-configure client laptops and PCs with static IP ports before the client's router/LAN is even installed.
3. **Enterprise Compliance & Security:** Built with an embedded Windows UAC Administrator manifest (`requireAdministrator`) and utilizes documented Windows DriverStore and Print Spooler APIs (`pnputil`, native TCP/IP spooler ports).

---

## ✨ Features Overview

### 1. 📥 1-Click Self-Service Installation
* Automatically stages official Ricoh PCL 6 drivers into the Windows DriverStore.
* Creates the Standard TCP/IP Printer Port (e.g. `IP_192.168.23.160`).
* Registers the printer queue and enables immediate test page printing.

### 2. 🟢 Real-Time Network & PC Spooler Status
* Background ping engine continuously checks if printers are **Online** or **Offline / Asleep**.
* Automatically detects whether a machine is already **Installed** on the local PC.

### 3. 🔍 Fast Subnet Auto-Discovery (Port 9100 & Web Image Monitor)
* Scans the office subnet (`1-254`) in under 3 seconds.
* Queries PJL `@PJL INFO ID` and Ricoh's embedded Web Image Monitor to identify the exact printer model (e.g. `MP C3503`, `MP C4503`, `MP C5503`, `IM C2000`, `IM C6000`).

### 4. ➕ Offline Pre-Configuration (`Add Manually`)
* Allows technicians to pre-install print queues and drivers for machines that have not yet been connected to the client's network.

### 5. 📂 Dynamic Driver Auto-Discovery
* The software dynamically scans the `drivers/` folder on startup.
* Dropping any new Ricoh driver folder into `drivers/` instantly registers all supported models into the application without recompiling.

### 6. 🗑️ Clean Client Management
* Allows technicians to remove previous client printers with 1 click when preparing setup packages for a new company.

---

## 🗂️ Project Structure

```text
RicohPrinterInstaller/
├── RicohPrinterSetup.exe      # Compiled standalone executable (32 KB)
├── Program.cs                 # C# Windows Forms source code
├── app.manifest               # Windows UAC elevation manifest
├── printers.json              # Client printer configuration file
├── build.bat                  # 1-Click native C# compiler script
├── drivers/                   # Ricoh official PCL 6 driver repository
│   ├── IM C2000-C2500/        # (Supports IM C2000 through IM C6000)
│   ├── MP C2003-C2503/        # (Supports MP C2003 through MP C2503)
│   ├── MP C2004-C2504/        # (Supports MP C2004 through MP C2504)
│   ├── MP C2004ex-C2504ex/    # (Supports MP C2004ex through MP C6004ex)
│   ├── MP C2800-C3300/        # (Supports MP C2800 and MP C3300)
│   └── MP C3003-C3503/        # (Supports MP C3003 through MP C6003)
└── README.md                  # Documentation & Executive Summary
```

---

## 🚀 How to Deploy in Office / Client Sites

### Option 1: Central Shared Network Folder (Recommended)
Place the `RicohPrinterInstaller` directory on your office file server:
```
\\YourServer\Shared\RicohPrinterSetup\RicohPrinterSetup.exe
```
Staff can simply double-click the executable directly from the network share.

### Option 2: USB Drive / Local Distribution
Copy the folder to a USB thumb drive or zip it and send it to staff workstations.

---

## 🛠️ Building from Source

No Visual Studio installation is required. The project compiles using the built-in Microsoft .NET Framework compiler included with Windows:

```cmd
build.bat
```

---

## 📄 License & Attribution
Developed for Enterprise IT Workstation Deployment and Ricoh MFP Rental & Sales Management.
All printer drivers and model trademarks belong to Ricoh Company, Ltd.
