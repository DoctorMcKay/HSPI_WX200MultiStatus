﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HomeSeer.PluginSdk;
using HomeSeer.PluginSdk.Devices;
using HomeSeer.PluginSdk.Logging;
using HSPI_WX200MultiStatus.Enums;

namespace HSPI_WX200MultiStatus.DataStructures;

// ReSharper disable once InconsistentNaming
public class WX200Device {
	public readonly WX200DeviceType Type;
	public readonly int DevRef;
	public readonly string DeviceName;
	public readonly string HomeId;
	public readonly byte NodeId;
	public readonly List<string> Groups;
	private readonly HSPI _plugin;

	private bool _hasSyncedState = false;
	private readonly byte[] _statusLedStates;
	private bool _isInStatusMode;
	private byte _blinkMask;

	// ReSharper disable once InconsistentNaming
	private const int MFG_ID_HOMESEER_TECHNOLOGIES = 0x000c;

	public WX200Device(HSPI plugin, AbstractHsDevice hsDevice, string deviceName) {
		_plugin = plugin;
		Groups = new List<string>();

		Type = GetDeviceType(hsDevice);
		DevRef = hsDevice.Ref;
		DeviceName = deviceName;

		string[] address = hsDevice.Address.Split('-');
		HomeId = address[0];
		NodeId = byte.Parse(address[1]);

		_loadGroups();

		_statusLedStates = new byte[GetLedCount()];
	}

	private void _loadGroups() {
		Dictionary<string, string> section = _plugin.GetIniSection(_getIniSectionName());
		foreach (string groupName in section.Keys.Where(groupName => section[groupName] == "1")) {
			Groups.Add(groupName);
		}
	}

	public void SaveGroups() {
		string section = _getIniSectionName();
		_plugin.ClearIniSection(section);
		foreach (string groupName in Groups) {
			_plugin.SaveIniSetting(section, groupName, "1");
		}
	}

	public async void SetStatusLed(byte ledIndex, WX200StatusModeColor color, bool blink) {
		await SyncState();
		if (ledIndex >= GetLedCount()) {
			throw new Exception($"LED index {ledIndex} is out of range (max {GetLedCount()}");
		}

		if (color == WX200StatusModeColor.Off) {
			// If we're turning the LED off, force blink to false
			blink = false;
		}

		_statusLedStates[ledIndex] = (byte) color;
		if (blink) {
			_blinkMask |= (byte) (1 << ledIndex);
		} else {
			_blinkMask &= (byte) ~(1 << ledIndex);
		}

		_plugin.WriteLog(ELogType.Debug, $"Value for {HomeId}:{NodeId} LED {ledIndex} is now {_statusLedStates[ledIndex]} with blink mask {_blinkMask}");

		_plugin.ConfigSet(HomeId, NodeId, (byte) (WX200ConfigParam.StatusModeLed1Color + ledIndex), 1, _statusLedStates[ledIndex]);
		if (Type == WX200DeviceType.WS200) {
			_plugin.ConfigSet(HomeId, NodeId, (byte) WX200ConfigParam.WSStatusModeBlinkFrequency, 1, (blink ? 1 : 0) * _plugin.WsBlinkFrequency);
		} else {
			_plugin.ConfigSet(HomeId, NodeId, (byte) WX200ConfigParam.WDFCStatusModeBlinkBitmask, 1, _blinkMask);
		}

		_syncStatusMode();
	}

	private string _getIniSectionName() {
		return $"Groups_{HomeId}_{NodeId}";
	}

	public byte GetLedCount() {
		switch (Type) {
			case WX200DeviceType.WS200:
				return 1;

			//Both WD200 and WX300 have 7 lights
			case WX200DeviceType.WD200:
			case WX200DeviceType.WX300:
				return 7;

			case WX200DeviceType.FC200:
				return 4;

			default:
				throw new Exception("Unknown type");
		}
	}

	internal async Task SyncState() {
		if (_hasSyncedState) {
			return;
		}
			
		_plugin.WriteLog(ELogType.Info, $"Synchronizing device state for {HomeId}:{NodeId} ({DeviceName})");

		try {
			for (byte i = 0; i < GetLedCount(); i++) {
				_statusLedStates[i] = (byte) _plugin.ConfigGet(HomeId, NodeId, (byte) (WX200ConfigParam.StatusModeLed1Color + i));
			}

			_isInStatusMode = _plugin.ConfigGet(HomeId, NodeId, (byte) WX200ConfigParam.StatusModeActive) == 1;

			// Blink mask is not used for WS200+ so don't waste time retrieving it
			if (Type != WX200DeviceType.WS200) {
				_blinkMask = (byte) _plugin.ConfigGet(HomeId, NodeId, (byte) WX200ConfigParam.WDFCStatusModeBlinkBitmask);
			}
				
			_hasSyncedState = true;
		} catch (Exception ex) {
			_plugin.WriteLog(ELogType.Error, $"Failure while synchronizing device state for {HomeId}:{NodeId} ({DeviceName}): {ex.Message}");
			await Task.Delay(1000);
			await SyncState();
		}
	}

	private void _syncStatusMode() {
		bool shouldStatusMode = _statusLedStates.Any(val => val != (byte) WX200StatusModeColor.Off);
		if (shouldStatusMode != _isInStatusMode) {
			_plugin.ConfigSet(HomeId, NodeId, (byte) WX200ConfigParam.StatusModeActive, 1, shouldStatusMode ? 1 : 0);
			_isInStatusMode = shouldStatusMode;
		}
	}

	public static WX200DeviceType GetDeviceType(AbstractHsDevice hsDevice) {
		if (hsDevice.Interface != "Z-Wave") {
			throw new Exception("Provided device is not a Z-Wave device");
		}

		int? manufacturerId = hsDevice.PlugExtraData.GetNamed<int?>("manufacturer_id");
		ushort? prodType = hsDevice.PlugExtraData.GetNamed<ushort?>("manufacturer_prod_type");
		ushort? prodId = hsDevice.PlugExtraData.GetNamed<ushort?>("manufacturer_prod_id");

		if (manufacturerId != MFG_ID_HOMESEER_TECHNOLOGIES) {
			throw new Exception("Provided device is not a HS-WX200+");
		}

		if (prodType == 0x4447 && prodId == 0x3035) {
			return WX200DeviceType.WS200;
		}

		if (prodType == 0x4447 && prodId == 0x3036) {
			return WX200DeviceType.WD200;
		}

		// 0x4036 = WX300 in dimmer mode; 0x4037 = WX300 in binary switch mode
		if (prodType == 0x4447 && (prodId == 0x4036 || prodId == 0x4037)) {
			return WX200DeviceType.WX300;
		}

		if (prodType == 0x203 && prodId == 0x1) {
			return WX200DeviceType.FC200;
		}

		throw new Exception("Provided device is not a HS-WX200+");
	}

	// ReSharper disable once InconsistentNaming
	public static bool IsWX200Device(AbstractHsDevice hsDevice) {
		try {
			GetDeviceType(hsDevice);
			return true;
		} catch (Exception) {
			return false;
		}
	}
}