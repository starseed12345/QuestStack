# QuestStack

QuestStack automates the Quest 1 root, vulnerable boot-firmware downgrade, and CVE-2021-1931 bootloader unlock flow.

## Modes

Run without arguments for the interactive menu, or choose a mode explicitly:

```powershell
QuestStack.exe --full
QuestStack.exe --unlock-only
QuestStack.exe --unlock-only --abl C:\path\to\abl.img
```

`--full` runs the ADB, root, inactive-slot flash, verification, slot switch, and unlock flow.

`--unlock-only` skips ADB, rooting, flashing, slot changes, and automatic reboots. Use it when the headset is already running a supported vulnerable ABL and is in fastboot. Without `--abl`, QuestStack downloads the checked firmware bundle and uses its ABL image.

Prompts accept `Y` or `N` immediately. Enter is not required.

## Fastboot behavior

QuestStack does not automatically reboot into fastboot. In the full flow it reboots normally, pauses so the headset can finish starting, and then waits for manual fastboot entry.

The unlock implementation:

1. Reads the build number directly from the Quest fastboot USB interface.
2. Selects the matching Quest 1 patch offset.
3. Extracts LinuxLoader from the LZMA-compressed UEFI section in `abl.img`.
4. Validates the original instruction bytes before patching.
5. Checks every USB transfer and fastboot response.
6. Reports success only after fastboot returns `Device unlocked: true`.

## Requirements

- Windows x64, Linux x64, or macOS x64/ARM64
- Windows: a WinUSB/libusb-compatible driver for the Quest fastboot interface
- Linux: USB permissions for the Quest fastboot interface, normally through a udev rule
- A supported Quest 1 starting firmware for the full flow

Release builds are self-contained and include the .NET runtime, Android Platform-Tools ADB 37.0.1, and the matching native libusb library. No separate .NET, ADB, or libusb installation is required. Linux still uses the operating system's standard glibc and `libudev.so.1` interfaces.

Bootloader and partition changes can make a device unbootable. Keep the on-device backups and do not switch slots if flashing or verification fails.

## Validation

Run the offline regression checks with:

```powershell
QuestStack.exe --self-test
```

To also validate extraction against a real firmware bundle:

```powershell
QuestStack.exe --self-test --abl C:\path\to\bundle\images\abl.img
```

The self-test runs the bundled `adb version` command and calls into `libusb_get_version`, so it fails if either embedded dependency cannot load.

## Single-file releases

Publish each supported target with:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o artifacts\win-x64
dotnet publish -c Release -r linux-x64 --self-contained true -o artifacts\linux-x64
dotnet publish -c Release -r osx-x64 --self-contained true -o artifacts\osx-x64
dotnet publish -c Release -r osx-arm64 --self-contained true -o artifacts\osx-arm64
```

Each output directory contains one executable. Windows names it `QuestStack.exe`; Linux and macOS name it `QuestStack`.

The bundled libusb library is licensed under LGPL-2.1-or-later. Its source and license are available from the [libusb project](https://github.com/libusb/libusb).
