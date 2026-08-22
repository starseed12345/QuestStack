# QuestStack

QuestStack is a Meta Quest 1 root and bootloader unlocking project.

Using the GhostLock privilege escalation chain and the CVE-2021-1931 ABL/fastboot vulnerability, QuestStack provides a method to gain root access on the latest Quest 1 firmware, replace the stock ABL image with a vulnerable version, and unlock the device bootloader.

The project combines Quest firmware research, root access, ABL modification, and fastboot unlocking into a single process aimed at giving developers and enthusiasts full control over Quest 1 hardware.

## Current Details

> Status: Released

## Before we start

### Supported:

* ✅ Quest 1
* ✅ Firmware: 49845030443200410 (Latest)

### Not supported:

* ❌ Quest 2
* ❌ Quest Pro
* ❌ Any other headset

## Website Version

If you prefer to do this with the website instead of app use -> https://quest1-unlock.skystate.ch/

Credits: Darknight

## Instructions

Check your current installed firmware version:

```bash
adb shell getprop ro.build.version.incremental
```

The required firmware version is:

```text
49845030443200410
```

If your device is not on this firmware version, reboot into "USB Update Mode" and sideload the required firmware package.

https://files.cocaine.trade/firmware/meta/Quest/q1_49845030443200410.zip

After installing the firmware:

1. Boot the headset normally.
2. Ensure ADB is enabled and working.
3. Run QuestStack.
4. Wait for the process to complete.
5. Once completed sideload your preferred update as the current slot is NOT bootable.

QuestStack will perform the required steps to gain root access, replace the ABL image, and continue the bootloader unlocking process.

## Support

If you're looking for help consider joining this server

https://discord.gg/6JSH88u2Rd

## Speculations

Quest 2 Devices running V59 or lower have a similar chance of having this work as intended to unlock the bootloader but the chance of bricking outweighs the benefit of unlocking the bootloader.

## Credits

Built using research and tools from the Android security and VR development communities specifically (FreeXR), including previous Quest bootloader unlocking work from darknight https://github.com/darknight1050/quest-bootloader-unlocker.

## License

QuestStack is licensed under the GNU General Public License v3.0. See [LICENSE](LICENSE) for details.

## Disclaimer

> This project is provided for educational and research purposes. Modifying bootloader state or system software can permanently affect your device. Use at your own risk.
