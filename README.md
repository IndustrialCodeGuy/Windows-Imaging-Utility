# Windows Imaging Utility

Windows Imaging Utility is a standalone Windows application for disk imaging, Windows image deployment, and offline image servicing. It provides a graphical interface for common WIM and FFU workflows while using Windows' built-in deployment and storage tools underneath.

The application is designed for use on Windows.

## Features

### Disk and partition imaging

- Capture physical disks to FFU images.
- Apply FFU images to physical disks.
- Capture partitions to WIM images.
- Apply WIM images to partitions.
- Deploy a WIM to a disk with partitioning, boot-file configuration, and WinRE staging.
- Inspect physical disks, partitions, volumes, and BitLocker state.
- Automatically refresh storage inventory when devices or BitLocker state change.

### WIM management

- View image information.
- Mount WIM images.
- Commit or discard mounted-image changes.
- Remount images when possible.
- Clean up stale mount state.
- Export WIM images.
- Delete individual WIM images.
- Split WIM files.
- Add drivers to mounted images.

### Windows integration

- Uses the modern Windows Open, Save, and folder-selection dialogs.
- Uses the native Windows BitLocker unlock interface for locked volumes.
- Opens the Windows BitLocker Drive Encryption interface for BitLocker management.
- Uses the Windows Storage Management Provider (`MSFT_Disk` and `MSFT_Partition`) for disk and partition inventory.
- Uses Windows-provided DISM, DiskPart, BCDBoot, and ReAgentC for imaging and deployment operations.

## Safety behavior

Windows Imaging Utility is capable of destructive disk operations. Review the selected source, target, disk number, partition, and image before confirming an operation.

When running in Windows, the utility protects the currently running Windows installation from operations that would overwrite or repartition it. In particular, FFU apply/capture and whole-disk WIM deployment are blocked against the live Windows disk, and WIM capture/apply is blocked against the running Windows partition where appropriate.

The utility does not move or replace the host system's `C:` drive assignment. Temporary drive letters used during deployment are released after the operation completes.

Microsoft Reserved (MSR) partitions remain part of the internal disk inventory and are shown in detailed disk information, but they are intentionally hidden from the normal selectable partition strip because they are not useful file-based WIM targets.

## Requirements

- 64-bit Windows.
- Administrator rights.
- Windows Storage Management Provider support.
- Built-in Windows deployment tools used by the selected operation, including DISM, DiskPart, BCDBoot, and ReAgentC where applicable.

The application targets .NET 8 for Windows and is configured to publish as a self-contained, single-file x64 executable, so the .NET Desktop Runtime does not need to be installed separately on the target machine.

## Building

Open the standalone solution:

```text
Windows.Imaging.Utility.sln
```

Build a Release configuration from the command line:

```powershell
dotnet build .\Windows.Imaging.Utility.sln -c Release
```

Publish the standalone x64 executable:

```powershell
dotnet publish .\Windows.Imaging.Utility\Windows.Imaging.Utility.csproj -p:PublishProfile=FolderProfile
```

The published executable is:

```text
WindowsImagingUtility.exe
```

## Project structure

```text
Windows-Imaging-Utility/
├── Imaging.Core/              Imaging, disk inventory, BitLocker status, and deployment logic
├── Windows.Imaging.Utility/   Windows Forms user interface and Windows integration
├── Windows.Imaging.Utility.sln
├── LICENSE
└── README.md
```

`Imaging.Core` contains the imaging and storage logic. `Windows.Imaging.Utility` contains the Windows Forms interface and application-specific Windows integration.

## Implementation notes

- Disk and partition inventory is based on `MSFT_Disk` and `MSFT_Partition` rather than legacy Win32 disk enumeration.
- Read-only BitLocker status is obtained through `Win32_EncryptableVolume`.
- The native Windows `unlock-bde` shell verb is used to open the BitLocker unlock interface.
- Long-running imaging operations request that Windows remain awake while work is active without changing the user's selected power plan.
- Deploy WIM currently chooses GPT/UEFI or MBR/BIOS layout based on the firmware mode of the computer running Windows Imaging Utility.

## License

Copyright (C) 2026 Dan Michel

Windows Imaging Utility is licensed under the **GNU General Public License version 3 only** (`GPL-3.0-only`). Commercial use is permitted. Distribution of modified or derivative versions must comply with GPLv3, including its corresponding-source and licensing requirements.

See [LICENSE](LICENSE) for the complete license terms.

SPDX-License-Identifier: GPL-3.0-only
