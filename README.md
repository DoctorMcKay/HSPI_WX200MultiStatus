# HS-WX200/FC200 Multi-Status

The RGB status LED features in HomeSeer HS-WX200/FC200 wall switches are very convenient,
but if you have many of those switches installed in your home, it can be a pain to display
various statuses across all of them using HomeSeer's native event system. It is natively
possible to manipulate a status LED on all switches at once, but it's impossible to get
any more granular without controlling devices one-by-one, which can get tedious.

This plugin solves that problem. You can add your various devices to groups and control
entire groups all at once.

# Installation

This plugin is **not yet** available in the HomeSeer plugin updater.

# Configuration

Once the plugin is installed and running, the settings page can be accessed via
Plugins > WX200 Multi-Status > Settings. The only setting you can configure is the blink
frequency for HS-WS200+ wall switches, since controlling that via a device's Z-Wave
Configuration is not possible.

To add or remove devices from groups, click on a device or feature for a WX200/FC200 switch.
Select the "WX200 Multi-Status" tab, and from that page you can control the device's groups.
To add the device to a group, simply type the name of your desired group (case-sensitive)
into the "Add To Group" box, then click Save. To remove a device from a group, select the
desired group from the "Remove From Group" menu, then click Save.

Groups are automatically created when the first device is added to them, and automatically
removed when the last device is removed.

# Controlling Status LEDs

Status LEDs are controlled through events. Create an event, select your desired trigger(s),
and then create an action and select "Z-Wave: Set HS-WX200+ Status Mode LED". Select your
desired device or group, the LED to control, the color for the LED, and whether the LED
should blink.

**Please note:** The plugin maintains an internal device state, and therefore you should
only control status LEDs using the aforementioned event action.
**Do not control your status LEDs using the built-in Z-Wave Actions > HS-WX200/HS-FC200 LED Actions action.**
Doing so may result in unexpected behavior. You may still use that action to control normal mode
LED colors if you wish.

Due to a bug in HS4, you may be unable to save a fully filled-out action. If this happens,
you probably just need to re-select your desired value in the bottom-most dropdown menu.

## Switch Model Details

Obviously, the different HS-WX/FC switch models feature differing numbers of LEDs. If a group
contains multiple different switch models and you attempt to manipulate an LED that does not
exist on all switches in that group, those switches which don't feature the LED you selected
will be skipped.

For example, if a group contains at least one of each WS, WD, and FC and you change LED #5, then
only your WD switches will be affected. If you change LED #3, then only your WD and FC switches
will be affected. If you change LED #1, then the bottom-most LED (the only LED on WS switches)
will be affected. If you choose to change all LEDs, then every LED on each switch in the group
will be affected.
