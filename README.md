# QuestStack

## Current Details

Expected Release ETA 7-21 Days

# Known Limitations

The exploit is currently lottery-based therefore it can succeed in either a minute to 10 hours (about 1 in 450 boots)
We are still actively researching a way to reduce this number but unfortunately due to the old kernel of the Q1 this isn't as easy.

## Before we start

Supported:
✅ Quest 1
✅ Firmware: 49845030443200410 (Latest)

Not supported:
❌ Quest 2
❌ Quest Pro
❌ Any other headset

QuestStack is a Meta Quest 1 root and bootloader unlocking project.

Using the GhostLock privilege escalation chain and the CVE-2021-1931 ABL/fastboot vulnerability, QuestStack provides a method to gain root access on the latest Quest 1 firmware, replace the stock ABL image with a vulnerable version, and unlock the device bootloader.

The project combines Quest firmware research, root access, ABL modification, and fastboot unlocking into a single process aimed at giving developers and enthusiasts full control over Quest 1 hardware.

## Instructions

Check your current installed firmware version:

adb shell getprop ro.build.version.incremental

The required firmware version is:

49845030443200410

If your device is not on this firmware version, reboot into "USB Update Mode" and sideload the required firmware package. -> https://files.cocaine.trade/firmware/meta/Quest/q1_49845030443200410.zip

After installing the firmware:

1. Boot the headset normally.
2. Ensure ADB is enabled and working.
3. Run QuestStack.
4. Wait for the process to complete.

QuestStack will perform the required steps to gain root access, replace the ABL image, and continue the bootloader unlocking process.

## Support

If you're looking for help consider joining this server https://discord.gg/6JSH88u2Rd

## Credits

Built using research and tools from the Android security and VR development communities specifically (FreeXR), including previous Quest bootloader unlocking work.

## Disclaimer

This project is provided for educational and research purposes. Modifying bootloader state or system software can permanently affect your device. Use at your own risk.
