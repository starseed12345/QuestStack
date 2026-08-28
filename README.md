# QuestStack

QuestStack is a Meta Quest root and bootloader unlocking project.

Using the GhostLock privilege escalation chain and the CVE-2021-1931 ABL/fastboot vulnerability, QuestStack provides a way to gain root access on the latest Quest 1 firmware, temporarily boot using a vulnerable ABL, and unlock the device bootloader.

The project combines Quest firmware research, root access, A/B slot manipulation, ABL modification, fastboot exploitation, backups, and verification into one automated workflow aimed at giving developers and enthusiasts full control over Quest 1 hardware.

## Current Details

> **Status: Released**
>
> **90 HZ Possible. Check the Discord below**

QuestStack is currently available as a desktop application for Windows, Linux, and macOS.

The current version includes a full graphical interface, automatic device detection, verified backups, SHA-256 integrity checks, automatic slot handling, and recovery logic.

## Before We Start

### Supported

* ✅ Quest 1
* ✅ Firmware: `49845030443200410` (Latest)
* ✅ Quest 2 (Website version Only)
* ✅ Firmware: `50670960048600150`

### Not Supported

* ❌ Quest Pro
* ❌ Any other headset

QuestStack checks the connected device before allowing the full process to start.

The firmware build, Quest product information, fingerprint, and A/B slot state are checked automatically to reduce the chance of running the process on the wrong device.

## Website Version

If you prefer to do this with the website instead of the app, use:

https://quest1-unlock.skystate.ch/

Credits: Darknight

The website version is a separate implementation and does not necessarily provide the same workflow, backup system, or recovery features as the desktop version.

## Requirements

If your device is not on the required firmware version, reboot into **USB Update Mode** and sideload the required firmware package:

https://files.cocaine.trade/firmware/meta/Quest/q1_49845030443200410.zip

https://files.cocaine.trade/firmware/meta/Quest%202/q2_50670960048600150.zip

After installing the firmware, boot the headset normally and make sure ADB / USB debugging is enabled.

## Instructions

1. Boot the Quest normally.
2. Connect it to your computer using USB.
3. Allow USB debugging if prompted.
4. Launch QuestStack.
5. Wait for the headset to be detected.
6. Make sure QuestStack reports the device as supported.
7. Select **Full Process**.
8. Press **Start**.
9. Read the warning before continuing.
10. Keep the headset connected until QuestStack finishes.

The full process will factory reset the headset.

QuestStack handles the rest of the process automatically.

## What QuestStack Does

During the full process, QuestStack will:

1. Detect the connected headset.
2. Verify that it is a supported Quest.
3. Verify the required firmware and device fingerprint.
4. Detect the currently active A/B slot.
5. Download and verify the required files.
6. Gain temporary root access using GhostLock / ionstack.
7. Create a verified backup of the inactive slot.
8. Push the required vulnerable firmware files to the headset.
9. Flash the vulnerable boot firmware to the inactive slot.
10. Read the flashed partitions back and verify them.
11. Switch to the vulnerable slot.
12. Reboot directly into fastboot.
13. Perform the CVE-2021-1931 bootloader unlock.
14. Verify that the bootloader is actually unlocked.
15. Restore the original slot.
16. Factory reset the headset.
17. Reboot back into Android.

If something fails before the vulnerable slot is activated, QuestStack stops without switching to it.

If something fails after the vulnerable slot has been activated, QuestStack attempts to restore the original slot automatically.

## Backups

Before modifying the inactive slot, QuestStack creates a persistent backup of the affected partitions on your computer.

Backups include information such as:

* Device serial
* Device model
* Firmware fingerprint
* Slot information
* Partition sizes
* SHA-256 hashes

QuestStack compares the SHA-256 hash of each copied partition against the original block device before marking the backup as valid.

Incomplete backups are not treated as valid backups.

Existing verified backups can be viewed from the **Backups** tab inside QuestStack.

## SHA-256 Verification

QuestStack verifies important files throughout the entire process.

SHA-256 verification is currently used for:

* Firmware archive
* Firmware images
* ionstack root exploit
* Bundled ADB and fastboot tools
* Files pushed to the headset
* Partition backups
* Flashed partitions

If a required file does not match its expected hash, QuestStack will stop instead of continuing.

## A/B Slot Safety

QuestStack performs the downgrade on the **inactive slot**.

The currently booted Android slot is left untouched during the downgrade process.

Before switching slots, QuestStack verifies that the vulnerable firmware was actually written correctly.

After the bootloader has been unlocked, QuestStack switches back to the original slot before wiping and rebooting the headset.

This means the vulnerable slot is only used temporarily for the unlock process.

## Unlock Only Mode

QuestStack also includes an **Unlock Only** mode.

This mode is intended for a headset that is already running the vulnerable ABL and is already in fastboot.

Unlock Only mode does **not**:

* Gain root
* Flash firmware
* Modify the active slot
* Create the normal downgrade backup
* Factory reset the headset
* Automatically reboot the headset

Use this mode only if you already know the headset is running the correct vulnerable ABL.

## Recovery

QuestStack contains automatic recovery logic for failures that happen after the vulnerable slot has been activated.

If possible, the program will switch the headset back to the original slot automatically.

If automatic slot recovery fails, do not continue booting the vulnerable slot blindly.

Keep the headset connected, preserve the backup created by QuestStack, and check the logs before doing anything else.

For help, use the Discord server linked below.

## Support

If you're looking for help consider joining this server:

https://discord.gg/6JSH88u2Rd

You can also find discussion about Quest modifications, 90 Hz, recovery, and other experiments there.

## Speculations

Quest 2 Devices running V59 or lower have a similar chance of having this work as intended to unlock the bootloader but the chance of bricking outweighs the benefit of unlocking the bootloader, This remains a future goal and will not be ignored

## Credits

Built using research and tools from the Android security and VR development communities specifically (FreeXR), including previous Quest bootloader unlocking work from darknight:

https://github.com/darknight1050/quest-bootloader-unlocker

Additional credit goes to the researchers and developers behind GhostLock, ionstack, and the other work that made modern Quest root and bootloader unlocking possible.

## License

QuestStack is licensed under the GNU General Public License v3.0.

See [LICENSE](LICENSE) for details.

## Disclaimer

> This project is provided for educational and research purposes.
>
> Modifying bootloader state, flashing boot firmware, or changing system software can permanently affect your device.
>
> QuestStack includes backups, verification, and recovery checks to reduce risk, but there is still a possibility of data loss or an unbootable device.
>
> Use at your own risk.
